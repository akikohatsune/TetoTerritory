using Discord;
using Discord.WebSocket;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.SlashCommands;

internal sealed class SlashCommandDispatcher
{
    private readonly IReadOnlyDictionary<string, ISlashCommandHandler> _handlers;

    public SlashCommandDispatcher(IEnumerable<ISlashCommandHandler> handlers)
    {
        _handlers = handlers.ToDictionary(
            h => h.CommandName,
            StringComparer.OrdinalIgnoreCase);
    }

    public ApplicationCommandProperties[] BuildGlobalCommands()
    {
        return _handlers.Values
            .Select(h => h.BuildCommand().Build())
            .Cast<ApplicationCommandProperties>()
            .ToArray();
    }

    public IReadOnlyList<string> GetCommandNames()
    {
        return _handlers.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<bool> TryHandleAsync(DiscordBot bot, SocketSlashCommand command)
    {
        if (!_handlers.TryGetValue(command.Data.Name, out var handler))
        {
            return false;
        }

        await handler.HandleAsync(bot, command);
        return true;
    }
}
