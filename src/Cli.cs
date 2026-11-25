using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DmEnhancer
{
    /// <summary>Shared helpers for launching a CLI, feeding it stdin and collecting its output.</summary>
    static class Cli
    {
        public static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public sealed class Result
        {
            public int ExitCode;
            public string StdOut;
            public string StdErr;
        }

        public static string FindOnPath(params string[] names)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var name in names)
                foreach (var dir in path.Split(';'))
                {
                    if (dir.Trim().Length == 0) continue;
                    try
                    {
                        var p = Path.Combine(dir.Trim(), name);
                        if (File.Exists(p)) return p;
                    }
                    catch (ArgumentException) { }
                }
            return null;
        }

        public static async Task<Result> RunAsync(string exe, IEnumerable<string> args, string stdin, int timeoutSeconds, string workDir, string toolName, CancellationToken ct)
        {
            var argLine = string.Join(" ", args.Select(QuoteArg));
            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workDir,
            };
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.Arguments = "/d /s /c \"" + QuoteArg(exe) + " " + argLine + "\"";
            }
            else
            {
                psi.FileName = exe;
                psi.Arguments = argLine;
            }

            using (var proc = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                proc.Exited += (_, __) => exited.TrySetResult(true);
                proc.Start();
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                using (var w = new StreamWriter(proc.StandardInput.BaseStream, Utf8NoBom))
                    await w.WriteAsync(stdin).ConfigureAwait(false);

                var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct);
                var done = await Task.WhenAny(exited.Task, timeout).ConfigureAwait(false);
                if (done != exited.Task)
                {
                    try { if (!proc.HasExited) proc.Kill(); } catch { }
                    ct.ThrowIfCancellationRequested();
                    throw new TimeoutException(toolName + " took longer than " + timeoutSeconds + "s.");
                }
                proc.WaitForExit();
                return new Result
                {
                    ExitCode = proc.ExitCode,
                    StdOut = await stdoutTask.ConfigureAwait(false),
                    StdErr = await stderrTask.ConfigureAwait(false),
                };
            }
        }

        public static string ErrorSummary(string toolName, Result r)
        {
            var text = (r.StdErr ?? "") + "\n" + (r.ExitCode != 0 ? r.StdOut ?? "" : "");
            var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var err = lines.LastOrDefault(l => l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) ?? lines.LastOrDefault();
            if (err != null && err.Length > 300) err = err.Substring(0, 300) + "…";
            return toolName + " failed (exit " + r.ExitCode + ")" + (err != null ? ": " + err : ".");
        }

        /// <summary>Standard Windows argv quoting (CommandLineToArgvW rules).</summary>
        public static string QuoteArg(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (var c in a)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1).Append('"'); backslashes = 0; continue; }
                sb.Append('\\', backslashes).Append(c);
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }
    }

    /// <summary>Picks the provider chosen in settings.</summary>
    static class Editor
    {
        public const string Claude = "claude";
        public const string Codex = "codex";
        public const string Agy = "agy";

        public static Task<string> RunAsync(AppSettings s, string input, CancellationToken ct)
        {
            switch (s.Provider)
            {
                case Codex: return DmEnhancer.Codex.RunAsync(s, input, ct);
                case Agy: return Antigravity.RunAsync(s, input, ct);
                default: return ClaudeCode.RunAsync(s, input, ct);
            }
        }

        public static string ModelOf(AppSettings s) =>
            s.Provider == Codex ? s.Model : s.Provider == Agy ? s.AgyModel : s.ClaudeModel;
    }
}
