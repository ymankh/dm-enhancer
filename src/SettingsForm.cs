using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DmEnhancer
{
    sealed class SettingsForm : Form
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "DmEnhancer";

        public event Action<AppSettings> Saved;

        readonly AppSettings work;
        string currentProvider;
        readonly ComboBox provider = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        static readonly string[] ProviderIds = { Editor.Claude, Editor.Codex, Editor.Agy };
        readonly Label agyWarning = new Label { AutoSize = true, MaximumSize = new Size(590, 0), ForeColor = Color.FromArgb(176, 72, 0), Margin = new Padding(0, 0, 0, 4) };
        readonly CheckBox agyDeleteHistory = new CheckBox { AutoSize = true, Text = "Delete each agy conversation after the edit (agy has no option to skip saving)" };
        FlowLayoutPanel agyPanel;
        Label agyPanelLabel;
        bool switchingProvider;
        readonly Label reasoningHint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 6, 0, 0) };
        readonly ComboBox model = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 220 };
        readonly ComboBox reasoning = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        readonly TextBox hotkeyBox = new TextBox { Width = 220, ReadOnly = true, BackColor = SystemColors.Window, Cursor = Cursors.Hand };
        readonly CheckBox copyFirst = new CheckBox { AutoSize = true, Text = "Copy the selected text first (sends Ctrl+C, so you can skip copying)" };
        readonly CheckBox autoPaste = new CheckBox { AutoSize = true, Text = "Paste the result automatically (sends Ctrl+V)" };
        readonly CheckBox straightQuotes = new CheckBox { AutoSize = true, Text = "Use straight quotes (' \") instead of curly ones (’ ”)" };
        readonly CheckBox startup = new CheckBox { AutoSize = true, Text = "Start with Windows" };
        readonly TextBox codexPath = new TextBox { Dock = DockStyle.Fill };
        readonly TextBox prompt = new TextBox
        {
            Multiline = true, AcceptsReturn = true, AcceptsTab = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f),
        };
        readonly TextBox tryInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 54 };
        readonly TextBox tryOutput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 54, ReadOnly = true, BackColor = SystemColors.Window };
        readonly Button tryButton = new Button { Text = "Try it", AutoSize = true };
        readonly Label tryStatus = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 6, 0, 0) };
        Keys hotkeyCombo;
        CancellationTokenSource tryCts;

        public SettingsForm(AppSettings current, Icon icon)
        {
            work = current.Clone();
            currentProvider = Array.IndexOf(ProviderIds, current.Provider) >= 0 ? current.Provider : Editor.Claude;
            Text = "DM Enhancer settings";
            Icon = icon;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 760);
            MinimumSize = new Size(600, 600);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Provider, model + reasoning
            provider.Items.AddRange(new object[] { "Claude Code (recommended)", "Codex", "Antigravity (agy)" });
            provider.SelectedIndex = Array.IndexOf(ProviderIds, currentProvider);
            provider.SelectedIndexChanged += (_, __) =>
            {
                if (switchingProvider) return;
                var next = ProviderIds[provider.SelectedIndex];
                if (next == Editor.Agy && !work.AgyWarningAccepted)
                {
                    if (!ConfirmAgy())
                    {
                        switchingProvider = true;
                        provider.SelectedIndex = Array.IndexOf(ProviderIds, currentProvider);
                        switchingProvider = false;
                        return;
                    }
                    work.AgyWarningAccepted = true;
                }
                StoreProviderFields();
                currentProvider = next;
                LoadProviderFields();
            };
            AddRow(root, "Provider:", Row(provider, new Label { Text = "Claude Code ≈ 2s per message, Codex ≈ 4-5s, agy ≈ 4-6s", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 6, 0, 0) }));
            var modelRow = Row(model, new Label { Text = "Reasoning:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) }, reasoning, reasoningHint);
            AddRow(root, "Model:", modelRow);

            // agy safety notes (only shown when agy is the provider)
            agyDeleteHistory.Checked = current.AgyDeleteHistory;
            agyPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            agyPanel.Controls.Add(agyWarning);
            agyPanel.Controls.Add(agyDeleteHistory);
            agyPanelLabel = AddRow(root, "agy safety:", agyPanel);
            agyPanelLabel.ForeColor = Color.FromArgb(176, 72, 0);

            // Hotkey
            HotkeyText.TryParse(current.Hotkey, out hotkeyCombo);
            hotkeyBox.Text = current.Hotkey;
            hotkeyBox.KeyDown += HotkeyKeyDown;
            hotkeyBox.PreviewKeyDown += (_, e) => e.IsInputKey = true;
            AddRow(root, "Hotkey:", Row(hotkeyBox, new Label { Text = "click the box and press a new combination", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 6, 0, 0) }));

            // Options
            copyFirst.Checked = current.CopySelectionFirst;
            autoPaste.Checked = current.AutoPaste;
            straightQuotes.Checked = current.StraightQuotes;
            startup.Checked = IsStartupEnabled();
            var options = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            options.Controls.AddRange(new Control[] { autoPaste, copyFirst, straightQuotes, startup });
            AddRow(root, "Options:", options);

            // CLI path
            var browse = new Button { Text = "Browse…", AutoSize = true };
            browse.Click += (_, __) =>
            {
                using (var dlg = new OpenFileDialog { Filter = "Programs|*.exe;*.cmd|All files|*.*", Title = "Find the CLI" })
                    if (dlg.ShowDialog(this) == DialogResult.OK) codexPath.Text = dlg.FileName;
            };
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.Controls.Add(codexPath, 0, 0);
            pathRow.Controls.Add(browse, 1, 0);
            AddRow(root, "CLI path:", pathRow);
            LoadProviderFields();

            // Prompt
            prompt.Text = current.Prompt.Replace("\r\n", "\n").Replace("\n", "\r\n");
            var reset = new LinkLabel { Text = "Reset to default", AutoSize = true };
            reset.LinkClicked += (_, __) =>
            {
                if (MessageBox.Show(this, "Replace the prompt with the default one?", "DM Enhancer", MessageBoxButtons.OKCancel) == DialogResult.OK)
                    prompt.Text = AppSettings.DefaultPrompt.Replace("\r\n", "\n").Replace("\n", "\r\n");
            };
            var promptLabel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
            promptLabel.Controls.Add(new Label { Text = "Prompt:", AutoSize = true });
            promptLabel.Controls.Add(reset);
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(promptLabel, 0, root.RowCount);
            root.Controls.Add(prompt, 1, root.RowCount);
            root.RowCount++;

            // Try it
            SetCue(tryInput, "Type a test message here, then hit Try it (uses the settings above, saved or not)");
            tryButton.Click += async (_, __) => await TryItAsync();
            var tryBox = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(0) };
            tryBox.Controls.Add(tryInput);
            tryBox.Controls.Add(Row(tryButton, tryStatus));
            tryBox.Controls.Add(tryOutput);
            AddRow(root, "Test:", tryBox);

            // Buttons
            var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(90, 0) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(90, 0), DialogResult = DialogResult.Cancel };
            save.Click += (_, __) => SaveAndClose();
            cancel.Click += (_, __) => Close();
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            root.Controls.Add(buttons, 0, root.RowCount);
            root.SetColumnSpan(buttons, 2);
            root.RowCount++;

            AcceptButton = null; // Enter must work inside the prompt box
            CancelButton = cancel;
            Controls.Add(root);
            FormClosing += (_, __) => tryCts?.Cancel();
        }

        static FlowLayoutPanel Row(params Control[] controls)
        {
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            p.Controls.AddRange(controls);
            return p;
        }

        static Label AddRow(TableLayoutPanel t, string label, Control c)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
            t.Controls.Add(l, 0, t.RowCount);
            c.Margin = new Padding(0, 2, 0, 6);
            t.Controls.Add(c, 1, t.RowCount);
            t.RowCount++;
            return l;
        }

        bool ConfirmAgy()
        {
            const string text =
                "agy (Antigravity CLI) is a full coding agent. It has no plain \"text only\" mode like Claude Code and Codex.\n\n" +
                "DM Enhancer locks it down as far as agy allows:\n" +
                "  • a custom editor agent with your prompt and an empty tool list\n" +
                "  • session-only settings that deny every command, file, web and MCP action, use the strictest approval mode and turn on the terminal sandbox\n" +
                "  • an empty workspace folder, with your message sent on stdin\n" +
                "  • if agy still tries to use any tool, the result is thrown away\n\n" +
                "What can't be fixed:\n" +
                "  • agy saves every conversation. DM Enhancer can delete each one afterwards, but your messages still end up in agy's shared history summaries.\n" +
                "  • Global agy rules, plugins and hooks (in ~/.gemini) are still loaded.\n" +
                "  • It's slower: about 4-6s per message.\n\n" +
                "Use agy anyway?";
            return MessageBox.Show(this, text, "Antigravity (agy): security warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        void HotkeyKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            var key = e.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return;
            if ((e.Modifiers & (Keys.Control | Keys.Alt)) == 0)
            {
                hotkeyBox.Text = "Use Ctrl and/or Alt plus a key";
                hotkeyCombo = Keys.None;
                return;
            }
            hotkeyCombo = e.Modifiers | key;
            hotkeyBox.Text = HotkeyText.Format(hotkeyCombo);
        }

        void LoadProviderFields()
        {
            bool claude = currentProvider == Editor.Claude, agy = currentProvider == Editor.Agy;
            model.Items.Clear();
            model.Items.AddRange((claude ? ClaudeCode.Models : agy ? Antigravity.Models : Codex.ListModels().ToArray()).Cast<object>().ToArray());
            model.Text = claude ? work.ClaudeModel : agy ? work.AgyModel : work.Model;
            reasoning.Items.Clear();
            reasoning.Items.AddRange(claude || agy
                ? new object[] { "default", "low", "medium", "high" }
                : new object[] { "none", "low", "medium", "high", "xhigh" });
            var effort = claude ? work.ClaudeEffort : agy ? work.AgyEffort : work.Reasoning;
            reasoning.SelectedItem = reasoning.Items.Contains(effort) ? effort : (agy ? "default" : "low");
            reasoningHint.Text = claude ? "low = fastest" : agy ? "default = level in the model name" : "none = fastest (not every model supports it)";
            codexPath.Text = claude ? work.ClaudePath : agy ? work.AgyPath : work.CodexPath;
            var detected = claude ? ClaudeCode.FindExecutable(null) : agy ? Antigravity.FindExecutable(null) : Codex.FindExecutable(null);
            SetCue(codexPath, detected != null ? "auto: " + detected : "not found: browse to " + (claude ? "claude.exe" : agy ? "agy.exe" : "codex.exe"));

            agyPanel.Visible = agyPanelLabel.Visible = agy;
            if (agy)
            {
                var text = "agy is a coding agent, so it runs locked down: a no-tools editor agent, a session-only deny-all " +
                           "permission list, the terminal sandbox and an empty workspace. Any attempt to use a tool throws the result away. " +
                           "Your messages still reach agy's shared history summaries, even with the option below turned on.";
                var extras = Antigravity.GlobalExtras();
                if (extras.Count > 0)
                    text += "\n\u26A0 These global agy files are loaded into every run and can't be turned off here: " + string.Join(", ", extras) + ".";
                agyWarning.Text = "\u26A0 " + text;
            }
        }

        void StoreProviderFields()
        {
            var effort = reasoning.SelectedItem as string ?? "low";
            work.AgyDeleteHistory = agyDeleteHistory.Checked;
            if (currentProvider == Editor.Agy)
            {
                work.AgyModel = model.Text.Trim();
                work.AgyEffort = effort;
                work.AgyPath = codexPath.Text.Trim();
            }
            else if (currentProvider == Editor.Claude)
            {
                work.ClaudeModel = model.Text.Trim();
                work.ClaudeEffort = effort;
                work.ClaudePath = codexPath.Text.Trim();
            }
            else
            {
                work.Model = model.Text.Trim();
                work.Reasoning = effort;
                work.CodexPath = codexPath.Text.Trim();
            }
        }

        AppSettings FromForm()
        {
            StoreProviderFields();
            var s = work.Clone();
            s.Provider = currentProvider;
            if (hotkeyCombo != Keys.None) s.Hotkey = HotkeyText.Format(hotkeyCombo);
            s.CopySelectionFirst = copyFirst.Checked;
            s.AutoPaste = autoPaste.Checked;
            s.StraightQuotes = straightQuotes.Checked;
            s.Prompt = prompt.Text.Replace("\r\n", "\n");
            return s;
        }

        void SaveAndClose()
        {
            if (model.Text.Trim().Length == 0) { MessageBox.Show(this, "Pick a model.", "DM Enhancer"); return; }
            if (hotkeyCombo == Keys.None) { MessageBox.Show(this, "Set a hotkey with Ctrl and/or Alt.", "DM Enhancer"); return; }
            if (prompt.Text.Trim().Length == 0) { MessageBox.Show(this, "The prompt is empty.", "DM Enhancer"); return; }
            var s = FromForm();
            try
            {
                s.Save();
                SetStartup(startup.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save: " + ex.Message, "DM Enhancer");
                return;
            }
            Saved?.Invoke(s);
            Close();
        }

        async Task TryItAsync()
        {
            if (tryCts != null) { tryCts.Cancel(); return; }
            var input = tryInput.Text;
            if (input.Trim().Length == 0) { tryStatus.Text = "type something first"; return; }
            var s = FromForm();
            tryCts = new CancellationTokenSource();
            tryButton.Text = "Cancel";
            tryStatus.Text = "running " + Editor.ModelOf(s) + "…";
            tryOutput.Text = "";
            var started = DateTime.UtcNow;
            try
            {
                var result = await Task.Run(() => Editor.RunAsync(s, input.Replace("\r\n", "\n"), tryCts.Token));
                if (s.StraightQuotes) result = TextFixes.StraightenQuotes(result);
                tryOutput.Text = result.Replace("\r\n", "\n").Replace("\n", "\r\n");
                tryStatus.Text = string.Format("done in {0:0.0}s", (DateTime.UtcNow - started).TotalSeconds);
            }
            catch (OperationCanceledException) { if (!IsDisposed) tryStatus.Text = "cancelled"; }
            catch (Exception ex) { if (!IsDisposed) { tryStatus.Text = "failed"; tryOutput.Text = ex.Message; } }
            finally
            {
                tryCts.Dispose();
                tryCts = null;
                if (!IsDisposed) tryButton.Text = "Try it";
            }
        }

        static bool IsStartupEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k?.GetValue(RunValue) != null;
            }
            catch { return false; }
        }

        static void SetStartup(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        static void SetCue(TextBox box, string cue)
        {
            box.Tag = cue;
            if (box.IsHandleCreated) SendMessage(box.Handle, 0x1501 /*EM_SETCUEBANNER*/, (IntPtr)1, cue);
            else if (box.AccessibleName != "cue")
            {
                box.AccessibleName = "cue";
                box.HandleCreated += (_, __) => SendMessage(box.Handle, 0x1501, (IntPtr)1, (string)box.Tag);
            }
        }
    }
}
