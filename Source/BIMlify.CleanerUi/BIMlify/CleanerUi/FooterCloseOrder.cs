using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace BIMlify.CleanerUi
{
    public static class FooterCloseOrder
    {
        internal const int RightInset = 18;
        internal const int ButtonGap = 12;

        private static readonly ConditionalWeakTable<Panel, object> Attached =
            new ConditionalWeakTable<Panel, object>();

        public static void Attach(Panel group)
        {
            Attached.GetValue(group, panel =>
            {
                panel.VisibleChanged += (sender, args) => Queue(panel);
                panel.SizeChanged += (sender, args) => Queue(panel);
                Queue(panel);
                return new object();
            });
        }

        private static void Queue(Panel group)
        {
            if (group.IsDisposed || !group.IsHandleCreated || !group.Visible) return;
            group.BeginInvoke(new Action(() => MoveCloseRight(group)));
        }

        private static void MoveCloseRight(Panel group)
        {
            if (group.IsDisposed || group.Dock != DockStyle.Right) return;
            var buttons = group.Controls.OfType<Button>().Where(button => button.Visible)
                .OrderBy(button => button.Left).ToArray();
            var close = buttons.FirstOrDefault(button =>
                String.Equals(button.Text.Trim(), "Close", StringComparison.OrdinalIgnoreCase));
            if (close == null || group.Parent == null) return;

            var row = buttons.Where(button => button.Top == close.Top && button.Dock == DockStyle.None).ToArray();
            if (!row.Contains(close)) return;
            int rightmost = row.Last().Right;
            // Convert the desired footer right edge to coordinates inside the button group.
            int targetRight = Math.Min(group.ClientSize.Width,
                group.Parent.ClientSize.Width - group.Left - RightInset);
            int right = Math.Max(rightmost, targetRight);
            if (row.Last() == close && right == rightmost) return;
            var ordered = row.Where(button => button != close).Concat(new[] { close }).ToArray();
            int gap = ButtonGap;
            if (ordered.Length > 1)
                gap = Math.Min(gap, Math.Max(0, (right - ordered.Sum(button => button.Width)) / (ordered.Length - 1)));
            for (int i = ordered.Length - 1; i >= 0; i--)
            {
                var button = ordered[i];
                button.Left = right - button.Width;
                right = button.Left - gap;
            }
        }
    }
}
