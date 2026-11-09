using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BIMlify.CleanerUi;

public static class LegacyWarningFilters
{
	private sealed class FilterState
	{
		public DataGridView Grid;

		public ComboBox Severity;

		public ComboBox Category;

		public TextBox Search;
	}

	public static void Apply(Form form)
	{
		try
		{
			if (form == null || form.IsDisposed)
			{
				return;
			}
			TabPage tabPage = All(form).OfType<TabPage>().FirstOrDefault((TabPage p) => string.Equals(p.Text, "Model Warnings", StringComparison.OrdinalIgnoreCase));
			DataGridView grid = ((tabPage == null) ? null : All(tabPage).OfType<DataGridView>().FirstOrDefault());
			if (grid == null || grid.IsDisposed || grid.Parent == null || grid.Parent.Controls.OfType<Panel>().Any((Panel p) => p.Name == "BIMlifyWarningFilters"))
			{
				return;
			}
			Control host = grid.Parent;
			Label description = host.Controls.OfType<Label>().FirstOrDefault((Label l) => l.Text.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0);
			Panel bar = new Panel
			{
				Name = "BIMlifyWarningFilters",
				Height = 40,
				BackColor = Color.FromArgb(238, 241, 245),
				Padding = new Padding(7, 4, 7, 4),
				Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right)
			};
			Label severityLabel = MakeLabel("Severity", 72);
			ComboBox severity = MakeCombo("All severities", 140);
			Label categoryLabel = MakeLabel("Category", 72);
			ComboBox category = MakeCombo("All categories", 190);
			TextBox search = new TextBox
			{
				Name = "BIMlifyWarningSearch",
				Font = new Font("Segoe UI", 8.5f),
				BackColor = Color.White,
				ForeColor = Color.FromArgb(78, 85, 92),
				BorderStyle = BorderStyle.FixedSingle
			};
			search.AccessibleName = "Search warnings";
            BIMlify.SearchControls.SearchGlyph.Attach(search);
			bar.Controls.Add(severityLabel);
			bar.Controls.Add(severity);
			bar.Controls.Add(categoryLabel);
			bar.Controls.Add(category);
			bar.Controls.Add(search);
			FilterState state = new FilterState
			{
				Grid = grid,
				Severity = severity,
				Category = category,
				Search = search
			};
			foreach (DataGridViewRow item in from DataGridViewRow r in grid.Rows
				where !r.IsNewRow
				select r)
			{
				AddOption(severity, Cell(item, 0));
				AddOption(category, Cell(item, 1));
			}
			severity.SelectedIndex = 0;
			category.SelectedIndex = 0;
			Action filter = () =>
			{
				FilterRows(state);
			};
			severity.SelectedIndexChanged += (object s, EventArgs e) =>
			{
				filter();
			};
			category.SelectedIndexChanged += (object s, EventArgs e) =>
			{
				filter();
			};
			search.TextChanged += (object s, EventArgs e) =>
			{
				filter();
			};
			host.Controls.Add(bar);
			bar.BringToFront();
			if (description != null)
			{
				description.BringToFront();
			}
			grid.Dock = DockStyle.None;
			grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			Action layout = () =>
			{
				LayoutControls(host, description, bar, grid, severityLabel, severity, categoryLabel, category, search);
			};
			host.Resize += (object s, EventArgs e) =>
			{
				layout();
			};
			bar.Resize += (object s, EventArgs e) =>
			{
				LayoutBar(bar, severityLabel, severity, categoryLabel, category, search);
			};
			LayoutControls(host, description, bar, grid, severityLabel, severity, categoryLabel, category, search);
			FilterRows(state);
		}
		catch
		{
		}
	}

	private static void AddOption(ComboBox combo, string value)
	{
		if (!string.IsNullOrWhiteSpace(value) && !combo.Items.Cast<object>().Any((object x) => string.Equals(Convert.ToString(x), value, StringComparison.CurrentCultureIgnoreCase)))
		{
			combo.Items.Add(value);
		}
	}

	private static Label MakeLabel(string text, int width)
	{
		return new Label
		{
			Text = text,
			Width = width,
			Height = 30,
			AutoSize = false,
			TextAlign = ContentAlignment.MiddleLeft,
			Font = new Font("Segoe UI", 8.5f),
			ForeColor = Color.FromArgb(78, 85, 92),
			Margin = Padding.Empty
		};
	}

	private static ComboBox MakeCombo(string first, int width)
	{
		ComboBox comboBox = new ComboBox
		{
			Name = "BIMlifyWarningFilter",
			Width = width,
			Height = 27,
			DropDownStyle = ComboBoxStyle.DropDownList,
			Font = new Font("Segoe UI", 8.5f),
			BackColor = Color.FromArgb(255, 244, 216),
			ForeColor = Color.FromArgb(78, 85, 92),
			IntegralHeight = false,
			Margin = Padding.Empty
		};
		comboBox.Items.Add(first);
		return comboBox;
	}

	private static void LayoutControls(Control host, Label description, Panel bar, DataGridView grid, Label sl, ComboBox sev, Label cl, ComboBox cat, TextBox search)
	{
		if (host != null && !host.IsDisposed && !bar.IsDisposed && !grid.IsDisposed)
		{
			int num = Math.Max(6, grid.Margin.Left);
			int width = Math.Max(120, host.ClientSize.Width - num - Math.Max(6, grid.Margin.Right));
			int num2 = 4;
			if (description != null && !description.IsDisposed)
			{
				description.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
				description.SetBounds(num, Math.Max(0, description.Top), width, Math.Max(24, description.Height));
				num2 = description.Bottom;
			}
			int num3 = num2 + 3;
			bar.SetBounds(num, num3, width, 40);
			grid.SetBounds(num, num3 + 40 + 4, width, Math.Max(70, host.ClientSize.Height - num3 - 40 - 4 - 6));
			LayoutBar(bar, sl, sev, cl, cat, search);
			bar.BringToFront();
			description?.BringToFront();
		}
	}

	private static void LayoutBar(Panel bar, Label sl, ComboBox sev, Label cl, ComboBox cat, TextBox search)
	{
		int num = Math.Max(28, bar.ClientSize.Height - bar.Padding.Vertical);
		int y = (bar.ClientSize.Height - 30) / 2;
		int y2 = (bar.ClientSize.Height - 27) / 2;
		int left = bar.Padding.Left;
		int num2 = 6;
		sl.SetBounds(left, y, 72, 30);
		left += 72 + num2;
		int num3 = bar.ClientSize.Width - left - 72 - num2 - 130 - num2 - bar.Padding.Right;
		int num4 = Math.Max(110, Math.Min(155, num3 / 4));
		sev.SetBounds(left, y2, num4, 27);
		left += num4 + num2;
		cl.SetBounds(left, y, 72, 30);
		left += 72 + num2;
		int num5 = Math.Max(130, Math.Min(200, num3 / 2));
		cat.SetBounds(left, y2, num5, 27);
		left += num5 + num2;
		search.SetBounds(left, y2, Math.Max(120, bar.ClientSize.Width - bar.Padding.Right - left), 27);
	}

	private static string Cell(DataGridViewRow row, int index)
	{
		try
		{
			return Convert.ToString(row.Cells[index].FormattedValue) ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static void FilterRows(FilterState state)
	{
		if (state?.Grid == null || state.Grid.IsDisposed)
		{
			return;
		}
		string text = ((state.Severity.SelectedIndex > 0) ? Convert.ToString(state.Severity.SelectedItem) : null);
		string text2 = ((state.Category.SelectedIndex > 0) ? Convert.ToString(state.Category.SelectedItem) : null);
		string query = state.Search.Text?.Trim() ?? "";
		state.Grid.SuspendLayout();
		try
		{
			Dictionary<DataGridViewRow, bool> dictionary = new Dictionary<DataGridViewRow, bool>();
			foreach (DataGridViewRow item in (IEnumerable)state.Grid.Rows)
			{
				if (!item.IsNewRow)
				{
					dictionary[item] = (text == null || string.Equals(Cell(item, 0), text, StringComparison.CurrentCultureIgnoreCase)) && (text2 == null || string.Equals(Cell(item, 1), text2, StringComparison.CurrentCultureIgnoreCase)) && (query.Length == 0 || item.Cells.Cast<DataGridViewCell>().Any((DataGridViewCell c) =>
					{
						string text3 = Convert.ToString(c.FormattedValue);
						return text3 != null && text3.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
					}));
				}
			}
			if (state.Grid.CurrentCell != null && dictionary.TryGetValue(state.Grid.CurrentCell.OwningRow, out var value) && !value)
			{
				state.Grid.CurrentCell = null;
			}
			state.Grid.ClearSelection();
			foreach (KeyValuePair<DataGridViewRow, bool> item2 in dictionary)
			{
				item2.Key.Visible = item2.Value;
			}
		}
		catch
		{
		}
		finally
		{
			state.Grid.ResumeLayout();
		}
	}

	private static IEnumerable<Control> All(Control root)
	{
		foreach (Control child in root.Controls)
		{
			yield return child;
			foreach (Control item in All(child))
			{
				yield return item;
			}
		}
	}
}
