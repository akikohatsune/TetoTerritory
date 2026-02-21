using Discord;
using Discord.WebSocket;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.SlashCommands;

internal interface ISlashCommandHandler
{
    string CommandName { get; }
    SlashCommandBuilder BuildCommand();
    Task HandleAsync(DiscordBot bot, SocketSlashCommand command);
}
