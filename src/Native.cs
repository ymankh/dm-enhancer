using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DmEnhancer
{
    static class Native
    {
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        const uint KEYEVENTF_KEYUP = 2;
        const byte VK_CONTROL = 0x11;
        static readonly int[] Modifiers = { 0x10, 0x11, 0x12, 0x5B, 0x5C }; // Shift, Ctrl, Alt, LWin, RWin

        /// <summary>Waits until the hotkey's modifier keys are let go, so a simulated Ctrl+V doesn't become Ctrl+Alt+V.</summary>
        public static async Task WaitForModifiersReleased(int timeoutMs = 1500)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool held = false;
                foreach (var vk in Modifiers)
                    if ((GetAsyncKeyState(vk) & 0x8000) != 0) { held = true; break; }
                if (!held) return;
                await Task.Delay(15);
            }
        }

        public static void SendCtrlPlus(Keys key)
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event((byte)key, 0, 0, UIntPtr.Zero);
            keybd_event((byte)key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        /// <summary>Hands idle memory back to Windows (keeps the Task Manager number low while sitting in the tray).</summary>
        public static void TrimWorkingSet()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }
    }

    /// <summary>Hidden message-only window that receives the global hotkey.</summary>
    sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        const int WM_HOTKEY = 0x0312;
        const int Id = 0xD3;
        bool registered;
        public event Action Pressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams { Parent = (IntPtr)(-3) }); // HWND_MESSAGE
        }

        public bool Register(Keys combo)
        {
            Unregister();
            uint mods = 0x4000; // MOD_NOREPEAT
            if ((combo & Keys.Alt) != 0) mods |= 1;
            if ((combo & Keys.Control) != 0) mods |= 2;
            if ((combo & Keys.Shift) != 0) mods |= 4;
            registered = Native.RegisterHotKey(Handle, Id, mods, (uint)(combo & Keys.KeyCode));
            return registered;
        }

        public void Unregister()
        {
            if (registered) Native.UnregisterHotKey(Handle, Id);
            registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && (int)m.WParam == Id) Pressed?.Invoke();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }
    }

    static class HotkeyText
    {
        public static string Format(Keys combo)
        {
            var parts = "";
            if ((combo & Keys.Control) != 0) parts += "Ctrl+";
            if ((combo & Keys.Alt) != 0) parts += "Alt+";
            if ((combo & Keys.Shift) != 0) parts += "Shift+";
            return parts + KeyName(combo & Keys.KeyCode);
        }

        static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            return k.ToString();
        }

        public static bool TryParse(string text, out Keys combo)
        {
            combo = Keys.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            Keys key = Keys.None;
            foreach (var raw in text.Split('+'))
            {
                var p = raw.Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) combo |= Keys.Control;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) combo |= Keys.Alt;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) combo |= Keys.Shift;
                else if (p.Length == 1 && char.IsDigit(p[0])) key = Keys.D0 + (p[0] - '0');
                else if (!Enum.TryParse(p, true, out key)) return false;
            }
            if (key == Keys.None || (combo & (Keys.Control | Keys.Alt)) == 0) return false;
            combo |= key;
            return true;
        }
    }

    static class ClipboardHelper
    {
        public static async Task<string> GetTextAsync()
        {
            for (int i = 0; ; i++)
            {
                try
                {
                    return Clipboard.ContainsText(TextDataFormat.UnicodeText) ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;
                }
                catch (ExternalException) when (i < 10) { await Task.Delay(50); } // clipboard busy in another app
            }
        }

        public static void SetText(string text) => Clipboard.SetDataObject(text, true, 10, 50);
    }
}
