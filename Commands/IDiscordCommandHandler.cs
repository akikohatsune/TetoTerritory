using Discord.WebSocket;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.Commands;

internal interface IDiscordCommandHandler
{
    bool CanHandle(string commandName);
    Task HandleAsync(DiscordBot bot, SocketUserMessage message, string commandName, string args);
}
