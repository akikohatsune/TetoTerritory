using Discord.WebSocket;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.Commands;

internal sealed class ReplayCommandHandler : IDiscordCommandHandler
{
    public bool CanHandle(string commandName)
    {
        return string.Equals(commandName, "replayteto", StringComparison.Ordinal);
    }

    public async Task HandleAsync(DiscordBot bot, SocketUserMessage message, string commandName, string args)
    {
        if (!bot.IsOwner(message.Author))
        {
            await bot.ReplyAsync(message, "Only the bot owner can use this command.");
            return;
        }

        var action = string.IsNullOrWhiteSpace(args) ? "ls" : args.Trim();
        string payload;
        try
        {
            payload = await bot.BuildReplayPayloadAsync(action, bot.GetGuildId(message));
        }
        catch (Exception ex)
        {
            await bot.ReplyAsync(message, ex.Message);
            return;
        }

        await bot.SendLongMessageAsync(message, payload);
    }
}
