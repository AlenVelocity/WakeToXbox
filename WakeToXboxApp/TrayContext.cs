using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WakeToXbox
{
    sealed class TrayContext : ApplicationContext
    {
        readonly NotifyIcon _tray;
        readonly MessageWindow _messages;
        readonly ToolStripMenuItem _enabledItem;
        readonly List<OverlayForm> _overlays = new List<OverlayForm>();
        SettingsForm _settings;
        int _busy;
        DateTime _lastHandledWakeUtc = DateTime.MinValue;

        static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WakeToXbox.log");

        internal static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + "\r\n");
            }
            catch { }
        }

        public TrayContext()
        {
            Log("--- started, wake source: \"" + Config.WakeSource + "\", enabled: " + Config.Enabled);
            _messages = new MessageWindow(this);

            _enabledItem = new ToolStripMenuItem("Enabled");
            _enabledItem.Checked = Config.Enabled;
            _enabledItem.CheckOnClick = true;
            _enabledItem.CheckedChanged += delegate { Config.Enabled = _enabledItem.Checked; };

            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
            settingsItem.Click += delegate { ShowSettings(); };

            var launchItem = new ToolStripMenuItem("Launch Xbox mode now");
            launchItem.Click += delegate { SendWinF11(); };

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { ExitThread(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(settingsItem);
            menu.Items.Add(_enabledItem);
            menu.Items.Add(launchItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _tray = new NotifyIcon();
            _tray.Icon = CreateTrayIcon();
            _tray.Text = "WakeToXbox";
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowSettings(); };
            _tray.Visible = true;

            if (Config.IsFirstRun)
            {
                _tray.ShowBalloonTip(4000, "WakeToXbox",
                    "Running in the system tray. Double-click the icon to configure.", ToolTipIcon.Info);
                ShowSettings();
            }
        }

        public void RefreshEnabledState()
        {
            _enabledItem.Checked = Config.Enabled;
        }

        void ShowSettings()
        {
            if (_settings == null || _settings.IsDisposed)
                _settings = new SettingsForm(this);
            _settings.Show();
            _settings.WindowState = FormWindowState.Normal;
            _settings.Activate();
        }

        // Both wake signals (resume broadcast, display-on) can fire for the same
        // wake; the busy flag and _lastHandledWakeUtc keep it from running twice.
        internal void OnWakeSignal(string trigger)
        {
            Log("wake signal: " + trigger + (Config.Enabled ? "" : " (automation disabled, ignoring)"));
            if (Config.Enabled)
                RunWakeSequence(false);
        }

        // The full wake flow: confirm the wake source, overlay up, wait out the
        // lock screen and the shell, send Win+F11. The overlay waits for the match
        // because the display-on trigger also fires for ordinary screen-on.
        // skipEventCheck is used by the "Test" button.
        internal async void RunWakeSequence(bool skipEventCheck)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                Log("wake sequence already running, skipping");
                return;
            }
            try
            {
                bool matched = true;
                if (!skipEventCheck)
                {
                    var cutoffUtc = DateTime.UtcNow.AddSeconds(-15);
                    var wanted = Config.WakeSource;
                    var evt = await Task.Run(() => WaitForMatchingWake(cutoffUtc, wanted));
                    matched = evt != null && evt.TimeUtc > _lastHandledWakeUtc;
                    if (evt != null && !matched)
                        Log("wake event already handled, skipping");
                    if (matched)
                        _lastHandledWakeUtc = evt.TimeUtc;
                }

                if (matched)
                {
                    Log("wake matched, launching");
                    ShowOverlays();
                    if (!await WaitForLockScreenGone())
                    {
                        Log("lock screen never went away, giving up");
                        return;
                    }
                    await WaitForExplorer();
                    await Task.Delay(500);
                    Log("sending Win+F11");
                    SendWinF11();
                    await Task.Delay(3000);
                }
            }
            catch (Exception ex)
            {
                Log("wake sequence error: " + ex.Message);
            }
            finally
            {
                HideOverlays();
                _busy = 0;
            }
        }

        // Polls until a wake event newer than the cutoff shows up (the log entry can
        // lag the resume by a few seconds), then checks its source. 12s deadline.
        static WakeEvent WaitForMatchingWake(DateTime cutoffUtc, string wanted)
        {
            WakeEvent newest = null;
            var deadline = Environment.TickCount + 12000;
            while (Environment.TickCount < deadline)
            {
                try { newest = WakeEvents.GetLatest(); }
                catch { }

                if (newest != null && newest.TimeUtc >= cutoffUtc)
                {
                    bool match = newest.SourceText.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
                    Log("wake event \"" + newest.SourceText + "\" -> " + (match ? "match" : "no match"));
                    return match ? newest : null;
                }

                Thread.Sleep(750);
            }
            Log("no wake event newer than cutoff; newest seen: "
                + (newest == null ? "(none)" : "\"" + newest.SourceText + "\" at " + newest.TimeUtc.ToLocalTime()));
            return null;
        }

        // Injected keystrokes can't reach the lock-screen curtain on the secure
        // desktop, so wait for LogonUI.exe (which runs exactly while the curtain
        // is up) to exit before sending Win+F11.
        static async Task<bool> WaitForLockScreenGone()
        {
            var deadline = Environment.TickCount + 120000;
            bool seen = false;
            while (Environment.TickCount < deadline)
            {
                var procs = Process.GetProcessesByName("LogonUI");
                var present = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                if (!present)
                {
                    if (seen) Log("lock screen dismissed");
                    return true;
                }
                if (!seen) { seen = true; Log("lock screen up, waiting"); }
                await Task.Delay(300);
            }
            return false;
        }

        static async Task WaitForExplorer()
        {
            var deadline = Environment.TickCount + 6000;
            while (Environment.TickCount < deadline)
            {
                var procs = Process.GetProcessesByName("explorer");
                var running = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                if (running) return;
                await Task.Delay(200);
            }
        }

        void ShowOverlays()
        {
            if (!Config.ShowOverlay) return;
            foreach (var screen in Screen.AllScreens)
            {
                var overlay = new OverlayForm(screen.Bounds);
                overlay.Show();
                _overlays.Add(overlay);
            }
        }

        void HideOverlays()
        {
            foreach (var overlay in _overlays)
                overlay.Dispose();
            _overlays.Clear();
        }

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        const byte VK_LWIN = 0x5B;
        const byte VK_F11 = 0x7A;
        const uint KEYEVENTF_KEYUP = 0x0002;

        internal static void SendWinF11()
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_F11, 0, 0, UIntPtr.Zero);
            Thread.Sleep(100);
            keybd_event(VK_F11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // The tray shows the exe's own embedded icon (built from assets/icon.png by
        // build.ps1), so the tray and the file icon can never drift apart.
        static Icon CreateTrayIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                    return icon;
            }
            catch { }

            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var brush = new SolidBrush(Color.FromArgb(0, 180, 90)))
                        g.FillEllipse(brush, 1, 1, 30, 30);
                }
                var handle = bmp.GetHicon();
                using (var temp = Icon.FromHandle(handle))
                {
                    var icon = (Icon)temp.Clone();
                    DestroyIcon(handle);
                    return icon;
                }
            }
        }

        protected override void ExitThreadCore()
        {
            Log("exiting");
            _tray.Visible = false;
            _tray.ContextMenuStrip.Dispose();
            _tray.Dispose();
            _messages.Dispose();
            base.ExitThreadCore();
        }

        // Hidden top-level window that receives WM_POWERBROADCAST; also registers
        // for display-state changes, the wake signal that fires on Modern Standby.
        sealed class MessageWindow : Form
        {
            const int WM_POWERBROADCAST = 0x218;
            const int PBT_APMSUSPEND = 0x4;
            const int PBT_APMRESUMESUSPEND = 0x7;
            const int PBT_APMRESUMEAUTOMATIC = 0x12;
            const int PBT_POWERSETTINGCHANGE = 0x8013;
            const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

            static readonly Guid GuidConsoleDisplayState =
                new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");

            [StructLayout(LayoutKind.Sequential, Pack = 4)]
            struct POWERBROADCAST_SETTING
            {
                public Guid PowerSetting;
                public uint DataLength;
                public byte Data;
            }

            [DllImport("user32.dll", SetLastError = true)]
            static extern IntPtr RegisterPowerSettingNotification(
                IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

            readonly TrayContext _ctx;
            bool _displayWasOff;

            public MessageWindow(TrayContext ctx)
            {
                _ctx = ctx;
                ShowInTaskbar = false;
                // Never shown; force handle creation so we get broadcasts.
                CreateHandle();

                var guid = GuidConsoleDisplayState;
                var result = RegisterPowerSettingNotification(Handle, ref guid, DEVICE_NOTIFY_WINDOW_HANDLE);
                if (result == IntPtr.Zero)
                    Log("RegisterPowerSettingNotification failed: " + Marshal.GetLastWin32Error());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_POWERBROADCAST)
                {
                    var code = m.WParam.ToInt64();
                    if (code == PBT_APMRESUMEAUTOMATIC || code == PBT_APMRESUMESUSPEND)
                    {
                        Log("power broadcast: resume (0x" + code.ToString("X") + ")");
                        _ctx.OnWakeSignal("resume broadcast");
                    }
                    else if (code == PBT_APMSUSPEND)
                    {
                        Log("power broadcast: suspend");
                        _displayWasOff = true;
                    }
                    else if (code == PBT_POWERSETTINGCHANGE)
                    {
                        var setting = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(
                            m.LParam, typeof(POWERBROADCAST_SETTING));
                        if (setting.PowerSetting == GuidConsoleDisplayState)
                        {
                            Log("display state: " + setting.Data);
                            if (setting.Data == 0)
                            {
                                _displayWasOff = true;
                            }
                            else if (setting.Data == 1 && _displayWasOff)
                            {
                                _displayWasOff = false;
                                _ctx.OnWakeSignal("display on");
                            }
                        }
                    }
                }
                base.WndProc(ref m);
            }
        }
    }
}
