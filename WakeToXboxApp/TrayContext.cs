using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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

        public TrayContext()
        {
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

        internal void OnResume()
        {
            if (Config.Enabled)
                RunWakeSequence(false);
        }

        // The full wake flow: overlay up, confirm the wake came from the controller,
        // wait for the shell, send Win+F11, overlay down. skipEventCheck is used by
        // the "Test" button to run the sequence without a real wake event.
        internal async void RunWakeSequence(bool skipEventCheck)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return;
            try
            {
                ShowOverlays();

                bool matched = true;
                if (!skipEventCheck)
                {
                    var cutoffUtc = DateTime.UtcNow.AddSeconds(-5);
                    var wanted = Config.WakeSource;
                    matched = await Task.Run(() => WaitForMatchingWake(cutoffUtc, wanted));
                }

                if (matched)
                {
                    await WaitForExplorer();
                    await Task.Delay(500);
                    SendWinF11();
                    await Task.Delay(3000);
                }
            }
            catch { }
            finally
            {
                HideOverlays();
                _busy = 0;
            }
        }

        // Polls until a wake event newer than the cutoff shows up (the log entry can
        // lag the resume by a few seconds), then checks its source. 12s deadline.
        static bool WaitForMatchingWake(DateTime cutoffUtc, string wanted)
        {
            var deadline = Environment.TickCount + 12000;
            while (Environment.TickCount < deadline)
            {
                WakeEvent evt = null;
                try { evt = WakeEvents.GetLatest(); }
                catch { }

                if (evt != null && evt.TimeUtc >= cutoffUtc)
                    return evt.SourceText.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;

                Thread.Sleep(750);
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
            _tray.Visible = false;
            _tray.ContextMenuStrip.Dispose();
            _tray.Dispose();
            _messages.Dispose();
            base.ExitThreadCore();
        }

        // Hidden top-level window that receives the WM_POWERBROADCAST broadcast.
        sealed class MessageWindow : Form
        {
            const int WM_POWERBROADCAST = 0x218;
            const int PBT_APMRESUMEAUTOMATIC = 0x12;

            readonly TrayContext _ctx;

            public MessageWindow(TrayContext ctx)
            {
                _ctx = ctx;
                ShowInTaskbar = false;
                // Never shown; force handle creation so we get broadcasts.
                CreateHandle();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt64() == PBT_APMRESUMEAUTOMATIC)
                    _ctx.OnResume();
                base.WndProc(ref m);
            }
        }
    }
}
