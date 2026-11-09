using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using FontStyle = System.Drawing.FontStyle;
using ComboBox = System.Windows.Forms.ComboBox;
using Control = System.Windows.Forms.Control;
using Panel = System.Windows.Forms.Panel;
using TextBox = System.Windows.Forms.TextBox;

namespace BIMlify.CategorySelectSupport;

public static partial class Entry
{
    private static void AddAdvancedSelectionTabs(TabControl tabs, PickerHandler handler)
    {
        Panel similar = SelectionCard(tabs, "Select Similar",
            "Select a reference element in Revit, then choose how to find similar elements.");
        ComboBox match = Choices("Same type", "Same family", "Same category", "Same level", "Same workset", "Shares a material");
        ComboBox similarScope = Choices("Active view", "Entire project");
        AddField(similar, "Match by", match, 75);
        AddField(similar, "Search scope", similarScope, 111);
        AddAction(similar, "Select Similar", 151, () =>
        {
            int criterion = match.SelectedIndex;
            bool entire = similarScope.SelectedIndex == 1;
            handler.Queue("advanced", ui =>
            {
                var selected = ui.Selection.GetElementIds();
                if (selected.Count != 1) throw new InvalidOperationException("Select exactly one reference element in Revit.");
                Element seed = ui.Document.GetElement(selected.Single());
                if (criterion == 5)
                {
                    var materials = new HashSet<ElementId>(seed.GetMaterialIds(false));
                    materials.UnionWith(seed.GetMaterialIds(true));
                    if (materials.Count == 0) { handler.SetStatus("The reference element has no assigned materials."); return; }
                    SelectMatches(ui, Candidates(ui, entire).Where(element =>
                        element.GetMaterialIds(false).Any(materials.Contains) || element.GetMaterialIds(true).Any(materials.Contains)), handler);
                    return;
                }
                ElementId key = SimilarKey(seed, criterion);
                if (key == null || key == ElementId.InvalidElementId)
                    throw new InvalidOperationException("This element has no matching type, loadable family or category for the chosen rule.");
                SelectMatches(ui, Candidates(ui, entire).Where(element => SimilarKey(element, criterion) == key), handler);
            });
        });

        Panel parameter = SelectionCard(tabs, "Select by Parameter",
            "Find elements by a named parameter. Enter the value as it appears in Revit.");
        var parameterName = new TextBox();
        var parameterValue = new TextBox();
        ComboBox matchRule = Choices(RuleOperators);
        ComboBox parameterScope = Choices("Active view", "Entire project");
        AddField(parameter, "Parameter name", parameterName, 75);
        AddField(parameter, "Value", parameterValue, 110);
        AddField(parameter, "Match", matchRule, 145);
        AddField(parameter, "Search scope", parameterScope, 180);
        AddAction(parameter, "Select Matches", 222, () =>
        {
            string name = parameterName.Text.Trim();
            string value = parameterValue.Text.Trim();
            string comparison = matchRule.Text;
            bool entire = parameterScope.SelectedIndex == 1;
            if (name.Length == 0 || (!IsEmptyRule(comparison) && value.Length == 0))
            {
                handler.SetStatus("Enter a parameter name and a value for the chosen condition.");
                return;
            }
            handler.Queue("advanced", ui =>
            {
                var typeParameters = new Dictionary<ElementId, Parameter>();
                SelectMatches(ui, Candidates(ui, entire).Where(element =>
                {
                    IList<Parameter> parameters = element.GetParameters(name);
                    if (parameters.Count > 1) return false;
                    Parameter item = parameters.SingleOrDefault();
                    ElementId typeId = element.GetTypeId();
                    if (item == null && typeId != ElementId.InvalidElementId && typeId != null)
                    {
                        if (!typeParameters.TryGetValue(typeId, out item))
                        {
                            var typeValues = ui.Document.GetElement(typeId)?.GetParameters(name);
                            item = typeValues?.Count == 1 ? typeValues[0] : null;
                            typeParameters[typeId] = item;
                        }
                    }
                    if (item == null) return false;
                    return MatchesParameter(item, ui.Document, comparison, value);
                }), handler);
            });
        });

        Panel sets = SelectionCard(tabs, "Saved Selections",
            "Save the current Revit selection as a named set in this project, or load an existing set.");
        var setName = new TextBox();
        ComboBox saved = Choices();
        AddField(sets, "New set name", setName, 75);
        AddAction(sets, "Save Selection", 110, () =>
        {
            string name = setName.Text.Trim();
            if (name.Length == 0) { handler.SetStatus("Enter a name for the selection set."); return; }
            handler.Queue("advanced", ui =>
            {
                var ids = ui.Selection.GetElementIds().Where(id => ui.Document.GetElement(id) != null).ToList();
                if (ids.Count == 0) throw new InvalidOperationException("Select one or more elements in Revit first.");
                if (new FilteredElementCollector(ui.Document).OfClass(typeof(FilterElement)).Any(
                    item => String.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("A filter or selection set already uses this name. Choose another name.");
                using (var transaction = new Transaction(ui.Document, "BIMlify Save Selection"))
                {
                    transaction.Start();
                    SelectionFilterElement.Create(ui.Document, name).SetElementIds(ids);
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit did not save the selection set.");
                }
                PopulateSets(saved, ui.Document, name);
                handler.SetStatus("Saved " + ids.Count + " element(s) to " + name + ".");
            });
        });
        AddField(sets, "Saved selection", saved, 156);
        Action refresh = () => handler.Queue("advanced", ui =>
        {
            PopulateSets(saved, ui.Document, saved.Text);
            handler.SetStatus(saved.Items.Count + " saved selection set(s).");
        });
        AddAction(sets, "Load Selection", 198, () =>
        {
            string name = saved.SelectedItem as string;
            if (String.IsNullOrEmpty(name)) { handler.SetStatus("Choose a saved selection set."); return; }
            handler.Queue("advanced", ui =>
            {
                var filter = SelectionSets(ui.Document).FirstOrDefault(item => item.Name == name);
                if (filter == null) throw new InvalidOperationException("This selection set is unavailable. Refresh the list.");
                var ids = filter.GetElementIds().Where(id => ui.Document.GetElement(id) != null).ToList();
                if (ids.Count == 0) { handler.SetStatus("The saved set has no remaining elements."); return; }
                ui.Selection.SetElementIds(ids);
                handler.SetStatus("Loaded " + ids.Count + " element(s) from " + name + ".");
            });
        });
        AddAction(sets, "Refresh Sets", 198, refresh, left: 184);
        tabs.SelectedIndexChanged += (sender, args) =>
        {
            if (tabs.SelectedTab == sets.Parent) refresh();
        };
        AddSelectionRulesTab(tabs, handler);
    }

    private static IEnumerable<Element> Candidates(UIDocument ui, bool entire)
    {
        if (!entire && !CanReadView(ui.Document, ui.ActiveView))
            throw new InvalidOperationException("This view cannot be searched. Choose Entire project or switch views.");
        var collector = entire ? new FilteredElementCollector(ui.Document) :
            new FilteredElementCollector(ui.Document, ui.ActiveView.Id);
        return collector.WhereElementIsNotElementType().Where(element => element.Category != null && !(element is RevitLinkInstance));
    }

    private static ElementId SimilarKey(Element element, int criterion)
    {
        if (element == null) return null;
        if (criterion == 0) return element.GetTypeId();
        if (criterion == 2) return element.Category?.Id;
        if (criterion == 3) return element.LevelId;
        if (criterion == 4) return new ElementId((long)element.WorksetId.IntegerValue);
        ElementId typeId = element.GetTypeId();
        return typeId != ElementId.InvalidElementId && typeId != null
            ? (element.Document.GetElement(typeId) as FamilySymbol)?.Family?.Id : null;
    }

    private static string ParameterDisplay(Parameter parameter, Document doc)
    {
        if (parameter.StorageType == StorageType.String) return parameter.AsString() ?? "";
        string formatted = parameter.AsValueString();
        if (formatted != null) return formatted;
        if (parameter.StorageType == StorageType.Integer) return parameter.AsInteger().ToString(CultureInfo.CurrentCulture);
        if (parameter.StorageType == StorageType.ElementId)
        {
            var id = parameter.AsElementId();
            return doc.GetElement(id)?.Name ?? id.Value.ToString(CultureInfo.InvariantCulture);
        }
        return "";
    }

    private static void SelectMatches(UIDocument ui, IEnumerable<Element> matches, PickerHandler handler)
    {
        var ids = matches.Select(element => element.Id).Distinct().ToList();
        if (ids.Count == 0) { handler.SetStatus("No elements matched the rule."); return; }
        ui.Selection.SetElementIds(ids);
        handler.SetStatus(ids.Count + " matching element(s) selected in Revit.");
    }

    private static IEnumerable<SelectionFilterElement> SelectionSets(Document doc)
        => new FilteredElementCollector(doc).OfClass(typeof(SelectionFilterElement)).Cast<SelectionFilterElement>();

    private static void PopulateSets(ComboBox list, Document doc, string selected)
    {
        var names = SelectionSets(doc).Select(item => item.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            list.Items.AddRange(names);
            if (names.Length > 0) list.SelectedIndex = Math.Max(0, Array.IndexOf(names, selected));
        }
        finally { list.EndUpdate(); }
    }

    private static Panel SelectionCard(TabControl tabs, string title, string description)
    {
        var page = new TabPage(title) { BackColor = Back, Padding = new Padding(8) };
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AutoScroll = true };
        var heading = new Label { Text = title, Left = 20, Top = 14, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Ink };
        var detail = new Label { Text = description, Left = 20, Top = 37, AutoSize = true, Font = new Font("Segoe UI", 8.5f), ForeColor = Muted };
        card.Controls.Add(heading);
        card.Controls.Add(detail);
        page.Controls.Add(card);
        tabs.TabPages.Add(page);
        return card;
    }

    private static ComboBox Choices(params string[] items)
    {
        var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(255, 245, 215) };
        picker.Items.AddRange(items);
        if (items.Length > 0) picker.SelectedIndex = 0;
        return picker;
    }

    private static void AddField(Panel panel, string text, Control control, int y)
    {
        var caption = new Label { Text = text, Left = 20, Top = y + 3, AutoSize = true, Font = new Font("Segoe UI", 8.5f), ForeColor = Ink };
        control.SetBounds(155, y, 310, 26);
        control.Font = caption.Font;
        control.AccessibleName = text;
        panel.Controls.Add(caption);
        panel.Controls.Add(control);
    }

    private static void AddAction(Panel panel, string text, int y, Action action, int left = 20)
    {
        Button button = MakeButton(text, 150, primary: true);
        button.Left = left;
        button.Top = y;
        button.Click += (sender, args) => action();
        panel.Controls.Add(button);
    }
}
