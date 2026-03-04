using System.Globalization;
using System.Text;

namespace TetoTerritory.CSharp.Core;

public sealed class Settings
{
    private const string DefaultGeminiModel = "gemini-3-flash";
    private const string DefaultGeminiApprovalModel = "gemini-3-flash";
    private const string DefaultGroqModel = "llama-3.3-70b-versatile";
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
    public required string SystemRulesPath { get; init; }

    public required bool Persona2Enabled { get; init; }
    public required string Persona2Name { get; init; }
    public required string Persona2Provider { get; init; }
    public string? Persona2GeminiApiKey { get; init; }
    public required string Persona2GeminiModel { get; init; }
    public string? Persona2GroqApiKey { get; init; }
    public required string Persona2GroqModel { get; init; }
    public string? Persona2OpenAiApiKey { get; init; }
    public required string Persona2OpenAiModel { get; init; }
    public required string Persona2SystemPrompt { get; init; }
    public required string Persona2SystemRulesPath { get; init; }

    public required string ChatReplayLogPath { get; init; }
    public required string ChatMemoryDbPath { get; init; }
    public required string BanDbPath { get; init; }
    public required string CallnamesDbPath { get; init; }
    public required int MemoryIdleTtlSeconds { get; init; }
    public required int ImageMaxBytes { get; init; }
    public required int MaxReplyChars { get; init; }
    public required double Temperature { get; init; }
    public required int MaxHistory { get; init; }

    public LlmRuntimeProfile MainPersonaProfile =>
        new(
            PersonaKey: "main",
            PersonaName: "teto",
            Provider: Provider,
            SystemPrompt: SystemPrompt,
            GeminiApiKey: GeminiApiKey,
            GeminiModel: GeminiModel,
            GroqApiKey: GroqApiKey,
            GroqModel: GroqModel,
            OpenAiApiKey: OpenAiApiKey,
            OpenAiModel: OpenAiModel);

    public LlmRuntimeProfile? SecondaryPersonaProfile
    {
        get
        {
            if (!Persona2Enabled)
            {
                return null;
            }

            return new LlmRuntimeProfile(
                PersonaKey: "persona2",
                PersonaName: Persona2Name,
                Provider: Persona2Provider,
                SystemPrompt: Persona2SystemPrompt,
                GeminiApiKey: Persona2GeminiApiKey,
                GeminiModel: Persona2GeminiModel,
                GroqApiKey: Persona2GroqApiKey,
                GroqModel: Persona2GroqModel,
                OpenAiApiKey: Persona2OpenAiApiKey,
                OpenAiModel: Persona2OpenAiModel);
        }
    }

    public static Settings Load()
    {
        var provider = NormalizeProvider(
            GetEnvString("LLM_PROVIDER", "gemini"),
            "LLM_PROVIDER");

        var discordToken = GetEnvString("DISCORD_TOKEN", string.Empty);
        if (string.IsNullOrWhiteSpace(discordToken))
        {
            throw new InvalidOperationException("Missing DISCORD_TOKEN in environment variables.");
        }

        var baseSystemPrompt = GetEnvString(
            "SYSTEM_PROMPT",
            "You are Teto, a playful AI assistant on Discord. Reply in the same language as the user's latest message. Keep a light, fun tone while staying helpful and respectful.");

        var systemRulesPath = GetEnvString("SYSTEM_RULES_PATH", "system_rules.md");
        var rulesPrompt = LoadSystemRulesPrompt(systemRulesPath);
        var fullSystemPrompt = string.IsNullOrWhiteSpace(rulesPrompt)
            ? baseSystemPrompt
            : $"{baseSystemPrompt}\n\n{rulesPrompt}";

        var legacyMemoryDbPath = GetEnvString("MEMORY_DB_PATH", "data/chat_memory.db");
        var geminiModel = GetEnvString("GEMINI_MODEL", DefaultGeminiModel);
        var geminiApprovalModel = GetEnvString("GEMINI_APPROVAL_MODEL", DefaultGeminiApprovalModel);
        var groqModel = GetEnvString("GROQ_MODEL", DefaultGroqModel);
        var openAiModel = GetEnvString("OPENAI_MODEL", DefaultOpenAiModel);

        var geminiApiKey = GetOptionalEnv("GEMINI_API_KEY");
        var groqApiKey = GetOptionalEnv("GROQ_API_KEY");
        var openAiApiKey = GetOptionalEnv("OPENAI_API_KEY");
        var approvalGeminiApiKey = GetOptionalEnv("APPROVAL_GEMINI_API_KEY") ?? geminiApiKey;

        var persona2RulesPath = GetEnvString("SYSTEM_RULES_PATH_2", "system_rule2.md");
        var persona2DefaultEnabled = File.Exists(Path.GetFullPath(persona2RulesPath));
        var persona2Enabled = GetEnvBool("PERSONA2_ENABLED", persona2DefaultEnabled);
        var persona2Name = GetEnvString("PERSONA2_NAME", "persona2");
        if (string.IsNullOrWhiteSpace(persona2Name))
        {
            persona2Name = "persona2";
        }

        var provider2Default = provider switch
        {
            "gemini" => "groq",
            "groq" => "openai",
            _ => "gemini",
        };
        var persona2Provider = NormalizeProvider(
            GetEnvString("LLM_PROVIDER_2", provider2Default),
            "LLM_PROVIDER_2");

        var baseSystemPrompt2 = GetEnvString("SYSTEM_PROMPT_2", baseSystemPrompt);
        var rulesPrompt2 = LoadSystemRulesPrompt(persona2RulesPath);
        var fullSystemPrompt2 = string.IsNullOrWhiteSpace(rulesPrompt2)
            ? baseSystemPrompt2
            : $"{baseSystemPrompt2}\n\n{rulesPrompt2}";

        var persona2GeminiModel = GetEnvString("GEMINI_MODEL_2", geminiModel);
        var persona2GroqModel = GetEnvString("GROQ_MODEL_2", groqModel);
        var persona2OpenAiModel = GetEnvString("OPENAI_MODEL_2", openAiModel);

        var persona2GeminiApiKey = GetOptionalEnv("GEMINI_API_KEY_2") ?? geminiApiKey;
        var persona2GroqApiKey = GetOptionalEnv("GROQ_API_KEY_2") ?? groqApiKey;
        var persona2OpenAiApiKey = GetOptionalEnv("OPENAI_API_KEY_2") ?? openAiApiKey;

        if (string.IsNullOrWhiteSpace(geminiApprovalModel))
        {
            throw new InvalidOperationException("GEMINI_APPROVAL_MODEL cannot be empty.");
        }

        ValidateProviderKey(provider, geminiApiKey, groqApiKey, openAiApiKey, "LLM_PROVIDER");

        if (string.IsNullOrWhiteSpace(approvalGeminiApiKey))
        {
            throw new InvalidOperationException(
                "Missing approval Gemini API key. Set APPROVAL_GEMINI_API_KEY or GEMINI_API_KEY.");
        }

        if (persona2Enabled)
        {
            ValidateProviderKey(
                persona2Provider,
                persona2GeminiApiKey,
                persona2GroqApiKey,
                persona2OpenAiApiKey,
                "LLM_PROVIDER_2");

            var mainSignature = BuildProviderSignature(
                provider,
                geminiModel,
                groqModel,
                openAiModel);
            var persona2Signature = BuildProviderSignature(
                persona2Provider,
                persona2GeminiModel,
                persona2GroqModel,
                persona2OpenAiModel);
            if (string.Equals(mainSignature, persona2Signature, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "PERSONA2 must use a different provider/model than the primary persona.");
            }
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
            SystemRulesPath = systemRulesPath,
            Persona2Enabled = persona2Enabled,
            Persona2Name = persona2Name,
            Persona2Provider = persona2Provider,
            Persona2GeminiApiKey = persona2GeminiApiKey,
            Persona2GeminiModel = persona2GeminiModel,
            Persona2GroqApiKey = persona2GroqApiKey,
            Persona2GroqModel = persona2GroqModel,
            Persona2OpenAiApiKey = persona2OpenAiApiKey,
            Persona2OpenAiModel = persona2OpenAiModel,
            Persona2SystemPrompt = fullSystemPrompt2,
            Persona2SystemRulesPath = persona2RulesPath,
            ChatReplayLogPath = GetEnvString("CHAT_REPLAY_LOG_PATH", "logger/chat_replay.jsonl"),
            ChatMemoryDbPath = GetEnvString("CHAT_MEMORY_DB_PATH", legacyMemoryDbPath),
            BanDbPath = GetEnvString("BAN_DB_PATH", "data/ban_control.db"),
            CallnamesDbPath = GetEnvString("CALLNAMES_DB_PATH", "data/callnames.db"),
            MemoryIdleTtlSeconds = GetEnvInt("MEMORY_IDLE_TTL_SECONDS", 300, minimum: 0),
            ImageMaxBytes = GetEnvInt("IMAGE_MAX_BYTES", 5 * 1024 * 1024, minimum: 1),
            MaxReplyChars = GetEnvInt("MAX_REPLY_CHARS", 1800, minimum: 100),
            Temperature = GetEnvDouble("TEMPERATURE", 0.7),
            MaxHistory = GetEnvInt("MAX_HISTORY", 10, minimum: 1),
        };
    }

    private static void ValidateProviderKey(
        string provider,
        string? geminiApiKey,
        string? groqApiKey,
        string? openAiApiKey,
        string providerEnvName)
    {
        if (provider == "gemini" && string.IsNullOrWhiteSpace(geminiApiKey))
        {
            throw new InvalidOperationException($"Missing GEMINI_API_KEY for {providerEnvName}=gemini.");
        }

        if (provider == "groq" && string.IsNullOrWhiteSpace(groqApiKey))
        {
            throw new InvalidOperationException($"Missing GROQ_API_KEY for {providerEnvName}=groq.");
        }

        if (provider == "openai" && string.IsNullOrWhiteSpace(openAiApiKey))
        {
            throw new InvalidOperationException(
                $"Missing OPENAI_API_KEY for {providerEnvName}=openai (or chatgpt).");
        }
    }

    private static string BuildProviderSignature(
        string provider,
        string geminiModel,
        string groqModel,
        string openAiModel)
    {
        var activeModel = ResolveActiveModel(provider, geminiModel, groqModel, openAiModel);
        return $"{provider}:{activeModel}";
    }

    private static string ResolveActiveModel(
        string provider,
        string geminiModel,
        string groqModel,
        string openAiModel)
    {
        return provider switch
        {
            "gemini" => geminiModel,
            "groq" => groqModel,
            "openai" => openAiModel,
            _ => throw new InvalidOperationException($"Unsupported provider: {provider}"),
        };
    }

    private static string NormalizeProvider(string rawValue, string variableName)
    {
        var provider = rawValue.Trim().ToLowerInvariant();
        if (provider == "chatgpt")
        {
            provider = "openai";
        }

        if (provider is not ("gemini" or "groq" or "openai"))
        {
            throw new InvalidOperationException(
                $"{variableName} must be one of: gemini, groq, openai, chatgpt.");
        }

        return provider;
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

        string content;
        try
        {
            content = File.ReadAllText(path, Encoding.UTF8).Trim();
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Cannot read system rules file: {path}",
                ex);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return
            "You must follow these extra system rules loaded from Markdown.\n" +
            "Reply directly in plain text unless the user asks for another format.\n" +
            $"Rules source: {path}\n" +
            "Rules Markdown:\n" +
            content;
    }
}
