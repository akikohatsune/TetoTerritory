# TetoTerritory

Discord AI bot viết bằng C#, chạy đa nền tảng với `.NET 10` (Windows, Linux, macOS).

## Tinh nang chinh

- Chat da provider: `gemini`, `groq`, `openai` (`chatgpt` alias)
- Prefix commands: `!chat`, `!ask`
- Tu dong tra loi khi duoc mention
- Memory ngan han theo channel (SQLite)
- Ban control: `!ban`, `!removeban` (chi owner)
- Call-name profile:
  - `!ucallteto <name>` / `!callteto <name>`
  - `!tetocallu <name>` / `!callme <name>`
  - `!tetomention` / `!callprofile`
- Replay logs:
  - `!replayteto ls`
  - `!replayteto <id>`
  - `!replayteto<id>` (inline)
- Runtime controls: `!clearmemo`, `!resetchat`, `!terminated on|off|status`, `!provider`
- Slash command: `/tetomodel` (xem provider/model hien tai)
- RPC presence config qua env

## Yeu cau

- `.NET SDK 10`
- Discord bot token (`DISCORD_TOKEN`)
- API key theo provider:
  - Gemini: `GEMINI_API_KEY`
  - Groq: `GROQ_API_KEY`
  - OpenAI: `OPENAI_API_KEY`
- Trong Discord Developer Portal: bat `MESSAGE CONTENT INTENT`

## Cai dat

```bash
cd TetoTerritory
cp .env.example .env
cp system_rules_example.json system_rules.json
```

Neu dung PowerShell:

```powershell
Copy-Item .env.example .env
```

Sua file `.env` roi chay:

```bash
dotnet restore
dotnet run
```

## Chay test

```bash
dotnet test ../TetoTerritory.CSharp.Tests/TetoTerritory.CSharp.Tests.csproj
```

## Bien moi truong quan trong

- `COMMAND_PREFIX` (mac dinh `!`)
- `BOT_OWNER_USER_ID` (khuyen nghi set de dung owner commands on dinh)
- `LLM_PROVIDER` (`gemini|groq|openai|chatgpt`)
- `SYSTEM_PROMPT`, `SYSTEM_RULES_JSON`
- `CHAT_MEMORY_DB_PATH`, `BAN_DB_PATH`, `CALLNAMES_DB_PATH` (mac dinh trong `data/`)
- `CHAT_REPLAY_LOG_PATH`
- `RPC_ENABLED`, `RPC_STATUS`, `RPC_ACTIVITY_TYPE`, `RPC_ACTIVITY_NAME`, `RPC_ACTIVITY_URL`

## Luu y

- Neu `BOT_OWNER_USER_ID` bo trong, bot se thu tu resolve owner tu Discord application info luc startup.
- Tat ca duong dan trong `.env` duoc resolve theo working directory `TetoTerritory/`.
