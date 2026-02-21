using Discord;
using Discord.WebSocket;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.SlashCommands;

internal sealed class TetoModelSlashCommandHandler : ISlashCommandHandler
{
    public string CommandName => "tetomodel";

    public SlashCommandBuilder BuildCommand()
    {
        return new SlashCommandBuilder()
            .WithName(CommandName)
            .WithDescription("Show current provider/model used by Teto.");
    }

    public Task HandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        return command.RespondAsync(
            text: bot.BuildModelStatusMessage(),
            ephemeral: true);
    }
}
