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

---
*Fixed by Gemini CLI*
