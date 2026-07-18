using Microsoft.Win32;
using System.Windows.Forms;

namespace WakeToXbox
{
    // Settings live in HKCU\Software\WakeToXbox; autostart uses the standard HKCU Run key.
    static class Config
    {
        const string KeyPath = @"Software\WakeToXbox";
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "WakeToXbox";

        public static bool IsFirstRun
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                    return key == null;
            }
        }

        public static string WakeSource
        {
            get { return ReadString("WakeSource", "USB Composite Device"); }
            set { Write("WakeSource", value); }
        }

        public static bool Enabled
        {
            get { return ReadBool("Enabled", true); }
            set { Write("Enabled", value ? 1 : 0); }
        }

        public static bool ShowOverlay
        {
            get { return ReadBool("ShowOverlay", true); }
            set { Write("ShowOverlay", value ? 1 : 0); }
        }

        public static bool StartWithWindows
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return key != null && key.GetValue(RunValueName) != null;
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (value)
                        key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else
                        key.DeleteValue(RunValueName, false);
                }
            }
        }

        static string ReadString(string name, string fallback)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                if (key == null) return fallback;
                var value = key.GetValue(name) as string;
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
        }

        static bool ReadBool(string name, bool fallback)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                if (key == null) return fallback;
                var value = key.GetValue(name);
                return value is int ? (int)value != 0 : fallback;
            }
        }

        static void Write(string name, object value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
                key.SetValue(name, value);
        }
    }
}
