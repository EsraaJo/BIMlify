using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Form = System.Windows.Forms.Form;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;

namespace BIMlify.CleanerUi
{
    public static partial class SheetOrganization191
    {
        public static void Attach(Form form, UIDocument ui)
        {
            if (ui == null || form == null) return;
            var original = form.Controls.OfType<System.Windows.Forms.Panel>()
                .SelectMany(panel => panel.Controls.OfType<DataGridView>())
                .FirstOrDefault(grid => grid.Columns.Cast<DataGridViewColumn>().Any(column => column.HeaderText == "Preview Number"));
            if (original == null) return;
            SheetWorkspace.Attach(form, ui);
            var host = original.Parent;
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(170, 32),
                Padding = new Point(12, 4),
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(238, 241, 245)
            };
            tabs.DrawItem += (sender, args) =>
            {
                if (args.Index < 0 || args.Index >= tabs.TabPages.Count) return;
                Rectangle bounds = args.Bounds;
                bool selected = args.Index == tabs.SelectedIndex;
                using (var fill = new SolidBrush(selected ? Color.White : Color.FromArgb(222, 226, 231)))
                    args.Graphics.FillRectangle(fill, bounds);
                using (var outline = new Pen(Color.FromArgb(161, 169, 178)))
                    args.Graphics.DrawRectangle(outline, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
                using (var font = new Font(tabs.Font, selected ? FontStyle.Bold : FontStyle.Regular))
                    TextRenderer.DrawText(args.Graphics, tabs.TabPages[args.Index].Text, font, Rectangle.Inflate(bounds, -6, -2),
                        Color.FromArgb(67, 76, 86), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                if (selected)
                    using (var accent = new Pen(Color.FromArgb(220, 177, 82), 2f))
                        args.Graphics.DrawLine(accent, bounds.Left + 1, bounds.Bottom - 2, bounds.Right - 2, bounds.Bottom - 2);
            };
            var organization = new TabPage("Sheets organization") { BackColor = System.Drawing.Color.White };
            var tools = new TabPage("Edit/copy sheets") { BackColor = System.Drawing.Color.White, Padding = new Padding(6) };
            var revisions = Revision.GetAllRevisionIds(ui.Document)
                .Select(id => ui.Document.GetElement(id) as Revision)
                .Where(revision => revision != null)
                .OrderBy(revision => revision.SequenceNumber)
                .ToList();
            var revisionChoices = revisions.GroupBy(RevisionLabel, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Id, StringComparer.Ordinal);
            const string noRevision = "(No revision)";
            revisionChoices[noRevision] = ElementId.InvalidElementId;
            Action saveCopyTab = PopulateTools(form, tools, original, ui.Document, revisionChoices, noRevision,
                out var copyActions, out var duplicateViews);
            host.Controls.Remove(original);
            organization.Controls.Add(original);
            tabs.TabPages.Add(organization);
            tabs.TabPages.Add(tools);
            var creation = new TabPage("Create Sheets") { BackColor = Color.White, Padding = new Padding(6) };
            tabs.TabPages.Add(creation);
            Button createAction = PopulateSheetCreation(creation, original, ui, out var checkNumbers);
            var placement = new TabPage("Place Views on Sheets") { BackColor = Color.White, Padding = new Padding(8) };
            tabs.TabPages.Add(placement);
            organization.Controls.Add(CreateSheetTabDescription("Edit sheet names and numbers in the table, then click Save Changes."));
            tools.Controls.Add(CreateSheetTabDescription("Edit sheet details or select the sheets you want to copy."));
            Button[] placementActions = null;
            host.Controls.Add(tabs);
            var apply = form.Controls.Cast<System.Windows.Forms.Control>().SelectMany(Descendants)
                .OfType<Button>().FirstOrDefault(button => button.Text == "Apply Organization");
            if (apply == null) return;
            var footer = apply.Parent;
            var save = DashboardEnhancements.CreateSettingsStyleButton("Save Changes", DialogResult.None);
            save.Name = "BIMlifyConfirmContinue";
            save.BackColor = Color.FromArgb(250, 230, 168);
            save.Bounds = apply.Bounds;
            save.Anchor = apply.Anchor;
            footer.Controls.Add(save);
            apply.Visible = false;
            save.BringToFront();
            var cancel = footer.Controls.OfType<Button>().FirstOrDefault(button => button.DialogResult == DialogResult.Cancel || button.Text == "Cancel");
            if (cancel != null) cancel.Click += (sender, args) => form.Close();
            if (cancel != null && cancel.Left < save.Left)
            {
                int right = save.Right;
                save.Left = cancel.Left;
                cancel.Left = right - cancel.Width;
            }
            void MoveActionToFooter(Button button)
            {
                button.Dock = DockStyle.None;
                button.Bounds = save.Bounds;
                button.Anchor = save.Anchor;
                footer.Controls.Add(button);
                button.Visible = false;
                button.BringToFront();
            }
            MoveActionToFooter(createAction);
            MoveActionToFooter(checkNumbers);
            foreach (var button in copyActions) MoveActionToFooter(button);
            var filterLabel = Descendants(form).OfType<Label>().FirstOrDefault(label => label.Text == "Filter sheets");
            if (filterLabel?.Parent != null) filterLabel.Parent.Hide();
            const int buttonGap = FooterCloseOrder.ButtonGap;
            const int standardWidth = 96;
            var actionPanel = footer.Parent;
            var loadedStatus = actionPanel.Controls.OfType<Label>().FirstOrDefault(label => label.Dock == DockStyle.Left);
            if (loadedStatus != null) loadedStatus.Width = 150;
            var all = DashboardEnhancements.CreateSettingsStyleButton("Select all", DialogResult.None);
            var none = DashboardEnhancements.CreateSettingsStyleButton("Clear Selection", DialogResult.None);
            all.SetBounds(174, 18, 96, 30);
            none.SetBounds(all.Right + buttonGap, all.Top, all.Width, all.Height);
            footer.Controls.Add(all); footer.Controls.Add(none);
            all.BringToFront(); none.BringToFront();
            all.Click += (sender, args) => SetChecked(original, true);
            none.Click += (sender, args) => SetChecked(original, false);
            // The original browser-group column often contains an unresolved "???".
            // Reuse that space for a useful, project-backed revision field instead.
            var revisionColumn = new DataGridViewComboBoxColumn
            {
                Name = "CurrentRevision",
                HeaderText = "Current revision",
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat,
                DataSource = revisionChoices.Keys.OrderBy(text => text == noRevision ? "" : text).ToArray(),
                Width = original.Columns[1].Width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            original.Columns.RemoveAt(1);
            original.Columns.Insert(1, revisionColumn);
            original.Columns[2].HeaderText = "Number preview";
            original.Columns[2].HeaderCell.ToolTipText = "Reflects the sheet number entered in this row.";
            original.Columns[3].HeaderCell.ToolTipText = "Edit this number and click Save Changes.";
            var instruction = Descendants(form).OfType<Label>().FirstOrDefault(label => label.Text.StartsWith("Group Code ="));
            if (instruction != null) instruction.Text = "Edit sheet numbers in the table, then click Save Changes.";
            original.Columns[3].ReadOnly = false;
            foreach (DataGridViewRow row in original.Rows)
            {
                row.Cells[0].Value = false;
                row.Cells[1].Value = row.Tag is ViewSheet sheet ? CurrentRevisionLabel(sheet, ui.Document, noRevision) : noRevision;
            }
            original.ClearSelection();
            original.CellFormatting += (sender, args) =>
            {
                if (args.RowIndex >= 0 && args.ColumnIndex == 2)
                    args.Value = original.Rows[args.RowIndex].Cells[3].Value;
            };
            original.CellDoubleClick += (sender, args) =>
            {
                if (args.RowIndex >= 0 && (args.ColumnIndex == 1 || args.ColumnIndex == 3 || args.ColumnIndex == 4))
                {
                    original.CurrentCell = original.Rows[args.RowIndex].Cells[args.ColumnIndex];
                    original.BeginEdit(true);
                }
            };
            AttachSheetCheckboxes(original);
            DashboardEnhancements.AttachGridEditEscape(original);
            DataGridViewRow[] batchRows = Array.Empty<DataGridViewRow>();
            var readSelection = TrackBatchSelection(original, 1, 3, 4);
            string originalValue = null;
            original.CellBeginEdit += (sender, args) =>
            {
                var edited = original.Rows[args.RowIndex];
                originalValue = Convert.ToString(edited.Cells[args.ColumnIndex].Value);
                batchRows = readSelection(edited, args.ColumnIndex)
                    .Where(row => row.Visible && row.Tag is ViewSheet).ToArray();
                if (batchRows.Length <= 1 && Convert.ToBoolean(edited.Cells[0].Value))
                    batchRows = original.Rows.Cast<DataGridViewRow>()
                        .Where(row => row.Visible && row.Tag is ViewSheet && Convert.ToBoolean(row.Cells[0].Value)).ToArray();
            };
            original.CellEndEdit += (sender, args) =>
            {
                if (!DashboardEnhancements.IsCancellingGridEdit(original) && args.RowIndex >= 0 && (args.ColumnIndex == 1 || args.ColumnIndex == 3 || args.ColumnIndex == 4))
                {
                    var edited = original.Rows[args.RowIndex];
                    string value = Convert.ToString(edited.Cells[args.ColumnIndex].Value);
                    if (String.Equals(originalValue, value, StringComparison.Ordinal)) return;
                    if (batchRows.Length > 1 && batchRows.Contains(edited))
                    {
                        if (args.ColumnIndex == 1 || args.ColumnIndex == 4)
                        {
                            foreach (var row in batchRows) { row.Cells[args.ColumnIndex].Value = value; row.Cells[0].Value = true; }
                        }
                        else
                        {
                            var suffix = System.Text.RegularExpressions.Regex.Match(value, "[0-9]+$");
                            if (!suffix.Success || !long.TryParse(suffix.Value, out long seed) ||
                                seed < Array.IndexOf(batchRows, edited) || seed > long.MaxValue - batchRows.Length)
                            {
                                edited.Cells[3].Value = originalValue;
                                DashboardEnhancements.ShowSettingsStyleMessage(form, "Smart Sheets",
                                    "For a group edit, enter a sheet number ending in digits, such as A-101.");
                                return;
                            }
                            long start = seed - Array.IndexOf(batchRows, edited);
                            for (int index = 0; index < batchRows.Length; index++)
                            {
                                batchRows[index].Cells[3].Value = value.Substring(0, suffix.Index) +
                                    (start + index).ToString("D" + suffix.Length, System.Globalization.CultureInfo.InvariantCulture);
                                batchRows[index].Cells[0].Value = true;
                            }
                        }
                        original.Invalidate();
                    }
                    edited.Cells[0].Value = true;
                    original.InvalidateRow(args.RowIndex);
                }
            };
            save.Click += (sender, args) => SheetWorkspace.Run(original, () =>
            {
                if (tabs.SelectedTab == placement || tabs.SelectedTab == creation) return;
                if (tabs.SelectedTab == tools) { saveCopyTab(); return; }
                if (!original.EndEdit()) return;
                try
                {
                    SaveSheetRows(ui.Document, original.Rows.Cast<DataGridViewRow>()
                        .Where(row => Convert.ToBoolean(row.Cells[0].Value)), 3, 4);
                    SaveSheetRevisions(ui.Document, original.Rows.Cast<DataGridViewRow>()
                        .Where(row => Convert.ToBoolean(row.Cells[0].Value)), 1, revisionChoices, noRevision);
                    if (loadedStatus != null)
                    {
                        loadedStatus.ForeColor = Color.FromArgb(39, 125, 72);
                        loadedStatus.Text = "Sheet changes saved.";
                    }
                }
                catch (Exception error) { DashboardEnhancements.ShowSettingsStyleMessage(form, "Smart Sheets", error.Message); }
            });
            void LayoutActions()
            {
                bool placing = tabs.SelectedTab == placement;
                bool copying = tabs.SelectedTab == tools;
                bool creating = tabs.SelectedTab == creation;
                var buttons = new List<Button>();
                if (tabs.SelectedTab == organization) buttons.AddRange(new[] { all, none });
                if (copying) buttons.AddRange(copyActions);
                if (creating) buttons.AddRange(new[] { checkNumbers, createAction });
                else if (placing && placementActions != null) buttons.AddRange(placementActions);
                else buttons.Add(save);
                if (cancel != null) buttons.Add(cancel);

                var widths = buttons.Select(button => Math.Max(standardWidth,
                    TextRenderer.MeasureText(button.Text, button.Font).Width + 24)).ToArray();
                int rightInset = Math.Max(0, FooterCloseOrder.RightInset - actionPanel.Padding.Right);
                int totalWidth = widths.Sum() + buttonGap * (buttons.Count - 1) + rightInset;
                if (footer.Width != totalWidth) footer.Width = totalWidth;
                int x = footer.ClientSize.Width - totalWidth;
                for (int i = 0; i < buttons.Count; i++)
                {
                    buttons[i].Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    buttons[i].SetBounds(x, Math.Max(0, (footer.ClientSize.Height - 30) / 2), widths[i], 30);
                    x += widths[i] + buttonGap;
                }
                foreach (var button in footer.Controls.OfType<Button>())
                    button.Visible = buttons.Contains(button);



            }
            actionPanel.SizeChanged += (sender, args) => LayoutActions();
            form.Shown += (sender, args) => LayoutActions();
            tabs.SelectedIndexChanged += (sender, args) =>
            {
                tabs.Invalidate();
                all.Visible = none.Visible = tabs.SelectedTab == organization;
                bool placing = tabs.SelectedTab == placement;
                bool copying = tabs.SelectedTab == tools;
                if (placing && placementActions == null)
                {
                    placementActions = PopulateViewPlacement(placement, original, ui);
                    foreach (Button button in placementActions) MoveActionToFooter(button);
                }
                LayoutActions();
                if (tabs.SelectedTab != organization) return;
                foreach (DataGridViewRow row in original.Rows)
                    if (row.Tag is ViewSheet sheet && sheet.IsValidObject && !Convert.ToBoolean(row.Cells[0].Value))
                        row.Cells[3].Value = sheet.SheetNumber;
            };
        }

        private static Func<DataGridViewRow, int, DataGridViewRow[]> TrackBatchSelection(DataGridView grid, params int[] editableColumns)
        {

            DataGridViewRow[] Selected(int column) => grid.SelectedRows.Cast<DataGridViewRow>()
                .Concat(grid.SelectedCells.Cast<DataGridViewCell>().Where(cell => cell.ColumnIndex == column)
                    .Select(cell => cell.OwningRow))
                .Where(row => row.Visible && !row.IsNewRow).Distinct().OrderBy(row => row.Index).ToArray();

            var groups = editableColumns.ToDictionary(column => column, column => Array.Empty<DataGridViewRow>());
            grid.SelectionChanged += (sender, args) =>
            {
                foreach (int column in editableColumns)
                {
                    var selected = Selected(column);
                    if (selected.Length > 1 || selected.Length == 0 ||
                        !groups[column].Contains(selected[0]))
                        groups[column] = selected;
                }
            };
            return (row, column) =>
            {
                if (!groups.TryGetValue(column, out var group)) return Array.Empty<DataGridViewRow>();
                var selected = Selected(column);
                return selected.Length > 1 ? selected :
                    group.Contains(row) ? group.Where(item => item.DataGridView == grid && item.Visible).ToArray() : selected;
            };
        }

        private static void AttachBatchSheetNames(DataGridView grid, string column)
        {
            int nameIndex = grid.Columns[column].Index;
            var readSelection = TrackBatchSelection(grid, nameIndex);
            DataGridViewRow[] targets = Array.Empty<DataGridViewRow>();
            string before = null;
            grid.CellBeginEdit += (sender, args) =>
            {
                if (args.ColumnIndex != nameIndex) return;
                var row = grid.Rows[args.RowIndex];
                before = Convert.ToString(row.Cells[nameIndex].Value);
                targets = readSelection(row, nameIndex);
            };
            grid.CellEndEdit += (sender, args) =>
            {
                if (args.ColumnIndex != nameIndex || args.RowIndex < 0 ||
                    DashboardEnhancements.IsCancellingGridEdit(grid)) return;
                var row = grid.Rows[args.RowIndex];
                string name = Convert.ToString(row.Cells[nameIndex].Value);
                if (name == before || !targets.Contains(row)) return;
                foreach (var target in targets.Where(target => target != row && target.DataGridView == grid && target.Visible && !target.Cells[nameIndex].ReadOnly))
                    target.Cells[nameIndex].Value = name;
            };
        }

        private static readonly Font SheetDescriptionFont = new Font("Segoe UI", 8f);

        private static Label CreateSheetTabDescription(string text) => new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(8, 0, 8, 0),
            Font = SheetDescriptionFont,
            ForeColor = Color.FromArgb(102, 109, 118),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        private static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control control)
        {
            yield return control;
            foreach (System.Windows.Forms.Control child in control.Controls)
                foreach (var item in Descendants(child)) yield return item;
        }

        private static string DisplayBrowserGroup(BrowserOrganization browser, ViewSheet sheet) =>
            browser == null ? "" : String.Join(" / ", browser.GetFolderItems(sheet.Id)
                .Select(folder => (folder.Name ?? "").Trim())
                .Where(name => name.Length > 0 && name.Any(character => character != '?')));

        private static Action PopulateTools(Form form, TabPage page, DataGridView original, Document doc,
            IReadOnlyDictionary<string, ElementId> revisionChoices, string noRevision,
            out Button[] actions, out CheckBox duplicateViews)
        {
            var browser = BrowserOrganization.GetCurrentBrowserOrganizationForSheets(doc);
            Func<ViewSheet, string> groupPath = sheet => DisplayBrowserGroup(browser, sheet);
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(sheet => !sheet.IsPlaceholder)
                .Select(sheet => new { Sheet = sheet, Group = groupPath(sheet) })
                .OrderBy(item => item.Group, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Sheet.SheetNumber, StringComparer.CurrentCultureIgnoreCase).ToList();
            var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            DashboardEnhancements.AttachGridEditEscape(grid);
            EventHandler matchOriginal = (sender, args) =>
            {
                grid.Font = original.Font;
                grid.EnableHeadersVisualStyles = original.EnableHeadersVisualStyles;
                grid.ColumnHeadersDefaultCellStyle = original.ColumnHeadersDefaultCellStyle.Clone();
                grid.DefaultCellStyle = original.DefaultCellStyle.Clone();
                grid.AlternatingRowsDefaultCellStyle = original.AlternatingRowsDefaultCellStyle.Clone();
                grid.GridColor = original.GridColor;
                grid.BackgroundColor = original.BackgroundColor;
                grid.BorderStyle = original.BorderStyle;
                grid.ColumnHeadersHeight = original.ColumnHeadersHeight;
                grid.RowTemplate.Height = original.RowTemplate.Height;
                foreach (DataGridViewRow row in grid.Rows) row.Height = grid.RowTemplate.Height;
            };
            matchOriginal(form, EventArgs.Empty);
            form.Shown += matchOriginal;
            grid.Columns.Add("Group", "Revit browser group");
            // Do not present the Revit browser path in the editing table: it can be
            // unavailable for a browser organisation and then reads as "???".
            // The value remains attached to each row for copy/group operations.
            grid.Columns["Group"].Visible = false;
            var revisionColumn = new DataGridViewComboBoxColumn
            {
                Name = "CurrentRevision",
                HeaderText = "Current revision",
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat,
                DataSource = revisionChoices.Keys.OrderBy(text => text == noRevision ? "" : text).ToArray(),
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            grid.Columns.Add(revisionColumn);
            grid.Columns.Add("Current", "Current sheet number");
            grid.Columns.Add("Number", "New sheet number");
            grid.Columns.Add("Name", "Sheet name");
            grid.Columns[0].ReadOnly = grid.Columns[2].ReadOnly = true;
            AttachBatchSheetNames(grid, "Name");
            foreach (var item in sheets)
            {
                int index = grid.Rows.Add(item.Group, CurrentRevisionLabel(item.Sheet, doc, noRevision),
                    item.Sheet.SheetNumber, item.Sheet.SheetNumber, item.Sheet.Name);
                grid.Rows[index].Tag = item.Sheet;
            }
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };
            var copy = DashboardEnhancements.CreateSharedStyleButton("Copy Sheets", primary: true);
            copy.BackColor = Color.FromArgb(250, 230, 168);
            copy.Width = 144;
            var withViews = new CheckBox { Text = "Duplicate placed views with detailing", Checked = true, AutoSize = true, Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(78, 85, 92) };
            var move = DashboardEnhancements.CreateSharedStyleButton("Change browser group", primary: true);
            move.BackColor = Color.FromArgb(250, 230, 168);
            move.Width = 150;
            var status = new Label { AutoSize = true, Padding = new Padding(8, 6, 0, 0), Font = new Font("Segoe UI", 8.5f) };
            actions = new[] { copy, move };
            duplicateViews = withViews;
            footer.Controls.Add(status);
            footer.Visible = false;
            status.TextChanged += (sender, args) => footer.Visible = !String.IsNullOrEmpty(status.Text);

            Action reload = () =>
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    var sheet = (ViewSheet)row.Tag;
                    row.Cells[0].Value = groupPath(sheet);
                    row.Cells[1].Value = CurrentRevisionLabel(sheet, doc, noRevision);
                    row.Cells[2].Value = row.Cells[3].Value = sheet.SheetNumber;
                    row.Cells[4].Value = sheet.Name;
                }
            };
            Action save = () =>
            {
                if (!grid.EndEdit()) return;
                try
                {
                    SaveSheetRows(doc, grid.Rows.Cast<DataGridViewRow>(), 3, 4);
                    SaveSheetRevisions(doc, grid.Rows.Cast<DataGridViewRow>(), 1, revisionChoices, noRevision);
                    reload();
                    status.ForeColor = Color.FromArgb(39, 125, 72);
                    status.Text = "Sheet edits saved.";
                }
                catch (Exception error) { DashboardEnhancements.ShowSettingsStyleMessage(form, "Smart Sheets", error.Message); }
            };
            move.Click += (sender, args) => SheetWorkspace.Run(grid, () =>
            {
                if (browser == null || grid.CurrentRow == null) return;
                try
                {
                    var sheet = (ViewSheet)grid.CurrentRow.Tag;
                    var folderIds = new HashSet<ElementId>(browser.GetFolderItems(sheet.Id).Select(folder => folder.ElementId));
                    var fields = sheet.Parameters.Cast<Parameter>().Where(parameter => folderIds.Contains(parameter.Id)
                        && !parameter.IsReadOnly && parameter.StorageType == StorageType.String).ToList();
                    if (fields.Count == 0) throw new InvalidOperationException("This browser group has no writable text grouping parameter.");
                    using (var edit = DashboardEnhancements.CreateSettingsStyleDialog())
                    {
                        edit.Text = "Change Revit browser group";
                        edit.Size = new Size(600, 430);
                        edit.StartPosition = FormStartPosition.CenterParent;
                        edit.Font = new Font("Segoe UI", 8.5f);
                        var body = new System.Windows.Forms.Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(238, 241, 245) };
                        var field = new System.Windows.Forms.ComboBox { Left = 26, Top = 30, Width = 430, DropDownStyle = ComboBoxStyle.DropDownList };
                        var value = new System.Windows.Forms.TextBox { Left = 26, Top = 68, Width = 430 };
                        var bar = new System.Windows.Forms.Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(216, 221, 227) };
                        var buttons = new System.Windows.Forms.Panel { Dock = DockStyle.Right, Width = 208 };
                        var cancel = DashboardEnhancements.CreateSettingsStyleButton("Cancel", DialogResult.Cancel);
                        var applyGroup = DashboardEnhancements.CreateSettingsStyleButton("Apply", DialogResult.OK);
                        cancel.SetBounds(0, 16, 96, 30); applyGroup.SetBounds(112, 16, 96, 30);
                        buttons.Controls.Add(cancel); buttons.Controls.Add(applyGroup); bar.Controls.Add(buttons);
                        field.Items.AddRange(fields.Select(parameter => (object)parameter.Definition.Name).ToArray());
                        field.SelectedIndexChanged += (s, e) => value.Text = fields[field.SelectedIndex].AsString() ?? "";
                        field.SelectedIndex = 0;
                        body.Controls.Add(value); body.Controls.Add(field);
                        DashboardEnhancements.AddSettingsStyleMark(body);
                        edit.Controls.Add(body); edit.Controls.Add(bar);
                        DashboardEnhancements.AddSettingsStyleTitleBar(edit);
                        DashboardEnhancements.ApplySettingsStyleWindowCorners(edit);
                        edit.AcceptButton = applyGroup; edit.CancelButton = cancel;
                        if (edit.ShowDialog(form) != DialogResult.OK) return;
                        Parameter choice = fields[field.SelectedIndex];
                        var targets = grid.SelectedRows.Cast<DataGridViewRow>().Select(row => (ViewSheet)row.Tag).ToList();
                        Commit(doc, "BIMlify change browser group", () =>
                        {
                            foreach (ViewSheet target in targets)
                            {
                                var parameter = target.Parameters.Cast<Parameter>().FirstOrDefault(candidate => candidate.Id == choice.Id);
                                if (parameter == null || parameter.IsReadOnly || !parameter.Set(value.Text))
                                    throw new InvalidOperationException("Revit rejected the group change on " + target.SheetNumber + ".");
                            }
                        });
                        foreach (DataGridViewRow row in grid.Rows) row.Cells[0].Value = groupPath((ViewSheet)row.Tag);
                        grid.Sort(grid.Columns[0], System.ComponentModel.ListSortDirection.Ascending);
                        status.Text = targets.Count + " sheets regrouped";
                    }
                }
                catch (Exception error) { MessageBox.Show(form, error.Message, "Change group"); }
            });
            copy.Click += (sender, args) => SheetWorkspace.Run(grid, () =>
            {
                if (!grid.EndEdit()) return;
                var sources = grid.SelectedRows.Cast<DataGridViewRow>()
                    .OrderBy(row => row.Index).Select(row => row.Tag).OfType<ViewSheet>().ToArray();
                if (sources.Length == 0)
                {
                    status.Text = "Select the sheets to copy.";
                    return;
                }

                var copies = new List<ViewSheet>();
                var failures = new List<string>();
                bool duplicateViewsWithDetailing = withViews.Checked;
                foreach (var source in sources)
                {
                    try
                    {
                        ViewSheet result = null;
                        Commit(doc, "BIMlify copy sheet", () => result = CopySheet(doc, source, duplicateViewsWithDetailing));
                        copies.Add(result);
                    }
                    catch (Exception error) { failures.Add(source.SheetNumber + ": " + error.Message); }
                }

                if (copies.Count > 0) grid.ClearSelection();
                foreach (var sheet in copies)
                {
                    int row = grid.Rows.Add(groupPath(sheet), CurrentRevisionLabel(sheet, doc, noRevision),
                        sheet.SheetNumber, sheet.SheetNumber, sheet.Name);
                    grid.Rows[row].Tag = sheet;
                    grid.Rows[row].Selected = true;
                }
                status.ForeColor = failures.Count == 0 ? Color.FromArgb(39, 125, 72) : Color.FromArgb(155, 75, 42);
                status.Text = copies.Count + " sheets copied.";
                if (failures.Count > 0)
                    DashboardEnhancements.ShowSettingsStyleMessage(form, "Copy sheets",
                        copies.Count + " copied. Could not copy: " + String.Join(" | ", failures));
            });
            page.Controls.Add(grid);
            page.Controls.Add(footer);
            return save;
        }

        private static void SetChecked(DataGridView grid, bool value)
        {
            foreach (DataGridViewRow row in grid.Rows)
                if (row.Visible) row.Cells[0].Value = value;
        }

        private static void SaveSheetRows(Document doc, IEnumerable<DataGridViewRow> rows, int numberColumn, int nameColumn)
        {
            var edits = rows.Where(row => row.Tag is ViewSheet).Select(row => new
            {
                Sheet = (ViewSheet)row.Tag,
                Number = Convert.ToString(row.Cells[numberColumn].Value).Trim(),
                Name = Convert.ToString(row.Cells[nameColumn].Value).Trim()
            }).Where(item => item.Number != item.Sheet.SheetNumber || item.Name != item.Sheet.Name).ToList();
            if (edits.Count == 0) return;
            if (edits.Any(item => item.Number.Length == 0 || item.Name.Length == 0))
                throw new InvalidOperationException("Sheet number and name are required.");
            Commit(doc, "BIMlify edit sheets", () =>
            {
                var renumber = edits.Where(item => item.Number != item.Sheet.SheetNumber).ToList();
                foreach (var item in renumber) item.Sheet.SheetNumber = "BIMlify-" + Guid.NewGuid().ToString("N");
                foreach (var item in renumber) item.Sheet.SheetNumber = item.Number;
                foreach (var item in edits.Where(item => item.Name != item.Sheet.Name)) item.Sheet.Name = item.Name;
            });
        }

        private static string RevisionLabel(Revision revision)
        {
            string number;
            try { number = revision.RevisionNumber; }
            catch { number = revision.SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            string description = (revision.Description ?? "").Trim();
            return String.IsNullOrWhiteSpace(description) ? number : number + " — " + description;
        }

        private static string CurrentRevisionLabel(ViewSheet sheet, Document doc, string empty)
        {
            var current = sheet.GetAllRevisionIds()
                .Select(id => doc.GetElement(id) as Revision)
                .Where(revision => revision != null)
                .OrderBy(revision => revision.SequenceNumber)
                .LastOrDefault();
            return current == null ? empty : RevisionLabel(current);
        }

        // A sheet's displayed current revision is derived by Revit from its revision
        // history.  We therefore append the selected real Revision element without
        // removing revision-cloud history already on that sheet.
        private static void SaveSheetRevisions(Document doc, IEnumerable<DataGridViewRow> rows, int revisionColumn,
            IReadOnlyDictionary<string, ElementId> choices, string empty)
        {
            var changes = rows.Where(row => row.Tag is ViewSheet)
                .Select(row => new { Sheet = (ViewSheet)row.Tag, Choice = Convert.ToString(row.Cells[revisionColumn].Value) })
                .Where(item => !String.IsNullOrWhiteSpace(item.Choice) && item.Choice != empty &&
                    choices.TryGetValue(item.Choice, out var id) && id != ElementId.InvalidElementId &&
                    !item.Sheet.GetAllRevisionIds().Contains(id))
                .ToList();
            if (changes.Count == 0) return;
            Commit(doc, "BIMlify set sheet revisions", () =>
            {
                foreach (var change in changes)
                {
                    ElementId revision = choices[change.Choice];
                    var additional = change.Sheet.GetAdditionalRevisionIds().ToList();
                    if (!additional.Contains(revision))
                    {
                        additional.Add(revision);
                        change.Sheet.SetAdditionalRevisionIds(additional);
                    }
                }
            });
        }

        private static ViewSheet CopySheet(Document doc, ViewSheet source, bool withViews)
        {
            if (withViews)
            {
                foreach (var option in new[] { SheetDuplicateOption.DuplicateSheetWithViewsAndDetailing, SheetDuplicateOption.DuplicateSheetWithViewsOnly })
                    if (source.CanBeDuplicated(option)) return (ViewSheet)doc.GetElement(source.Duplicate(option));
            }
            var detailOption = SheetDuplicateOption.DuplicateSheetWithDetailing;
            var copy = source.CanBeDuplicated(detailOption)
                ? (ViewSheet)doc.GetElement(source.Duplicate(detailOption))
                : CreateSheetShell(doc, source);
            if (!withViews) return copy;
            foreach (ElementId id in source.GetAllViewports())
            {
                var viewport = (Viewport)doc.GetElement(id);
                var view = (Autodesk.Revit.DB.View)doc.GetElement(viewport.ViewId);
                ElementId target = view.Id;
                if (!Viewport.CanAddViewToSheet(doc, copy.Id, target))
                {
                    var option = view.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing) ? ViewDuplicateOption.WithDetailing : ViewDuplicateOption.Duplicate;
                    if (!view.CanViewBeDuplicated(option))
                        throw new InvalidOperationException("View cannot be copied: " + view.Name);
                    target = view.Duplicate(option);
                }
                var placed = Viewport.Create(doc, copy.Id, target, viewport.GetBoxCenter());
                if (placed.GetTypeId() != viewport.GetTypeId()) placed.ChangeTypeId(viewport.GetTypeId());
            }
            foreach (var schedule in new FilteredElementCollector(doc, source.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                if (!schedule.IsTitleblockRevisionSchedule)
                    ScheduleSheetInstance.Create(doc, copy.Id, schedule.ScheduleId, schedule.Point);
            return copy;
        }

        private static ViewSheet CreateSheetShell(Document doc, ViewSheet source)
        {
            ElementId titleBlock = new FilteredElementCollector(doc, source.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType().FirstOrDefault()?.GetTypeId() ?? ElementId.InvalidElementId;
            var copy = ViewSheet.Create(doc, titleBlock);
            copy.Name = source.Name;
            var browser = BrowserOrganization.GetCurrentBrowserOrganizationForSheets(doc);
            if (browser != null)
            {
                var groupIds = new HashSet<ElementId>(browser.GetFolderItems(source.Id).Select(folder => folder.ElementId));
                foreach (Parameter original in source.Parameters)
                {
                    if (!groupIds.Contains(original.Id) || original.StorageType != StorageType.String) continue;
                    Parameter field = copy.Parameters.Cast<Parameter>().FirstOrDefault(parameter => parameter.Id == original.Id);
                    if (field != null && !field.IsReadOnly) field.Set(original.AsString() ?? "");
                }
            }
            return copy;
        }
        private static void Commit(Document doc, string title, Action changes)
        {
            using (var transaction = new Transaction(doc, title))
            {
                transaction.Start();
                changes();
                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit did not commit the sheet changes.");
            }
        }
    }
}




