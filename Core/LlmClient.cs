using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TetoTerritory.CSharp.Core;

public sealed class LlmClient : IDisposable
{
    private const string OverloadFallbackMessage = "i overload!";

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

    public async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        try
        {
            return _settings.Provider switch
            {
                "gemini" => await CallGeminiAsync(messages, cancellationToken),
                "groq" => await CallGroqAsync(messages, cancellationToken),
                "openai" => await CallOpenAiAsync(messages, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported provider: {_settings.Provider}"),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
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

    private async Task<string> CallOpenAiAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.OpenAiApiKey))
        {
            throw new InvalidOperationException("Missing OPENAI_API_KEY");
        }

        return await CallOpenAiCompatibleChatAsync(
            endpoint: "https://api.openai.com/v1/chat/completions",
            apiKey: _settings.OpenAiApiKey,
            model: _settings.OpenAiModel,
            messages: messages,
            cancellationToken: cancellationToken);
    }

    private async Task<string> CallGroqAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.GroqApiKey))
        {
            throw new InvalidOperationException("Missing GROQ_API_KEY");
        }

        return await CallOpenAiCompatibleChatAsync(
            endpoint: "https://api.groq.com/openai/v1/chat/completions",
            apiKey: _settings.GroqApiKey,
            model: _settings.GroqModel,
            messages: messages,
            cancellationToken: cancellationToken);
    }

    private async Task<string> CallOpenAiCompatibleChatAsync(
        string endpoint,
        string apiKey,
        string model,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var providerMessages = new List<object>
        {
            new
            {
                role = "system",
                content = _settings.SystemPrompt,
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

        using var doc = await PostJsonAsync(endpoint, payload, apiKey, cancellationToken);
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

        using var doc = await PostJsonAsync(endpoint, payload, bearerToken: null, cancellationToken);
        return ExtractGeminiText(doc.RootElement, "Gemini approval");
    }

    private async Task<string> CallGeminiAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.GeminiApiKey))
        {
            throw new InvalidOperationException("Missing GEMINI_API_KEY");
        }

        var endpoint = BuildGeminiEndpoint(_settings.GeminiModel, _settings.GeminiApiKey);
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
                    new { text = _settings.SystemPrompt },
                },
            },
        };

        using var doc = await PostJsonAsync(endpoint, payload, bearerToken: null, cancellationToken);
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
        CancellationToken cancellationToken)
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
            var snippet = body.Length <= 400 ? body : body[..400];
            throw new InvalidOperationException(
                $"Upstream HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {snippet}");
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
}
