using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WakeToXbox
{
    sealed class SettingsForm : Form
    {
        readonly TrayContext _ctx;
        TextBox _wakeSource;
        ListView _events;
        CheckBox _enabled;
        CheckBox _overlay;
        CheckBox _startup;
        Button _refresh;

        public SettingsForm(TrayContext ctx)
        {
            _ctx = ctx;

            Text = "WakeToXbox Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 470);
            Font = new Font("Segoe UI", 9f);

            var sourceLabel = new Label();
            sourceLabel.Text = "Wake source text (the wake counts as \"controller\" when its source contains this):";
            sourceLabel.SetBounds(15, 15, 510, 18);

            _wakeSource = new TextBox();
            _wakeSource.SetBounds(15, 38, 510, 24);

            var eventsLabel = new Label();
            eventsLabel.Text = "Recent wake events — click one to use it as the wake source:";
            eventsLabel.SetBounds(15, 75, 400, 18);

            _refresh = new Button();
            _refresh.Text = "Refresh";
            _refresh.SetBounds(440, 70, 85, 26);
            _refresh.Click += async delegate { await LoadEventsAsync(); };

            _events = new ListView();
            _events.View = View.Details;
            _events.FullRowSelect = true;
            _events.MultiSelect = false;
            _events.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _events.SetBounds(15, 102, 510, 180);
            _events.Columns.Add("Time", 140);
            _events.Columns.Add("Wake source", 345);
            _events.SelectedIndexChanged += delegate
            {
                if (_events.SelectedItems.Count > 0)
                {
                    var source = _events.SelectedItems[0].SubItems[1].Text;
                    if (source.Length > 0 && source != "(unknown)")
                        _wakeSource.Text = source;
                }
            };

            _enabled = new CheckBox();
            _enabled.Text = "Launch Xbox fullscreen mode when the controller wakes the PC";
            _enabled.SetBounds(15, 295, 510, 22);

            _overlay = new CheckBox();
            _overlay.Text = "Hide the desktop with a black overlay during the transition";
            _overlay.SetBounds(15, 320, 510, 22);

            _startup = new CheckBox();
            _startup.Text = "Start WakeToXbox automatically when I sign in to Windows";
            _startup.SetBounds(15, 345, 510, 22);

            var testButton = new Button();
            testButton.Text = "Test now";
            testButton.SetBounds(15, 420, 100, 30);
            testButton.Click += delegate { RunTest(); };

            var saveButton = new Button();
            saveButton.Text = "Save";
            saveButton.SetBounds(330, 420, 95, 30);
            saveButton.Click += delegate { SaveAndClose(); };

            var cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.SetBounds(430, 420, 95, 30);
            cancelButton.Click += delegate { Close(); };

            var hint = new Label();
            hint.Text = "Tip: put the PC to sleep, wake it with the controller, then hit Refresh — the newest\r\nentry at the top is your controller's wake source.";
            hint.ForeColor = SystemColors.GrayText;
            hint.SetBounds(15, 375, 510, 34);

            Controls.AddRange(new Control[]
            {
                sourceLabel, _wakeSource, eventsLabel, _refresh, _events,
                _enabled, _overlay, _startup, hint, testButton, saveButton, cancelButton
            });

            AcceptButton = saveButton;
            CancelButton = cancelButton;

            Load += async delegate
            {
                _wakeSource.Text = Config.WakeSource;
                _enabled.Checked = Config.Enabled;
                _overlay.Checked = Config.ShowOverlay;
                _startup.Checked = Config.StartWithWindows;
                await LoadEventsAsync();
            };
        }

        async Task LoadEventsAsync()
        {
            _refresh.Enabled = false;
            _events.Items.Clear();
            _events.Items.Add(new ListViewItem(new[] { "Loading...", "" }));
            try
            {
                var events = await Task.Run(() => WakeEvents.GetRecent(15));
                _events.Items.Clear();
                foreach (var evt in events)
                {
                    var time = evt.TimeUtc.ToLocalTime().ToString("MMM d, h:mm:ss tt");
                    var source = evt.SourceText.Length > 0 ? evt.SourceText : "(unknown)";
                    _events.Items.Add(new ListViewItem(new[] { time, source }));
                }
                if (events.Count == 0)
                    _events.Items.Add(new ListViewItem(new[] { "-", "No wake events found. Sleep and wake the PC first." }));
            }
            catch (Exception ex)
            {
                _events.Items.Clear();
                _events.Items.Add(new ListViewItem(new[] { "Error", ex.Message }));
            }
            finally
            {
                _refresh.Enabled = true;
            }
        }

        void RunTest()
        {
            var result = MessageBox.Show(
                "This runs the real wake sequence: a brief black overlay, then Win+F11 to launch " +
                "Xbox fullscreen mode.\r\n\r\nContinue?",
                "WakeToXbox — Test", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (result == DialogResult.OK)
                _ctx.RunWakeSequence(true);
        }

        void SaveAndClose()
        {
            var source = _wakeSource.Text.Trim();
            if (source.Length == 0)
            {
                MessageBox.Show("The wake source text can't be empty.", "WakeToXbox",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Config.WakeSource = source;
            Config.Enabled = _enabled.Checked;
            Config.ShowOverlay = _overlay.Checked;
            Config.StartWithWindows = _startup.Checked;
            _ctx.RefreshEnabledState();
            Close();
        }
    }
}
