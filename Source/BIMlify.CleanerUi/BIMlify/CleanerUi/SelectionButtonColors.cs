using System;
using System.Drawing;
using System.Windows.Forms;

namespace BIMlify.CleanerUi
{
    public static class SelectionButtonColors
    {
        private static Color SharedFrame(Color border)
        {
            bool neutral = border.IsEmpty || (border.R <= border.G && border.G <= border.B && border.B - border.R <= 32);
            bool gold = border.R >= 175 && border.R >= border.G && border.G - border.B >= 22;
            return neutral || gold ? Color.FromArgb(144, 151, 158) : border;
        }

        public static void Apply(Button button, ref Color fill, ref Color border)
        {
            ApplyAccent(button.Text.Trim(), button.Enabled, ref fill, ref border);
            if (button.Name == "BIMlifyClashReviewAction") fill = Color.FromArgb(255, 232, 232);
            else if (button.Name == "BIMlifyClashDetailAction") fill = Color.White;
            border = Color.FromArgb(45, SharedFrame(border));
            if (fill.ToArgb() == Color.FromArgb(250, 230, 168).ToArgb() ||
                fill.ToArgb() == Color.FromArgb(255, 239, 190).ToArgb())
                fill = Color.FromArgb(255, 241, 196);
            if (button.Enabled && button.ClientRectangle.Contains(button.PointToClient(Cursor.Position)))
                fill = Mix(fill, fill.GetBrightness() >= 0.97f ? Color.FromArgb(204, 217, 231) : Color.White, 24);
        }

        public static void PaintRaisedFace(Graphics graphics, Brush brush, System.Drawing.Drawing2D.GraphicsPath path)
        {
            using var shadowPath = (System.Drawing.Drawing2D.GraphicsPath)path.Clone();
            using var offset = new System.Drawing.Drawing2D.Matrix();
            offset.Translate(-0.65f, 0.85f);
            shadowPath.Transform(offset);
            using var shadow = new SolidBrush(Color.FromArgb(24, 55, 65, 75));
            var bounds = path.GetBounds();
            var state = graphics.Save();
            try
            {
                graphics.IntersectClip(RectangleF.FromLTRB(bounds.Left - 1f, bounds.Top + 2f,
                    bounds.Right - 2f, bounds.Bottom + 1f));
                graphics.FillPath(shadow, shadowPath);
            }
            finally { graphics.Restore(state); }
            graphics.FillPath(brush, path);
        }

        public static Color CaptionHover(Color fill, bool hovered, bool pressed, int kind)
        {
            if (!hovered && !pressed) return fill;
            if (kind == 2 && !pressed) return fill;
            return Mix(fill, Color.FromArgb(92, 105, 118), pressed ? 17 : 9);
        }

        private static Color Mix(Color from, Color to, int percent)
        {
            int Blend(int a, int b) => (a * (100 - percent) + b * percent) / 100;
            return Color.FromArgb(Blend(from.R, to.R), Blend(from.G, to.G), Blend(from.B, to.B));
        }


        private static bool IsClearSelection(string caption) =>
            String.Equals(caption, "Unselect All", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(caption, "Clear Selection", StringComparison.OrdinalIgnoreCase);

        internal static void ApplyFlatButton(Button button)
        {
            bool clearSelection = IsClearSelection(button.Text.Trim());
            if (clearSelection && button.Text != "Clear Selection")
                button.Text = "Clear Selection";
            if (clearSelection && button.GetType() == typeof(Button))
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Color.FromArgb(251, 225, 225);
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Color.FromArgb(196, 134, 134);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 236, 236);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(245, 215, 215);
                return;
            }
            if (button.GetType() != typeof(Button) || button.FlatStyle != FlatStyle.Flat ||
                button.FlatAppearance.BorderSize == 0) return;

            var appearance = button.FlatAppearance;
            appearance.BorderColor = Mix(button.BackColor, SharedFrame(appearance.BorderColor), 16);
            Color fill = button.BackColor;

            if (appearance.MouseOverBackColor.IsEmpty || appearance.MouseOverBackColor == fill)
                appearance.MouseOverBackColor = Mix(fill,
                    fill.GetBrightness() >= 0.97f ? Color.FromArgb(204, 217, 231) : Color.White, 24);
        }

        private static void ApplyAccent(string caption, bool enabled, ref Color fill, ref Color border)
        {
            if (!enabled) return;
            if (String.Equals(caption, "Discipline A", StringComparison.OrdinalIgnoreCase) || String.Equals(caption, "Discipline B", StringComparison.OrdinalIgnoreCase))
            {
                fill = Color.FromArgb(250, 230, 168);
                border = Color.FromArgb(225, 197, 128);
                return;
            }
            if (String.Equals(caption, "Select hosts and services", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(caption, "Auto-create all openings", StringComparison.OrdinalIgnoreCase))
            {
                fill = Color.FromArgb(255, 241, 196);
                return;
            }
            if (String.Equals(caption, "Select All", StringComparison.OrdinalIgnoreCase))
            {
                fill = Color.FromArgb(250, 230, 168);
                border = Color.FromArgb(198, 151, 42);
            }
            else if (IsClearSelection(caption))
            {
                fill = Color.FromArgb(251, 225, 225);
                border = Color.FromArgb(196, 134, 134);
            }
        }
    }
}
