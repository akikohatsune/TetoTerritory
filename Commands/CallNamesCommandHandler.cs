using Discord.WebSocket;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.Commands;

internal sealed class CallNamesCommandHandler : IDiscordCommandHandler
{
    private static readonly HashSet<string> CommandNames = new(StringComparer.Ordinal)
    {
        "ucallteto",
        "callteto",
        "tetocallu",
        "callme",
        "tetomention",
        "callprofile",
    };

    public bool CanHandle(string commandName)
    {
        return CommandNames.Contains(commandName);
    }

    public async Task HandleAsync(DiscordBot bot, SocketUserMessage message, string commandName, string args)
    {
        switch (commandName)
        {
            case "ucallteto":
            case "callteto":
                await bot.SetCallNameWithApprovalAsync(
                    message,
                    rawName: args,
                    fieldName: "user_calls_teto",
                    successMessage: "Saved: you call Teto `{0}`.",
                    saveAsync: bot.SetUserCallsTetoAsync);
                return;

            case "tetocallu":
            case "callme":
                await bot.SetCallNameWithApprovalAsync(
                    message,
                    rawName: args,
                    fieldName: "teto_calls_user",
                    successMessage: "Saved: Teto will call you `{0}`.",
                    saveAsync: bot.SetTetoCallsUserAsync);
                return;

            case "tetomention":
            case "callprofile":
                await bot.ShowCallProfileAsync(message);
                return;
        }
    }
}
