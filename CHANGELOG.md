# Changelog

## 1.1.0 – 2026-11-25

- **Added the Antigravity CLI (`agy`) as a third provider**, locked down as far as agy allows:
  - a custom editor agent with your prompt and an empty tool list (the model sees 1 tool instead of 60)
  - session-only settings that deny every command, file, URL and MCP action, use strict approvals, turn on the terminal sandbox and turn off access outside the workspace
  - an empty workspace, with the message sent on stdin
  - the JSON event stream is checked, and the result is thrown away if agy used any tool or the lockdown wasn't applied
  - each agy conversation is deleted after the edit (on by default)
- Picking agy shows a security warning the first time. The settings window shows a safety note, including any global agy rules, plugins or hooks that would still load.
- New [docs/SECURITY.md](docs/SECURITY.md): how each provider is locked down, test results, and known limits

## 1.0.0 – 2026-11-25

First release.

- Tray app with a global hotkey (Ctrl+Alt+E by default) that edits the clipboard text and pastes the result back
- Claude Code provider (default; about 2s per message) and Codex provider (4–5s)
- Each CLI runs as a plain text editor: your prompt replaces the agent system prompt, and tools, skills, plugins, MCP servers, hooks and project context are turned off
- Settings window: provider, model, reasoning effort, hotkey, prompt editor with reset and a Test box, auto-paste, copy-selection-first, straight quotes, start with Windows
- Cancel a running edit by pressing the hotkey again. The app won't paste if you've switched windows in the meantime.
- Installer (per user, no admin), portable zip and single-file exe, built by GitHub Actions
