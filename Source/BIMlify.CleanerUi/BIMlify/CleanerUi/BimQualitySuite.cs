using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using BIMlify.ProgressOverlay;
using Color = System.Drawing.Color;
using Form = System.Windows.Forms.Form;
using Panel = System.Windows.Forms.Panel;
using Rectangle = System.Drawing.Rectangle;
using TextBox = System.Windows.Forms.TextBox;
using View = Autodesk.Revit.DB.View;

namespace BIMlify.CleanerUi
{
    internal static class BimQualitySuite
    {
        private static ExternalEvent _externalEvent;
        private static SuiteHandler _handler;
        private static SuiteForm _window;
        private static Form _layoutDashboard;
        private static readonly Dictionary<System.Windows.Forms.Control, bool> DashboardVisibility = new Dictionary<System.Windows.Forms.Control, bool>();
        private static readonly ConditionalWeakTable<Panel, object> DashboardTileShadows = new ConditionalWeakTable<Panel, object>();
        private static readonly object TraceSync = new object();
        private static readonly List<TraceRecord> TraceRows = new List<TraceRecord>();
        private static UIApplication TraceApplication;
        private static Form _traceMonitor;
        private static string TraceMonitorDocumentTitle = "";
        private static string TraceDocumentKey = "";
        private static string TraceFile = "";
        private static string TraceUser = "";
        private static bool TraceTracking;
        private static int TraceCurrentSessionCount;
        private static string _dashboardTitle;
        private static readonly string[] ToolNames = {
            "Door & Window Audit", "Finishes Checker",
            "Level & Grid Audit", "LOD & Deliverables Checker", "Model Health Score",
            "Naming Convention Checker", "Opening Checker", "Parameter Audit",
            "Revision Manager", "Room Data Checker", "Sheet Audit", "Workset Checker", "Project Standards"
        };
        private static readonly string[] StandaloneToolNames = { "PDF Batch Export", "Model Change Log", "Parameter Mapping", "BIM Task Planner", "Smart Links", "Quantity Reports" };
        private static readonly string[] StandaloneTitles = { "Smart Export", "Smart Log", "Smart Map", "Smart Planner", "Smart Links", "Quantity Reports" };

        private static void EnableTextClipboard(TextBox box)
        {
            if (box == null) return;
            box.ShortcutsEnabled = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem cut = new ToolStripMenuItem("Cut", null, (s, e) => { try { if (box.SelectionLength > 0) box.Cut(); } catch { } });
            ToolStripMenuItem copy = new ToolStripMenuItem("Copy", null, (s, e) => { try { if (box.SelectionLength > 0) box.Copy(); } catch { } });
            ToolStripMenuItem paste = new ToolStripMenuItem("Paste", null, (s, e) => { try { if (Clipboard.ContainsText()) box.Paste(); } catch { } });
            ToolStripMenuItem selectAll = new ToolStripMenuItem("Select all", null, (s, e) => box.SelectAll());
            menu.Items.AddRange(new ToolStripItem[] { cut, copy, paste, new ToolStripSeparator(), selectAll });
            menu.Opening += (s, e) =>
            {
                cut.Enabled = box.Enabled && !box.ReadOnly && box.SelectionLength > 0;
                copy.Enabled = box.SelectionLength > 0;
                try { paste.Enabled = box.Enabled && !box.ReadOnly && Clipboard.ContainsText(); } catch { paste.Enabled = false; }
                selectAll.Enabled = box.TextLength > 0;
            };
            box.ContextMenuStrip = menu;
            // Explicitly handle the common editing shortcuts as Revit or an add-in
            // can consume accelerator keys before a hosted TextBox sees them.
            box.KeyDown += (s, e) =>
            {
                if (!e.Control) return;
                try
                {
                    if (e.KeyCode == Keys.A) { box.SelectAll(); e.Handled = true; e.SuppressKeyPress = true; }
                    else if (e.KeyCode == Keys.C && box.SelectionLength > 0) { box.Copy(); e.Handled = true; e.SuppressKeyPress = true; }
                    else if (e.KeyCode == Keys.X && !box.ReadOnly && box.SelectionLength > 0) { box.Cut(); e.Handled = true; e.SuppressKeyPress = true; }
                    else if (e.KeyCode == Keys.V && !box.ReadOnly && Clipboard.ContainsText()) { box.SelectedText = Clipboard.GetText(); e.Handled = true; e.SuppressKeyPress = true; }
                }
                catch { }
            };
            box.Disposed += (s, e) => menu.Dispose();
        }

        private sealed class GoldCheckBox : CheckBox
        {
            internal GoldCheckBox()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                AutoSize = false;
                FlatStyle = FlatStyle.Flat;
                BackColor = Color.FromArgb(238, 241, 245);
            }

            protected override void OnCheckedChanged(EventArgs e)
            {
                base.OnCheckedChanged(e);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                int size = 16;
                int x = 2;
                int y = Math.Max(2, (ClientSize.Height - size) / 2);
                Rectangle box = new Rectangle(x, y, size, size);
                Color border = Checked ? Color.FromArgb(199, 151, 52) : Color.FromArgb(119, 129, 138);
                Color fill = Checked ? Color.FromArgb(246, 220, 150) : Color.White;
                using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, box);
                using (Pen pen = new Pen(border, 1f)) e.Graphics.DrawRectangle(pen, box);
                if (Checked)
                {
                    using (Pen check = new Pen(Color.FromArgb(35, 39, 42), 2f))
                    {
                        check.StartCap = LineCap.Round; check.EndCap = LineCap.Round;
                        e.Graphics.DrawLines(check, new[] { new System.Drawing.Point(x + 3, y + 8), new System.Drawing.Point(x + 7, y + 12), new System.Drawing.Point(x + 13, y + 4) });
                    }
                }
                Rectangle textBounds = new Rectangle(x + size + 8, 0, Math.Max(0, ClientSize.Width - x - size - 8), ClientSize.Height);
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : SystemColors.GrayText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(textBounds.Left, 1, Math.Max(0, textBounds.Width - 1), Math.Max(0, textBounds.Height - 2)));
            }
        }

        internal sealed class PdfSheetInfo
        {
            internal long Id;
            internal string Number;
            internal string Name;
            internal string ProjectName; internal string ProjectIdentity, UniqueId;
            public override string ToString() => Number + "  |  " + Name;
        }

        internal sealed class PdfExportRequest
        {
            internal bool ExportDwg;
            internal string Folder;
            internal string Template;
            internal bool Combine;
            internal List<long> SheetIds;
            internal string PaperFormat;
            internal string Orientation;
            internal string ColorDepth;
            internal string PaperPlacement;
            internal bool HideCropBoundaries;
            internal bool AlwaysUseRaster;
            internal bool HideScopeBoxes;
            internal bool HideReferencePlane;
            internal bool HideUnreferencedViewTags;
            internal bool ReplaceHalftoneWithThinLines;
            internal bool MaskCoincidentLines;
        }

        internal sealed class NwcExportRequest
        {
            internal string Folder;
            internal string FileNameTemplate;
            internal string Scope;
            internal long ViewId;
            internal List<long> ViewIds = new List<long>();
            internal string Coordinates;
            internal string Parameters;
            internal bool ExportLinks;
            internal bool ExportElementIds;
            internal bool ExportRooms;
            internal bool DivideByLevels;
            internal bool ConvertLinkedCad;
            internal bool ExportElementProperties;
            internal bool ExportRoomAsAttribute;
            internal bool FindMissingMaterials;
            internal bool ExportUrls;
            internal bool ExportParts;
            internal bool ConvertLights;
            internal bool HideShaftOpeningLines;
            internal bool HideMasses;
            internal bool HideLines;
        internal bool OpenOutputAfterExport;
            internal double FacetingFactor = 1.0;
        }

        internal sealed class NwcViewInfo
        {
            internal long Id;
            internal string Name;
            public override string ToString() => Name ?? "3D View";
        }

        internal static void AttachDashboard(Form dashboard)
        {
            if (dashboard == null || dashboard.IsDisposed) return;
            FlowLayoutPanel flow = DashboardEnhancements.Descendants(dashboard).OfType<FlowLayoutPanel>()
                .FirstOrDefault(x => x.Controls.OfType<Panel>().Any(IsExistingTile) && x.Controls.OfType<Panel>().Count(IsExistingTile) >= 3);
            if (flow == null) return;
            if (flow.Controls.ContainsKey("BIMlifyQaQcTile")) { AddStandaloneDashboardTiles(dashboard); return; }
            Panel tile = new Panel {
                Name = "BIMlifyQaQcTile", Width = 84, Height = 94, Margin = new Padding(2),
                BackColor = Color.White, Cursor = Cursors.Hand, TabStop = false, AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Smart QA/QC"
            };
            PictureBox icon = new PictureBox { Width = 52, Height = 48, Left = 16, Top = 1, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            try {
                string path = Path.Combine(Path.GetDirectoryName(typeof(BimQualitySuite).Assembly.Location), "Icons", "smartqaqc.png");
                if (File.Exists(path)) using (Image img = Image.FromFile(path)) using (Bitmap source = new Bitmap(img))
                {
                    int left = source.Width, top = source.Height, right = -1, bottom = -1;
                    for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
                        if (source.GetPixel(x, y).A > 0) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
                    if (right >= left && bottom >= top)
                    {
                        // Keep the original transparent shadow space so the glyph stays the same size after its raster shadow is removed.
                        int cropBottom = Math.Max(bottom, source.Height - 8);
                        icon.Image = source.Clone(new Rectangle(left, top, right - left + 1, cropBottom - top + 1), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    }
                }
            } catch { }
            Label label = new Label { Text = "Smart\nQA/QC", Width = 82, Height = 34, Left = 1, Top = 58,
                TextAlign = ContentAlignment.TopCenter, ForeColor = Color.FromArgb(78, 85, 92), BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.5f), AutoEllipsis = true };
            tile.Controls.Add(icon); tile.Controls.Add(label);
            ToolTip tileTips = new ToolTip { InitialDelay = 550, ReshowDelay = 120, AutoPopDelay = 9000, ShowAlways = true, IsBalloon = false, OwnerDraw = true };
            Font tileTipFont = new Font("Segoe UI", 8f, FontStyle.Regular);
            tileTips.Popup += (s, e) =>
            {
                Size measured = TextRenderer.MeasureText(tileTips.GetToolTip(e.AssociatedControl), tileTipFont, new Size(320, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                e.ToolTipSize = new Size(Math.Min(340, Math.Max(150, measured.Width + 16)), Math.Min(96, measured.Height + 12));
            };
            tileTips.Draw += (s, e) =>
            {
                e.DrawBackground(); e.DrawBorder();
                Rectangle bounds = e.Bounds; bounds.Inflate(-8, -5);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, tileTipFont, bounds, Color.FromArgb(78, 85, 92), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            };
            const string tileDescription = "Review model quality, coordination, and delivery requirements with Smart QA/QC checks.";
            tileTips.SetToolTip(tile, tileDescription);
            tileTips.SetToolTip(icon, tileDescription);
            tileTips.SetToolTip(label, tileDescription);
            tile.Disposed += (s, e) => { tileTips.Dispose(); tileTipFont.Dispose(); };
            tile.Click += (s, e) => Open(null, null); icon.Click += (s, e) => Open(null, null); label.Click += (s, e) => Open(null, null);
            bool hovered = false;
            int iconTarget = 48;
            Timer iconMotion = new Timer { Interval = 15 };
            iconMotion.Tick += (s, e) =>
            {
                int height = icon.Height;
                if (height == iconTarget) { iconMotion.Stop(); return; }
                height += height < iconTarget ? 3 : -3;
                if (Math.Abs(height - iconTarget) < 3) height = iconTarget;
                icon.Height = height;
                icon.Width = (int)Math.Round(52.0 * height / 48.0);
                icon.Left = (tile.Width - icon.Width) / 2;
                icon.Top = Math.Max(0, 1 - (height - 48) / 2);
            };
            Action<bool> setHover = active =>
            {
                if (hovered == active) return;
                hovered = active;
                iconTarget = active ? 56 : 48;
                label.ForeColor = active ? Color.FromArgb(174, 137, 53) : Color.FromArgb(48, 64, 84);
                tile.Invalidate();
                iconMotion.Start();
            };
            EventHandler enterHover = (s, e) => setHover(true);
            EventHandler leaveHover = (s, e) =>
            {
                if (!tile.ClientRectangle.Contains(tile.PointToClient(System.Windows.Forms.Control.MousePosition))) setHover(false);
            };
            tile.MouseEnter += enterHover; icon.MouseEnter += enterHover; label.MouseEnter += enterHover;
            tile.MouseLeave += leaveHover; icon.MouseLeave += leaveHover; label.MouseLeave += leaveHover;
            Timer hoverWatch = new Timer { Interval = 70 };
            hoverWatch.Tick += (s, e) =>
            {
                if (hovered && !tile.ClientRectangle.Contains(tile.PointToClient(System.Windows.Forms.Control.MousePosition))) setHover(false);
            };
            hoverWatch.Start();
            tile.Disposed += (s, e) => { iconMotion.Stop(); iconMotion.Dispose(); hoverWatch.Stop(); hoverWatch.Dispose(); };
            tile.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (hovered)
                {
                    Rectangle bounds = tile.ClientRectangle;
                    bounds.Inflate(-2, -2);
                    int diameter = 16;
                    using (GraphicsPath hoverPath = new GraphicsPath())
                    {
                        hoverPath.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                        hoverPath.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                        hoverPath.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                        hoverPath.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                        hoverPath.CloseFigure();
                        using (SolidBrush hoverFill = new SolidBrush(Color.FromArgb(255, 253, 248)))
                        using (Pen hoverBorder = new Pen(Color.FromArgb(170, 231, 203, 135), 1f))
                        {
                            e.Graphics.FillPath(hoverFill, hoverPath);
                            e.Graphics.DrawPath(hoverBorder, hoverPath);
                        }
                    }
                }
                if (tile.Focused)
                {
                    using (Pen p = new Pen(Color.FromArgb(214,171,61), 1f))
                    {
                        p.DashStyle = DashStyle.Dot;
                        Rectangle r = tile.ClientRectangle; r.Inflate(-2,-2); e.Graphics.DrawRectangle(p,r);
                    }
                }
            };
            AttachUnifiedTileShadow(tile);
            flow.Controls.Add(tile);
            AddStandaloneDashboardTiles(dashboard);
            try {
                if (_handler == null) { _handler = new SuiteHandler(); _externalEvent = ExternalEvent.Create(_handler); }
            } catch { tile.Enabled = false; tile.AccessibleDescription = "Reopen the dashboard after Revit finishes starting."; }
        }

        internal static void OpenFromSearch(string toolName)
        {
            NavigateHome();
            Open(null, null);
            if (_window != null && !_window.IsDisposed && !String.IsNullOrWhiteSpace(toolName))
                _window.SelectTool(toolName);
        }

        internal static void OpenStandaloneTool(string toolName)
        {
            bool openDataChecker = String.Equals(toolName, "Excel ↔ Revit Data Checker", StringComparison.Ordinal);
            string standaloneTool = openDataChecker ? "Parameter Mapping" : toolName;
            int index = Array.IndexOf(StandaloneToolNames, standaloneTool);
            if (index < 0) return;
            NavigateHome();
            Open(standaloneTool, StandaloneTitles[index]);
            if (openDataChecker && _window != null && !_window.IsDisposed) _window.SelectTool("Excel ↔ Revit Data Checker");
        }

        private static bool IsExistingTile(Panel p) => p.Controls.OfType<PictureBox>().Any() && p.Controls.OfType<Label>().Any(x => !String.IsNullOrWhiteSpace(x.Text));

        private static void AddStandaloneDashboardTiles(Form dashboard)
        {
            FlowLayoutPanel[] cards = DashboardEnhancements.Descendants(dashboard).OfType<FlowLayoutPanel>()
                .Where(x => x.Controls.OfType<Panel>().Any(IsExistingTile))
                .Take(3).ToArray();
            if (cards.Length < 3) return;
            foreach (FlowLayoutPanel card in cards)
                foreach (Panel existingTile in card.Controls.OfType<Panel>().Where(IsExistingTile).ToArray())
                    if (!String.Equals(existingTile.Name, "BIMlifyQaQcTile", StringComparison.Ordinal) && !IsLegacyDashboardTile(existingTile)) AttachUnifiedTileShadow(existingTile);
            int[] destinations = { 0, 0, 1, 1, 0, 0 };
            string[] tips = {
                "Export coordinated project deliverables with consistent naming and preflight checks.",
                "Compare model snapshots and review changes.",
                "Map source parameters to target parameters.",
                "Create and review locally saved BIM tasks.",
                "Inspect Revit link status and reload links.",
                "Group element counts and material quantities, then export the report."
            };
            for (int i = 0; i < StandaloneToolNames.Length; i++)
            {
                FlowLayoutPanel flow = cards[destinations[i]];
                string controlName = "BIMlifyStandaloneTile" + i.ToString(CultureInfo.InvariantCulture);
                if (flow.Controls.ContainsKey(controlName)) continue;
                Panel tile = new Panel { Name = controlName, Width = 84, Height = 94, Margin = new Padding(2), BackColor = flow.BackColor, Cursor = Cursors.Hand, TabStop = false, AccessibleRole = AccessibleRole.PushButton, AccessibleName = StandaloneTitles[i] };
                PictureBox icon = new PictureBox { Width = 40, Height = 40, Left = 22, Top = 3, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Cursor = Cursors.Hand };
                icon.Image = CreateStandaloneIcon(i);
                Label label = new Label { Width = 82, Height = 38, Left = 1, Top = 54, Text = StandaloneTitles[i].Replace(" ", "\n"), UseMnemonic = false, TextAlign = ContentAlignment.TopCenter, ForeColor = Color.FromArgb(55, 67, 79), Font = new Font("Segoe UI", 7.5f), BackColor = Color.Transparent, Cursor = Cursors.Hand };
                tile.Controls.Add(icon); tile.Controls.Add(label);
                Font tipFont = new Font("Segoe UI", 8f, FontStyle.Regular);
                ToolTip tip = new ToolTip { InitialDelay = 550, ReshowDelay = 120, AutoPopDelay = 9000, ShowAlways = true, IsBalloon = false, OwnerDraw = true };
                tip.Popup += (s, e) =>
                {
                    Size measured = TextRenderer.MeasureText(tip.GetToolTip(e.AssociatedControl), tipFont, new Size(320, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    e.ToolTipSize = new Size(Math.Min(340, Math.Max(150, measured.Width + 16)), Math.Min(96, measured.Height + 12));
                };
                tip.Draw += (s, e) =>
                {
                    e.DrawBackground(); e.DrawBorder();
                    Rectangle bounds = e.Bounds; bounds.Inflate(-8, -5);
                    TextRenderer.DrawText(e.Graphics, e.ToolTipText, tipFont, bounds, Color.FromArgb(78, 85, 92), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                };
                tip.SetToolTip(tile, tips[i]); tip.SetToolTip(icon, tips[i]); tip.SetToolTip(label, tips[i]);
                tile.Disposed += (s, e) => { tip.Dispose(); tipFont.Dispose(); icon.Image?.Dispose(); };
                int toolIndex = i;
                EventHandler open = (s, e) => OpenStandaloneTool(StandaloneToolNames[toolIndex]);
                tile.Click += open; icon.Click += open; label.Click += open;
                bool hovered = false; int target = 40;
                Timer motion = new Timer { Interval = 15 };
                motion.Tick += (s, e) =>
                {
                    int size = icon.Width;
                    if (size == target) { motion.Stop(); return; }
                    size += size < target ? 3 : -3;
                    if (Math.Abs(size - target) < 3) size = target;
                    icon.Size = new Size(size, size); icon.Left = (tile.Width - size) / 2; icon.Top = Math.Max(0, 3 - (size - 42) / 2);
                };
                Action<bool> setHover = active =>
                {
                    if (hovered == active) return;
                    hovered = active; target = active ? 46 : 40;
                    tile.BackColor = flow.BackColor; icon.BackColor = Color.Transparent; label.BackColor = Color.Transparent;
                    label.ForeColor = active ? Color.FromArgb(174, 137, 53) : Color.FromArgb(55, 67, 79);
                    tile.Invalidate(); motion.Start();
                };
                EventHandler enter = (s, e) => setHover(true);
                EventHandler leave = (s, e) => { if (!tile.ClientRectangle.Contains(tile.PointToClient(System.Windows.Forms.Control.MousePosition))) setHover(false); };
                tile.MouseEnter += enter; icon.MouseEnter += enter; label.MouseEnter += enter;
                tile.MouseLeave += leave; icon.MouseLeave += leave; label.MouseLeave += leave;
                Timer hoverWatch = new Timer { Interval = 70 };
                hoverWatch.Tick += (s, e) => { if (hovered && !tile.ClientRectangle.Contains(tile.PointToClient(System.Windows.Forms.Control.MousePosition))) setHover(false); };
                hoverWatch.Start(); tile.Disposed += (s, e) => { motion.Stop(); motion.Dispose(); hoverWatch.Stop(); hoverWatch.Dispose(); };
                tile.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (hovered)
                    {
                        Rectangle bounds = tile.ClientRectangle; bounds.Inflate(-2, -2); const int diameter = 16;
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90); path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
                            using (SolidBrush fill = new SolidBrush(Color.FromArgb(255, 252, 245))) using (Pen border = new Pen(Color.FromArgb(239, 226, 197), 0.65f)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
                        }
                    }
                };
                AttachUnifiedTileShadow(tile);
                flow.Controls.Add(tile);
            }
        }

        private static bool IsLegacyDashboardTile(Panel tile)
        {
            if (tile == null) return false;
            string label = String.Join(" ", tile.Controls.OfType<Label>().Select(x => x.Text));
            label = String.Join(" ", label.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            string[] originalTiles = {
                "Clash Detection", "Review & Clean", "Smart Cleaner", "Smart Openings", "Make Pattern", "Pattern Maker",
                "3D Room", "BIM Library", "Level Bubbles", "Smart Sheets", "Plan View Range", "Smart Templates",
                "Select and Isolate", "Select / Isolate", "Select by ID", "Select By ID"
            };
            return originalTiles.Any(name => label.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static Bitmap CreateStandaloneIcon(int kind)
        {
            Bitmap bitmap = new Bitmap(80, 80, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                // Slightly narrow the glyphs as a group without stretching their height or changing the tile size.
                float glyphWidthScale = kind == 2 ? 0.82f : (kind == 3 ? 0.85f : 0.9f);
                using (Matrix narrowGlyph = new Matrix(glyphWidthScale, 0f, 0f, 1f, 40f * (1f - glyphWidthScale), 0f)) g.Transform = narrowGlyph;
                // Keep the new tile glyphs at the same light visual weight as the existing dashboard icons.
                using (Pen ink = new Pen(Color.FromArgb(198, 151, 42), 2.75f)) using (Pen gold = new Pen(Color.FromArgb(198, 151, 42), 2.75f))
                using (SolidBrush paper = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                {
                    ink.LineJoin = LineJoin.Round; gold.LineJoin = LineJoin.Round;
                    if (kind == 0)
                    {
                        // Smart PDF: a folded sheet with a clear export arrow.
                        g.DrawLines(ink, new[] { new System.Drawing.Point(19, 11), new System.Drawing.Point(48, 11), new System.Drawing.Point(61, 24), new System.Drawing.Point(61, 49) });
                        g.DrawLines(ink, new[] { new System.Drawing.Point(61, 49), new System.Drawing.Point(61, 58), new System.Drawing.Point(19, 58), new System.Drawing.Point(19, 11) });
                        g.DrawLine(gold, 48, 12, 48, 23); g.DrawLine(gold, 48, 23, 59, 23);
                        g.DrawLine(ink, 31, 30, 51, 30); g.DrawLine(ink, 31, 37, 43, 37);
                        g.DrawLine(gold, 42, 39, 42, 51); g.DrawLine(gold, 36, 46, 42, 52); g.DrawLine(gold, 48, 46, 42, 52);
                    }
                    else if (kind == 1)
                    {
                        // Smart Log: an event timeline, differentiated from the document icons.
                        g.DrawLine(gold, 25, 13, 25, 57);
                        g.DrawEllipse(ink, 19, 14, 12, 12); g.DrawEllipse(ink, 19, 30, 12, 12); g.DrawEllipse(ink, 19, 46, 12, 12);
                        g.DrawLine(ink, 39, 20, 61, 20); g.DrawLine(gold, 39, 36, 56, 36); g.DrawLine(ink, 39, 52, 61, 52);
                    }
                    else if (kind == 2)
                    {
                        // Smart Map: two parameter endpoints joined by a mapping arrow.
                        g.DrawRectangle(ink, 13, 17, 22, 18); g.DrawRectangle(ink, 45, 43, 22, 18);
                        g.DrawLine(gold, 31, 27, 48, 44); g.DrawLine(gold, 42, 44, 48, 44); g.DrawLine(gold, 48, 38, 48, 44);
                        g.DrawLine(ink, 19, 26, 28, 26); g.DrawLine(ink, 51, 52, 60, 52);
                    }
                    else if (kind == 4)
                    {
                        g.DrawRectangle(ink, 14, 12, 29, 35);
                        g.DrawLine(ink, 21, 21, 35, 21);
                        g.DrawLine(ink, 21, 28, 32, 28);
                        g.DrawArc(gold, 30, 34, 22, 15, 45, 270);
                        g.DrawArc(gold, 45, 45, 22, 15, 225, 270);
                        g.DrawLine(gold, 44, 43, 53, 50);
                    }
                    else if (kind == 5)
                    {
                        g.DrawRectangle(ink, 16, 38, 11, 19);
                        g.DrawRectangle(ink, 34, 27, 11, 30);
                        g.DrawRectangle(ink, 52, 16, 11, 41);
                    }
                    else
                    {
                        // Smart Planner: calendar plus a prominent scheduled-task check.
                        g.DrawRectangle(ink, 14, 19, 52, 39); g.DrawLine(gold, 14, 31, 66, 31);
                        g.DrawLine(ink, 26, 12, 26, 25); g.DrawLine(ink, 54, 12, 54, 25);
                        g.DrawLine(ink, 25, 43, 31, 49); g.DrawLine(gold, 31, 49, 44, 36);
                        g.DrawLine(ink, 48, 51, 57, 51);
                    }
                }
            }
            using (bitmap)
                return bitmap.Clone(new Rectangle(12, 8, 56, 56), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        }

        private static void AttachUnifiedTileShadow(Panel tile)
        {
            if (tile == null || tile.IsDisposed || DashboardTileShadows.TryGetValue(tile, out _)) return;
            PictureBox icon = tile.Controls.OfType<PictureBox>().FirstOrDefault();
            if (icon == null) return;
            DashboardTileShadows.Add(tile, new object());
            tile.Paint += (s, e) =>
            {
                if (tile.IsDisposed || icon.IsDisposed) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = icon.Height / 40f;
                float width = 26f * scale;
                float height = 4.5f * scale;
                float x = (tile.ClientSize.Width - width) / 2f;
                float y = icon.Bottom + 1f;
                using (GraphicsPath shadowPath = new GraphicsPath())
                {
                    shadowPath.AddEllipse(x, y, width, height);
                    using (PathGradientBrush shadow = new PathGradientBrush(shadowPath))
                    {
                        shadow.CenterColor = Color.FromArgb(55, 82, 91, 101);
                        shadow.SurroundColors = new[] { Color.FromArgb(0, 82, 91, 101) };
                        e.Graphics.FillPath(shadow, shadowPath);
                    }
                }
            };
            icon.SizeChanged += (s, e) => tile.Invalidate();
            icon.LocationChanged += (s, e) => tile.Invalidate();
        }

        private static void Open(string standaloneTool, string standaloneTitle)
        {
            Form dashboard = DashboardEnhancements.DashboardWindow;
            if (dashboard == null || dashboard.IsDisposed) return;
            if (_externalEvent == null) { DashboardEnhancements.ShowSettingsStyleMessage(dashboard, "BIMlify", "Revit is not ready to open Smart QA/QC yet."); return; }
            if (_window == null || _window.IsDisposed || !_window.IsStandaloneFor(standaloneTool))
            {
                _window = new SuiteForm(_handler, standaloneTool, standaloneTitle);
                _handler.Window = _window;
            }
            if (DashboardVisibility.Count == 0)
            {
                _dashboardTitle = dashboard.Text;
                foreach (System.Windows.Forms.Control child in dashboard.Controls)
                {
                    DashboardVisibility[child] = child.Visible;
                    if (IsSharedMainTitleBar(child)) continue;
                    child.Visible = false;
                }
            }
            if (_window.Parent != dashboard)
            {
                _window.Parent?.Controls.Remove(_window);
                _window.Dock = DockStyle.None;
                dashboard.Controls.Add(_window);
            }
            if (!ReferenceEquals(_layoutDashboard, dashboard))
            {
                if (_layoutDashboard != null) _layoutDashboard.Resize -= LayoutSuiteWindow;
                _layoutDashboard = dashboard;
                _layoutDashboard.Resize += LayoutSuiteWindow;
            }
            dashboard.Text = String.IsNullOrEmpty(standaloneTitle) ? "BIMlify — Smart QA/QC" : "BIMlify — " + standaloneTitle;
            LayoutSuiteWindow();
            _window.Visible = true;
            _window.SendToBack();
            if (String.Equals(standaloneTool, "BIM Task Planner", StringComparison.Ordinal))
            {
                _handler.Request("Task Planner Context");
                try { _externalEvent.Raise(); } catch { }
            }
            // Keep the dashboard's shared title bar at its original z-order. Bringing
            // a Dock=Top control to front changes WinForms dock order and shifts the
            // dashboard header when returning from this embedded tool.
            QueueOpenedStatus();
            _window.ScheduleAuditLoad();
            if (String.Equals(standaloneTool, "Model Change Log", StringComparison.Ordinal))
                _window.PreloadSmartLog();
            if (String.Equals(standaloneTool, "Smart Links", StringComparison.Ordinal))
                _window.PreloadLinks();
        }

        private static void LayoutSuiteWindow(object sender = null, EventArgs e = null)
        {
            Form dashboard = DashboardEnhancements.DashboardWindow;
            if (dashboard == null || dashboard.IsDisposed || _window == null || _window.IsDisposed || _window.Parent != dashboard) return;
            System.Windows.Forms.Control titleBar = dashboard.Controls.Cast<System.Windows.Forms.Control>().FirstOrDefault(IsSharedMainTitleBar);
            int top = titleBar == null ? 0 : Math.Max(0, titleBar.Bottom);
            _window.Dock = DockStyle.None;
            _window.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _window.SetBounds(0, top, dashboard.ClientSize.Width, Math.Max(0, dashboard.ClientSize.Height - top));
        }

        private static bool IsSharedMainTitleBar(System.Windows.Forms.Control control)
        {
            if (!(control is Panel) || control.Dock != DockStyle.Top || control.Height < 32 || control.Height > 36)
                return false;
            // Identify the shared caption by its title text, not its tint. The
            // caption uses the standard gray window-bar color.
            return control.Controls.OfType<Label>().Any(label => label.Top == 0 && label.Height >= 32 &&
                label.Text.StartsWith("BIMlify", StringComparison.OrdinalIgnoreCase));
        }
        internal static void NavigateHome()
        {
            Form dashboard = DashboardEnhancements.DashboardWindow;
            if (_window != null && !_window.IsDisposed)
            {
                _window.Visible = false;
                _window.Parent?.Controls.Remove(_window);
            }
            if (_layoutDashboard != null) _layoutDashboard.Resize -= LayoutSuiteWindow;
            _layoutDashboard = null;
            if (dashboard == null || dashboard.IsDisposed) return;
            foreach (KeyValuePair<System.Windows.Forms.Control, bool> item in DashboardVisibility)
                if (!item.Key.IsDisposed && item.Key.Parent == dashboard) item.Key.Visible = item.Value;
            DashboardVisibility.Clear();
            if (!String.IsNullOrEmpty(_dashboardTitle)) dashboard.Text = _dashboardTitle;
            _dashboardTitle = null;
            dashboard.PerformLayout();
            dashboard.Invalidate(true);
            dashboard.Activate();
            DashboardEnhancements.SetExecutionResult(1);
        }

        private static void QueueOpenedStatus()
        {
            Form dashboard = DashboardEnhancements.DashboardWindow;
            if (dashboard != null && !dashboard.IsDisposed && dashboard.IsHandleCreated)
            {
                try { dashboard.BeginInvoke(new Action(() => DashboardEnhancements.SetExecutionResult(0))); return; } catch { }
            }
            DashboardEnhancements.SetExecutionResult(0);
        }

        private sealed class SuiteHandler : IExternalEventHandler
        {
            private string _requested = "";
            private List<string> _requestedIds = new List<string>();
            private long _requestedPdfSheetId;
            private string _requestedPdfSheetNumber = "";
            private string _requestedPdfSheetName = "";
            private NwcExportRequest _requestedNwc;
            internal List<ReportRow> InputRows = new List<ReportRow>();
            internal SuiteForm Window;
            private HashSet<long> _auditScope;
            private List<ReportRow> ApplyAuditScope(List<ReportRow> rows)
            {
                return _auditScope == null ? rows : rows.Where(row =>
                    Int64.TryParse(row.ElementId, out long id) && _auditScope.Contains(id)).ToList();
            }
            private string _editTool;
            private string _inventoryProject;
            private List<InventoryEdit> _pendingInventoryEdits = new List<InventoryEdit>();

            internal void RequestInventory(string audit, IEnumerable<InventoryEdit> edits = null)
            {
                Request("QA inventory");
                _editTool = audit;
                _pendingInventoryEdits = edits == null ? new List<InventoryEdit>() : edits.ToList();
            }
            private string _collaboratorProject;
            private Action<string> _locateFailed;
            internal void Request(string tool, List<ReportRow> input = null, List<string> locateIds = null, Action<string> locateFailed = null) { _locateFailed = locateFailed; _requested = tool; InputRows = input ?? new List<ReportRow>(); _requestedIds = locateIds ?? new List<string>(); _requestedPdfSheetId = 0; }
            internal void RequestPdfSheetEdit(long id, string number, string name) { _requested = "PDF Sheet Edit"; _requestedPdfSheetId = id; _requestedPdfSheetNumber = number ?? ""; _requestedPdfSheetName = name ?? ""; InputRows = new List<ReportRow>(); _requestedIds = new List<string>(); }
            internal void RequestNwcExport(NwcExportRequest request) { _requested = "NWC Export"; _requestedNwc = request; InputRows = new List<ReportRow>(); _requestedIds = new List<string>(); }
            public string GetName() => "BIMlify Smart QA/QC";
            public void Execute(UIApplication app)
            {
                string tool = _requested; _requested = "";
                Action<string> locateFailed = _locateFailed; _locateFailed = null;
                List<string> locateIds = _requestedIds; _requestedIds = new List<string>();
                long pdfSheetId = _requestedPdfSheetId; _requestedPdfSheetId = 0;
                string pdfSheetNumber = _requestedPdfSheetNumber; _requestedPdfSheetNumber = "";
                string pdfSheetName = _requestedPdfSheetName; _requestedPdfSheetName = "";
                NwcExportRequest nwcRequest = _requestedNwc; _requestedNwc = null;
                if (String.IsNullOrWhiteSpace(tool)) return;
                bool isPdfSheetLoad = String.Equals(tool, "PDF Batch Load", StringComparison.Ordinal);
                bool isPdfSheetEdit = String.Equals(tool, "PDF Sheet Edit", StringComparison.Ordinal);
                bool isNwcViewsLoad = String.Equals(tool, "NWC Views Load", StringComparison.Ordinal);
                bool isNwcOpenVg = String.Equals(tool, "NWC Open Visibility/Graphics", StringComparison.Ordinal);
                bool isLocate = tool == "Locate Selected Elements" || tool == "Locate Collaborator Elements" || tool == "Locate Version Elements";
                bool isTraceToggle = String.Equals(tool, "Smart Trace Toggle", StringComparison.Ordinal);
                bool isTraceStop = String.Equals(tool, "Smart Trace Stop", StringComparison.Ordinal);
                bool isTraceResume = String.Equals(tool, "Smart Trace Resume", StringComparison.Ordinal);
                bool versions = tool == "Save Version Snapshot" || tool == "Compare Versions";
                bool preload = tool == "Smart Log Preload";
                bool previewComparison = tool == "Smart Log Preview";
                bool isCollaboratorsRefresh = preload || tool == "Collaborators Refresh";
                bool isTaskPlannerContext = String.Equals(tool, "Task Planner Context", StringComparison.Ordinal);
                if ((Window == null || Window.IsDisposed) && !(isTraceStop || isTraceResume)) { InputRows = new List<ReportRow>(); return; }
                bool correctToolSelected = isPdfSheetLoad || isPdfSheetEdit
                    ? Window.IsToolSelected("PDF Batch Export")
                    : (isNwcViewsLoad || isNwcOpenVg ? Window.IsToolSelected("NWC Export")
                    : (tool == "QA inventory" || isLocate || isTraceToggle || (isCollaboratorsRefresh || previewComparison || versions) && Window.IsToolSelected("Model Change Log") || Window.IsToolSelected(tool)));
                if (!(isTraceStop || isTraceResume || isTaskPlannerContext) && !correctToolSelected) { InputRows = new List<ReportRow>(); return; }
                Document doc = app.ActiveUIDocument == null ? null : app.ActiveUIDocument.Document;
                try {
                    if (isTraceStop) { StopSmartTrace(); return; }
                    if (isTraceResume) { ToggleSmartTrace(app, doc); return; }
                    if (doc == null) throw new InvalidOperationException("Open a Revit project first.");
                    if (tool == "Locate Version Elements")
                    {
                        var matched = locateIds.Select(id => doc.GetElement(id)).Where(element => element != null).Select(element => element.Id).ToList();
                        if (matched.Count == 0) throw new InvalidOperationException("The selected elements are unavailable in the open model.");
                        app.ActiveUIDocument.Selection.SetElementIds(matched);
                        app.ActiveUIDocument.ShowElements(matched);
                        Window.SetStatus(matched.Count + " elements located.");
                        return;
                    }
                    if (versions)
                    {
                        var files = InputRows.First();
                        if (tool == "Save Version Snapshot")
                        {
                            CompareSnapshot(doc, snapshotPath: files.Element);
                            Window.SetStatus("Version snapshot saved.");
                        }
                        else
                        {
                            Window.ShowVersionRows(CompareSavedVersions(files.Element, files.Details));
                        }
                        return;
                    }
                    if (isTaskPlannerContext) { Window?.SetTaskProjectContext(TaskProjectIdentity(doc), doc.Title); return; }
                    if (isNwcOpenVg)
                    {
                        RevitCommandId visibilityCommand = RevitCommandId.LookupPostableCommandId(PostableCommand.VisibilityOrGraphics);
                        if (visibilityCommand == null) throw new InvalidOperationException("Revit's Visibility/Graphics command is not available in this session.");
                        Window?.KeepVisibleBehindRevit();
                        app.PostCommand(visibilityCommand);
                        Window?.SetStatusForTool("NWC Export", "Visibility/Graphics opened for Revit's active view. Use VV/VG to hide the category as a last resort, then return here and export again.");
                        return;
                    }
                    if (isNwcViewsLoad)
                    {
                        List<NwcViewInfo> views = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                            .Where(v => !v.IsTemplate).OrderByDescending(v => v.Id == doc.ActiveView.Id).ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
                            .Select(v => new NwcViewInfo { Id = v.Id.Value, Name = v.Name }).ToList();
                        Window?.SetNwcViews(views, null, Path.GetFileNameWithoutExtension(doc.Title));
                        return;
                    }
                    if (String.Equals(tool, "NWC Export", StringComparison.Ordinal))
                    {
                        List<string> createdFiles = ExportNwc(doc, app.ActiveUIDocument, nwcRequest);
                        string successMessage = createdFiles.Count == 1
                            ? "Exported successfully: " + Path.GetFileName(createdFiles[0])
                            : "Exported successfully: " + createdFiles.Count.ToString(CultureInfo.InvariantCulture) + " NWC files saved to " + nwcRequest.Folder;
                        Window?.SetNwcExportSucceeded(successMessage);
                        if (nwcRequest.OpenOutputAfterExport) OpenNwcOutput(createdFiles, nwcRequest.Folder);
                        return;
                    }
                    if (isTraceToggle) { ToggleSmartTrace(app, doc); return; }
                    if (previewComparison)
                    {
                        if (!Window.IsSnapshotTab()) return;
                        if (!File.Exists(SnapshotPath(doc)))
                            Window.ShowRows("Model Change Log", new List<ReportRow> {
                                new ReportRow("", "Snapshot", doc.Title, "No saved snapshot", "Use Save Snapshot or Compare to create a baseline.")
                            });
                        else
                            Window.ShowRows("Model Change Log", CompareSnapshot(doc, updateBaseline: false));
                        return;
                    }
                    if (preload)
                        Window.SetTraceRows(ReadTraceHistory(doc));
                    if (isCollaboratorsRefresh)
                    {
                        if (!doc.IsWorkshared)
                        {
                            string source = doc.IsModelInCloud ? "This ACC cloud model is not enabled for Revit Cloud Worksharing, so Revit cannot provide element owners or last-saved-by names." : "This model is not workshared, so Revit has no collaborator ownership records to show.";
                            Window?.SetCollaborators(new List<CollaboratorSummary>(), source);
                            return;
                        }
                        _collaboratorProject = AuditProjectKey(doc);
                        List<CollaboratorSummary> collaborators = BuildCollaboratorSummaries(doc);
                        string emptyMessage = collaborators.Count == 0
                            ? "Revit returned no workset owners or element ownership records for this workshared model. ACC activity history is not included."
                            : (collaborators.Any(person => !person.HasElementRecords)
                                ? "Workset owners are shown where available; element counts use Revit ownership and last-saved-by records. This is not a list of everyone connected to ACC."
                                : null);
                        Dictionary<string, string> localTimes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        lock (TraceSync)
                        {
                            if (String.Equals(TraceKey(doc), TraceDocumentKey, StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (TraceRecord record in TraceRows.Where(row => row.Change != "Deleted"))
                                    foreach (string id in SplitTraceIds(record.ElementId))
                                        localTimes[record.User + "|" + id] = record.Time;
                            }
                        }
                        Window?.SetCollaborators(collaborators, emptyMessage, localTimes);
                        return;
                    }
                    if (isLocate)
                    {
                        if (tool == "Locate Collaborator Elements" && AuditProjectKey(doc) != _collaboratorProject)
                        {
                            locateFailed?.Invoke("Return to the collaborator snapshot project or refresh users.");
                            return;
                        }
                        List<ElementId> targets = new List<ElementId>();
                        foreach (string value in locateIds ?? new List<string>())
                        {
                            long numericId;
                            if (!Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out numericId)) continue;
                            ElementId id = new ElementId(numericId);
                            if (doc.GetElement(id) != null && !targets.Contains(id)) targets.Add(id);
                        }
                        if (targets.Count == 0)
                        {
                            const string message = "The selected result has no matching Revit element.";
                            if (locateFailed != null) locateFailed(message); else Window?.SetStatus(message);
                            return;
                        }
                        app.ActiveUIDocument.Selection.SetElementIds(targets);
                        try { app.ActiveUIDocument.ShowElements(targets); }
                        catch (Exception) when (tool == "Locate Collaborator Elements")
                        {
                            locateFailed?.Invoke("Selected the matching IDs. Revit could not zoom to these elements.");
                            return;
                        }
                        string located = "Located " + targets.Count + " selected Revit element(s).";
                        if (locateFailed != null) locateFailed(located); else Window?.SetStatus(located);
                        return;
                    }
                    if (isPdfSheetEdit)
                    {
                        if (String.IsNullOrWhiteSpace(pdfSheetNumber) || String.IsNullOrWhiteSpace(pdfSheetName))
                            throw new InvalidOperationException("Sheet number and sheet name cannot be empty.");
                        ViewSheet sheet = doc.GetElement(new ElementId(pdfSheetId)) as ViewSheet;
                        if (sheet == null || sheet.IsPlaceholder) throw new InvalidOperationException("The selected sheet is no longer available.");
                        using (Transaction transaction = new Transaction(doc, "Edit sheet number and name"))
                        {
                            transaction.Start();
                            sheet.SheetNumber = pdfSheetNumber.Trim();
                            sheet.Name = pdfSheetName.Trim();
                            transaction.Commit();
                        }
                        List<PdfSheetInfo> refreshed = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                            .Where(x => !x.IsPlaceholder).OrderBy(x => x.SheetNumber, StringComparer.OrdinalIgnoreCase)
                            .Select(x => new PdfSheetInfo { Id = x.Id.Value, Number = x.SheetNumber ?? "", Name = x.Name ?? "", ProjectName = Path.GetFileNameWithoutExtension(doc.Title), ProjectIdentity = AuditProjectKey(doc), UniqueId = x.UniqueId }).ToList();
                        Window?.SetPdfSheets(refreshed);
                        Window?.SetStatusForTool("PDF Batch Export", "Updated sheet " + pdfSheetNumber.Trim() + " — " + pdfSheetName.Trim() + ".");
                        return;
                    }
                    if (tool == "PDF Batch Load")
                    {
                        List<PdfSheetInfo> sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                            .Where(x => !x.IsPlaceholder).OrderBy(x => x.SheetNumber, StringComparer.OrdinalIgnoreCase)
                            .Select(x => new PdfSheetInfo { Id = x.Id.Value, Number = x.SheetNumber ?? "", Name = x.Name ?? "", ProjectName = Path.GetFileNameWithoutExtension(doc.Title), ProjectIdentity = AuditProjectKey(doc), UniqueId = x.UniqueId }).ToList();
                        Window?.SetPdfSheets(sheets);
                        return;
                    }
                    if (tool == "PDF Batch Export")
                    {
                        string successMessage = ExportPdf(doc, Window.PdfRequest);
                        Window?.SetPdfExportSucceeded(successMessage);
                        return;
                    }
                    if (tool == "QA inventory")
                    {
                        if (!Window.IsToolSelected(_editTool)) return;
                        string project = AuditProjectKey(doc);
                        List<InventoryEdit> edits = _pendingInventoryEdits;
                        _pendingInventoryEdits = new List<InventoryEdit>();
                        if (edits.Count > 0)
                        {
                            if (project != _inventoryProject)
                                throw new InvalidOperationException("Return to the project that produced this table.");
                            var writable = new List<Tuple<InventoryEdit, Tuple<string, Action<string>>>>();
                            foreach (InventoryEdit pending in edits)
                            {
                                Element element = doc.GetElement(pending.Row.UniqueId);
                                if (element == null || !AuditFields(element).TryGetValue(pending.Row.Field, out var edit))
                                    throw new InvalidOperationException("One or more fields are no longer writable.");
                                if (!String.Equals(edit.Item1 ?? "", pending.Row.Value ?? "", StringComparison.Ordinal))
                                    throw new InvalidOperationException("A value changed in Revit. Reload the table and try again.");
                                writable.Add(Tuple.Create(pending, edit));
                            }
                            using (var transaction = new Transaction(doc, "BIMlify edit project element"))
                            {
                                transaction.Start();
                                foreach (var pending in writable) pending.Item2.Item2(pending.Item1.Value);
                                if (transaction.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit did not save this change.");
                            }
                        }
                        _inventoryProject = project;
                        var selectedIds = Window.UseRevitSelection
                            ? new HashSet<ElementId>(app.ActiveUIDocument.Selection.GetElementIds()) : null;
                        if (Window.IsToolSelected(_editTool))
                        {
                            var inventory = BuildInventory(doc, _editTool, selectedIds);
                            if (Window.IncludeRevitLinks)
                                foreach (var link in LoadedAuditLinks(doc, selectedIds?.Select(id => id.Value).ToHashSet()))
                                    foreach (var row in BuildInventory(link.Item2, _editTool, null))
                                    {
                                        row.Id = link.Item1.Id.Value + ":" + row.Id;
                                        row.Name = link.Item1.Name + " / " + row.Name;
                                        row.Field = null;
                                        inventory.Add(row);
                                    }
                            Window.ShowInventory(inventory);
                            if (edits.Count > 0)
                                Window.SetStatus(edits.Count + " change(s) saved.");
                        }
                        return;
                    }
                    _auditScope = Window.UseRevitSelection && ToolNames.Contains(tool)
                        ? new HashSet<long>(app.ActiveUIDocument.Selection.GetElementIds().Select(id => id.Value)) : null;
                    if (_auditScope != null && _auditScope.Count == 0)
                        throw new InvalidOperationException("Select one or more elements in Revit before running this check.");
                    if (_auditScope != null && tool == "Naming Convention Checker")
                    {
                        foreach (long id in _auditScope.ToList())
                        {
                            Element element = doc.GetElement(new ElementId(id));
                            ElementId typeId = element?.GetTypeId();
                            if (typeId != null && typeId != ElementId.InvalidElementId) _auditScope.Add(typeId.Value);
                            if (element is FamilyInstance instance) _auditScope.Add(instance.Symbol.Family.Id.Value);
                        }
                    }
                    List<ReportRow> input = InputRows; InputRows = new List<ReportRow>();
                    List<ReportRow> rows = Audit(doc, tool, input);
                    rows = ApplyAuditScope(rows);
                    if (Window.IncludeRevitLinks && ToolNames.Contains(tool))
                    {
                        foreach (var link in LoadedAuditLinks(doc, _auditScope))
                        {
                            try
                            {
                                rows.AddRange(Audit(link.Item2, tool, input).Select(row => new ReportRow(
                                    String.IsNullOrEmpty(row.ElementId) ? "" : link.Item1.Id.Value + ":" + row.ElementId,
                                    row.Category, row.Element, row.Finding, link.Item1.Name + " / " + row.Details, row.Stage)));
                            }
                            catch (Exception error)
                            {
                                rows.Add(new ReportRow("", "Revit Links", link.Item1.Name, "Link could not be checked", error.Message));
                            }
                        }
                    }
                    Window?.ShowRows(tool, rows);
                } catch (Exception ex)
                {
                    string statusTool = versions ? "Model Change Log" : isPdfSheetLoad || isPdfSheetEdit ? "PDF Batch Export" : tool;
                    if (locateFailed != null) locateFailed(ex.Message);
                    else if (isPdfSheetLoad) Window?.SetPdfLoadError(ex.Message);
                    else if (isNwcViewsLoad) Window?.SetNwcViews(new List<NwcViewInfo>(), "Could not read 3D views: " + ex.Message, doc == null ? null : Path.GetFileNameWithoutExtension(doc.Title));
                    else if (isPdfSheetEdit)
                    {
                        try
                        {
                            List<PdfSheetInfo> refreshed = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                                .Where(x => !x.IsPlaceholder).OrderBy(x => x.SheetNumber, StringComparer.OrdinalIgnoreCase)
                                .Select(x => new PdfSheetInfo { Id = x.Id.Value, Number = x.SheetNumber ?? "", Name = x.Name ?? "", ProjectName = Path.GetFileNameWithoutExtension(doc.Title), ProjectIdentity = AuditProjectKey(doc), UniqueId = x.UniqueId }).ToList();
                            Window?.SetPdfSheets(refreshed);
                        }
                        catch { }
                        Window?.SetStatusForTool(statusTool, "Could not update sheet: " + ex.Message);
                    }
                    else if (isCollaboratorsRefresh) Window?.CollaboratorRefreshFailed("Could not read collaborator data: " + ex.Message);
                    else if (previewComparison) Window?.SetStatus("Could not load snapshot comparison: " + ex.Message);
                    else if (tool == "QA inventory") Window?.SetStatus("Could not edit result: " + ex.Message);
                    else if (tool == "PDF Batch Export") Window?.SetPdfExportFailed("Export failed: " + ex.Message);
                    else { if (String.Equals(tool, "NWC Export", StringComparison.Ordinal)) Indicator.Stop(); Window?.SetStatusForTool(statusTool, statusTool + " failed: " + ex.Message); }
                }
            }
        }

        private sealed class ReportRow
        {
            internal string ElementId, Category, Element, Finding, Details, Stage;
            internal ReportRow(string id, string category, string element, string finding, string details, string stage = "") { ElementId=id; Category=category; Element=element; Finding=finding; Details=details; Stage=stage ?? ""; }
        }

        private sealed class InventoryRow
        {
            internal string UniqueId, Id, Category, Name, Field, Value, SheetNumber;
        }

        private sealed class InventoryEdit
        {
            internal InventoryRow Row;
            internal string Value;
        }

        private static IEnumerable<Element> InventoryElements(Document doc, string tool)
        {
            var collector = new FilteredElementCollector(doc);
            switch (tool)
            {
                case "Door & Window Audit":
                    return collector.WhereElementIsNotElementType().Where(e => e.Category != null &&
                        (e.Category.Id.Value == (long)BuiltInCategory.OST_Doors || e.Category.Id.Value == (long)BuiltInCategory.OST_Windows));
                case "Room Data Checker":
                case "Finishes Checker":
                    return collector.OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType();
                case "Level & Grid Audit":
                    return collector.WherePasses(new ElementMulticlassFilter(new List<Type> { typeof(Level), typeof(Grid) }));
                case "Sheet Audit":
                    return collector.OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(sheet => !sheet.IsPlaceholder);
                case "Revision Manager":
                    return collector.OfClass(typeof(Revision));
                case "Opening Checker":
                    return collector.WhereElementIsNotElementType().Where(e => e is Opening ||
                        (e.Category?.Name ?? "").IndexOf("opening", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ((e as FamilyInstance)?.Symbol?.FamilyName ?? "").IndexOf("opening", StringComparison.OrdinalIgnoreCase) >= 0);
                case "Naming Convention Checker":
                    return collector.WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(),
                        new ElementMulticlassFilter(new List<Type> { typeof(Family), typeof(View), typeof(Level), typeof(Grid) })));
                default:
                    return collector.WhereElementIsNotElementType().Where(e => e.Category != null || e is View);
            }
        }

        private static IEnumerable<Tuple<RevitLinkInstance, Document>> LoadedAuditLinks(Document doc, HashSet<long> selected)
        {
            foreach (var instance in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                if (selected != null && !selected.Contains(instance.Id.Value)) continue;
                var link = instance.GetLinkDocument();
                if (link != null) yield return Tuple.Create(instance, link);
            }
        }

        private static List<InventoryRow> BuildInventory(Document doc, string tool, HashSet<ElementId> selectedIds)
        {
            var rows = new List<InventoryRow>();
            foreach (Element element in InventoryElements(doc, tool))
            {
                if (selectedIds != null && !selectedIds.Contains(element.Id)) continue;
                string category = element.Category?.Name ?? "";
                foreach (var field in AuditFields(element, includeWorkset: tool == "Workset Checker"))
                {
                    bool common = field.Key == "Element name" || field.Key.StartsWith("Family name") || field.Key.StartsWith("Type name");
                    bool relevant = common || tool == "Parameter Audit" ||
                        (tool == "Door & Window Audit" && (field.Key.StartsWith("Mark [") || field.Key.StartsWith("Type Mark") || field.Key.StartsWith("Width") || field.Key.StartsWith("Height"))) ||
                        (tool == "Room Data Checker" && (field.Key.StartsWith("Number [") || field.Key.StartsWith("Name [") || field.Key.StartsWith("Department ["))) ||
                        (tool == "Finishes Checker" && field.Key.Contains("Finish [")) ||
                        (tool == "Level & Grid Audit" && field.Key == "Elevation") ||
                        (tool == "Sheet Audit" && field.Key == "Sheet number") ||
                        (tool == "Revision Manager" && field.Key == "Revision date") ||
                        (tool == "Workset Checker" && field.Key == "Workset name");
                    if (!relevant) continue;
                    rows.Add(new InventoryRow { UniqueId = element.UniqueId, Id = element.Id.Value.ToString(), Category = category,
                        Name = element.Name, Field = field.Key, Value = field.Value.Item1 ?? "", SheetNumber = (element as ViewSheet)?.SheetNumber });
                }
                if (!rows.Any() || rows[rows.Count - 1].UniqueId != element.UniqueId)
                    rows.Add(new InventoryRow { UniqueId = element.UniqueId, Id = element.Id.Value.ToString(), Category = category, Name = element.Name, SheetNumber = (element as ViewSheet)?.SheetNumber });
            }
            return rows;
        }

        private sealed class TraceRecord
        {
            internal string Time, User, Change, ElementId, Category, Element, Transaction;
            internal TraceRecord(string time, string user, string change, string id, string category, string element, string transaction)
            { Time = time ?? ""; User = user ?? ""; Change = change ?? ""; ElementId = id ?? ""; Category = category ?? ""; Element = element ?? ""; Transaction = transaction ?? ""; }
        }

        private static string AuditProjectKey(Document doc) =>
            doc.PathName + "|" + doc.Title + "|" + (doc.ProjectInformation?.UniqueId ?? "");

        private static string RequiredName(string value)
        {
            string name = (value ?? "").Trim();
            if (name.Length == 0) throw new InvalidOperationException("Enter a name before applying the edit.");
            return name;
        }

        private static Dictionary<string, Tuple<string, Action<string>>> AuditFields(Element element, bool includeWorkset = true)
        {
            var fields = new Dictionary<string, Tuple<string, Action<string>>>();
            ElementType type = element as ElementType;
            if (type == null && element.GetTypeId() != ElementId.InvalidElementId)
                type = element.Document.GetElement(element.GetTypeId()) as ElementType;
            Family family = element as Family ?? (type as FamilySymbol)?.Family;
            if (family != null)
                fields["Family name (all its types)"] = Tuple.Create(family.Name, (Action<string>)(value => family.Name = RequiredName(value)));
            if (type != null)
                fields["Type name (all its instances)"] = Tuple.Create(type.Name, (Action<string>)(value => type.Name = RequiredName(value)));
            if (element is View || element is Level || element is Grid)
                fields["Element name"] = Tuple.Create(element.Name, (Action<string>)(value => element.Name = RequiredName(value)));
            Parameter workset = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            if (includeWorkset && element.Document.IsWorkshared && workset != null && !workset.IsReadOnly && workset.StorageType == StorageType.Integer)
            {
                var userWorksets = new FilteredWorksetCollector(element.Document).OfKind(WorksetKind.UserWorkset).ToWorksets();
                fields["Workset name"] = Tuple.Create(element.Document.GetWorksetTable().GetWorkset(element.WorksetId)?.Name,
                    (Action<string>)(value =>
                    {
                        Workset target = userWorksets.FirstOrDefault(item => String.Equals(item.Name, value.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (target == null || !workset.Set(target.Id.IntegerValue))
                            throw new InvalidOperationException("Enter an existing user workset name.");
                    }));
            }
            Parameter typeMark = type?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
            if (typeMark != null && !typeMark.IsReadOnly)
                fields["Type Mark (all instances)"] = Tuple.Create(typeMark.AsString(), (Action<string>)(value =>
                {
                    if (!typeMark.Set(value)) throw new InvalidOperationException("Revit rejected the type mark.");
                }));
            foreach (Parameter parameter in element.Parameters.Cast<Parameter>().Where(p => !p.IsReadOnly))
            {
                if (parameter.StorageType != StorageType.String && parameter.StorageType != StorageType.Double && parameter.StorageType != StorageType.Integer) continue;
                fields[parameter.Definition.Name + " [" + parameter.Id.Value + "]"] = Tuple.Create(
                    parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString(),
                    (Action<string>)(value =>
                    {
                        bool success = parameter.StorageType == StorageType.String ? parameter.Set(value) : parameter.SetValueString(value);
                        if (!success) throw new InvalidOperationException("Revit rejected this parameter value.");
                    }));
            }
            if (element is Level level)
            {
                Parameter elevation = level.get_Parameter(BuiltInParameter.LEVEL_ELEV);
                if (elevation != null && !elevation.IsReadOnly)
                    fields["Elevation"] = Tuple.Create(elevation.AsValueString(), (Action<string>)(value =>
                    {
                        if (!elevation.SetValueString(value))
                            throw new InvalidOperationException("Enter an elevation in project units.");
                    }));
            }
            if (type != null && element.Category != null &&
                (element.Category.Id.Value == (long)BuiltInCategory.OST_Doors ||
                 element.Category.Id.Value == (long)BuiltInCategory.OST_Windows))
                foreach (string dimension in new[] { "Width", "Height" })
                {
                    Parameter parameter = type.LookupParameter(dimension);
                    if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double) continue;
                    fields[dimension + " (all instances)"] = Tuple.Create(parameter.AsValueString(), (Action<string>)(value =>
                    {
                        if (!parameter.SetValueString(value) || parameter.AsDouble() <= 0)
                            throw new InvalidOperationException("Enter a positive dimension in project units.");
                    }));
                }
            if (element is ViewSheet sheet)
                fields["Sheet number"] = Tuple.Create(sheet.SheetNumber, (Action<string>)(value => sheet.SheetNumber = RequiredName(value)));
            if (element is Revision revision)
                fields["Revision date"] = Tuple.Create(revision.RevisionDate, (Action<string>)(value => revision.RevisionDate = value.Trim()));
            return fields;
        }


        internal sealed class CollaboratorSummary
        {
            internal string User;
            internal int OwnedElements;
            internal int LastChangedElements;
            internal bool HasElementRecords;
            internal List<string> OwnedElementIds;
            internal List<string> LastChangedElementIds;
            internal CollaboratorSummary(string user, int owned, int changed, bool hasElementRecords = true, List<string> ownedIds = null, List<string> changedIds = null)
            { User = user ?? ""; OwnedElements = owned; LastChangedElements = changed; HasElementRecords = hasElementRecords; OwnedElementIds = ownedIds ?? new List<string>(); LastChangedElementIds = changedIds ?? new List<string>(); }
        }

        private static List<CollaboratorSummary> BuildCollaboratorSummaries(Document doc)
        {
            if (doc == null || !doc.IsWorkshared) return new List<CollaboratorSummary>();
            Dictionary<string, int> owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> lastEditors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> ownedIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> changedIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            // Workset previews expose owners for central/cloud workshared models
            // without opening another document. Keep these names as a fallback when
            // Revit has no element-level tooltip records available.
            try
            {
                ModelPath centralPath = doc.GetWorksharingCentralModelPath();
                if (centralPath != null)
                    foreach (WorksetPreview workset in WorksharingUtils.GetUserWorksetInfo(centralPath) ?? new List<WorksetPreview>())
                    {
                        string owner = (workset?.Owner ?? "").Trim();
                        if (owner.Length > 0 && !owners.ContainsKey(owner)) owners[owner] = 0;
                    }
            }
            catch { }

            // Types are not borrowable/placed instances and needlessly expand this
            // scan in large central models.
            foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                WorksharingTooltipInfo info = null;
                try
                {
                    info = WorksharingUtils.GetWorksharingTooltipInfo(doc, element.Id);
                    string owner = (info?.Owner ?? "").Trim();
                    string editor = (info?.LastChangedBy ?? "").Trim();
                    string elementId = element.Id.Value.ToString(CultureInfo.InvariantCulture);
                    if (owner.Length > 0)
                    {
                        owners[owner] = owners.ContainsKey(owner) ? owners[owner] + 1 : 1;
                        if (!ownedIds.ContainsKey(owner)) ownedIds[owner] = new List<string>();
                        ownedIds[owner].Add(elementId);
                    }
                    if (editor.Length > 0)
                    {
                        lastEditors[editor] = lastEditors.ContainsKey(editor) ? lastEditors[editor] + 1 : 1;
                        if (!changedIds.ContainsKey(editor)) changedIds[editor] = new List<string>();
                        changedIds[editor].Add(elementId);
                    }
                }
                catch { }
                finally { info?.Dispose(); }
            }
            return owners.Keys.Concat(lastEditors.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(user => new CollaboratorSummary(user, owners.ContainsKey(user) ? owners[user] : 0, lastEditors.ContainsKey(user) ? lastEditors[user] : 0,
                    (owners.ContainsKey(user) && owners[user] > 0) || (lastEditors.ContainsKey(user) && lastEditors[user] > 0),
                    ownedIds.ContainsKey(user) ? ownedIds[user] : new List<string>(), changedIds.ContainsKey(user) ? changedIds[user] : new List<string>()))
                .OrderByDescending(user => user.OwnedElements).ThenByDescending(user => user.LastChangedElements)
                .ThenBy(user => user.User, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static List<TraceRecord> ReadTraceHistory(Document doc)
        {
            string key = TraceKey(doc);
            lock (TraceSync)
            {
                if (String.Equals(key, TraceDocumentKey, StringComparison.OrdinalIgnoreCase))
                    return TraceRows.ToList();
                string path = TracePath(doc, key);
                if (!File.Exists(path)) return new List<TraceRecord>();
                return CompactTraceRows(File.ReadLines(path).Skip(1).Select(ParseTraceCsv)
                    .Where(fields => fields.Count >= 7)
                    .Select(fields => new TraceRecord(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6])));
            }
        }

        private static void ToggleSmartTrace(UIApplication app, Document doc)
        {
            string key = TraceKey(doc);
            lock (TraceSync)
            {
                if (TraceTracking && String.Equals(TraceDocumentKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    StopSmartTrace();
                    return;
                }
                if (TraceTracking && TraceApplication != null) TraceApplication.Application.DocumentChanged -= OnTraceDocumentChanged;
                TraceDocumentKey = key;
                TraceFile = TracePath(doc, key);
                TraceRows.Clear();
                TraceCurrentSessionCount = 0;
                // Older builds wrote one row per element. Compact that saved history
                // into one row per committed transaction and change type, retaining
                // all IDs so the Locate action still selects every affected element.
                if (File.Exists(TraceFile))
                    foreach (string line in File.ReadAllLines(TraceFile).Skip(1))
                    {
                        List<string> fields = ParseTraceCsv(line);
                        if (fields.Count >= 7) TraceRows.Add(new TraceRecord(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6]));
                    }
                List<TraceRecord> compacted = CompactTraceRows(TraceRows);
                TraceRows.Clear();
                TraceRows.AddRange(compacted);
                TraceUser = String.IsNullOrWhiteSpace(app.Application.Username) ? Environment.UserName : app.Application.Username;
                TraceApplication = app;
                app.Application.DocumentChanged += OnTraceDocumentChanged;
                TraceTracking = true;
                try { SaveTraceRows(); } catch { }
                _window?.SetTraceRows(TraceRows.ToList());
                _window?.SetTraceTracking(true);
                _window?.SetStatus("Tracking Revit changes now. Saved local entries may also be shown; the username is the current Revit session, not verified authorship or ACC history.");
                ShowTraceMonitor(doc.Title, TraceCurrentSessionCount, app.MainWindowHandle);
            }
        }

        private static void StopSmartTrace()
        {
            lock (TraceSync)
            {
                if (TraceApplication != null) TraceApplication.Application.DocumentChanged -= OnTraceDocumentChanged;
                TraceTracking = false;
                TraceApplication = null;
            }
            _window?.SetTraceTracking(false);
            _window?.SetTraceRows(TraceRows.ToList());
            _window?.SetStatus("Smart Trace stopped. Saved local Revit session events remain available in this project.");
            UpdateTraceMonitor(TraceCurrentSessionCount);
        }

        private sealed class TraceWindowOwner : IWin32Window
        {
            internal TraceWindowOwner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; private set; }
        }

        private static void ShowTraceMonitor(string documentTitle, int capturedChanges, IntPtr ownerHandle)
        {
            if (_traceMonitor != null && !_traceMonitor.IsDisposed)
            {
                UpdateTraceMonitor(capturedChanges);
                if (!_traceMonitor.Visible) _traceMonitor.Show();
                _traceMonitor.Activate();
                return;
            }

            Form monitor = new Form
            {
                Text = "BIMlify — Smart Trace",
                FormBorderStyle = FormBorderStyle.FixedSingle,
                StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(460, 128),
                ShowInTaskbar = false,
                MinimizeBox = false,
                MaximizeBox = false,
                BackColor = Color.FromArgb(238, 241, 245),
                ForeColor = Color.FromArgb(55, 67, 79),
                Font = new Font("Segoe UI", 8.5f)
            };
            Label title = new Label { Name = "BIMlifyTraceMonitorTitle", Text = "Smart Trace is running", Left = 14, Top = 8, Width = 420, Height = 21, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(78, 85, 92), TextAlign = ContentAlignment.MiddleLeft };
            Label description = new Label { Text = "Captures Revit change events while running; this is not an ACC audit.", Left = 14, Top = 31, Width = 420, Height = 19, Font = new Font("Segoe UI", 8f), ForeColor = Color.FromArgb(102, 109, 118), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            Label status = new Label { Name = "BIMlifyTraceMonitorStatus", Left = 14, Top = 53, Width = 420, Height = 18, AutoEllipsis = true, ForeColor = Color.FromArgb(102, 109, 118), TextAlign = ContentAlignment.MiddleLeft };
            ProgressBar activity = new ProgressBar { Left = 14, Top = 94, Width = 285, Height = 16, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24 };
            Button stop = DashboardEnhancements.CreateSharedStyleButton("Stop Trace", false);
            stop.AutoSize = false;
            stop.MinimumSize = Size.Empty;
            stop.SetBounds(320, 88, 124, 28);
            stop.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            stop.Cursor = Cursors.Hand;
            monitor.Controls.Add(title); monitor.Controls.Add(description); monitor.Controls.Add(status); monitor.Controls.Add(activity); monitor.Controls.Add(stop);
            stop.Click += (s, e) =>
            {
                if (_handler == null) return;
                _handler.Request(TraceTracking ? "Smart Trace Stop" : "Smart Trace Resume");
                RaiseEvent();
            };
            monitor.FormClosed += (s, e) =>
            {
                if (!ReferenceEquals(_traceMonitor, monitor)) return;
                _traceMonitor = null;
                if (TraceTracking && _handler != null)
                {
                    _handler.Request("Smart Trace Stop");
                    RaiseEvent();
                }
            };
            _traceMonitor = monitor;
            TraceMonitorDocumentTitle = documentTitle ?? "";
            UpdateTraceMonitor(capturedChanges, documentTitle);
            if (ownerHandle != IntPtr.Zero) monitor.Show(new TraceWindowOwner(ownerHandle));
            else monitor.Show();
        }

        private static void ApplyTraceMonitorCorners(Form monitor)
        {
            if (monitor == null || monitor.IsDisposed || monitor.Width < 2 || monitor.Height < 2) return;
            using (GraphicsPath path = new GraphicsPath())
            {
                int diameter = 16;
                Rectangle bounds = new Rectangle(0, 0, monitor.Width, monitor.Height);
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                Region previous = monitor.Region;
                monitor.Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        private static GraphicsPath RoundedButtonPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            Rectangle arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter; path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter; path.AddArc(arc, 0, 90);
            arc.X = bounds.X; path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Region RoundedButtonRegion(int width, int height, int radius)
        {
            using (GraphicsPath path = RoundedButtonPath(new Rectangle(0, 0, Math.Max(1, width - 1), Math.Max(1, height - 1)), radius))
                return new Region(path);
        }

        private static void UpdateTraceMonitor(int capturedChanges, string documentTitle = null)
        {
            Form monitor = _traceMonitor;
            if (monitor == null || monitor.IsDisposed) return;
            if (monitor.InvokeRequired)
            {
                try { monitor.BeginInvoke(new Action(() => UpdateTraceMonitor(capturedChanges, documentTitle))); } catch { }
                return;
            }
            Label status = monitor.Controls["BIMlifyTraceMonitorStatus"] as Label;
            if (status == null) return;
            string model = String.IsNullOrWhiteSpace(documentTitle) ? TraceMonitorDocumentTitle : documentTitle;
            if (!String.IsNullOrWhiteSpace(documentTitle)) TraceMonitorDocumentTitle = documentTitle;
            Label title = monitor.Controls["BIMlifyTraceMonitorTitle"] as Label;
            if (title != null) title.Text = TraceTracking ? "Smart Trace is running" : "Smart Trace stopped";
            status.Text = (String.IsNullOrWhiteSpace(model) ? "Revit session" : model) + "  •  " + capturedChanges.ToString(CultureInfo.InvariantCulture) + " changes captured this session";
            ProgressBar activity = monitor.Controls.OfType<ProgressBar>().FirstOrDefault();
            if (activity != null)
            {
                activity.Style = TraceTracking ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
                activity.MarqueeAnimationSpeed = TraceTracking ? 24 : 0;
                activity.Value = 0;
            }
            Button stop = monitor.Controls.OfType<Button>().FirstOrDefault();
            if (stop != null) { stop.Text = TraceTracking ? "Stop Trace" : "Continue Trace"; stop.Enabled = true; }
        }

        private static string TraceKey(Document doc)
        {
            string path = doc?.PathName;
            string identity;
            if (!String.IsNullOrWhiteSpace(path)) identity = "saved:" + path.Trim();
            else
            {
                string projectIdentity = "";
                try { projectIdentity = doc?.ProjectInformation?.UniqueId ?? ""; } catch { }
                identity = "unsaved:" + (String.IsNullOrWhiteSpace(projectIdentity) ? (doc?.Title ?? "New Revit document") : projectIdentity);
            }
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity ?? ""))).Replace("-", "").Substring(0, 16);
        }

        private static string TaskProjectIdentity(Document doc)
        {
            if (doc == null) return "";
            string identity = "";
            try
            {
                if (doc.IsWorkshared)
                {
                    ModelPath central = doc.GetWorksharingCentralModelPath();
                    if (central != null) identity = "workshared:" + ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                }
            }
            catch { }
            if (String.IsNullOrWhiteSpace(identity))
            {
                try { if (!String.IsNullOrWhiteSpace(doc.PathName)) identity = "saved:" + doc.PathName.Trim(); } catch { }
            }
            if (String.IsNullOrWhiteSpace(identity))
            {
                string projectGuid = "";
                try { projectGuid = doc.ProjectInformation?.UniqueId ?? ""; } catch { }
                identity = "unsaved:" + (String.IsNullOrWhiteSpace(projectGuid) ? (doc.Title ?? "New Revit document") : projectGuid);
            }
            return identity;
        }

        private static string TracePath(Document doc, string key)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMlify", "Trace");
            Directory.CreateDirectory(directory);
            string name = Regex.Replace(Path.GetFileNameWithoutExtension(doc.Title ?? "Model"), "[^A-Za-z0-9_-]", "_");
            return Path.Combine(directory, name + "_" + key + ".csv");
        }

        private static string TraceCsv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        private static List<string> ParseTraceCsv(string line)
        {
            List<string> values = new List<string>();
            if (line == null) return values;
            StringBuilder value = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; } else quoted = !quoted; }
                else if (c == ',' && !quoted) { values.Add(value.ToString()); value.Clear(); }
                else value.Append(c);
            }
            values.Add(value.ToString()); return values;
        }

        private static List<string> SplitTraceIds(string value)
        {
            return (value ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => { long parsed; return Int64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0; })
                .Distinct(StringComparer.Ordinal).ToList();
        }

        private static string TraceElementSummary(IEnumerable<string> names)
        {
            List<string> unique = (names ?? Enumerable.Empty<string>()).Where(name => !String.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            if (unique.Count <= 1) return unique.FirstOrDefault() ?? "";
            string preview = String.Join(", ", unique.Take(3));
            return unique.Count.ToString(CultureInfo.InvariantCulture) + " elements: " + preview + (unique.Count > 3 ? ", …" : "");
        }

        private static List<TraceRecord> CompactTraceRows(IEnumerable<TraceRecord> rows)
        {
            return (rows ?? Enumerable.Empty<TraceRecord>())
                .GroupBy(row => String.Join("\u001f", row.Time, row.User, row.Change, row.Transaction), StringComparer.Ordinal)
                .Select(group =>
                {
                    List<TraceRecord> items = group.ToList();
                    if (items.Count == 1) return items[0];
                    string ids = String.Join(";", items.SelectMany(item => SplitTraceIds(item.ElementId)).Distinct(StringComparer.Ordinal));
                    string categories = String.Join(", ", items.Select(item => item.Category).Where(value => !String.IsNullOrWhiteSpace(value)).Distinct(StringComparer.CurrentCultureIgnoreCase));
                    return new TraceRecord(items[0].Time, items[0].User, items[0].Change, ids, categories,
                        TraceElementSummary(items.Select(item => item.Element)), items[0].Transaction);
                }).ToList();
        }

        private static void SaveTraceRows()
        {
            if (String.IsNullOrWhiteSpace(TraceFile)) return;
            File.WriteAllLines(TraceFile,
                new[] { "Time,User,Change,Element ID,Category,Element,Transaction" }
                    .Concat(TraceRows.Select(x => String.Join(",", new[] { x.Time, x.User, x.Change, x.ElementId, x.Category, x.Element, x.Transaction }.Select(TraceCsv)))),
                new UTF8Encoding(true));
        }

        private static void OnTraceDocumentChanged(object sender, DocumentChangedEventArgs args)
        {
            try
            {
                Document doc = args.GetDocument();
                if (doc == null || !String.Equals(TraceKey(doc), TraceDocumentKey, StringComparison.OrdinalIgnoreCase)) return;
                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                string transaction = String.Join(" / ", args.GetTransactionNames() ?? new List<string>());
                List<TraceRecord> added = new List<TraceRecord>();
                Action<ICollection<ElementId>, string> capture = (ids, change) =>
                {
                    List<ElementId> distinctIds = (ids ?? new List<ElementId>()).Where(id => id != null).GroupBy(id => id.Value).Select(group => group.First()).ToList();
                    if (distinctIds.Count == 0) return;
                    List<Element> elements = distinctIds.Where(id => change != "Deleted").Select(id => doc.GetElement(id)).Where(element => element != null).ToList();
                    string categories = String.Join(", ", elements.Select(element => element.Category?.Name ?? "").Where(name => !String.IsNullOrWhiteSpace(name)).Distinct(StringComparer.CurrentCultureIgnoreCase));
                    string names = change == "Deleted"
                        ? distinctIds.Count.ToString(CultureInfo.InvariantCulture) + " deleted elements"
                        : TraceElementSummary(elements.Select(element => element.Name));
                    added.Add(new TraceRecord(time, TraceUser, change,
                        String.Join(";", distinctIds.Select(id => id.Value.ToString(CultureInfo.InvariantCulture))),
                        categories, names, transaction));
                };
                capture(args.GetAddedElementIds(), "Added");
                capture(args.GetModifiedElementIds(), "Modified");
                capture(args.GetDeletedElementIds(), "Deleted");
                if (added.Count == 0) return;
                List<TraceRecord> snapshot;
                int capturedThisSession;
                lock (TraceSync)
                {
                    TraceRows.InsertRange(0, added);
                    if (TraceRows.Count > 5000) TraceRows.RemoveRange(5000, TraceRows.Count - 5000);
                    TraceCurrentSessionCount += added.Count;
                    capturedThisSession = TraceCurrentSessionCount;
                    SaveTraceRows();
                    snapshot = TraceRows.ToList();
                }
                _window?.SetTraceRows(snapshot);
                UpdateTraceMonitor(capturedThisSession, doc.Title);
            }
            catch { }
        }

        private sealed class SuiteForm : UserControl
        {
            private readonly SuiteHandler _handler;
            private readonly string _standaloneTool;
            private readonly string _standaloneTitle;
            private readonly ListBox _tools = new ListBox();
            private readonly DataGridView _grid = new DataGridView();
            private readonly DataGridView _traceGrid = new DataGridView();
            private TabControl _changeLogTabs;
            private TabPage _smartTracePage;
            private TabPage _smartLogPage;
            private readonly DataGridView _versionGrid = new DataGridView();
            private TabPage _versionsPage;
            private TabPage _collaboratorsPage;
            private TabControl _smartMapTabs;
            private TabControl _smartExportTabs;
            private TabPage _pdfExportPage;
            private TabPage _nwcExportPage;
            private TextBox _nwcFolder;
            private TextBox _nwcFileName;
            private System.Windows.Forms.ComboBox _nwcScope;
            private CheckedListBox _nwcViewPicker;
            private TextBox _nwcViewSearch;
            private readonly HashSet<long> _nwcSelectedViewIds = new HashSet<long>();
            private GoldCheckBox _nwcRoomAttribute;
            private GoldCheckBox _nwcFindMaterials;
            private GoldCheckBox _nwcExportUrls;
            private GoldCheckBox _nwcParts;
            private GoldCheckBox _nwcLights;
            private GoldCheckBox _nwcHideShaftOpeningLines;
            private GoldCheckBox _nwcHideMasses, _nwcHideLines;
            private GoldCheckBox _nwcOpenAfterExport;
            private NumericUpDown _nwcFacetingFactor;
            private System.Windows.Forms.ComboBox _nwcCoordinates;
            private System.Windows.Forms.ComboBox _nwcParameters;
            private GoldCheckBox _nwcLinks;
            private GoldCheckBox _nwcElementIds;
            private GoldCheckBox _nwcRooms;
            private GoldCheckBox _nwcDivideLevels;
            private GoldCheckBox _nwcLinkedCad;
            private GoldCheckBox _nwcElementProperties;
            private Label _nwcReadiness;
            private Button _nwcExportButton;
            private Form _nwcOptionsForm;
            private bool _nwcViewsRequested;
            private bool _nwcFileNameEdited;
            private bool _settingNwcProjectName;
            private bool _lastNwcNameWasUserEdited;
            private string _nwcProjectName = "";
            private List<NwcViewInfo> _nwcAvailableViews = new List<NwcViewInfo>();
            private string _nwcReadinessMessage = "Preflight: the output folder, file name, view scope, and installed Navisworks exporter are checked before export.";
            private NwcExportRequest _lastNwcRequest;
            private Panel _smartMapContent;
            private TabPage _parameterMappingPage;
            private TabPage _excelRevitCheckerPage;
            private readonly DataGridView _collaboratorsGrid = new DataGridView();
            private readonly Label _collaboratorsSummary = new Label();
            private readonly Label _status = new Label();
            private readonly Label _toolTitle = new Label();
            private readonly Label _toolDescription = new Label();
            private readonly Panel _stageFilterBar = new Panel();
            private readonly System.Windows.Forms.ComboBox _stageFilter = new System.Windows.Forms.ComboBox();
            private readonly Label _filterLabel = new Label();
            private bool _updatingFilter;
            private readonly Button _run = new Button();
            private readonly Button _saveInventory = new Button();
            private readonly Button _locate = new Button();
            private readonly Button _import = new Button();
            private readonly Button _export = new Button();
            private readonly Button _snapshot = new Button();
            private readonly Button _trace = new Button();
            private readonly Button _refreshCollaborators = new Button();
            private readonly Button _addTask = new Button();
            private readonly Button _deleteTask = new Button();
            private readonly FlowLayoutPanel _actions = new FlowLayoutPanel();
            private readonly List<ReportRow> _inputRows = new List<ReportRow>();
            private List<PdfSheetInfo> _pdfSheets;
            private Form _pdfOptionsForm;
            private Action<string, bool> _pdfExportProgress;
            private List<string> _lodRules = new List<string>();
            internal string _lodStage = "Design Development";
            private string _inputForTool = "";
            private List<ReportRow> _rows = new List<ReportRow>();
            private string _rowsForTool = "";
            private bool _loadingPdfSheets;
            private bool _pdfEditPending;
            private readonly Dictionary<string, InventoryEdit> _inventoryChanges = new Dictionary<string, InventoryEdit>(StringComparer.Ordinal);
            private bool _gridLayoutQueued;
            private string _taskProjectKey = "";
            private string _taskProjectName = "";
            internal PdfExportRequest PdfRequest;
            internal NwcExportRequest NwcRequest;

            internal SuiteForm(SuiteHandler handler, string standaloneTool, string standaloneTitle)
            {
                _handler = handler;
                _standaloneTool = standaloneTool;
                _standaloneTitle = standaloneTitle;
                Dock = DockStyle.Fill;
                MinimumSize = new Size(0, 0);
                AutoScaleMode = AutoScaleMode.Dpi;
                BackColor = Color.FromArgb(238, 241, 245);
                ForeColor = Color.FromArgb(55, 67, 79);
                Font = new Font("Segoe UI", 9f);
                ApplyReviewCleanWindowFrame();
                Resize += (s, e) => ApplyReviewCleanWindowFrame();
                Paint += (s, e) =>
                {
                    if (ClientSize.Width < 4 || ClientSize.Height < 4) return;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Pen border = new Pen(Color.FromArgb(222, 227, 233), 1f))
                using (GraphicsPath path = ReviewCleanFramePath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 14))
                        e.Graphics.DrawPath(border, path);
                };

                Form railOwner = DashboardEnhancements.DashboardWindow;
                ReviewCleanSidebar rail = new ReviewCleanSidebar(railOwner, ReturnToDashboard) { Dock = DockStyle.Left };
                DashboardEnhancements.AttachToolSearchButton(rail, railOwner, 108, true);

                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(238, 241, 245), Padding = Padding.Empty, Margin = Padding.Empty };
                // Match the Review & Clean header's 128px band and typography/placement.
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 128f));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));

                Panel head = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 247, 245), Padding = new Padding(18, 9, 4, 9) };
                Label title = new Label { Text = String.IsNullOrEmpty(_standaloneTitle) ? "Smart QA/QC" : _standaloneTitle, AutoSize = false, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.FromArgb(78, 85, 92), Left = 18, Top = 10, Width = 650, Height = 25, TextAlign = ContentAlignment.MiddleLeft, Padding = Padding.Empty };
                Label sub = new Label { Text = String.IsNullOrEmpty(_standaloneTool) ? "Review model quality, coordination and delivery requirements." : HeaderDescription(_standaloneTool), AutoSize = false, Font = new Font("Segoe UI", 8f), ForeColor = Color.FromArgb(102, 109, 118), Left = 18, Top = 38, Width = 680, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = Padding.Empty, AutoEllipsis = true };
                Panel brand = new Panel { Width = 132, Height = 116, Dock = DockStyle.Right, BackColor = head.BackColor };
                PictureBox brandImage = new PictureBox { Left = 4, Top = 14, Width = 108, Height = 88, SizeMode = PictureBoxSizeMode.Zoom, BackColor = head.BackColor };
                string logoPath = Path.Combine(Path.GetDirectoryName(typeof(BimQualitySuite).Assembly.Location), "Branding", "LogoMarkWordmarkGoldTransparent.png");
                try { if (File.Exists(logoPath)) using (Image source = Image.FromFile(logoPath)) brandImage.Image = new Bitmap(source); } catch { }
                brand.Controls.Add(brandImage);
                head.Controls.Add(title); head.Controls.Add(sub); head.Controls.Add(brand);
                Action layoutHeaderText = () =>
                {
                    // Mirror AddYellowPanelAccent's Review & Clean label layout.
                    int availableTextWidth = Math.Max(220, head.ClientSize.Width - brand.Width - 42);
                    title.Width = availableTextWidth;
                    sub.Width = availableTextWidth;
                    title.Top = 10; title.Height = 25;
                    sub.Top = 38; sub.Height = 24;
                    if (TextRenderer.MeasureText(sub.Text ?? string.Empty, sub.Font,
                        new Size(Math.Max(1, sub.Width), 1000),
                        TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height <= 22)
                    {
                        sub.Height = 22;
                        sub.TextAlign = ContentAlignment.MiddleLeft;
                    }
                    int blockHeight = Math.Max(title.Bottom, sub.Bottom) - Math.Min(title.Top, sub.Top);
                    int spare = Math.Max(0, head.ClientSize.Height - 2 - 14 - blockHeight);
                    int shift = spare - Math.Min(title.Top, sub.Top);
                    title.Top += shift;
                    sub.Top += shift;
                };
                layoutHeaderText();
                head.Resize += (s, e) => layoutHeaderText();
                head.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    int y = head.Height - 2;
                    using (Pen gold = new Pen(Color.FromArgb(214, 171, 61), 2f))
                        e.Graphics.DrawLine(gold, 1, y, head.Width - 2, y);
                    using (Pen marker = new Pen(Color.FromArgb(224, 194, 116), 1.25f))
                        e.Graphics.DrawLine(marker, 9, Math.Max(8, title.Top - 2), 9, Math.Min(head.Height - 12, sub.Bottom + 2));
                };
                Panel body = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(238, 241, 245), Padding = new Padding(12, 10, 12, 10) };
                RoundedCard navigation = new RoundedCard { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(8) };
                navigation.Width = 270;
                Label navTitle = new Label { Text = "SMART QA/QC TOOLS", Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(55, 67, 79), BackColor = Color.White };
                _tools.Dock = DockStyle.Fill; _tools.BorderStyle = BorderStyle.None; _tools.BackColor = Color.White; _tools.ForeColor = Color.FromArgb(55, 67, 79); _tools.Font = new Font("Segoe UI", 9f); _tools.DrawMode = DrawMode.OwnerDrawFixed; _tools.ItemHeight = 26; _tools.IntegralHeight = false;
                IEnumerable<string> visibleToolNames = String.IsNullOrEmpty(_standaloneTool)
                    ? ToolNames
                    : (String.Equals(_standaloneTool, "Parameter Mapping", StringComparison.Ordinal)
                        ? new[] { "Parameter Mapping", "Excel ↔ Revit Data Checker" }
                        : (String.Equals(_standaloneTool, "PDF Batch Export", StringComparison.Ordinal)
                            ? new[] { "PDF Batch Export", "NWC Export" }
                            : new[] { _standaloneTool }));
                foreach (string item in visibleToolNames) _tools.Items.Add(item);
                _tools.SelectedIndex = 0;
                _tools.DrawItem += (s,e) =>
                {
                    if (e.Index < 0) return;
                    bool selected = (e.State & DrawItemState.Selected) != 0;
                    using (SolidBrush fill = new SolidBrush(selected ? Color.FromArgb(255, 247, 226) : Color.White)) e.Graphics.FillRectangle(fill, e.Bounds);
                    if (selected) using (Pen line = new Pen(Color.FromArgb(214, 171, 61), 2f)) e.Graphics.DrawLine(line, e.Bounds.Left + 1, e.Bounds.Top + 2, e.Bounds.Left + 1, e.Bounds.Bottom - 2);
                    TextRenderer.DrawText(e.Graphics, Convert.ToString(_tools.Items[e.Index]), _tools.Font, Rectangle.Inflate(e.Bounds, -9, 0), Color.FromArgb(55, 67, 79), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                };
                navigation.Controls.Add(_tools); navigation.Controls.Add(navTitle);

                RoundedCard workspace = new RoundedCard { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10) };
                Panel intro = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Color.White, Padding = new Padding(8, 8, 12, 6) };
                _toolTitle.Dock = DockStyle.Top; _toolTitle.Height = 20; _toolTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold); _toolTitle.ForeColor = Color.FromArgb(55, 67, 79); _toolTitle.Text = "Workset Checker";
                _toolDescription.Dock = DockStyle.Fill; _toolDescription.Font = new Font("Segoe UI", 8f); _toolDescription.ForeColor = Color.FromArgb(105, 117, 129); _toolDescription.TextAlign = ContentAlignment.MiddleLeft; _toolDescription.Text = ToolDescription(_toolTitle.Text);
                intro.Controls.Add(_toolDescription); intro.Controls.Add(_toolTitle);
                _stageFilterBar.Dock = DockStyle.Top; _stageFilterBar.Height = 38; _stageFilterBar.BackColor = Color.White; _stageFilterBar.Visible = true;
                _stageFilterBar.Controls.Add(_onlySelected);
                _stageFilterBar.Controls.Add(_includeRevitLinks);
                _includeRevitLinks.CheckedChanged += (sender, args) => ScheduleAuditLoad();
                _filterLabel.Text = "Filter by category"; _filterLabel.SetBounds(14, 9, 116, 20); _filterLabel.Font = new Font("Segoe UI", 8f); _filterLabel.ForeColor = Color.FromArgb(105, 117, 129); _filterLabel.TextAlign = ContentAlignment.MiddleLeft; _filterLabel.BackColor = Color.White;
                _stageFilter.SetBounds(136, 5, 190, 27); _stageFilter.DropDownStyle = ComboBoxStyle.DropDownList; _stageFilter.Font = new Font("Segoe UI", 8.5f);
                _stageFilter.SelectedIndexChanged += (s, e) => { if (!_updatingFilter) ApplyStageFilter(); };
                _stageFilterBar.Controls.Add(_filterLabel); _stageFilterBar.Controls.Add(_stageFilter);
                _sheetAuditSearch.AutoSize = false;
                _sheetAuditSearch.SetBounds(112, 7, 190, 23);
                BIMlify.SearchControls.SearchGlyph.Attach(_sheetAuditSearch);
                _sheetAuditSearch.Font = _stageFilter.Font;
                _sheetAuditSearch.TextChanged += (s, e) => ApplyStageFilter();
                _stageFilterBar.Controls.Add(_sheetAuditSearch);
                _linkActionLabel.Text = "Link action";
                _linkActionLabel.SetBounds(346, 9, 72, 20);
                _linkActionLabel.Font = _filterLabel.Font;
                _linkActionLabel.ForeColor = _filterLabel.ForeColor;
                _linkActionLabel.TextAlign = ContentAlignment.MiddleLeft;
                _linkAction.SetBounds(422, 5, 214, 27);
                _linkAction.DropDownStyle = _stageFilter.DropDownStyle;
                _linkAction.Font = _stageFilter.Font;
                _linkAction.AccessibleName = "Link action";
                _linkAction.Items.AddRange(new object[] { "Refresh links", "Reload selected link", "Change selected link path" });
                _linkAction.SelectedIndex = 0;
                _linkActionLabel.Visible = _linkAction.Visible = _standaloneTool == "Smart Links";
                _linkAction.SelectedIndexChanged += (s, e) => ConfigureButtons();
                _stageFilterBar.Controls.Add(_linkActionLabel);
                _stageFilterBar.Controls.Add(_linkAction);
                var quantityGroupingLabel = new Label
                {
                    Text = "Group quantities by",
                    Font = _filterLabel.Font,
                    ForeColor = _filterLabel.ForeColor,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                quantityGroupingLabel.SetBounds(346, 9, 114, 20);
                _quantityGrouping.SetBounds(464, 5, _stageFilter.Width, 27);
                _quantityGrouping.DropDownStyle = _stageFilter.DropDownStyle;
                _quantityGrouping.Font = _stageFilter.Font;
                _quantityGrouping.AccessibleName = quantityGroupingLabel.Text;
                _quantityGrouping.Items.AddRange(new object[] { "Category", "Level" });
                _quantityGrouping.SelectedIndex = 0;
                quantityGroupingLabel.Visible = _quantityGrouping.Visible = _standaloneTool == "Quantity Reports";
                _stageFilterBar.Controls.Add(quantityGroupingLabel);
                _stageFilterBar.Controls.Add(_quantityGrouping);
                Panel gridFrame = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1), Margin = new Padding(0, 8, 0, 0) };
                _grid.Dock = DockStyle.Fill; _grid.BackgroundColor = Color.White; _grid.BorderStyle = BorderStyle.None; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.AllowUserToResizeColumns = true; _grid.ReadOnly = true; _grid.RowHeadersVisible = false; _grid.AutoGenerateColumns = false; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None; _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = true;
                ReportGridStyle.Apply(_grid);
                _grid.Columns.Add("Id", "Revit Element ID"); _grid.Columns.Add("Category", "Category"); _grid.Columns.Add("Element", "Element"); _grid.Columns.Add("Finding", "Finding"); _grid.Columns.Add("Details", "Details"); _grid.Columns.Add("Stage", "Stage"); _grid.Columns["Stage"].Visible = false;
                int[] initialGridWidths = { 88, 80, 205, 115, 160, 120 };
                int[] initialGridMinimums = { 70, 68, 112, 88, 110, 90 };
                for (int i = 0; i < _grid.Columns.Count; i++) { _grid.Columns[i].Width = initialGridWidths[i]; _grid.Columns[i].MinimumWidth = initialGridMinimums[i]; }
                _grid.SizeChanged += (s, e) => QueueGridColumnLayout();
                _grid.HandleCreated += (s, e) => QueueGridColumnLayout();
                _grid.CellEndEdit += (s, e) => { if (!DashboardEnhancements.IsCancellingGridEdit(_grid)) SavePdfSheetEdit(e); };
                gridFrame.Controls.Add(_grid);
                if (String.IsNullOrEmpty(_standaloneTool)) AddInventoryTabs(gridFrame);
                if (String.Equals(_standaloneTool, "Parameter Mapping", StringComparison.Ordinal))
                {
                    _smartMapTabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f), Padding = new System.Drawing.Point(12, 4), BackColor = Color.White, DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(230, 30) };
                    _smartMapTabs.DrawItem += (s, e) =>
                    {
                        if (e.Index < 0 || e.Index >= _smartMapTabs.TabPages.Count) return;
                        Rectangle bounds = e.Bounds;
                        bool selected = e.Index == _smartMapTabs.SelectedIndex;
                        using (SolidBrush fill = new SolidBrush(selected ? Color.White : Color.FromArgb(222, 226, 231))) e.Graphics.FillRectangle(fill, bounds);
                        using (Pen outline = new Pen(Color.FromArgb(161, 169, 178), 1f)) e.Graphics.DrawRectangle(outline, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
                        using (Font tabFont = new Font(_smartMapTabs.Font, selected ? FontStyle.Bold : FontStyle.Regular))
                            TextRenderer.DrawText(e.Graphics, _smartMapTabs.TabPages[e.Index].Text, tabFont, Rectangle.Inflate(bounds, -5, -2), Color.FromArgb(67, 76, 86), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        if (selected) using (Pen accent = new Pen(Color.FromArgb(220, 177, 82), 2f)) e.Graphics.DrawLine(accent, bounds.Left + 1, bounds.Bottom - 2, bounds.Right - 2, bounds.Bottom - 2);
                    };
                    _parameterMappingPage = new TabPage("Parameter Mapping") { BackColor = Color.White, Padding = new Padding(8) };
                    _excelRevitCheckerPage = new TabPage("Excel ↔ Revit Data Checker") { BackColor = Color.White, Padding = new Padding(8) };
                    _smartMapContent = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
                    _smartMapContent.Controls.Add(gridFrame);
                    _smartMapContent.Controls.Add(_stageFilterBar);
                    _smartMapContent.Controls.Add(intro);
                    _parameterMappingPage.Controls.Add(_smartMapContent);
                    _smartMapTabs.TabPages.Add(_parameterMappingPage);
                    _smartMapTabs.TabPages.Add(_excelRevitCheckerPage);
                    _smartMapTabs.SelectedIndexChanged += (s, e) =>
                    {
                        TabPage page = _smartMapTabs.SelectedTab;
                        if (page == null || _smartMapContent == null) return;
                        _smartMapContent.Parent?.Controls.Remove(_smartMapContent);
                        page.Controls.Add(_smartMapContent);
                        int selectedIndex = page == _excelRevitCheckerPage ? 1 : 0;
                        if (_tools.SelectedIndex != selectedIndex) _tools.SelectedIndex = selectedIndex;
                        ConfigureButtons();
                    };
                    workspace.Controls.Add(_smartMapTabs);
                }
                else if (String.Equals(_standaloneTool, "Model Change Log", StringComparison.Ordinal))
                {
                    _changeLogTabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f), Padding = new System.Drawing.Point(12, 4), BackColor = Color.White, DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(152, 30) };
                    _changeLogTabs.DrawItem += (s, e) =>
                    {
                        if (e.Index < 0 || e.Index >= _changeLogTabs.TabPages.Count) return;
                        Rectangle bounds = e.Bounds;
                        bool selected = e.Index == _changeLogTabs.SelectedIndex;
                        using (SolidBrush fill = new SolidBrush(selected ? Color.White : Color.FromArgb(222, 226, 231))) e.Graphics.FillRectangle(fill, bounds);
                        using (Pen outline = new Pen(Color.FromArgb(161, 169, 178), 1f)) e.Graphics.DrawRectangle(outline, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
                        using (Font tabFont = new Font(_changeLogTabs.Font, selected ? FontStyle.Bold : FontStyle.Regular))
                            TextRenderer.DrawText(e.Graphics, _changeLogTabs.TabPages[e.Index].Text, tabFont, Rectangle.Inflate(bounds, -5, -2), Color.FromArgb(67, 76, 86), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        if (selected) using (Pen accent = new Pen(Color.FromArgb(220, 177, 82), 2f)) e.Graphics.DrawLine(accent, bounds.Left + 1, bounds.Bottom - 2, bounds.Right - 2, bounds.Bottom - 2);
                    };
                    _smartTracePage = new TabPage("Smart Trace") { BackColor = Color.White, Padding = new Padding(8) };
                    _smartLogPage = new TabPage("Smart Log") { BackColor = Color.White, Padding = new Padding(8) };
                    _collaboratorsPage = new TabPage("Collaborators") { BackColor = Color.White, Padding = new Padding(8) };
                    Panel traceIntro = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White, Padding = new Padding(8, 6, 8, 4) };
                    Label traceTitle = new Label { Text = "Live Model Changes", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(55, 67, 79), BackColor = Color.White };
                    Label traceDescription = new Label { Text = "Records Revit change events while tracking. Time is local; user is the signed-in Revit session, not verified authorship or ACC history.", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 7.5f), ForeColor = Color.FromArgb(105, 117, 129), BackColor = Color.White, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
                    traceIntro.Controls.Add(traceDescription); traceIntro.Controls.Add(traceTitle);
                    Panel traceGridFrame = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1), Margin = new Padding(0, 8, 0, 0) };
                    _traceGrid.Dock = DockStyle.Fill; _traceGrid.BackgroundColor = Color.White; _traceGrid.BorderStyle = BorderStyle.None; _traceGrid.AllowUserToAddRows = false; _traceGrid.AllowUserToDeleteRows = false; _traceGrid.AllowUserToResizeColumns = true; _traceGrid.ReadOnly = true; _traceGrid.RowHeadersVisible = false; _traceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _traceGrid.MultiSelect = true; _traceGrid.EnableHeadersVisualStyles = false; _traceGrid.ColumnHeadersHeight = 30; _traceGrid.RowTemplate.Height = 28; _traceGrid.Font = new Font("Segoe UI", 7.8f); _traceGrid.GridColor = Color.FromArgb(220, 224, 229);
                    _traceGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 245); _traceGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 67, 79); _traceGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.2f, FontStyle.Bold); _traceGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False; _traceGrid.DefaultCellStyle.BackColor = Color.White; _traceGrid.DefaultCellStyle.ForeColor = Color.FromArgb(55, 67, 79); _traceGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201); _traceGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(55, 67, 79); _traceGrid.DefaultCellStyle.Padding = new Padding(3, 2, 3, 2); _traceGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.False; _traceGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 252);
                    string[] traceHeaders = { "Local time", "Session user", "Change", "Element ID", "Category", "Element", "Revit transaction" };
                    float[] traceWeights = { 1.25f, 1f, 0.85f, 0.95f, 1.25f, 1.7f, 1.5f };
                    for (int i = 0; i < traceHeaders.Length; i++) { DataGridViewColumn column = new DataGridViewTextBoxColumn { Name = "Trace" + i.ToString(CultureInfo.InvariantCulture), HeaderText = traceHeaders[i], AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = traceWeights[i], MinimumWidth = 62, SortMode = DataGridViewColumnSortMode.NotSortable }; _traceGrid.Columns.Add(column); }
                    traceGridFrame.Controls.Add(_traceGrid);
                    _smartTracePage.Controls.Add(traceGridFrame); _smartTracePage.Controls.Add(traceIntro);
                    // The window header already carries the Smart Log title and description;
                    // avoid repeating them inside the tab page where docked labels crowded.
                    _smartLogPage.Controls.Add(gridFrame); _smartLogPage.Controls.Add(_stageFilterBar);
                    Panel collaboratorsIntro = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.White, Padding = new Padding(8, 6, 8, 4) };
                    Label collaboratorsTitle = new Label { Text = "Worksharing Collaborators", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(55, 67, 79), BackColor = Color.White };
                    _collaboratorsSummary.Text = "Refresh to inspect element owners and last-saved-by users in this workshared central or ACC cloud model. This is not the full ACC activity history.";
                    _collaboratorsSummary.Dock = DockStyle.Fill; _collaboratorsSummary.Font = new Font("Segoe UI", 7.5f); _collaboratorsSummary.ForeColor = Color.FromArgb(105, 117, 129); _collaboratorsSummary.BackColor = Color.White; _collaboratorsSummary.AutoEllipsis = true; _collaboratorsSummary.TextAlign = ContentAlignment.MiddleLeft;
                    collaboratorsIntro.Controls.Add(_collaboratorsSummary); collaboratorsIntro.Controls.Add(collaboratorsTitle);
                    Panel collaboratorsGridFrame = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1), Margin = new Padding(0, 8, 0, 0) };
                    _collaboratorsGrid.Dock = DockStyle.Fill; _collaboratorsGrid.BackgroundColor = Color.White; _collaboratorsGrid.BorderStyle = BorderStyle.None; _collaboratorsGrid.AllowUserToAddRows = false; _collaboratorsGrid.AllowUserToDeleteRows = false; _collaboratorsGrid.ReadOnly = true; _collaboratorsGrid.RowHeadersVisible = false; _collaboratorsGrid.AutoGenerateColumns = false; _collaboratorsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; _collaboratorsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _collaboratorsGrid.MultiSelect = false; _collaboratorsGrid.EnableHeadersVisualStyles = false; _collaboratorsGrid.ColumnHeadersHeight = 30; _collaboratorsGrid.RowTemplate.Height = 28; _collaboratorsGrid.Font = new Font("Segoe UI", 8f); _collaboratorsGrid.GridColor = Color.FromArgb(220, 224, 229);
                    _collaboratorsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 245); _collaboratorsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 67, 79); _collaboratorsGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold); _collaboratorsGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False; _collaboratorsGrid.DefaultCellStyle.BackColor = Color.White; _collaboratorsGrid.DefaultCellStyle.ForeColor = Color.FromArgb(55, 67, 79); _collaboratorsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201); _collaboratorsGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(55, 67, 79); _collaboratorsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 252);
                    _collaboratorsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Collaborator", HeaderText = "User", FillWeight = 42, MinimumWidth = 130 });
                    _collaboratorsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OwnedElements", HeaderText = "Currently owns elements", FillWeight = 29, MinimumWidth = 130 });
                    _collaboratorsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastChanged", HeaderText = "Last changed elements", FillWeight = 29, MinimumWidth = 130 });
                    _collaboratorsGrid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        Name = "UserDetails", HeaderText = "Details", ReadOnly = true,
                        AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 64,
                        SortMode = DataGridViewColumnSortMode.NotSortable
                    });
                    collaboratorsGridFrame.Controls.Add(_collaboratorsGrid);
                    _collaboratorsPage.Controls.Add(collaboratorsGridFrame); _collaboratorsPage.Controls.Add(collaboratorsIntro);
                    _versionsPage = BuildVersionsPage();
                    _changeLogTabs.TabPages.Add(_smartTracePage); _changeLogTabs.TabPages.Add(_collaboratorsPage); _changeLogTabs.TabPages.Add(_smartLogPage); _changeLogTabs.TabPages.Add(_versionsPage); _changeLogTabs.SelectedTab = _smartTracePage;
                    _changeLogTabs.SelectedIndexChanged += (s, e) =>
                    {
                        ConfigureButtons();
                        if (IsCollaboratorsTab() && !_collaboratorsLoaded && !_collaboratorsLoading) RefreshCollaborators();
                        if (IsSnapshotTab() && !_snapshotPreviewRequested) QueueSnapshotPreview();
                    };
                    workspace.Controls.Add(_changeLogTabs);
                }
                else if (String.Equals(_standaloneTool, "PDF Batch Export", StringComparison.Ordinal))
                {
                    _smartExportTabs = CreateSmartExportTabs();
                    _pdfExportPage = new TabPage("PDF/DWG Batch") { BackColor = Color.White, Padding = new Padding(7) };
                    _nwcExportPage = new TabPage("NWC Export") { BackColor = Color.White, Padding = new Padding(7) };
                    Panel pdfContent = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
                    pdfContent.Controls.Add(gridFrame); pdfContent.Controls.Add(_stageFilterBar); pdfContent.Controls.Add(intro);
                    _pdfExportPage.Controls.Add(pdfContent);
                    _nwcExportPage.Controls.Add(BuildNwcExportPage());
                    _smartExportTabs.TabPages.Add(_pdfExportPage);
                    _smartExportTabs.TabPages.Add(_nwcExportPage);
                    _smartExportTabs.SelectedIndexChanged += (s, e) =>
                    {
                        string selectedTool = _smartExportTabs.SelectedTab == _nwcExportPage ? "NWC Export" : "PDF Batch Export";
                        if (!String.Equals(_tools.SelectedItem as string, selectedTool, StringComparison.Ordinal))
                            _tools.SelectedItem = selectedTool;
                        if (selectedTool == "NWC Export") LoadNwcViews();
                        ConfigureButtons();
                        if (selectedTool == "NWC Export") SetStatus("Configure the output, scope, and model-data options before exporting.");
                    };
                    workspace.Controls.Add(_smartExportTabs);
                }
                else if (!String.Equals(_standaloneTool, "Parameter Mapping", StringComparison.Ordinal)) { workspace.Controls.Add(gridFrame); workspace.Controls.Add(_stageFilterBar); workspace.Controls.Add(intro); }
                TableLayoutPanel cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = body.BackColor, Margin = Padding.Empty, Padding = Padding.Empty };
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, String.IsNullOrEmpty(_standaloneTool) ? 270f : 0f));
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, String.IsNullOrEmpty(_standaloneTool) ? 10f : 0f));
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                navigation.Visible = String.IsNullOrEmpty(_standaloneTool);
                cards.Controls.Add(navigation, 0, 0);
                cards.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = body.BackColor, Margin = Padding.Empty }, 1, 0);
                cards.Controls.Add(workspace, 2, 0);
                body.Controls.Add(cards);

                Panel bottom = new Panel { Dock = DockStyle.Fill, Height = 64, BackColor = Color.FromArgb(216, 221, 227), Padding = Padding.Empty, Margin = Padding.Empty };
                _status.Text = "Ready. Select a check, then Run Check."; _status.ForeColor = Color.FromArgb(105, 117, 129); _status.Font = new Font("Segoe UI", 8f); _status.TextAlign = ContentAlignment.MiddleLeft; _status.AutoEllipsis = true; _status.Left = 12; _status.Top = 12; _status.Height = 30; _status.Width = 400; _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                _actions.Dock = DockStyle.Right; _actions.Width = 672; _actions.Height = 64; _actions.FlowDirection = FlowDirection.RightToLeft; _actions.WrapContents = false; _actions.BackColor = Color.Transparent; _actions.Padding = new Padding(0, 17, 12, 0); _actions.Margin = Padding.Empty;
                Button close = MakeButton("Close", 88, false); close.Click += (s,e) => ReturnToDashboard();
                _addTask = MakeButton("Add Task", 96, false); _addTask.Click += (s,e) => AddTask();
                _deleteTask = MakeButton("Delete", 88, false); _deleteTask.Enabled = false; _deleteTask.Click += (s,e) => DeleteSelectedTasks();
                _snapshot = MakeButton("Save Snapshot", 104, false); _snapshot.Click += (s,e) => SaveSnapshot();
                _trace = MakeButton("Start Trace", 104, false); _trace.Click += (s,e) => ToggleTrace();
                _collaboratorsGrid.CellClick += (sender, args) =>
                {
                    if (args.ColumnIndex >= 0 && _collaboratorsGrid.Columns[args.ColumnIndex].Name == "UserDetails")
                        ShowCollaboratorDetails(args.RowIndex);
                };
                _collaboratorsGrid.CellDoubleClick += (sender, args) =>
                {
                    if (args.ColumnIndex >= 0 && _collaboratorsGrid.Columns[args.ColumnIndex].Name != "UserDetails")
                        ShowCollaboratorDetails(args.RowIndex);
                };

                _refreshCollaborators = MakeButton("Refresh Users", 112, false); _refreshCollaborators.Click += (s,e) => RefreshCollaborators();
                _import = MakeButton("Import CSV", 104, false); _import.Click += (s,e) => ImportCsv();
                _export = MakeButton("Export CSV", 104, false); _export.Click += (s,e) => ExportCsv();
                _locate = MakeButton("Locate Element", 112, false); _locate.Enabled = false; _locate.Click += (s,e) => LocateSelectedElements();
                _run = MakeButton("Run Check", 96, true); _run.Click += (s,e) => RunSelected();
                _saveInventory = MakeButton("Save Changes", 112, true); _saveInventory.Visible = false; _saveInventory.Click += (s,e) => SaveInventoryChanges();
                // FlowDirection is right-to-left: insert Locate Element after Run Check
                // visually, before the next applicable footer action, with shared margins.
                _actions.Controls.Add(close); _actions.Controls.Add(_saveInventory); _actions.Controls.Add(_addTask); _actions.Controls.Add(_deleteTask); _actions.Controls.Add(_snapshot); _actions.Controls.Add(_trace); _actions.Controls.Add(_refreshCollaborators); _actions.Controls.Add(_import); _actions.Controls.Add(_export); _actions.Controls.Add(_locate); _actions.Controls.Add(_run);
                bottom.Controls.Add(_status); bottom.Controls.Add(_actions);
                bottom.Resize += (s,e) => LayoutFooter();

                layout.Controls.Add(head, 0, 0); layout.Controls.Add(body, 0, 1); layout.Controls.Add(bottom, 0, 2);
                Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(238, 241, 245), Margin = Padding.Empty, Padding = Padding.Empty };
                content.Controls.Add(layout); content.Controls.Add(rail);
                TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, BackColor = Color.FromArgb(238, 241, 245), Padding = Padding.Empty, Margin = Padding.Empty };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.Controls.Add(content, 0, 0);
                Controls.Add(root);
                DashboardEnhancements.ApplyLightGoldDropdowns(root);
                _grid.SelectionChanged += (s,e) => UpdateLocateButton();
                _grid.SelectionChanged += (s,e) => UpdateDeleteTaskButton();
                _traceGrid.SelectionChanged += (s,e) => UpdateLocateButton();
                _tools.SelectedIndexChanged += (s,e) => ToolSelectionChanged();
                QueueGridColumnLayout();
                ToolSelectionChanged();
                ConfigureButtons();
            }

            private void ReturnToDashboard() => BimQualitySuite.NavigateHome();

            internal bool IsStandaloneFor(string tool) => String.Equals(_standaloneTool ?? "", tool ?? "", StringComparison.OrdinalIgnoreCase);

            private TabControl CreateSmartExportTabs()
            {
                TabControl tabs = new TabControl
                {
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 8.5f),
                    Padding = new System.Drawing.Point(12, 4),
                    BackColor = Color.White,
                    DrawMode = TabDrawMode.OwnerDrawFixed,
                    SizeMode = TabSizeMode.Fixed,
                    ItemSize = new Size(132, 30)
                };
                tabs.DrawItem += (s, e) =>
                {
                    if (e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
                    Rectangle bounds = e.Bounds;
                    bool selected = e.Index == tabs.SelectedIndex;
                    using (SolidBrush fill = new SolidBrush(selected ? Color.White : Color.FromArgb(222, 226, 231))) e.Graphics.FillRectangle(fill, bounds);
                    using (Pen outline = new Pen(Color.FromArgb(161, 169, 178), 1f)) e.Graphics.DrawRectangle(outline, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
                    using (Font tabFont = new Font(tabs.Font, selected ? FontStyle.Bold : FontStyle.Regular))
                        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabFont, Rectangle.Inflate(bounds, -5, -2), Color.FromArgb(67, 76, 86), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    if (selected) using (Pen accent = new Pen(Color.FromArgb(220, 177, 82), 2f)) e.Graphics.DrawLine(accent, bounds.Left + 1, bounds.Bottom - 2, bounds.Right - 2, bounds.Bottom - 2);
                };
                return tabs;
            }

            private System.Windows.Forms.Control BuildNwcExportPage()
            {
                Color ink = Color.FromArgb(55, 67, 79);
                Color muted = Color.FromArgb(105, 117, 129);
                Color soft = Color.FromArgb(247, 247, 245);
                Panel root = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 10, 12, 10) };
                Label title = new Label { Text = "Navisworks Cache Export", Left = 10, Top = 10, Width = 760, Height = 24, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = ink, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                Label description = new Label { Text = "Create an NWC from the whole model, a selected 3D view, or the current Revit selection.", Left = 10, Top = 38, Width = 820, Height = 22, Font = new Font("Segoe UI", 8f), ForeColor = muted, AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                RoundedCard summary = new RoundedCard { Left = 20, Top = 82, Width = 880, Height = 218, BackColor = soft, Anchor = AnchorStyles.Top };
                Label cardTitle = new Label { Text = "NWC export setup", Left = 22, Top = 16, Width = 800, Height = 24, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = ink, BackColor = soft };
                Label cardIntro = new Label { Text = "Review the main choices here, then open the full settings before exporting.", Left = 22, Top = 43, Width = 800, Height = 20, Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = soft, AutoEllipsis = true };
                TableLayoutPanel choices = new TableLayoutPanel { Left = 18, Top = 76, Width = 844, Height = 112, ColumnCount = 3, RowCount = 1, BackColor = soft, Margin = Padding.Empty, Padding = Padding.Empty };
                for (int i = 0; i < 3; i++) choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
                choices.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                string[] headings = { "Output", "Scope & coordinates", "Model data" };
                string[] bodies = {
                    "Choose the destination folder and a clear file name.",
                    "Export the model, a 3D view, or the current selection with shared or internal coordinates.",
                    "Control linked models, element IDs, properties, rooms, levels, CAD conversion, and parameters."
                };
                for (int i = 0; i < headings.Length; i++)
                {
                    RoundedCard item = new RoundedCard { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(i == 0 ? 0 : 6, 0, i == 2 ? 0 : 6, 0), Padding = new Padding(12, 10, 12, 8) };
                    Label heading = new Label { Text = headings[i], Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = ink, BackColor = Color.White };
                    Label body = new Label { Text = bodies[i], Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = Color.White, AutoEllipsis = true };
                    item.Controls.Add(body); item.Controls.Add(heading); choices.Controls.Add(item, i, 0);
                }
                summary.Controls.Add(choices); summary.Controls.Add(cardIntro); summary.Controls.Add(cardTitle);
                Label readiness = new Label { Text = _nwcReadinessMessage, Left = 20, Top = 320, Width = 880, Height = 28, Font = new Font("Segoe UI", 8f), ForeColor = muted, AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleLeft };
                root.Controls.Add(title); root.Controls.Add(description); root.Controls.Add(summary); root.Controls.Add(readiness);
                root.Resize += (s, e) =>
                {
                    int cardWidth = Math.Max(0, Math.Min(920, root.ClientSize.Width - 24));
                    title.Width = Math.Max(0, root.ClientSize.Width - 20);
                    description.Width = Math.Max(0, root.ClientSize.Width - 20);
                    summary.Width = cardWidth;
                    summary.Left = Math.Max(12, (root.ClientSize.Width - cardWidth) / 2);
                    choices.Width = Math.Max(0, cardWidth - 36);
                    readiness.Left = summary.Left;
                    readiness.Width = cardWidth;
                };
                return root;
            }
            private Form NwcExportOptionsDialog()
            {
                _nwcFileNameEdited = _lastNwcNameWasUserEdited;
                Form form = DashboardEnhancements.CreateSettingsStyleDialog();
                form.Text = "NWC Export"; form.Size = new Size(1080, 720); form.MinimumSize = new Size(980, 720);
                form.StartPosition = FormStartPosition.CenterScreen; form.ShowInTaskbar = false; form.AutoScaleMode = AutoScaleMode.None;
                form.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
                Color surface = Color.FromArgb(238, 241, 245), white = Color.White, ink = Color.FromArgb(55, 67, 79), muted = Color.FromArgb(105, 117, 129);
                Panel page = new Panel { Dock = DockStyle.Fill, Size = new Size(1080, 626), BackColor = surface, Padding = Padding.Empty };
                Label title = new Label { Text = "Navisworks Cache Export", Left = 20, Top = 12, Width = 760, Height = 24, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = ink, AutoEllipsis = true };
                Label description = new Label { Text = "Create an NWC from the whole model, selected 3D views, or the current Revit selection.", Left = 20, Top = 38, Width = 820, Height = 20, Font = new Font("Segoe UI", 8f), ForeColor = muted, AutoEllipsis = true };

                RoundedCard outputCard = new RoundedCard { Left = 16, Top = 84, Width = 1048, Height = 108, BackColor = white };
                Label outputHeading = new Label { Text = "Output", Left = 16, Top = 9, Width = 160, Height = 20, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = ink, BackColor = white };
                Label folderLabel = new Label { Text = "Output folder", Left = 16, Top = 33, Width = 102, Height = 22, Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = white, TextAlign = ContentAlignment.MiddleLeft };
                _nwcFolder = new TextBox { Left = 124, Top = 32, Width = 730, Height = 24, Font = new Font("Segoe UI", 8.5f), Text = _lastNwcRequest?.Folder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
                EnableTextClipboard(_nwcFolder);
                Button browse = DashboardEnhancements.CreateSettingsStyleButton("Browse…", DialogResult.None); browse.SetBounds(864, 31, 108, 26);
                Label fileLabel = new Label { Text = "File name", Left = 16, Top = 63, Width = 102, Height = 22, Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = white, TextAlign = ContentAlignment.MiddleLeft };
                string initialNwcName = _lastNwcRequest?.FileNameTemplate;
                if (String.IsNullOrWhiteSpace(initialNwcName)) initialNwcName = String.IsNullOrWhiteSpace(_nwcProjectName) ? "{Project}" : _nwcProjectName;
                _nwcFileName = new TextBox { Left = 124, Top = 62, Width = 810, Height = 24, Font = new Font("Segoe UI", 8.5f), Text = initialNwcName };
                EnableTextClipboard(_nwcFileName);
                _nwcFileName.TextChanged += (s, e) => { if (!_settingNwcProjectName) { _nwcFileNameEdited = true; _lastNwcNameWasUserEdited = true; } };
                Label extension = new Label { Text = ".nwc", Left = 944, Top = 63, Width = 48, Height = 22, Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = white, TextAlign = ContentAlignment.MiddleLeft };
                Label fileNameHelp = new Label { Text = "Tokens: {Project}, {Date}, {ViewName}. Add a prefix or suffix as needed.", Left = 124, Top = 91, Width = 850, Height = 20, Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = white, AutoEllipsis = false, TextAlign = ContentAlignment.MiddleLeft };
                outputCard.Controls.Add(outputHeading); outputCard.Controls.Add(folderLabel); outputCard.Controls.Add(_nwcFolder); outputCard.Controls.Add(browse); outputCard.Controls.Add(fileLabel); outputCard.Controls.Add(_nwcFileName); outputCard.Controls.Add(extension); outputCard.Controls.Add(fileNameHelp);
                browse.Click += (s, e) => { using (FolderBrowserDialog picker = new FolderBrowserDialog { SelectedPath = Directory.Exists(_nwcFolder.Text) ? _nwcFolder.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Description = "Choose the NWC output folder" }) if (picker.ShowDialog(form) == DialogResult.OK) _nwcFolder.Text = picker.SelectedPath; };

                RoundedCard scopeCard = new RoundedCard { Left = 16, Top = 200, Width = 1048, Height = 166, BackColor = white };
                Label scopeHeading = new Label { Text = "Export scope and coordinates", Left = 16, Top = 9, Width = 280, Height = 20, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = ink, BackColor = white };
                Label scopeLabel = new Label { Text = "Scope", Left = 16, Top = 34, Width = 100, Height = 24, Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = white, TextAlign = ContentAlignment.MiddleLeft };
                _nwcScope = new System.Windows.Forms.ComboBox { Left = 124, Top = 33, Width = 228, Height = 25, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8.5f) };
                _nwcScope.Items.AddRange(new object[] { "Entire model", "Selected 3D views", "Current selection" });
                string savedScope = "Entire model"; _nwcScope.SelectedItem = savedScope == "Selected 3D view" ? "Selected 3D views" : savedScope; if (_nwcScope.SelectedIndex < 0) _nwcScope.SelectedIndex = 0;
                Label coordinatesLabel = new Label { Text = "Coordinates", Left = 590, Top = 34, Width = 100, Height = 24, Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = white, TextAlign = ContentAlignment.MiddleLeft };
                _nwcCoordinates = new System.Windows.Forms.ComboBox { Left = 700, Top = 33, Width = 292, Height = 25, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8.5f) };
                _nwcCoordinates.Items.AddRange(new object[] { "Shared coordinates", "Internal coordinates" }); _nwcCoordinates.SelectedItem = _lastNwcRequest?.Coordinates ?? "Shared coordinates"; if (_nwcCoordinates.SelectedIndex < 0) _nwcCoordinates.SelectedIndex = 0;
                Label viewLabel = new Label { Text = "Search views", Left = 16, Top = 66, Width = 102, Height = 24, Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = white, TextAlign = ContentAlignment.MiddleLeft };
                _nwcViewSearch = new TextBox { Left = 124, Top = 65, Width = 868, Height = 24, Font = new Font("Segoe UI", 8f), Enabled = false };
            BIMlify.SearchControls.SearchGlyph.Attach(_nwcViewSearch, "Search 3D views");
                Label viewListLabel = new Label { Text = "3D views", Left = 16, Top = 97, Width = 102, Height = 24, Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = white, TextAlign = ContentAlignment.MiddleLeft };
                _nwcViewPicker = new CheckedListBox { Left = 124, Top = 95, Width = 868, Height = 59, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, BackColor = white, ForeColor = ink, Font = new Font("Segoe UI", 8f), Enabled = false };
                if (_lastNwcRequest?.ViewIds != null) foreach (long id in _lastNwcRequest.ViewIds) _nwcSelectedViewIds.Add(id);
                Action applyViewFilter = () =>
                {
                    if (_nwcViewPicker == null || _nwcViewPicker.IsDisposed) return;
                    string query = (_nwcViewSearch?.Text ?? "").Trim();
                    _nwcViewPicker.BeginUpdate(); _nwcViewPicker.Items.Clear();
                    foreach (NwcViewInfo view in _nwcAvailableViews.Where(v => String.IsNullOrEmpty(query) || v.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                        _nwcViewPicker.Items.Add(view, _nwcSelectedViewIds.Contains(view.Id));
                    _nwcViewPicker.EndUpdate();
                };
                _nwcViewPicker.ItemCheck += (s, e) =>
                {
                    if (e.Index < 0 || e.Index >= _nwcViewPicker.Items.Count) return;
                    NwcViewInfo view = _nwcViewPicker.Items[e.Index] as NwcViewInfo;
                    if (view == null) return;
                    if (e.NewValue == CheckState.Checked) _nwcSelectedViewIds.Add(view.Id); else _nwcSelectedViewIds.Remove(view.Id);
                };
                _nwcViewSearch.TextChanged += (s, e) => applyViewFilter();
                applyViewFilter();
                scopeCard.Controls.Add(scopeHeading); scopeCard.Controls.Add(scopeLabel); scopeCard.Controls.Add(_nwcScope); scopeCard.Controls.Add(coordinatesLabel); scopeCard.Controls.Add(_nwcCoordinates); scopeCard.Controls.Add(viewLabel); scopeCard.Controls.Add(_nwcViewSearch); scopeCard.Controls.Add(viewListLabel); scopeCard.Controls.Add(_nwcViewPicker);
                _nwcScope.SelectedIndexChanged += (s, e) => { bool viewScope = _nwcScope.SelectedIndex == 1; if (_nwcViewPicker != null) _nwcViewPicker.Enabled = viewScope; if (_nwcViewSearch != null) _nwcViewSearch.Enabled = viewScope; if (_nwcHideShaftOpeningLines != null) _nwcHideShaftOpeningLines.Enabled = viewScope; if (_nwcHideMasses != null) _nwcHideMasses.Enabled = viewScope; if (_nwcHideLines != null) _nwcHideLines.Enabled = viewScope; };
                _nwcViewPicker.Enabled = _nwcScope.SelectedIndex == 1; _nwcViewSearch.Enabled = _nwcScope.SelectedIndex == 1;

                RoundedCard dataCard = new RoundedCard { Left = 16, Top = 374, Width = 1048, Height = 204, BackColor = white };
                Label dataHeading = new Label { Text = "Model data and geometry", Left = 16, Top = 9, Width = 300, Height = 20, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = ink, BackColor = white };
                _nwcLinks = CreateNwcCheck("Include linked models in one NWC", _lastNwcRequest?.ExportLinks ?? false);
                _nwcElementIds = CreateNwcCheck("Include Revit element IDs", _lastNwcRequest?.ExportElementIds ?? true);
                _nwcElementProperties = CreateNwcCheck("Include element properties", _lastNwcRequest?.ExportElementProperties ?? true);
                _nwcRooms = CreateNwcCheck("Export room geometry", _lastNwcRequest?.ExportRooms ?? true);
                _nwcDivideLevels = CreateNwcCheck("Divide model by levels", _lastNwcRequest?.DivideByLevels ?? true);
                _nwcLinkedCad = CreateNwcCheck("Convert linked CAD formats", _lastNwcRequest?.ConvertLinkedCad ?? true);
                _nwcParts = CreateNwcCheck("Export Revit parts", _lastNwcRequest?.ExportParts ?? false);
                _nwcRoomAttribute = CreateNwcCheck("Export room as attribute", _lastNwcRequest?.ExportRoomAsAttribute ?? true);
                _nwcFindMaterials = CreateNwcCheck("Find missing materials", _lastNwcRequest?.FindMissingMaterials ?? true);
                _nwcExportUrls = CreateNwcCheck("Export URLs", _lastNwcRequest?.ExportUrls ?? true);
                _nwcLights = CreateNwcCheck("Convert lights", _lastNwcRequest?.ConvertLights ?? false);
                _nwcHideShaftOpeningLines = CreateNwcCheck("Hide shaft openings", _lastNwcRequest?.HideShaftOpeningLines ?? false);
                _nwcHideMasses = CreateNwcCheck("Hide masses", _lastNwcRequest?.HideMasses ?? false);
                _nwcOpenAfterExport = CreateNwcCheck("Open file / folder after export", _lastNwcRequest?.OpenOutputAfterExport ?? false);
                _nwcHideShaftOpeningLines.Enabled = _nwcScope.SelectedIndex == 1;
                _nwcHideMasses.Enabled = _nwcScope.SelectedIndex == 1;
                ToolTip visibilityOptionsTip = new ToolTip { AutoPopDelay = 7000, InitialDelay = 400, ReshowDelay = 100, ShowAlways = true };
                visibilityOptionsTip.SetToolTip(_nwcHideShaftOpeningLines, "Hides shaft opening graphics in temporary copies of the selected 3D views for this export.");
                visibilityOptionsTip.SetToolTip(_nwcHideMasses, "Hides mass elements in temporary copies of the selected 3D views for this export.");
                form.Disposed += (s, e) => visibilityOptionsTip.Dispose();
                _nwcFacetingFactor = new NumericUpDown { DecimalPlaces = 1, Increment = 0.1m, Minimum = 0.1m, Maximum = 10m, Value = (decimal)Math.Max(0.1, Math.Min(10.0, _lastNwcRequest?.FacetingFactor ?? 1.0)), Font = new Font("Segoe UI", 8.5f), BackColor = white, ForeColor = ink, ThousandsSeparator = false };
                Label facetingLabel = new Label { Text = "Faceting factor", Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = white, TextAlign = ContentAlignment.MiddleLeft };
                Label parametersLabel = new Label { Text = "Parameters", Font = new Font("Segoe UI", 8f), ForeColor = muted, BackColor = white, TextAlign = ContentAlignment.MiddleLeft };
                _nwcParameters = new System.Windows.Forms.ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8.5f) };
                _nwcParameters.Items.AddRange(new object[] { "All parameters", "Element parameters", "No parameters" }); _nwcParameters.SelectedItem = _lastNwcRequest?.Parameters ?? "All parameters"; if (_nwcParameters.SelectedIndex < 0) _nwcParameters.SelectedIndex = 0;
                dataCard.Controls.Add(dataHeading); dataCard.Controls.Add(_nwcLinks); dataCard.Controls.Add(_nwcElementIds); dataCard.Controls.Add(_nwcElementProperties); dataCard.Controls.Add(_nwcRooms); dataCard.Controls.Add(_nwcDivideLevels); dataCard.Controls.Add(_nwcLinkedCad); dataCard.Controls.Add(_nwcParts); dataCard.Controls.Add(_nwcRoomAttribute); dataCard.Controls.Add(_nwcFindMaterials); dataCard.Controls.Add(_nwcExportUrls); dataCard.Controls.Add(_nwcLights); dataCard.Controls.Add(_nwcHideShaftOpeningLines); dataCard.Controls.Add(_nwcHideMasses); _nwcHideLines = CreateNwcCheck("Hide lines", _lastNwcRequest?.HideLines ?? false); _nwcHideLines.Enabled = _nwcScope.SelectedIndex == 1; dataCard.Controls.Add(_nwcHideLines); dataCard.Controls.Add(_nwcOpenAfterExport); dataCard.Controls.Add(facetingLabel); dataCard.Controls.Add(_nwcFacetingFactor); dataCard.Controls.Add(parametersLabel); dataCard.Controls.Add(_nwcParameters);

                _nwcReadiness = new Label { Left = 20, Top = 586, Width = 960, Height = 32, Font = new Font("Segoe UI", 8f), ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false, Text = _nwcReadinessMessage };
                Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(12, 0, 12, 0) };
                Panel actions = new Panel { Dock = DockStyle.Right, Width = 306, BackColor = Color.FromArgb(216, 221, 227) };
                Button export = DashboardEnhancements.CreateSettingsStyleButton("Export", DialogResult.None); export.Size = new Size(90, 30); _nwcExportButton = export;
                Button openVg = DashboardEnhancements.CreateSettingsStyleButton("Open VV / VG", DialogResult.None); openVg.Size = new Size(96, 30);
                Button close = DashboardEnhancements.CreateSettingsStyleButton("Close", DialogResult.Cancel); close.Size = new Size(90, 30); close.Click += (s, e) => form.Close();
                actions.Controls.Add(openVg); actions.Controls.Add(export); actions.Controls.Add(close);
                Label footerStatus = new Label { Left = 14, Top = 18, Width = 650, Height = 20, Font = new Font("Segoe UI", 8f), ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Text = "Review the NWC scope and output settings before export." };
                footer.Controls.Add(footerStatus); footer.Controls.Add(actions);
                footerStatus.Anchor = AnchorStyles.Left;
                footerStatus.Top = (footer.Height - footerStatus.Height) / 2;

                export.Click += (s, e) =>
                {
                    if (String.IsNullOrWhiteSpace(_nwcFolder.Text) || !Directory.Exists(_nwcFolder.Text)) { DashboardEnhancements.ShowSettingsStyleMessage(form, "NWC Export", "Choose an existing output folder."); return; }
                    if (String.IsNullOrWhiteSpace(_nwcFileName.Text)) { DashboardEnhancements.ShowSettingsStyleMessage(form, "NWC Export", "Enter a file name or use the {Project}_{Date} template."); return; }
                    List<long> selectedViewIds = _nwcSelectedViewIds.Where(id => _nwcAvailableViews.Any(v => v.Id == id)).Distinct().ToList();
                    if (_nwcScope.SelectedIndex == 1 && selectedViewIds.Count == 0) { DashboardEnhancements.ShowCompactSettingsStyleMessage(form, "NWC Export", "Select at least one 3D view, or change the export scope."); return; }
                    double facetingFactor = Convert.ToDouble(_nwcFacetingFactor.Value, CultureInfo.InvariantCulture);
                    NwcExportRequest request = new NwcExportRequest { Folder = _nwcFolder.Text.Trim(), FileNameTemplate = _nwcFileName.Text.Trim(), Scope = _nwcScope.SelectedIndex == 1 ? "Selected 3D views" : Convert.ToString(_nwcScope.SelectedItem) ?? "Entire model", ViewId = selectedViewIds.FirstOrDefault(), ViewIds = selectedViewIds, Coordinates = Convert.ToString(_nwcCoordinates.SelectedItem) ?? "Shared coordinates", Parameters = Convert.ToString(_nwcParameters.SelectedItem) ?? "All parameters", ExportLinks = _nwcLinks.Checked, ExportElementIds = _nwcElementIds.Checked, ExportRooms = _nwcRooms.Checked, DivideByLevels = _nwcDivideLevels.Checked, ConvertLinkedCad = _nwcLinkedCad.Checked, ExportElementProperties = _nwcElementProperties.Checked, ExportRoomAsAttribute = _nwcRoomAttribute.Checked, FindMissingMaterials = _nwcFindMaterials.Checked, ExportUrls = _nwcExportUrls.Checked, ExportParts = _nwcParts.Checked, ConvertLights = _nwcLights.Checked, HideShaftOpeningLines = _nwcScope.SelectedIndex == 1 && _nwcHideShaftOpeningLines.Checked, HideMasses = _nwcScope.SelectedIndex == 1 && _nwcHideMasses.Checked, HideLines = _nwcScope.SelectedIndex == 1 && _nwcHideLines.Checked, OpenOutputAfterExport = _nwcOpenAfterExport.Checked, FacetingFactor = facetingFactor };
                    _lastNwcRequest = request;
                    NwcRequest = request;
                    _nwcReadiness.Text = "NWC export queued. Revit is preparing the file; this window will remain open.";
                    _nwcReadiness.ForeColor = muted;
                    export.Enabled = false;
                    SetStatus("Starting NWC export in Revit…");
                    int progressTotal = String.Equals(request.Scope, "Selected 3D views", StringComparison.Ordinal) && request.ViewIds != null
                        ? Math.Max(1, request.ViewIds.Distinct().Count()) : 1;
                    Indicator.StartProgress("Waiting for Revit to start NWC export…", progressTotal);
                    try
                    {
                        _handler.RequestNwcExport(request);
                        BimQualitySuite.RaiseEvent();
                    }
                    catch (Exception ex)
                    {
                        Indicator.Stop();
                        export.Enabled = true;
                        _nwcReadiness.Text = "Could not start export: " + ex.Message;
                        SetStatus("Could not start NWC export: " + ex.Message);
                    }
                };
                openVg.Click += (s, e) =>
                {
                    SetStatus("Opening Visibility/Graphics for the active Revit view…");
                    try { _handler.Request("NWC Open Visibility/Graphics"); BimQualitySuite.RaiseEvent(); }
                    catch (Exception ex) { SetStatus("Could not open Visibility/Graphics: " + ex.Message); }
                };

                Action layout = () =>
                {
                    int width = Math.Max(700, page.ClientSize.Width - 32);
                    outputCard.Left = scopeCard.Left = dataCard.Left = 16;
                    outputCard.Width = scopeCard.Width = dataCard.Width = width;
                    const int cardTop = 76, cardGap = 8, readinessHeight = 26, pageBottomPad = 8;
                    int availableCardsHeight = Math.Max(486, page.ClientSize.Height - cardTop - readinessHeight - pageBottomPad - cardGap * 2 - 12);
                    int extraHeight = availableCardsHeight - 486;
                    int outputHeight = 124 + extraHeight / 3;
                    // The scope controls end at y=154, so 164px keeps a clear
                    // inner margin while giving the data card enough room for its
                    // parameter row at the minimum window size.
                    int scopeHeight = 164 + extraHeight / 2;
                    int dataHeight = Math.Max(198, availableCardsHeight - outputHeight - scopeHeight + 6);
                    outputCard.Top = cardTop; outputCard.Height = outputHeight;
                    scopeCard.Top = outputCard.Bottom + cardGap; scopeCard.Height = scopeHeight;
                    dataCard.Top = scopeCard.Bottom + cardGap; dataCard.Height = dataHeight;
                    _nwcReadiness.Top = dataCard.Bottom; _nwcReadiness.Height = Math.Max(0, page.ClientSize.Height - dataCard.Bottom);
                    int right = width - 16;
                    browse.Left = right - browse.Width; _nwcFolder.Width = Math.Max(180, browse.Left - _nwcFolder.Left - 10);
                    _nwcFileName.Width = _nwcFolder.Width; extension.Left = _nwcFileName.Right + 10;
                    fileNameHelp.Width = Math.Max(200, right - fileNameHelp.Left);
                    scopeLabel.Width = 100; coordinatesLabel.Width = scopeLabel.Width; viewLabel.Width = scopeLabel.Width; viewListLabel.Width = scopeLabel.Width;
                    // Widening the right-aligned selector moves its equally sized label left.
                    _nwcCoordinates.Width = Math.Max(340, _nwcScope.Width);
                    _nwcCoordinates.Left = right - _nwcCoordinates.Width; coordinatesLabel.Left = _nwcCoordinates.Left - coordinatesLabel.Width - 8;
                    _nwcViewSearch.Width = _nwcFolder.Width;
                    _nwcViewPicker.Width = Math.Max(220, right - _nwcViewPicker.Left);
                    int columnGap = 12;
                    int columnWidth = Math.Max(150, (right - 16 - 3 * columnGap) / 4);
                    int col1 = 16, col2 = col1 + columnWidth + columnGap, col3 = col2 + columnWidth + columnGap, col4 = col3 + columnWidth + columnGap;
                    int col4Width = Math.Max(140, right - col4);
                    int rowStep = 30;
                    _nwcLinks.SetBounds(col1, 36, columnWidth, 24); _nwcRooms.SetBounds(col1, 36 + rowStep, columnWidth, 24); _nwcParts.SetBounds(col1, 36 + rowStep * 2, columnWidth, 24); _nwcExportUrls.SetBounds(col1, 36 + rowStep * 3, columnWidth, 24);
                    _nwcElementIds.SetBounds(col2, 36, columnWidth, 24); _nwcDivideLevels.SetBounds(col2, 36 + rowStep, columnWidth, 24); _nwcRoomAttribute.SetBounds(col2, 36 + rowStep * 2, columnWidth, 24); _nwcLights.SetBounds(col2, 36 + rowStep * 3, columnWidth, 24);
                    _nwcElementProperties.SetBounds(col3, 36, columnWidth, 24); _nwcElementProperties.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    _nwcLinkedCad.SetBounds(col3, 36 + rowStep, columnWidth, 24); _nwcLinkedCad.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    _nwcFindMaterials.SetBounds(col3, 36 + rowStep * 2, columnWidth, 24); _nwcFindMaterials.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    facetingLabel.SetBounds(col3, 36 + rowStep * 3, 96, 24); _nwcFacetingFactor.SetBounds(col3 + 104, 35 + rowStep * 3, 82, 25);
                    _nwcHideShaftOpeningLines.SetBounds(col4, 36, col4Width, 24); _nwcHideMasses.SetBounds(col4, 36 + rowStep, col4Width, 24); _nwcHideLines.SetBounds(col4, 36 + rowStep * 2, col4Width, 24); _nwcOpenAfterExport.SetBounds(col4, 36 + rowStep * 3, col4Width, 24);
                    int parameterTop = Math.Min(dataCard.Height - 33, 36 + rowStep * 3 + 30);
                    parametersLabel.SetBounds(16, parameterTop + 1, scopeLabel.Width, 24); _nwcParameters.SetBounds(124, parameterTop, Math.Max(220, right - 124), 25);
                    _nwcReadiness.Width = Math.Max(200, page.ClientSize.Width - 40);
                    footerStatus.Width = Math.Max(160, footer.ClientSize.Width - actions.Width - 40);
                    int buttonTop = Math.Max(0, (actions.ClientSize.Height - 30) / 2);
                    close.SetBounds(actions.ClientSize.Width - 96, buttonTop, 90, 30);
                    export.SetBounds(actions.ClientSize.Width - 194, buttonTop, 90, 30);
                    openVg.SetBounds(actions.ClientSize.Width - 296, buttonTop, 96, 30);
                };
                page.SizeChanged += (s, e) => layout(); footer.SizeChanged += (s, e) => layout(); actions.SizeChanged += (s, e) => layout(); form.Shown += (s, e) => layout();
                page.Controls.Add(title); page.Controls.Add(description); page.Controls.Add(outputCard); page.Controls.Add(scopeCard); page.Controls.Add(dataCard); page.Controls.Add(_nwcReadiness);
                DashboardEnhancements.AddSettingsStyleMark(page); form.Controls.Add(page); form.Controls.Add(footer);
                DashboardEnhancements.AddSettingsStyleTitleBar(form); DashboardEnhancements.ApplySettingsStyleWindowCorners(form);
                form.AcceptButton = export; form.CancelButton = close;
                layout();
                StyleExportLabels(page);
                return form;
            }
            private static readonly Font ExportLabelFont = new Font("Segoe UI", 7.8f);

            private static void StyleExportLabels(System.Windows.Forms.Control page)
            {
                var muted = Color.FromArgb(105, 117, 129);
                foreach (var label in page.Controls.OfType<Label>()
                    .Concat(page.Controls.OfType<Panel>().SelectMany(card => card.Controls.OfType<Label>())))
                {
                    if (label.Font.Bold) continue;
                    label.Font = ExportLabelFont;
                    label.ForeColor = muted;
                }
                DashboardEnhancements.ApplyLightGoldDropdowns(page);
            }

            private static GoldCheckBox CreateNwcCheck(string text, bool isChecked)
            {
                return new GoldCheckBox { Text = text, Checked = isChecked, Dock = DockStyle.None, AutoSize = false, Font = new Font("Segoe UI", 7.8f), ForeColor = Color.FromArgb(105, 117, 129), BackColor = Color.White, Margin = new Padding(0), Padding = new Padding(2, 0, 0, 0) };
            }
            private void LoadNwcViews()
            {
                if (_nwcViewsRequested || _handler == null) return;
                _nwcViewsRequested = true;
                _handler.Request("NWC Views Load");
                try { BimQualitySuite.RaiseEvent(); }
                catch { _nwcViewsRequested = false; SetNwcViews(new List<NwcViewInfo>(), "Revit is not ready to load 3D views."); }
            }

            internal void SetNwcViews(List<NwcViewInfo> views, string message = null, string projectName = null)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetNwcViews(views, message, projectName))); return; }
                _nwcAvailableViews = views ?? new List<NwcViewInfo>();
                if (!String.IsNullOrWhiteSpace(projectName)) _nwcProjectName = projectName;
                if (_nwcFileName != null && !_nwcFileName.IsDisposed && !_nwcFileNameEdited)
                {
                    _settingNwcProjectName = true;
                    try { _nwcFileName.Text = String.IsNullOrWhiteSpace(projectName) ? "Project" : projectName; }
                    finally { _settingNwcProjectName = false; }
                }
                _nwcReadinessMessage = !String.IsNullOrWhiteSpace(message) ? message : _nwcAvailableViews.Count == 0 ? "No exportable 3D views were found. Choose Entire model or Current selection instead." : _nwcAvailableViews.Count + " exportable 3D view(s) found. Review the output and coordination settings before exporting.";
                if (_nwcViewPicker != null && !_nwcViewPicker.IsDisposed)
                {
                    foreach (NwcViewInfo view in _nwcViewPicker.CheckedItems.Cast<NwcViewInfo>()) _nwcSelectedViewIds.Add(view.Id);
                    if (_lastNwcRequest?.ViewIds != null) foreach (long id in _lastNwcRequest.ViewIds) _nwcSelectedViewIds.Add(id);
                    string query = (_nwcViewSearch?.Text ?? "").Trim();
                    _nwcViewPicker.BeginUpdate(); _nwcViewPicker.Items.Clear();
                    foreach (NwcViewInfo view in _nwcAvailableViews.Where(v => String.IsNullOrEmpty(query) || v.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)) _nwcViewPicker.Items.Add(view, _nwcSelectedViewIds.Contains(view.Id));
                    _nwcViewPicker.EndUpdate();
                }
                if (_nwcReadiness != null && !_nwcReadiness.IsDisposed) _nwcReadiness.Text = _nwcReadinessMessage;
            }
            private void SizeGridColumnsToContent()
            {
                if (_grid.Columns.Count == 0 || _grid.ClientSize.Width < 100) return;
                bool pdfMode = IsToolSelected("PDF Batch Export");
                int[] minimum = pdfMode ? new[] { 70, 110, 180, 88, 110, 90 } : new[] { 70, 68, 112, 88, 110, 90 };
                int[] maximum = pdfMode ? new[] { 150, 190, 360, 210, 260, 180 } : new[] { 150, 160, 300, 210, 260, 180 };
                for (int columnIndex = 0; columnIndex < _grid.Columns.Count; columnIndex++)
                {
                    DataGridViewColumn column = _grid.Columns[columnIndex];
                    if (!column.Visible) continue;
                    int wanted = TextRenderer.MeasureText(column.HeaderText ?? "", _grid.ColumnHeadersDefaultCellStyle.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 20;
                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        string value = Convert.ToString(row.Cells[columnIndex].FormattedValue) ?? "";
                        if (value.Length == 0) continue;
                        int width = TextRenderer.MeasureText(value, _grid.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 18;
                        if (width > wanted) wanted = width;
                        if (wanted >= maximum[columnIndex]) { wanted = maximum[columnIndex]; break; }
                    }
                    column.MinimumWidth = minimum[columnIndex];
                    column.Width = Math.Max(minimum[columnIndex], Math.Min(maximum[columnIndex], wanted));
                }

                int available = Math.Max(0, _grid.ClientSize.Width - 4);
                if (_grid.Controls.OfType<VScrollBar>().Any(x => x.Visible)) available -= SystemInformation.VerticalScrollBarWidth;
                List<DataGridViewColumn> visible = _grid.Columns.Cast<DataGridViewColumn>().Where(x => x.Visible).ToList();
                int total = visible.Sum(x => x.Width);
                int[] shrinkOrder = { 2, 4, 3, 5, 1, 0 };
                foreach (int i in shrinkOrder)
                {
                    if (!_grid.Columns[i].Visible) continue;
                    if (total <= available) break;
                    int reduce = Math.Min(total - available, _grid.Columns[i].Width - minimum[i]);
                    if (reduce > 0) { _grid.Columns[i].Width -= reduce; total -= reduce; }
                }
                if (total < available)
                {
                    int remaining = available - total;
                    List<DataGridViewColumn> expandable = visible.Where(c => c.Width < maximum[c.Index]).ToList();
                    while (remaining > 0 && expandable.Count > 0)
                    {
                        int share = Math.Max(1, remaining / expandable.Count);
                        for (int j = expandable.Count - 1; j >= 0 && remaining > 0; j--)
                        {
                            DataGridViewColumn column = expandable[j];
                            int add = Math.Min(Math.Min(share, remaining), maximum[column.Index] - column.Width);
                            if (add > 0) { column.Width += add; remaining -= add; }
                            if (column.Width >= maximum[column.Index]) expandable.RemoveAt(j);
                        }
                    }
                }
            }

            private void QueueGridColumnLayout()
            {
                if (_gridLayoutQueued || _grid.IsDisposed || !_grid.IsHandleCreated) return;
                _gridLayoutQueued = true;
                try
                {
                    _grid.BeginInvoke(new Action(() =>
                    {
                        _gridLayoutQueued = false;
                        if (!_grid.IsDisposed) SizeGridColumnsToContent();
                    }));
                }
                catch { _gridLayoutQueued = false; }
            }

            private readonly CheckBox _onlySelected = new CheckBox { Text = "Use current Revit selection", Font = new Font("Segoe UI", 7.8f), AutoSize = true, Left = 344, Top = 8 };
            private readonly CheckBox _includeRevitLinks = new CheckBox
            {
                Text = "Include Revit Links", Font = new Font("Segoe UI", 7.8f), AutoSize = true,
                Left = 520, Top = 8, Visible = false
            };
            internal bool IncludeRevitLinks => _includeRevitLinks.Visible && _includeRevitLinks.Checked;
            internal bool UseRevitSelection => _onlySelected.Visible && _onlySelected.Checked;
            private readonly TextBox _sheetAuditSearch = new TextBox
            {
                AccessibleName = "Search sheets by number or name", Visible = false
            };
            private const string AllSheetFindings = "All findings";
            private const string AllDoorWindowCategories = "Doors and windows";


            private void ConfigureResultFilter(bool isLod, IEnumerable<ReportRow> rows, IEnumerable<string> inventoryCategories = null)
            {
                _stageFilterBar.Visible = !IsToolSelected("PDF Batch Export");
                string audit = _tools.SelectedItem as string;
                bool editing = _auditTabs?.SelectedIndex == 1;
                isLod = isLod && !editing;
                _onlySelected.Visible = ToolNames.Contains(audit) && (editing || audit != "Model Health Score" && !isLod);
                _includeRevitLinks.Visible = ToolNames.Contains(audit);
                _includeRevitLinks.Left = _onlySelected.Visible ? _onlySelected.Left + _onlySelected.PreferredSize.Width + 8 : _onlySelected.Left;
                bool sheetAudit = audit == "Sheet Audit";
                _sheetAuditSearch.Visible = sheetAudit && editing;
                _stageFilter.Visible = !_sheetAuditSearch.Visible;
                bool healthSummary = !editing && audit == "Model Health Score";
                _filterLabel.Text = healthSummary ? "Check status" : sheetAudit ? (editing ? "Search sheets" : "Finding type") :
                    (isLod ? "Filter by stage" : "Filter by category");
                _filterLabel.Width = sheetAudit && editing ? 92 : 116;
                _filterLabel.AccessibleDescription = sheetAudit && editing ? "Search by sheet number or name." : "";
                bool doorWindowAudit = audit == "Door & Window Audit";
                string previous = _stageFilter.SelectedItem as string;
                _updatingFilter = true;
                try
                {
                    _stageFilter.BeginUpdate();
                    _stageFilter.Items.Clear();
                    _stageFilter.Items.Add(sheetAudit || healthSummary ? AllSheetFindings : isLod ? "All stages" : doorWindowAudit ? AllDoorWindowCategories : "All categories");
                    if (sheetAudit)
                    {
                        if (!editing)
                        {
                            var findings = new[] { "Duplicate sheet number", "Expected sheet is missing" }
                                .Concat((rows ?? Enumerable.Empty<ReportRow>()).Select(row => row.Finding))
                                .Where(finding => !String.IsNullOrWhiteSpace(finding))
                                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                            _stageFilter.Items.AddRange(findings);
                        }
                        _stageFilter.SelectedIndex = Math.Max(0, _stageFilter.Items.IndexOf(previous ?? AllSheetFindings));
                        return;
                    }
                    if (healthSummary)
                    {
                        _stageFilter.Items.AddRange(new object[] { "Passed", "Review needed", "Could not run" });
                        _stageFilter.SelectedIndex = Math.Max(0, _stageFilter.Items.IndexOf(previous ?? AllSheetFindings));
                        return;
                    }
                    if (doorWindowAudit)
                    {
                        _stageFilter.Items.Add(LabelUtils.GetLabelFor(BuiltInCategory.OST_Doors));
                        _stageFilter.Items.Add(LabelUtils.GetLabelFor(BuiltInCategory.OST_Windows));
                        _stageFilter.SelectedIndex = Math.Max(0, _stageFilter.Items.IndexOf(previous ?? AllDoorWindowCategories));
                        return;
                    }
                    if (isLod)
                    {
                        _stageFilter.Items.AddRange(new object[] { "Design Development", "Shop Drawings", "As-Built" });
                    }
                    else
                    {
                        var categories = inventoryCategories ?? (rows ?? Enumerable.Empty<ReportRow>()).Select(r => r.Category ?? "");
                        if (audit == "Finishes Checker" || audit == "Room Data Checker")
                            categories = new[] { LabelUtils.GetLabelFor(BuiltInCategory.OST_Rooms) };
                        else if (audit == "Level & Grid Audit")
                            categories = new[] { LabelUtils.GetLabelFor(BuiltInCategory.OST_Levels), LabelUtils.GetLabelFor(BuiltInCategory.OST_Grids) };
                        foreach (string category in categories.Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase))
                            _stageFilter.Items.Add(category);
                    }
                    _stageFilter.SelectedIndex = previous == null ? 0 : Math.Max(0, _stageFilter.Items.IndexOf(previous));
                }
                finally
                {
                    _stageFilter.EndUpdate();
                    _updatingFilter = false;
                }
            }

            private void ApplyStageFilter()
            {
                if (!_stageFilterBar.Visible) return;
                string filter = Convert.ToString(_stageFilter.SelectedItem) ?? "";
                bool editing = _auditTabs?.SelectedIndex == 1;
                var grid = editing ? _inventory : _grid;
                bool isLod = !editing && IsToolSelected("LOD & Deliverables Checker");
                bool sheetAudit = IsToolSelected("Sheet Audit");
                bool healthSummary = !editing && IsToolSelected("Model Health Score");
                string all = sheetAudit || healthSummary ? AllSheetFindings : isLod ? "All stages" : IsToolSelected("Door & Window Audit") ? AllDoorWindowCategories : "All categories";
                string search = _sheetAuditSearch.Text.Trim();
                bool Matches(DataGridViewRow row)
                {
                    if (sheetAudit && editing)
                    {
                        if (search.Length == 0) return true;
                        var entry = row.Tag as InventoryRow;
                        return entry != null && (entry.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                            (entry.SheetNumber ?? "").IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);
                    }
                    return String.Equals(filter, all, StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(Convert.ToString(row.Cells[sheetAudit || healthSummary ? "Finding" : isLod ? "Stage" : "Category"].Value),
                            filter, StringComparison.OrdinalIgnoreCase);
                }
                if (grid.CurrentRow != null && !Matches(grid.CurrentRow)) grid.CurrentCell = null;
                foreach (DataGridViewRow row in grid.Rows) row.Visible = Matches(row);
            }

            private void LayoutFooter()
            {
                Panel footer = _actions.Parent as Panel;
                if (footer == null) return;
                int left = 12;
                _actions.Height = footer.ClientSize.Height;
                _status.SetBounds(left, Math.Max(0, (footer.ClientSize.Height - 30) / 2), Math.Max(0, _actions.Left - left - 12), 30);
                _actions.BringToFront();
            }

            private void ApplyReviewCleanWindowFrame()
            {
                if (Width < 4 || Height < 4) return;
                Region previous = Region;
                using (GraphicsPath path = ReviewCleanFramePath(new Rectangle(0, 0, Width, Height), 14))
                    Region = new Region(path);
                previous?.Dispose();
            }

            private static GraphicsPath ReviewCleanFramePath(Rectangle bounds, int radius)
            {
                GraphicsPath path = new GraphicsPath();
                int diameter = radius * 2;
                // The suite body starts below the shared rounded title bar, so its
                // header edge stays square. Only the footer/base keeps rounded corners.
                path.StartFigure();
                path.AddLine(bounds.Left, bounds.Top, bounds.Right, bounds.Top);
                path.AddLine(bounds.Right, bounds.Top, bounds.Right, bounds.Bottom - radius);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddLine(bounds.Right - radius, bounds.Bottom, bounds.Left + radius, bounds.Bottom);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.AddLine(bounds.Left, bounds.Bottom - radius, bounds.Left, bounds.Top);
                path.CloseFigure();
                return path;
            }

            internal void SelectTool(string name)
            {
                if (String.IsNullOrWhiteSpace(name)) return;
                if (String.Equals(_standaloneTool, "Parameter Mapping", StringComparison.Ordinal) && _smartMapTabs != null)
                {
                    _smartMapTabs.SelectedIndex = String.Equals(name, "Excel ↔ Revit Data Checker", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    return;
                }
                for (int i = 0; i < _tools.Items.Count; i++)
                    if (String.Equals(Convert.ToString(_tools.Items[i]), name, StringComparison.OrdinalIgnoreCase))
                    { _tools.SelectedIndex = i; return; }
            }

            internal bool IsToolSelected(string name)
            {
                return String.Equals(_tools.SelectedItem as string, name, StringComparison.Ordinal);
            }

            private void ToolSelectionChanged()
            {
                if (_smartExportTabs != null && (_tools.SelectedItem as string == "PDF Batch Export" || _tools.SelectedItem as string == "NWC Export"))
                {
                    TabPage desiredPage = String.Equals(_tools.SelectedItem as string, "NWC Export", StringComparison.Ordinal) ? _nwcExportPage : _pdfExportPage;
                    if (desiredPage != null && _smartExportTabs.SelectedTab != desiredPage) _smartExportTabs.SelectedTab = desiredPage;
                    if (desiredPage == _nwcExportPage) LoadNwcViews();
                    ConfigureButtons();
                    return;
                }
                _rows = new List<ReportRow>();
                _rowsForTool = "";
                _inventory?.Rows.Clear();
                _grid.Rows.Clear();
                bool isLod = String.Equals(_tools.SelectedItem as string, "LOD & Deliverables Checker", StringComparison.Ordinal);
                _grid.Columns["Stage"].Visible = isLod;
                ConfigureResultFilter(isLod, _rows);
                QueueGridColumnLayout();
                SetStatus("Ready. Select a check, then Run Check.");
                ConfigureButtons();
                ScheduleAuditLoad();
            }

            private sealed class RoundedCard : Panel
            {
                internal RoundedCard()
                {
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                    Padding = new Padding(1);
                }
                protected override void OnSizeChanged(EventArgs e)
                {
                    base.OnSizeChanged(e);
                    if (Width < 4 || Height < 4) return;
                    Region old = Region;
                    using (GraphicsPath path = RoundedButtonPath(new Rectangle(0, 0, Width - 1, Height - 1), 10)) Region = new Region(path);
                    old?.Dispose();
                }
                protected override void OnPaint(PaintEventArgs e)
                {
                    base.OnPaint(e);
                    if (Width < 4 || Height < 4) return;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = RoundedButtonPath(new Rectangle(0, 0, Width - 1, Height - 1), 10))
                    using (Pen border = new Pen(Color.FromArgb(220, 224, 229), 1f)) e.Graphics.DrawPath(border, path);
                }
            }

            private static Region RoundedButtonRegion(int width, int height, int radius)
            {
                using (GraphicsPath path = RoundedButtonPath(new Rectangle(0, 0, width, height), radius)) return new Region(path);
            }

            private static GraphicsPath RoundedButtonPath(Rectangle r, int radius)
            {
                GraphicsPath path = new GraphicsPath(); int d = radius * 2;
                path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
            }

            private Button MakeButton(string text, int width, bool primary)
            {
                Button button = DashboardEnhancements.CreateSharedStyleButton(text, primary);
                button.Width = Math.Max(96, width); button.Height = 30; button.Margin = new Padding(5, 0, 5, 0); button.Cursor = Cursors.Hand;
                return button;
            }

            private enum SidebarAction { Back, Home, Contact, Settings, Account }

            private sealed class ReviewCleanSidebar : Panel
            {
                private readonly Form _owner;
                private readonly Action _navigateHome;
                private readonly ToolTip _tip = new ToolTip { InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 4500, ShowAlways = true };
                private readonly Color _surface = Color.FromArgb(246, 240, 222);
                private readonly Color _iconColor = Color.FromArgb(86, 94, 101);
                private readonly Color _borderColor = Color.FromArgb(220, 216, 203);
                private SidebarAction? _hovered;

                internal ReviewCleanSidebar(Form owner, Action navigateHome)
                {
                    _owner = owner;
                    _navigateHome = navigateHome;
                    Width = 58; MinimumSize = new Size(58, 0); BackColor = _surface; Padding = Padding.Empty; Margin = Padding.Empty;
                    SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                    Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ToolBar; AccessibleName = "BIMlify navigation";
                    _tip.SetToolTip(this, "BIMlify navigation");
                    Disposed += (s, e) => _tip.Dispose();
                }

                protected override void OnMouseMove(MouseEventArgs e)
                {
                    SidebarAction? action = HitTest(e.Location);
                    if (action != _hovered)
                    {
                        _hovered = action; Invalidate();
                        _tip.SetToolTip(this, action.HasValue ? Description(action.Value) : "BIMlify navigation");
                    }
                    base.OnMouseMove(e);
                }

                protected override void OnMouseLeave(EventArgs e)
                {
                    _hovered = null; Invalidate(); _tip.SetToolTip(this, "BIMlify navigation"); base.OnMouseLeave(e);
                }

                protected override void OnMouseClick(MouseEventArgs e)
                {
                    base.OnMouseClick(e);
                    if (e.Button != MouseButtons.Left) return;
                    SidebarAction? action = HitTest(e.Location);
                    if (!action.HasValue) return;
                    switch (action.Value)
                    {
                        case SidebarAction.Back:
                        case SidebarAction.Home: _navigateHome?.Invoke(); break;
                        case SidebarAction.Contact: DashboardEnhancements.InvokeBimlifyWindowAction("ShowContactOptions", _owner); break;
                        case SidebarAction.Settings: DashboardEnhancements.InvokeBimlifyWindowAction("ShowSettings", _owner); break;
                        case SidebarAction.Account: DashboardEnhancements.InvokeBimlifyWindowAction("ShowAccount", _owner); break;
                    }
                }

                protected override void OnPaintBackground(PaintEventArgs e)
                {
                    e.Graphics.Clear(_surface);
                    using (SolidBrush brush = new SolidBrush(_surface)) e.Graphics.FillRectangle(brush, ClientRectangle);
                    using (Pen edge = new Pen(Color.FromArgb(205, 202, 189), 1f)) e.Graphics.DrawLine(edge, Width - 1, 0, Width - 1, Height);
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    base.OnPaint(e);
                    e.Graphics.Clear(_surface);
                    using (SolidBrush surface = new SolidBrush(_surface)) e.Graphics.FillRectangle(surface, ClientRectangle);
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
                    using (Pen edge = new Pen(Color.FromArgb(205, 202, 189), 1f)) e.Graphics.DrawLine(edge, Width - 1, 0, Width - 1, Height);
                    foreach (SidebarAction action in new[] { SidebarAction.Home, SidebarAction.Contact, SidebarAction.Settings, SidebarAction.Account })
                    {
                        Rectangle bounds = ButtonBounds(action);
                        bool hovered = _hovered.HasValue && _hovered.Value == action;
                        bool primary = action == SidebarAction.Back || action == SidebarAction.Home;
                        using (GraphicsPath path = RoundedButtonPath(bounds, 8))
                        using (SolidBrush fill = new SolidBrush(hovered ? Color.FromArgb(255, 249, 231) : (primary ? Color.FromArgb(255, 252, 242) : _surface)))
                        using (Pen outline = new Pen(hovered ? Color.FromArgb(214, 171, 61) : (primary ? Color.FromArgb(214, 171, 61) : _borderColor), hovered ? 1.15f : (primary ? 0.6f : 0.6f)))
                        {
                            e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(outline, path);
                        }
                        GraphicsState state = e.Graphics.Save();
                        float scale = hovered ? 1.14f : 1f;
                        e.Graphics.TranslateTransform(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
                        e.Graphics.ScaleTransform(scale, scale);
                        e.Graphics.TranslateTransform(-(bounds.Left + bounds.Width / 2f), -(bounds.Top + bounds.Height / 2f));
                        DrawGlyph(e.Graphics, action, bounds);
                        e.Graphics.Restore(state);
                    }
                }

                private string Description(SidebarAction action)
                {
                    switch (action)
                    {
                        case SidebarAction.Back: return "Back to previous window";
                        case SidebarAction.Home: return "Home — return to BIMlify tools";
                        case SidebarAction.Contact: return "Contact BIMlify by email or LinkedIn";
                        case SidebarAction.Settings: return "BIMlify settings";
                        default: return "Account and subscription";
                    }
                }

                private Rectangle ButtonBounds(SidebarAction action)
                {
                    int top = action switch
                    {
                        SidebarAction.Back => 10,
                        SidebarAction.Home => 10,
                        SidebarAction.Settings => Math.Max(68, ClientSize.Height - 112),
                        SidebarAction.Contact => Math.Max(18, Math.Max(68, ClientSize.Height - 112) - 48),
                        _ => Math.Max(112, ClientSize.Height - 62)
                    };
                    return new Rectangle((ClientSize.Width - 42) / 2, top, 42, 42);
                }

                private SidebarAction? HitTest(System.Drawing.Point point)
                {
                    foreach (SidebarAction action in new[] { SidebarAction.Home, SidebarAction.Contact, SidebarAction.Settings, SidebarAction.Account })
                        if (ButtonBounds(action).Contains(point)) return action;
                    return null;
                }

                private void DrawGlyph(Graphics graphics, SidebarAction action, Rectangle bounds)
                {
                    bool primary = action == SidebarAction.Back || action == SidebarAction.Home;
                    using (Pen icon = new Pen(primary ? Color.FromArgb(214, 171, 61) : _iconColor, 2f))
                    {
                        icon.StartCap = icon.EndCap = LineCap.Round;
                        float x = bounds.Left + bounds.Width / 2f, y = bounds.Top + bounds.Height / 2f;
                        switch (action)
                        {
                            case SidebarAction.Back:
                                graphics.DrawLine(icon, x + 2f, y - 8f, x - 6f, y); graphics.DrawLine(icon, x - 6f, y, x + 2f, y + 8f); graphics.DrawLine(icon, x - 6f, y, x + 6f, y); break;
                            case SidebarAction.Home:
                                graphics.DrawLines(icon, new[] { new PointF(x - 10f, y - 1f), new PointF(x, y - 10f), new PointF(x + 10f, y - 1f) });
                                graphics.DrawLine(icon, x - 7f, y - 2f, x - 7f, y + 9f); graphics.DrawLine(icon, x + 7f, y - 2f, x + 7f, y + 9f);
                                graphics.DrawLine(icon, x - 7f, y + 9f, x - 2f, y + 9f); graphics.DrawLine(icon, x + 2f, y + 9f, x + 7f, y + 9f);
                                graphics.DrawLine(icon, x - 2f, y + 9f, x - 2f, y + 3f); graphics.DrawLine(icon, x + 2f, y + 9f, x + 2f, y + 3f); graphics.DrawLine(icon, x - 2f, y + 3f, x + 2f, y + 3f); break;
                            case SidebarAction.Contact:
                                graphics.DrawRectangle(icon, x - 10f, y - 7f, 20f, 14f); graphics.DrawLine(icon, x - 10f, y - 6f, x, y + 1f); graphics.DrawLine(icon, x, y + 1f, x + 10f, y - 6f); break;
                            case SidebarAction.Settings:
                                for (int i = 0; i < 8; i++) { double angle = i * Math.PI / 4.0; graphics.DrawLine(icon, x + (float)Math.Cos(angle) * 8f, y + (float)Math.Sin(angle) * 8f, x + (float)Math.Cos(angle) * 11f, y + (float)Math.Sin(angle) * 11f); }
                                graphics.DrawEllipse(icon, x - 8f, y - 8f, 16f, 16f); graphics.DrawEllipse(icon, x - 3f, y - 3f, 6f, 6f); break;
                            case SidebarAction.Account:
                                graphics.DrawEllipse(icon, x - 4f, y - 10f, 8f, 8f); graphics.DrawArc(icon, x - 10f, y - 1f, 20f, 17f, 190f, 160f); break;
                        }
                    }
                }
            }

            private Button MakeButton(string text) { return MakeButton(text, 86, false); }

            private void ConfigureButtons()
            {
                string t = _tools.SelectedItem as string ?? "";
                bool versionPage = _versionsPage != null && _changeLogTabs.SelectedTab == _versionsPage;
                bool smartTrace = String.Equals(t, "Model Change Log", StringComparison.Ordinal) && IsSmartTraceTab();
                bool collaborators = String.Equals(t, "Model Change Log", StringComparison.Ordinal) && IsCollaboratorsTab();
                bool pdfMode = String.Equals(t, "PDF Batch Export", StringComparison.Ordinal);
                bool nwcMode = String.Equals(t, "NWC Export", StringComparison.Ordinal);
                _grid.ReadOnly = false;
                foreach (DataGridViewColumn column in _grid.Columns)
                    column.ReadOnly = _pdfEditPending || !pdfMode || (column.Name != "Category" && column.Name != "Element");
                _grid.Columns["Id"].HeaderText = pdfMode ? "Sheet ID" : "Revit Element ID";
                _grid.Columns["Category"].HeaderText = pdfMode ? "Sheet Number" : "Category";
                _grid.Columns["Element"].HeaderText = pdfMode ? "Sheet Name" : "Element";
                _toolTitle.Text = t;
                _toolDescription.Text = ToolDescription(t);
                _run.Text = pdfMode ? "Export PDF" : (nwcMode ? "Configure NWC" : (t == "Model Change Log" ? "Compare" : "Run Check"));
                if (t == "Smart Links")
                    _run.Text = _linkAction.SelectedIndex == 1 ? "Reload Link" : _linkAction.SelectedIndex == 2 ? "Choose RVT" : "Refresh Links";
                _run.Width = nwcMode || t == "Smart Links" ? 124 : 96;
                if (_auditTabs?.SelectedIndex == 0 && SupportsAutomaticAudit(t))
                {
                    _run.Text = "Refresh Results";
                    _run.Width = Math.Max(120, TextRenderer.MeasureText(_run.Text, _run.Font).Width + 24);
                }
                _run.Visible = t != "BIM Task Planner" && !smartTrace && !versionPage;
                _import.Visible = _import.Enabled = t == "Sheet Audit" || t == "Parameter Mapping" || t == "Excel ↔ Revit Data Checker";
                if (_auditTabs?.SelectedIndex == 1)
                {
                    _run.Text = "Reload Elements";
                    _run.Width = Math.Max(128, TextRenderer.MeasureText(_run.Text, _run.Font).Width + 24);
                    UpdateInventorySaveState();
                }
                else if (_saveInventory != null) _saveInventory.Visible = false;
                _snapshot.Visible = t == "Model Change Log" && !smartTrace && !versionPage;
                _snapshot.Enabled = _snapshot.Visible && !_collaboratorsLoading;
                _run.Enabled = !_collaboratorsLoading;
                _trace.Enabled = !_collaboratorsLoading;
                _trace.Visible = t == "Model Change Log" && smartTrace;
                _refreshCollaborators.Visible = collaborators;
                _refreshCollaborators.Enabled = collaborators && !_collaboratorsLoading;
                _trace.Text = TraceTracking ? "Stop Trace" : "Start Trace";
                _addTask.Visible = t == "BIM Task Planner";
                _addTask.Enabled = _addTask.Visible && !String.IsNullOrWhiteSpace(_taskProjectKey);
                _deleteTask.Visible = t == "BIM Task Planner";
                _deleteTask.Enabled = _deleteTask.Visible && _grid.SelectedRows.Count > 0;
                _export.Visible = _export.Enabled = _rows.Count > 0 && String.Equals(_rowsForTool, t, StringComparison.Ordinal);
                _locate.Visible = !nwcMode && HasLocatableResults();
                UpdateLocateButton();
                foreach (var button in _versionActions) button.Visible = versionPage;
                int usedWidth = _actions.Controls.OfType<Button>().Where(x => x.Visible).Sum(x => x.Width + x.Margin.Horizontal);
                _actions.Width = usedWidth + _actions.Padding.Horizontal;
                LayoutFooter();
                if (pdfMode && _pdfSheets == null && !_loadingPdfSheets) LoadPdfSheets();
            }

            private List<string> GetSelectedRevitElementIds()
            {
                List<string> ids = new List<string>();
                DataGridView selectedGrid = IsSmartTraceTab() ? _traceGrid : _grid;
                foreach (DataGridViewRow row in selectedGrid.SelectedRows)
                {
                    string value = IsSmartTraceTab()
                        ? Convert.ToString(row.Cells["Trace3"].Value) ?? ""
                        : Convert.ToString(row.Cells["Id"].Value) ?? "";
                    bool isDeleted = IsSmartTraceTab() && String.Equals(Convert.ToString(row.Cells["Trace2"].Value), "Deleted", StringComparison.OrdinalIgnoreCase);
                    if (!isDeleted) ids.AddRange(SplitTraceIds(value));
                }
                return ids.Distinct(StringComparer.Ordinal).ToList();
            }

            private bool IsSmartTraceTab() => _changeLogTabs != null && _changeLogTabs.SelectedTab == _smartTracePage;
            internal bool IsCollaboratorsTab() => _changeLogTabs != null && _changeLogTabs.SelectedTab == _collaboratorsPage;

            private bool _collaboratorsLoaded;
            private bool _collaboratorsLoading;
            private bool _snapshotPreviewRequested;

            private Button[] _versionActions = Array.Empty<Button>();
            private TabPage BuildVersionsPage()
            {
                var page = new TabPage("Compare Versions") { BackColor = Color.White, Padding = new Padding(8) };
                _versionGrid.Dock = DockStyle.Fill;
                _versionGrid.Font = _grid.Font;
                _versionGrid.ColumnHeadersDefaultCellStyle = _grid.ColumnHeadersDefaultCellStyle.Clone();
                _versionGrid.DefaultCellStyle = _grid.DefaultCellStyle.Clone();
                _versionGrid.BackgroundColor = Color.White;
                _versionGrid.EnableHeadersVisualStyles = false;
                _versionGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
                _versionGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                _versionGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                _versionGrid.AllowUserToAddRows = false;
                _versionGrid.RowHeadersVisible = false;
                _versionGrid.ReadOnly = true;
                foreach (string name in new[] { "Change", "Category", "Element", "Details" }) _versionGrid.Columns.Add(name, name);

                var save = MakeButton("Save Version Snapshot", 158, false);
                var compare = MakeButton("Compare Two Snapshots", 174, true);
                var locate = MakeButton("Locate", 96, false);
                _versionActions = new[] { locate, compare, save };
                foreach (var button in _versionActions) { button.Visible = false; _actions.Controls.Add(button); }
                save.Click += (sender, args) =>
                {
                    using (var picker = new SaveFileDialog { Filter = "BIMlify snapshot (*.csv)|*.csv", FileName = "BIMlify-version.csv" })
                        if (picker.ShowDialog(FindForm()) == DialogResult.OK)
                        {
                            _handler.Request("Save Version Snapshot", new List<ReportRow> { new ReportRow("", "", picker.FileName, "", "") });
                            BimQualitySuite.RaiseEvent();
                        }
                };
                compare.Click += (sender, args) =>
                {
                    using (var picker = new OpenFileDialog { Filter = "BIMlify snapshot (*.csv)|*.csv", Title = "Select the earlier snapshot" })
                    {
                        if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
                        string earlier = picker.FileName;
                        picker.Title = "Select the later snapshot";
                        picker.FileName = "";
                        if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
                        _handler.Request("Compare Versions", new List<ReportRow> { new ReportRow("", "", earlier, "", picker.FileName) });
                        BimQualitySuite.RaiseEvent();
                    }
                };
                locate.Click += (sender, args) =>
                {
                    var ids = _versionGrid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Tag as string)
                        .Where(id => !String.IsNullOrWhiteSpace(id)).ToList();
                    if (ids.Count == 0) { SetStatus("Select a result that exists in the open model."); return; }
                    _handler.Request("Locate Version Elements", null, ids);
                    BimQualitySuite.RaiseEvent();
                };
                page.Controls.Add(_versionGrid);
                return page;
            }

            internal void ShowVersionRows(List<ReportRow> rows)
            {
                _versionGrid.Rows.Clear();
                foreach (var row in rows)
                    _versionGrid.Rows[_versionGrid.Rows.Add(row.Finding, row.Category, row.Element, row.Details)].Tag = row.ElementId;
                SetStatus(rows.Count + " changes between snapshots.");
            }

            internal bool IsSnapshotTab() => _changeLogTabs != null && _changeLogTabs.SelectedTab == _smartLogPage;

            internal void PreloadSmartLog()
            {
                RefreshCollaborators(preload: true);
            }

            private void QueueSnapshotPreview()
            {
                if (_collaboratorsLoading || _externalEvent == null || _externalEvent.IsPending) return;
                _snapshotPreviewRequested = true;
                _handler.Request("Smart Log Preview");
                BimQualitySuite.RaiseEvent();
            }

            internal void CollaboratorRefreshFailed(string message)
            {
                _collaboratorsLoading = false;
                if (!_collaboratorsLoaded) _collaboratorsSummary.Text = message;
                if (IsCollaboratorsTab()) SetStatus(message);
                ConfigureButtons();
            }

            private void RefreshCollaborators(bool preload = false)
            {
                if (!IsToolSelected("Model Change Log") || (!preload && !IsCollaboratorsTab()) ||
                    _collaboratorsLoading || (_externalEvent != null && _externalEvent.IsPending)) return;
                _collaboratorsLoading = true;
                if (!_collaboratorsLoaded) _collaboratorsSummary.Text = "Reading Revit worksharing data…";
                if (IsCollaboratorsTab()) SetStatus("Refreshing collaborator information…");
                ConfigureButtons();
                if (_handler == null || _externalEvent == null)
                {
                    SetCollaborators(new List<CollaboratorSummary>(), "Revit's refresh event is unavailable. Close and reopen Smart Log, then try Refresh Users again.");
                    return;
                }
                _handler.Request(preload ? "Smart Log Preload" : "Collaborators Refresh");
                try
                {
                    string raiseResult = _externalEvent.Raise().ToString();
                    if (String.Equals(raiseResult, "Denied", StringComparison.OrdinalIgnoreCase))
                        CollaboratorRefreshFailed("Revit is busy. Use Refresh Users to retry.");
                }
                catch (Exception ex)
                {
                    CollaboratorRefreshFailed("Could not start collaborator refresh: " + ex.Message);
                }
            }

            private void ShowCollaboratorDetails(int rowIndex)
            {
                if (rowIndex < 0 || !(_collaboratorsGrid.Rows[rowIndex].Tag is CollaboratorSummary person)) return;
                ShowCollaboratorDetails(person);
            }

            private void ShowCollaboratorDetails(CollaboratorSummary person)
            {
                Form details = DashboardEnhancements.CreateSettingsStyleDialog();
                    details.Text = "BIMlify — Collaborator Details";
                    details.Size = new Size(690, 480);
                    details.MinimumSize = new Size(550, 340);
                    details.StartPosition = FormStartPosition.CenterParent;
                    details.ShowInTaskbar = false;
                    details.Font = new Font("Segoe UI", 8.5f);
                    var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 10), BackColor = Color.FromArgb(238, 241, 245) };
                    var heading = new Label { Text = person.User, Dock = DockStyle.Top, Height = 30, AutoEllipsis = true,
                        ForeColor = Color.FromArgb(55, 67, 79), Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
                    var elements = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
                    ReportGridStyle.Apply(elements);
                    elements.Columns.Add("Id", "Revit element ID");
                    elements.Columns.Add("ModifiedId", "Modified element ID");
                    elements.Columns.Add("Record", "Worksharing record");
                    elements.Columns.Add("Time", "Last observed in Smart Trace");
                    elements.Columns[3].FillWeight = 135;
                    Action<string, string, bool> add = (id, record, wasModified) =>
                    {
                        _collaboratorTraceTimes.TryGetValue(person.User + "|" + id, out string time);
                        elements.Rows.Add(id, wasModified ? id : "—", record, time ?? "Unavailable");
                    };
                    foreach (string id in person.LastChangedElementIds ?? new List<string>()) add(id, "Last saved by", true);
                    foreach (string id in person.OwnedElementIds ?? new List<string>()) add(id, "Currently owned", false);
                    var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft,
                        Padding = new Padding(12, 12, 12, 12), BackColor = Color.FromArgb(216, 221, 227), WrapContents = false };
                    Button close = DashboardEnhancements.CreateSettingsStyleButton("Close", DialogResult.Cancel);
                    close.Click += (sender, args) => details.Close();
                    Button locate = DashboardEnhancements.CreateSettingsStyleButton("Locate Elements", DialogResult.None);
                    locate.Width = 126;
                    locate.Enabled = elements.SelectedRows.Count > 0;
                    elements.SelectionChanged += (sender, args) => locate.Enabled = elements.SelectedRows.Count > 0;
                    var status = new Label { Dock = DockStyle.Bottom, Height = 34,
                        ForeColor = Color.FromArgb(55, 67, 79), TextAlign = ContentAlignment.MiddleLeft };
                    locate.Click += (sender, args) =>
                    {
                        var ids = elements.SelectedRows.Cast<DataGridViewRow>()
                            .Select(row => Convert.ToString(row.Cells[0].Value)).ToList();
                        if (ids.Count == 0) return;
                        if (_externalEvent == null || _externalEvent.IsPending)
                        {
                            status.Text = "Revit is busy. Try Locate Elements again.";
                            return;
                        }
                        void Report(string message)
                        {
                            if (!details.IsDisposed) status.Text = message;
                        }
                        try
                        {
                            _handler.Request("Locate Collaborator Elements", locateIds: ids, locateFailed: Report);
                            if (_externalEvent.Raise() == ExternalEventRequest.Denied)
                                Report("Revit is busy. Try Locate Elements again.");
                        }
                        catch (Exception error) { Report(error.Message); }
                    };
                    footer.Controls.Add(close);
                    footer.Controls.Add(locate);
                    page.Controls.Add(elements);
                    page.Controls.Add(status);
                    page.Controls.Add(heading);
                    details.Controls.Add(page);
                    details.Controls.Add(footer);
                    details.CancelButton = close;
                    DashboardEnhancements.AddSettingsStyleTitleBar(details);
                    DashboardEnhancements.ApplySettingsStyleWindowCorners(details);
                    details.Show(FindForm());
            }

            private Dictionary<string, string> _collaboratorTraceTimes = new Dictionary<string, string>();
            internal void SetCollaborators(List<CollaboratorSummary> collaborators, string message = null, Dictionary<string, string> traceTimes = null)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetCollaborators(collaborators, message, traceTimes))); return; }
                _collaboratorsLoading = false;
                _collaboratorsLoaded = true;
                ConfigureButtons();
                if (IsSnapshotTab() && !_snapshotPreviewRequested)
                    BeginInvoke(new Action(() => { if (!IsDisposed && IsSnapshotTab()) QueueSnapshotPreview(); }));
                _collaboratorTraceTimes = traceTimes ?? new Dictionary<string, string>();
                _collaboratorsGrid.Rows.Clear();
                foreach (CollaboratorSummary person in collaborators ?? new List<CollaboratorSummary>())
                {
                    List<string> changedIds = person.LastChangedElementIds ?? new List<string>();
                    string changedSummary = person.HasElementRecords
                        ? person.LastChangedElements.ToString(CultureInfo.InvariantCulture) + (changedIds.Count > 0 ? " · " + String.Join(", ", changedIds.Take(2)) + (changedIds.Count > 2 ? ", …" : "") : "")
                        : "—";
                    int rowIndex = _collaboratorsGrid.Rows.Add(person.User, person.HasElementRecords ? (object)person.OwnedElements : "—", changedSummary);
                    DataGridViewRow row = _collaboratorsGrid.Rows[rowIndex];
                    row.Tag = person;
                    row.Cells["UserDetails"].Value = "…";
                    row.Cells["UserDetails"].Style.BackColor = Color.FromArgb(250, 230, 168);
                    row.Cells["UserDetails"].Style.SelectionBackColor = Color.FromArgb(250, 230, 168);
                    row.Cells["UserDetails"].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    row.Cells["UserDetails"].ToolTipText = "Open element details for " + person.User;
                    row.Cells["OwnedElements"].ToolTipText = person.OwnedElementIds != null && person.OwnedElementIds.Count > 0
                        ? "Currently owned element IDs: " + String.Join(", ", person.OwnedElementIds)
                        : "No elements are currently reported as owned by this user.";
                    row.Cells["LastChanged"].ToolTipText = changedIds.Count > 0
                        ? "Elements whose Revit LastChangedBy record names this user: " + String.Join(", ", changedIds)
                        : "Revit returned no element IDs with this user as LastChangedBy.";
                }
                if (!String.IsNullOrWhiteSpace(message))
                {
                    _collaboratorsSummary.Text = message;
                    SetStatus(message);
                    return;
                }
                int activeOwners = (collaborators ?? new List<CollaboratorSummary>()).Count(person => person.OwnedElements > 0);
                int lastEditors = (collaborators ?? new List<CollaboratorSummary>()).Count(person => person.LastChangedElements > 0);
                _collaboratorsSummary.Text = "Snapshot read " + DateTime.Now.ToString("dd MMM yyyy, HH:mm", CultureInfo.CurrentCulture) + " · " + activeOwners.ToString(CultureInfo.InvariantCulture) + " current element owners · " + lastEditors.ToString(CultureInfo.InvariantCulture) + " users recorded as last saver. Revit does not provide per-element save timestamps here; ACC activity history is not connected. Click … beside a user to view element details.";
                if (IsCollaboratorsTab()) SetStatus("Collaborator snapshot refreshed from Revit worksharing data.");
            }

            private void ToggleTrace()
            {
                if (!IsToolSelected("Model Change Log")) return;
                _handler.Request("Smart Trace Toggle");
                SetStatus(TraceTracking ? "Stopping Smart Trace…" : "Starting Smart Trace for this Revit session…");
                BimQualitySuite.RaiseEvent();
            }

            internal void SetTraceTracking(bool tracking)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetTraceTracking(tracking))); return; }
                if (_trace != null && !_trace.IsDisposed) _trace.Text = tracking ? "Stop Trace" : "Start Trace";
                ConfigureButtons();
            }

            internal void SetTraceRows(List<TraceRecord> rows)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetTraceRows(rows))); return; }
                _traceGrid.Rows.Clear();
                foreach (TraceRecord row in rows ?? new List<TraceRecord>())
                {
                    int index = _traceGrid.Rows.Add(row.Time, row.User, row.Change, row.ElementId, row.Category, row.Element, row.Transaction);
                    _traceGrid.Rows[index].Tag = row;
                }
                UpdateLocateButton();
            }

            private TabControl _auditTabs;
            private DataGridView _inventory;
            private Label _auditLoading;

            private void AddInventoryTabs(Panel host)
            {
                _auditTabs = CreateSmartExportTabs();
                _auditTabs.ItemSize = new Size(170, 30);
                var findings = new TabPage("Check Results") { BackColor = Color.White };
                var elements = new TabPage("Edit Project Elements") { BackColor = Color.White };
                _inventory = new DataGridView { Dock = DockStyle.Fill, RowHeadersVisible = false,
                    AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
                ReportGridStyle.Apply(_inventory);
                _inventory.BorderStyle = BorderStyle.None;
                foreach (string column in new[] { "Element ID", "Category", "Element", "Field", "Value (edit)" })
                    _inventory.Columns.Add(column, column);
                for (int index = 0; index < 4; index++) _inventory.Columns[index].ReadOnly = true;
                _inventory.CellEndEdit += (sender, args) =>
                {
                    if (args.RowIndex < 0 || DashboardEnhancements.IsCancellingGridEdit(_inventory)) return;
                    var row = _inventory.Rows[args.RowIndex];
                    if (args.ColumnIndex != 4 || !(row.Tag is InventoryRow data) || data.Field == null) return;
                    string value = Convert.ToString(row.Cells[4].Value) ?? "";
                    string key = (data.UniqueId ?? "") + "|" + (data.Field ?? "");
                    if (String.Equals(value, data.Value ?? "", StringComparison.Ordinal))
                    {
                        _inventoryChanges.Remove(key);
                        row.Cells[4].Style.BackColor = Color.FromArgb(255, 248, 226);
                    }
                    else
                    {
                        _inventoryChanges[key] = new InventoryEdit { Row = data, Value = value };
                        row.Cells[4].Style.BackColor = Color.FromArgb(255, 239, 196);
                    }
                    UpdateInventorySaveState();
                };
                findings.Controls.Add(_grid);
                elements.Controls.Add(_inventory);
                _auditTabs.TabPages.Add(findings);
                _auditTabs.TabPages.Add(elements);
                host.Controls.Add(_auditTabs);
                _auditLoading = new Label
                {
                    Visible = false, Size = new Size(210, 22), TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.White, ForeColor = Color.FromArgb(105, 117, 129),
                    Font = new Font("Segoe UI", 7.5f), BorderStyle = BorderStyle.None
                };
                host.Controls.Add(_auditLoading);
                host.Resize += (sender, args) =>
                {
                    _auditLoading.Left = Math.Max(0, (host.ClientSize.Width - _auditLoading.Width) / 2);
                    _auditLoading.Top = Math.Max(0, (host.ClientSize.Height - _auditLoading.Height) / 2);
                };
                _onlySelected.CheckedChanged += (sender, args) =>
                {
                    ScheduleAuditLoad();
                };
                _auditTabs.SelectedIndexChanged += (sender, args) =>
                {
                    bool editing = _auditTabs.SelectedIndex == 1;
                    ConfigureResultFilter(IsToolSelected("LOD & Deliverables Checker"), _rows,
                        editing ? Enumerable.Empty<string>() : null);
                    ConfigureButtons();
                    ScheduleAuditLoad();
                };
            }

            private static bool SupportsAutomaticAudit(string tool) => ToolNames.Contains(tool) &&
                tool != "Project Standards" && tool != "LOD & Deliverables Checker";

            private System.Windows.Forms.Timer _auditLoadTimer;

            internal void ScheduleAuditLoad()
            {
                if (_auditTabs == null || IsDisposed) return;
                if (_auditLoadTimer == null)
                {
                    _auditLoadTimer = new System.Windows.Forms.Timer { Interval = 100 };
                    Disposed += (sender, args) => _auditLoadTimer.Dispose();
                    _auditLoadTimer.Tick += (sender, args) =>
                    {
                        if (!Visible) { _auditLoadTimer.Stop(); return; }
                        if (_externalEvent == null || _externalEvent.IsPending) return;
                        _auditLoadTimer.Stop();
                        if (_auditTabs.SelectedIndex == 1)
                        {
                            QueueInventory();
                            return;
                        }
                        string tool = _tools.SelectedItem as string;
                        if (SupportsAutomaticAudit(tool)) RunSelected();
                    };
                }
                _auditLoadTimer.Stop();
                _auditLoadTimer.Start();
            }

            private void QueueInventory()
            {
                if (_externalEvent == null || _externalEvent.IsPending)
                {
                    SetStatus("Revit is busy. Try again shortly.");
                    return;
                }
                _handler.RequestInventory(_tools.SelectedItem as string);
                SetStatus("Loading project elements…");
                BimQualitySuite.RaiseEvent();
            }

            private void SaveInventoryChanges()
            {
                if (_inventoryChanges.Count == 0) { SetStatus("No edited values to save."); return; }
                if (_externalEvent == null || _externalEvent.IsPending)
                {
                    SetStatus("Revit is busy. Try again shortly.");
                    return;
                }
                _handler.RequestInventory(_tools.SelectedItem as string, _inventoryChanges.Values);
                _saveInventory.Enabled = false;
                SetStatus("Saving " + _inventoryChanges.Count + " change(s) in Revit…");
                BimQualitySuite.RaiseEvent();
            }

            private void UpdateInventorySaveState()
            {
                if (_saveInventory == null || _saveInventory.IsDisposed) return;
                _saveInventory.Visible = _auditTabs?.SelectedIndex == 1;
                _saveInventory.Enabled = _inventoryChanges.Count > 0;
                _saveInventory.Text = _inventoryChanges.Count > 0
                    ? "Save Changes (" + _inventoryChanges.Count + ")"
                    : "Save Changes";
                _saveInventory.Width = Math.Max(112, TextRenderer.MeasureText(_saveInventory.Text, _saveInventory.Font).Width + 24);
            }

            internal void ShowInventory(List<InventoryRow> rows)
            {
                if (_auditTabs?.SelectedIndex != 1) return;
                _inventory.SuspendLayout();
                try
                {
                    _inventory.Rows.Clear();
                    _inventoryChanges.Clear();
                    foreach (var row in rows)
                    {
                        int index = _inventory.Rows.Add(row.Id, row.Category, row.Name, row.Field, row.Value);
                        var gridRow = _inventory.Rows[index];
                        gridRow.Tag = row;
                        gridRow.Cells[4].ReadOnly = row.Field == null;
                        if (row.Field != null)
                            gridRow.Cells[4].Style.BackColor = Color.FromArgb(255, 248, 226);
                    }
                }
                finally { _inventory.ResumeLayout(); }
                ConfigureResultFilter(false, null, rows.Select(row => row.Category));
                ApplyStageFilter();
                UpdateInventorySaveState();
                SetStatus(rows.Count == 0 && UseRevitSelection
                    ? "No matching elements in the current Revit selection."
                    : rows.Count + " row(s). Edit highlighted values, then click Save Changes.");
            }

            private void UpdateLocateButton()
            {
                if (_locate == null || _locate.IsDisposed) return;
                bool hasLocatableResults = HasLocatableResults();
                _locate.Visible = hasLocatableResults && _auditTabs?.SelectedIndex != 1;
                _locate.Enabled = hasLocatableResults && GetSelectedRevitElementIds().Count > 0;

                int usedWidth = _actions.Controls.OfType<Button>().Where(x => x.Visible).Sum(x => x.Width + x.Margin.Horizontal);
                _actions.Width = usedWidth + _actions.Padding.Horizontal;
                LayoutFooter();
            }

            private void UpdateDeleteTaskButton()
            {
                if (_deleteTask == null || _deleteTask.IsDisposed) return;
                bool isTaskPlanner = IsToolSelected("BIM Task Planner");
                _deleteTask.Visible = isTaskPlanner;
                _deleteTask.Enabled = isTaskPlanner && _grid.SelectedRows.Count > 0;

                int usedWidth = _actions.Controls.OfType<Button>().Where(x => x.Visible).Sum(x => x.Width + x.Margin.Horizontal);
                _actions.Width = usedWidth + _actions.Padding.Horizontal;
                LayoutFooter();
            }

            private bool HasLocatableResults()
            {
                string tool = _tools == null ? "" : (_tools.SelectedItem as string ?? "");
                if (String.Equals(tool, "Model Change Log", StringComparison.Ordinal) && IsSmartTraceTab())
                    return _traceGrid.Rows.Cast<DataGridViewRow>().Any(row => row.Cells.Count > 3 && !String.Equals(Convert.ToString(row.Cells["Trace2"].Value), "Deleted", StringComparison.OrdinalIgnoreCase) && SplitTraceIds(Convert.ToString(row.Cells["Trace3"].Value)).Count > 0);
                if (String.IsNullOrWhiteSpace(tool) || !String.Equals(_rowsForTool, tool, StringComparison.Ordinal)) return false;
                if (tool == "BIM Task Planner" || tool == "Model Health Score" || tool == "PDF Batch Export" || tool == "Revision Manager" || tool == "Sheet Audit") return false;
                return (_rows ?? new List<ReportRow>()).Any(row =>
                {
                    long id;
                    return row != null && !String.IsNullOrWhiteSpace(row.Category) &&
                        Int64.TryParse(row.ElementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0;
                });
            }

            private void LocateSelectedElements()
            {
                List<string> ids = GetSelectedRevitElementIds();
                if (ids.Count == 0) { SetStatus("Select a result row with a Revit Element ID first. Deleted elements cannot be located."); return; }
                _handler.Request("Locate Selected Elements", null, ids);
                SetStatus("Locating selected Revit element(s)…");
                BimQualitySuite.RaiseEvent();
            }

            internal void RefreshTraceRows(List<TraceRecord> rows) => SetTraceRows(rows);

            private void LoadPdfSheets()
            {
                if (_loadingPdfSheets) return;
                _loadingPdfSheets = true;
                SetStatus("Loading project sheets…");
                _handler.Request("PDF Batch Load");
                BimQualitySuite.RaiseEvent();
            }

            internal void SetPdfSheets(List<PdfSheetInfo> sheets)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetPdfSheets(sheets))); return; }
                _pdfSheets = sheets ?? new List<PdfSheetInfo>();
                _loadingPdfSheets = false;
                _pdfEditPending = false;
                if (IsToolSelected("PDF Batch Export"))
                {
                    _rowsForTool = "PDF Batch Export";
                    _rows = _pdfSheets.Select(sheet => new ReportRow(
                        sheet.Id.ToString(CultureInfo.InvariantCulture), sheet.Number,
                        sheet.Name, "Ready to export", "Printable project sheet")).ToList();
                    _grid.Rows.Clear();
                    _grid.Columns["Stage"].Visible = false;
                    foreach (ReportRow row in _rows)
                        _grid.Rows.Add(row.ElementId, row.Category, row.Element, row.Finding, row.Details, row.Stage);
                    ConfigureResultFilter(false, _rows);
                    QueueGridColumnLayout();
                    SetStatus(_pdfSheets.Count == 0 ? "No printable sheets were found." : _pdfSheets.Count + " project sheet(s) ready. Select Export PDF to choose a batch.");
                    ConfigureButtons();
                }
            }

            private void SavePdfSheetEdit(DataGridViewCellEventArgs e)
            {
                if (e == null || e.RowIndex < 0 || e.ColumnIndex < 0 || _pdfEditPending || !IsToolSelected("PDF Batch Export")) return;
                string column = _grid.Columns[e.ColumnIndex].Name;
                if (column != "Category" && column != "Element") return;
                DataGridViewRow row = _grid.Rows[e.RowIndex];
                long id;
                if (!Int64.TryParse(Convert.ToString(row.Cells["Id"].Value), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return;
                string number = Convert.ToString(row.Cells["Category"].Value)?.Trim() ?? "";
                string name = Convert.ToString(row.Cells["Element"].Value)?.Trim() ?? "";
                PdfSheetInfo original = _pdfSheets?.FirstOrDefault(sheet => sheet.Id == id);
                if (original == null) return;
                if (String.Equals(number, original.Number, StringComparison.Ordinal) && String.Equals(name, original.Name, StringComparison.Ordinal)) return;
                if (String.IsNullOrWhiteSpace(number) || String.IsNullOrWhiteSpace(name))
                {
                    SetPdfSheets(_pdfSheets);
                    SetStatus("Sheet number and sheet name cannot be empty.");
                    return;
                }
                _pdfEditPending = true;
                ConfigureButtons();
                SetStatus("Saving sheet number and name to the Revit model…");
                _handler.RequestPdfSheetEdit(id, number, name);
                BimQualitySuite.RaiseEvent();
            }

            internal void SetPdfLoadError(string message)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetPdfLoadError(message))); return; }
                _loadingPdfSheets = false;
                if (IsToolSelected("PDF Batch Export"))
                    SetStatus("Could not load project sheets: " + (message ?? "Unknown Revit error"));
            }

            private static string ToolDescription(string tool)
            {
                switch (tool)
                {
                    case "Workset Checker": return "Review worksets and inspect the elements assigned to each one.";
                    case "Sheet Audit": return "Find duplicate or empty sheets; optionally compare a required sheet list.";
                    case "Revision Manager": return "Check revision numbers and dates for blanks, duplicates and invalid dates.";
                    case "PDF Batch Export": return "Export project sheets to one PDF using a project and date naming template.";
                    case "NWC Export": return "Export a Navisworks Cache with controlled scope, coordinates, links and model properties.";
                    case "Parameter Audit": return "Find blank instance parameter values in the current model.";
                    case "Naming Convention Checker": return "Review names of views, families, types, levels and grids for inconsistent spacing.";
                    case "Model Health Score": return "Summarize pass/fail results across the active model quality checks.";
                    case "Room Data Checker": return "Find rooms missing a number, name or department.";
                    case "Door & Window Audit": return "Review door and window marks, types and dimensions.";
                    case "Finishes Checker": return "Find rooms with unassigned wall, floor, ceiling or base finishes.";
                    case "Level & Grid Audit": return "Find inconsistent level or grid names and levels placed at nearly identical elevations.";
                    case "Opening Checker": return "Check placed opening elements for a valid host; Smart Openings remains the creation tool.";
                    case "Model Change Log": return "Review captured Revit changes or compare saved model snapshots.";
                    case "LOD & Deliverables Checker": return "Track project-stage deliverables in a saved checklist.";
                    case "Parameter Mapping": return "Compare two parameter names using a SourceParameter, TargetParameter CSV.";
                    case "Excel ↔ Revit Data Checker": return "Compare CSV values with matching Revit elements using Revit Element ID, Parameter and Value columns.";
                    case "BIM Task Planner": return "Add and review locally saved BIM tasks and due dates.";
                    default: return "Choose a check and run it against the active Revit project.";
                }
            }

            private static string HeaderDescription(string tool)
            {
                if (tool == "Smart Links") return "Review Revit links, reload a selected link, or choose a new RVT path.";
                if (String.Equals(tool, "Model Change Log", StringComparison.Ordinal))
                    return "Review Revit session changes or compare saved model snapshots.";
                if (String.Equals(tool, "Parameter Mapping", StringComparison.Ordinal))
                    return "Compare Revit parameter names and values with imported CSV data.";
                if (String.Equals(tool, "PDF Batch Export", StringComparison.Ordinal))
                    return "Export PDF sheet sets or Navisworks Cache coordination models.";
                return ToolDescription(tool);
            }

            private readonly System.Windows.Forms.ComboBox _linkAction = new System.Windows.Forms.ComboBox();
            private readonly Label _linkActionLabel = new Label();

            private readonly System.Windows.Forms.ComboBox _quantityGrouping = new System.Windows.Forms.ComboBox();

            internal void PreloadLinks()
            {
                _linkAction.SelectedIndex = 0;
                if (_stageFilter.Items.Count > 0) _stageFilter.SelectedIndex = 0;
                _handler.Request("Smart Links");
                SetStatus("Loading Revit links…");
                BimQualitySuite.RaiseEvent();
            }

            private List<ReportRow> ReadLinkAction(Form owner)
            {
                if (_linkAction.SelectedIndex <= 0) return new List<ReportRow>();
                var ids = GetSelectedRevitElementIds();
                if (ids.Count != 1) { SetStatus("Select one link row first."); return null; }
                var selectedLink = _rows.FirstOrDefault(row => row.ElementId == ids[0]);
                if (selectedLink == null) return null;
                string path = "";
                if (_linkAction.SelectedIndex == 2)
                {
                    using (var picker = new OpenFileDialog { Filter = "Revit project (*.rvt)|*.rvt", CheckFileExists = true })
                    {
                        if (picker.ShowDialog(owner) != DialogResult.OK) return null;
                        path = picker.FileName;
                    }
                }
                return new List<ReportRow> { new ReportRow(ids[0], "", "", "", path, selectedLink.Stage) };
            }

            private void RunSelected()
            {
                _auditLoadTimer?.Stop();
                string tool = _tools.SelectedItem as string;
                if (String.IsNullOrEmpty(tool)) return;
                if (_auditTabs?.SelectedIndex == 1) { QueueInventory(); return; }
                if (tool == "NWC Export")
                {
                    if (_nwcOptionsForm != null && !_nwcOptionsForm.IsDisposed)
                    {
                        _nwcOptionsForm.Activate();
                        return;
                    }
                    LoadNwcViews();
                    Form dialog = NwcExportOptionsDialog();
                    _nwcOptionsForm = dialog;
                    dialog.FormClosed += (s, e) => { if (ReferenceEquals(_nwcOptionsForm, dialog)) _nwcOptionsForm = null; };
                    dialog.Show(FindForm());
                    return;
                }
                if (tool == "PDF Batch Export")
                {
                    if (_pdfSheets == null) { LoadPdfSheets(); return; }
                    if (_pdfOptionsForm != null && !_pdfOptionsForm.IsDisposed)
                    {
                        _pdfOptionsForm.Activate();
                        return;
                    }
                    Form dialog = PdfExportOptionsDialog(_pdfSheets, (request, progress) =>
                    {
                        if (request == null || request.SheetIds == null || request.SheetIds.Count == 0) return;
                        PdfRequest = request;
                        _pdfExportProgress = progress;
                        string message = request.ExportDwg ? "Exporting selected sheets to PDF and DWG…" : "Exporting selected sheets to PDF…";
                        progress?.Invoke(message, true);
                        SetStatus(message);
                        _handler.Request(tool);
                        BimQualitySuite.RaiseEvent();
                    });
                    _pdfOptionsForm = dialog;
                    dialog.FormClosed += (s, e) =>
                    {
                        if (ReferenceEquals(_pdfOptionsForm, dialog)) _pdfOptionsForm = null;
                        _pdfExportProgress = null;
                    };
                    dialog.Show(FindForm());
                    return;
                }
                if (tool == "BIM Task Planner") { ShowTasks(); return; }
                if (tool == "Smart Links" || tool == "Quantity Reports")
                {
                    var options = tool == "Smart Links" ? ReadLinkAction(FindForm()) :
                        new List<ReportRow> { new ReportRow("", "", "", "", _quantityGrouping.Text) };
                    if (options == null) return;
                    _handler.Request(tool, options);
                    SetStatus("Running " + tool + "…");
                    BimQualitySuite.RaiseEvent();
                    return;
                }
                if (tool == "Project Standards")
                {
                    var rule = ReadProjectStandard(FindForm());
                    if (rule == null) return;
                    _handler.Request(tool, new List<ReportRow> { rule });
                    SetStatus("Checking project standards…");
                    BimQualitySuite.RaiseEvent();
                    return;
                }
                if (tool == "LOD & Deliverables Checker")
                {
                    List<string> selectedRules = ShowChecklist();
                    if (selectedRules == null) return;
                    if (selectedRules.Count == 0) { SetStatus("Select at least one deliverable check to run."); return; }
                    List<ReportRow> rules = selectedRules.Select(x => new ReportRow("", x, "", "Selected delivery check", "", _lodStage)).ToList();
                    _handler.Request(tool, rules);
                    SetStatus("Checking selected delivery requirements…");
                    BimQualitySuite.RaiseEvent();
                    return;
                }

                bool requiresInput = tool == "Parameter Mapping" || tool == "Excel ↔ Revit Data Checker";
                bool hasMatchingInput = _inputForTool == tool && _inputRows.Count > 0;
                if (requiresInput && !hasMatchingInput)
                {
                    SetStatus(tool == "Parameter Mapping"
                        ? "Import a CSV with SourceParameter and TargetParameter columns first."
                        : "Import a CSV with ElementId, Parameter and Value columns first.");
                    return;
                }
                List<ReportRow> input = hasMatchingInput && (requiresInput || tool == "Sheet Audit")
                    ? _inputRows.ToList() : new List<ReportRow>();
                _handler.Request(tool, input);
                SetStatus("Running " + tool + "…");
                BimQualitySuite.RaiseEvent();
            }

            internal void ShowRows(string title,List<ReportRow> rows)
            {
                if(IsDisposed)return; if(InvokeRequired){BeginInvoke(new Action(()=>ShowRows(title,rows)));return;}
                if (!IsToolSelected(title)) return;
                _rowsForTool = title ?? "";
                _rows=rows??new List<ReportRow>(); bool isLod = String.Equals(_rowsForTool, "LOD & Deliverables Checker", StringComparison.Ordinal); _grid.Columns["Stage"].Visible = isLod; _grid.Rows.Clear(); foreach(ReportRow r in _rows) { int rowIndex = _grid.Rows.Add(r.ElementId,r.Category,r.Element,r.Finding,r.Details,r.Stage); _grid.Rows[rowIndex].Cells["Details"].ToolTipText = r.Details ?? ""; } ConfigureResultFilter(isLod, _rows); ApplyStageFilter(); SizeGridColumnsToContent();
                SetStatus(title + ": " + _rows.Count + " finding(s)." + (ToolNames.Contains(title) ? " Open Edit Project Elements to edit values." : "")); ConfigureButtons();
            }
            internal void SetStatus(string text)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text))); return; }
                _status.Text = text;
                _status.ForeColor = Color.FromArgb(105, 117, 129);
                if (_auditLoading == null) return;
                bool loading = text == "Loading project elements…" || text.StartsWith("Checking ", StringComparison.Ordinal) ||
                    text.StartsWith("Running ", StringComparison.Ordinal);
                if (!loading) { _auditLoading.Hide(); return; }
                _auditLoading.Text = text == "Loading project elements…" ? "Loading project elements…" : "Scanning project…";
                var host = _auditLoading.Parent;
                _auditLoading.Location = new System.Drawing.Point(
                    Math.Max(0, (host.ClientSize.Width - _auditLoading.Width) / 2),
                    Math.Max(0, (host.ClientSize.Height - _auditLoading.Height) / 2));
                _auditLoading.BringToFront();
                _auditLoading.Show();
                _auditLoading.Update();
            }
            internal void SetStatusForTool(string tool, string text)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetStatusForTool(tool, text))); return; }
                if (IsToolSelected(tool)) SetStatus(text);
                if (String.Equals(tool, "NWC Export", StringComparison.Ordinal))
                {
                    if (_nwcReadiness != null && !_nwcReadiness.IsDisposed) _nwcReadiness.Text = text;
                    if (_nwcExportButton != null && !_nwcExportButton.IsDisposed) _nwcExportButton.Enabled = true;
                }
            }
            internal void SetNwcExportSucceeded(string text)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetNwcExportSucceeded(text))); return; }
                if (IsToolSelected("NWC Export")) _status.Text = text;
                if (_nwcReadiness != null && !_nwcReadiness.IsDisposed)
                {
                    _nwcReadiness.Text = text;
                    _nwcReadiness.ForeColor = Color.FromArgb(39, 125, 72);
                }
                if (_nwcExportButton != null && !_nwcExportButton.IsDisposed) _nwcExportButton.Enabled = true;
            }
            internal void SetPdfExportSucceeded(string text)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetPdfExportSucceeded(text))); return; }
                if (IsToolSelected("PDF Batch Export"))
                {
                    _status.Text = text;
                    _status.ForeColor = Color.FromArgb(39, 125, 72);
                }
                _pdfExportProgress?.Invoke(text, false);
            }

            internal void SetPdfExportFailed(string text)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetPdfExportFailed(text))); return; }
                if (IsToolSelected("PDF Batch Export")) SetStatus(text);
                _pdfExportProgress?.Invoke(text, false);
            }
            internal void KeepVisibleBehindRevit()
            {
                if (IsDisposed) return;
                if (InvokeRequired) { try { BeginInvoke(new Action(KeepVisibleBehindRevit)); } catch { } return; }
                if (!Visible) Show();
                SendToBack();
            }
            private void ExportCsv()
            {
                if(_rows.Count==0){SetStatus("There are no results to export.");return;}
                using(SaveFileDialog d=new SaveFileDialog { Filter="CSV files (*.csv)|*.csv",FileName="BIMlify_QAQC.csv" }) if(d.ShowDialog(this)==DialogResult.OK)
                { var lines=new List<string>{"ElementId,Category,Element,Finding,Details,Stage"}; lines.AddRange(_rows.Select(r=>String.Join(",",new[]{r.ElementId,r.Category,r.Element,r.Finding,r.Details,r.Stage}.Select(Csv)))); File.WriteAllLines(d.FileName,lines,new UTF8Encoding(true)); SetStatus("Report exported."); }
            }
            private void ImportCsv()
            {
                string tool = _tools.SelectedItem as string ?? "";
                using (OpenFileDialog dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    _inputRows.Clear(); _inputForTool = "";
                    try
                    {
                        string[] lines = File.ReadAllLines(dialog.FileName);
                        if (lines.Length == 0) throw new InvalidDataException("The CSV file is empty.");
                        string[] headers = ParseCsv(lines[0]).Select(x => (x ?? "").Trim()).ToArray();
                        int column(string name) => Array.FindIndex(headers, h => String.Equals(h, name, StringComparison.OrdinalIgnoreCase));
                        int id = -1, parameter = -1, value = -1, source = -1, target = -1, sheetName = -1;
                        if (tool == "Parameter Mapping")
                        {
                            source = column("SourceParameter"); target = column("TargetParameter");
                            if (source < 0 || target < 0) throw new InvalidDataException("Parameter Mapping requires SourceParameter and TargetParameter columns.");
                        }
                        else if (tool == "Sheet Audit")
                        {
                            id = column("SheetNumber"); sheetName = column("SheetName");
                            if (id < 0) throw new InvalidDataException("Sheet Audit reference requires a SheetNumber column.");
                        }
                        else
                        {
                            id = column("ElementId"); parameter = column("Parameter"); value = column("Value");
                            if (id < 0 || parameter < 0 || value < 0) throw new InvalidDataException("This check requires ElementId, Parameter and Value columns.");
                        }

                        _inputRows.Clear();
                        foreach (string line in lines.Skip(1))
                        {
                            if (String.IsNullOrWhiteSpace(line)) continue;
                            string[] cells = ParseCsv(line);
                            string cell(int index) => index >= 0 && index < cells.Length ? cells[index].Trim() : "";
                            if (tool == "Parameter Mapping")
                                _inputRows.Add(new ReportRow(cell(source), cell(target), "", "Parameter mapping", cell(source) + " → " + cell(target)));
                            else if (tool == "Sheet Audit")
                                _inputRows.Add(new ReportRow(cell(id), cell(sheetName), "", "Expected sheet", "Required in this project"));
                            else
                                _inputRows.Add(new ReportRow(cell(id), cell(parameter), cell(value), "Reference value", "Imported from " + Path.GetFileName(dialog.FileName)));
                        }
                        _inputForTool = tool;
                        _rows = _inputRows.ToList();
                        _grid.Rows.Clear();
                        _grid.Columns["Stage"].Visible = false;
                        foreach (ReportRow row in _rows) _grid.Rows.Add(row.ElementId, row.Category, row.Element, row.Finding, row.Details, row.Stage);
                        ConfigureResultFilter(false, _rows);
                        SizeGridColumnsToContent();
                        SetStatus("Imported " + _inputRows.Count + " reference row(s) for " + tool + ". Select Run Check to compare.");
                        ConfigureButtons();
                    }
                    catch (Exception ex) { SetStatus("CSV import failed: " + ex.Message); }
                }
            }
            private void SaveSnapshot()
            {
                string t=_tools.SelectedItem as string; if(t!="Model Change Log"){SetStatus("Select Model Change Log before saving a snapshot.");return;}
                _handler.Request("Model Change Log"); SetStatus("Comparing with the saved model snapshot…"); BIMlify.CleanerUi.BimQualitySuite.RaiseEvent();
            }
            private void AddTask()
            {
                if (!IsToolSelected("BIM Task Planner") || String.IsNullOrWhiteSpace(_taskProjectKey)) { SetStatus("Open a Revit project to save tasks for that project."); return; }
                using(Form prompt=TaskPrompt()) if(prompt.ShowDialog(this)==DialogResult.OK)
                {
                    string[] values=(prompt.Tag as string??"").Split(new[]{'\t'});
                    string path=TaskFilePath();
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if(!File.Exists(path)) File.WriteAllText(path,"Created,DueDate,Task"+Environment.NewLine,new UTF8Encoding(true));
                    File.AppendAllText(path,Csv(DateTime.Now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))+","+Csv(values.Length>0?values[0]:"")+","+Csv(values.Length>1?values[1]:"")+Environment.NewLine,new UTF8Encoding(true));
                    ShowTasks(); SetStatus("Task saved for " + _taskProjectName + ".");
                }
            }

            private string TaskFilePath()
            {
                if (String.IsNullOrWhiteSpace(_taskProjectKey)) return "";
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMlify", "Tasks", _taskProjectKey + ".csv");
            }

            internal void SetTaskProjectContext(string identity, string projectName)
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action(() => SetTaskProjectContext(identity, projectName))); return; }
                if (String.IsNullOrWhiteSpace(identity)) { _taskProjectKey = ""; _taskProjectName = ""; ShowTasks(); ConfigureButtons(); return; }
                if (String.IsNullOrWhiteSpace(projectName)) projectName = "Current Revit project";
                string key;
                using (SHA256 sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").Substring(0, 20);
                bool changed = !String.Equals(_taskProjectKey, key, StringComparison.OrdinalIgnoreCase);
                _taskProjectKey = key;
                _taskProjectName = projectName.Trim();
                if (changed || IsToolSelected("BIM Task Planner")) ShowTasks();
                ConfigureButtons();
            }

            private void DeleteSelectedTasks()
            {
                if (!IsToolSelected("BIM Task Planner")) return;
                List<int> sourceLines = _grid.SelectedRows.Cast<DataGridViewRow>()
                    .Where(row => row != null && !row.IsNewRow && row.Tag is int)
                    .Select(row => (int)row.Tag).Distinct().OrderBy(index => index).ToList();
                if (sourceLines.Count == 0) { SetStatus("Select one or more tasks to delete."); return; }
                DialogResult answer = MessageBox.Show(this, "Delete the selected task(s) from your local task list?", "Delete BIM Tasks", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
                string path = TaskFilePath();
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) { SetStatus("The project task file is unavailable."); return; }
                try
                {
                    string[] lines = File.ReadAllLines(path);
                    HashSet<int> selectedLines = new HashSet<int>(sourceLines);
                    List<string> keptLines = lines.Where((line, index) => !selectedLines.Contains(index)).ToList();
                    File.WriteAllLines(path, keptLines, new UTF8Encoding(true));
                    ShowTasks();
                    SetStatus("Deleted " + sourceLines.Count + " task(s).");
                }
                catch (Exception ex)
                {
                    SetStatus("Could not delete the selected task(s): " + ex.Message);
                }
            }

            private void ShowTasks()
            {
                string path=TaskFilePath();
                _rowsForTool = "BIM Task Planner"; _grid.Columns["Stage"].Visible = false; _rows=new List<ReportRow>(); _grid.Rows.Clear();
                if (String.IsNullOrWhiteSpace(path))
                {
                    ConfigureResultFilter(false, _rows); SizeGridColumnsToContent();
                    SetStatus("Open a Revit project to view its task list."); ConfigureButtons(); return;
                }
                if(File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
                    {
                        string[] c = ParseCsv(lines[lineIndex]);
                        if (c.Length < 3) continue;
                        var row = new ReportRow("", "Task", c[2], "Due " + c[1], "Created " + c[0]);
                        _rows.Add(row);
                        int gridIndex = _grid.Rows.Add(row.ElementId, row.Category, row.Element, row.Finding, row.Details, row.Stage);
                        _grid.Rows[gridIndex].Tag = lineIndex;
                    }
                }
                ConfigureResultFilter(false, _rows);
                SizeGridColumnsToContent();
                SetStatus(_rows.Count.ToString(CultureInfo.InvariantCulture)+" task(s) for "+_taskProjectName+"."); ConfigureButtons();
            }

            private Form PdfExportOptionsDialog(List<PdfSheetInfo> sheets, Action<PdfExportRequest, Action<string, bool>> requestExport)
            {
                List<PdfSheetInfo> availableSheets = sheets ?? new List<PdfSheetInfo>();
                string projectName = availableSheets.Select(x => x.ProjectName).FirstOrDefault(x => !String.IsNullOrWhiteSpace(x)) ?? "Project";
                Form form = DashboardEnhancements.CreateSettingsStyleDialog();
                form.Text = "PDF/DWG Batch Export"; form.Size = new Size(1080, 720); form.MinimumSize = new Size(980, 720);
                form.StartPosition = FormStartPosition.CenterScreen; form.ShowInTaskbar = false; form.AutoScaleMode = AutoScaleMode.None;
                form.Font = new Font("Segoe UI", 8f, FontStyle.Regular);
                Color surface = Color.FromArgb(238, 241, 245), ink = Color.FromArgb(55, 67, 79), muted = Color.FromArgb(105, 117, 129);
                Panel page = new Panel { Dock = DockStyle.Fill, Size = new Size(1080, 626), BackColor = surface, Padding = new Padding(16, 10, 16, 8) };
                Label description = new Label { Text = "Choose sheets, set a consistent file name, and review print options before export.", Left = 4, Top = 42, Width = 800, Height = 18, Font = new Font("Segoe UI", 8f), ForeColor = muted, AutoEllipsis = true };
                Label outputSection = new Label { Text = "OUTPUT NAMING", Left = 4, Top = 18, Width = 260, Height = 18, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = ink };
                Label folderLabel = new Label { Text = "Output folder", Left = 4, Top = 66, Width = 120, Height = 18, ForeColor = muted };
                TextBox folder = new TextBox { Left = 4, Top = 86, Width = 430, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Font = new Font("Segoe UI", 8f) };
                EnableTextClipboard(folder);
                Button browse = DashboardEnhancements.CreateSettingsStyleButton("Browse…", DialogResult.None); browse.SetBounds(442, 85, 92, 26); browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                Label nameLabel = new Label { Text = "File name template: {Project}, {Date}, {SheetNumber}, {SheetName}", Left = 4, Top = 116, Width = 600, Height = 18, ForeColor = muted };
                TextBox template = new TextBox { Left = 4, Top = 136, Width = 530, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = projectName, Font = new Font("Segoe UI", 8f) };
                EnableTextClipboard(template);
                Label preview = new Label { Left = 4, Top = 158, Width = 760, Height = 16, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, AutoEllipsis = true, Font = new Font("Segoe UI", 7.8f), ForeColor = Color.FromArgb(105, 117, 129), Text = "File name preview" };
                Label sheetsLabel = new Label { Text = "SHEETS TO EXPORT (" + availableSheets.Count.ToString(CultureInfo.InvariantCulture) + ")", Left = 4, Top = 181, Width = 320, Height = 18, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = ink };
                CheckedListBox list = new CheckedListBox { Left = 4, Top = 202, Width = 570, Height = 168, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, CheckOnClick = true, IntegralHeight = false, ScrollAlwaysVisible = true, HorizontalScrollbar = false, BackColor = Color.White, Font = new Font("Segoe UI", 8f, FontStyle.Regular), ItemHeight = 20 };
                foreach (PdfSheetInfo sheet in availableSheets) list.Items.Add(sheet, true);
                Button selectAll = DashboardEnhancements.CreateSettingsStyleButton("Select all", DialogResult.None); selectAll.SetBounds(4, 376, 96, 28);
                Button clear = DashboardEnhancements.CreateSettingsStyleButton("Clear Selection", DialogResult.None); clear.SetBounds(108, 376, 108, 28); clear.BackColor = Color.White;
                GoldCheckBox combine = new GoldCheckBox { Text = "Combine selected sheets into one PDF", Left = 228, Top = 379, Width = 250, Height = 22, Checked = true, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 8f) };
                GoldCheckBox exportDwg = new GoldCheckBox { Text = "Export DWG with PDF", Left = 482, Top = 379, Width = 160, Height = 22, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 8f) };
                Label printLabel = new Label { Text = "PRINT SETTINGS", Left = 4, Top = 410, Width = 200, Height = 18, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = ink };
                Label paperLabel = new Label { Text = "Paper size", Left = 4, Top = 433, Width = 150, Height = 18 };
                System.Windows.Forms.ComboBox paper = new System.Windows.Forms.ComboBox { Left = 4, Top = 452, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8f) };
                paper.Items.AddRange(new object[] { "Default (sheet size)", "ISO_A0", "ISO_A1", "ISO_A2", "ISO_A3", "ISO_A4", "ARCH_D", "ARCH_E", "ANSI_D", "ANSI_E" }); paper.SelectedIndex = 0;
                Label orientationLabel = new Label { Text = "Orientation", Left = 192, Top = 433, Width = 150, Height = 18 };
                System.Windows.Forms.ComboBox orientation = new System.Windows.Forms.ComboBox { Left = 192, Top = 452, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8f) };
                orientation.Items.AddRange(new object[] { "Auto", "Portrait", "Landscape" }); orientation.SelectedIndex = 0;
                Label colorLabel = new Label { Text = "Color depth", Left = 360, Top = 433, Width = 150, Height = 18 };
                System.Windows.Forms.ComboBox color = new System.Windows.Forms.ComboBox { Left = 360, Top = 452, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8f) };
                color.Items.AddRange(new object[] { "Color", "GrayScale", "BlackLine" }); color.SelectedIndex = 0;
                Label placementLabel = new Label { Text = "Paper placement", Left = 4, Top = 486, Width = 150, Height = 18 };
                System.Windows.Forms.ComboBox placement = new System.Windows.Forms.ComboBox { Left = 4, Top = 505, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8f) };
                placement.Items.AddRange(new object[] { "Center", "LowerLeft" }); placement.SelectedIndex = 0;
                GoldCheckBox hideCrop = new GoldCheckBox { Text = "Hide crop boundaries", Left = 192, Top = 507, Width = 170, Height = 22, Checked = true, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 8f) };
                GoldCheckBox raster = new GoldCheckBox { Text = "Use raster processing", Left = 360, Top = 507, Width = 180, Height = 22, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 8f) };
                GoldCheckBox hideScopes = new GoldCheckBox { Text = "Hide scope boxes", Left = 4, Top = 537, Width = 150, Height = 22, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 8f) };
                GoldCheckBox hidePlanes = new GoldCheckBox { Text = "Hide reference planes", Left = 170, Top = 537, Width = 180, Height = 22, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 8f) };
                GoldCheckBox hideTags = new GoldCheckBox { Text = "Hide unreferenced view tags", Left = 360, Top = 537, Width = 215, Height = 22, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 8f) };
                GoldCheckBox thinHalftone = new GoldCheckBox { Text = "Replace halftone with thin lines", Left = 4, Top = 564, Width = 220, Height = 22, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 8f) };
                GoldCheckBox maskLines = new GoldCheckBox { Text = "Mask coincident lines", Left = 240, Top = 564, Width = 190, Height = 22, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 8f) };
                Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(12, 0, 16, 0) };
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 220, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(0, 14, 0, 0) };
                Label selectionSummary = new Label { Left = 14, Top = 18, Width = 600, Height = 20, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Segoe UI", 8f), ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
                Button close = DashboardEnhancements.CreateSettingsStyleButton("Close", DialogResult.Cancel); close.Size = new Size(90, 30); close.Margin = Padding.Empty;
                Button export = DashboardEnhancements.CreateSettingsStyleButton("Export", DialogResult.None); export.Size = new Size(90, 30); export.Margin = new Padding(0, 0, 8, 0); export.BackColor = Color.FromArgb(247, 220, 137);
                Action updateSelectionSummary = () =>
                {
                    int selectedCount = list.CheckedItems.Count;
                    sheetsLabel.Text = "SHEETS TO EXPORT (" + availableSheets.Count.ToString(CultureInfo.InvariantCulture) + ")";
                    selectionSummary.Text = selectedCount.ToString(CultureInfo.InvariantCulture) + " sheet(s) selected  ·  " + (combine.Checked ? "One combined PDF" : "Separate PDFs");
                    PdfSheetInfo exampleSheet = list.CheckedItems.Cast<PdfSheetInfo>().FirstOrDefault();
                    if (exampleSheet == null) { preview.Text = "Preview: select at least one sheet."; return; }
                    string number = combine.Checked ? "Sheets" : exampleSheet.Number, sheetName = combine.Checked ? "" : exampleSheet.Name;
                    string fileName = ExpandPdfName(template.Text, projectName, DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), number, sheetName);
                    preview.Text = "Preview: " + fileName + ".pdf" + (combine.Checked ? "" : "  (example: " + exampleSheet.Number + ")");
                };
                template.TextChanged += (s,e) => updateSelectionSummary(); combine.CheckedChanged += (s,e) => updateSelectionSummary();
                list.ItemCheck += (s,e) => { try { form.BeginInvoke((MethodInvoker)(() => updateSelectionSummary())); } catch { updateSelectionSummary(); } };
                browse.Click += (s,e) => { using (FolderBrowserDialog picker = new FolderBrowserDialog { SelectedPath = folder.Text, Description = "Choose the PDF output folder" }) if (picker.ShowDialog(form) == DialogResult.OK) folder.Text = picker.SelectedPath; };
                selectAll.Click += (s,e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
                clear.Click += (s,e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };
                var presetButton = DashboardEnhancements.CreateSharedStyleButton("Save Preset", primary: true);
                page.Controls.Add(presetButton);
                var projectIdentity = availableSheets.FirstOrDefault()?.ProjectIdentity;
                presetButton.Enabled = !String.IsNullOrWhiteSpace(projectIdentity);
                using var hash = SHA256.Create();
                string presetPath = presetButton.Enabled
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "BIMlify", "Export Presets", BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(projectIdentity))).Replace("-", "") + ".xml")
                    : null;
                var choices = new[] { paper, orientation, color, placement };
                CheckBox[] flags = { combine, exportDwg, hideCrop, raster, hideScopes, hidePlanes, hideTags, thinHalftone, maskLines };
                presetButton.Click += (sender, args) =>
                {
                    try
                    {
                        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(string[]));
                        var values = new[] { folder.Text, template.Text }
                            .Concat(choices.Select(choice => Convert.ToString(choice.SelectedItem)))
                            .Concat(flags.Select(flag => flag.Checked ? "1" : "0"))
                            .Concat(list.CheckedItems.Cast<PdfSheetInfo>().Select(sheet => sheet.UniqueId)).ToArray();
                        Directory.CreateDirectory(Path.GetDirectoryName(presetPath));
                        using (var stream = File.Create(presetPath)) serializer.Serialize(stream, values);
                        selectionSummary.Text = "Export preset saved for this project.";
                    }
                    catch (Exception error) { selectionSummary.Text = error.Message; }
                };
                void LoadPreset()
                {
                    try
                    {
                        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(string[]));
                        string[] values;
                        if (presetPath == null || !File.Exists(presetPath))
                        { selectionSummary.Text = "No export preset saved for this project."; return; }
                        using (var stream = File.OpenRead(presetPath)) values = (string[])serializer.Deserialize(stream);
                        if (values.Length >= 2 + choices.Length + flags.Length)
                        {
                            folder.Text = values[0];
                            template.Text = values[1];
                            int index = 2;
                            foreach (var choice in choices)
                            {
                                int match = choice.Items.IndexOf(values[index++]);
                                if (match >= 0) choice.SelectedIndex = match;
                            }
                            foreach (var flag in flags) flag.Checked = values[index++] == "1";
                            var sheetIds = new HashSet<string>(values.Skip(index), StringComparer.Ordinal);
                            for (int row = 0; row < list.Items.Count; row++)
                                list.SetItemChecked(row, sheetIds.Contains(((PdfSheetInfo)list.Items[row]).UniqueId));
                        }
                    }
                    catch (Exception error) { selectionSummary.Text = error.Message; }
                }

                var loadPresetButton = DashboardEnhancements.CreateSharedStyleButton("Load Preset", primary: true);
                loadPresetButton.Enabled = presetPath != null;
                loadPresetButton.Click += (sender, args) => { LoadPreset(); };
                page.Controls.Add(loadPresetButton);
                form.Shown += (sender, args) => { if (presetPath != null && File.Exists(presetPath)) LoadPreset(); };
                EventHandler exportSheets = (s,e) =>
                {
                    List<PdfSheetInfo> selected = list.CheckedItems.Cast<PdfSheetInfo>().ToList();
                    if (selected.Count == 0 || !Directory.Exists(folder.Text)) { DashboardEnhancements.ShowSettingsStyleMessage(form, "PDF Batch Export", selected.Count == 0 ? "Select at least one sheet." : "Choose an existing output folder."); return; }
                    PdfExportRequest request = new PdfExportRequest { ExportDwg = exportDwg.Checked, Folder = folder.Text, Template = template.Text, Combine = combine.Checked, SheetIds = selected.Select(x => x.Id).ToList(), PaperFormat = paper.SelectedIndex == 0 ? "Default" : Convert.ToString(paper.SelectedItem), Orientation = Convert.ToString(orientation.SelectedItem), ColorDepth = Convert.ToString(color.SelectedItem), PaperPlacement = Convert.ToString(placement.SelectedItem), HideCropBoundaries = hideCrop.Checked, AlwaysUseRaster = raster.Checked, HideScopeBoxes = hideScopes.Checked, HideReferencePlane = hidePlanes.Checked, HideUnreferencedViewTags = hideTags.Checked, ReplaceHalftoneWithThinLines = thinHalftone.Checked, MaskCoincidentLines = maskLines.Checked };
                    requestExport?.Invoke(request, (message, busy) =>
                    {
                        if (form.IsDisposed) return;
                        selectionSummary.Text = message;
                        export.Enabled = !busy;
                        export.Text = busy ? "Exporting…" : "Export";
                        form.UseWaitCursor = busy;
                    });
                };
                export.Click += exportSheets;
                buttons.Controls.Add(close); buttons.Controls.Add(export); footer.Controls.Add(selectionSummary); footer.Controls.Add(buttons);
                selectionSummary.Top = (footer.Height - selectionSummary.Height) / 2;
                page.Controls.Add(maskLines); page.Controls.Add(thinHalftone); page.Controls.Add(hideTags); page.Controls.Add(hidePlanes); page.Controls.Add(hideScopes); page.Controls.Add(raster); page.Controls.Add(hideCrop); page.Controls.Add(placement); page.Controls.Add(placementLabel); page.Controls.Add(color); page.Controls.Add(colorLabel); page.Controls.Add(orientation); page.Controls.Add(orientationLabel); page.Controls.Add(paper); page.Controls.Add(paperLabel); page.Controls.Add(printLabel); page.Controls.Add(exportDwg); page.Controls.Add(combine); page.Controls.Add(clear); page.Controls.Add(selectAll); page.Controls.Add(list); page.Controls.Add(sheetsLabel); page.Controls.Add(preview); page.Controls.Add(template); page.Controls.Add(nameLabel); page.Controls.Add(browse); page.Controls.Add(folder); page.Controls.Add(folderLabel); page.Controls.Add(outputSection); page.Controls.Add(description);
                Action layoutPdfControls = () =>
                {
                    int width = page.ClientSize.Width; const int sideInset = 18; int leftInset = sideInset, rightInset = sideInset;
                    int pageHeight = page.ClientSize.Height;
                    int listTop = 234;
                    int printTop = Math.Max(400, pageHeight - 202);
                    int actionTop = printTop - 36;
                    int settingsRow1Label = printTop + 24;
                    int settingsRow1Control = printTop + 44;
                    int settingsRow2Label = printTop + 80;
                    int settingsRow2Control = printTop + 100;
                    int checkboxRow2 = printTop + 129;
                    int checkboxRow3 = printTop + 158;

                    outputSection.Left = leftInset; outputSection.Top = 16;
                    description.Left = leftInset; description.Top = 40; description.Width = Math.Max(260, Math.Min(700, width - rightInset - leftInset - 150));
                    folderLabel.Left = nameLabel.Left = leftInset; folder.Left = leftInset; folderLabel.Top = 72; folder.Top = 94; browse.Top = 93;
                    nameLabel.Top = 130; template.Top = 152; preview.Top = 180;
                    sheetsLabel.Left = leftInset; sheetsLabel.Top = 210; list.Top = listTop;
                    list.Height = Math.Max(100, actionTop - listTop - 8);
                    selectAll.Left = leftInset; selectAll.Top = actionTop;
                    clear.Left = selectAll.Right + 8; clear.Top = actionTop;
                    combine.Left = clear.Right + 14; combine.Top = actionTop + 3;
                    exportDwg.Left = combine.Right + 12; exportDwg.Top = actionTop + 3;
                    presetButton.SetBounds(exportDwg.Right + 10, actionTop, 110, 28); loadPresetButton.SetBounds(presetButton.Right + 10, actionTop, 110, 28);
                    printLabel.Left = leftInset; printLabel.Top = printTop;
                    browse.Left = Math.Max(leftInset + 220, width - rightInset - browse.Width); folder.Width = Math.Max(180, browse.Left - folder.Left - 10);
                    template.Left = leftInset; template.Width = Math.Max(180, width - leftInset - rightInset); preview.Width = template.Width; list.Left = leftInset; list.Width = Math.Max(220, width - leftInset * 2);
                    int gap = 18, columnWidth = Math.Max(170, (width - leftInset * 2 - gap * 2) / 3), column2 = leftInset + columnWidth + gap, column3 = column2 + columnWidth + gap;
                    paperLabel.Left = placementLabel.Left = hideScopes.Left = thinHalftone.Left = leftInset; orientationLabel.Left = hideCrop.Left = hidePlanes.Left = maskLines.Left = column2; colorLabel.Left = raster.Left = hideTags.Left = column3;
                    paperLabel.Top = orientationLabel.Top = colorLabel.Top = settingsRow1Label; paper.Top = orientation.Top = color.Top = settingsRow1Control;
                    placementLabel.Top = settingsRow2Label; placement.Top = settingsRow2Control; hideCrop.Top = raster.Top = settingsRow2Control;
                    hideScopes.Top = hidePlanes.Top = hideTags.Top = checkboxRow2; thinHalftone.Top = maskLines.Top = checkboxRow3;
                    paper.Left = placement.Left = leftInset; orientation.Left = column2; color.Left = column3; paper.Width = placement.Width = Math.Max(150, columnWidth - 8); orientation.Width = color.Width = Math.Max(140, columnWidth - 8);
                    int checkboxWidth = Math.Max(150, columnWidth - 8); hideCrop.Width = raster.Width = hideScopes.Width = hidePlanes.Width = hideTags.Width = thinHalftone.Width = maskLines.Width = checkboxWidth;
                };
                page.SizeChanged += (s,e) => layoutPdfControls(); form.Shown += (s,e) => layoutPdfControls(); footer.Resize += (s,e) => { selectionSummary.Width = Math.Max(120, footer.ClientSize.Width - buttons.Width - 44); buttons.Padding = new Padding(0, Math.Max(0, (buttons.ClientSize.Height - 30) / 2), 0, 0); };
                updateSelectionSummary(); DashboardEnhancements.AddSettingsStyleMark(page); form.Controls.Add(page); form.Controls.Add(footer);
                DashboardEnhancements.AddSettingsStyleTitleBar(form); DashboardEnhancements.ApplySettingsStyleWindowCorners(form);
                form.AcceptButton = export; form.CancelButton = close; StyleExportLabels(page);
                return form;
            }
            private List<string> ShowChecklist()
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMlify", "LOD");
                string path = Path.Combine(directory, "deliverables.txt");
                HashSet<string> saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { if (File.Exists(path)) foreach (string item in File.ReadAllLines(path)) saved.Add(item.Trim()); } catch { }
                using (Form form = DashboardEnhancements.CreateSettingsStyleDialog())
                {
                    Color surface = Color.FromArgb(238, 241, 245);
                    form.Text = "LOD & Deliverables Checklist"; form.Size = new Size(600, 430); form.MinimumSize = new Size(560, 410);
                    form.StartPosition = FormStartPosition.CenterScreen; form.BackColor = surface; form.Font = new Font("Segoe UI", 9f);
                    form.MaximizeBox = true; form.MinimizeBox = true; form.ShowIcon = false; form.ShowInTaskbar = false;
                    Panel page = new Panel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(18, 12, 18, 10) };
                    Label heading = new Label { Text = "Select a project stage and checks to run against this model", Dock = DockStyle.Top, Height = 38, Padding = new Padding(18, 8, 4, 4), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(55, 67, 79), BackColor = surface };
                    Label stageLabel = new Label { Text = "Project stage", Left = 18, Top = 52, Width = 120, Height = 20, Font = new Font("Segoe UI", 8f), ForeColor = Color.FromArgb(78, 85, 92), BackColor = surface };
                    System.Windows.Forms.ComboBox stage = new System.Windows.Forms.ComboBox { Left = 18, Top = 74, Width = 250, Height = 26, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9f) };
                    stage.Items.AddRange(new object[] { "Design Development", "Shop Drawings", "As-Built" });
                    string savedStage = saved.Contains("Shop Drawings") ? "Shop Drawings" : saved.Contains("As-Built") ? "As-Built" : "Design Development";
                    stage.SelectedItem = savedStage;
                    Label checksLabel = new Label { Text = "Checks to run", Left = 18, Top = 112, Width = 180, Height = 20, Font = new Font("Segoe UI", 8f), ForeColor = Color.FromArgb(78, 85, 92), BackColor = surface };
                    CheckedListBox list = new CheckedListBox { Left = 18, Top = 134, Width = 506, Height = 140, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, CheckOnClick = true, BackColor = Color.White, ForeColor = Color.FromArgb(55, 67, 79), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9f), IntegralHeight = false };
                    string[] items = { "Views named and organized", "Sheets complete", "Parameters populated", "Revisions complete", "Deliverables exported" };
                    foreach (string item in items) list.Items.Add(item, saved.Contains(item) || saved.Count == 0);
                    Label details = new Label { Text = "The checks run against the active Revit model. Deliverables exported is recorded as a manual confirmation.", Left = 18, Top = 282, Width = 506, Height = 34, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Segoe UI", 8f), ForeColor = Color.FromArgb(105, 117, 129), BackColor = surface };
                    Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(12, 12, 16, 8) };
                    Panel actions = new Panel { Dock = DockStyle.Right, Width = 224, BackColor = footer.BackColor };
                    Button run = DashboardEnhancements.CreateSettingsStyleButton("Run Checks", DialogResult.None); run.SetBounds(0, 3, 104, 30);
                    Button close = DashboardEnhancements.CreateSettingsStyleButton("Close", DialogResult.Cancel); close.SetBounds(120, 3, 96, 30);
                    run.Click += (s,e) =>
                    {
                        try
                        {
                            List<string> selected = list.CheckedItems.Cast<object>().Select(x => x.ToString()).ToList();
                            Directory.CreateDirectory(directory);
                            _lodStage = stage.SelectedItem == null ? "Design Development" : stage.SelectedItem.ToString();
                            File.WriteAllLines(path, new[] { _lodStage }.Concat(selected), new UTF8Encoding(true));
                            form.Tag = String.Join("\n", selected); form.DialogResult = DialogResult.OK; form.Close();
                        }
                        catch (Exception ex) { DashboardEnhancements.ShowSettingsStyleMessage(form, "Smart QA/QC", "Checklist could not be saved: " + ex.Message); }
                    };
                    actions.Controls.Add(run); actions.Controls.Add(close); footer.Controls.Add(actions);
                    page.Controls.Add(list); page.Controls.Add(details); page.Controls.Add(checksLabel); page.Controls.Add(stage); page.Controls.Add(stageLabel); page.Controls.Add(heading); DashboardEnhancements.AddSettingsStyleMark(page);
                    form.Controls.Add(page); form.Controls.Add(footer);
                    DashboardEnhancements.AddSettingsStyleTitleBar(form); DashboardEnhancements.ApplySettingsStyleWindowCorners(form);
                    form.AcceptButton = run; form.CancelButton = close;
                    if (form.ShowDialog(this) != DialogResult.OK) return null;
                    _lodRules = (form.Tag as string ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    return _lodRules;
                }
            }
            private Form TaskPrompt()
            {
                Color surface = Color.FromArgb(238, 241, 245);
                Form form = DashboardEnhancements.CreateSettingsStyleDialog();
                form.Text = "Add BIM Task"; form.Size = new Size(600, 430); form.MinimumSize = new Size(560, 410);
                form.StartPosition = FormStartPosition.CenterScreen; form.BackColor = surface; form.Font = new Font("Segoe UI", 9f);
                form.MaximizeBox = true; form.MinimizeBox = true; form.ShowIcon = false; form.ShowInTaskbar = false;
                Panel page = new Panel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(24, 18, 82, 12) };
                Label taskLabel = new Label { Text = "Task", Left = 24, Top = 22, Width = 400, Height = 20, Font = new Font("Segoe UI", 8f), ForeColor = Color.FromArgb(78, 85, 92), BackColor = surface };
                TextBox box = new TextBox { Left = 24, Top = 44, Width = 430, Height = 27, Font = new Font("Segoe UI", 9f) };
                Label dueLabel = new Label { Text = "Due date", Left = 24, Top = 82, Width = 180, Height = 20, Font = new Font("Segoe UI", 8f), ForeColor = Color.FromArgb(78, 85, 92), BackColor = surface };
                DateTimePicker due = new DateTimePicker { Left = 24, Top = 104, Width = 180, Format = DateTimePickerFormat.Short, Font = new Font("Segoe UI", 9f) };
                Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(12, 12, 16, 8) };
                Panel actions = new Panel { Dock = DockStyle.Right, Width = 208, BackColor = footer.BackColor };
                Button save = DashboardEnhancements.CreateSettingsStyleButton("Save", DialogResult.None);
                Button cancel = DashboardEnhancements.CreateSettingsStyleButton("Close", DialogResult.Cancel);
                save.SetBounds(0, 3, 96, 30); cancel.SetBounds(112, 3, 96, 30);
                save.Click += (s,e) => { if (String.IsNullOrWhiteSpace(box.Text)) { DashboardEnhancements.ShowSettingsStyleMessage(form, "Add BIM Task", "Enter a task name before saving."); return; } form.Tag = due.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"\t"+box.Text.Trim(); form.DialogResult=DialogResult.OK; form.Close(); };
                actions.Controls.Add(save); actions.Controls.Add(cancel); footer.Controls.Add(actions);
                page.Controls.Add(taskLabel); page.Controls.Add(box); page.Controls.Add(dueLabel); page.Controls.Add(due);
                DashboardEnhancements.AddSettingsStyleMark(page);
                form.Controls.Add(page); form.Controls.Add(footer);
                DashboardEnhancements.AddSettingsStyleTitleBar(form); DashboardEnhancements.ApplySettingsStyleWindowCorners(form);
                form.AcceptButton = save; form.CancelButton = cancel;
                return form;
            }

            private string PromptValue(string title,string initial)
            {
                Color surface = Color.FromArgb(238, 241, 245);
                using (Form form = DashboardEnhancements.CreateSettingsStyleDialog())
                {
                    form.Text = title; form.Size = new Size(600, 430); form.MinimumSize = new Size(560, 410);
                    form.StartPosition = FormStartPosition.CenterScreen; form.BackColor = surface; form.Font = new Font("Segoe UI", 9f);
                    form.MaximizeBox = true; form.MinimizeBox = true; form.ShowIcon = false; form.ShowInTaskbar = false;
                    Panel page = new Panel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(24, 18, 82, 12) };
                    Label label = new Label { Text = title, Left = 24, Top = 22, Width = 440, Height = 20, Font = new Font("Segoe UI", 8f), ForeColor = Color.FromArgb(78, 85, 92), BackColor = surface };
                    TextBox box = new TextBox { Left = 24, Top = 46, Width = 430, Height = 27, Text = initial ?? "", Font = new Font("Segoe UI", 9f) };
                    Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(12, 12, 16, 8) };
                    Panel actions = new Panel { Dock = DockStyle.Right, Width = 208, BackColor = footer.BackColor };
                    Button ok = DashboardEnhancements.CreateSettingsStyleButton("OK", DialogResult.None);
                    Button cancel = DashboardEnhancements.CreateSettingsStyleButton("Close", DialogResult.Cancel);
                    ok.SetBounds(0, 3, 96, 30); cancel.SetBounds(112, 3, 96, 30);
                    ok.Click += (s,e) => { form.Tag=box.Text; form.DialogResult=DialogResult.OK; form.Close(); };
                    actions.Controls.Add(ok); actions.Controls.Add(cancel); footer.Controls.Add(actions);
                    page.Controls.Add(label); page.Controls.Add(box); DashboardEnhancements.AddSettingsStyleMark(page);
                    form.Controls.Add(page); form.Controls.Add(footer);
                    DashboardEnhancements.AddSettingsStyleTitleBar(form); DashboardEnhancements.ApplySettingsStyleWindowCorners(form);
                    form.AcceptButton=ok; form.CancelButton=cancel;
                    return form.ShowDialog(this)==DialogResult.OK?(form.Tag as string):null;
                }
            }
        }

        private static void RaiseEvent() { try { if (_externalEvent == null) { _window?.SetStatus("Revit refresh event is not ready; reopen Smart Log and try again."); return; } if (_externalEvent.Raise() == ExternalEventRequest.Denied) _window?.SetStatus("Revit is busy; try again shortly."); } catch { _window?.SetStatus("Revit is busy; try again shortly."); } }
        private static string Csv(string s) => "\""+(s??"").Replace("\"","\"\"")+"\"";
        private static string[] ParseCsv(string line) { var list=new List<string>(); var b=new StringBuilder(); bool q=false; for(int i=0;i<line.Length;i++){char c=line[i];if(c=='"'){if(q&&i+1<line.Length&&line[i+1]=='"'){b.Append('"');i++;}else q=!q;}else if(c==','&&!q){list.Add(b.ToString());b.Clear();}else b.Append(c);}list.Add(b.ToString());return list.ToArray(); }

        private static ReportRow ReadProjectStandard(Form owner)
        {
            using (Form dialog = DashboardEnhancements.CreateSettingsStyleDialog())
            {
                dialog.Text = "BIMlify — Project Standards";
                dialog.Size = new Size(600, 430);
                dialog.Font = new Font("Segoe UI", 9f);
                dialog.BackColor = Color.FromArgb(238, 241, 245);
                dialog.StartPosition = FormStartPosition.CenterParent;
                var category = new TextBox();
                var parameters = new TextBox();
                var pattern = new TextBox();
                var body = new Panel { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f) };
                void AddField(string text, string help, TextBox input, int top)
                {
                    var label = new Label { Text = text, AutoSize = true, Left = 26, Top = top };
                    var hint = new Label { Text = help, AutoSize = true, Left = 26 };
                    input.SetBounds(26, top + 30, 462, 26);
                    input.AccessibleName = text;
                    body.Controls.AddRange(new System.Windows.Forms.Control[] { label, input, hint });
                    input.Top = Math.Max(input.Top, label.Bottom + 8);
                    hint.Top = input.Bottom + 6;
                }
                AddField("Category", "Leave blank to check every category for required parameters.", category, 28);
                AddField("Required parameters", "Separate parameter names with commas, for example Mark, Comments.", parameters, 120);
                AddField("Sheet number pattern", "Optional regular expression, for example ^A-[0-9]{3}$ matches A-101.", pattern, 212);
                DashboardEnhancements.AddSettingsStyleMark(body);
                var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(216, 221, 227) };
                var cancel = DashboardEnhancements.CreateSettingsStyleButton("Cancel", DialogResult.Cancel);
                var run = DashboardEnhancements.CreateSettingsStyleButton("Run Check", DialogResult.OK);
                run.BackColor = Color.FromArgb(250, 230, 168);
                void LayoutButtons()
                {
                    cancel.SetBounds(footer.ClientSize.Width - 125, 17, 96, 30);
                    run.SetBounds(cancel.Left - 108, 17, 96, 30);
                }
                footer.Controls.AddRange(new System.Windows.Forms.Control[] { run, cancel });
                footer.Resize += (s, e) => LayoutButtons();
                dialog.Controls.Add(body);
                dialog.Controls.Add(footer);
                LayoutButtons();
                dialog.AcceptButton = run;
                dialog.CancelButton = cancel;
                DashboardEnhancements.AddSettingsStyleTitleBar(dialog);
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                return new ReportRow("", category.Text.Trim(), parameters.Text.Trim(), "", pattern.Text.Trim());
            }
        }

        private static List<ReportRow> AuditProjectStandard(Document doc, List<ReportRow> input)
        {
            var rule = input.FirstOrDefault();
            if (rule == null) throw new InvalidOperationException("Enter a project standard before running the check.");
            var names = rule.Element.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var sheetPattern = rule.Details.Length == 0 ? null : new Regex(rule.Details, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            if (names.Length == 0 && sheetPattern == null)
                throw new InvalidOperationException("Enter required parameters, a sheet number pattern, or both.");
            var rows = new List<ReportRow>();
            if (names.Length > 0)
            {
                var categories = doc.Settings.Categories.Cast<Category>()
                    .Where(category => String.Equals(category.Name, rule.Category, StringComparison.CurrentCultureIgnoreCase))
                    .Select(category => category.Id).ToList();
                if (rule.Category.Length > 0 && categories.Count == 0)
                    throw new InvalidOperationException("The project has no category named " + rule.Category + ".");
                var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                if (categories.Count > 0) collector.WherePasses(new ElementMulticategoryFilter(categories));
                foreach (Element element in collector)
                {
                    if (element.Category == null || element is View view && view.IsTemplate) continue;
                    foreach (string name in names)
                    {
                        var values = element.GetParameters(name);
                        if (values.Any(value => value.HasValue && (value.StorageType != StorageType.String || !String.IsNullOrWhiteSpace(value.AsString())))) continue;
                        rows.Add(new ReportRow(element.Id.Value.ToString(), element.Category.Name, element.Name,
                            values.Count == 0 ? "Missing parameter" : "Empty parameter", name));
                    }
                }
            }
            if (sheetPattern != null)
                foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                    if (!sheet.IsPlaceholder && !sheetPattern.IsMatch(sheet.SheetNumber))
                        rows.Add(new ReportRow(sheet.Id.Value.ToString(), "Sheets", sheet.Name, "Sheet number does not match", sheet.SheetNumber));
            return rows;
        }

        private static List<ReportRow> Audit(Document doc,string tool,List<ReportRow> input)
        {
            var rows=new List<ReportRow>(); Func<Element,string> cat=e=>e.Category==null?"":e.Category.Name; Action<Element,string,string> add=(e,f,d)=>rows.Add(new ReportRow(e.Id.Value.ToString(),cat(e),e.Name,f,d));
            if (tool == "Project Standards") return AuditProjectStandard(doc, input);
            if (tool == "Smart Links")
            {
                var request = input.FirstOrDefault();
                if (request != null && long.TryParse(request.ElementId, out long id))
                {
                    if (request.Stage != AuditProjectKey(doc) || !(doc.GetElement(new ElementId(id)) is RevitLinkType link))
                        throw new InvalidOperationException("The selected link is unavailable in this project.");
                    LinkLoadResult result = request.Details.Length == 0 ? link.Reload() :
                        link.LoadFrom(ModelPathUtils.ConvertUserVisiblePathToModelPath(request.Details), new WorksetConfiguration());
                    if (result.LoadResult != LinkLoadResultType.LinkLoaded)
                        throw new InvalidOperationException("Revit link load result: " + result.LoadResult);
                }
                foreach (var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
                {
                    var reference = link.IsExternalFileReference() ? link.GetExternalFileReference() : null;
                    var path = reference?.GetAbsolutePath();
                    string displayPath = path == null ? "" : ModelPathUtils.ConvertModelPathToUserVisiblePath(path);
                    bool externalResource = link.RefersToExternalResourceReferences();
                    if (String.IsNullOrWhiteSpace(displayPath) && externalResource)
                        displayPath = link.GetExternalResourceReferences().Values.Select(resource => resource.InSessionPath)
                            .FirstOrDefault(resourcePath => !String.IsNullOrWhiteSpace(resourcePath)) ?? "";
                    string status = reference?.GetLinkedFileStatus().ToString() ??
                        (RevitLinkType.IsLoaded(doc, link.Id) ? "Loaded" : "Not loaded");
                    string category = reference != null ? "File link" : externalResource ? "Resource link" : "Other Revit link";
                    rows.Add(new ReportRow(link.Id.Value.ToString(), category, link.Name,
                        status, displayPath, AuditProjectKey(doc)));
                }
                return rows;
            }
            if (tool == "Quantity Reports")
            {
                bool byLevel = input.FirstOrDefault()?.Details == "Level";
                var counts = new Dictionary<string, int>();
                var materials = new Dictionary<Tuple<string, ElementId>, (int Count, double Area, double Volume)>();
                foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    if (element.Category?.CategoryType != CategoryType.Model || element is View || element is RevitLinkInstance) continue;
                    string group = byLevel ? doc.GetElement(element.LevelId)?.Name ?? "No level" : element.Category.Name;
                    counts.TryGetValue(group, out int count);
                    counts[group] = count + 1;
                    if (!element.Category.HasMaterialQuantities) continue;
                    foreach (var materialId in element.GetMaterialIds(false))
                    {
                        var key = Tuple.Create(group, materialId);
                        materials.TryGetValue(key, out var quantity);
                        materials[key] = (quantity.Count + 1, quantity.Area + element.GetMaterialArea(materialId, false),
                            quantity.Volume + element.GetMaterialVolume(materialId));
                    }
                }
                foreach (var count in counts.OrderBy(pair => pair.Key))
                    rows.Add(new ReportRow("", count.Key, "Elements", "Count", count.Value.ToString(CultureInfo.InvariantCulture)));
                foreach (var item in materials.OrderBy(pair => pair.Key.Item1))
                {
                    var quantity = item.Value;
                    string name = doc.GetElement(item.Key.Item2)?.Name ?? item.Key.Item2.Value.ToString();
                    rows.Add(new ReportRow("", item.Key.Item1, name, "Material quantity",
                        FormattableString.Invariant($"{quantity.Count} elements | {UnitUtils.ConvertFromInternalUnits(quantity.Area, UnitTypeId.SquareMeters):0.###} m² | {UnitUtils.ConvertFromInternalUnits(quantity.Volume, UnitTypeId.CubicMeters):0.###} m³")));
                }
                return rows;
            }
            if(tool=="Excel ↔ Revit Data Checker") return CompareImportedData(doc,input,rows); if(tool=="Parameter Mapping") return ReviewParameterMappings(doc,input,rows);
            if(tool=="LOD & Deliverables Checker")
            {
                string stage=(input??new List<ReportRow>()).Select(x=>x.Stage).FirstOrDefault(x=>!String.IsNullOrWhiteSpace(x)) ?? (_window==null?"Selected project stage":_window._lodStage);
                Dictionary<string,string> mapped=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
                {
                    {"Views named and organized","Naming Convention Checker"}, {"Sheets complete","Sheet Audit"},
                    {"Parameters populated","Parameter Audit"}, {"Revisions complete","Revision Manager"}
                };
                foreach(ReportRow selected in input??new List<ReportRow>())
                {
                    string rule=selected.Category??"";
                    if(rule.Equals("Deliverables exported",StringComparison.OrdinalIgnoreCase))
                    {
                        rows.Add(new ReportRow("","Delivery",stage,"Manual confirmation","Marked complete in the checklist; verify the required files in the delivery folder.",stage));
                        continue;
                    }
                    string check;
                    if(!mapped.TryGetValue(rule,out check)) continue;
                    List<ReportRow> findings=Audit(doc,check,new List<ReportRow>());
                    if(findings.Count==0) rows.Add(new ReportRow("","Delivery",stage+" / "+rule,"Passed","No findings from "+check+".",stage));
                    else foreach(ReportRow finding in findings) rows.Add(new ReportRow(finding.ElementId,finding.Category,finding.Element,"Delivery · "+finding.Finding,stage+" / "+rule+" — "+finding.Details,stage));
                }
                return rows;
            }
            if(tool=="Workset Checker") { if(!doc.IsWorkshared){rows.Add(new ReportRow("","Worksets","","Not workshared","Enable worksharing to use workset assignment."));return rows;} var members=new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements();var elementCounts=members.GroupBy(e=>e.WorksetId.IntegerValue).ToDictionary(g=>g.Key,g=>g.Count());int listed=0; foreach(Workset w in new FilteredWorksetCollector(doc).ToWorksets()){int n=elementCounts.ContainsKey(w.Id.IntegerValue)?elementCounts[w.Id.IntegerValue]:0; rows.Add(new ReportRow("", "Workset", w.Name, "Element count", n+" element(s)"));foreach(Element e in members.Where(e=>e.WorksetId.IntegerValue==w.Id.IntegerValue).Take(Math.Max(0,1200-listed))){rows.Add(new ReportRow(e.Id.Value.ToString(),e.Category==null?"":e.Category.Name,e.Name,"Assigned to workset",w.Name));listed++;}if(listed>=1200)break;} if(members.Count>listed)rows.Add(new ReportRow("","Workset","","Member list limited","Listed "+listed+" elements of "+members.Count+"; counts above include all elements.")); return rows; }
            if(tool=="Sheet Audit")
            {
                var sheets=new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s=>!s.IsPlaceholder).ToList();
                if(input!=null&&input.Count>0)
                {
                    var actual=new HashSet<string>(sheets.Select(x=>x.SheetNumber),StringComparer.OrdinalIgnoreCase);
                    foreach(ReportRow expected in input)
                        if(!String.IsNullOrWhiteSpace(expected.ElementId)&&!actual.Contains(expected.ElementId))
                            rows.Add(new ReportRow("", "Sheet", expected.Category, "Expected sheet is missing", "Sheet number: "+expected.ElementId));
                }
                foreach(var g in sheets.GroupBy(s=>s.SheetNumber,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
                    foreach(ViewSheet sheet in g) add(sheet,"Duplicate sheet number",sheet.SheetNumber);
                foreach(ViewSheet sheet in sheets.Where(sheet=>sheet.GetAllPlacedViews().Count==0))
                    add(sheet,"Empty sheet","No views placed on sheet");
                return rows;
            }
            if(tool=="Revision Manager")
            {
                List<Revision> revisions=Revision.GetAllRevisionIds(doc).Select(id=>doc.GetElement(id) as Revision).Where(r=>r!=null).ToList();
                bool perSheet=RevisionSettings.GetRevisionSettings(doc).RevisionNumbering==Autodesk.Revit.DB.RevisionNumbering.PerSheet;
                if(perSheet)
                {
                    // Revision.RevisionNumber throws when the project numbers revisions per sheet.
                    // Compare the displayed number within each sheet using Revit's sheet API.
                    foreach(ViewSheet sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s=>!s.IsPlaceholder))
                    {
                        List<Tuple<Revision,string>> onSheet=new List<Tuple<Revision,string>>();
                        foreach(ElementId id in sheet.GetAllRevisionIds())
                        {
                            Revision revision=doc.GetElement(id) as Revision;
                            if(revision==null) continue;
                            string number=sheet.GetRevisionNumberOnSheet(id)??"";
                            if(!String.IsNullOrWhiteSpace(number)) onSheet.Add(Tuple.Create(revision,number));
                        }
                        foreach(var group in onSheet.GroupBy(x=>x.Item2,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
                            foreach(var duplicate in group) rows.Add(new ReportRow(duplicate.Item1.Id.Value.ToString(),"Revision",duplicate.Item1.Name,"Duplicate revision number on sheet",sheet.SheetNumber+" — "+group.Key));
                    }
                }
                else
                {
                    foreach(var group in revisions.Where(r=>!String.IsNullOrWhiteSpace(r.RevisionNumber)).GroupBy(r=>r.RevisionNumber,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
                        foreach(Revision duplicate in group) add(duplicate,"Duplicate revision number",duplicate.RevisionNumber);
                    foreach(Revision revision in revisions)
                        if(String.IsNullOrWhiteSpace(revision.RevisionNumber)) add(revision,"Revision number is blank",revision.RevisionDate);
                }
                foreach(Revision revision in revisions)
                {
                    if(String.IsNullOrWhiteSpace(revision.RevisionDate)) add(revision,"Revision date is blank",perSheet?"Per-sheet numbering":"Revision number: "+revision.RevisionNumber);
                    else { DateTime parsed; if(!DateTime.TryParse(revision.RevisionDate,CultureInfo.CurrentCulture,DateTimeStyles.None,out parsed)) add(revision,"Revision date format",revision.RevisionDate); }
                }
                return rows;
            }
            if(tool=="Parameter Audit")
            {
                HashSet<string> specialized=new HashSet<string>(new[]{"Number","Name","Department","Mark","Type Mark","Wall Finish","Floor Finish","Ceiling Finish","Base Finish"},StringComparer.OrdinalIgnoreCase);
                foreach(Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements().Take(10000))
                {
                    foreach(Parameter p in e.Parameters)
                    {
                        try
                        {
                            if(p==null||p.Definition==null||specialized.Contains(p.Definition.Name)||p.Id==null||p.Id.Value<0) continue;
                            if(!p.HasValue || (p.StorageType==StorageType.String&&String.IsNullOrWhiteSpace(p.AsString()))) add(e,"Blank custom parameter",p.Definition.Name);
                        }
                        catch { }
                        if(rows.Count>=1000) break;
                    }
                    if(rows.Count>=1000) break;
                }
                return rows;
            }
            if(tool=="Naming Convention Checker")
            {
                foreach (Element element in new FilteredElementCollector(doc).WhereElementIsElementType())
                    if (InvalidName(element.Name)) add(element, "Inconsistent type name", element.Name);
                foreach (Family family in new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>())
                    if (InvalidName(family.Name)) add(family, "Inconsistent family name", family.Name);
                foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    if (!(element is View) && !(element is Level) && !(element is Grid) && element.Category == null) continue;
                    if (InvalidName(element.Name)) add(element, "Inconsistent element name", element.Name);
                }
                return rows;
            }

            if(tool=="Level & Grid Audit")
            {
                List<Level> levels=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x=>x.Elevation).ToList();
                foreach(Level level in levels)
                    if(InvalidName(level.Name)) add(level,"Inconsistent level name","Check leading/trailing or repeated spaces and control characters.");
                const double elevationToleranceFeet=0.003280839895;
                for(int i=1;i<levels.Count;i++)
                    if(Math.Abs(levels[i].Elevation-levels[i-1].Elevation)<elevationToleranceFeet)
                        add(levels[i],"Nearly duplicate level elevation","Another level is within 1 mm. Review level names and elevations.");
                foreach(Grid grid in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
                    if(InvalidName(grid.Name)) add(grid,"Inconsistent grid name","Check leading/trailing or repeated spaces and control characters.");
                return rows;
            }

            if(tool=="Model Health Score")
            {
                string[] checks={"Sheet Audit","Revision Manager","Parameter Audit","Naming Convention Checker","Room Data Checker","Door & Window Audit","Finishes Checker","Level & Grid Audit","Opening Checker"};
                int passed=0,issues=0;
                foreach(string check in checks)
                {
                    try
                    {
                        List<ReportRow> findings=Audit(doc,check,new List<ReportRow>());
                        if(findings.Count==0) passed++; else issues+=findings.Count;
                        rows.Add(new ReportRow("","Quality area",check,findings.Count==0?"Passed":"Review needed",findings.Count+" finding(s)"));
                    }
                    catch(Exception ex)
                    {
                        issues++;
                        rows.Add(new ReportRow("","Quality area",check,"Could not run",ex.Message));
                    }
                }
                int score=(int)Math.Round(100.0*passed/checks.Length);
                rows.Insert(0,new ReportRow("","Model Health",doc.Title,"Score: "+score+" / 100",passed+" of "+checks.Length+" audit areas passed; "+issues+" total finding(s). Each audit area has equal weight."));
                return rows;
            }
            
            if(tool=="Room Data Checker"||tool=="Finishes Checker") { foreach(Element e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().ToElements()){if(tool=="Room Data Checker"){foreach(string n in new[]{"Number","Name","Department"}){Parameter p=e.LookupParameter(n);if(p==null||!p.HasValue||String.IsNullOrWhiteSpace(p.AsString()))add(e,"Missing room data",n);}}else foreach(string n in new[]{"Wall Finish","Floor Finish","Ceiling Finish","Base Finish"}){Parameter p=e.LookupParameter(n);if(p!=null&&(!p.HasValue||String.IsNullOrWhiteSpace(p.AsString())))add(e,"Finish not assigned",n);}} return rows; }
            if(tool=="Door & Window Audit")
            {
                var instances = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements()
                    .Where(e=>e.Category!=null && (e.Category.Id.Value==(int)BuiltInCategory.OST_Doors || e.Category.Id.Value==(int)BuiltInCategory.OST_Windows));
                foreach(Element e in instances)
                {
                    Element type = doc.GetElement(e.GetTypeId());
                    Parameter mark = e.LookupParameter("Mark");
                    if(mark==null||!mark.HasValue||String.IsNullOrWhiteSpace(mark.AsString())) add(e,"Missing mark","Mark");
                    Parameter typeMark = type==null?null:type.LookupParameter("Type Mark");
                    if(typeMark==null||!typeMark.HasValue||String.IsNullOrWhiteSpace(typeMark.AsString())) add(e,"Missing type mark",type==null?"Type is unavailable":"Type Mark");
                    if(e is FamilyInstance fi&&fi.Symbol==null) add(e,"Missing type","Family instance has no type");
                    foreach(string n in new[]{"Width","Height"})
                    {
                        Parameter p=e.LookupParameter(n)??(type==null?null:type.LookupParameter(n));
                        if(p!=null&&p.HasValue&&p.StorageType==StorageType.Double&&p.AsDouble()<=0) add(e,"Invalid dimension",n+" is zero or negative");
                    }
                }
                return rows;
            }
            if(tool=="Opening Checker") { foreach(Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements().Where(e=>cat(e).IndexOf("opening",StringComparison.OrdinalIgnoreCase)>=0||((e as FamilyInstance)?.Symbol?.FamilyName??"").IndexOf("opening",StringComparison.OrdinalIgnoreCase)>=0)){FamilyInstance fi=e as FamilyInstance;if(fi==null||fi.Host==null)add(e,"Opening has no host","Check host and placement");} return rows; }
            if(tool=="Model Change Log") { return CompareSnapshot(doc); }
            return rows;
        }

        private static List<ReportRow> CompareImportedData(Document doc,List<ReportRow> input,List<ReportRow> rows)
        {
            foreach(ReportRow r in input??new List<ReportRow>()) { Element e=ResolveElement(doc,r.ElementId); if(e==null){rows.Add(new ReportRow(r.ElementId,"","","Element not found","CSV row could not be matched to a Revit element."));continue;} Parameter p=e.LookupParameter(r.Category??""); if(p==null){rows.Add(new ReportRow(r.ElementId,e.Category==null?"":e.Category.Name,e.Name,"Parameter not found",r.Category));continue;} string actual=p.StorageType==StorageType.String?p.AsString():p.AsValueString(); if(!String.Equals((actual??"").Trim(),(r.Element??"").Trim(),StringComparison.OrdinalIgnoreCase)) rows.Add(new ReportRow(e.Id.Value.ToString(),e.Category==null?"":e.Category.Name,e.Name,"Value differs",(r.Category??"")+": Revit="+(actual??"<blank>")+"; CSV="+(r.Element??"<blank>"))); }
            return rows;
        }
        private static List<ReportRow> ReviewParameterMappings(Document doc,List<ReportRow> input,List<ReportRow> rows)
        {
            List<Element> elements=new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements().Take(10000).ToList();
            foreach(ReportRow map in input??new List<ReportRow>()) { string source=map.ElementId??"", target=map.Category??""; if(String.IsNullOrWhiteSpace(source)||String.IsNullOrWhiteSpace(target))continue; foreach(Element e in elements){Parameter a=e.LookupParameter(source),b=e.LookupParameter(target);if(a==null&&b==null)continue;if(a==null||b==null){rows.Add(new ReportRow(e.Id.Value.ToString(),e.Category==null?"":e.Category.Name,e.Name,"Mapping parameter missing",source+" → "+target));continue;} string av=a.StorageType==StorageType.String?a.AsString():a.AsValueString(),bv=b.StorageType==StorageType.String?b.AsString():b.AsValueString();if(!String.Equals((av??"").Trim(),(bv??"").Trim(),StringComparison.OrdinalIgnoreCase))rows.Add(new ReportRow(e.Id.Value.ToString(),e.Category==null?"":e.Category.Name,e.Name,"Mapped values differ",source+"="+(av??"<blank>")+" → "+target+"="+(bv??"<blank>")));if(rows.Count>=1000)return rows;}}
            return rows;
        }
        private static Element ResolveElement(Document doc,string id)
        {
            if(String.IsNullOrWhiteSpace(id))return null; Element e=null; long numeric; if(Int64.TryParse(id,NumberStyles.Integer,CultureInfo.InvariantCulture,out numeric))try{e=doc.GetElement(new ElementId(numeric));}catch{} if(e==null)try{e=doc.GetElement(id);}catch{} return e;
        }
        private static bool InvalidName(string s) => String.IsNullOrWhiteSpace(s)||s!=s.Trim()||Regex.IsMatch(s,@"\s{2,}")||s.Any(Char.IsControl);
        private static string Fingerprint(Element e) { try { StringBuilder b=new StringBuilder(); foreach(Parameter p in e.Parameters) { if(p==null||p.Definition==null)continue; string v=p.StorageType==StorageType.String?p.AsString():p.AsValueString(); b.Append(p.Definition.Name).Append("=").Append(v??"").Append(";"); } using(SHA256 sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-",""); } catch { return ""; } }
        private static string SnapshotPath(Document d)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMlify", "Snapshots");
            Directory.CreateDirectory(dir);
            string title = Regex.Replace(Path.GetFileNameWithoutExtension(d.Title ?? "Model"), "[^A-Za-z0-9_-]", "_");
            // Separate different models that happen to share a title, so Smart Log
            // never compares one project's elements against another project's baseline.
            string identity = !String.IsNullOrWhiteSpace(d.PathName) ? d.PathName : (d.Title ?? "") + "|" + (d.ProjectInformation?.UniqueId ?? "");
            string key;
            using (SHA256 sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").Substring(0, 16);
            return Path.Combine(dir, title + "_" + key + ".csv");
        }
        private static List<ReportRow> CompareSnapshot(Document doc, bool updateBaseline = true, string snapshotPath = null)
        {
            string path = snapshotPath ?? SnapshotPath(doc);
            List<Element> elements = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements().ToList();
            Dictionary<string, string> current = elements.ToDictionary(
                e => e.UniqueId,
                e => String.Join(",", new[] { Csv(e.UniqueId), Csv(e.Id.Value.ToString(CultureInfo.InvariantCulture)), Csv(e.Category == null ? "" : e.Category.Name), Csv(e.Name), Csv(Fingerprint(e)), Csv(SnapshotParameterValues(e)) }),
                StringComparer.Ordinal);
            DateTime comparedAt = DateTime.Now;
            List<ReportRow> rows = new List<ReportRow>();
            if (snapshotPath == null && File.Exists(path))
            {
                Dictionary<string, string[]> old = File.ReadAllLines(path).Skip(1)
                    .Where(line => !String.IsNullOrWhiteSpace(line))
                    .Select(ParseCsv)
                    .Where(fields => fields.Length >= 5)
                    .ToDictionary(fields => fields[0], fields => fields, StringComparer.Ordinal);
                Dictionary<string, string[]> now = current.Values.Select(ParseCsv)
                    .ToDictionary(fields => fields[0], fields => fields, StringComparer.Ordinal);
                bool refreshBaseline = old.Values.Any(fields => fields.Length < 6);
                if (refreshBaseline)
                {
                    rows.Add(new ReportRow("", "Snapshot", doc.Title, "Baseline refreshed",
                        "Updated the local snapshot format without reporting changes, to avoid false results. Compare again after a model update to review meaningful parameter differences."));
                }
                else
                {
                    int added = now.Keys.Count(id => !old.ContainsKey(id));
                    int removed = old.Keys.Count(id => !now.ContainsKey(id));
                    int changed = now.Keys.Count(id => old.ContainsKey(id) && !String.Equals(now[id][5], old[id][5], StringComparison.Ordinal));
                    rows.Add(new ReportRow("", "Snapshot", doc.Title, "Added " + added + " · Modified " + changed + " · Removed " + removed,
                        "Compared editable element values with the previous local snapshot at " + comparedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ". The comparison time is not the actual edit time."));
                    foreach (KeyValuePair<string, string[]> item in now.Where(pair => !old.ContainsKey(pair.Key)))
                        rows.Add(new ReportRow(item.Value[1], item.Value[2], item.Value[3], "Added since snapshot", "New element in the current comparison."));
                    foreach (KeyValuePair<string, string[]> item in old.Where(pair => !now.ContainsKey(pair.Key)))
                        rows.Add(new ReportRow(item.Value[1], item.Value[2], item.Value[3], "Removed since snapshot", "Element existed in the previous local snapshot."));
                    foreach (KeyValuePair<string, string[]> item in now.Where(pair => old.ContainsKey(pair.Key) && !String.Equals(pair.Value[5], old[pair.Key][5], StringComparison.Ordinal)))
                    {
                        string changedParameters = DescribeSnapshotParameterChanges(old[item.Key][5], item.Value[5]);
                        rows.Add(new ReportRow(item.Value[1], item.Value[2], item.Value[3], "Element data changed", changedParameters));
                    }
                }
            }
            else
            {
                rows.Add(new ReportRow("", "Snapshot", doc.Title, "Baseline saved", "Saved a local baseline of " + current.Count + " elements at " + comparedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ". Run Compare again after an update to see differences."));
            }
            if (updateBaseline) File.WriteAllLines(path, new[] { "UniqueId,ElementId,Category,Element,ParameterFingerprint,EditableParameters" }.Concat(current.Values), new UTF8Encoding(true));
            return rows;
        }

        private static List<ReportRow> CompareSavedVersions(string earlier, string later)
        {
            Dictionary<string, string[]> Read(string path)
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length == 0 || !lines[0].StartsWith("UniqueId,ElementId,Category,Element,ParameterFingerprint,EditableParameters", StringComparison.Ordinal))
                    throw new InvalidOperationException("Choose snapshots saved by BIMlify.");
                return lines.Skip(1).Where(line => !String.IsNullOrWhiteSpace(line)).Select(ParseCsv)
                    .Where(fields => fields.Length >= 6).ToDictionary(fields => fields[0], StringComparer.Ordinal);
            }
            var before = Read(earlier);
            var after = Read(later);
            var rows = new List<ReportRow>();
            foreach (var pair in before)
                if (!after.ContainsKey(pair.Key))
                    rows.Add(new ReportRow("", pair.Value[2], pair.Value[3], "Deleted", "Present in the earlier snapshot."));
            foreach (var pair in after)
            {
                var row = pair.Value;
                if (!before.TryGetValue(pair.Key, out var old))
                    rows.Add(new ReportRow(pair.Key, row[2], row[3], "Added", "Present in the later snapshot."));
                else if (old[3] != row[3] || old[4] != row[4] || old[5] != row[5])
                    rows.Add(new ReportRow(pair.Key, row[2], row[3], "Modified",
                        old[3] != row[3] ? "Previous name: " + old[3] : DescribeSnapshotParameterChanges(old[5], row[5])));
            }
            return rows;
        }

        private static string SnapshotParameterValues(Element element)
        {
            if (element == null) return "";
            SortedDictionary<string, string> values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> duplicateNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter == null || parameter.Definition == null || parameter.IsReadOnly) continue;
                string name = parameter.Definition.Name ?? "Parameter";
                int occurrence = duplicateNames.ContainsKey(name) ? duplicateNames[name] + 1 : 1;
                duplicateNames[name] = occurrence;
                string key = occurrence == 1 ? name : name + " [" + occurrence.ToString(CultureInfo.InvariantCulture) + "]";
                string value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
                values[key] = value ?? "";
            }
            return String.Join("\n", values.Select(pair =>
                Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Key)) + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Value))));
        }

        private static string DescribeSnapshotParameterChanges(string previous, string current)
        {
            Func<string, Dictionary<string, string>> decode = text =>
            {
                Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in (text ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] pair = line.Split('|');
                    if (pair.Length != 2) continue;
                    try { result[Encoding.UTF8.GetString(Convert.FromBase64String(pair[0]))] = Encoding.UTF8.GetString(Convert.FromBase64String(pair[1])); } catch { }
                }
                return result;
            };
            Dictionary<string, string> oldValues = decode(previous);
            Dictionary<string, string> newValues = decode(current);
            List<string> changes = oldValues.Keys.Concat(newValues.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(key => !oldValues.ContainsKey(key) || !newValues.ContainsKey(key) || !String.Equals(oldValues[key], newValues[key], StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.CurrentCultureIgnoreCase)
                .Select(key => key + ": " + (oldValues.ContainsKey(key) ? oldValues[key] : "(new)") + " → " + (newValues.ContainsKey(key) ? newValues[key] : "(removed)"))
                .ToList();
            if (changes.Count == 0) return "Element data differs from the previous snapshot; editable parameter details were unavailable.";
            int shown = Math.Min(4, changes.Count);
            string summary = String.Join("; ", changes.Take(shown));
            if (changes.Count > shown) summary += "; and " + (changes.Count - shown).ToString(CultureInfo.InvariantCulture) + " more changed parameter(s)";
            return summary;
        }

        private static void OpenNwcOutput(IList<string> createdFiles, string folder)
        {
            try
            {
                string target = createdFiles != null && createdFiles.Count == 1 && File.Exists(createdFiles[0])
                    ? createdFiles[0]
                    : folder;
                if (!String.IsNullOrWhiteSpace(target) && (File.Exists(target) || Directory.Exists(target)))
                    Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            }
            catch (Exception ex) { _window?.SetStatus("Export completed, but the output could not be opened: " + ex.Message); }
        }

        private static List<string> ExportNwc(Document doc, UIDocument uiDocument, NwcExportRequest request)
        {
            if (request == null) throw new InvalidOperationException("Choose NWC export settings before exporting.");
            if (String.IsNullOrWhiteSpace(request.Folder) || !Directory.Exists(request.Folder)) throw new DirectoryNotFoundException("Choose an existing output folder.");
            if (!OptionalFunctionalityUtils.IsNavisworksExporterAvailable())
                throw new InvalidOperationException("The Revit Navisworks NWC exporter is not available. Install or enable the Navisworks NWC Export Utility for Revit 2024, then restart Revit.");

            bool selectedViewsMode = String.Equals(request.Scope, "Selected 3D views", StringComparison.Ordinal) || String.Equals(request.Scope, "Selected 3D view", StringComparison.Ordinal);
            List<View3D> views = new List<View3D>();
            if (selectedViewsMode)
            {
                List<long> ids = request.ViewIds == null ? new List<long>() : request.ViewIds.Distinct().ToList();
                if (ids.Count == 0 && request.ViewId > 0) ids.Add(request.ViewId);
                foreach (long id in ids)
                {
                    View3D view = doc.GetElement(new ElementId(id)) as View3D;
                    if (view == null || view.IsTemplate) throw new InvalidOperationException("One of the selected 3D views is no longer exportable. Refresh the list and choose valid views.");
                    views.Add(view);
                }
                if (views.Count == 0) throw new InvalidOperationException("Select at least one exportable 3D view.");
            }
            else if (String.Equals(request.Scope, "Current selection", StringComparison.Ordinal))
            {
                ICollection<ElementId> ids = uiDocument?.Selection?.GetElementIds();
                if (ids == null || ids.Count == 0) throw new InvalidOperationException("Select one or more Revit elements before exporting the current selection.");
            }

            string project = Path.GetFileNameWithoutExtension(doc.Title);
            string date = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            List<Tuple<View3D, string>> exports = new List<Tuple<View3D, string>>();
            if (selectedViewsMode)
            {
                foreach (View3D view in views)
                {
                    string name = ExpandNwcName(request.FileNameTemplate, project, date, view.Name);
                    if (views.Count > 1 && (request.FileNameTemplate ?? "").IndexOf("{ViewName}", StringComparison.OrdinalIgnoreCase) < 0)
                        name = SafeFileName(name + "_" + view.Name);
                    exports.Add(Tuple.Create(view, name));
                }
            }
            else exports.Add(Tuple.Create<View3D, string>(null, ExpandNwcName(request.FileNameTemplate, project, date, "")));

            List<string> outputPaths = exports.Select(item => Path.Combine(request.Folder, item.Item2.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase) ? item.Item2 : item.Item2 + ".nwc")).ToList();
            if (outputPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != outputPaths.Count)
                throw new InvalidOperationException("The file name template creates duplicate names for the selected views. Add {ViewName} to the template.");
            string existingPath = outputPaths.FirstOrDefault(File.Exists);
            if (!String.IsNullOrEmpty(existingPath)) throw new IOException("An NWC file already exists at " + existingPath + ". Change the file name to avoid overwriting it.");

            Indicator.StartProgress("Starting NWC export…", exports.Count);
            try
            {
            int completedExports = 0;
            foreach (Tuple<View3D, string> item in exports)
            {
                Indicator.SetStage("Exporting NWC " + (completedExports + 1).ToString(CultureInfo.InvariantCulture) + " of " + exports.Count.ToString(CultureInfo.InvariantCulture) + ": " + item.Item2);
                ElementId temporaryViewId = null;
                try
                {
                    ElementId exportViewId = item.Item1 == null ? null : item.Item1.Id;
                    if (item.Item1 != null && (request.HideShaftOpeningLines || request.HideMasses || request.HideLines))
                    {
                        using (Transaction transaction = new Transaction(doc, "BIMlify temporary NWC export view"))
                        {
                            transaction.Start();
                            temporaryViewId = item.Item1.Duplicate(ViewDuplicateOption.Duplicate);
                            View3D temporaryView = doc.GetElement(temporaryViewId) as View3D;
                            if (temporaryView == null) throw new InvalidOperationException("Could not create a temporary 3D view for the NWC visibility options.");
                            if (request.HideShaftOpeningLines)
                            {
                                HideNwcCategories(temporaryView, doc, "shaft openings", BuiltInCategory.OST_ShaftOpening, BuiltInCategory.OST_ShaftOpeningHiddenLines);
                            }
                            if (request.HideMasses)
                                HideNwcCategories(temporaryView, doc, "masses", BuiltInCategory.OST_Mass, BuiltInCategory.OST_Massing, BuiltInCategory.OST_MassForm, BuiltInCategory.OST_MassHiddenLines);
                            if (request.HideLines) HideNwcCategories(temporaryView, doc, "lines", BuiltInCategory.OST_Lines);
                            if (request.ExportLinks && (request.HideShaftOpeningLines || request.HideMasses || request.HideLines))
                                ApplyHostCategoryVisibilityToNwcLinks(temporaryView, doc);
                            transaction.Commit();
                        }
                        exportViewId = temporaryViewId;
                    }

                    using (NavisworksExportOptions options = new NavisworksExportOptions())
                    {
                    options.ExportLinks = request.ExportLinks;
                    options.ExportElementIds = request.ExportElementIds;
                    options.ExportRoomGeometry = request.ExportRooms;
                    options.ExportRoomAsAttribute = request.ExportRoomAsAttribute;
                    options.DivideFileIntoLevels = request.DivideByLevels;
                    options.ConvertLinkedCADFormats = request.ConvertLinkedCad;
                    options.ConvertElementProperties = request.ExportElementProperties;
                    options.FindMissingMaterials = request.FindMissingMaterials;
                    options.ExportUrls = request.ExportUrls;
                    options.ExportParts = request.ExportParts;
                    options.ConvertLights = request.ConvertLights;
                    options.FacetingFactor = request.FacetingFactor;
                    options.Coordinates = String.Equals(request.Coordinates, "Internal coordinates", StringComparison.Ordinal)
                        ? NavisworksCoordinates.Internal : NavisworksCoordinates.Shared;
                    options.Parameters = String.Equals(request.Parameters, "Element parameters", StringComparison.Ordinal)
                        ? NavisworksParameters.Elements
                        : (String.Equals(request.Parameters, "No parameters", StringComparison.Ordinal) ? NavisworksParameters.None : NavisworksParameters.All);

                    if (String.Equals(request.Scope, "Current selection", StringComparison.Ordinal))
                    {
                        options.ExportScope = NavisworksExportScope.SelectedElements;
                        options.SetSelectedElementIds(uiDocument.Selection.GetElementIds());
                    }
                    else if (item.Item1 != null)
                    {
                        options.ExportScope = NavisworksExportScope.View;
                        options.ViewId = exportViewId;
                    }
                    else options.ExportScope = NavisworksExportScope.Model;

                        doc.Export(request.Folder, item.Item2, options);
                    }
                    completedExports++;
                    Indicator.SetProgress(completedExports, exports.Count, "NWC files completed: " + completedExports.ToString(CultureInfo.InvariantCulture) + " of " + exports.Count.ToString(CultureInfo.InvariantCulture));
                }
                finally
                {
                    if (temporaryViewId != null && doc.GetElement(temporaryViewId) != null)
                    {
                        using (Transaction cleanup = new Transaction(doc, "Remove temporary NWC export view"))
                        {
                            cleanup.Start();
                            doc.Delete(temporaryViewId);
                            cleanup.Commit();
                        }
                    }
                }
            }
            }
            finally
            {
                Indicator.Stop();
            }
            return outputPaths;
        }

        private static void HideNwcCategories(View3D view, Document doc, string displayName, params BuiltInCategory[] builtInCategories)
        {
            if (view == null || doc == null) return;
            List<Category> targetCategories = new List<Category>();
            foreach (BuiltInCategory builtInCategory in builtInCategories ?? new BuiltInCategory[0])
            {
                Category category = null;
                try { category = doc.Settings.Categories.get_Item(builtInCategory); }
                catch { }
                if (category == null) continue;
                targetCategories.Add(category);
                try
                {
                    foreach (Category subCategory in category.SubCategories)
                        if (subCategory != null) targetCategories.Add(subCategory);
                }
                catch { }
            }
            targetCategories = targetCategories.GroupBy(category => category.Id.IntegerValue).Select(group => group.First()).ToList();
            if (targetCategories.Count == 0)
                throw new InvalidOperationException("Revit could not resolve the NWC visibility categories for " + displayName + ". No NWC was exported. Open Visibility/Graphics (VV/VG) in the source 3D view, hide the category, and retry.");

            HashSet<int> targetIds = new HashSet<int>(targetCategories.Select(category => category.Id.IntegerValue));
            List<Element> matchingElements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(element => element.Category != null && targetIds.Contains(element.Category.Id.IntegerValue))
                .ToList();

            HashSet<int> hiddenCategoryIds = new HashSet<int>();
            foreach (Category category in targetCategories)
            {
                try
                {
                    if (view.CanCategoryBeHidden(category.Id))
                    {
                        view.SetCategoryHidden(category.Id, true);
                        if (view.GetCategoryHidden(category.Id)) hiddenCategoryIds.Add(category.Id.IntegerValue);
                    }
                }
                catch { }
            }

            // Hide every matching host-model instance as a fallback. This covers
            // mass-form and shaft-opening categories that do not honor a category
            // visibility override in some Revit models.
            List<ElementId> hideableIds = new List<ElementId>();
            List<Element> unhideableVisibleElements = new List<Element>();
            foreach (Element element in matchingElements)
            {
                try
                {
                    if (hiddenCategoryIds.Contains(element.Category.Id.IntegerValue)) continue;
                    if (element.CanBeHidden(view)) hideableIds.Add(element.Id);
                    else unhideableVisibleElements.Add(element);
                }
                catch { }
            }
            if (hideableIds.Count > 0) view.HideElements(hideableIds.Distinct().ToList());
            List<Element> stillVisibleCandidates = matchingElements.Where(element =>
            {
                if (hiddenCategoryIds.Contains(element.Category.Id.IntegerValue)) return false;
                try { return !element.IsHidden(view); }
                catch { return true; }
            }).ToList();
            if (unhideableVisibleElements.Count > 0 || stillVisibleCandidates.Count > 0)
            {
                string categories = String.Join(", ", targetCategories.Select(category => category.Name).Distinct().Take(5));
                throw new InvalidOperationException("Revit could not reliably hide all " + displayName + " in the temporary NWC export view (" + categories + "). No NWC was exported. Hide these categories in Visibility/Graphics (VV/VG) for the source 3D view, or turn off Export Links if the remaining objects are inside a linked model, then retry.");
            }
        }

        private static void ApplyHostCategoryVisibilityToNwcLinks(View3D view, Document doc)
        {
            foreach (RevitLinkInstance link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                try
                {
                    RevitLinkGraphicsSettings settings = new RevitLinkGraphicsSettings();
                    settings.LinkVisibilityType = LinkVisibility.ByHostView;
                    view.SetLinkOverrides(link.Id, settings);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Revit could not apply the temporary host-view visibility rules to linked model '" + link.Name + "'. No NWC was exported. Turn off Export Links or hide the categories in the linked model's export view, then retry.", ex);
                }
            }
        }

        private static string ExpandNwcName(string template, string project, string date, string viewName = "")
        {
            string result = (template ?? "{Project}_{Date}")
                .Replace("{Project}", project ?? "")
                .Replace("{Date}", date ?? "")
                .Replace("{ViewName}", viewName ?? "");
            return SafeFileName(result);
        }
        private static string ExportPdf(Document doc, PdfExportRequest request)
        {
            if(request==null||String.IsNullOrWhiteSpace(request.Folder)||!Directory.Exists(request.Folder))throw new DirectoryNotFoundException("Choose an existing output folder.");
            HashSet<long> selected=new HashSet<long>(request.SheetIds??new List<long>());
            List<ViewSheet> sheets=new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s=>!s.IsPlaceholder&&selected.Contains(s.Id.Value)).ToList();
            if(sheets.Count==0)throw new InvalidOperationException("Select at least one exportable sheet.");
            string project=Path.GetFileNameWithoutExtension(doc.Title), date=DateTime.Now.ToString("yyyyMMdd");
            int pdfCount = 0;
            if(request.Combine)
            {
                string name=ExpandPdfName(request.Template,project,date,"Sheets","");
                using(PDFExportOptions options=BuildPdfOptions(request)){options.Combine=true;options.FileName=name;if(!doc.Export(request.Folder,sheets.Select(s=>s.Id).ToList(),options))throw new IOException("Revit reported that the combined PDF export did not complete.");}
                pdfCount = 1;
            }
            else foreach(ViewSheet sheet in sheets)
            {
                string name=ExpandPdfName(request.Template,project,date,sheet.SheetNumber,sheet.Name);
                using(PDFExportOptions options=BuildPdfOptions(request)){options.Combine=true;options.FileName=name;if(!doc.Export(request.Folder,new List<ElementId>{sheet.Id},options))throw new IOException("Revit reported that PDF export did not complete for sheet " + sheet.SheetNumber + ".");}
                pdfCount++;
            }
            int dwgCount = 0;
            if (request.ExportDwg)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var options = new DWGExportOptions())
                foreach (ViewSheet sheet in sheets)
                {
                    string name = ExpandPdfName(request.Template, project, date, sheet.SheetNumber, sheet.Name);
                    if (!names.Add(name))
                    {
                        name = SafeFileName(name + "-" + sheet.SheetNumber);
                        if (!names.Add(name)) throw new IOException("The DWG file names must be unique.");
                    }
                    if (File.Exists(Path.Combine(request.Folder, name + ".dwg")))
                        throw new IOException("DWG already exists: " + name + ".dwg");
                    if (!doc.Export(request.Folder, name, new List<ElementId> { sheet.Id }, options))
                        throw new IOException("Revit could not export DWG for sheet " + sheet.SheetNumber + ".");
                    dwgCount++;
                }
            }
            string pdfMessage = request.Combine ? sheets.Count.ToString(CultureInfo.InvariantCulture) + " sheet(s) combined into one PDF" : pdfCount.ToString(CultureInfo.InvariantCulture) + " PDF file(s)";
            return "Export completed: " + pdfMessage + (dwgCount > 0 ? " and " + dwgCount.ToString(CultureInfo.InvariantCulture) + " DWG file(s)" : "") + " in " + request.Folder + ".";
        }
        private static PDFExportOptions BuildPdfOptions(PdfExportRequest request)
        {
            PDFExportOptions options=new PDFExportOptions();
            options.Combine=true;
            options.PaperFormat=(ExportPaperFormat)Enum.Parse(typeof(ExportPaperFormat),String.IsNullOrWhiteSpace(request.PaperFormat)?"Default":request.PaperFormat);
            options.PaperOrientation=(PageOrientationType)Enum.Parse(typeof(PageOrientationType),String.IsNullOrWhiteSpace(request.Orientation)?"Auto":request.Orientation);
            options.ColorDepth=(ColorDepthType)Enum.Parse(typeof(ColorDepthType),String.IsNullOrWhiteSpace(request.ColorDepth)?"Color":request.ColorDepth);
            options.PaperPlacement=(PaperPlacementType)Enum.Parse(typeof(PaperPlacementType),String.IsNullOrWhiteSpace(request.PaperPlacement)?"Center":request.PaperPlacement);
            options.HideCropBoundaries=request.HideCropBoundaries;
            options.AlwaysUseRaster=request.AlwaysUseRaster;
            options.HideScopeBoxes=request.HideScopeBoxes;
            options.HideReferencePlane=request.HideReferencePlane;
            options.HideUnreferencedViewTags=request.HideUnreferencedViewTags;
            options.ReplaceHalftoneWithThinLines=request.ReplaceHalftoneWithThinLines;
            options.MaskCoincidentLines=request.MaskCoincidentLines;
            return options;
        }
        private static string ExpandPdfName(string template,string project,string date,string number,string name)
        {
            string result=(template??"{Project}_{Date}").Replace("{Project}",project??"").Replace("{Date}",date??"").Replace("{SheetNumber}",number??"").Replace("{SheetName}",name??"");
            return SafeFileName(result);
        }
        private static string SafeFileName(string s){foreach(char c in Path.GetInvalidFileNameChars())s=s.Replace(c,'_');return String.IsNullOrWhiteSpace(s)?"BIMlify_Sheets":s;}
    }
    public static class QuickBarShadowRenderer
    {
        public static void Draw(Graphics graphics, float centerX, float centerY)
        {
            if (graphics == null) return;
            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF bounds = new RectangleF(centerX - 20f, centerY - 3.5f, 40f, 8f);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(bounds);
                using (PathGradientBrush brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(28, 32, 34, 36);
                    brush.SurroundColors = new[] { Color.FromArgb(0, 32, 34, 36) };
                    brush.FocusScales = new PointF(0.40f, 0.28f);
                    graphics.FillPath(brush, path);
                }
            }
            graphics.SmoothingMode = previous;
        }

        // Kept for the existing Revit host assemblies that call this shared renderer.
        public static void DrawSidebarIconShadow(Graphics graphics, Rectangle buttonBounds)
        {
        }
    }}














