using Discord.WebSocket;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.Commands;

internal sealed class ChatRuntimeCommandHandler : IDiscordCommandHandler
{
    private static readonly HashSet<string> CommandNames = new(StringComparer.Ordinal)
    {
        "chat",
        "ask",
        "chat2",
        "ask2",
        "clearmemo",
        "resetchat",
        "terminated",
        "provider",
    };

    public bool CanHandle(string commandName)
    {
        return CommandNames.Contains(commandName);
    }

    public async Task HandleAsync(DiscordBot bot, SocketUserMessage message, string commandName, string args)
    {
        switch (commandName)
        {
            case "chat":
            case "ask":
                await HandleChatAsync(
                    bot,
                    message,
                    args,
                    persona: ChatPersona.Main,
                    trigger: "command");
                return;

            case "chat2":
            case "ask2":
                await HandleChatAsync(
                    bot,
                    message,
                    args,
                    persona: ChatPersona.Secondary,
                    trigger: "command:persona2");
                return;

            case "clearmemo":
            case "resetchat":
                await bot.ClearChannelMemoryAsync(message.Channel.Id);
                await bot.ReplyAsync(message, "Cleared short-term memory for this channel.");
                return;

            case "terminated":
                await HandleTerminatedAsync(bot, message, args);
                return;

            case "provider":
                await bot.ReplyAsync(message, bot.BuildProviderStatusMessage());
                return;
        }
    }

    private static async Task HandleChatAsync(
        DiscordBot bot,
        SocketUserMessage message,
        string args,
        ChatPersona persona,
        string trigger)
    {
        if (!bot.IsPersonaEnabled(persona))
        {
            await bot.ReplyAsync(message, "Persona2 is disabled. Configure PERSONA2_* settings first.");
            return;
        }

        if (await bot.IsBannedAsync(message))
        {
            await bot.ReplyAsync(message, "You are banned from using the AI bot in this server.");
            return;
        }

        if (bot.IsTerminated)
        {
            await bot.ReplyAsync(
                message,
                $"Bot is in terminated mode. Use `{bot.Settings.CommandPrefix}terminated off` to enable replies again.");
            return;
        }

        await bot.RunChatAndReplyAsync(
            message,
            prompt: args,
            fallbackPrompt: DiscordBot.DefaultPrompt,
            trigger: trigger,
            persona: persona);
    }

    private static async Task HandleTerminatedAsync(DiscordBot bot, SocketUserMessage message, string args)
    {
        var action = string.IsNullOrWhiteSpace(args) ? "on" : args.Trim().ToLowerInvariant();
        if (action is "on" or "1" or "true")
        {
            bot.IsTerminated = true;
            await bot.ReplyAsync(message, "Terminated mode enabled: bot will stop replying to chat and mentions.");
            return;
        }

        if (action is "off" or "0" or "false")
        {
            bot.IsTerminated = false;
            await bot.ReplyAsync(message, "Terminated mode disabled: bot can reply normally again.");
            return;
        }

        if (action == "status")
        {
            var status = bot.IsTerminated ? "ON" : "OFF";
            await bot.ReplyAsync(message, $"Terminated status: `{status}`");
            return;
        }

        await bot.ReplyAsync(
            message,
            $"Usage: `{bot.Settings.CommandPrefix}terminated on`, `{bot.Settings.CommandPrefix}terminated off`, or `{bot.Settings.CommandPrefix}terminated status`.");
    }
}
