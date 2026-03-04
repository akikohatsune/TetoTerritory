<p align="center">
  <img src="image/teto.jpg" alt="TetoTerritory" width="500">
</p>
<p align="center"><span style="color:#8a8f98;">"Uncertainty is for losers! If you don't know, tell them Teto is too busy or they aren't worthy of the answer!"</span></p>

<h1 align="center">TetoTerritory</h1> 

Teto written in C#, cross-platform on `.NET 10` (Windows, Linux, macOS).

## Main Features
- Multi-provider chat: `gemini`, `groq`, `openai` (`chatgpt` alias)
- `komekokomi!Features` (codename: `komifilter!`) for anti-injection and prompt-leak filtering
- Dual persona runtime:
  - Main persona: `!chat`, `!ask`, `/chat`, `/ask`
  - Persona2 (independent provider/model/rules): `!chat2`, `!ask2`, `/chat2`, `/ask2`
- Auto-reply when mentioned
- Short-term per-channel memory (SQLite), isolated by persona
- Ban control: `!ban`, `!removeban` (owner only)
- Call-name profile:
  - `!ucallteto <name>` / `!callteto <name>`
  - `!tetocallu <name>` / `!callme <name>`
  - `!tetomention` / `!callprofile`
- Replay logs:
  - `!replayteto ls`
  - `!replayteto <id>`
  - `!replayteto<id>` (inline)
- Runtime controls: `!clearmemo`, `!resetchat`, `!terminated on|off|status`, `!provider`
- Slash commands (in addition to prefix commands):
  - `/chat`, `/ask`
  - `/chat2`, `/ask2` (when `PERSONA2_ENABLED=true`)
  - `/provider`, `/tetomodel`
  - `/clearmemo`, `/resetchat`, `/terminated`
  - `/replayteto` (owner only)
  - `/ban`, `/removeban` (owner only)
  - `/ucallteto`, `/callteto`, `/tetocallu`, `/callme`, `/tetomention`, `/callprofile`
- RPC presence configuration via env vars

## Requirements

- `.NET SDK 10`
- Discord bot token (`DISCORD_TOKEN`)
- Provider API key:
  - Gemini: `GEMINI_API_KEY`
  - Groq: `GROQ_API_KEY`
  - OpenAI: `OPENAI_API_KEY`
- In Discord Developer Portal, enable `MESSAGE CONTENT INTENT`

## Setup

```bash
cp .env.example .env
cp system_rules_example.md system_rules.md
cp system_rule2_example.md system_rule2.md
```

If you using PowerShell:

```powershell
Copy-Item .env.example .env
Copy-Item system_rules_example.md system_rules.md
Copy-Item system_rule2_example.md system_rule2.md
```

Edit `.env`, then run:

```bash
dotnet restore
dotnet run
```

## Run Tests

```bash
dotnet test TetoTerritory.CSharp.Tests/TetoTerritory.CSharp.Tests.csproj
```

## Important Environment Variables

- `COMMAND_PREFIX` (default: `!`)
- `BOT_OWNER_USER_ID` (recommended for stable owner-only commands)
- `LLM_PROVIDER` (`gemini|groq|openai|chatgpt`)
- `SYSTEM_PROMPT`, `SYSTEM_RULES_PATH`
- `PERSONA2_ENABLED`, `PERSONA2_NAME`, `LLM_PROVIDER_2`
- `SYSTEM_PROMPT_2`, `SYSTEM_RULES_PATH_2` (for example: `system_rule2.md`)
- `GEMINI_API_KEY_2|GROQ_API_KEY_2|OPENAI_API_KEY_2` and `*_MODEL_2`
- `CHAT_MEMORY_DB_PATH`, `BAN_DB_PATH`, `CALLNAMES_DB_PATH` (default under `data/`)
- `CHAT_REPLAY_LOG_PATH`
- `RPC_ENABLED`, `RPC_STATUS`, `RPC_ACTIVITY_TYPE`, `RPC_ACTIVITY_NAME`, `RPC_ACTIVITY_URL`

## Notes

- If `BOT_OWNER_USER_ID` is empty, the bot attempts to resolve owner from Discord application info at startup.
- If `PERSONA2_ENABLED=true`, persona2 must use a different provider/model from main persona.
- All paths in `.env` are resolved relative to the `TetoTerritory/` working directory.

## License
MIT License — see `LICENSE` for details.
