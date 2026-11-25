using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DmEnhancer
{
    sealed class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray;
        readonly HotkeyWindow hotkey = new HotkeyWindow();
        readonly Icon idleIcon = MakeIcon(Color.FromArgb(37, 99, 235));
        readonly Icon busyIcon = MakeIcon(Color.FromArgb(234, 138, 0));
        readonly ToolStripMenuItem runItem;
        AppSettings settings;
        SettingsForm settingsForm;
        BusyToast toast;
        CancellationTokenSource cts;
        bool busy;

        public TrayApp()
        {
            settings = AppSettings.Load();

            var menu = new ContextMenuStrip();
            runItem = new ToolStripMenuItem("Edit clipboard text", null, async (_, __) => await EditAsync(fromHotkey: false));
            menu.Items.Add(runItem);
            menu.Items.Add("Settings…", null, (_, __) => ShowSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, __) => ExitThread());

            tray = new NotifyIcon { Icon = idleIcon, Text = "DM Enhancer", ContextMenuStrip = menu, Visible = true };
            tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

            hotkey.Pressed += async () => await EditAsync(fromHotkey: true);
            ApplyHotkey(showSuccess: true);

            // Give back startup memory once everything is loaded.
            var t = new System.Windows.Forms.Timer { Interval = 3000 };
            t.Tick += (_, __) => { t.Stop(); t.Dispose(); Native.TrimWorkingSet(); };
            t.Start();
        }

        void ApplyHotkey(bool showSuccess)
        {
            Keys combo;
            if (!HotkeyText.TryParse(settings.Hotkey, out combo) || !hotkey.Register(combo))
            {
                runItem.Text = "Edit clipboard text";
                Notify("Couldn't register the hotkey " + settings.Hotkey + " (another app may be using it). Pick another one in Settings.", ToolTipIcon.Warning);
                return;
            }
            runItem.Text = "Edit clipboard text\t" + settings.Hotkey;
            tray.Text = "DM Enhancer (" + settings.Hotkey + ")";
            if (showSuccess)
                Notify("Running in the tray. Copy a message and press " + settings.Hotkey + ".", ToolTipIcon.None);
        }

        void ShowSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(settings, idleIcon);
            settingsForm.Saved += s =>
            {
                settings = s;
                ApplyHotkey(showSuccess: false);
            };
            settingsForm.FormClosed += (_, __) => { settingsForm = null; Native.TrimWorkingSet(); };
            settingsForm.Show();
            settingsForm.Activate();
        }

        async Task EditAsync(bool fromHotkey)
        {
            if (busy)
            {
                cts?.Cancel(); // pressing the hotkey again cancels the running edit
                return;
            }
            busy = true;
            var target = fromHotkey ? Native.GetForegroundWindow() : IntPtr.Zero;
            try
            {
                if (fromHotkey && settings.CopySelectionFirst)
                {
                    await Native.WaitForModifiersReleased();
                    var seq = Native.GetClipboardSequenceNumber();
                    Native.SendCtrlPlus(Keys.C);
                    bool changed = false;
                    for (int i = 0; i < 40 && !changed; i++)
                    {
                        await Task.Delay(20);
                        changed = Native.GetClipboardSequenceNumber() != seq;
                    }
                    if (!changed)
                    {
                        Notify("Nothing was selected to copy.", ToolTipIcon.Info);
                        return;
                    }
                    await Task.Delay(30);
                }

                var text = await ClipboardHelper.GetTextAsync();
                if (string.IsNullOrWhiteSpace(text))
                {
                    Notify("The clipboard doesn't contain any text.", ToolTipIcon.Info);
                    return;
                }

                cts = new CancellationTokenSource();
                SetBusy(true, fromHotkey);
                var run = settings.Clone();
                var result = await Task.Run(() => Editor.RunAsync(run, text, cts.Token));
                if (settings.StraightQuotes) result = TextFixes.StraightenQuotes(result);

                ClipboardHelper.SetText(result);
                SetBusy(false, false);

                if (fromHotkey && settings.AutoPaste)
                {
                    if (Native.GetForegroundWindow() == target)
                    {
                        await Native.WaitForModifiersReleased();
                        await Task.Delay(40);
                        Native.SendCtrlPlus(Keys.V);
                    }
                    else
                    {
                        Notify("You switched windows, so it wasn't pasted. The edited text is on your clipboard.", ToolTipIcon.Info);
                    }
                }
                else
                {
                    Notify("Edited text copied to the clipboard.", ToolTipIcon.None);
                }
            }
            catch (OperationCanceledException)
            {
                Notify("Cancelled.", ToolTipIcon.None);
            }
            catch (Exception ex)
            {
                Notify(ex.Message, ToolTipIcon.Error);
            }
            finally
            {
                SetBusy(false, false);
                cts?.Dispose();
                cts = null;
                busy = false;
                Native.TrimWorkingSet();
            }
        }

        void SetBusy(bool on, bool showToast)
        {
            tray.Icon = on ? busyIcon : idleIcon;
            tray.Text = on ? "DM Enhancer: editing…" : "DM Enhancer (" + settings.Hotkey + ")";
            if (on && showToast)
            {
                if (toast == null || toast.IsDisposed) toast = new BusyToast();
                toast.ShowNearCursor("Editing…  (" + settings.Hotkey + " to cancel)");
            }
            else if (!on && toast != null && !toast.IsDisposed)
            {
                toast.Hide();
            }
        }

        void Notify(string message, ToolTipIcon icon)
        {
            tray.ShowBalloonTip(4000, "DM Enhancer", message, icon);
        }

        protected override void ExitThreadCore()
        {
            cts?.Cancel();
            hotkey.Dispose();
            tray.Visible = false;
            tray.Dispose();
            toast?.Dispose();
            settingsForm?.Close();
            base.ExitThreadCore();
        }

        static Icon MakeIcon(Color color)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var path = RoundedRect(new Rectangle(1, 1, 30, 30), 8))
                using (var brush = new SolidBrush(color))
                using (var font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.FillPath(brush, path);
                    g.DrawString("Aa", font, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
                }
                var h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    static class TextFixes
    {
        public static string StraightenQuotes(string s) =>
            s.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"');
    }

    /// <summary>Small "Editing…" label near the mouse. Never takes focus, so the paste lands in the right window.</summary>
    sealed class BusyToast : Form
    {
        readonly Label label;

        public BusyToast()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 32, 36);
            Padding = new Padding(10, 6, 10, 6);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            label = new Label
            {
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f),
                Dock = DockStyle.Fill,
            };
            Controls.Add(label);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/;
                return cp;
            }
        }

        public void ShowNearCursor(string text)
        {
            label.Text = text;
            var pt = Cursor.Position;
            var area = Screen.FromPoint(pt).WorkingArea;
            var size = GetPreferredSize(Size.Empty);
            int x = Math.Min(pt.X + 16, area.Right - size.Width);
            int y = Math.Min(pt.Y + 20, area.Bottom - size.Height);
            Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
            if (!Visible) Show();
        }
    }
}
