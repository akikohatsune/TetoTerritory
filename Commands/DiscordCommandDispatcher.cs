using Discord.WebSocket;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.Commands;

internal sealed class DiscordCommandDispatcher
{
    private readonly IReadOnlyList<IDiscordCommandHandler> _handlers;

    public DiscordCommandDispatcher(IEnumerable<IDiscordCommandHandler> handlers)
    {
        _handlers = handlers.ToList();
    }

    public async Task<bool> TryHandleAsync(DiscordBot bot, SocketUserMessage message, string commandName, string args)
    {
        foreach (var handler in _handlers)
        {
            if (!handler.CanHandle(commandName))
            {
                continue;
            }

            await handler.HandleAsync(bot, message, commandName, args);
            return true;
        }

        return false;
    }
}
