using System;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Color = System.Drawing.Color;

namespace BIMlify.CleanerUi;

public static class LegacyPurgeSelection
{
	private sealed class Attachment
	{
		internal Panel Host;
		internal DataGridView Grid;
		internal Timer RefreshTimer;
		internal bool Syncing;
		internal int[] PendingCheckboxRows;
	}

	private static readonly ConditionalWeakTable<CheckedListBox, Attachment> Attached = new ConditionalWeakTable<CheckedListBox, Attachment>();
	public static void Apply(Form form)
	{
		try
		{
			CheckedListBox list = form?.Controls.Find("BIMlifyPurgeList", true).OfType<CheckedListBox>().FirstOrDefault()
				?? All(form).OfType<CheckedListBox>().FirstOrDefault();
			if (list == null || list.IsDisposed || Attached.TryGetValue(list, out _)) return;

			list.Enabled = true;
			list.CheckOnClick = false;
			list.SelectionMode = SelectionMode.One;
			list.Visible = false;

			Attachment view = AttachGrid(list);
			Attached.Add(list, view);
			// Capture the selected group before WinForms processes the checkbox click.
			// FullRowSelect can collapse SelectedRows during the same mouse gesture.
			view.Grid.MouseDown += (s, e) =>
			{
				DataGridView.HitTestInfo hit = view.Grid.HitTest(e.X, e.Y);
				if (hit.RowIndex >= 0 && hit.ColumnIndex == 0)
				{
					int[] selected = view.Grid.Rows.Cast<DataGridViewRow>().Where(row => row.Selected).Select(row => row.Index).ToArray();
					view.PendingCheckboxRows = selected.Contains(hit.RowIndex) && selected.Length > 1 ? selected : new[] { hit.RowIndex };
				}
			};
			view.Grid.CurrentCellDirtyStateChanged += (s, e) =>
			{
				if (view.Grid.IsCurrentCellDirty && view.Grid.CurrentCell is DataGridViewCheckBoxCell)
					view.Grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
			};
			view.Grid.CellValueChanged += (s, e) =>
			{
				if (view.Syncing || e.RowIndex < 0 || e.ColumnIndex != 0 || e.RowIndex >= list.Items.Count) return;
				try
				{
					bool check = Convert.ToBoolean(view.Grid.Rows[e.RowIndex].Cells[0].Value ?? false);
					int[] selected = view.PendingCheckboxRows ?? view.Grid.Rows.Cast<DataGridViewRow>().Where(row => row.Selected)
						.Select(row => row.Index)
						.Where(index => index >= 0 && index < list.Items.Count)
						.Distinct()
						.ToArray();
					int[] targets = selected.Length > 1 && selected.Contains(e.RowIndex) ? selected : new[] { e.RowIndex };
					view.PendingCheckboxRows = null;
					ApplyChecks(list, view, targets, check);
				}
				catch { }
			};
			view.Grid.CellPainting += (s, e) =>
			{
				if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
				try
				{
					e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
					int dpi = Math.Max(96, view.Grid.DeviceDpi);
					int size = Math.Max(13, (int)Math.Round(15.0 * dpi / 96.0));
					Rectangle box = new Rectangle(e.CellBounds.Left + (e.CellBounds.Width - size) / 2, e.CellBounds.Top + (e.CellBounds.Height - size) / 2, size, size);
					bool isChecked = Convert.ToBoolean(e.Value ?? false);
					using (SolidBrush fill = new SolidBrush(isChecked ? Color.FromArgb(214, 171, 61) : Color.White))
					using (Pen outline = new Pen(Color.FromArgb(118, 126, 134), Math.Max(1f, dpi / 96f)))
					{
						e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
						e.Graphics.FillRectangle(fill, box);
						e.Graphics.DrawRectangle(outline, box);
						if (isChecked)
						{
							using Pen tick = new Pen(Color.Black, Math.Max(1.6f, 1.9f * dpi / 96f));
							tick.StartCap = System.Drawing.Drawing2D.LineCap.Round;
							tick.EndCap = System.Drawing.Drawing2D.LineCap.Round;
							e.Graphics.DrawLines(tick, new[] { new Point(box.Left + size / 4, box.Top + size / 2), new Point(box.Left + size / 2 - 1, box.Bottom - size / 4), new Point(box.Right - size / 5, box.Top + size / 4) });
						}
					}
					e.Handled = true;
				}
				catch { }
			};
			view.Grid.SelectionChanged += (s, e) =>
			{
				if (view.Syncing || view.Grid.CurrentRow == null) return;
				try
				{
					int index = view.Grid.CurrentRow.Index;
					if (index >= 0 && index < list.Items.Count) list.SelectedIndex = index;
				}
				catch { }
			};
			view.Grid.KeyDown += (s, e) =>
			{
				if (e.KeyCode != Keys.Space || view.Grid.SelectedRows.Count == 0) return;
				try
				{
					bool check = view.Grid.SelectedRows.Cast<DataGridViewRow>().Any(row => !Convert.ToBoolean(row.Cells[0].Value ?? false));
					int[] selected = view.Grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index).ToArray();
					ApplyChecks(list, view, selected, check);
					e.Handled = true;
					e.SuppressKeyPress = true;
				}
				catch { }
			};
			list.ItemCheck += (s, e) =>
			{
				try
				{
					if (view.Grid.IsDisposed || !view.Grid.IsHandleCreated) return;
					int index = e.Index;
					bool check = e.NewValue == CheckState.Checked;
					view.Grid.BeginInvoke((MethodInvoker)(() => SetGridCheck(view, index, check)));
				}
				catch { }
			};

			view.RefreshTimer = new Timer { Interval = 400 };
			view.RefreshTimer.Tick += (s, e) => SyncRows(list, view);
			view.RefreshTimer.Start();
			if (form != null)
			{
				form.FormClosed += (s, e) =>
				{
					try { view.RefreshTimer?.Stop(); view.RefreshTimer?.Dispose(); } catch { }
				};
			}
			SyncRows(list, view);
		}
		catch { }
	}

	private static Attachment AttachGrid(CheckedListBox list)
	{
		Control parent = list.Parent;
		Panel host = new Panel { BackColor = Color.White, Margin = list.Margin, Padding = Padding.Empty };
		if (parent is TableLayoutPanel table)
		{
			TableLayoutPanelCellPosition cell = table.GetCellPosition(list);
			int columns = table.GetColumnSpan(list);
			int rows = table.GetRowSpan(list);
			table.Controls.Remove(list);
			host.Dock = DockStyle.Fill;
			table.Controls.Add(host, cell.Column, cell.Row);
			table.SetColumnSpan(host, columns);
			table.SetRowSpan(host, rows);
		}
		else if (parent != null)
		{
			DockStyle dock = list.Dock;
			AnchorStyles anchor = list.Anchor;
			Rectangle bounds = list.Bounds;
			parent.Controls.Remove(list);
			host.Dock = dock;
			host.Anchor = anchor;
			if (dock == DockStyle.None) host.Bounds = bounds;
			parent.Controls.Add(host);
		}

		list.Dock = DockStyle.None;
		list.Size = new Size(1, 1);
		list.Location = new Point(-4, -4);
		list.Visible = false;
		host.Controls.Add(list);

		DataGridView grid = new DataGridView
		{
			Name = "BIMlifyPurgeSelectionGrid",
			Dock = DockStyle.Fill,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			AllowUserToResizeRows = false,
			AutoGenerateColumns = false,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
			BackgroundColor = Color.FromArgb(247, 248, 250),
			BorderStyle = BorderStyle.FixedSingle,
			CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
			ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
			EnableHeadersVisualStyles = false,
			RowHeadersVisible = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			MultiSelect = true,
			EditMode = DataGridViewEditMode.EditOnEnter,
			GridColor = Color.FromArgb(213, 219, 226),
			Font = new Font("Segoe UI", 9f),
			ColumnHeadersHeight = Math.Max(28, (int)Math.Round(30.0 * Math.Max(96, list.DeviceDpi) / 96.0)),
			RowTemplate = { Height = Math.Max(25, (int)Math.Round(26.0 * Math.Max(96, list.DeviceDpi) / 96.0)) }
		};
		grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 235, 239);
		grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(38, 55, 73);
		grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
		grid.DefaultCellStyle.BackColor = Color.FromArgb(247, 248, 250);
		grid.DefaultCellStyle.ForeColor = Color.FromArgb(49, 82, 111);
		grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
		grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(49, 82, 111);
		grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(237, 240, 244);
		grid.CellFormatting += (s, e) =>
		{
			if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
			bool isChecked = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value ?? false);
			e.CellStyle.BackColor = isChecked ? Color.FromArgb(255, 240, 201) : (e.RowIndex % 2 == 0 ? Color.FromArgb(247, 248, 250) : Color.FromArgb(237, 240, 244));
			e.CellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
			e.CellStyle.ForeColor = Color.FromArgb(49, 82, 111);
			e.CellStyle.SelectionForeColor = Color.FromArgb(49, 82, 111);
		};

		DataGridViewCheckBoxColumn delete = new DataGridViewCheckBoxColumn
		{
			Name = "Delete",
			HeaderText = "Delete",
			FillWeight = 8f,
			MinimumWidth = 68,
			ThreeState = false,
			TrueValue = true,
			FalseValue = false,
			DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
		};
		grid.Columns.Add(delete);
		AddTextColumn(grid, "Name", "Name", 23f, 180);
		AddTextColumn(grid, "Type", "Type", 13f, 100);
		AddTextColumn(grid, "ElementId", "Element ID", 11f, 90);
		AddTextColumn(grid, "ReviewNote", "Review note", 28f, 150);
		AddTextColumn(grid, "Category", "Category", 17f, 110);
		host.Controls.Add(grid);
		grid.BringToFront();
		return new Attachment { Host = host, Grid = grid };
	}

	private static void AddTextColumn(DataGridView grid, string name, string header, float weight, int minWidth)
	{
		grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = name,
			HeaderText = header,
			FillWeight = weight,
			MinimumWidth = minWidth,
			ReadOnly = true,
			SortMode = DataGridViewColumnSortMode.NotSortable
		});
	}

	private static void SyncRows(CheckedListBox list, Attachment view)
	{
		if (list.IsDisposed || view.Grid.IsDisposed) return;
		try
		{
			bool rebuild = view.Grid.Rows.Count != list.Items.Count;
			if (!rebuild)
			{
				for (int i = 0; i < list.Items.Count; i++)
				{
					RowFields fields = ParseRow(list.GetItemText(list.Items[i]));
					DataGridViewRow row = view.Grid.Rows[i];
					if (!string.Equals(Convert.ToString(row.Cells[1].Value), fields.Name, StringComparison.Ordinal)
						|| !string.Equals(Convert.ToString(row.Cells[2].Value), fields.Type, StringComparison.Ordinal)
						|| !string.Equals(Convert.ToString(row.Cells[3].Value), fields.Id, StringComparison.Ordinal)
						|| !string.Equals(Convert.ToString(row.Cells[5].Value), fields.Category, StringComparison.Ordinal))
					{
						rebuild = true;
						break;
					}
				}
			}

			view.Syncing = true;
			if (rebuild)
			{
				view.Grid.Rows.Clear();
				for (int i = 0; i < list.Items.Count; i++)
					AddRow(list, view.Grid, i);
			}
			else
			{
				for (int i = 0; i < list.Items.Count; i++)
				{
					bool check = list.GetItemChecked(i);
					if (!Equals(view.Grid.Rows[i].Cells[0].Value, check)) view.Grid.Rows[i].Cells[0].Value = check;
				}
			}
		}
		catch { }
		finally { view.Syncing = false; }
	}

	private static void AddRow(CheckedListBox list, DataGridView grid, int index)
	{
		RowFields fields = ParseRow(list.GetItemText(list.Items[index]));
		int row = grid.Rows.Add(list.GetItemChecked(index), fields.Name, fields.Type, fields.Id, "Revit reports unused", fields.Category);
		grid.Rows[row].Tag = index;
	}

	private static void SetGridCheck(Attachment view, int index, bool check)
	{
		if (view.Grid.IsDisposed || index < 0 || index >= view.Grid.Rows.Count) return;
		try
		{
			view.Syncing = true;
			view.Grid.Rows[index].Cells[0].Value = check;
		}
		catch { }
		finally { view.Syncing = false; }
	}

	private static void ApplyChecks(CheckedListBox list, Attachment view, System.Collections.Generic.IEnumerable<int> indices, bool check)
	{
		int[] valid = indices.Where(index => index >= 0 && index < list.Items.Count && index < view.Grid.Rows.Count).Distinct().ToArray();
		try
		{
			view.Syncing = true;
			foreach (int index in valid)
				if (!Equals(view.Grid.Rows[index].Cells[0].Value, check)) view.Grid.Rows[index].Cells[0].Value = check;
			foreach (int index in valid)
				if (list.GetItemChecked(index) != check) list.SetItemChecked(index, check);
		}
		catch { }
		finally { view.Syncing = false; }
	}

	private sealed class RowFields
	{
		internal string Name;
		internal string Type;
		internal string Id;
		internal string Category;
	}

	private static RowFields ParseRow(string text)
	{
		text = text ?? "";
		string category = "";
		string name = text;
		string type = "";
		string id = "";
		int split = text.IndexOf('|');
		if (split >= 0) { category = text.Substring(0, split).Trim(); name = text.Substring(split + 1).Trim(); }
		int dash = name.IndexOf('—');
		if (dash >= 0) { type = name.Substring(dash + 1).Trim(); name = name.Substring(0, dash).Trim(); }
		int open = type.LastIndexOf('[');
		int close = type.LastIndexOf(']');
		if (open >= 0 && close > open) { id = type.Substring(open + 1, close - open - 1).Trim(); type = type.Substring(0, open).Trim(); }
		else
		{
			open = name.LastIndexOf('[');
			close = name.LastIndexOf(']');
			if (open >= 0 && close > open) { id = name.Substring(open + 1, close - open - 1).Trim(); name = name.Substring(0, open).Trim(); }
		}
		return new RowFields { Name = name, Type = type, Id = id, Category = category };
	}

	private static System.Collections.Generic.IEnumerable<Control> All(Control root)
	{
		if (root == null) yield break;
		foreach (Control child in root.Controls)
		{
			yield return child;
			foreach (Control nested in All(child)) yield return nested;
		}
	}
}
