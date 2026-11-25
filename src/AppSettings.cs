using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace DmEnhancer
{
    /// <summary>User settings, stored in %APPDATA%\DmEnhancer (settings.ini + prompt.md).</summary>
    sealed class AppSettings
    {
        public string Provider = "";                      // "claude" or "codex"
        public string Model = "";                         // Codex model
        public string Reasoning = "low";                  // Codex reasoning effort
        public string ClaudeModel = ClaudeCode.DefaultModel;
        public string ClaudeEffort = "low";
        public string AgyModel = Antigravity.DefaultModel;
        public string AgyEffort = "default";
        public string AgyPath = "";
        public bool AgyDeleteHistory = true;
        public bool AgyWarningAccepted = false;
        public string Hotkey = "Ctrl+Alt+E";
        public bool CopySelectionFirst = false;
        public bool AutoPaste = true;
        public bool StraightQuotes = true;
        public string CodexPath = "";
        public string ClaudePath = "";
        public int TimeoutSeconds = 60;
        public string Prompt = "";

        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DmEnhancer");
        static string IniPath => Path.Combine(Dir, "settings.ini");
        static string PromptPath => Path.Combine(Dir, "prompt.md");

        /// <summary>Empty folder Codex runs in, so no project files / AGENTS.md get picked up.</summary>
        public static string WorkDir => Path.Combine(Dir, "work");

        public AppSettings Clone() => (AppSettings)MemberwiseClone();

        public static string DefaultPrompt
        {
            get
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DefaultPrompt.md"))
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            Directory.CreateDirectory(Dir);
            Directory.CreateDirectory(WorkDir);

            if (File.Exists(IniPath))
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(IniPath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                string v;
                if (map.TryGetValue("Provider", out v)) s.Provider = v;
                if (map.TryGetValue("Model", out v)) s.Model = v;
                if (map.TryGetValue("ClaudeModel", out v) && v.Length > 0) s.ClaudeModel = v;
                if (map.TryGetValue("ClaudeEffort", out v)) s.ClaudeEffort = v;
                if (map.TryGetValue("ClaudePath", out v)) s.ClaudePath = v;
                if (map.TryGetValue("AgyModel", out v) && v.Length > 0) s.AgyModel = v;
                if (map.TryGetValue("AgyEffort", out v) && v.Length > 0) s.AgyEffort = v;
                if (map.TryGetValue("AgyPath", out v)) s.AgyPath = v;
                if (map.TryGetValue("AgyDeleteHistory", out v)) s.AgyDeleteHistory = v != "false";
                if (map.TryGetValue("AgyWarningAccepted", out v)) s.AgyWarningAccepted = v == "true";
                if (map.TryGetValue("Reasoning", out v)) s.Reasoning = v;
                if (map.TryGetValue("Hotkey", out v) && v.Length > 0) s.Hotkey = v;
                if (map.TryGetValue("CopySelectionFirst", out v)) s.CopySelectionFirst = v == "true";
                if (map.TryGetValue("AutoPaste", out v)) s.AutoPaste = v == "true";
                if (map.TryGetValue("StraightQuotes", out v)) s.StraightQuotes = v == "true";
                if (map.TryGetValue("CodexPath", out v)) s.CodexPath = v;
                int t;
                if (map.TryGetValue("TimeoutSeconds", out v) && int.TryParse(v, out t) && t >= 10) s.TimeoutSeconds = t;
            }

            s.Prompt = File.Exists(PromptPath) ? File.ReadAllText(PromptPath, Encoding.UTF8) : DefaultPrompt;
            if (string.IsNullOrWhiteSpace(s.Model)) s.Model = Codex.DefaultModel();
            if (s.Provider != Editor.Claude && s.Provider != Editor.Codex && s.Provider != Editor.Agy)
                // Claude Code answers in about half the time Codex needs, so prefer it when it's installed.
                s.Provider = ClaudeCode.FindExecutable(s.ClaudePath) != null || Codex.FindExecutable(s.CodexPath) == null ? Editor.Claude : Editor.Codex;
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            sb.AppendLine("Provider=" + Provider);
            sb.AppendLine("Model=" + Model);
            sb.AppendLine("ClaudeModel=" + ClaudeModel);
            sb.AppendLine("ClaudeEffort=" + ClaudeEffort);
            sb.AppendLine("ClaudePath=" + ClaudePath);
            sb.AppendLine("AgyModel=" + AgyModel);
            sb.AppendLine("AgyEffort=" + AgyEffort);
            sb.AppendLine("AgyPath=" + AgyPath);
            sb.AppendLine("AgyDeleteHistory=" + (AgyDeleteHistory ? "true" : "false"));
            sb.AppendLine("AgyWarningAccepted=" + (AgyWarningAccepted ? "true" : "false"));
            sb.AppendLine("Reasoning=" + Reasoning);
            sb.AppendLine("Hotkey=" + Hotkey);
            sb.AppendLine("CopySelectionFirst=" + (CopySelectionFirst ? "true" : "false"));
            sb.AppendLine("AutoPaste=" + (AutoPaste ? "true" : "false"));
            sb.AppendLine("StraightQuotes=" + (StraightQuotes ? "true" : "false"));
            sb.AppendLine("CodexPath=" + CodexPath);
            sb.AppendLine("TimeoutSeconds=" + TimeoutSeconds);
            File.WriteAllText(IniPath, sb.ToString(), Utf8NoBom);
            File.WriteAllText(PromptPath, Prompt, Utf8NoBom);
        }
    }
}
