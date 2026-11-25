# Changelog

## 1.0.0 – 2026-11-25

First release.

- Tray app with a global hotkey (Ctrl+Alt+E by default) that edits the clipboard text and pastes the result back
- Claude Code provider (default; about 2s per message) and Codex provider (4–5s)
- Each CLI runs as a plain text editor: your prompt replaces the agent system prompt, and tools, skills, plugins, MCP servers, hooks and project context are turned off
- Settings window: provider, model, reasoning effort, hotkey, prompt editor with reset and a Test box, auto-paste, copy-selection-first, straight quotes, start with Windows
- Cancel a running edit by pressing the hotkey again. The app won't paste if you've switched windows in the meantime.
- Installer (per user, no admin), portable zip and single-file exe, built by GitHub Actions
