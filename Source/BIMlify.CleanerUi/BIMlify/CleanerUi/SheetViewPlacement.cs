using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.ExtensibleStorage;
using Color = System.Drawing.Color;
using Control = System.Windows.Forms.Control;
using Panel = System.Windows.Forms.Panel;
using TextBox = System.Windows.Forms.TextBox;
using View = Autodesk.Revit.DB.View;

namespace BIMlify.CleanerUi
{
    public static partial class SheetOrganization191
    {
        private static readonly Guid PlacementSchemaId = new Guid("546c8d85-5ea5-4a17-b6d6-48ccb5182623");

        private sealed class PlacementReference
        {
            internal string Label;
            internal XYZ Point;
            internal int Segment = -1;
            public override string ToString() => Label;
        }

        private static Button[] PopulateViewPlacement(TabPage page, DataGridView style, Autodesk.Revit.UI.UIDocument ui)
        {
            Document doc = ui.Document;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            page.Controls.Add(layout);
            var bodyFont = new Font(style.Font.FontFamily, 8f, FontStyle.Regular);
            var headerFont = new Font(bodyFont, FontStyle.Bold);
            page.Disposed += (sender, args) => { headerFont.Dispose(); bodyFont.Dispose(); };
            var viewGrid = PlacementGrid(style);
            viewGrid.Columns.Add("Type", "Type");
            viewGrid.Columns.Add("View", "View name");
            viewGrid.Columns[0].FillWeight = 45;
            viewGrid.MultiSelect = false;
            var sheetGrid = PlacementGrid(style);
            sheetGrid.ReadOnly = false;
            sheetGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Use", FillWeight = 20 });
            sheetGrid.Columns.Add("Number", "Sheet number");
            sheetGrid.Columns.Add("Name", "Sheet name");
            sheetGrid.Columns.Add("Status", "Placement");
            foreach (DataGridViewColumn column in sheetGrid.Columns.Cast<DataGridViewColumn>().Skip(1)) column.ReadOnly = true;
            foreach (var grid in new[] { viewGrid, sheetGrid })
            {
                grid.Font = bodyFont;
                grid.DefaultCellStyle.Font = bodyFont;
                grid.AlternatingRowsDefaultCellStyle.Font = bodyFont;
                grid.ColumnHeadersDefaultCellStyle.Font = headerFont;
                grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
                grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
                grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                grid.ColumnHeadersHeight = Math.Max(30, TextRenderer.MeasureText("Sheet number", headerFont).Height + 12);
                grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 67, 79);
                grid.DefaultCellStyle.Padding = new Padding(5, 2, 5, 2);
                grid.AlternatingRowsDefaultCellStyle.Padding = new Padding(5, 2, 5, 2);
            }
            sheetGrid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            sheetGrid.Columns[0].Width = 42;
            AttachSheetCheckboxes(sheetGrid);
            var viewSearch = new TextBox { AccessibleName = "Search views by name or type" };
            var sheetSearch = new TextBox { AccessibleName = "Search sheets by number or name" };
            var references = new System.Windows.Forms.ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Placement reference" };
            var detailing = new CheckBox { Text = "Keep detailing when duplicating", Checked = true, AutoSize = true };
            var viewType = new System.Windows.Forms.ComboBox
            {
                Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = bodyFont, AccessibleName = "View type"
            };
            var typeBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            typeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            typeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            typeBar.Controls.Add(new Label { Text = "View type", Font = bodyFont, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = style.ForeColor }, 0, 0);
            typeBar.Controls.Add(viewType, 1, 0);
            var viewCard = (TableLayoutPanel)PlacementCard(style, "1. Choose view", "Search views by name", viewSearch, viewGrid,
                new Label { Text = "Placement reference", TextAlign = ContentAlignment.MiddleLeft }, references, detailing);
            foreach (Control control in viewCard.Controls.Cast<Control>().OrderByDescending(viewCard.GetRow).ToArray())
                if (viewCard.GetRow(control) >= 1) viewCard.SetRow(control, viewCard.GetRow(control) + 1);
            viewCard.RowStyles.Insert(1, new RowStyle(SizeType.Absolute, 34));
            viewCard.Controls.Add(typeBar, 0, 1);
            layout.Controls.Add(viewCard, 0, 0);
            layout.Controls.Add(PlacementCard(style, "2. Select target sheets", "Search sheets by number or name", sheetSearch, sheetGrid), 1, 0);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                Font = bodyFont, Text = "Choose a view and check the target sheets." };
            var all = DashboardEnhancements.CreateSettingsStyleButton("Select all", DialogResult.None);
            var none = DashboardEnhancements.CreateSettingsStyleButton("Clear Selection", DialogResult.None);
            var place = DashboardEnhancements.CreateSettingsStyleButton("Place on Selected Sheets", DialogResult.None);
            place.BackColor = Color.FromArgb(250, 230, 168);
            footer.Controls.Add(status, 0, 0);




            layout.Controls.Add(footer, 0, 1);
            layout.SetColumnSpan(footer, 2);

            View SelectedView() => viewGrid.CurrentRow?.Tag as View;
            bool loadingViews = false;
            void LoadViews()
            {
                var prior = SelectedView()?.Id;
                loadingViews = true;
                try
                {
                    viewGrid.Rows.Clear();
                    var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                        .Where(v => !v.IsTemplate && !(v is ViewSheet) &&
                            (v is ViewSchedule schedule ? !schedule.IsTitleblockRevisionSchedule && !schedule.IsInternalKeynoteSchedule :
                                v is PanelScheduleView || v.CanBePrinted))
                        .OrderBy(v => v.ViewType).ThenBy(v => v.Name).ToList();
                    string selectedType = viewType.SelectedItem as string ?? "All types";
                    var availableTypes = new[] { "All types" }.Concat(views.Select(view => PlacementViewTypeLabel(view.ViewType))
                        .Distinct().OrderBy(type => type, StringComparer.CurrentCultureIgnoreCase)).ToArray();
                    if (!viewType.Items.Cast<string>().SequenceEqual(availableTypes))
                    {
                        viewType.Items.Clear();
                        viewType.Items.AddRange(availableTypes);
                        viewType.SelectedIndex = Math.Max(0, Array.IndexOf(availableTypes, selectedType));
                        selectedType = (string)viewType.SelectedItem;
                    }
                    foreach (var view in views)
                    {
                        string type = PlacementViewTypeLabel(view.ViewType);
                        if ((selectedType != "All types" && selectedType != type) || !PlacementSearch(viewSearch.Text, view.Name)) continue;
                        var row = viewGrid.Rows[viewGrid.Rows.Add(type, view.Name)];
                        row.Tag = view;
                        if (view.Id == prior) viewGrid.CurrentCell = row.Cells[0];
                    }
                }
                finally { loadingViews = false; }
                RefreshPreview();
            }

            void RefreshPreview()
            {
                if (loadingViews) return;
                View view = SelectedView();
                var priorReference = references.SelectedItem?.ToString();
                references.Items.Clear();
                references.Items.Add(new PlacementReference { Label = "Find free space on each sheet" });
                var checks = sheetGrid.Rows.Cast<DataGridViewRow>().Where(r => Convert.ToBoolean(r.Cells[0].Value))
                    .Select(r => ((ViewSheet)r.Tag).Id).ToHashSet();
                sheetGrid.Rows.Clear();
                var placements = PlacementInstances(doc, view).ToLookup(instance => instance is Viewport viewport ? viewport.SheetId : instance.OwnerViewId);
                bool reuse = view is ViewSchedule || view is PanelScheduleView;
                bool duplicable = view != null && (view.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing) ||
                    view.CanViewBeDuplicated(ViewDuplicateOption.Duplicate));
                foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber))
                {
                    foreach (var instance in placements[sheet.Id])
                    {
                        var reference = ReferencePlacement(instance);
                        reference.Label = sheet.SheetNumber + (reference.Segment >= 0 ? " · segment " + (reference.Segment + 1) : "");
                        references.Items.Add(reference);
                    }
                    string result = view == null ? "Choose a view" : placements.Contains(sheet.Id) ? "Already placed" :
                        reuse || view.ViewType == ViewType.Legend && Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id) ? "Use original view" :
                        duplicable ? "Place or duplicate" :
                        Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id) ? "Use original view" : "Unavailable";
                    bool available = result == "Use original view" || result == "Place or duplicate";
                    var row = sheetGrid.Rows[sheetGrid.Rows.Add(available && checks.Contains(sheet.Id), sheet.SheetNumber, sheet.Name, result)];
                    row.Tag = sheet;
                    row.Cells[0].ReadOnly = !available;
                }
                FilterSheets();
                int index = references.FindStringExact(priorReference ?? "");
                references.SelectedIndex = Math.Max(0, index);
                place.Enabled = view != null;
            }

            viewSearch.TextChanged += (sender, args) => LoadViews();
            viewType.SelectedIndexChanged += (sender, args) => { if (!loadingViews) LoadViews(); };
            sheetSearch.TextChanged += (sender, args) => FilterSheets();
            void FilterSheets()
            {
                sheetGrid.CurrentCell = null;
                foreach (DataGridViewRow row in sheetGrid.Rows)
                    row.Visible = PlacementSearch(sheetSearch.Text, Convert.ToString(row.Cells[1].Value), Convert.ToString(row.Cells[2].Value));
            }
            viewGrid.SelectionChanged += (sender, args) => RefreshPreview();
            all.Click += (sender, args) => SetPlacementChecks(true);
            none.Click += (sender, args) => SetPlacementChecks(false);
            void SetPlacementChecks(bool value)
            {
                foreach (DataGridViewRow row in sheetGrid.Rows)
                    if (row.Visible && !row.Cells[0].ReadOnly) row.Cells[0].Value = value;
            }

            place.Click += (sender, args) => SheetWorkspace.Run(sheetGrid, () =>
            {
                if (!sheetGrid.EndEdit() || !(SelectedView() is View view)) return;
                var targets = sheetGrid.Rows.Cast<DataGridViewRow>()
                    .Where(row => row.Visible && !row.Cells[0].ReadOnly && Convert.ToBoolean(row.Cells[0].Value)).ToList();
                if (targets.Count == 0) { status.Text = "Check one or more available sheets."; return; }
                var reference = (PlacementReference)references.SelectedItem;
                int placed = 0;
                var results = new Dictionary<ElementId, string>();
                foreach (DataGridViewRow row in targets)
                {
                    var sheet = (ViewSheet)row.Tag;
                    try
                    {
                        using (var transaction = new Transaction(doc, "BIMlify — Place view on " + sheet.SheetNumber))
                        {
                            transaction.Start();
                            var failures = new PlacementFailures();
                            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                                .SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                            var instanceId = PlaceViewOnSheet(doc, sheet, view, reference, detailing.Checked);
                            if (transaction.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException(failures.Error ?? "Revit did not commit the placement.");
                            var instance = doc.GetElement(instanceId);
                            ElementId owner = instance is Viewport viewport ? viewport.SheetId : instance?.OwnerViewId;
                            if (owner != sheet.Id)
                                throw new InvalidOperationException("Revit did not retain the placement on this sheet.");
                        }
                        placed++;
                        results.Add(sheet.Id, "Placed");
                    }
                    catch (Exception error) { results.Add(sheet.Id, error.Message); }
                }
                RefreshPreview();
                foreach (DataGridViewRow row in sheetGrid.Rows)
                {
                    if (!results.TryGetValue(((ViewSheet)row.Tag).Id, out string result)) continue;
                    row.Cells[3].Value = result;
                    row.Cells[3].ToolTipText = result;
                    if (result == "Placed") { row.Cells[0].Value = false; row.Cells[0].ReadOnly = true; }
                }
                int failed = targets.Count - placed;
                status.Text = failed == 0
                    ? placed + " placed. Double-click a sheet marked Placed to open it in Revit."
                    : targets.Count == 1 ? results.Values.Single()
                    : placed + " placed. " + failed + " failed. Hover over a sheet status for details.";
                status.ForeColor = placed == targets.Count ? Color.FromArgb(36, 112, 65) : style.ForeColor;
            });
            sheetGrid.CellDoubleClick += (sender, args) => { int rowIndex = args.RowIndex; SheetWorkspace.Run(sheetGrid, () =>
            {
                if (rowIndex < 0 || args.ColumnIndex < 1) return;
                var row = sheetGrid.Rows[rowIndex];
                string result = Convert.ToString(row.Cells[3].Value);
                if (result != "Placed" && result != "Already placed") return;
                try
                {
                    ui.RequestViewChange((ViewSheet)row.Tag);

                }
                catch (Exception error) { status.Text = error.Message; }
            }); };
            page.Enter += (sender, args) => LoadViews();
            LoadViews();
            return new[] { all, none, place };
        }

        private sealed class PlacementFailures : IFailuresPreprocessor
        {
            internal string Error;

            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (var failure in accessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning) continue;
                    Error = failure.GetDescriptionText();
                    return FailureProcessingResult.ProceedWithRollBack;
                }
                return FailureProcessingResult.Continue;
            }
        }

        private static void AttachSheetCheckboxes(DataGridView grid)
        {
            DataGridViewRow anchor = null;
            DataGridViewRow[] previous = Array.Empty<DataGridViewRow>();
            DataGridViewRow[] pending = Array.Empty<DataGridViewRow>();
            DataGridViewRow[] Selected() => grid.SelectedRows.Cast<DataGridViewRow>().ToArray();
            bool Eligible(DataGridViewRow row) => row.DataGridView == grid && row.Visible && !row.IsNewRow && !row.Cells[0].ReadOnly;
            void Toggle(DataGridViewRow source, IEnumerable<DataGridViewRow> targets)
            {
                if (!Eligible(source)) return;
                bool value = !Convert.ToBoolean(source.Cells[0].Value);
                foreach (var row in targets.Where(Eligible)) row.Cells[0].Value = value;
            }
            grid.MultiSelect = true;
            grid.SelectionChanged += (sender, args) =>
            {
                var rows = Selected();
                if (rows.Length > 1) previous = rows;
            };
            grid.MouseDown += (sender, args) =>
            {
                var hit = grid.HitTest(args.X, args.Y);
                if (args.Button != MouseButtons.Left || hit.ColumnIndex != 0 || hit.RowIndex < 0)
                {
                    previous = pending = Array.Empty<DataGridViewRow>();
                    return;
                }
                var row = grid.Rows[hit.RowIndex];
                var selection = Selected();
                pending = selection.Length > 1 && selection.Contains(row) ? selection :
                    previous.Contains(row) ? previous : new[] { row };
            };
            grid.CellBeginEdit += (sender, args) => { if (args.ColumnIndex == 0) args.Cancel = true; };
            grid.CellMouseClick += (sender, args) =>
            {
                if (args.Button != MouseButtons.Left || args.ColumnIndex != 0 || args.RowIndex < 0) return;
                var row = grid.Rows[args.RowIndex];
                var targets = pending.Contains(row) ? pending : new[] { row };
                if ((System.Windows.Forms.Control.ModifierKeys & Keys.Shift) != 0 && anchor?.DataGridView == grid)
                    targets = Enumerable.Range(Math.Min(anchor.Index, row.Index), Math.Abs(anchor.Index - row.Index) + 1)
                        .Select(index => grid.Rows[index]).ToArray();
                Toggle(row, targets);
                foreach (var target in targets.Where(Eligible)) target.Selected = true;
                anchor = row;
                pending = Array.Empty<DataGridViewRow>();
            };
            grid.KeyDown += (sender, args) =>
            {
                if (args.KeyCode != Keys.Space || grid.CurrentCell?.ColumnIndex != 0) return;
                var selection = Selected();
                var row = grid.CurrentRow;
                Toggle(row, selection.Contains(row) ? selection : new[] { row });
                args.Handled = args.SuppressKeyPress = true;
            };
            grid.CellPainting += (sender, args) =>
            {
                if (args.RowIndex < 0 || args.ColumnIndex != 0) return;
                args.Paint(args.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border |
                    DataGridViewPaintParts.SelectionBackground | DataGridViewPaintParts.Focus);
                bool selected = Convert.ToBoolean(grid.Rows[args.RowIndex].Cells[0].Value);
                int size = Math.Max(13, (int)Math.Round(args.Graphics.DpiX * 15 / 96));
                var box = new System.Drawing.Rectangle(args.CellBounds.Left + (args.CellBounds.Width - size) / 2,
                    args.CellBounds.Top + (args.CellBounds.Height - size) / 2, size, size);
                using (var fill = new SolidBrush(selected ? Color.FromArgb(217, 169, 51) : Color.White))
                using (var border = new Pen(Color.FromArgb(118, 126, 134)))
                {
                    args.Graphics.FillRectangle(fill, box);
                    args.Graphics.DrawRectangle(border, box);
                }
                if (selected)
                    using (var tick = new Pen(Color.Black, Math.Max(1.5f, size / 8f)))
                        args.Graphics.DrawLines(tick, new[] {
                            new System.Drawing.Point(box.Left + size / 4, box.Top + size / 2),
                            new System.Drawing.Point(box.Left + size * 7 / 16, box.Top + size * 3 / 4),
                            new System.Drawing.Point(box.Left + size * 4 / 5, box.Top + size / 4) });
                args.Handled = true;
            };
        }

        private static bool PlacementSearch(string query, params string[] fields)
            => fields.Any(field => field.IndexOf(query.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0);

        private static string PlacementViewTypeLabel(ViewType type)
        {
            string name = type.ToString();
            switch (name)
            {
                case "ThreeD": return "3D";
                case "CeilingPlan": return "Reflected Ceiling Plan";
                case "EngineeringPlan": return "Structural Plan";
                case "AreaPlan": return "Area Plan";
                case "PanelSchedule": return "Panel Schedule";
            }
            if (name == "DraftingView") return "Drafting View";
            return System.Text.RegularExpressions.Regex.Replace(name,
                @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        }

        private static DataGridView PlacementGrid(DataGridView style) => new DataGridView
        {
            Dock = DockStyle.Fill, Font = style.Font, BackgroundColor = Color.White, BorderStyle = style.BorderStyle,
            GridColor = style.GridColor, DefaultCellStyle = style.DefaultCellStyle.Clone(),
            AlternatingRowsDefaultCellStyle = style.AlternatingRowsDefaultCellStyle.Clone(),
            ColumnHeadersDefaultCellStyle = style.ColumnHeadersDefaultCellStyle.Clone(), EnableHeadersVisualStyles = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowTemplate = new DataGridViewRow { Height = style.RowTemplate.Height }
        };

        private static Control PlacementCard(DataGridView style, string heading, string prompt, TextBox search, DataGridView grid, params Control[] options)
        {
            var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12, 10, 12, 10),
                Margin = new Padding(5), BackColor = Color.White, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
            card.Paint += (sender, args) =>
            {
                int right = card.ClientSize.Width - 1, bottom = card.ClientSize.Height - 1;
                if (right < 12 || bottom < 12) return;
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                using (var pen = new Pen(Color.FromArgb(210, 215, 220)))
                {
                    const int d = 12;
                    path.AddArc(0, 0, d, d, 180, 90);
                    path.AddArc(right - d, 0, d, d, 270, 90);
                    path.AddArc(right - d, bottom - d, d, d, 0, 90);
                    path.AddArc(0, bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    args.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    args.Graphics.DrawPath(pen, path);
                }
            };
            card.Resize += (sender, args) => card.Invalidate();
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var title = new Label { Text = heading, Font = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font,
                ForeColor = style.ForeColor, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            card.Controls.Add(title, 0, 0);
            card.Controls.Add(new Label { Text = prompt, Font = grid.Font, ForeColor = style.ForeColor,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            search.Font = new Font("Segoe UI", 8f);
            search.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            BIMlify.SearchControls.SearchGlyph.Attach(search);
            card.Controls.Add(search, 0, 2);
            card.Controls.Add(grid, 0, 3);
            for (int index = 0; index < options.Length; index++)
            {
                var option = options[index];
                option.Font = grid.Font;
                option.Margin = new Padding(3, 5, 3, 3);
                option.ForeColor = style.ForeColor;
                option.Dock = DockStyle.Fill;
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(option, 0, 4 + index);
            }
            return card;
        }

        private static IEnumerable<Element> PlacementInstances(Document doc, View source)
        {
            if (source == null) return Enumerable.Empty<Element>();
            if (source is PanelScheduleView)
                return new FilteredElementCollector(doc).OfClass(typeof(PanelScheduleSheetInstance)).Cast<PanelScheduleSheetInstance>()
                    .Where(instance => instance.ScheduleId == source.Id);
            if (source is ViewSchedule)
                return new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                    .Where(instance => instance.ScheduleId == source.Id || IsPlacementCopy(instance, source));
            return new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
                .Where(instance => instance.ViewId == source.Id || IsPlacementCopy(instance, source));
        }

        private static bool IsPlacementCopy(Element instance, View source)
        {
            var schema = Schema.Lookup(PlacementSchemaId);
            if (schema == null) return false;
            var entity = instance.GetEntity(schema);
            return entity.IsValid() && entity.Get<string>("SourceView") == source.UniqueId;
        }

        private static PlacementReference ReferencePlacement(Element instance)
        {
            if (instance is Viewport viewport) return new PlacementReference { Point = viewport.GetBoxCenter() };
            if (instance is ScheduleSheetInstance schedule) return new PlacementReference { Point = schedule.Point, Segment = schedule.SegmentIndex };
            return new PlacementReference { Point = ((PanelScheduleSheetInstance)instance).Origin };
        }

        private static ElementId PlaceViewOnSheet(Document doc, ViewSheet sheet, View source, PlacementReference reference, bool detailing)
        {
            var outline = sheet.Outline;
            var center = new XYZ((outline.Min.U + outline.Max.U) / 2, (outline.Min.V + outline.Max.V) / 2, 0);
            XYZ point = reference.Point ?? center;
            var occupied = new FilteredElementCollector(doc).OwnedByView(sheet.Id).WhereElementIsNotElementType()
                .Where(element => element is Viewport || element is ScheduleSheetInstance || element is PanelScheduleSheetInstance)
                .Select(element => element.get_BoundingBox(sheet)).Where(box => box != null).ToList();
            Element placed;
            if (source is PanelScheduleView)
            {
                var panel = PanelScheduleSheetInstance.Create(doc, source.Id, sheet);
                panel.Origin = point;
                placed = panel;
            }
            else if (source is ViewSchedule)
            {
                placed = reference.Segment < 0 ? ScheduleSheetInstance.Create(doc, sheet.Id, source.Id, point) :
                    ScheduleSheetInstance.Create(doc, sheet.Id, source.Id, point, reference.Segment);
            }
            else
            {
                ElementId viewId = source.Id;
                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
                {
                    var option = detailing && source.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing)
                        ? ViewDuplicateOption.WithDetailing : ViewDuplicateOption.Duplicate;
                    if (!source.CanViewBeDuplicated(option))
                        throw new InvalidOperationException("Revit cannot place or duplicate this view for this sheet.");
                    viewId = source.Duplicate(option);
                    if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
                        throw new InvalidOperationException("Revit cannot place this copy on the target sheet.");
                }
                placed = Viewport.Create(doc, sheet.Id, viewId, point);
            }
            doc.Regenerate();
            FitPlacement(doc, sheet, placed, point, reference.Point != null, occupied);
            var schema = Schema.Lookup(PlacementSchemaId);
            if (schema == null)
            {
                var builder = new SchemaBuilder(PlacementSchemaId);
                builder.SetSchemaName("BIMlifyViewPlacement");
                builder.AddSimpleField("SourceView", typeof(string));
                schema = builder.Finish();
            }
            var entity = new Entity(schema);
            entity.Set("SourceView", source.UniqueId);
            placed.SetEntity(entity);
            return placed.Id;
        }

        private static void FitPlacement(Document doc, ViewSheet sheet, Element placed, XYZ origin, bool exact, List<BoundingBoxXYZ> occupied)
        {
            var bounds = placed.get_BoundingBox(sheet);
            var outline = sheet.Outline;
            if (bounds == null || outline.Max.U <= outline.Min.U || outline.Max.V <= outline.Min.V) return;
            const double tolerance = 0.001;
            bool Fits(XYZ delta)
            {
                double left = bounds.Min.X + delta.X, right = bounds.Max.X + delta.X;
                double bottom = bounds.Min.Y + delta.Y, top = bounds.Max.Y + delta.Y;
                return left >= outline.Min.U - tolerance && right <= outline.Max.U + tolerance &&
                    bottom >= outline.Min.V - tolerance && top <= outline.Max.V + tolerance &&
                    !occupied.Any(box => left < box.Max.X - tolerance && right > box.Min.X + tolerance &&
                        bottom < box.Max.Y - tolerance && top > box.Min.Y + tolerance);
            }
            if (Fits(XYZ.Zero)) return;
            if (!exact)
            {
                // Search free rectangles using the sheet and neighboring viewport edges.
                var xs = occupied.Select(box => box.Max.X + 0.02).Prepend(outline.Min.U + 0.02).Distinct().OrderBy(x => x);
                var ys = occupied.Select(box => box.Min.Y - 0.02).Prepend(outline.Max.V - 0.02).Distinct().OrderByDescending(y => y);
                foreach (double y in ys)
                    foreach (double x in xs)
                    {
                        var delta = new XYZ(x - bounds.Min.X, y - bounds.Max.Y, 0);
                        if (!Fits(delta)) continue;
                        if (placed is Viewport viewport) viewport.SetBoxCenter(origin + delta);
                        else if (placed is ScheduleSheetInstance schedule) schedule.Point = origin + delta;
                        else ((PanelScheduleSheetInstance)placed).Origin = origin + delta;
                        return;
                    }
            }
            throw new InvalidOperationException(exact ? "Reference position is outside the sheet or overlaps another placement." :
                "No free area found for this view on the target sheet.");
        }
    }
}
