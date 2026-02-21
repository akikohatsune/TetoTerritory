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
        var embed = new EmbedBuilder()
            .WithTitle("Teto Model")
            .WithDescription($"Current model: `{bot.CurrentModelName()}`")
            .WithColor(Color.Magenta)
            .Build();

        return command.RespondAsync(
            embed: embed,
            ephemeral: true);
    }
}
