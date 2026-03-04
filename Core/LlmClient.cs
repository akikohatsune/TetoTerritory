using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TetoTerritory.CSharp.Core;

public sealed class LlmClient : IDisposable
{
    private const string OverloadFallbackMessage = "i overload!";
    private const int MaxUpstreamAttempts = 4;
    private const int MaxGroqUpstreamAttempts = 2;
    private const int BaseRetryDelayMs = 750;
    private const int MaxRetryDelayMs = 15_000;
    private const int MaxErrorSnippetChars = 400;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Settings _settings;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public LlmClient(Settings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(90),
        };
    }

    public Task<string> GenerateAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        return GenerateAsync(messages, _settings.MainPersonaProfile, cancellationToken);
    }

    public async Task<string> GenerateAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmRuntimeProfile profile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var latestUserText = FindLatestUserText(messages);
            var systemPrompt = SystemPromptFactory.Build(profile.SystemPrompt, latestUserText);

            var primaryTarget = BuildProviderTarget(profile, profile.Provider);
            return await CallProviderAsync(
                messages,
                systemPrompt,
                primaryTarget,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTimeOffset.UtcNow:O}] [llm] provider={profile.Provider} persona={profile.PersonaKey} generate failed: {ex}");
            return OverloadFallbackMessage;
        }
    }

    public async Task<bool> ApproveCallNameAsync(string fieldName, string value, CancellationToken cancellationToken = default)
    {
        var response = await ApproveCallNameWithGeminiAsync(fieldName, value, cancellationToken);
        var verdict = NormalizeYesNo(response);
        return verdict == "yes";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private async Task<string> CallProviderAsync(
        IReadOnlyList<ChatMessage> messages,
        string systemPrompt,
        ProviderTarget target,
        CancellationToken cancellationToken)
    {
        return target.Provider switch
        {
            "gemini" => await CallGeminiAsync(
                messages,
                systemPrompt,
                target.ApiKey,
                target.Model,
                cancellationToken),
            "groq" => await CallGroqAsync(
                messages,
                systemPrompt,
                target.ApiKey,
                target.Model,
                cancellationToken),
            "openai" => await CallOpenAiAsync(
                messages,
                systemPrompt,
                target.ApiKey,
                target.Model,
                cancellationToken),
            "openrouter" => await CallOpenRouterAsync(
                messages,
                systemPrompt,
                target.ApiKey,
                target.Model,
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported provider: {target.Provider}"),
        };
    }

    private static ProviderTarget BuildProviderTarget(LlmRuntimeProfile profile, string provider)
    {
        return provider switch
        {
            "gemini" => new ProviderTarget(
                Provider: "gemini",
                Model: profile.GeminiModel,
                ApiKey: profile.GeminiApiKey),
            "groq" => new ProviderTarget(
                Provider: "groq",
                Model: profile.GroqModel,
                ApiKey: profile.GroqApiKey),
            "openai" => new ProviderTarget(
                Provider: "openai",
                Model: profile.OpenAiModel,
                ApiKey: profile.OpenAiApiKey),
            "openrouter" => new ProviderTarget(
                Provider: "openrouter",
                Model: profile.OpenRouterModel,
                ApiKey: profile.OpenRouterApiKey),
            _ => throw new InvalidOperationException($"Unsupported provider: {provider}"),
        };
    }

    private async Task<string> CallOpenAiAsync(
        IReadOnlyList<ChatMessage> messages,
        string systemPrompt,
        string? apiKey,
        string model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Missing OPENAI_API_KEY for selected persona.");
        }

        return await CallOpenAiCompatibleChatAsync(
            endpoint: "https://api.openai.com/v1/chat/completions",
            apiKey: apiKey,
            model: model,
            systemPrompt: systemPrompt,
            messages: messages,
            maxAttempts: MaxUpstreamAttempts,
            cancellationToken: cancellationToken);
    }

    private async Task<string> CallGroqAsync(
        IReadOnlyList<ChatMessage> messages,
        string systemPrompt,
        string? apiKey,
        string model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Missing GROQ_API_KEY for selected persona.");
        }

        return await CallOpenAiCompatibleChatAsync(
            endpoint: "https://api.groq.com/openai/v1/chat/completions",
            apiKey: apiKey,
            model: model,
            systemPrompt: systemPrompt,
            messages: messages,
            maxAttempts: MaxGroqUpstreamAttempts,
            cancellationToken: cancellationToken);
    }

    private async Task<string> CallOpenRouterAsync(
        IReadOnlyList<ChatMessage> messages,
        string systemPrompt,
        string? apiKey,
        string model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Missing OPENROUTER_API_KEY for selected persona.");
        }

        return await CallOpenAiCompatibleChatAsync(
            endpoint: "https://openrouter.ai/api/v1/chat/completions",
            apiKey: apiKey,
            model: model,
            systemPrompt: systemPrompt,
            messages: messages,
            maxAttempts: MaxUpstreamAttempts,
            cancellationToken: cancellationToken);
    }

    private async Task<string> CallOpenAiCompatibleChatAsync(
        string endpoint,
        string apiKey,
        string model,
        string systemPrompt,
        IReadOnlyList<ChatMessage> messages,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var providerMessages = new List<object>
        {
            new
            {
                role = "system",
                content = systemPrompt,
            },
        };

        foreach (var msg in messages)
        {
            var text = msg.Content.Trim();
            if (msg.Images.Count > 0)
            {
                var parts = new List<object>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(new { type = "text", text });
                }

                foreach (var image in msg.Images)
                {
                    var dataUrl = $"data:{image.MimeType};base64,{image.DataB64}";
                    parts.Add(new
                    {
                        type = "image_url",
                        image_url = new { url = dataUrl },
                    });
                }

                providerMessages.Add(new
                {
                    role = msg.Role,
                    content = parts,
                });
                continue;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                providerMessages.Add(new
                {
                    role = msg.Role,
                    content = text,
                });
            }
        }

        var payload = new
        {
            model,
            temperature = _settings.Temperature,
            messages = providerMessages,
        };

        using var doc = await PostJsonAsync(endpoint, payload, apiKey, maxAttempts, cancellationToken);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Provider returned no choices.");
        }

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var content))
        {
            throw new InvalidOperationException("Provider returned empty message content.");
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var textParts = new List<string>();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (part.TryGetProperty("text", out var textProp) &&
                    textProp.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(textProp.GetString()))
                {
                    textParts.Add(textProp.GetString()!.Trim());
                }
            }

            if (textParts.Count > 0)
            {
                return string.Join("\n", textParts);
            }
        }

        throw new InvalidOperationException("Provider returned an empty response.");
    }

    private async Task<string> ApproveCallNameWithGeminiAsync(string fieldName, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApprovalGeminiApiKey))
        {
            throw new InvalidOperationException(
                "Missing APPROVAL_GEMINI_API_KEY (or GEMINI_API_KEY fallback)");
        }

        var endpoint = BuildGeminiEndpoint(
            _settings.GeminiApprovalModel,
            _settings.ApprovalGeminiApiKey);

        var payload = new
        {
            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new
                        {
                            text = $"Call-name field: {fieldName}\nContent: {value}",
                        },
                    },
                },
            },
            generationConfig = new
            {
                temperature = 0,
            },
            systemInstruction = new
            {
                parts = new object[]
                {
                    new
                    {
                        text = ApprovalSystemInstruction(),
                    },
                },
            },
        };

        using var doc = await PostJsonAsync(
            endpoint,
            payload,
            bearerToken: null,
            maxAttempts: MaxUpstreamAttempts,
            cancellationToken);
        return ExtractGeminiText(doc.RootElement, "Gemini approval");
    }

    private async Task<string> CallGeminiAsync(
        IReadOnlyList<ChatMessage> messages,
        string systemPrompt,
        string? apiKey,
        string model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Missing GEMINI_API_KEY for selected persona.");
        }

        var endpoint = BuildGeminiEndpoint(model, apiKey);
        var contents = new List<object>();
        foreach (var msg in messages)
        {
            var role = msg.Role == "assistant" ? "model" : "user";
            var parts = new List<object>();
            if (!string.IsNullOrWhiteSpace(msg.Content))
            {
                parts.Add(new { text = msg.Content });
            }

            foreach (var image in msg.Images)
            {
                parts.Add(new
                {
                    inlineData = new
                    {
                        mimeType = image.MimeType,
                        data = image.DataB64,
                    },
                });
            }

            if (parts.Count > 0)
            {
                contents.Add(new
                {
                    role,
                    parts,
                });
            }
        }

        var payload = new
        {
            contents,
            generationConfig = new
            {
                temperature = _settings.Temperature,
            },
            systemInstruction = new
            {
                parts = new object[]
                {
                    new { text = systemPrompt },
                },
            },
        };

        using var doc = await PostJsonAsync(
            endpoint,
            payload,
            bearerToken: null,
            maxAttempts: MaxUpstreamAttempts,
            cancellationToken);
        return ExtractGeminiText(doc.RootElement, "Gemini");
    }

    private static string ExtractGeminiText(JsonElement root, string context)
    {
        if (root.TryGetProperty("text", out var directText) &&
            directText.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(directText.GetString()))
        {
            return directText.GetString()!.Trim();
        }

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{context} returned an empty response.");
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Object ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var textParts = new List<string>();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (part.TryGetProperty("text", out var textProp) &&
                    textProp.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(textProp.GetString()))
                {
                    textParts.Add(textProp.GetString()!.Trim());
                }
            }

            if (textParts.Count > 0)
            {
                return string.Join("\n", textParts);
            }
        }

        throw new InvalidOperationException($"{context} returned an empty response.");
    }

    private static string? NormalizeYesNo(string value)
    {
        var cleaned = value.Trim().ToLowerInvariant().Trim('`', '\'', '"', '.', '!', '?', '[', ']', '(', ')', '{', '}', ' ');
        return cleaned switch
        {
            "yes" or "y" => "yes",
            "no" or "n" => "no",
            _ => null,
        };
    }

    private static string? FindLatestUserText(IReadOnlyList<ChatMessage> messages)
    {
        for (var idx = messages.Count - 1; idx >= 0; idx--)
        {
            var message = messages[idx];
            if (!string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                return message.Content;
            }
        }

        return null;
    }

    private static string ApprovalSystemInstruction()
    {
        return
            "You are a moderator for Discord call-names. " +
            "Reply with exactly one word: 'yes' or 'no'. " +
            "Reply 'no' if the content is insulting, harassing, hateful, sexual, " +
            "discriminatory, or generally not appropriate for respectful addressing.";
    }

    private static string BuildGeminiEndpoint(string model, string apiKey)
    {
        var escapedModel = Uri.EscapeDataString(model);
        var escapedKey = Uri.EscapeDataString(apiKey);
        return $"https://generativelanguage.googleapis.com/v1beta/models/{escapedModel}:generateContent?key={escapedKey}";
    }

    private async Task<JsonDocument> PostJsonAsync(
        string endpoint,
        object payload,
        string? bearerToken,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var attemptLimit = Math.Max(1, maxAttempts);
        for (var attempt = 1; attempt <= attemptLimit; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload, JsonOptions),
                        Encoding.UTF8,
                        "application/json"),
                };

                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var failureKind = ClassifyHttpFailure(response.StatusCode, body);
                    var rateLimitInfo = BuildRateLimitInfo(response.Headers);
                    var snippet = MakeBodySnippet(body);
                    if (!IsTransientStatusCode(response.StatusCode))
                    {
                        throw new InvalidOperationException(
                            $"Upstream HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) kind={failureKind}; rate_limit={rateLimitInfo}; body={snippet}");
                    }

                    if (attempt == attemptLimit)
                    {
                        throw new UpstreamTransientException(
                            $"Upstream HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) kind={failureKind} (after {attempt} attempts); rate_limit={rateLimitInfo}; body={snippet}");
                    }

                    var delay = ComputeRetryDelay(attempt, response.Headers.RetryAfter);
                    Console.Error.WriteLine(
                        $"[{DateTimeOffset.UtcNow:O}] [llm] transient upstream HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) kind={failureKind} on attempt {attempt}/{attemptLimit}; retrying in {(int)delay.TotalMilliseconds}ms; rate_limit={rateLimitInfo}; body={snippet}");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                try
                {
                    return JsonDocument.Parse(body);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"Invalid JSON from upstream: {ex.Message}",
                        ex);
                }
            }
            catch (HttpRequestException ex) when (attempt < attemptLimit)
            {
                var delay = ComputeRetryDelay(attempt, retryAfter: null);
                Console.Error.WriteLine(
                    $"[{DateTimeOffset.UtcNow:O}] [llm] transient upstream network error on attempt {attempt}/{attemptLimit}: {ex.Message}; retrying in {(int)delay.TotalMilliseconds}ms");
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < attemptLimit)
            {
                var delay = ComputeRetryDelay(attempt, retryAfter: null);
                Console.Error.WriteLine(
                    $"[{DateTimeOffset.UtcNow:O}] [llm] upstream request timed out on attempt {attempt}/{attemptLimit}: {ex.Message}; retrying in {(int)delay.TotalMilliseconds}ms");
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new UpstreamTransientException(
                    $"Upstream request failed after {attemptLimit} attempts: {ex.Message}",
                    ex);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpstreamTransientException(
                    $"Upstream request timed out after {attemptLimit} attempts.",
                    ex);
            }
        }

        throw new InvalidOperationException("Upstream request failed after retries.");
    }

    private static string MakeBodySnippet(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
        {
            return "<empty>";
        }

        return trimmed.Length <= MaxErrorSnippetChars ? trimmed : trimmed[..MaxErrorSnippetChars];
    }

    private static string ClassifyHttpFailure(HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return "rate_limit";
        }

        if (statusCode == HttpStatusCode.ServiceUnavailable)
        {
            if (ContainsInsensitive(responseBody, "over capacity"))
            {
                return "over_capacity";
            }

            return "service_unavailable";
        }

        if ((int)statusCode is >= 500 and <= 599)
        {
            return "upstream_server_error";
        }

        if ((int)statusCode is >= 400 and <= 499)
        {
            return "client_error";
        }

        return "unknown";
    }

    private static bool ContainsInsensitive(string value, string candidate)
    {
        return value.Contains(candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRateLimitInfo(HttpResponseHeaders headers)
    {
        var parts = new List<string>();
        AppendHeaderIfPresent(parts, headers, "retry-after", "retry_after");
        AppendHeaderIfPresent(parts, headers, "x-ratelimit-limit-requests", "limit_req");
        AppendHeaderIfPresent(parts, headers, "x-ratelimit-remaining-requests", "rem_req");
        AppendHeaderIfPresent(parts, headers, "x-ratelimit-reset-requests", "reset_req");
        AppendHeaderIfPresent(parts, headers, "x-ratelimit-limit-tokens", "limit_tok");
        AppendHeaderIfPresent(parts, headers, "x-ratelimit-remaining-tokens", "rem_tok");
        AppendHeaderIfPresent(parts, headers, "x-ratelimit-reset-tokens", "reset_tok");
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }

    private static void AppendHeaderIfPresent(
        List<string> parts,
        HttpResponseHeaders headers,
        string headerName,
        string label)
    {
        if (!headers.TryGetValues(headerName, out var values))
        {
            return;
        }

        var value = string.Join("|", values).Trim();
        if (value.Length == 0)
        {
            return;
        }

        parts.Add($"{label}:{value}");
    }

    private sealed record ProviderTarget(
        string Provider,
        string Model,
        string? ApiKey);

    private sealed class UpstreamTransientException : Exception
    {
        public UpstreamTransientException(string message)
            : base(message)
        {
        }

        public UpstreamTransientException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            || code == 425;
    }

    private static TimeSpan ComputeRetryDelay(int attempt, RetryConditionHeaderValue? retryAfter)
    {
        var suggestedDelay = ParseRetryAfter(retryAfter);
        if (suggestedDelay.HasValue)
        {
            return ClampRetryDelay(suggestedDelay.Value);
        }

        var exponentialMs = BaseRetryDelayMs * Math.Pow(2, attempt - 1);
        var jitterMs = Random.Shared.Next(125, 501);
        var delayMs = Math.Min(MaxRetryDelayMs, exponentialMs + jitterMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }

    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta.HasValue)
        {
            return retryAfter.Delta.Value;
        }

        if (retryAfter.Date.HasValue)
        {
            var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return null;
    }

    private static TimeSpan ClampRetryDelay(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return TimeSpan.FromMilliseconds(BaseRetryDelayMs);
        }

        if (delay > TimeSpan.FromMilliseconds(MaxRetryDelayMs))
        {
            return TimeSpan.FromMilliseconds(MaxRetryDelayMs);
        }

        return delay;
    }
}
