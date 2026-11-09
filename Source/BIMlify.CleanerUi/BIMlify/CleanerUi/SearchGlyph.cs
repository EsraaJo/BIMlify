using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BIMlify.SearchControls
{
    public sealed class SearchGlyph : NativeWindow
    {
        private readonly TextBox _box;
        private readonly string _prompt;
        private readonly bool _drawIcon, _externalIcon;
        private static readonly Font PromptFont = new Font("Segoe UI", 8f);
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, SearchGlyph> Hooks =
            new System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, SearchGlyph>();

        private SearchGlyph(TextBox box, string prompt, bool drawIcon, bool externalIcon)
        {
            _box = box;
            _prompt = prompt;
            _drawIcon = drawIcon; _externalIcon = externalIcon;
            box.HandleCreated += (sender, args) => Connect();
            box.HandleDestroyed += (sender, args) => ReleaseHandle();
            box.SizeChanged += (sender, args) => ApplyMargin();
            box.TextChanged += (sender, args) => box.Invalidate();
            box.GotFocus += (sender, args) => box.Invalidate();
            box.LostFocus += (sender, args) => box.Invalidate();
            if (box.IsHandleCreated) Connect();
        }

        public static void Attach(TextBox box, string prompt = null, bool drawIcon = true, bool externalIcon = false)
        {
            prompt = prompt ?? box.AccessibleName ?? "Search";
            box.AccessibleName = prompt;
            Hooks.GetValue(box, control => new SearchGlyph(control, prompt, drawIcon, externalIcon));
        }

        private void Connect()
        {
            SetCueBanner(_box.Handle, 0x1501, IntPtr.Zero, "");
            AssignHandle(_box.Handle);
            ApplyMargin();
        }

        private int IconSize => Math.Max(8, Math.Min(15, _box.ClientSize.Height / 2));

        private void ApplyMargin()
        {
            if (Handle == IntPtr.Zero) return;
            SendMessage(Handle, 0x00D3, (IntPtr)1, (IntPtr)(_externalIcon ? 2 : IconSize + 12));
            _box.Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x1501)
            {
                message.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref message);
            if (message.Msg != 0x000F || Handle == IntPtr.Zero) return;

            int size = IconSize;
            float diameter = size * 0.69f;
            float top = (_box.ClientSize.Height - size) / 2f;
            using (var graphics = Graphics.FromHwnd(Handle))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (_drawIcon)
                {
                    using var pen = new Pen(Color.FromArgb(204, 158, 54), 1f);
                    graphics.DrawEllipse(pen, 6, top, diameter, diameter);
                    graphics.DrawLine(pen, 6 + diameter * .84f, top + diameter * .84f, 6 + size, top + size);
                }
                int left = _externalIcon ? 3 : IconSize + 14;
                int width = _box.ClientSize.Width - left - 4;
                if (_box.TextLength == 0 && width > 0)
                {
                    var bounds = new Rectangle(left, 1, width, Math.Max(0, _box.ClientSize.Height - 2));
                    using var background = new SolidBrush(_box.BackColor);
                    graphics.FillRectangle(background, bounds);
                    TextRenderer.DrawText(graphics, _prompt, PromptFont, bounds, Color.FromArgb(105, 117, 129),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SetCueBanner(IntPtr handle, int message, IntPtr wParam, string text);
    }
}
