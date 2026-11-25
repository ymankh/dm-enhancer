using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DmEnhancer
{
    /// <summary>Runs `claude -p` as a plain text editor: your prompt replaces the system prompt, with no tools, MCP, skills, hooks or CLAUDE.md.</summary>
    static class ClaudeCode
    {
        public static readonly string[] Models = { "claude-sonnet-5-5", "claude-haiku-5-5", "claude-opus-5-5" };
        public const string DefaultModel = "claude-sonnet-5-5";

        public static string FindExecutable(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var p in new[]
            {
                Path.Combine(appData, "npm", "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe"),
                Path.Combine(home, ".local", "bin", "claude.exe"),
            })
                if (File.Exists(p)) return p;
            return Cli.FindOnPath("claude.exe", "claude.cmd");
        }

        public static async Task<string> RunAsync(AppSettings s, string input, CancellationToken ct)
        {
            var exe = FindExecutable(s.ClaudePath);
            if (exe == null)
                throw new Exception("Couldn't find Claude Code. Install it or set its path in Settings.");

            Directory.CreateDirectory(AppSettings.WorkDir);
            var promptFile = Path.Combine(AppSettings.Dir, "run-prompt-claude.md");
            File.WriteAllText(promptFile, s.Prompt ?? "", Cli.Utf8NoBom);

            var args = new List<string>
            {
                "-p",
                "--model", s.ClaudeModel.Trim(),
                "--system-prompt-file", promptFile,   // replaces the Claude Code system prompt
                "--tools", "",                        // no built-in tools
                "--strict-mcp-config",                // no MCP servers
                "--disable-slash-commands",           // no skills or commands
                "--setting-sources", "",              // no user/project settings, hooks or CLAUDE.md
                "--no-session-persistence",
                "--output-format", "text",
            };
            if (!string.IsNullOrWhiteSpace(s.ClaudeEffort) && s.ClaudeEffort != "default")
                args.AddRange(new[] { "--effort", s.ClaudeEffort.Trim() });

            var r = await Cli.RunAsync(exe, args, input, s.TimeoutSeconds, AppSettings.WorkDir, "Claude Code", ct).ConfigureAwait(false);
            var output = (r.StdOut ?? "").Trim();
            if (r.ExitCode != 0 || output.Length == 0)
                throw new Exception(Cli.ErrorSummary("Claude Code", r));
            return output;
        }
    }
}
