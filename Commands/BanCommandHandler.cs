using Discord.WebSocket;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.Commands;

internal sealed class BanCommandHandler : IDiscordCommandHandler
{
    private static readonly HashSet<string> CommandNames = new(StringComparer.Ordinal)
    {
        "ban",
        "removeban",
    };

    public bool CanHandle(string commandName)
    {
        return CommandNames.Contains(commandName);
    }

    public async Task HandleAsync(DiscordBot bot, SocketUserMessage message, string commandName, string args)
    {
        switch (commandName)
        {
            case "ban":
                await HandleBanAsync(bot, message, args);
                return;

            case "removeban":
                await HandleRemoveBanAsync(bot, message, args);
                return;
        }
    }

    private static async Task HandleBanAsync(DiscordBot bot, SocketUserMessage message, string args)
    {
        if (!await bot.EnsureOwnerPermissionAsync(message))
        {
            return;
        }

        var guildId = bot.GetGuildId(message);
        if (!guildId.HasValue)
        {
            await bot.ReplyAsync(message, "This command can only be used in a server.");
            return;
        }

        if (!CommandParser.TryParseFirstToken(args, out var targetToken, out var reason))
        {
            await bot.ReplyAsync(message, $"Usage: `{bot.Settings.CommandPrefix}ban @user [reason]`");
            return;
        }

        var userId = CommandParser.ExtractUserId(
            targetToken,
            message.MentionedUsers.Select(u => u.Id));
        if (!userId.HasValue)
        {
            await bot.ReplyAsync(message, "Unable to parse target user.");
            return;
        }

        if (message.Channel is SocketGuildChannel guildChannel)
        {
            var guildUser = guildChannel.Guild.GetUser(userId.Value);
            if (guildUser?.IsBot == true)
            {
                await bot.ReplyAsync(message, "You cannot ban a bot account.");
                return;
            }
        }

        var created = await bot.BanUserAsync(
            guildId.Value,
            userId.Value,
            bannedBy: message.Author.Id,
            reason: reason);

        if (created)
        {
            await bot.ReplyAsync(message, $"Banned <@{userId.Value}> from using the AI bot.");
            return;
        }

        await bot.ReplyAsync(message, $"Updated ban entry for <@{userId.Value}>.");
    }

    private static async Task HandleRemoveBanAsync(DiscordBot bot, SocketUserMessage message, string args)
    {
        if (!await bot.EnsureOwnerPermissionAsync(message))
        {
            return;
        }

        var guildId = bot.GetGuildId(message);
        if (!guildId.HasValue)
        {
            await bot.ReplyAsync(message, "This command can only be used in a server.");
            return;
        }

        if (!CommandParser.TryParseFirstToken(args, out var targetToken, out _))
        {
            await bot.ReplyAsync(message, $"Usage: `{bot.Settings.CommandPrefix}removeban @user`");
            return;
        }

        var userId = CommandParser.ExtractUserId(
            targetToken,
            message.MentionedUsers.Select(u => u.Id));
        if (!userId.HasValue)
        {
            await bot.ReplyAsync(message, "Unable to parse target user.");
            return;
        }

        var removed = await bot.UnbanUserAsync(guildId.Value, userId.Value);
        if (removed)
        {
            await bot.ReplyAsync(message, $"Removed AI-bot ban for <@{userId.Value}>.");
            return;
        }

        await bot.ReplyAsync(message, $"<@{userId.Value}> is not currently in the ban list.");
    }
}
