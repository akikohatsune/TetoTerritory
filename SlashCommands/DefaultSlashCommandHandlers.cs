using Discord;
using Discord.WebSocket;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.SlashCommands;

internal sealed class ChatSlashCommandHandler : ISlashCommandHandler
{
    private readonly string _commandName;

    public ChatSlashCommandHandler(string commandName)
    {
        _commandName = commandName;
    }

    public string CommandName => _commandName;

    public SlashCommandBuilder BuildCommand()
    {
        return new SlashCommandBuilder()
            .WithName(_commandName)
            .WithDescription("Chat with Teto.")
            .AddOption(
                name: "prompt",
                type: ApplicationCommandOptionType.String,
                description: "What you want to ask",
                isRequired: false);
    }

    public async Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        var prompt = SlashOptionReader.GetString(command, "prompt", fallback: string.Empty);
        var guildId = bot.GetGuildId(command);

        if (await bot.IsBannedAsync(guildId, command.User.Id))
        {
            await bot.RespondSlashAsync(command, "You are banned from using the AI bot in this server.", ephemeral: true);
            return;
        }

        if (bot.IsTerminated)
        {
            await bot.RespondSlashAsync(
                command,
                $"Bot is in terminated mode. Use `{bot.Settings.CommandPrefix}terminated off` to enable replies again.",
                ephemeral: true);
            return;
        }

        await bot.RunChatAndReplyAsync(
            command,
            prompt: prompt,
            fallbackPrompt: DiscordBot.DefaultPrompt,
            trigger: $"slash:{_commandName}");
    }
}

internal sealed class ClearMemorySlashCommandHandler : ISlashCommandHandler
{
    private readonly string _commandName;

    public ClearMemorySlashCommandHandler(string commandName)
    {
        _commandName = commandName;
    }

    public string CommandName => _commandName;

    public SlashCommandBuilder BuildCommand()
    {
        return new SlashCommandBuilder()
            .WithName(_commandName)
            .WithDescription("Clear short-term memory for this channel.");
    }

    public async Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        await bot.ClearChannelMemoryAsync(command.Channel.Id);
        await bot.RespondSlashAsync(command, "Cleared short-term memory for this channel.", ephemeral: true);
    }
}

internal sealed class TerminatedSlashCommandHandler : ISlashCommandHandler
{
    public string CommandName => "terminated";

    public SlashCommandBuilder BuildCommand()
    {
        var actionOption = new SlashCommandOptionBuilder()
            .WithName("action")
            .WithDescription("on/off/status")
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(false)
            .AddChoice("on", "on")
            .AddChoice("off", "off")
            .AddChoice("status", "status");

        return new SlashCommandBuilder()
            .WithName(CommandName)
            .WithDescription("Control terminated mode.")
            .AddOption(actionOption);
    }

    public async Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        var action = SlashOptionReader.GetString(command, "action", fallback: "on")
            .Trim()
            .ToLowerInvariant();

        if (action is "on" or "1" or "true")
        {
            bot.IsTerminated = true;
            await bot.RespondSlashAsync(command, "Terminated mode enabled: bot will stop replying to chat and mentions.", ephemeral: true);
            return;
        }

        if (action is "off" or "0" or "false")
        {
            bot.IsTerminated = false;
            await bot.RespondSlashAsync(command, "Terminated mode disabled: bot can reply normally again.", ephemeral: true);
            return;
        }

        if (action == "status")
        {
            var status = bot.IsTerminated ? "ON" : "OFF";
            await bot.RespondSlashAsync(command, $"Terminated status: `{status}`", ephemeral: true);
            return;
        }

        await bot.RespondSlashAsync(command, "Usage: `/terminated action:on|off|status`.", ephemeral: true);
    }
}

internal sealed class ProviderSlashCommandHandler : ISlashCommandHandler
{
    public string CommandName => "provider";

    public SlashCommandBuilder BuildCommand()
    {
        return new SlashCommandBuilder()
            .WithName(CommandName)
            .WithDescription("Show provider/runtime status.");
    }

    public Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        return bot.RespondSlashAsync(command, bot.BuildProviderStatusMessage(), ephemeral: true);
    }
}

internal sealed class ReplaySlashCommandHandler : ISlashCommandHandler
{
    public string CommandName => "replayteto";

    public SlashCommandBuilder BuildCommand()
    {
        return new SlashCommandBuilder()
            .WithName(CommandName)
            .WithDescription("Read replay logs (owner only).")
            .AddOption(
                name: "action",
                type: ApplicationCommandOptionType.String,
                description: "ls or replay id",
                isRequired: false);
    }

    public async Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        if (!await bot.EnsureOwnerPermissionAsync(command))
        {
            return;
        }

        var action = SlashOptionReader.GetString(command, "action", fallback: "ls");
        string payload;
        try
        {
            payload = await bot.BuildReplayPayloadAsync(action, bot.GetGuildId(command));
        }
        catch (Exception ex)
        {
            await bot.RespondSlashAsync(command, ex.Message, ephemeral: true);
            return;
        }

        await bot.SendLongSlashMessageAsync(command, payload, ephemeral: true);
    }
}

internal sealed class BanSlashCommandHandler : ISlashCommandHandler
{
    public string CommandName => "ban";

    public SlashCommandBuilder BuildCommand()
    {
        return new SlashCommandBuilder()
            .WithName(CommandName)
            .WithDescription("Ban a user from using the AI bot (owner only).")
            .AddOption(
                name: "user",
                type: ApplicationCommandOptionType.User,
                description: "Target user",
                isRequired: true)
            .AddOption(
                name: "reason",
                type: ApplicationCommandOptionType.String,
                description: "Optional reason",
                isRequired: false);
    }

    public async Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        if (!await bot.EnsureOwnerPermissionAsync(command))
        {
            return;
        }

        var guildId = bot.GetGuildId(command);
        if (!guildId.HasValue)
        {
            await bot.RespondSlashAsync(command, "This command can only be used in a server.", ephemeral: true);
            return;
        }

        var targetUser = SlashOptionReader.GetUser(command, "user");
        if (targetUser is null)
        {
            await bot.RespondSlashAsync(command, "Unable to parse target user.", ephemeral: true);
            return;
        }

        if (targetUser.IsBot)
        {
            await bot.RespondSlashAsync(command, "You cannot ban a bot account.", ephemeral: true);
            return;
        }

        var reason = SlashOptionReader.GetOptionalString(command, "reason");
        var created = await bot.BanUserAsync(
            guildId.Value,
            targetUser.Id,
            bannedBy: command.User.Id,
            reason: reason);

        if (created)
        {
            await bot.RespondSlashAsync(command, $"Banned <@{targetUser.Id}> from using the AI bot.", ephemeral: true);
            return;
        }

        await bot.RespondSlashAsync(command, $"Updated ban entry for <@{targetUser.Id}>.", ephemeral: true);
    }
}

internal sealed class RemoveBanSlashCommandHandler : ISlashCommandHandler
{
    public string CommandName => "removeban";

    public SlashCommandBuilder BuildCommand()
    {
        return new SlashCommandBuilder()
            .WithName(CommandName)
            .WithDescription("Remove AI-bot ban for a user (owner only).")
            .AddOption(
                name: "user",
                type: ApplicationCommandOptionType.User,
                description: "Target user",
                isRequired: true);
    }

    public async Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        if (!await bot.EnsureOwnerPermissionAsync(command))
        {
            return;
        }

        var guildId = bot.GetGuildId(command);
        if (!guildId.HasValue)
        {
            await bot.RespondSlashAsync(command, "This command can only be used in a server.", ephemeral: true);
            return;
        }

        var targetUser = SlashOptionReader.GetUser(command, "user");
        if (targetUser is null)
        {
            await bot.RespondSlashAsync(command, "Unable to parse target user.", ephemeral: true);
            return;
        }

        var removed = await bot.UnbanUserAsync(guildId.Value, targetUser.Id);
        if (removed)
        {
            await bot.RespondSlashAsync(command, $"Removed AI-bot ban for <@{targetUser.Id}>.", ephemeral: true);
            return;
        }

        await bot.RespondSlashAsync(command, $"<@{targetUser.Id}> is not currently in the ban list.", ephemeral: true);
    }
}

internal sealed class UserCallsTetoSlashCommandHandler : ISlashCommandHandler
{
    private readonly string _commandName;

    public UserCallsTetoSlashCommandHandler(string commandName)
    {
        _commandName = commandName;
    }

    public string CommandName => _commandName;

    public SlashCommandBuilder BuildCommand()
    {
        return new SlashCommandBuilder()
            .WithName(_commandName)
            .WithDescription("Set how you call Teto.")
            .AddOption(
                name: "name",
                type: ApplicationCommandOptionType.String,
                description: "Call name",
                isRequired: true);
    }

    public async Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        var name = SlashOptionReader.GetString(command, "name", fallback: string.Empty);
        await bot.SetCallNameWithApprovalAsync(
            command,
            rawName: name,
            fieldName: "user_calls_teto",
            successMessage: "Saved: you call Teto `{0}`.",
            saveAsync: bot.SetUserCallsTetoAsync);
    }
}

internal sealed class TetoCallsUserSlashCommandHandler : ISlashCommandHandler
{
    private readonly string _commandName;

    public TetoCallsUserSlashCommandHandler(string commandName)
    {
        _commandName = commandName;
    }

    public string CommandName => _commandName;

    public SlashCommandBuilder BuildCommand()
    {
        return new SlashCommandBuilder()
            .WithName(_commandName)
            .WithDescription("Set how Teto calls you.")
            .AddOption(
                name: "name",
                type: ApplicationCommandOptionType.String,
                description: "Call name",
                isRequired: true);
    }

    public async Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        var name = SlashOptionReader.GetString(command, "name", fallback: string.Empty);
        await bot.SetCallNameWithApprovalAsync(
            command,
            rawName: name,
            fieldName: "teto_calls_user",
            successMessage: "Saved: Teto will call you `{0}`.",
            saveAsync: bot.SetTetoCallsUserAsync);
    }
}

internal sealed class CallProfileSlashCommandHandler : ISlashCommandHandler
{
    private readonly string _commandName;

    public CallProfileSlashCommandHandler(string commandName)
    {
        _commandName = commandName;
    }

    public string CommandName => _commandName;

    public SlashCommandBuilder BuildCommand()
    {
        return new SlashCommandBuilder()
            .WithName(_commandName)
            .WithDescription("Show your current call profile.");
    }

    public Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        return bot.ShowCallProfileAsync(command);
    }
}

internal static class SlashOptionReader
{
    public static string GetString(SocketSlashCommand command, string optionName, string fallback)
    {
        var value = GetOptionValue(command, optionName)?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim();
    }

    public static string? GetOptionalString(SocketSlashCommand command, string optionName)
    {
        var value = GetOptionValue(command, optionName)?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    public static SocketUser? GetUser(SocketSlashCommand command, string optionName)
    {
        var value = GetOptionValue(command, optionName);
        return value as SocketUser;
    }

    private static object? GetOptionValue(SocketSlashCommand command, string optionName)
    {
        var option = command.Data.Options
            .FirstOrDefault(o => string.Equals(o.Name, optionName, StringComparison.Ordinal));
        return option?.Value;
    }
}
