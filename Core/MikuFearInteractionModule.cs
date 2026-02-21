using Discord.WebSocket;

namespace TetoTerritory.CSharp.Core;

internal sealed class MikuFearInteractionModule
{
    private const ulong MikuBotUserId = 1373458132851888128;
    private const int MaxRepliesPerSession = 7;
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<ulong, MikuFearSession> _sessions = new();

    public async Task<bool> TryHandleAsync(
        DiscordBot bot,
        SocketUserMessage message,
        ulong? tetoBotUserId,
        CancellationToken cancellationToken = default)
    {
        if (!tetoBotUserId.HasValue)
        {
            return false;
        }

        if (message.Author.IsBot)
        {
            return await TryHandleMikuReplyAsync(bot, message, cancellationToken);
        }

        return await TryStartSessionAsync(message, tetoBotUserId.Value, cancellationToken);
    }

    private async Task<bool> TryStartSessionAsync(
        SocketUserMessage message,
        ulong tetoBotUserId,
        CancellationToken cancellationToken)
    {
        if (!IsDualMentionTrigger(message, tetoBotUserId))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            CleanupExpiredSessions_NoLock();

            _sessions[message.Channel.Id] = new MikuFearSession(
                TriggerUserId: message.Author.Id,
                TriggerMessageId: message.Id,
                RepliesSent: 0,
                ExpiresAtUtc: DateTimeOffset.UtcNow.Add(SessionTtl));
        }
        finally
        {
            _gate.Release();
        }

        // Consume this trigger so Teto waits for Miku's reply first.
        return true;
    }

    private async Task<bool> TryHandleMikuReplyAsync(
        DiscordBot bot,
        SocketUserMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Author.Id != MikuBotUserId)
        {
            return false;
        }

        MikuFearSession? session = null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CleanupExpiredSessions_NoLock();
            if (!_sessions.TryGetValue(message.Channel.Id, out var current))
            {
                return false;
            }

            session = current with
            {
                RepliesSent = current.RepliesSent + 1,
                ExpiresAtUtc = DateTimeOffset.UtcNow.Add(SessionTtl),
            };
            _sessions[message.Channel.Id] = session;
        }
        finally
        {
            _gate.Release();
        }

        var prompt = BuildFearPrompt(message, session!);
        await bot.RunChatAndReplyAsync(
            message,
            prompt: prompt,
            fallbackPrompt: DiscordBot.DefaultMentionPrompt,
            trigger: "hook:miku_fear");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryGetValue(message.Channel.Id, out var current) &&
                current.RepliesSent >= MaxRepliesPerSession)
            {
                _sessions.Remove(message.Channel.Id);
            }
        }
        finally
        {
            _gate.Release();
        }

        return true;
    }

    private static bool IsDualMentionTrigger(SocketUserMessage message, ulong tetoBotUserId)
    {
        if (!message.MentionedUserIds.Contains(tetoBotUserId))
        {
            return false;
        }

        return message.MentionedUserIds.Contains(MikuBotUserId);
    }

    private static string BuildFearPrompt(SocketUserMessage message, MikuFearSession session)
    {
        var mikuText = message.Content.Trim();
        if (mikuText.Length == 0)
        {
            mikuText = "(no text)";
        }

        return
            "[hidden_hook:miku_fear]\n" +
            "Context: user mentioned both Teto and Miku in the same message.\n" +
            "Behavior: Teto feels intimidated by Miku and replies after Miku.\n" +
            $"Session turn: {session.RepliesSent}/{MaxRepliesPerSession}\n" +
            $"Trigger user id: {session.TriggerUserId}\n" +
            "Keep tone playful but slightly nervous.\n" +
            "Miku message:\n" +
            mikuText;
    }

    private void CleanupExpiredSessions_NoLock()
    {
        if (_sessions.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var expiredChannels = _sessions
            .Where(kv => kv.Value.ExpiresAtUtc <= now)
            .Select(kv => kv.Key)
            .ToArray();
        foreach (var channelId in expiredChannels)
        {
            _sessions.Remove(channelId);
        }
    }

    private sealed record MikuFearSession(
        ulong TriggerUserId,
        ulong TriggerMessageId,
        int RepliesSent,
        DateTimeOffset ExpiresAtUtc);
}
