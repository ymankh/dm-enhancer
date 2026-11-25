using System;
using System.Threading;
using System.Windows.Forms;

namespace DmEnhancer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (new Mutex(true, @"Local\DmEnhancer.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("DM Enhancer is already running. Look for it in the system tray.", "DM Enhancer");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Application.Run(new TrayApp());
            }
        }
    }
}
