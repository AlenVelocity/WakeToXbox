using System.Drawing;
using System.Windows.Forms;

namespace WakeToXbox
{
    // Fullscreen black window that hides the desktop during the wake transition.
    // WS_EX_NOACTIVATE keeps it from stealing focus so Win+F11 lands normally.
    sealed class OverlayForm : Form
    {
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        public OverlayForm(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = Color.Black;
            TopMost = true;
            ShowInTaskbar = false;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }
}
