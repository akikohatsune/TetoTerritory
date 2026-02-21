# TetoTerritory C# (.NET 10)

This is a C# port of the original Python bot, targeting `net10.0` for cross-platform runtime on:
- Windows
- Linux
- macOS

## Features ported

- Multi-provider chat: `gemini`, `groq`, `openai` (`chatgpt` alias)
- Prefix chat commands: `!chat`, `!ask`
- Mention auto-reply
- Short-term memory in SQLite
- Ban control commands: `!ban`, `!removeban` (owner only)
- Call-name commands: `!ucallteto`, `!tetocallu`, `!tetomention`
- Replay logger commands: `!replayteto ls`, `!replayteto <id>`, `!replayteto<id>` (owner only)
- RPC presence settings
- Terminated mode: `!terminated on|off|status`

## Setup

```bash
cd TetoTerritory.CSharp
cp .env.example .env
```

Edit `.env`, then run:

```bash
dotnet restore
dotnet run
```

Run tests:

```bash
dotnet test ../TetoTerritory.CSharp.Tests/TetoTerritory.CSharp.Tests.csproj
```

## Notes

- `BOT_OWNER_USER_ID` is optional but recommended for owner-only commands.
- If `BOT_OWNER_USER_ID` is empty, the bot attempts to resolve owner from Discord application info on startup.
- Paths in `.env` are resolved from `TetoTerritory.CSharp/` working directory.
