# Architecture

DM Enhancer is a small tray app that connects your clipboard to an AI CLI. It has no AI code of its own. For each message it starts `claude.exe` or `codex.exe` as a separate process, passes the text in, and pastes the answer back.

## Stack

| Layer | Choice | Why |
|---|---|---|
| Language | C# (latest language version) | Easy access to Win32 calls, small output |
| Runtime | .NET Framework 4.8 | Built into Windows 10 and 11, so there's nothing to install or ship |
| UI | WinForms | A thin layer over native Windows controls. No browser engine. |
| Build | .NET SDK project targeting `net48` | Modern tooling for an old, always-present runtime |
| Installer | Inno Setup 6 | Per-user install, no admin prompt, small setup file |
| CI/CD | GitHub Actions on `windows-latest` | Builds every push; tags publish releases |
| AI | Claude Code (`claude -p`), Codex (`codex exec`) or Antigravity (`agy`) | Already installed and signed in on your machine |

The app has no NuGet packages. Everything it uses ships with Windows.

## What happens when you press the hotkey

```
Windows sends WM_HOTKEY to the app's hidden message-only window
  │
  ├─ optional: wait for Ctrl/Alt to be released, send Ctrl+C, wait for the clipboard to change
  ├─ read the clipboard text (retries if another app has the clipboard locked)
  ├─ show the "Editing…" label (a window that never takes focus)
  │
  ├─ start the CLI with the prompt and the message:
  │     claude.exe -p --system-prompt-file prompt.md --tools "" --strict-mcp-config
  │                   --disable-slash-commands --setting-sources "" --no-session-persistence
  │     (the message goes in on stdin, the answer comes back on stdout)
  │   the CLI exits as soon as it has answered, and its memory is freed
  │
  ├─ optionally turn curly quotes into straight ones, then put the result on the clipboard
  ├─ if the same window still has focus: send Ctrl+V
  └─ trim the app's memory and go back to waiting
```

Pressing the hotkey again during an edit kills the CLI process. Each edit also has a 60-second timeout.

## Keeping the AI output clean

Both CLIs are coding agents by default. They add a long system prompt, tool definitions, skills, plugins, MCP servers, project files and environment details. The app turns off as much of that as each CLI allows, so the model acts as a plain editor.

**Claude Code** (`src/ClaudeCode.cs`):
- `--system-prompt-file` replaces the whole Claude Code system prompt with your prompt.
- `--tools ""` turns off all built-in tools.
- `--strict-mcp-config` drops all MCP servers.
- `--disable-slash-commands` turns off skills and commands.
- `--setting-sources ""` skips user and project settings, which means no hooks and no CLAUDE.md.
- `--no-session-persistence` means nothing is saved.

Claude Code still adds a short environment note (working folder, OS, date).

**Codex** (`src/Codex.cs`):
- `model_instructions_file` replaces Codex's coding-agent instructions.
- `--ignore-user-config` skips `config.toml`, which means no MCP servers, plugins or notify hooks. Sign-in still works.
- `skills.include_instructions=false`, `skills.bundled.enabled=false` and `project_doc_max_bytes=0` mean no skills list and no AGENTS.md.
- `include_environment_context`, `include_permissions_instructions`, `include_apps_instructions` and `include_collaboration_mode_instructions` are all set to `false`.
- About 20 `--disable <feature>` flags turn off the shell, plugins, apps, browser and computer use, image generation, multi-agent, memories, goals and similar tools.
- `--ephemeral`, `history.persistence="none"` and `--sandbox read-only` mean nothing is saved or changed.

Some Codex models still declare a few built-in tools that can't be turned off with settings. They're disabled and unused.

Both CLIs run in an empty folder (`%APPDATA%\DmEnhancer\work`), so no project files get picked up.

**Antigravity (agy)** (`src/Antigravity.cs`) can't be stripped down with flags alone. The app:
- runs a custom no-tools agent with your prompt,
- applies session-only deny-all permissions and the terminal sandbox,
- sends the message on stdin as a `stream-json` event,
- reads the JSON event stream and throws the result away if any tool was used or the lockdown wasn't applied,
- deletes the conversation files agy saves for each run.

[SECURITY.md](SECURITY.md) has the details, test results and known limits.

## Why it's so small

**The exe is 50 KB:**
1. The runtime is already on every Windows PC. .NET Framework 4.8 lives in `C:\Windows\Microsoft.NET`, and every .NET program shares its libraries. The exe contains only the app's own code, as compact bytecode. For comparison, an Electron app ships Chromium and Node.js (150 MB or more), and a self-contained modern .NET app ships its runtime (around 70 MB).
2. There are no package dependencies. Even JSON parsing (for the Codex model list) is replaced with a few lines of regex.
3. There are no asset files at runtime. The tray icons are drawn in code, the default prompt is embedded in the exe, and the windows are built in code.
4. The AI lives in the CLI you already have, not in the app.

**About 0.5 MB of active memory while idle:**
1. The UI is native. WinForms controls are standard Windows controls that Windows draws itself.
2. The app does nothing while idle. It doesn't poll or run timers; it waits for Windows to send it a hotkey message, so it uses 0% CPU.
3. The AI process only lives for the few seconds of an edit. It's started for each message and exits right after, so nothing heavy stays in memory.
4. The settings window is created only when you open it and thrown away when you close it.
5. After startup and after each edit, the app tells Windows to page out memory it isn't using (`SetProcessWorkingSetSize(-1, -1)`).

To be precise about the numbers: Task Manager shows about **0.5 MB** because idle memory has been handed back to Windows. The committed memory is about **23 MB**, mostly the .NET runtime, and Windows pages it back in when you press the hotkey.

## Why Claude Code is faster than Codex

With debug logging on, Codex spends 2.5–3s before the model starts answering. It fetches the model list (~0.5s), warms up a WebSocket connection (~0.8s) and builds session state (0.4–1.8s). This happens on every message, because each edit starts a new process. Claude Code's startup is much lighter, so the whole round trip takes about 2s.

## Source files

| File | Responsibility |
|---|---|
| `Program.cs` | Entry point. Makes sure only one copy runs (named mutex), then starts the tray app. |
| `TrayApp.cs` | Tray icon and menu, the hotkey → edit → paste flow, cancelling, the "Editing…" label, drawing the icons. |
| `SettingsForm.cs` | Settings window built in code: provider and model pickers, hotkey capture, prompt editor, the Test box, the startup registry entry. |
| `AppSettings.cs` | Reads and writes `settings.ini` and `prompt.md`, chooses the default provider. |
| `Cli.cs` | Starts a CLI process (stdin and stdout in UTF-8), handles timeouts and cancelling, Windows argument quoting, picks the provider (`Editor`). |
| `ClaudeCode.cs` | Finds `claude.exe` and builds its arguments. |
| `Codex.cs` | Finds the native `codex.exe` (skipping the Node wrapper), reads the model list from Codex's cache, builds its arguments. |
| `Antigravity.cs` | Finds `agy.exe`, writes the no-tools agent and session settings, checks the JSON event stream, deletes the conversation files, lists global agy files that still load. |
| `Native.cs` | `RegisterHotKey` on a message-only window, `keybd_event` for Ctrl+C and Ctrl+V, clipboard retries, memory trimming. |
| `DefaultPrompt.md` | The built-in editor prompt, embedded as a resource. |
