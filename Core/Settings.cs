using System.Globalization;
using System.Text.Json;

namespace TetoTerritory.CSharp.Core;

public sealed class Settings
{
    private const string DefaultGeminiModel = "gemini-3-flash";
    private const string DefaultGeminiApprovalModel = "gemini-3-flash";
    private const string DefaultOpenAiModel = "gpt-4o-mini";

    public required string DiscordToken { get; init; }
    public required string CommandPrefix { get; init; }
    public required bool RpcEnabled { get; init; }
    public required string RpcStatus { get; init; }
    public required string RpcActivityType { get; init; }
    public required string RpcActivityName { get; init; }
    public string? RpcActivityUrl { get; init; }
    public ulong? BotOwnerUserId { get; init; }

    public required string Provider { get; init; }
    public string? GeminiApiKey { get; init; }
    public string? ApprovalGeminiApiKey { get; init; }
    public required string GeminiModel { get; init; }
    public required string GeminiApprovalModel { get; init; }
    public string? GroqApiKey { get; init; }
    public required string GroqModel { get; init; }
    public string? OpenAiApiKey { get; init; }
    public required string OpenAiModel { get; init; }

    public required string SystemPrompt { get; init; }
    public required string SystemRulesJson { get; init; }
    public required string ChatReplayLogPath { get; init; }
    public required string ChatMemoryDbPath { get; init; }
    public required string BanDbPath { get; init; }
    public required string CallnamesDbPath { get; init; }
    public required int MemoryIdleTtlSeconds { get; init; }
    public required int ImageMaxBytes { get; init; }
    public required int MaxReplyChars { get; init; }
    public required double Temperature { get; init; }
    public required int MaxHistory { get; init; }

    public static Settings Load()
    {
        var provider = GetEnvString("LLM_PROVIDER", "gemini").ToLowerInvariant();
        if (provider == "chatgpt")
        {
            provider = "openai";
        }

        if (provider is not ("gemini" or "groq" or "openai"))
        {
            throw new InvalidOperationException(
                "LLM_PROVIDER must be one of: gemini, groq, openai, chatgpt.");
        }

        var discordToken = GetEnvString("DISCORD_TOKEN", string.Empty);
        if (string.IsNullOrWhiteSpace(discordToken))
        {
            throw new InvalidOperationException("Missing DISCORD_TOKEN in environment variables.");
        }

        var baseSystemPrompt = GetEnvString(
            "SYSTEM_PROMPT",
            "You are Teto, a playful AI assistant on Discord. Reply in the same language as the user's latest message. Keep a light, fun tone while staying helpful and respectful.");

        var systemRulesJson = GetEnvString("SYSTEM_RULES_JSON", "system_rules.json");
        var rulesPrompt = LoadSystemRulesPrompt(systemRulesJson);
        var fullSystemPrompt = string.IsNullOrWhiteSpace(rulesPrompt)
            ? baseSystemPrompt
            : $"{baseSystemPrompt}\n\n{rulesPrompt}";

        var legacyMemoryDbPath = GetEnvString("MEMORY_DB_PATH", "chat_memory.db");
        var geminiModel = GetEnvString("GEMINI_MODEL", DefaultGeminiModel);
        var geminiApprovalModel = GetEnvString("GEMINI_APPROVAL_MODEL", DefaultGeminiApprovalModel);
        var groqModel = GetEnvString("GROQ_MODEL", "llama-3.3-70b-versatile");
        var openAiModel = GetEnvString("OPENAI_MODEL", DefaultOpenAiModel);

        var geminiApiKey = GetOptionalEnv("GEMINI_API_KEY");
        var groqApiKey = GetOptionalEnv("GROQ_API_KEY");
        var openAiApiKey = GetOptionalEnv("OPENAI_API_KEY");
        var approvalGeminiApiKey = GetOptionalEnv("APPROVAL_GEMINI_API_KEY") ?? geminiApiKey;

        if (string.IsNullOrWhiteSpace(geminiApprovalModel))
        {
            throw new InvalidOperationException("GEMINI_APPROVAL_MODEL cannot be empty.");
        }

        if (provider == "gemini" && string.IsNullOrWhiteSpace(geminiApiKey))
        {
            throw new InvalidOperationException("Missing GEMINI_API_KEY for LLM_PROVIDER=gemini.");
        }

        if (provider == "groq" && string.IsNullOrWhiteSpace(groqApiKey))
        {
            throw new InvalidOperationException("Missing GROQ_API_KEY for LLM_PROVIDER=groq.");
        }

        if (provider == "openai" && string.IsNullOrWhiteSpace(openAiApiKey))
        {
            throw new InvalidOperationException(
                "Missing OPENAI_API_KEY for LLM_PROVIDER=openai (or chatgpt).");
        }

        if (string.IsNullOrWhiteSpace(approvalGeminiApiKey))
        {
            throw new InvalidOperationException(
                "Missing approval Gemini API key. Set APPROVAL_GEMINI_API_KEY or GEMINI_API_KEY.");
        }

        var rpcEnabled = GetEnvBool("RPC_ENABLED", true);
        var rpcStatus = GetEnvString("RPC_STATUS", "online").ToLowerInvariant();
        var rpcActivityType = GetEnvString("RPC_ACTIVITY_TYPE", "playing").ToLowerInvariant();
        var rpcActivityName = GetEnvString("RPC_ACTIVITY_NAME", "with AI chats");
        var rpcActivityUrl = GetOptionalEnv("RPC_ACTIVITY_URL");

        if (rpcStatus is not ("online" or "idle" or "dnd" or "invisible"))
        {
            throw new InvalidOperationException(
                "RPC_STATUS must be one of: online, idle, dnd, invisible.");
        }

        if (rpcActivityType is not ("none" or "playing" or "listening" or "watching" or "competing" or "streaming"))
        {
            throw new InvalidOperationException(
                "RPC_ACTIVITY_TYPE must be one of: none, playing, listening, watching, competing, streaming.");
        }

        if (rpcActivityType != "none" && string.IsNullOrWhiteSpace(rpcActivityName))
        {
            throw new InvalidOperationException(
                "RPC_ACTIVITY_NAME cannot be empty when RPC_ACTIVITY_TYPE is set.");
        }

        if (rpcActivityType == "streaming" && string.IsNullOrWhiteSpace(rpcActivityUrl))
        {
            throw new InvalidOperationException(
                "RPC_ACTIVITY_URL is required when RPC_ACTIVITY_TYPE=streaming.");
        }

        ulong? botOwnerUserId = null;
        var ownerRaw = GetOptionalEnv("BOT_OWNER_USER_ID");
        if (!string.IsNullOrWhiteSpace(ownerRaw))
        {
            if (!ulong.TryParse(ownerRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new InvalidOperationException($"BOT_OWNER_USER_ID must be an unsigned integer, got: '{ownerRaw}'");
            }

            botOwnerUserId = parsed;
        }

        return new Settings
        {
            DiscordToken = discordToken,
            CommandPrefix = GetEnvString("COMMAND_PREFIX", "!"),
            RpcEnabled = rpcEnabled,
            RpcStatus = rpcStatus,
            RpcActivityType = rpcActivityType,
            RpcActivityName = rpcActivityName,
            RpcActivityUrl = rpcActivityUrl,
            BotOwnerUserId = botOwnerUserId,
            Provider = provider,
            GeminiApiKey = geminiApiKey,
            ApprovalGeminiApiKey = approvalGeminiApiKey,
            GeminiModel = geminiModel,
            GeminiApprovalModel = geminiApprovalModel,
            GroqApiKey = groqApiKey,
            GroqModel = groqModel,
            OpenAiApiKey = openAiApiKey,
            OpenAiModel = openAiModel,
            SystemPrompt = fullSystemPrompt,
            SystemRulesJson = systemRulesJson,
            ChatReplayLogPath = GetEnvString("CHAT_REPLAY_LOG_PATH", "logger/chat_replay.jsonl"),
            ChatMemoryDbPath = GetEnvString("CHAT_MEMORY_DB_PATH", legacyMemoryDbPath),
            BanDbPath = GetEnvString("BAN_DB_PATH", "ban_control.db"),
            CallnamesDbPath = GetEnvString("CALLNAMES_DB_PATH", "callnames.db"),
            MemoryIdleTtlSeconds = GetEnvInt("MEMORY_IDLE_TTL_SECONDS", 300, minimum: 0),
            ImageMaxBytes = GetEnvInt("IMAGE_MAX_BYTES", 5 * 1024 * 1024, minimum: 1),
            MaxReplyChars = GetEnvInt("MAX_REPLY_CHARS", 1800, minimum: 100),
            Temperature = GetEnvDouble("TEMPERATURE", 0.7),
            MaxHistory = GetEnvInt("MAX_HISTORY", 10, minimum: 1),
        };
    }

    private static string GetEnvString(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim();
    }

    private static string? GetOptionalEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static int GetEnvInt(string name, int fallback, int? minimum = null)
    {
        var raw = GetEnvString(name, fallback.ToString(CultureInfo.InvariantCulture));
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"{name} must be an integer, got: '{raw}'");
        }

        if (minimum.HasValue && value < minimum.Value)
        {
            return minimum.Value;
        }

        return value;
    }

    private static double GetEnvDouble(string name, double fallback)
    {
        var raw = GetEnvString(name, fallback.ToString(CultureInfo.InvariantCulture));
        if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"{name} must be a float, got: '{raw}'");
        }

        return value;
    }

    private static bool GetEnvBool(string name, bool fallback)
    {
        var raw = GetEnvString(name, fallback ? "true" : "false").ToLowerInvariant();
        return raw switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new InvalidOperationException(
                $"{name} must be a boolean (true/false), got: '{raw}'"),
        };
    }

    private static string LoadSystemRulesPrompt(string pathValue)
    {
        var path = Path.GetFullPath(pathValue);
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid JSON in system rules file: {path}",
                ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Cannot read system rules file: {path}",
                ex);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"System rules JSON must be an object: {path}");
        }

        if (root.TryGetProperty("enabled", out var enabled) &&
            enabled.ValueKind == JsonValueKind.False)
        {
            return string.Empty;
        }

        var pretty = JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        return
            "You must follow these extra system rules loaded from JSON.\n" +
            "If response_form exists, obey it exactly.\n" +
            $"Rules source: {path}\n" +
            "Rules JSON:\n" +
            pretty;
    }
}
