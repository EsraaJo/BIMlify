using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Color = System.Drawing.Color;

namespace BIMlify.CleanerUi
{
    public static partial class SheetOrganization191
    {
        private static Button PopulateSheetCreation(TabPage page, DataGridView style, Autodesk.Revit.UI.UIDocument ui, out Button checkNumbers)
        {
            Document doc = ui.Document;
            var grid = PlacementGrid(style);
            grid.ReadOnly = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.MultiSelect = true;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.BorderStyle = BorderStyle.None;

            DashboardEnhancements.AttachGridEditEscape(grid);
            grid.AllowUserToAddRows = true;
            grid.AllowUserToDeleteRows = true;
            grid.Columns.Add("Number", "Sheet number");
            grid.Columns.Add("Name", "Sheet name");
            var titleBlocks = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType().OfType<FamilySymbol>()
                .OrderBy(symbol => symbol.FamilyName).ThenBy(symbol => symbol.Name).ToList();
            var choices = new Dictionary<string, ElementId> { { "No title block", ElementId.InvalidElementId } };
            foreach (var symbol in titleBlocks)
            {
                string name = symbol.FamilyName + " — " + symbol.Name;
                if (choices.ContainsKey(name)) name += " [" + symbol.Id.Value + "]";
                choices.Add(name, symbol.Id);
            }
            var blocks = new DataGridViewComboBoxColumn { Name = "Block", HeaderText = "Title block", FlatStyle = FlatStyle.Flat };
            blocks.Items.AddRange(choices.Keys.Cast<object>().ToArray());
            grid.Columns.Add(blocks);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", ReadOnly = true });
            AttachBatchSheetNames(grid, "Name");
            grid.Columns["Number"].FillWeight = 18;
            grid.Columns["Name"].FillWeight = 37;
            grid.Columns["Block"].FillWeight = 25;
            grid.Columns["Status"].FillWeight = 20;
            foreach (DataGridViewColumn column in grid.Columns)
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns["Status"].DefaultCellStyle.ForeColor = Color.FromArgb(103, 115, 127);
            grid.CellValueChanged += (sender, args) =>
            {
                if (args.RowIndex >= 0 && (args.ColumnIndex == 0 || args.ColumnIndex == 1))
                    grid.Rows[args.RowIndex].Cells["Status"].Value = null;
            };
            grid.DefaultValuesNeeded += (sender, args) => args.Row.Cells["Block"].Value = "No title block";
            var heading = CreateSheetTabDescription("Enter a unique sheet number and name. The title block is optional.");
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = Math.Max(30, style.ColumnHeadersHeight);
            var create = DashboardEnhancements.CreateSettingsStyleButton("Create Sheets", DialogResult.None);
            create.BackColor = Color.FromArgb(250, 230, 168);
            var check = DashboardEnhancements.CreateSettingsStyleButton("Check Numbers", DialogResult.None);

            checkNumbers = check;
            page.Controls.Add(grid);

            page.Controls.Add(heading);

            List<DataGridViewRow> Validate()
            {
                if (!grid.EndEdit()) return new List<DataGridViewRow>();
                var rows = grid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow && !row.ReadOnly &&
                    (Text(row, "Number").Length > 0 || Text(row, "Name").Length > 0)).ToList();
                var existing = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>().Select(sheet => sheet.SheetNumber), StringComparer.OrdinalIgnoreCase);
                var repeated = new HashSet<string>(rows.GroupBy(row => Text(row, "Number"), StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1).Select(group => group.Key), StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                {
                    string number = Text(row, "Number");
                    row.Cells["Status"].Value = number.Length == 0 ? "Sheet number required" :
                        Text(row, "Name").Length == 0 ? "Sheet name required" :
                        existing.Contains(number) ? "Number already exists" : repeated.Contains(number) ? "Duplicate in this list" : "Ready";
                }
                int ready = rows.Count(row => Text(row, "Status") == "Ready");
                heading.Text = rows.Count == 0 ? "Enter a sheet number and name in a new row." :
                    ready + " of " + rows.Count + " sheets ready. Review the Status column.";
                return rows;
            }

            grid.CellDoubleClick += (sender, args) => { int rowIndex = args.RowIndex; SheetWorkspace.Run(grid, () =>
            {
                if (rowIndex < 0 || !(grid.Rows[rowIndex].Tag is ElementId id)) return;
                if (!(doc.GetElement(id) is ViewSheet sheet))
                {
                    heading.Text = "The created sheet is no longer available in this project.";
                    return;
                }
                try
                {
                    ui.RequestViewChange(sheet);

                }
                catch (Exception error) { heading.Text = error.Message; }
            }); };

            check.Click += (sender, args) => SheetWorkspace.Run(grid, () => Validate());
            create.Click += (sender, args) => SheetWorkspace.Run(grid, () =>
            {
                var rows = Validate();
                if (rows.Count == 0 || rows.Any(row => Text(row, "Status") != "Ready")) return;
                try
                {
                    var created = new List<ElementId>();
                    using (var transaction = new Transaction(doc, "BIMlify — Create Sheets"))
                    {
                        transaction.Start();
                        var failures = new PlacementFailures();
                        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                            .SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                        foreach (var row in rows)
                        {
                            if (!choices.TryGetValue(Text(row, "Block"), out ElementId block)) block = ElementId.InvalidElementId;
                            var sheet = ViewSheet.Create(doc, block);
                            sheet.SheetNumber = Text(row, "Number");
                            sheet.Name = Text(row, "Name");
                            created.Add(sheet.Id);
                        }
                        if (transaction.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException(failures.Error ?? "Revit could not create the sheets.");
                    }
                    for (int index = 0; index < rows.Count; index++)
                    {
                        var row = rows[index];
                        row.Tag = created[index];
                        row.Cells["Status"].Value = "Created successfully";
                        row.Cells["Status"].ToolTipText = "Double-click to open this sheet in Revit.";
                        row.Cells["Status"].Style.ForeColor = Color.FromArgb(36, 112, 65);
                        row.ReadOnly = true;
                    }
                    heading.Text = rows.Count + " sheets created. Double-click a created row to open its sheet in Revit.";
                }
                catch (Exception error) { heading.Text = error.Message; }
            });
            return create;
        }

        private static string Text(DataGridViewRow row, string column)
            => Convert.ToString(row.Cells[column].Value).Trim();
    }
}
