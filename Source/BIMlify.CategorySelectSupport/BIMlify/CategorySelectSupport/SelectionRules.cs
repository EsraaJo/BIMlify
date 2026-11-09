using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Color = System.Drawing.Color;

namespace BIMlify.CategorySelectSupport;

public static partial class Entry
{
    private static readonly string[] RuleOperators =
    {
        "Equals", "Does not equal", "Contains", "Does not contain", "Starts with",
        "Ends with", "Greater than", "Less than", "Is empty", "Is not empty"
    };

    private sealed class SelectionRule
    {
        internal string Field, Operator, Value;
    }

    private static void AddSelectionRulesTab(TabControl tabs, PickerHandler handler)
    {
        var card = SelectionCard(tabs, "Selection Rules",
            "Use Category, Family, Type, Level, Workset, Element ID, or a Revit parameter name.");
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(10, 58, 10, 8),
            ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var rulesPanel = new System.Windows.Forms.Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 5, 0), Padding = new Padding(4) };
        var actionsPanel = new System.Windows.Forms.Panel { Dock = DockStyle.Fill, Margin = new Padding(5, 0, 0, 0), Padding = new Padding(4) };
        content.Controls.Add(rulesPanel, 0, 0);
        content.Controls.Add(actionsPanel, 1, 0);
        card.Controls.Add(content);
        content.SendToBack();
        var compactFont = new Font("Segoe UI", 8f);
        card.Disposed += (sender, args) => compactFont.Dispose();

        void AddCompactField(System.Windows.Forms.Panel panel, string title, System.Windows.Forms.Control field, int top)
        {
            var label = new Label { Text = title, Left = 10, Top = top + 3, AutoSize = true, Font = compactFont, ForeColor = Ink };
            field.SetBounds(116, top, 210, 25);
            field.Font = compactFont;
            field.AccessibleName = title;
            panel.Controls.Add(label);
            panel.Controls.Add(field);
        }

        var scope = Choices("Active view", "Entire project");
        var logic = Choices("Match all (AND)", "Match any (OR)");
        AddCompactField(rulesPanel, "Search scope", scope, 4);
        AddCompactField(rulesPanel, "Combine rules", logic, 36);
        var grid = new DataGridView
        {
            Left = 10, Top = 68, Height = 124, BackgroundColor = Color.White,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.None, RowHeadersVisible = false, GridColor = Color.FromArgb(220, 224, 229),
            AllowUserToAddRows = true, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = scope.Font, ForeColor = Ink, EnableHeadersVisualStyles = false,
            AccessibleName = "Selection rules"
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 245);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        grid.RowTemplate.Height = 27;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(250, 229, 171);
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.Columns.Add("Field", "Field or parameter name");
        grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Condition", FlatStyle = FlatStyle.Flat, DataSource = RuleOperators });
        grid.Columns.Add("Value", "Value");
        grid.Rows.Add("Category", "Equals", "");
        grid.Width = Math.Max(1, rulesPanel.ClientSize.Width - 20);
        rulesPanel.Controls.Add(grid);
        AddAction(rulesPanel, "Remove rule", 200, () =>
        {
            if (grid.CurrentRow != null && !grid.CurrentRow.IsNewRow) grid.Rows.Remove(grid.CurrentRow);
        });
        var removeRule = rulesPanel.Controls.OfType<Button>().Single();
        rulesPanel.Resize += (sender, args) =>
        {
            grid.SetBounds(10, 68, Math.Max(1, rulesPanel.ClientSize.Width - 20), Math.Max(60, rulesPanel.ClientSize.Height - 112));
            removeRule.Top = grid.Bottom + 8;
        };
        var modes = Choices("Replace selection", "Add matches", "Remove matches", "Invert within scope");
        AddCompactField(actionsPanel, "Selection action", modes, 4);
        var preset = new System.Windows.Forms.TextBox();
        AddCompactField(actionsPanel, "Preset name", preset, 122);

        List<SelectionRule> ReadRules()
        {
            grid.EndEdit();
            return grid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).Select(row => new SelectionRule
            {
                Field = Convert.ToString(row.Cells[0].Value).Trim(),
                Operator = Convert.ToString(row.Cells[1].Value),
                Value = Convert.ToString(row.Cells[2].Value).Trim()
            }).Where(rule => rule.Field.Length > 0 || rule.Operator.Length > 0 || rule.Value.Length > 0).ToList();
        }

        bool ValidateRules(List<SelectionRule> rules)
        {
            if (rules.Count > 0 && rules.All(rule => rule.Field.Length > 0 && RuleOperators.Contains(rule.Operator)
                && (IsEmptyRule(rule.Operator) || rule.Value.Length > 0))) return true;
            handler.SetStatus("Enter a field and condition on every rule, plus a value when required.");
            return false;
        }

        void Run(bool preview, bool isolate = false)
        {
            bool entire = scope.SelectedIndex == 1;
            bool any = logic.SelectedIndex == 1;
            int mode = isolate ? 0 : modes.SelectedIndex;
            var rules = ReadRules();
            if (mode != 3 && !ValidateRules(rules)) return;
            handler.Queue("advanced", ui =>
            {
                var current = new HashSet<ElementId>(ui.Selection.GetElementIds());
                var matches = new HashSet<ElementId>();
                if (isolate && !ui.ActiveView.CanUseTemporaryVisibilityModes())
                    throw new InvalidOperationException("This view does not support temporary isolation. Switch views or use Select.");
                foreach (Element element in Candidates(ui, entire && !isolate))
                {
                    if (mode == 3 ? !current.Contains(element.Id) :
                        any ? rules.Any(rule => MatchesRule(element, rule)) :
                        rules.All(rule => MatchesRule(element, rule)))
                        matches.Add(element.Id);
                }
                if (preview)
                {
                    handler.SetStatus(matches.Count + (mode == 3 ? " element(s) in the inverted selection." : " matching element(s)."));
                    return;
                }
                if (isolate)
                {
                    if (matches.Count == 0) { handler.SetStatus("No matches to isolate in the active view."); return; }
                    using (var tx = new Transaction(ui.Document, "BIMlify — Isolate rule matches"))
                    {
                        tx.Start();
                        ui.ActiveView.IsolateElementsTemporary(matches.ToList());
                        if (tx.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Revit could not isolate the matching elements.");
                    }
                    handler.SetStatus(matches.Count + " element(s) temporarily isolated in the active view.");
                    return;
                }
                if (mode == 1) { current.UnionWith(matches); matches = current; }
                if (mode == 2) { current.ExceptWith(matches); matches = current; }
                ui.Selection.SetElementIds(matches.ToList());
                handler.SetStatus(matches.Count + " element(s) selected in Revit.");
            });
        }

        AddAction(actionsPanel, "Preview Count", 36, () => Run(true), 10);
        AddAction(actionsPanel, "Apply Selection", 36, () => Run(false), 174);
        AddAction(actionsPanel, "Isolate in View", 74, () => Run(false, isolate: true), 10);
        AddAction(actionsPanel, "Save Rule Preset", 156, () =>
        {
            string name = preset.Text.Trim();
            var rules = ReadRules();
            if (name.Length == 0) { handler.SetStatus("Enter a preset name."); return; }
            if (!ValidateRules(rules)) return;
            bool entire = scope.SelectedIndex == 1;
            bool any = logic.SelectedIndex == 1;
            handler.Queue("advanced", ui =>
            {
                string path = RulePresetPath(ui.Document, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    new XElement("SelectionRules", new XAttribute("EntireProject", entire), new XAttribute("MatchAny", any),
                        rules.Select(rule => new XElement("Rule", new XAttribute("Field", rule.Field),
                            new XAttribute("Operator", rule.Operator), new XElement("Value", rule.Value)))).Save(temp);
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                handler.SetStatus("Saved rule preset: " + name + ".");
            });
        });
        AddAction(actionsPanel, "Load Rule Preset", 156, () =>
        {
            string name = preset.Text.Trim();
            if (name.Length == 0) { handler.SetStatus("Enter a preset name."); return; }
            handler.Queue("advanced", ui =>
            {
                string path = RulePresetPath(ui.Document, name);
                if (!File.Exists(path)) { handler.SetStatus("No local preset with that name in this project."); return; }
                var data = XElement.Load(path);
                var rules = data.Elements("Rule").Select(row => new SelectionRule
                {
                    Field = (string)row.Attribute("Field") ?? "", Operator = (string)row.Attribute("Operator") ?? "",
                    Value = (string)row.Element("Value") ?? ""
                }).ToList();
                if (!ValidateRules(rules)) return;
                scope.SelectedIndex = (bool)data.Attribute("EntireProject") ? 1 : 0;
                logic.SelectedIndex = (bool)data.Attribute("MatchAny") ? 1 : 0;
                grid.Rows.Clear();
                foreach (var rule in rules) grid.Rows.Add(rule.Field, rule.Operator, rule.Value);
                handler.SetStatus("Loaded " + rules.Count + " rule(s) from " + name + ".");
            });
        }, 174);
    }

    private static string RulePresetPath(Document doc, string name)
    {
        string project = doc.ProjectInformation?.UniqueId ?? doc.Title;
        using (var hash = SHA256.Create())
        {
            string key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(project + "|" + name.ToUpperInvariant()))).Replace("-", "");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMlify", "SelectionRules", key + ".xml");
        }
    }

    private static bool IsEmptyRule(string op) => op == "Is empty" || op == "Is not empty";

    private static bool MatchesRule(Element element, SelectionRule rule)
    {
        var doc = element.Document;
        string actual;
        switch (rule.Field.ToUpperInvariant())
        {
            case "CATEGORY": actual = element.Category?.Name; break;
            case "FAMILY": actual = (doc.GetElement(element.GetTypeId()) as ElementType)?.FamilyName; break;
            case "TYPE": actual = doc.GetElement(element.GetTypeId())?.Name; break;
            case "LEVEL": actual = doc.GetElement(element.LevelId)?.Name; break;
            case "WORKSET": actual = doc.GetWorksetTable().GetWorkset(element.WorksetId)?.Name; break;
            case "ELEMENT ID": actual = element.Id.Value.ToString(CultureInfo.CurrentCulture); break;
            default:
                var parameters = element.GetParameters(rule.Field);
                if (parameters.Count == 0)
                    parameters = doc.GetElement(element.GetTypeId())?.GetParameters(rule.Field) ?? new List<Parameter>();
                return parameters.Count == 1 && MatchesParameter(parameters[0], doc, rule.Operator, rule.Value);
        }
        return MatchText(actual ?? "", rule.Operator, rule.Value);
    }

    private static bool MatchesParameter(Parameter parameter, Document doc, string op, string expected)
    {
        if ((op == "Greater than" || op == "Less than") && parameter.StorageType == StorageType.Double)
        {
            if (!parameter.HasValue || !UnitFormatUtils.TryParse(doc.GetUnits(), parameter.Definition.GetDataType(), expected, out double value)) return false;
            return op == "Greater than" ? parameter.AsDouble() > value : parameter.AsDouble() < value;
        }
        if ((op == "Greater than" || op == "Less than") && parameter.StorageType == StorageType.Integer)
        {
            if (!parameter.HasValue || !int.TryParse(expected, out int value)) return false;
            return op == "Greater than" ? parameter.AsInteger() > value : parameter.AsInteger() < value;
        }
        return MatchText(parameter.HasValue ? ParameterDisplay(parameter, doc) : "", op, expected);
    }

    private static bool MatchText(string actual, string op, string expected)
    {
        actual = actual.Trim();
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        switch (op)
        {
            case "Equals": return actual.Equals(expected, comparison);
            case "Does not equal": return !actual.Equals(expected, comparison);
            case "Contains": return actual.IndexOf(expected, comparison) >= 0;
            case "Does not contain": return actual.IndexOf(expected, comparison) < 0;
            case "Starts with": return actual.StartsWith(expected, comparison);
            case "Ends with": return actual.EndsWith(expected, comparison);
            case "Is empty": return actual.Length == 0;
            case "Is not empty": return actual.Length != 0;
            case "Greater than":
            case "Less than":
                return double.TryParse(actual, out double first) && double.TryParse(expected, out double second)
                    && (op == "Greater than" ? first > second : first < second);
            default: return false;
        }
    }
}
