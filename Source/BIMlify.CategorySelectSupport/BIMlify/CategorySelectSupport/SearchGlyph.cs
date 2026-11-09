using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BIMlify.SearchControls
{
    internal sealed class SearchGlyph : NativeWindow
    {
        private readonly TextBox _box;

        private SearchGlyph(TextBox box)
        {
            _box = box;
            box.HandleCreated += (sender, args) => Connect();
            box.HandleDestroyed += (sender, args) => ReleaseHandle();
            box.SizeChanged += (sender, args) => ApplyMargin();
            if (box.IsHandleCreated) Connect();
        }

        internal static void Attach(TextBox box) => new SearchGlyph(box);

        private void Connect()
        {
            AssignHandle(_box.Handle);
            ApplyMargin();
        }

        private int IconSize => Math.Max(8, Math.Min(15, _box.ClientSize.Height / 2));

        private void ApplyMargin()
        {
            if (Handle == IntPtr.Zero) return;
            SendMessage(Handle, 0x00D3, (IntPtr)1, (IntPtr)(IconSize + 12));
            _box.Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != 0x000F || Handle == IntPtr.Zero) return;

            int size = IconSize;
            float diameter = size * 0.69f;
            float top = (_box.ClientSize.Height - size) / 2f;
            using (var graphics = Graphics.FromHwnd(Handle))
            using (var pen = new Pen(Color.FromArgb(204, 158, 54), 1f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawEllipse(pen, 6, top, diameter, diameter);
                graphics.DrawLine(pen, 6 + diameter * .84f, top + diameter * .84f, 6 + size, top + size);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    }
}
