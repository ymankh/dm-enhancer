using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace DmEnhancer
{
    /// <summary>
    /// Runs the Antigravity CLI (`agy`) as a text editor. agy is a full coding agent with no "plain model" mode,
    /// so the app wraps it in several layers of protection:
    ///  1. A custom agent (`--agent dm-editor`) whose system prompt is your editor prompt and whose tool list is empty.
    ///  2. Session-only settings (`--settings`, never saved) that deny every command, file, URL and MCP action,
    ///     use the strictest approval mode, turn on the terminal sandbox and turn off access outside the workspace.
    ///  3. An empty workspace folder, so no project files or workspace rules are in reach.
    ///  4. The message goes in on stdin (not the command line, where other programs could read it).
    ///  5. The JSON event stream is checked: if the safety settings weren't applied, or agy tried to use any tool,
    ///     the result is thrown away.
    ///  6. Optionally, the conversation files agy saves for each run are deleted afterwards.
    /// </summary>
    static class Antigravity
    {
        public static readonly string[] Models =
        {
            "gemini-4-hydrogen-low", "gemini-3.8-flash-low", "gemini-4-argon-low", "gemini-4-carbon-low",
            "gemini-4-neon-low", "claude-sonnet-5-5-low", "gpt-oss-120b-medium",
        };
        public const string DefaultModel = "gemini-4-hydrogen-low";
        const string AgentName = "dm-editor";

        // Everything the agent could do through a tool. Deny always wins over allow in agy, and applies in every mode.
        static readonly string[] DenyAll =
        {
            "command(*)", "unsandboxed(*)", "write_file(*)", "read_file(*)",
            "read_url(*)", "execute_url(*)", "download_url(*)", "mcp(*)",
        };

        static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static string DataDir => Path.Combine(Home, ".gemini", "antigravity-cli");
        static string WorkDir => Path.Combine(AppSettings.Dir, "agy-work");

        public static string FindExecutable(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
            if (File.Exists(p)) return p;
            return Cli.FindOnPath("agy.exe");
        }

        /// <summary>Global agy files that get loaded into every session and can't be switched off from the command line.</summary>
        public static List<string> GlobalExtras()
        {
            var found = new List<string>();
            var gemini = Path.Combine(Home, ".gemini");
            Action<string> file = rel =>
            {
                var f = Path.Combine(gemini, rel);
                try { if (File.Exists(f) && new FileInfo(f).Length > 0) found.Add("~/.gemini/" + rel.Replace('\\', '/')); } catch { }
            };
            Action<string> dir = rel =>
            {
                var d = Path.Combine(gemini, rel);
                try { if (Directory.Exists(d) && Directory.EnumerateFileSystemEntries(d).Any()) found.Add("~/.gemini/" + rel.Replace('\\', '/') + "/"); } catch { }
            };
            file("AGENTS.md"); file("GEMINI.md"); file(@"config\AGENTS.md"); file(@"config\GEMINI.md");
            dir(@"config\rules"); dir(@"config\plugins"); file(@"config\hooks.json");
            return found;
        }

        public static async Task<string> RunAsync(AppSettings s, string input, CancellationToken ct)
        {
            var exe = FindExecutable(s.AgyPath);
            if (exe == null)
                throw new Exception("Couldn't find the Antigravity CLI (agy). Install it or set its path in Settings.");

            // 1 + 3: an empty workspace that holds only the no-tools agent.
            var agentDir = Path.Combine(WorkDir, ".agents", "agents");
            Directory.CreateDirectory(agentDir);
            var agent = new StringBuilder()
                .Append("---\n")
                .Append("name: ").Append(AgentName).Append('\n')
                .Append("description: Edits chat messages. Text only, no tools.\n")
                .Append("tools: []\n")
                .Append("mainAgent: true\n")
                .Append("subagent: false\n")
                .Append("commandExecutionPolicy: off\n")
                .Append("---\n\n")
                .Append(s.Prompt ?? "")
                .ToString();
            File.WriteAllText(Path.Combine(agentDir, AgentName + ".md"), agent, Cli.Utf8NoBom);

            // 2: settings for this session only.
            var settingsFile = Path.Combine(AppSettings.Dir, "agy-session-settings.json");
            var settingsJson =
                "{\n" +
                "  \"toolPermission\": \"strict\",\n" +
                "  \"enableTerminalSandbox\": true,\n" +
                "  \"allowNonWorkspaceAccess\": false,\n" +
                "  \"disableSlashCommands\": true,\n" +
                "  \"permissions\": {\n" +
                "    \"allow\": [],\n" +
                "    \"ask\": [],\n" +
                "    \"deny\": [" + string.Join(", ", DenyAll.Select(JsonString)) + "]\n" +
                "  }\n" +
                "}\n";
            File.WriteAllText(settingsFile, settingsJson, Cli.Utf8NoBom);

            var args = new List<string>
            {
                "--agent", AgentName,
                "--settings", settingsFile,
                "--model", s.AgyModel.Trim(),
                "--disable-slash-commands",
                "--input-format", "stream-json",
                "--output-format", "stream-json",
            };
            if (!string.IsNullOrWhiteSpace(s.AgyEffort) && s.AgyEffort != "default")
                args.AddRange(new[] { "--effort", s.AgyEffort.Trim() });

            // 4: the message goes in on stdin as one stream-json "user" event.
            var stdin = "{\"event\":\"user\",\"message\":{\"content\":" + JsonString(input) + "}}\n";

            string conversationId = null;
            try
            {
                var r = await Cli.RunAsync(exe, args, stdin, s.TimeoutSeconds, WorkDir, "agy", ct).ConfigureAwait(false);
                var check = Inspect(r.StdOut);
                conversationId = check.ConversationId;

                // 5: refuse anything that doesn't look exactly like a locked-down, tool-free run.
                if (check.ToolUsed != null)
                    throw new Exception("Safety check: agy tried to use the tool \"" + check.ToolUsed + "\". The result was thrown away.");
                if (check.InitSeen && check.Agent != AgentName)
                    throw new Exception("Safety check: agy didn't load the no-tools editor agent. The result was thrown away.");
                if (check.InitSeen && check.PermissionMode != "strict")
                    throw new Exception("Safety check: agy didn't apply the strict permission settings (mode: " + (check.PermissionMode ?? "unknown") + "). The result was thrown away.");
                if (!check.InitSeen)
                    throw new Exception(Cli.ErrorSummary("agy", r));
                if (Mentions(r.StdErr, "denied"))
                    throw new Exception("Safety check: agy asked for a permission that was denied. The result was thrown away.");
                if (check.Status != "SUCCESS" || string.IsNullOrWhiteSpace(check.Response))
                    throw new Exception("agy failed" + (check.Error != null ? ": " + check.Error : " (status " + (check.Status ?? "none") + ")."));
                return check.Response.Trim();
            }
            finally
            {
                // 6: agy has no "don't save" option, so remove this run's conversation files afterwards.
                if (s.AgyDeleteHistory && conversationId != null) DeleteConversation(conversationId);
            }
        }

        sealed class StreamCheck
        {
            public bool InitSeen;
            public string ConversationId, Agent, PermissionMode, ToolUsed, Status, Response, Error;
        }

        static StreamCheck Inspect(string stdout)
        {
            var c = new StreamCheck();
            foreach (var raw in (stdout ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("{")) continue;
                XElement e;
                try
                {
                    using (var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(line), XmlDictionaryReaderQuotas.Max))
                        e = XElement.Load(reader);
                }
                catch { continue; }

                var ev = (string)e.Element("event");
                if (c.ConversationId == null) c.ConversationId = (string)e.Element("conversation_id");
                if (ev == "init")
                {
                    var init = e.Element("init");
                    c.InitSeen = true;
                    c.Agent = (string)init?.Element("agent");
                    c.PermissionMode = (string)init?.Element("permission_mode");
                }
                else if (ev == "step_update")
                {
                    var st = e.Element("step_update");
                    if (st == null) continue;
                    if (c.ConversationId == null) c.ConversationId = (string)st.Element("conversation_id");
                    var type = (string)st.Element("step_type");
                    var tool = (string)st.Element("tool_name") ?? (string)st.Element("tool_info")?.Element("name");
                    if (type == "tool" || tool != null || st.Element("tool_info") != null || st.Element("subagent_info") != null)
                        c.ToolUsed = c.ToolUsed ?? tool ?? type ?? "unknown";
                }
                else if (ev == "result")
                {
                    var res = e.Element("result");
                    if (res == null) continue;
                    c.ConversationId = (string)res.Element("conversation_id") ?? c.ConversationId;
                    c.Status = (string)res.Element("status");
                    c.Response = (string)res.Element("response");
                    var err = res.Element("error");
                    if (err != null) c.Error = err.HasElements ? (string)err.Element("message") ?? err.Value : err.Value;
                }
            }
            return c;
        }

        /// <summary>Deletes the per-conversation files agy writes. Shared summary files can't be edited safely and are left alone.</summary>
        static void DeleteConversation(string id)
        {
            Guid g;
            if (!Guid.TryParse(id, out g)) return; // only ever touch paths named after a real conversation id
            id = g.ToString("D");
            var targets = new[]
            {
                Path.Combine(DataDir, "conversations", id + ".db"),
                Path.Combine(DataDir, "conversations", id + ".db-wal"),
                Path.Combine(DataDir, "conversations", id + ".db-shm"),
                Path.Combine(DataDir, "presence", id + ".lock"),
            };
            var brain = Path.Combine(DataDir, "brain", id);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                bool ok = true;
                foreach (var f in targets)
                    try { if (File.Exists(f)) File.Delete(f); } catch { ok = false; }
                try { if (Directory.Exists(brain)) Directory.Delete(brain, true); } catch { ok = false; }
                if (ok) return;
                Thread.Sleep(150); // agy may still be releasing its file handles
            }
        }

        static bool Mentions(string text, string word) =>
            (text ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

        static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var ch in s ?? "")
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20 || ch == (char)0x2028 || ch == (char)0x2029) sb.Append(@"\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
