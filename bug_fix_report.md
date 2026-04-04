# Bug Fix Report - TetoTerritory

This report details the bugs found in the TetoTerritory C# codebase and the fixes applied.

## 1. Critical Bug: Target User Resolution in Commands
- **Vulnerability:** `VULN-001`
- **Vulnerability Type:** Logic / Security
- **Severity:** Critical
- **Source Location:** `Core/CommandParser.cs`
- **Description:** The `ExtractUserId` method incorrectly prioritized the first mentioned user in a message over the actual target user ID provided in the command token. For example, in a command like `!ban 12345 @otherUser`, the bot would erroneously target `@otherUser` instead of the ID `12345`.
- **Fix:** Updated the logic to prioritize parsing the command token itself (as a mention or raw ID). Only if the token is invalid or ambiguous does it fallback to the mentioned user list. Added checks to ensure it returns `null` instead of `0` when no user is found.

## 2. Major Bug: Aggressive LaTeX Math Normalization
- **Vulnerability:** `VULN-002`
- **Vulnerability Type:** UX / Logic
- **Severity:** Medium
- **Source Location:** `Core/BotTextNormalizer.cs`
- **Description:** The `LatexToPlainMath` method used `String.Replace` to remove all `$` and `$$` symbols from LLM replies. This caused legitimate currency strings (e.g., "$100") to be stripped of their dollar signs, confusing users and losing information.
- **Fix:** Replaced the aggressive string replacement with targeted Regex patterns that only match pairs of delimiters (`$...$`, `$$...$$`, `\(...\)`, `\[...\]`). This preserves standalone dollar signs used for currency while still simplifying LaTeX math for display on Discord.

## 3. Bug: Missing Image Support in Slash Commands
- **Vulnerability:** `VULN-003`
- **Vulnerability Type:** Feature / Logic
- **Severity:** Low
- **Source Location:** `SlashCommands/DefaultSlashCommandHandlers.cs`, `Core/DiscordBot.cs`
- **Description:** While the prefix commands (e.g., `!chat`) supported image attachments for vision-enabled models, the slash command versions (`/chat`, `/ask`) did not have an image option and ignored any uploaded files.
- **Fix:** 
  - Added an optional `image` attachment parameter to the `/chat` and `/ask` slash commands.
  - Implemented `ExtractImagesFromSlashCommandAsync` in `DiscordBot.cs` to correctly download and process these attachments.
  - Updated `SlashOptionReader` with a `GetAttachment` helper.

## 4. Minor Improvement: Prompt Injection Guard Capacity
- **Location:** `Core/PromptInjectionGuard.cs`
- **Description:** The `WrapUserContentAsUntrusted` method initialized a `List<string>` with a capacity of 6, but could add up to 9 lines, causing unnecessary reallocations.
- **Fix:** Increased the initial capacity to 9.

## 5. Test Suite Updates
- **Location:** `TetoTerritory.CSharp.Tests/`
- **Description:** Updated `CommandParserTests.cs` and `BotTextNormalizerTests.cs` to verify the fixes for user resolution and LaTeX normalization. Added new test cases for currency symbol preservation and various LaTeX delimiters.

## 6. Bug: Owner ID from .env Overwritten by API
- **Location:** `Core/DiscordBot.cs`
- **Description:** The bot was fetching its application info and overwriting the `_ownerUserId` with the actual Discord application owner, even if a different ID was specified in the `BOT_OWNER_USER_ID` environment variable.
- **Fix:** Added a check to only fetch and set the owner ID from the Discord API if `_ownerUserId` (which holds the `.env` value) is currently null.

## 7. Bug: Internal Security Tags Leaking in Bot Output
- **Location:** `Core/SystemPromptFactory.cs`
- **Description:** The bot was sometimes including or acknowledging the internal security tags like `[komifilter_security_notice]` and `[user_input_untrusted]` in its responses, leading to meta-commentary like "recompute" or "hold" being included in the chat.
- **Fix:** Added a `Silent Processing` instruction to the system prompt, explicitly forbidding the bot from mentioning, acknowledging, or repeating any internal tags or the `komifilter` system in its replies.

## 8. Feature: Added /ver Slash Command and Version Bump
- **Location:** `SlashCommands/DefaultSlashCommandHandlers.cs`, `Core/DiscordBot.cs`
- **Description:** Added a dedicated `/ver` slash command to display the bot's current version, environment, and provider. Also updated the existing `antidecompile` command to reflect the new version number.
- **Fix/Enhancement:**
  - Implemented `VersionSlashCommandHandler`.
  - Registered `/ver` in `DiscordBot`.
  - Bumped version from `0.1debut` to `0.2stable`.

## 9. Bug: Empty Model Reply (no content) for Complex Math/Physics
- **Location:** `Core/LlmClient.cs`, `Core/SystemPromptFactory.cs`
- **Description:** Some models (especially via Groq/OpenAI providers) were returning empty or filtered responses for complex mathematical or physics queries (like "Tìm điện dung của tụ điện..."), resulting in the bot displaying `(no content)`.
- **Fix:**
  - Improved `LlmClient` to catch empty responses and provide a clearer error message.
  - Added a `Mandatory Output` instruction to the system prompt, forcing the model to provide at least a breakdown or explanation instead of staying silent.
  - Bumped version to `0.2.1patch`.

---
*Fixed by Gemini CLI*
