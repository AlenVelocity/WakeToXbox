using System;
using System.Threading;
using System.Windows.Forms;

namespace WakeToXbox
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (new Mutex(true, "WakeToXboxApp_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("WakeToXbox is already running. Look for its icon in the system tray.",
                        "WakeToXbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }
}
