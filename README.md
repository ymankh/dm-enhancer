# DM Enhancer

<img src="assets/icon.png" width="64" align="right" alt="">

A small Windows tray app that fixes the English in your chat messages. Copy a message, press **Ctrl+Alt+E**, and about two seconds later the corrected version is pasted back in place.

The editing is done by **Claude Code** or the **Codex CLI**, both already signed in on your machine. The app runs them as plain text editors with your own prompt: no coding-agent instructions, tools, skills, plugins or MCP servers. So the reply is only the corrected message.

The app is a single 50 KB exe with no dependencies. It uses about 0.5 MB of active memory while waiting in the tray.

## Install

Download the latest release from the [Releases page](https://github.com/ymankh/dm-enhancer/releases/latest):

| File | Use it if |
|---|---|
| `DmEnhancer-Setup-<version>.exe` | You want it installed with a Start menu entry and the option to start with Windows. Installs for your user only, with no admin prompt. |
| `DmEnhancer-<version>-portable.zip` / `DmEnhancer.exe` | You just want to run it. Put the exe anywhere and double-click it. |

**Requirements:**
- Windows 10 or 11. .NET Framework 4.8 is built in, so there's nothing else to install.
- At least one of these, installed and signed in:
  - [Claude Code](https://docs.anthropic.com/en/docs/claude-code) (recommended, faster)
  - [Codex CLI](https://github.com/openai/codex)

The files aren't code-signed, so SmartScreen may warn the first time. Click **More info**, then **Run anyway**.

## Use

1. Start DM Enhancer. It sits in the system tray.
2. In your chat app, select the message you've typed and copy it. Turn on **Copy the selected text first** in Settings to skip the copy step.
3. Press **Ctrl+Alt+E**. A small "Editing…" label appears next to the mouse, and when it's done the corrected text replaces your selection.

Some details:
- Press the hotkey again while an edit is running to cancel it.
- If you switch to another window before it finishes, it won't paste. The result is still on your clipboard.
- Right-click the tray icon and choose **Edit clipboard text** to edit without pasting.

### Settings

Double-click the tray icon to open the settings.

| Setting | What it does |
|---|---|
| Provider | Claude Code or Codex |
| Model / Reasoning | Which model runs the edit, and how much it thinks. `low` is the fastest. |
| Hotkey | Click the box and press a new combination (needs Ctrl and/or Alt) |
| Paste automatically | Sends Ctrl+V after editing. Turn it off to only update the clipboard. |
| Copy the selected text first | Sends Ctrl+C before reading the clipboard |
| Straight quotes | Turns `’ “ ”` from the model into `' "` |
| Start with Windows | Adds or removes the app from your startup programs |
| CLI path | Leave it empty to find the CLI automatically |
| Prompt | The editing instructions. **Reset to default** restores the built-in prompt. |
| Test | Tries the current settings on some text before you save them |

Settings are stored in `%APPDATA%\DmEnhancer` (`settings.ini` and `prompt.md`).

## Which provider and model?

Measured with the default prompt on short chat messages (November 2026):

| Provider | Model | Time per message |
|---|---|---|
| **Claude Code** | **claude-sonnet-5-5, low** | **~2.3s (recommended default)** |
| Claude Code | claude-haiku-5-5, low | ~2.0s (sometimes misses small fixes) |
| Claude Code | claude-opus-5-5, low | 3.5–6.5s |
| Codex | gpt-5.6-luna, none | ~3.7s (fastest Codex option) |
| Codex | gpt-6.2-sol / gpt-6-sol / gpt-5.6-sol | 4–5s |

No Codex model gets much below 4s, because Codex spends 2.5–3s on its own startup for every message. The `none` reasoning level only works on some Codex models.

## Build from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) (any recent version; it builds for .NET Framework 4.8). [Inno Setup 6](https://jrsoftware.org/isinfo.php) is optional and only needed for the installer.

```powershell
./build.ps1                 # builds into dist\: the exe, a portable zip and (if Inno Setup is installed) the installer
./build.ps1 -Version 1.2.0
```

For a quick build without packaging, run `dotnet build -c Release -o bin\app`.

## Releasing

Push a version tag. The [Release workflow](.github/workflows/release.yml) builds everything on GitHub Actions and publishes a GitHub release with the installer, the portable zip and the exe.

```powershell
git tag v1.2.0
git push origin v1.2.0
```

The [CI workflow](.github/workflows/ci.yml) builds every push and pull request, and uploads the results as workflow artifacts.

## Repository layout

```
src/                     the app (C#, WinForms, .NET Framework 4.8)
  Program.cs             entry point, single-instance check
  TrayApp.cs             tray icon, hotkey → clipboard → CLI → paste flow, "Editing…" label
  SettingsForm.cs        settings window (built in code, no designer files)
  AppSettings.cs         loads and saves %APPDATA%\DmEnhancer
  Cli.cs                 starts a CLI process, sends stdin, timeouts and cancelling, picks the provider
  ClaudeCode.cs          `claude -p` flags that strip tools, skills, MCP and settings
  Codex.cs               `codex exec` flags that strip tools, skills, plugins and context
  Native.cs              Win32 calls: global hotkey, simulated Ctrl+C/V, memory trim
  DefaultPrompt.md       the built-in editor prompt (embedded in the exe)
assets/                  app icon
installer/DmEnhancer.iss Inno Setup installer script
build.ps1                build and package script (used locally and in CI)
.github/workflows/       CI and release pipelines
docs/ARCHITECTURE.md     how it works and why it's so small
CHANGELOG.md             release history
```

To learn how the app works inside, start with [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
