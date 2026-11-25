using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DmEnhancer
{
    /// <summary>Runs `codex exec` as a plain text editor: no coding agent prompt, skills, plugins, MCP, tools or project context.</summary>
    static class Codex
    {
        // Features that add tools, skills, plugins or extra context to the request. Turned off for every run.
        static readonly string[] DisabledFeatures =
        {
            "plugins", "apps", "shell_tool", "unified_exec", "hooks", "multi_agent", "multi_agent_v2",
            "code_mode_host", "image_generation", "browser_use", "browser_use_external", "computer_use",
            "in_app_browser", "view_image", "tool_suggest", "skill_search", "skill_mcp_dependency_install",
            "goals", "sleep_tool", "memories", "send_message_to_user_async", "default_mode_request_user_input",
            "workspace_dependencies",
        };

        // Config overrides that strip the remaining injected context.
        static readonly string[] ConfigOverrides =
        {
            "project_doc_max_bytes=0",                       // no AGENTS.md
            "web_search=\"disabled\"",
            "skills.include_instructions=false",             // no skills list
            "skills.bundled.enabled=false",
            "include_permissions_instructions=false",        // no sandbox/approval blurb
            "include_environment_context=false",             // no cwd/shell/date block
            "include_apps_instructions=false",
            "include_collaboration_mode_instructions=false",
            "history.persistence=\"none\"",
        };

        public static string CodexHome
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            }
        }

        /// <summary>Prefers the native codex.exe inside the npm package (skips the Node.js wrapper: faster, less RAM).</summary>
        public static string FindExecutable(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

            var roots = new List<string>();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            roots.Add(Path.Combine(appData, "npm", "node_modules", "@openai", "codex"));
            var npmPrefix = Environment.GetEnvironmentVariable("NPM_CONFIG_PREFIX");
            if (!string.IsNullOrEmpty(npmPrefix)) roots.Add(Path.Combine(npmPrefix, "node_modules", "@openai", "codex"));

            foreach (var root in roots)
            {
                foreach (var arch in new[] { "x64", "arm64" })
                {
                    var triple = arch == "x64" ? "x86_64-pc-windows-msvc" : "aarch64-pc-windows-msvc";
                    var p = Path.Combine(root, "node_modules", "@openai", "codex-win32-" + arch, "vendor", triple, "bin", "codex.exe");
                    if (File.Exists(p)) return p;
                    p = Path.Combine(root, "vendor", triple, "codex", "codex.exe");
                    if (File.Exists(p)) return p;
                }
            }
            return Cli.FindOnPath("codex.exe", "codex.cmd");
        }

        /// <summary>Models listed in Codex's own model cache (the same ones the Codex model picker shows).</summary>
        public static List<string> ListModels()
        {
            var result = new List<string>();
            try
            {
                var file = Path.Combine(CodexHome, "models_cache.json");
                if (!File.Exists(file)) return result;
                // Light scan instead of a JSON library: each model has "slug", and "visibility" follows before the next slug.
                var json = File.ReadAllText(file, Encoding.UTF8);
                var slugs = Regex.Matches(json, "\"slug\"\\s*:\\s*\"([^\"]+)\"");
                for (int i = 0; i < slugs.Count; i++)
                {
                    int start = slugs[i].Index;
                    int end = i + 1 < slugs.Count ? slugs[i + 1].Index : json.Length;
                    var vis = Regex.Match(json.Substring(start, end - start), "\"visibility\"\\s*:\\s*\"([^\"]+)\"");
                    if (vis.Success && vis.Groups[1].Value == "hide") continue;
                    var slug = slugs[i].Groups[1].Value;
                    if (!result.Contains(slug)) result.Add(slug);
                }
            }
            catch { /* cache missing or in a new format: the combo box is still editable */ }
            return result;
        }

        /// <summary>The model from ~/.codex/config.toml, else the first listed model.</summary>
        public static string DefaultModel()
        {
            try
            {
                var file = Path.Combine(CodexHome, "config.toml");
                if (File.Exists(file))
                {
                    foreach (var line in File.ReadLines(file))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("[")) break; // only top-level keys
                        var m = Regex.Match(t, "^model\\s*=\\s*[\"']([^\"']+)[\"']");
                        if (m.Success) return m.Groups[1].Value;
                    }
                }
            }
            catch { }
            return ListModels().FirstOrDefault() ?? "gpt-5.5";
        }

        public static async Task<string> RunAsync(AppSettings s, string input, CancellationToken ct)
        {
            var exe = FindExecutable(s.CodexPath);
            if (exe == null)
                throw new Exception("Couldn't find the Codex CLI. Install it (npm i -g @openai/codex) or set its path in Settings.");

            Directory.CreateDirectory(AppSettings.WorkDir);
            var promptFile = Path.Combine(AppSettings.Dir, "run-prompt.md");
            File.WriteAllText(promptFile, s.Prompt ?? "", Cli.Utf8NoBom);
            var outFile = Path.Combine(Path.GetTempPath(), "dmenhancer-" + Guid.NewGuid().ToString("N") + ".txt");

            var args = new List<string>
            {
                "exec",
                "--ignore-user-config",   // no MCP servers, plugins, notify hooks, profiles from config.toml (auth still works)
                "--ignore-rules",
                "--ephemeral",            // don't save the session
                "--skip-git-repo-check",
                "--sandbox", "read-only",
                "--color", "never",
                "--cd", AppSettings.WorkDir,
                "--model", s.Model.Trim(),
                // Replaces Codex's coding-agent system prompt with the editor prompt.
                "-c", "model_instructions_file=" + TomlString(promptFile),
            };
            if (!string.IsNullOrWhiteSpace(s.Reasoning))
                args.AddRange(new[] { "-c", "model_reasoning_effort=" + TomlString(s.Reasoning.Trim()) });
            foreach (var c in ConfigOverrides) args.AddRange(new[] { "-c", c });
            foreach (var f in DisabledFeatures) args.AddRange(new[] { "--disable", f });
            args.AddRange(new[] { "--output-last-message", outFile, "-" }); // "-" = read the message from stdin

            try
            {
                var r = await Cli.RunAsync(exe, args, input, s.TimeoutSeconds, AppSettings.WorkDir, "Codex", ct).ConfigureAwait(false);
                var output = File.Exists(outFile) ? File.ReadAllText(outFile, Encoding.UTF8).Trim() : "";
                if (r.ExitCode != 0 || output.Length == 0)
                {
                    var msg = Cli.ErrorSummary("Codex", r);
                    if (msg.IndexOf("reasoning.effort", StringComparison.OrdinalIgnoreCase) >= 0)
                        msg = "This model doesn't support reasoning \"" + s.Reasoning + "\". Pick another level in Settings.";
                    throw new Exception(msg);
                }
                return output;
            }
            finally
            {
                try { File.Delete(outFile); } catch { }
            }
        }

        static string TomlString(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
