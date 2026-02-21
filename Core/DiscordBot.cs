using Discord;
using Discord.WebSocket;
using TetoTerritory.CSharp.Commands;
using TetoTerritory.CSharp.Logging;
using TetoTerritory.CSharp.SlashCommands;
using TetoTerritory.CSharp.Storage;

namespace TetoTerritory.CSharp.Core;

public sealed class DiscordBot : IAsyncDisposable
{
    private const int CleanupIntervalSeconds = 60;
    private const int MaxCallNameLength = 60;
    internal const string DefaultPrompt = "hi";
    internal const string DefaultMentionPrompt = "hi";

    private readonly Settings _settings;
    private readonly DiscordSocketClient _client;
    private readonly HttpClient _httpClient;
    private readonly LlmClient _llmClient;
    private readonly ChatMemoryStore _chatMemory;
    private readonly BanStore _banStore;
    private readonly CallNamesStore _callNamesStore;
    private readonly ChatReplayLogger _replayLogger;
    private readonly CommandParser _commandParser;
    private readonly DiscordCommandDispatcher _commandDispatcher;
    private readonly SlashCommandDispatcher _slashCommandDispatcher;

    private CancellationTokenSource? _runCts;
    private Task? _cleanupTask;
    private bool _terminated;
    private ulong? _ownerUserId;
    private int _readyInitializationStarted;
    private bool _disposed;

    public DiscordBot(Settings settings)
    {
        _settings = settings;
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds |
                             GatewayIntents.GuildMessages |
                             GatewayIntents.DirectMessages |
                             GatewayIntents.MessageContent,
            AlwaysDownloadUsers = false,
        });
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        _llmClient = new LlmClient(settings);
        _chatMemory = new ChatMemoryStore(settings.ChatMemoryDbPath, settings.MaxHistory);
        _banStore = new BanStore(settings.BanDbPath);
        _callNamesStore = new CallNamesStore(settings.CallnamesDbPath);
        _replayLogger = new ChatReplayLogger(settings.ChatReplayLogPath);
        _commandParser = new CommandParser(settings.CommandPrefix);
        _commandDispatcher = new DiscordCommandDispatcher(
            new IDiscordCommandHandler[]
            {
                new ChatRuntimeCommandHandler(),
                new ReplayCommandHandler(),
                new BanCommandHandler(),
                new CallNamesCommandHandler(),
            });
        _slashCommandDispatcher = new SlashCommandDispatcher(
            new ISlashCommandHandler[]
            {
                new ChatSlashCommandHandler("chat"),
                new ChatSlashCommandHandler("ask"),
                new ClearMemorySlashCommandHandler("clearmemo"),
                new ClearMemorySlashCommandHandler("resetchat"),
                new TerminatedSlashCommandHandler(),
                new ProviderSlashCommandHandler(),
                new ReplaySlashCommandHandler(),
                new BanSlashCommandHandler(),
                new RemoveBanSlashCommandHandler(),
                new UserCallsTetoSlashCommandHandler("ucallteto"),
                new UserCallsTetoSlashCommandHandler("callteto"),
                new TetoCallsUserSlashCommandHandler("tetocallu"),
                new TetoCallsUserSlashCommandHandler("callme"),
                new CallProfileSlashCommandHandler("tetomention"),
                new CallProfileSlashCommandHandler("callprofile"),
                new TetoModelSlashCommandHandler(),
            });

        _ownerUserId = settings.BotOwnerUserId;
    }

    internal Settings Settings => _settings;

    internal bool IsTerminated
    {
        get => _terminated;
        set => _terminated = value;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _chatMemory.InitializeAsync(cancellationToken);
        await _banStore.InitializeAsync(cancellationToken);
        await _callNamesStore.InitializeAsync(cancellationToken);
        await _replayLogger.InitializeAsync(cancellationToken);

        _client.Log += OnLogAsync;
        _client.Ready += OnReadyAsync;
        _client.MessageReceived += OnMessageReceivedAsync;
        _client.SlashCommandExecuted += OnSlashCommandExecutedAsync;

        await _client.LoginAsync(TokenType.Bot, _settings.DiscordToken);
        await _client.StartAsync();

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            _runCts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        if (_settings.MemoryIdleTtlSeconds > 0)
        {
            _cleanupTask = RunMemoryCleanupLoopAsync(_runCts.Token);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, _runCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (_cleanupTask is not null)
            {
                try
                {
                    await _cleanupTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            await ShutdownAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _runCts?.Cancel();
            await ShutdownAsync();
        }
        finally
        {
            _runCts?.Dispose();
            _httpClient.Dispose();
            _llmClient.Dispose();
            _client.Dispose();
        }
    }

    internal async Task<bool> IsBannedAsync(SocketUserMessage message)
    {
        var guildId = GetGuildId(message);
        if (!guildId.HasValue)
        {
            return false;
        }

        return await _banStore.IsUserBannedAsync(guildId.Value, message.Author.Id);
    }

    internal async Task<bool> IsBannedAsync(ulong? guildId, ulong userId)
    {
        if (!guildId.HasValue)
        {
            return false;
        }

        return await _banStore.IsUserBannedAsync(guildId.Value, userId);
    }

    internal async Task<bool> EnsureOwnerPermissionAsync(SocketUserMessage message)
    {
        if (GetGuildId(message) is null)
        {
            await ReplyAsync(message, "This command can only be used in a server.");
            return false;
        }

        if (!IsOwner(message.Author))
        {
            await ReplyAsync(message, "Only the bot owner can use this command.");
            return false;
        }

        return true;
    }

    internal async Task<bool> EnsureOwnerPermissionAsync(SocketSlashCommand command)
    {
        if (GetGuildId(command) is null)
        {
            await RespondSlashAsync(command, "This command can only be used in a server.", ephemeral: true);
            return false;
        }

        if (!IsOwner(command.User))
        {
            await RespondSlashAsync(command, "Only the bot owner can use this command.", ephemeral: true);
            return false;
        }

        return true;
    }

    internal bool IsOwner(SocketUser user)
    {
        return _ownerUserId.HasValue && user.Id == _ownerUserId.Value;
    }

    internal ulong? GetGuildId(SocketUserMessage message)
    {
        return message.Channel is SocketGuildChannel guildChannel
            ? guildChannel.Guild.Id
            : null;
    }

    internal ulong? GetGuildId(SocketSlashCommand command)
    {
        return command.Channel is SocketGuildChannel guildChannel
            ? guildChannel.Guild.Id
            : null;
    }

    internal async Task ReplyAsync(SocketUserMessage sourceMessage, string text)
    {
        await sourceMessage.Channel.SendMessageAsync(
            text: text,
            allowedMentions: AllowedMentions.None,
            messageReference: new MessageReference(sourceMessage.Id));
    }

    internal async Task SendLongMessageAsync(SocketUserMessage sourceMessage, string text)
    {
        var safeText = BotTextNormalizer.SanitizeMentions(text);
        var maxLen = Math.Min(1900, _settings.MaxReplyChars);
        if (maxLen < 1)
        {
            maxLen = 1;
        }

        var chunks = new List<string>();
        for (var i = 0; i < safeText.Length; i += maxLen)
        {
            var len = Math.Min(maxLen, safeText.Length - i);
            chunks.Add(safeText.Substring(i, len));
        }

        if (chunks.Count == 0)
        {
            chunks.Add("(no content)");
        }

        for (var idx = 0; idx < chunks.Count; idx++)
        {
            if (idx == 0)
            {
                await ReplyAsync(sourceMessage, chunks[idx]);
            }
            else
            {
                await sourceMessage.Channel.SendMessageAsync(
                    text: chunks[idx],
                    allowedMentions: AllowedMentions.None);
            }
        }
    }

    internal async Task RunChatAndReplyAsync(
        SocketUserMessage sourceMessage,
        string prompt,
        string fallbackPrompt,
        string trigger)
    {
        var effectivePrompt = NormalizePrompt(prompt, fallbackPrompt);

        string reply;
        var imageCount = 0;
        using (sourceMessage.Channel.EnterTypingState())
        {
            try
            {
                var images = await ExtractImagesFromMessageAsync(sourceMessage);
                imageCount = images.Count;
                var guildId = GetGuildId(sourceMessage);
                var promptForLlm = await ApplyCallPreferencesToPromptAsync(
                    effectivePrompt,
                    guildId: guildId,
                    userId: sourceMessage.Author.Id);

                var history = await _chatMemory.GetHistoryAsync(sourceMessage.Channel.Id);
                var llmMessages = new List<ChatMessage>(history.Count + 1);
                llmMessages.AddRange(history.Select(h => new ChatMessage
                {
                    Role = h.Role,
                    Content = h.Content,
                }));
                llmMessages.Add(new ChatMessage
                {
                    Role = "user",
                    Content = promptForLlm,
                    Images = images,
                });

                var rawReply = await _llmClient.GenerateAsync(llmMessages);
                reply = BotTextNormalizer.NormalizeModelReply(rawReply);
            }
            catch (Exception ex)
            {
                await ReplyAsync(sourceMessage, $"Error while calling AI: `{ex.Message}`");
                return;
            }
        }

        await _chatMemory.AppendMessageAsync(
            sourceMessage.Channel.Id,
            "user",
            MemoryUserEntry(effectivePrompt, imageCount));
        await _chatMemory.AppendMessageAsync(
            sourceMessage.Channel.Id,
            "assistant",
            reply);

        await _replayLogger.LogChatAsync(
            guildId: GetGuildId(sourceMessage),
            guildName: GetGuildName(sourceMessage),
            channelId: sourceMessage.Channel.Id,
            channelName: GetChannelName(sourceMessage),
            userId: sourceMessage.Author.Id,
            userName: sourceMessage.Author.Username,
            userDisplay: sourceMessage.Author is SocketGuildUser guildUser
                ? guildUser.DisplayName
                : sourceMessage.Author.Username,
            trigger: trigger,
            prompt: effectivePrompt,
            replyLength: reply.Length);

        await SendLongMessageAsync(sourceMessage, reply);
    }

    internal async Task RunChatAndReplyAsync(
        SocketSlashCommand sourceCommand,
        string prompt,
        string fallbackPrompt,
        string trigger)
    {
        if (!sourceCommand.HasResponded)
        {
            await sourceCommand.DeferAsync();
        }

        var effectivePrompt = NormalizePrompt(prompt, fallbackPrompt);

        string reply;
        var imageCount = 0;
        try
        {
            var guildId = GetGuildId(sourceCommand);
            var promptForLlm = await ApplyCallPreferencesToPromptAsync(
                effectivePrompt,
                guildId: guildId,
                userId: sourceCommand.User.Id);

            var history = await _chatMemory.GetHistoryAsync(sourceCommand.Channel.Id);
            var llmMessages = new List<ChatMessage>(history.Count + 1);
            llmMessages.AddRange(history.Select(h => new ChatMessage
            {
                Role = h.Role,
                Content = h.Content,
            }));
            llmMessages.Add(new ChatMessage
            {
                Role = "user",
                Content = promptForLlm,
                Images = new List<ImageInput>(),
            });

            var rawReply = await _llmClient.GenerateAsync(llmMessages);
            reply = BotTextNormalizer.NormalizeModelReply(rawReply);
        }
        catch (Exception ex)
        {
            await RespondSlashAsync(sourceCommand, $"Error while calling AI: `{ex.Message}`", ephemeral: true);
            return;
        }

        await _chatMemory.AppendMessageAsync(
            sourceCommand.Channel.Id,
            "user",
            MemoryUserEntry(effectivePrompt, imageCount));
        await _chatMemory.AppendMessageAsync(
            sourceCommand.Channel.Id,
            "assistant",
            reply);

        await _replayLogger.LogChatAsync(
            guildId: GetGuildId(sourceCommand),
            guildName: GetGuildName(sourceCommand),
            channelId: sourceCommand.Channel.Id,
            channelName: GetChannelName(sourceCommand),
            userId: sourceCommand.User.Id,
            userName: sourceCommand.User.Username,
            userDisplay: sourceCommand.User is SocketGuildUser guildUser
                ? guildUser.DisplayName
                : sourceCommand.User.Username,
            trigger: trigger,
            prompt: effectivePrompt,
            replyLength: reply.Length);

        await SendLongSlashMessageAsync(sourceCommand, reply);
    }

    internal Task ClearChannelMemoryAsync(ulong channelId)
    {
        return _chatMemory.ClearChannelAsync(channelId);
    }

    internal string BuildProviderStatusMessage()
    {
        return
            $"Current provider: `{_settings.Provider}` | " +
            $"Model: `{ActiveChatModel()}` | " +
            "Approval provider: `gemini` | " +
            $"Approval model: `{_settings.GeminiApprovalModel}` | " +
            $"Chat DB: `{_settings.ChatMemoryDbPath}` | " +
            $"Ban DB: `{_settings.BanDbPath}` | " +
            $"Callnames DB: `{_settings.CallnamesDbPath}` | " +
            $"Idle TTL: `{_settings.MemoryIdleTtlSeconds}s` | " +
            $"Image limit: `{_settings.ImageMaxBytes}` bytes | " +
            $"Reply chunk size: `{_settings.MaxReplyChars}` chars | " +
            $"Terminated: `{_terminated}`";
    }

    internal string CurrentModelName()
    {
        return ActiveChatModel();
    }

    internal Task<bool> BanUserAsync(ulong guildId, ulong userId, ulong bannedBy, string? reason)
    {
        return _banStore.BanUserAsync(guildId, userId, bannedBy, reason);
    }

    internal Task<bool> UnbanUserAsync(ulong guildId, ulong userId)
    {
        return _banStore.UnbanUserAsync(guildId, userId);
    }

    internal Task SetUserCallsTetoAsync(ulong guildId, ulong userId, string callName, CancellationToken cancellationToken)
    {
        return _callNamesStore.SetUserCallsTetoAsync(guildId, userId, callName, cancellationToken);
    }

    internal Task SetTetoCallsUserAsync(ulong guildId, ulong userId, string callName, CancellationToken cancellationToken)
    {
        return _callNamesStore.SetTetoCallsUserAsync(guildId, userId, callName, cancellationToken);
    }

    internal async Task ShowCallProfileAsync(SocketUserMessage message)
    {
        var guildId = GetGuildId(message) ?? 0;
        var (userCallsTeto, tetoCallsUser) = await _callNamesStore.GetUserCallPreferencesAsync(guildId, message.Author.Id);
        var display = message.Author is SocketGuildUser guildUser ? guildUser.DisplayName : message.Author.Username;
        await ReplyAsync(
            message,
            "Current call profile | " +
            $"You call Teto: `{userCallsTeto ?? "Teto"}` | " +
            $"Teto calls you: `{tetoCallsUser ?? display}`");
    }

    internal async Task ShowCallProfileAsync(SocketSlashCommand command)
    {
        var guildId = GetGuildId(command) ?? 0;
        var (userCallsTeto, tetoCallsUser) = await _callNamesStore.GetUserCallPreferencesAsync(guildId, command.User.Id);
        var display = command.User is SocketGuildUser guildUser ? guildUser.DisplayName : command.User.Username;
        await RespondSlashAsync(
            command,
            "Current call profile | " +
            $"You call Teto: `{userCallsTeto ?? "Teto"}` | " +
            $"Teto calls you: `{tetoCallsUser ?? display}`",
            ephemeral: true);
    }

    internal async Task SetCallNameWithApprovalAsync(
        SocketUserMessage message,
        string rawName,
        string fieldName,
        string successMessage,
        Func<ulong, ulong, string, CancellationToken, Task> saveAsync)
    {
        var value = NormalizeCallName(rawName);
        if (value is null)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                await ReplyAsync(message, "Name cannot be empty.");
            }
            else
            {
                await ReplyAsync(message, $"Name is too long (max {MaxCallNameLength} characters).");
            }

            return;
        }

        bool approved;
        try
        {
            approved = await _llmClient.ApproveCallNameAsync(fieldName, value);
        }
        catch (Exception ex)
        {
            await ReplyAsync(message, $"Unable to run call-name approval right now: `{ex.Message}`");
            return;
        }

        if (!approved)
        {
            await ReplyAsync(message, "Call-name was rejected by approval (`no`).");
            return;
        }

        var guildId = GetGuildId(message) ?? 0;
        await saveAsync(guildId, message.Author.Id, value, CancellationToken.None);
        await ReplyAsync(message, string.Format(successMessage, value));
    }

    internal async Task SetCallNameWithApprovalAsync(
        SocketSlashCommand command,
        string rawName,
        string fieldName,
        string successMessage,
        Func<ulong, ulong, string, CancellationToken, Task> saveAsync)
    {
        var value = NormalizeCallName(rawName);
        if (value is null)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                await RespondSlashAsync(command, "Name cannot be empty.", ephemeral: true);
            }
            else
            {
                await RespondSlashAsync(command, $"Name is too long (max {MaxCallNameLength} characters).", ephemeral: true);
            }

            return;
        }

        bool approved;
        try
        {
            approved = await _llmClient.ApproveCallNameAsync(fieldName, value);
        }
        catch (Exception ex)
        {
            await RespondSlashAsync(command, $"Unable to run call-name approval right now: `{ex.Message}`", ephemeral: true);
            return;
        }

        if (!approved)
        {
            await RespondSlashAsync(command, "Call-name was rejected by approval (`no`).", ephemeral: true);
            return;
        }

        var guildId = GetGuildId(command) ?? 0;
        await saveAsync(guildId, command.User.Id, value, CancellationToken.None);
        await RespondSlashAsync(command, string.Format(successMessage, value), ephemeral: true);
    }

    internal async Task<string> BuildReplayPayloadAsync(string action, ulong? guildId)
    {
        var normalized = action.Trim().ToLowerInvariant();
        if (normalized == "ls")
        {
            var records = await _replayLogger.ReadRecentAsync(limit: 30, guildId: guildId);
            if (records.Count == 0)
            {
                return "No chat replay logs yet.";
            }

            var lines = new List<string> { "Replay logs (newest first):" };
            foreach (var item in records)
            {
                var prompt = item.Prompt.Replace("\n", " ", StringComparison.Ordinal).Trim();
                if (prompt.Length > 70)
                {
                    prompt = $"{prompt[..67]}...";
                }

                lines.Add(
                    $"[{item.Id}] {item.TsUtc} | {item.UserDisplay} ({item.UserId}) | {item.Trigger} | {prompt}");
            }

            lines.Add($"Use `{_settings.CommandPrefix}replayteto <id>` to view full details.");
            return string.Join('\n', lines);
        }

        if (!int.TryParse(normalized, out var recordId))
        {
            throw new InvalidOperationException(
                $"Usage: `{_settings.CommandPrefix}replayteto ls` or `{_settings.CommandPrefix}replayteto <id>`.");
        }

        var entry = await _replayLogger.GetByIdAsync(recordId, guildId);
        if (entry is null)
        {
            return $"Replay id `{recordId}` not found.";
        }

        return string.Join(
            '\n',
            $"Replay #{entry.Id}",
            $"Time: {entry.TsUtc}",
            $"Guild: {entry.GuildName ?? "?"} ({entry.GuildId?.ToString() ?? "?"})",
            $"Channel: {entry.ChannelName ?? "?"} ({entry.ChannelId})",
            $"User: {entry.UserDisplay} ({entry.UserId})",
            $"Trigger: {entry.Trigger}",
            $"Reply length: {entry.ReplyLength}",
            "Prompt:",
            entry.Prompt.Length == 0 ? "(empty)" : entry.Prompt);
    }

    private Task OnLogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _readyInitializationStarted, 1) == 1)
        {
            return Task.CompletedTask;
        }

        _ = Task.Run(InitializeAfterReadyAsync);
        return Task.CompletedTask;
    }

    private async Task InitializeAfterReadyAsync()
    {
        try
        {
            await ApplyRpcPresenceAsync();
            await RegisterSlashCommandsAsync();

            try
            {
                var appInfo = await _client.GetApplicationInfoAsync();
                _ownerUserId = appInfo.Owner?.Id ?? _ownerUserId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to resolve owner via application info: {ex.Message}");
            }

            var user = _client.CurrentUser;
            var userId = user?.Id.ToString() ?? "unknown";
            Console.WriteLine($"Logged in as {user} (ID: {userId})");
            Console.WriteLine($"Provider: {_settings.Provider}");
            Console.WriteLine($"Model: {ActiveChatModel()}");
            Console.WriteLine("Approval provider: gemini (fixed)");
            Console.WriteLine($"Approval model: {_settings.GeminiApprovalModel}");
            Console.WriteLine($"System rules JSON: {_settings.SystemRulesJson}");
            Console.WriteLine($"Chat replay log: {_settings.ChatReplayLogPath}");
            Console.WriteLine($"Chat memory DB: {_settings.ChatMemoryDbPath}");
            Console.WriteLine($"Ban DB: {_settings.BanDbPath}");
            Console.WriteLine($"Callnames DB: {_settings.CallnamesDbPath}");
            Console.WriteLine($"Memory idle TTL: {_settings.MemoryIdleTtlSeconds}s");
            Console.WriteLine($"Image max bytes: {_settings.ImageMaxBytes}");
            Console.WriteLine($"Max reply chars: {_settings.MaxReplyChars}");
            Console.WriteLine($"Bot owner ID: {_ownerUserId?.ToString() ?? "(unknown)"}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ready initialization error: {ex.Message}");
        }
    }

    private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        var handled = await _slashCommandDispatcher.TryHandleAsync(this, command);
        if (!handled)
        {
            await RespondSlashAsync(command, "This slash command is not configured in the current runtime.", ephemeral: true);
        }
    }

    private async Task OnMessageReceivedAsync(SocketMessage rawMessage)
    {
        if (rawMessage is not SocketUserMessage message)
        {
            return;
        }

        if (message.Author.IsBot || string.IsNullOrWhiteSpace(message.Content))
        {
            return;
        }

        var inlineReplayId = _commandParser.ExtractInlineReplayId(message.Content);
        if (inlineReplayId.HasValue)
        {
            var handledInline = await _commandDispatcher.TryHandleAsync(
                this,
                message,
                "replayteto",
                inlineReplayId.Value.ToString());
            if (handledInline)
            {
                return;
            }
        }

        if (_commandParser.TryParsePrefixedCommand(message.Content, out var commandName, out var args))
        {
            var handledCommand = await _commandDispatcher.TryHandleAsync(this, message, commandName, args);
            if (handledCommand)
            {
                return;
            }
        }

        var currentUser = _client.CurrentUser;
        if (currentUser is null || _terminated || await IsBannedAsync(message))
        {
            return;
        }

        if (!message.MentionedUserIds.Contains(currentUser.Id))
        {
            return;
        }

        var mentionText = message.Content
            .Replace($"<@{currentUser.Id}>", string.Empty, StringComparison.Ordinal)
            .Replace($"<@!{currentUser.Id}>", string.Empty, StringComparison.Ordinal);

        await RunChatAndReplyAsync(
            message,
            prompt: mentionText,
            fallbackPrompt: DefaultMentionPrompt,
            trigger: "mention");
    }

    private async Task<List<ImageInput>> ExtractImagesFromMessageAsync(SocketUserMessage message)
    {
        var images = new List<ImageInput>();
        foreach (var attachment in message.Attachments)
        {
            var mimeType = (attachment.ContentType ?? GuessMimeType(attachment.Filename)).ToLowerInvariant();
            if (!mimeType.StartsWith("image/", StringComparison.Ordinal))
            {
                continue;
            }

            if (attachment.Size > _settings.ImageMaxBytes)
            {
                throw new InvalidOperationException(
                    $"Image '{attachment.Filename}' exceeds the limit of {_settings.ImageMaxBytes} bytes.");
            }

            var data = await _httpClient.GetByteArrayAsync(attachment.Url);
            images.Add(new ImageInput(mimeType, Convert.ToBase64String(data)));
        }

        return images;
    }

    private static string GuessMimeType(string filename)
    {
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            _ => "application/octet-stream",
        };
    }

    private async Task<string> ApplyCallPreferencesToPromptAsync(
        string prompt,
        ulong? guildId,
        ulong userId)
    {
        if (!guildId.HasValue)
        {
            return prompt;
        }

        var (userCallsTeto, tetoCallsUser) = await _callNamesStore.GetUserCallPreferencesAsync(guildId.Value, userId);
        if (string.IsNullOrWhiteSpace(userCallsTeto) && string.IsNullOrWhiteSpace(tetoCallsUser))
        {
            return prompt;
        }

        var lines = new List<string> { "[call_profile_context]" };
        if (!string.IsNullOrWhiteSpace(userCallsTeto))
        {
            lines.Add($"user calls Teto: {userCallsTeto}");
        }

        if (!string.IsNullOrWhiteSpace(tetoCallsUser))
        {
            lines.Add($"Teto calls user: {tetoCallsUser}");
        }

        lines.Add("[message_content]");
        lines.Add(prompt);
        return string.Join('\n', lines);
    }

    private static string NormalizePrompt(string prompt, string fallback)
    {
        var value = prompt.Trim();
        return value.Length == 0 ? fallback : value;
    }

    private static string MemoryUserEntry(string prompt, int imageCount)
    {
        if (imageCount <= 0)
        {
            return prompt;
        }

        return $"{prompt}\n[attached_images={imageCount}]";
    }

    private static string? NormalizeCallName(string rawName)
    {
        var value = rawName.Trim();
        if (value.Length == 0 || value.Length > MaxCallNameLength)
        {
            return null;
        }

        return value;
    }

    private static string? GetGuildName(SocketUserMessage message)
    {
        return message.Channel is SocketGuildChannel guildChannel
            ? guildChannel.Guild.Name
            : null;
    }

    private static string? GetGuildName(SocketSlashCommand command)
    {
        return command.Channel is SocketGuildChannel guildChannel
            ? guildChannel.Guild.Name
            : null;
    }

    private static string? GetChannelName(SocketUserMessage message)
    {
        return message.Channel.Name;
    }

    private static string? GetChannelName(SocketSlashCommand command)
    {
        return command.Channel.Name;
    }

    private string ActiveChatModel()
    {
        return _settings.Provider switch
        {
            "gemini" => _settings.GeminiModel,
            "groq" => _settings.GroqModel,
            "openai" => _settings.OpenAiModel,
            _ => "unknown",
        };
    }

    private async Task ApplyRpcPresenceAsync()
    {
        if (!_settings.RpcEnabled)
        {
            Console.WriteLine("Discord RPC presence: disabled");
            return;
        }

        await _client.SetStatusAsync(ResolveRpcStatus());
        if (_settings.RpcActivityType == "none")
        {
            await _client.SetGameAsync(name: null);
        }
        else
        {
            await _client.SetGameAsync(
                name: _settings.RpcActivityName,
                streamUrl: _settings.RpcActivityUrl,
                type: ResolveRpcActivityType());
        }

        var activityName = _settings.RpcActivityType == "none"
            ? "(none)"
            : _settings.RpcActivityName;
        Console.WriteLine(
            "Discord RPC presence applied: " +
            $"status={_settings.RpcStatus}, " +
            $"type={_settings.RpcActivityType}, " +
            $"name={activityName}");
    }

    private UserStatus ResolveRpcStatus()
    {
        return _settings.RpcStatus switch
        {
            "online" => UserStatus.Online,
            "idle" => UserStatus.Idle,
            "dnd" => UserStatus.DoNotDisturb,
            "invisible" => UserStatus.Invisible,
            _ => UserStatus.Online,
        };
    }

    private ActivityType ResolveRpcActivityType()
    {
        return _settings.RpcActivityType switch
        {
            "playing" => ActivityType.Playing,
            "streaming" => ActivityType.Streaming,
            "listening" => ActivityType.Listening,
            "watching" => ActivityType.Watching,
            "competing" => ActivityType.Competing,
            _ => ActivityType.Playing,
        };
    }

    private async Task RunMemoryCleanupLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(CleanupIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await _chatMemory.PruneInactiveChannelsAsync(
                        _settings.MemoryIdleTtlSeconds,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[memory-cleanup] error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ShutdownAsync()
    {
        if (_client.LoginState == LoginState.LoggedIn)
        {
            try
            {
                await _client.StopAsync();
                await _client.LogoutAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Shutdown warning: {ex.Message}");
            }
        }
    }

    internal async Task RespondSlashAsync(SocketSlashCommand command, string text, bool ephemeral = false)
    {
        var safeText = BotTextNormalizer.SanitizeMentions(text);
        if (!command.HasResponded)
        {
            await command.RespondAsync(
                text: safeText,
                allowedMentions: AllowedMentions.None,
                ephemeral: ephemeral);
            return;
        }

        await command.FollowupAsync(
            text: safeText,
            allowedMentions: AllowedMentions.None,
            ephemeral: ephemeral);
    }

    internal async Task SendLongSlashMessageAsync(SocketSlashCommand command, string text, bool ephemeral = false)
    {
        var safeText = BotTextNormalizer.SanitizeMentions(text);
        var maxLen = Math.Min(1900, _settings.MaxReplyChars);
        if (maxLen < 1)
        {
            maxLen = 1;
        }

        var chunks = new List<string>();
        for (var i = 0; i < safeText.Length; i += maxLen)
        {
            var len = Math.Min(maxLen, safeText.Length - i);
            chunks.Add(safeText.Substring(i, len));
        }

        if (chunks.Count == 0)
        {
            chunks.Add("(no content)");
        }

        for (var idx = 0; idx < chunks.Count; idx++)
        {
            if (idx == 0 && !command.HasResponded)
            {
                await command.RespondAsync(
                    text: chunks[idx],
                    allowedMentions: AllowedMentions.None,
                    ephemeral: ephemeral);
            }
            else
            {
                await command.FollowupAsync(
                    text: chunks[idx],
                    allowedMentions: AllowedMentions.None,
                    ephemeral: ephemeral);
            }
        }
    }

    private async Task RegisterSlashCommandsAsync()
    {
        try
        {
            var existing = await _client.GetGlobalApplicationCommandsAsync();
            var existingNames = existing
                .Select(cmd => cmd.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var registrations = _slashCommandDispatcher.BuildGlobalCommandRegistrations();
            var created = new List<string>();
            var alreadyExists = new List<string>();

            foreach (var registration in registrations)
            {
                if (existingNames.Contains(registration.Name))
                {
                    alreadyExists.Add(registration.Name);
                    continue;
                }

                await _client.CreateGlobalApplicationCommandAsync(registration.Command);
                created.Add(registration.Name);
            }

            if (created.Count > 0)
            {
                var createdDisplay = string.Join(", ", created.Select(name => $"/{name}"));
                Console.WriteLine($"Registered new slash commands: {createdDisplay}");
            }

            if (alreadyExists.Count > 0)
            {
                var existsDisplay = string.Join(", ", alreadyExists.Select(name => $"/{name}"));
                Console.WriteLine($"Slash commands already exist (kept): {existsDisplay}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to register slash commands: {ex.Message}");
        }
    }
}
