using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Form = System.Windows.Forms.Form;
using Control = System.Windows.Forms.Control;
using Panel = System.Windows.Forms.Panel;
using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;
using ComboBox = System.Windows.Forms.ComboBox;
using TextBox = System.Windows.Forms.TextBox;
using View = Autodesk.Revit.DB.View;
using BIMlify.CleanerUi;
using BIMlify.ProgressOverlay;

namespace BIMlify.ModelCleaner;

internal sealed class CleanerWindow : Form
{
	private readonly UIDocument _ui;

	private readonly Document _doc;

	private readonly Color _page = Color.FromArgb(238, 241, 245);

	private readonly Color _card = Color.White;

	private readonly Color _ink = Color.FromArgb(55, 67, 79);

	private readonly Color _muted = Color.FromArgb(105, 117, 129);

	private readonly Color _gold = Color.FromArgb(214, 171, 61);

	private readonly Color _paleGold = Color.FromArgb(255, 247, 226);

	private readonly Color _selectedGold = Color.FromArgb(255, 240, 201);

	private readonly TabControl _tabs = new TabControl();

	private readonly DataGridView _purgeGrid = new DataGridView();

	private readonly DataGridView _reviewGrid = new DataGridView();

	private readonly DataGridView _viewsGrid = new DataGridView();

	private readonly DataGridView _warningsGrid = new DataGridView();

	private readonly ComboBox _reviewGroupFilter = new ComboBox();

	private readonly TextBox _reviewSearch = new TextBox();

	private readonly ComboBox _kindFilter = new ComboBox();

	private readonly ComboBox _categoryFilter = new ComboBox();

	private readonly TextBox _candidateSearch = new TextBox();

	private readonly Label _summary = new Label();

	private readonly Label _status = new Label();

	private readonly Label _purgeCount = new Label();

	private readonly Label _cleanupCount = new Label();

	private readonly Label _viewsCount = new Label();

	private readonly Label _warningsCount = new Label();

	private readonly Label _purgeDetailsCount = new Label();

	private readonly Label _reviewDetailsCount = new Label();

	private readonly Label _warningDetailsCount = new Label();

	private readonly Button _scan = new Button();

	private readonly Button _clean = new Button();

	private readonly Button _deleteReviewed = new Button();

	private readonly Button _locate = new Button();

	private readonly Button _export = new Button();

	private readonly List<PurgeFinding> _purgeFindings = new List<PurgeFinding>();

	private readonly List<ReviewFinding> _reviewFindings = new List<ReviewFinding>();

	private readonly List<ViewFinding> _viewFindings = new List<ViewFinding>();

	private readonly List<WarningFinding> _warningFindings = new List<WarningFinding>();

	private readonly HashSet<ElementId> _selectedPurgeIds = new HashSet<ElementId>();

	private readonly HashSet<ElementId> _selectedReviewIds = new HashSet<ElementId>();

	private int _purgeRangeAnchor = -1;

	private int _reviewRangeAnchor = -1;

	private bool _updatingFilters;

	private bool _updatingReviewFilters;

	public CleanerWindow(UIDocument ui, bool openCategoryReview = false)
	{
		_ui = ui;
		_doc = ui.Document;
		Text = "BIMlify — Smart Cleaner";
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(1080, 720);
		MinimumSize = new Size(980, 640);
		AutoScaleMode = AutoScaleMode.Dpi;
		BackColor = _page;
		ForeColor = _ink;
		Font = new Font("Segoe UI", 9f);
		UiPolish.EnableCtrlA(this);
		BuildUi();
		if (openCategoryReview)
		{
			_tabs.SelectedIndex = 2;
		}
		Shown += delegate
		{
			_status.Text = "Ready. Select Scan Model when you want to inspect this project.";
		};
	}

	private void BuildUi()
	{
		EmptyState.WatchCleanerForm(this);
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = _page
		};
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Left,
			Width = 58,
			BackColor = Color.FromArgb(247, 242, 226)
		};
		Button button = CreateRailButton("‹", "Close this tool");
		button.Top = 10;
		button.Click += delegate
		{
			Close();
		};
		Button button2 = CreateRailButton("⌂", "Return to Revit");
		button2.Top = 58;
		button2.Click += delegate
		{
			Close();
		};
		panel2.Controls.Add(button);
		panel2.Controls.Add(button2);
		DashboardEnhancements.AttachToolSearchButton(panel2, this, 108);
		Panel value = new Panel
		{
			Dock = DockStyle.Right,
			Width = 1,
			BackColor = Color.FromArgb(224, 213, 181)
		};
		panel2.Controls.Add(value);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			BackColor = _page,
			Padding = new Padding(14, 12, 14, 5)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 128f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
		panel.Controls.Add(tableLayoutPanel);
		panel.Controls.Add(panel2);
		Controls.Add(panel);
		Panel header = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.FromArgb(247, 247, 245),
			Padding = new Padding(18, 10, 18, 6)
		};
		Label value2 = new Label
		{
			Text = "Smart Cleaner",
			Font = new Font("Segoe UI", 10f, FontStyle.Bold),
			ForeColor = _ink,
			AutoSize = true,
			Left = 18,
			Top = 10
		};
		Label value3 = new Label
		{
			Text = "Review unused-item suggestions, inspect views and model warnings, then use Review & Clean for selected cleanup.",
			ForeColor = _muted,
			Font = new Font("Segoe UI", 8f),
			AutoSize = true,
			Left = 18,
			Top = 38
		};
		_scan.Text = "Scan Model";
		_scan.Width = 126;
		_scan.Height = 34;
		_scan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		_scan.Left = 710;
		_scan.Top = 42;
		StyleButton(_scan, primary: true);
		_scan.Click += delegate
		{
			EmptyState.PostCleanerAction(this, "ScanModel");
		};
		Control brand = Branding.Create(header.BackColor, null);
		brand.Top = 2;
		brand.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		header.Resize += delegate
		{
			_scan.Left = header.ClientSize.Width - brand.Width - _scan.Width - 26;
			brand.Left = header.ClientSize.Width - brand.Width - 8;
		};
		header.Controls.Add(brand);
		header.Controls.Add(value2);
		header.Controls.Add(value3);
		header.Controls.Add(_scan);
		Panel value4 = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 2,
			BackColor = _gold
		};
		header.Controls.Add(value4);
		tableLayoutPanel.Controls.Add(header, 0, 0);
		_tabs.Dock = DockStyle.Fill;
		_tabs.Font = new Font("Segoe UI", 9f);
		_tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
		_tabs.ItemSize = new Size(170, 30);
		_tabs.SizeMode = TabSizeMode.Fixed;
		_tabs.DrawItem += DrawTab;
		_tabs.TabPages.Add(BuildOverviewTab());
		_tabs.TabPages.Add(BuildPurgeTab());
		_tabs.TabPages.Add(BuildCategoryReviewTab());
		_tabs.TabPages.Add(BuildViewsTab());
		_tabs.TabPages.Add(BuildWarningsTab());
		tableLayoutPanel.Controls.Add(_tabs, 0, 1);
		Panel footer = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = _page
		};
		_status.Text = "Ready. Select Scan Model to inspect this project.";
		_status.Left = 4;
		_status.Top = 14;
		_status.Width = 700;
		_status.ForeColor = _muted;
		_status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		Button selectAllCurrent = new Button
		{
			Text = "Select All",
			Width = 96,
			Height = 31,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right),
			Top = 5
		};
		StyleButton(selectAllCurrent, primary: false);
		selectAllCurrent.Click += delegate
		{
			if (_tabs.SelectedIndex == 1)
			{
				SetAllPurgeChecks(value: true);
			}
			else if (_tabs.SelectedIndex == 4)
			{
				_warningsGrid.SelectAll();
			}
		};
		Action updateSelectAllState = () =>
		{
			selectAllCurrent.Enabled = _tabs.SelectedIndex == 1 || _tabs.SelectedIndex == 4;
		};
		_tabs.SelectedIndexChanged += delegate
		{
			updateSelectAllState();
		};
		updateSelectAllState();
		_export.Text = "Export CSV";
		_export.Width = 104;
		_export.Height = 31;
		_export.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		_export.Top = 5;
		_export.Click += delegate
		{
			ExportCsv();
		};
		StyleButton(_export, primary: false);
		Button close = new Button
		{
			Text = "Close",
			Width = 88,
			Height = 31,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right),
			Top = 5
		};
		close.Click += delegate
		{
			Close();
		};
		StyleButton(close, primary: false);
		footer.Resize += delegate
		{
			close.Left = footer.ClientSize.Width - close.Width - 4;
			_export.Left = close.Left - _export.Width - 10;
			selectAllCurrent.Left = _export.Left - selectAllCurrent.Width - 10;
		};
		footer.Controls.Add(_status);
		footer.Controls.Add(selectAllCurrent);
		footer.Controls.Add(_export);
		footer.Controls.Add(close);
		tableLayoutPanel.Controls.Add(footer, 0, 2);
	}

	private Button CreateRailButton(string glyph, string tooltip)
	{
		Button button = new Button
		{
			Text = glyph,
			Width = 42,
			Height = 42,
			Left = 8,
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(255, 249, 235),
			ForeColor = _gold,
			Font = new Font("Segoe UI Symbol", 19f, FontStyle.Regular),
			Cursor = Cursors.Hand,
			TabStop = false,
			AccessibleName = tooltip
		};
		button.FlatAppearance.BorderColor = _gold;
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.MouseOverBackColor = _selectedGold;
		return button;
	}

	private TabPage NewTab(string text)
	{
		return new TabPage(text)
		{
			BackColor = _page,
			ForeColor = _ink,
			Padding = new Padding(10),
			UseVisualStyleBackColor = false
		};
	}

	private TabPage BuildOverviewTab()
	{
		TabPage tabPage = NewTab("Overview");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			BackColor = _page,
			Padding = new Padding(4)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 116f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			BackColor = _page
		};
		for (int i = 0; i < 3; i++)
		{
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
		}
		AddMetricCard(tableLayoutPanel2, 0, "Cleanup candidates", _cleanupCount);
		AddMetricCard(tableLayoutPanel2, 1, "Views to review", _viewsCount);
		AddMetricCard(tableLayoutPanel2, 2, "Model warnings", _warningsCount);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		Label control = new Label
		{
			Dock = DockStyle.Fill,
			BackColor = _card,
			ForeColor = _ink,
			Padding = new Padding(18),
			Text = "WORKFLOW\r\n1  Scan the model for Revit-reported unused items, review suggestions, views to check and model warnings.\r\n2  Review each finding and its evidence. A view not placed on a sheet is only a suggestion, not proof it is unused.\r\n3  Smart Cleaner does not delete model content. Open Review & Clean to choose candidates and confirm any cleanup there.\r\n\r\nSafety note: keep views and resources that may still be needed for coordination, design or project standards."
		};
		tableLayoutPanel.Controls.Add(control, 0, 1);
		_summary.Dock = DockStyle.Fill;
		_summary.Text = "No scan yet.";
		_summary.ForeColor = _ink;
		_summary.TextAlign = ContentAlignment.MiddleLeft;
		_summary.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
		tableLayoutPanel.Controls.Add(_summary, 0, 2);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			BackColor = _page,
			Padding = new Padding(0, 12, 0, 0)
		};
		Button button = new Button
		{
			Text = "Review unused items",
			Width = 175,
			Height = 34
		};
		button.Click += delegate
		{
			_tabs.SelectedIndex = 1;
		};
		StyleButton(button, primary: true);
		flowLayoutPanel.Controls.Add(button);
		Button button2 = new Button
		{
			Text = "Review suggestions",
			Width = 170,
			Height = 34
		};
		button2.Click += delegate
		{
			_tabs.SelectedIndex = 2;
		};
		StyleButton(button2, primary: false);
		flowLayoutPanel.Controls.Add(button2);
		Button button3 = new Button
		{
			Text = "Review views",
			Width = 130,
			Height = 34
		};
		button3.Click += delegate
		{
			_tabs.SelectedIndex = 3;
		};
		StyleButton(button3, primary: false);
		flowLayoutPanel.Controls.Add(button3);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 3);
		tabPage.Controls.Add(tableLayoutPanel);
		return tabPage;
	}

	private void AddMetricCard(TableLayoutPanel parent, int col, string caption, Label value)
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = _card,
			Margin = new Padding(0, 0, 10, 0),
			Padding = new Padding(16)
		};
		Label value2 = new Label
		{
			Text = caption.ToUpperInvariant(),
			Left = 14,
			Top = 12,
			AutoSize = true,
			ForeColor = _muted,
			Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
		};
		value.Text = "—";
		value.Left = 14;
		value.Top = 38;
		value.AutoSize = true;
		value.ForeColor = _gold;
		value.Font = new Font("Segoe UI Semibold", 22f, FontStyle.Bold);
		panel.Controls.Add(value2);
		panel.Controls.Add(value);
		parent.Controls.Add(panel, col, 0);
	}

	private Panel CreateSearchFrame(TextBox search, int left, int top, int width, int height)
	{
		Panel frame = new Panel
		{
			Name = "BIMlifySearchOutline",
			Left = left,
			Top = top,
			Width = width,
			Height = height,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			BackColor = Color.White,
			Margin = Padding.Empty
		};
		Label value = new Label
		{
			Text = "⌕",
			Left = 5,
			Top = 1,
			Width = 20,
			Height = height - 2,
			ForeColor = _gold,
			BackColor = Color.White,
			Font = new Font("Segoe UI", 13f),
			TextAlign = ContentAlignment.MiddleCenter
		};
		search.BorderStyle = BorderStyle.None;
		search.SetBounds(34, 3, Math.Max(80, width - 40), height - 6);
		search.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		frame.Controls.Add(value);
		frame.Controls.Add(search);
		BIMlify.SearchControls.SearchGlyph.Attach(search, search == _reviewSearch ? "Search review items" : "Search unused items", false, true);
		frame.Paint += (object sender, PaintEventArgs e) =>
		{
			using Pen pen = new Pen(Color.FromArgb(178, 187, 197), 1.25f);
			e.Graphics.DrawRectangle(pen, 0, 0, frame.Width - 1, frame.Height - 1);
		};
		frame.Resize += delegate
		{
			frame.Invalidate();
		};
		return frame;
	}

	private TabPage BuildPurgeTab()
	{
		TabPage tabPage = NewTab("Unused suggestions");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			BackColor = _page
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = _page
		};
		Label value = new Label
		{
			Text = "Type",
			Left = 2,
			Top = 8,
			Width = 38,
			Height = 22,
			ForeColor = _muted,
			TextAlign = ContentAlignment.MiddleLeft
		};
		_kindFilter.SetBounds(42, 4, 148, 28);
		_kindFilter.DropDownStyle = ComboBoxStyle.DropDownList;
		_kindFilter.BackColor = _paleGold;
		_kindFilter.ForeColor = _ink;
		_kindFilter.FlatStyle = FlatStyle.Flat;
		Label value2 = new Label
		{
			Text = "Category",
			Left = 202,
			Top = 8,
			Width = 62,
			Height = 22,
			ForeColor = _muted,
			TextAlign = ContentAlignment.MiddleLeft
		};
		_categoryFilter.SetBounds(266, 4, 210, 28);
		_categoryFilter.DropDownStyle = ComboBoxStyle.DropDownList;
		_categoryFilter.BackColor = _paleGold;
		_categoryFilter.ForeColor = _ink;
		_categoryFilter.FlatStyle = FlatStyle.Flat;
		Label value3 = new Label
		{
			Text = "Search",
			Left = 488,
			Top = 8,
			Width = 54,
			Height = 22,
			ForeColor = _muted,
			TextAlign = ContentAlignment.MiddleLeft
		};
		Panel value4 = CreateSearchFrame(_candidateSearch, 544, 4, 360, 28);
		_candidateSearch.BackColor = Color.White;
		_candidateSearch.ForeColor = _ink;
		_candidateSearch.AccessibleDescription = "Search category, item name, type or ID";
		panel.Controls.Add(value);
		panel.Controls.Add(_kindFilter);
		panel.Controls.Add(value2);
		panel.Controls.Add(_categoryFilter);
		panel.Controls.Add(value3);
		panel.Controls.Add(value4);
		tableLayoutPanel.Controls.Add(panel, 0, 0);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = _page,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Padding = new Padding(0, 2, 0, 0)
		};
		Button button = new Button
		{
			Text = "Clear",
			Width = 82,
			Height = 31
		};
		button.Click += delegate
		{
			SetAllPurgeChecks(value: false);
		};
		StyleButton(button, primary: false);
		flowLayoutPanel.Controls.Add(button);
		Label value5 = new Label
		{
			Text = "Review-only suggestions based on Revit-reported unused items. Deletion is handled by Review & Clean.",
			AutoSize = true,
			ForeColor = _muted,
			Padding = new Padding(12, 8, 0, 0)
		};
		flowLayoutPanel.Controls.Add(value5);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 1);
		ConfigureGrid(_purgeGrid);
		_purgeGrid.Columns.Add(new DataGridViewCheckBoxColumn
		{
			Name = "Pick",
			HeaderText = "Review",
			Width = 52,
			FillWeight = 5f
		});
		_purgeGrid.Columns.Add(TextColumn("Kind", "Type", 12f));
		_purgeGrid.Columns.Add(TextColumn("Category", "Category", 15f));
		_purgeGrid.Columns.Add(TextColumn("Name", "Name", 24f));
		_purgeGrid.Columns.Add(TextColumn("Id", "Element ID", 8f));
		_purgeGrid.Columns.Add(TextColumn("Reason", "Reason / evidence", 28f));
		tableLayoutPanel.Controls.Add(_purgeGrid, 0, 2);
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = _page
		};
		_purgeDetailsCount.Text = "0 candidates";
		_purgeDetailsCount.Left = 0;
		_purgeDetailsCount.Top = 12;
		_purgeDetailsCount.Width = 600;
		_purgeDetailsCount.ForeColor = _muted;
		_purgeDetailsCount.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		panel2.Controls.Add(_purgeDetailsCount);
		tableLayoutPanel.Controls.Add(panel2, 0, 3);
		_kindFilter.Items.Add("All types");
		_kindFilter.SelectedIndex = 0;
		_categoryFilter.Items.Add("All categories");
		_categoryFilter.SelectedIndex = 0;
		_kindFilter.SelectedIndexChanged += delegate
		{
			if (!_updatingFilters)
			{
				RefreshPurgeGrid();
			}
		};
		_categoryFilter.SelectedIndexChanged += delegate
		{
			if (!_updatingFilters)
			{
				RefreshPurgeGrid();
			}
		};
		_candidateSearch.TextChanged += delegate
		{
			RefreshPurgeGrid();
		};
		_purgeGrid.CellValueChanged += (object sender, DataGridViewCellEventArgs e) =>
		{
			//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d4: Expected Obj, but got Unknown
			//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c0: Expected Obj, but got Unknown
			if (e.RowIndex >= 0 && e.ColumnIndex == _purgeGrid.Columns["Pick"].Index && long.TryParse(Convert.ToString(_purgeGrid.Rows[e.RowIndex].Cells["Id"].Value), out var result))
			{
				if (Convert.ToBoolean(_purgeGrid.Rows[e.RowIndex].Cells["Pick"].Value ?? ((object)false)))
				{
					_selectedPurgeIds.Add(new ElementId(result));
				}
				else
				{
					_selectedPurgeIds.Remove(new ElementId(result));
				}
			}
			UpdateCleanEnabled();
		};
		_purgeGrid.CurrentCellDirtyStateChanged += delegate
		{
			if (_purgeGrid.IsCurrentCellDirty)
			{
				_purgeGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
			}
		};
		EnableShiftCheckboxRange(_purgeGrid, () => _purgeRangeAnchor, (int purgeRangeAnchor) =>
		{
			_purgeRangeAnchor = purgeRangeAnchor;
		});
		_purgeGrid.RowsAdded += delegate
		{
			UpdateCleanEnabled();
		};
		_purgeGrid.RowsRemoved += delegate
		{
			UpdateCleanEnabled();
		};
		tabPage.Controls.Add(tableLayoutPanel);
		TabPage tabPage2 = tabPage;
		EmptyState.Attach(tabPage2, this);
		return tabPage2;
	}

	private TabPage BuildCategoryReviewTab()
	{
		TabPage result = NewTab("Category suggestions");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			BackColor = _page
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = _page
		};
		Label value = new Label
		{
			Text = "Category",
			Left = 2,
			Top = 8,
			Width = 62,
			Height = 22,
			ForeColor = _muted,
			TextAlign = ContentAlignment.MiddleLeft
		};
		_reviewGroupFilter.SetBounds(66, 4, 220, 28);
		_reviewGroupFilter.DropDownStyle = ComboBoxStyle.DropDownList;
		_reviewGroupFilter.BackColor = _paleGold;
		_reviewGroupFilter.ForeColor = _ink;
		_reviewGroupFilter.FlatStyle = FlatStyle.Flat;
		Label value2 = new Label
		{
			Text = "Search",
			Left = 302,
			Top = 8,
			Width = 54,
			Height = 22,
			ForeColor = _muted,
			TextAlign = ContentAlignment.MiddleLeft
		};
		Panel value3 = CreateSearchFrame(_reviewSearch, 358, 4, 546, 28);
		_reviewSearch.BackColor = Color.White;
		_reviewSearch.ForeColor = _ink;
		_reviewSearch.AccessibleDescription = "Search category, item name, type or ID";
		panel.Controls.Add(value);
		panel.Controls.Add(_reviewGroupFilter);
		panel.Controls.Add(value2);
		panel.Controls.Add(value3);
		tableLayoutPanel.Controls.Add(panel, 0, 0);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = _page,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Padding = new Padding(0, 2, 0, 0)
		};
		Button button = new Button
		{
			Text = "Select All",
			Width = 96,
			Height = 31
		};
		button.Click += delegate
		{
			SetAllReviewChecks(value: true);
		};
		StyleButton(button, primary: false);
		Button button2 = new Button
		{
			Text = "Clear",
			Width = 82,
			Height = 31
		};
		button2.Click += delegate
		{
			SetAllReviewChecks(value: false);
		};
		StyleButton(button2, primary: false);
		Label value4 = new Label
		{
			Text = "These are review-only suggestions, not proof an item is unused. Use Review & Clean to delete selected candidates.",
			AutoSize = true,
			ForeColor = _muted,
			Padding = new Padding(12, 8, 0, 0)
		};
		flowLayoutPanel.Controls.Add(button);
		flowLayoutPanel.Controls.Add(button2);
		flowLayoutPanel.Controls.Add(value4);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 1);
		ConfigureGrid(_reviewGrid);
		_reviewGrid.Columns.Add(new DataGridViewCheckBoxColumn
		{
			Name = "Pick",
			HeaderText = "Review",
			Width = 58,
			FillWeight = 6f
		});
		_reviewGrid.Columns.Add(TextColumn("Group", "Group", 16f));
		_reviewGrid.Columns.Add(TextColumn("Kind", "Type", 13f));
		_reviewGrid.Columns.Add(TextColumn("Name", "Name", 24f));
		_reviewGrid.Columns.Add(TextColumn("Id", "Element ID", 8f));
		_reviewGrid.Columns.Add(TextColumn("Reason", "Review note", 33f));
		tableLayoutPanel.Controls.Add(_reviewGrid, 0, 2);
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = _page
		};
		_reviewDetailsCount.Text = "0 review candidates";
		_reviewDetailsCount.Left = 0;
		_reviewDetailsCount.Top = 12;
		_reviewDetailsCount.Width = 570;
		_reviewDetailsCount.ForeColor = _muted;
		_reviewDetailsCount.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		panel2.Controls.Add(_reviewDetailsCount);
		tableLayoutPanel.Controls.Add(panel2, 0, 3);
		_reviewGroupFilter.Items.Add("All categories");
		_reviewGroupFilter.SelectedIndex = 0;
		_reviewGroupFilter.SelectedIndexChanged += delegate
		{
			if (!_updatingReviewFilters)
			{
				RefreshReviewGrid();
			}
		};
		_reviewSearch.TextChanged += delegate
		{
			RefreshReviewGrid();
		};
		_reviewGrid.CellValueChanged += (object sender, DataGridViewCellEventArgs e) =>
		{
			//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d4: Expected Obj, but got Unknown
			//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c0: Expected Obj, but got Unknown
			if (e.RowIndex >= 0 && e.ColumnIndex == _reviewGrid.Columns["Pick"].Index && long.TryParse(Convert.ToString(_reviewGrid.Rows[e.RowIndex].Cells["Id"].Value), out var result2))
			{
				if (Convert.ToBoolean(_reviewGrid.Rows[e.RowIndex].Cells["Pick"].Value ?? ((object)false)))
				{
					_selectedReviewIds.Add(new ElementId(result2));
				}
				else
				{
					_selectedReviewIds.Remove(new ElementId(result2));
				}
			}
			UpdateReviewDeleteEnabled();
		};
		_reviewGrid.CurrentCellDirtyStateChanged += delegate
		{
			if (_reviewGrid.IsCurrentCellDirty)
			{
				_reviewGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
			}
		};
		EnableShiftCheckboxRange(_reviewGrid, () => _reviewRangeAnchor, (int reviewRangeAnchor) =>
		{
			_reviewRangeAnchor = reviewRangeAnchor;
		});
		return result;
	}

	private TabPage BuildViewsTab()
	{
		TabPage tabPage = NewTab("Views audit");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			BackColor = _page
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Label control = new Label
		{
			Dock = DockStyle.Fill,
			BackColor = _card,
			ForeColor = _ink,
			Padding = new Padding(12),
			Text = "Review only: not being on a sheet does not prove a view is unused. Views are never included in Clean Selected."
		};
		tableLayoutPanel.Controls.Add(control, 0, 0);
		ConfigureGrid(_viewsGrid);
		_viewsGrid.Columns.Add(TextColumn("Name", "View", 22f));
		_viewsGrid.Columns.Add(TextColumn("ViewType", "View type", 14f));
		_viewsGrid.Columns.Add(TextColumn("Template", "View template", 17f));
		_viewsGrid.Columns.Add(TextColumn("Status", "Status", 15f));
		_viewsGrid.Columns.Add(TextColumn("Reason", "Review note", 32f));
		tableLayoutPanel.Controls.Add(_viewsGrid, 0, 1);
		tabPage.Controls.Add(tableLayoutPanel);
		return tabPage;
	}

	private TabPage BuildWarningsTab()
	{
		TabPage tabPage = NewTab("Warnings");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			BackColor = _page
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		Label control = new Label
		{
			Text = "Revit warnings affecting model elements. Select rows and locate their elements; warnings are never deleted by this tool.",
			Dock = DockStyle.Fill,
			ForeColor = _muted,
			TextAlign = ContentAlignment.MiddleLeft
		};
		tableLayoutPanel.Controls.Add(control, 0, 0);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = _page,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Padding = new Padding(0, 3, 0, 0)
		};
		Button button = new Button
		{
			Text = "Clear",
			Width = 82,
			Height = 31
		};
		StyleButton(button, primary: false);
		button.Click += delegate
		{
			_warningsGrid.ClearSelection();
		};
		flowLayoutPanel.Controls.Add(button);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 1);
		ConfigureGrid(_warningsGrid);
		_warningsGrid.MultiSelect = true;
		_warningsGrid.Columns.Add(TextColumn("Severity", "Severity", 10f));
		_warningsGrid.Columns.Add(TextColumn("Description", "Warning", 42f));
		_warningsGrid.Columns.Add(TextColumn("Count", "Elements", 9f));
		_warningsGrid.Columns.Add(TextColumn("Categories", "Categories", 24f));
		_warningsGrid.Columns.Add(TextColumn("Ids", "Element IDs", 15f));
		tableLayoutPanel.Controls.Add(_warningsGrid, 0, 2);
		Panel bottom = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = _page
		};
		_warningDetailsCount.Text = "0 warnings";
		_warningDetailsCount.Left = 0;
		_warningDetailsCount.Top = 12;
		_warningDetailsCount.Width = 650;
		_warningDetailsCount.ForeColor = _muted;
		_locate.Text = "Locate Elements";
		_locate.Width = 132;
		_locate.Height = 32;
		_locate.Top = 4;
		_locate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		_locate.Enabled = false;
		_locate.Click += delegate
		{
			EmptyState.PostCleanerAction(this, "LocateSelectedWarnings");
		};
		StyleButton(_locate, primary: true);
		bottom.Resize += delegate
		{
			_locate.Left = bottom.ClientSize.Width - _locate.Width - 3;
		};
		bottom.Controls.Add(_warningDetailsCount);
		bottom.Controls.Add(_locate);
		tableLayoutPanel.Controls.Add(bottom, 0, 3);
		_warningsGrid.SelectionChanged += delegate
		{
			_locate.Enabled = _warningsGrid.SelectedRows.Count > 0;
		};
		tabPage.Controls.Add(tableLayoutPanel);
		TabPage tabPage2 = tabPage;
		EmptyState.AttachWarnings(tabPage2, this);
		return tabPage2;
	}

	private DataGridViewTextBoxColumn TextColumn(string name, string title, float weight)
	{
		return new DataGridViewTextBoxColumn
		{
			Name = name,
			HeaderText = title,
			ReadOnly = true,
			FillWeight = weight,
			SortMode = DataGridViewColumnSortMode.Automatic
		};
	}

	private void ConfigureGrid(DataGridView grid)
	{
		grid.Dock = DockStyle.Fill;
		grid.AllowUserToAddRows = false;
		grid.AllowUserToDeleteRows = false;
		grid.ReadOnly = false;
		grid.RowHeadersVisible = false;
		grid.AutoGenerateColumns = false;
		grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
		grid.MultiSelect = true;
		grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
		grid.BackgroundColor = _card;
		grid.BorderStyle = BorderStyle.None;
		grid.GridColor = Color.FromArgb(220, 224, 229);
		grid.EnableHeadersVisualStyles = false;
		grid.ColumnHeadersHeight = 34;
		grid.RowTemplate.Height = 34;
		grid.Font = new Font("Segoe UI", 8.5f);
		grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 245);
		grid.ColumnHeadersDefaultCellStyle.ForeColor = _ink;
		grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
		grid.DefaultCellStyle.BackColor = _card;
		grid.DefaultCellStyle.ForeColor = _ink;
		grid.DefaultCellStyle.SelectionBackColor = _selectedGold;
		grid.DefaultCellStyle.SelectionForeColor = _ink;
		grid.DefaultCellStyle.Padding = new Padding(4);
		grid.CellFormatting += (object sender, DataGridViewCellFormattingEventArgs e) =>
		{
			if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && !grid.Rows[e.RowIndex].Selected)
			{
				grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = ((e.RowIndex % 2 == 0) ? Color.White : Color.FromArgb(250, 251, 252));
			}
		};
	}

	private void StyleButton(Button button, bool primary)
	{
		bool closeAction = string.Equals((button.Text ?? string.Empty).Trim(), "Close", StringComparison.OrdinalIgnoreCase);
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.BorderColor = (primary || closeAction ? _gold : Color.FromArgb(158, 169, 179));
		button.BackColor = (primary || closeAction ? Color.FromArgb(250, 230, 168) : Color.White);
		if (closeAction)
		{
			button.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 239, 190);
			button.FlatAppearance.MouseDownBackColor = Color.FromArgb(250, 230, 168);
		}
		button.ForeColor = _ink;
		button.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
		button.Cursor = Cursors.Hand;
	}

	private void DrawTab(object sender, DrawItemEventArgs e)
	{
		TabPage tabPage = _tabs.TabPages[e.Index];
		bool flag = e.Index == _tabs.SelectedIndex;
		using (SolidBrush brush = new SolidBrush(flag ? _paleGold : _page))
		{
			e.Graphics.FillRectangle(brush, e.Bounds);
		}
		using (Pen pen = new Pen(flag ? _gold : Color.FromArgb(207, 214, 221)))
		{
			e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
		}
		TextRenderer.DrawText(e.Graphics, tabPage.Text, Font, e.Bounds, _ink, TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
	}

	internal void ScanModel()
	{
		try
		{
			_scan.Enabled = false;
			_status.Text = "Scanning Revit model…";
			_purgeFindings.Clear();
			_reviewFindings.Clear();
			_viewFindings.Clear();
			_warningFindings.Clear();
			_selectedPurgeIds.Clear();
			_selectedReviewIds.Clear();
			Indicator.Start("Scanning unused items in Revit… Please wait.");
			ScanUnusedElements();
			Indicator.SetStage("Checking model elements and cleanup suggestions…");
			ScanCategoryReviewCandidates();
			Indicator.SetStage("Checking view placement…");
			ScanViews();
			Indicator.SetStage("Reading model warnings…");
			ScanWarnings();
			Indicator.SetStage("Preparing the results…");
			PopulateGrids();
			_status.Text = "Scan complete. Review category suggestions carefully; view and warning audits are review-only.";
		}
		catch (Exception ex)
		{
			_status.Text = "Scan failed: " + ex.Message;
			string message = ex.Message;
			Indicator.Stop();
			MessageBox.Show(this, message, "BIMlify — Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			Button scan = _scan;
			Indicator.Stop();
			scan.Enabled = true;
		}
	}

	private void ScanUnusedElements()
	{
		ISet<ElementId> unusedElements = _doc.GetUnusedElements((ISet<ElementId>)new HashSet<ElementId>());
		foreach (ElementId item in unusedElements)
		{
			Element element = _doc.GetElement(item);
			if (element != null)
			{
				string kind;
				if (element is Material)
				{
					kind = "Material";
				}
				else if (element is FillPatternElement)
				{
					kind = "Fill Pattern";
				}
				else if (element is LinePatternElement)
				{
					kind = "Line Pattern";
				}
				else if (element is View)
				{
					kind = "View";
				}
				else
				{
					kind = ((element is ElementType) ? "Family / Type" : ((object)element).GetType().Name);
				}
				_purgeFindings.Add(new PurgeFinding
				{
					Id = item,
					Kind = kind,
					Category = ((element.Category == null) ? "—" : element.Category.Name),
					Name = (string.IsNullOrWhiteSpace(element.Name) ? ((object)element).GetType().Name : element.Name),
					Reason = "Revit GetUnusedElements reports this element as unused."
				});
			}
		}
		_purgeFindings.Sort((PurgeFinding a, PurgeFinding b) =>
		{
			int num = string.Compare(a.Category, b.Category, StringComparison.CurrentCultureIgnoreCase);
			return (num != 0) ? num : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
		});
	}

	private void ScanCategoryReviewCandidates()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_070e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0713: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0698: Unknown result type (might be due to invalid IL or missing references)
		//IL_069e: Invalid comparison between Unknown and I4
		//IL_04ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_04cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_06a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_06a9: Invalid comparison between Unknown and I4
		//IL_06ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b7: Invalid comparison between Unknown and I4
		//IL_025f: Unknown result type (might be due to invalid IL or missing references)
		//IL_027e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0565: Unknown result type (might be due to invalid IL or missing references)
		//IL_031b: Unknown result type (might be due to invalid IL or missing references)
		//IL_05c3: Unknown result type (might be due to invalid IL or missing references)
		List<Element> list = new FilteredElementCollector(_doc).WhereElementIsNotElementType().ToElements().ToList();
		List<ElementType> list2 = ((IEnumerable)new FilteredElementCollector(_doc).WhereElementIsElementType()).Cast<ElementType>().ToList();
		HashSet<long> usedTypes = new HashSet<long>();
		HashSet<long> usedMaterials = new HashSet<long>();
		HashSet<long> referencedIds = new HashSet<long>();
		Action<Element> action = (Element element) =>
		{
			//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ae: Expected Obj, but got Unknown
			//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b7: Invalid comparison between Unknown and I4
			try
			{
				ElementId typeId = element.GetTypeId();
				if (typeId != ElementId.InvalidElementId)
				{
					usedTypes.Add(typeId.Value);
				}
			}
			catch
			{
			}
			try
			{
				foreach (ElementId materialId in element.GetMaterialIds(false))
				{
					if (materialId != ElementId.InvalidElementId)
					{
						usedMaterials.Add(materialId.Value);
					}
				}
			}
			catch
			{
			}
			try
			{
				foreach (Parameter parameter in element.Parameters)
				{
					Parameter val2 = parameter;
					if ((int)val2.StorageType == 4)
					{
						ElementId val3 = val2.AsElementId();
						if (!(val3 == ElementId.InvalidElementId))
						{
							referencedIds.Add(val3.Value);
							if (_doc.GetElement(val3) is Material)
							{
								usedMaterials.Add(val3.Value);
							}
						}
					}
				}
			}
			catch
			{
			}
		};
		foreach (Element item in list)
		{
			action(item);
		}
		foreach (ElementType item2 in list2)
		{
			action((Element)(object)item2);
		}
		foreach (ImportInstance item3 in ((IEnumerable)new FilteredElementCollector(_doc).OfClass(typeof(ImportInstance)).WhereElementIsNotElementType()).Cast<ImportInstance>())
		{
			string kind = "Imported CAD";
			try
			{
				if (item3.IsLinked)
				{
					kind = "Linked CAD";
				}
			}
			catch
			{
			}
			AddReviewFinding((Element)(object)item3, "CAD Imports", kind, "CAD instance listed for review; check its use and view ownership before removal.");
		}
		foreach (ElementType item4 in list2)
		{
			if (!usedTypes.Contains(((Element)item4).Id.Value))
			{
				AddReviewFinding((Element)(object)item4, "Families & Types", GetCategoryName((Element)(object)item4), "No placed instance using this type was detected; review before removal.");
			}
		}
		foreach (Material item5 in ((IEnumerable)new FilteredElementCollector(_doc).OfClass(typeof(Material))).Cast<Material>())
		{
			if (!usedMaterials.Contains(((Element)item5).Id.Value))
			{
				AddReviewFinding((Element)(object)item5, "Materials", "Material", "No direct, geometry, or type material use was detected; review linked assets before removal.");
			}
		}
		IEnumerable<Element> enumerable = new FilteredElementCollector(_doc).OfClass(typeof(FillPatternElement)).ToElements().Concat(new FilteredElementCollector(_doc).OfClass(typeof(LinePatternElement)).ToElements());
		foreach (Element item6 in enumerable)
		{
			if (!referencedIds.Contains(item6.Id.Value))
			{
				AddReviewFinding(item6, "Patterns", (item6 is FillPatternElement) ? "Fill Pattern" : "Line Pattern", "No direct ElementId reference was detected; review before removal.");
			}
		}
		List<View> list3 = ((IEnumerable)new FilteredElementCollector(_doc).OfClass(typeof(View))).Cast<View>().ToList();
		HashSet<long> hashSet = new HashSet<long>();
		HashSet<long> hashSet2 = new HashSet<long>();
		foreach (View item7 in list3.Where((View v) => !v.IsTemplate))
		{
			try
			{
				if (item7.ViewTemplateId != ElementId.InvalidElementId)
				{
					hashSet.Add(item7.ViewTemplateId.Value);
				}
			}
			catch
			{
			}
			try
			{
				foreach (ElementId filter in item7.GetFilters())
				{
					hashSet2.Add(filter.Value);
				}
			}
			catch
			{
			}
		}
		foreach (View item8 in list3.Where((View v) => v.IsTemplate))
		{
			if (!hashSet.Contains(((Element)item8).Id.Value))
			{
				AddReviewFinding((Element)(object)item8, "View Templates", "View Template", "No view currently uses this template; review before removal.");
			}
		}
		IEnumerable<Element> enumerable2 = new FilteredElementCollector(_doc).OfClass(typeof(ParameterFilterElement)).ToElements().Concat(new FilteredElementCollector(_doc).OfClass(typeof(SelectionFilterElement)).ToElements());
		foreach (Element item9 in enumerable2)
		{
			if (!hashSet2.Contains(item9.Id.Value))
			{
				AddReviewFinding(item9, "Filters", ((object)item9).GetType().Name, "No non-template view currently applies this filter; review before removal.");
			}
		}
		HashSet<long> hashSet3 = new HashSet<long>();
		foreach (Viewport item10 in ((IEnumerable)new FilteredElementCollector(_doc).OfClass(typeof(Viewport))).Cast<Viewport>())
		{
			hashSet3.Add(item10.ViewId.Value);
		}
		foreach (Element item11 in new FilteredElementCollector(_doc).OfClass(typeof(ScheduleSheetInstance)))
		{
			try
			{
				PropertyInfo property = ((object)item11).GetType().GetProperty("ScheduleId");
				object obj4;
				if (!(property == null))
				{
					object value = property.GetValue(item11, null);
					obj4 = ((value is ElementId) ? value : null);
				}
				else
				{
					obj4 = null;
				}
				ElementId val = (ElementId)obj4;
				if (val != (ElementId)null && val != ElementId.InvalidElementId)
				{
					hashSet3.Add(val.Value);
				}
			}
			catch
			{
			}
		}
		foreach (View item12 in list3)
		{
			if (!item12.IsTemplate && !(item12 is ViewSheet) && (int)item12.ViewType != 7 && (int)item12.ViewType != 12 && (int)item12.ViewType != 214)
			{
				bool flag = false;
				try
				{
					flag = item12.GetDependentViewIds().Count > 0;
				}
				catch
				{
				}
				if (!hashSet3.Contains(((Element)item12).Id.Value) && !flag)
				{
					AddReviewFinding((Element)(object)item12, "Unplaced Views", ((object)item12.ViewType/*cast due to constrained. prefix*/).ToString(), "Not placed on a sheet; this does not prove the view is unused.");
				}
			}
		}
		_reviewFindings.Sort((ReviewFinding a, ReviewFinding b) =>
		{
			int num = string.Compare(a.Group, b.Group, StringComparison.CurrentCultureIgnoreCase);
			return (num != 0) ? num : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
		});
	}

	private string GetCategoryName(Element element)
	{
		return (element.Category == null) ? "—" : element.Category.Name;
	}

	private void AddReviewFinding(Element element, string group, string kind, string reason)
	{
		if (element != null)
		{
			_reviewFindings.Add(new ReviewFinding
			{
				Id = element.Id,
				Group = group,
				Kind = kind,
				Name = (string.IsNullOrWhiteSpace(element.Name) ? ((object)element).GetType().Name : element.Name),
				Reason = reason
			});
		}
	}

	private void ScanViews()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_026a: Unknown result type (might be due to invalid IL or missing references)
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0303: Unknown result type (might be due to invalid IL or missing references)
		//IL_0308: Unknown result type (might be due to invalid IL or missing references)
		HashSet<long> hashSet = new HashSet<long>();
		foreach (Element item in new FilteredElementCollector(_doc).OfClass(typeof(Viewport)))
		{
			Viewport val = (Viewport)(object)((item is Viewport) ? item : null);
			if (val != null)
			{
				hashSet.Add(val.ViewId.Value);
			}
		}
		foreach (Element item2 in new FilteredElementCollector(_doc).OfClass(typeof(ScheduleSheetInstance)))
		{
			try
			{
				PropertyInfo property = ((object)item2).GetType().GetProperty("ScheduleId");
				object obj;
				if (!(property == null))
				{
					object value = property.GetValue(item2, null);
					obj = ((value is ElementId) ? value : null);
				}
				else
				{
					obj = null;
				}
				ElementId val2 = (ElementId)obj;
				if (val2 != (ElementId)null && val2 != ElementId.InvalidElementId)
				{
					hashSet.Add(val2.Value);
				}
			}
			catch
			{
			}
		}
		List<View> list = ((IEnumerable)new FilteredElementCollector(_doc).OfClass(typeof(View))).Cast<View>().Where((View view) =>
		{
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Invalid comparison between Unknown and I4
			return view != null && !view.IsTemplate && (int)view.ViewType != 214;
		}).ToList();
		HashSet<string> hashSet2 = new HashSet<string>(from @group in (from key in list.Select((View view) =>
				{
					//IL_0001: Unknown result type (might be due to invalid IL or missing references)
					//IL_0006: Unknown result type (might be due to invalid IL or missing references)
					return ((object)view.ViewType/*cast due to constrained. prefix*/).ToString() + "|" + NormalizeViewName(((Element)view).Name);
				})
				where !key.EndsWith("|", StringComparison.Ordinal)
				select key).GroupBy((string key) => key, StringComparer.CurrentCultureIgnoreCase)
			where @group.Count() > 1
			select @group.Key, StringComparer.CurrentCultureIgnoreCase);
		foreach (View item3 in list)
		{
			bool flag = hashSet.Contains(((Element)item3).Id.Value);
			string templateName = GetTemplateName(item3);
			string text = NormalizeViewName(((Element)item3).Name);
			bool flag2 = !string.IsNullOrEmpty(text) && hashSet2.Contains(((object)item3.ViewType/*cast due to constrained. prefix*/).ToString() + "|" + text);
			if (!flag | flag2)
			{
				string status;
				if (!flag & flag2)
				{
					status = "Review: unplaced + similar name";
				}
				else
				{
					status = ((!flag) ? "Review: not on sheet" : "Review: similar name");
				}
				string reason = ((!flag) ? "Not placed on a sheet; it may still be used for design or coordination. Do not treat this alone as proof it is unused." : "Another view of the same type has a similar normalized name. Compare settings and purpose manually.");
				_viewFindings.Add(new ViewFinding
				{
					Id = ((Element)item3).Id,
					Name = ((Element)item3).Name,
					ViewType = ((object)item3.ViewType/*cast due to constrained. prefix*/).ToString(),
					Template = templateName,
					Status = status,
					Reason = reason
				});
			}
		}
		_viewFindings.Sort((ViewFinding a, ViewFinding b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
	}

	private string GetTemplateName(View view)
	{
		try
		{
			PropertyInfo property = typeof(View).GetProperty("ViewTemplateId");
			if (property == null)
			{
				return "—";
			}
			object value = property.GetValue(view, null);
			ElementId val = (ElementId)((value is ElementId) ? value : null);
			Element val2 = ((val == (ElementId)null) ? null : _doc.GetElement(val));
			return (val2 == null) ? "None" : val2.Name;
		}
		catch
		{
			return "—";
		}
	}

	private string NormalizeViewName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return string.Empty;
		}
		string input = Regex.Replace(name.ToLowerInvariant(), "\\b(copy|duplicate|working|old|temp|temporary|final|rev\\.?\\s*\\d*)\\b", " ");
		input = Regex.Replace(input, "\\b\\d+\\b", " ");
		input = Regex.Replace(input, "[^a-z0-9]+", " ");
		return Regex.Replace(input, "\\s+", " ").Trim();
	}

	private void ScanWarnings()
	{
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		foreach (FailureMessage warning in _doc.GetWarnings())
		{
			List<ElementId> list = new List<ElementId>();
			try
			{
				list.AddRange(from id in warning.GetFailingElements()
					where _doc.GetElement(id) != null
					select id);
			}
			catch
			{
			}
			if (list.Count != 0)
			{
				string categories = string.Join(", ", (from id in list
					select _doc.GetElement(id) into e
					where e != null && e.Category != null
					select e.Category.Name).Distinct().OrderBy((string x) => x, StringComparer.CurrentCultureIgnoreCase));
				_warningFindings.Add(new WarningFinding
				{
					Severity = ((object)warning.GetSeverity()/*cast due to constrained. prefix*/).ToString(),
					Description = warning.GetDescriptionText(),
					Categories = categories,
					Ids = list.Distinct().ToList()
				});
			}
		}
		_warningFindings.Sort((WarningFinding a, WarningFinding b) =>
		{
			int num = string.Compare(a.Severity, b.Severity, StringComparison.CurrentCultureIgnoreCase);
			return (num != 0) ? num : string.Compare(a.Description, b.Description, StringComparison.CurrentCultureIgnoreCase);
		});
	}

	private void PopulateGrids()
	{
		RefreshPurgeFilters();
		RefreshReviewFilters();
		_viewsGrid.Rows.Clear();
		foreach (ViewFinding viewFinding in _viewFindings)
		{
			_viewsGrid.Rows.Add(viewFinding.Name, viewFinding.ViewType, viewFinding.Template, viewFinding.Status, viewFinding.Reason);
		}
		_warningsGrid.Rows.Clear();
		foreach (WarningFinding warningFinding in _warningFindings)
		{
			_warningsGrid.Rows.Add(warningFinding.Severity, warningFinding.Description, warningFinding.Ids.Count, warningFinding.Categories, string.Join(", ", warningFinding.Ids.Select((ElementId id) => id.Value.ToString())));
		}
		_purgeCount.Text = _purgeFindings.Count.ToString();
		_viewsCount.Text = _viewFindings.Count.ToString();
		_warningsCount.Text = _warningFindings.Count.ToString();
		_purgeDetailsCount.Text = _purgeFindings.Count + " Revit-unused candidate(s)";
		_warningDetailsCount.Text = _warningFindings.Count + " warning(s)";
		_cleanupCount.Text = (_purgeFindings.Count + _reviewFindings.Count).ToString();
		_summary.Text = "Scan complete: " + _purgeFindings.Count + " Revit-unused candidates, " + _reviewFindings.Count + " additional review suggestions, " + _viewFindings.Count + " views to review, " + _warningFindings.Count + " warnings. Smart Cleaner is review-only; use Review & Clean to remove selected candidates.";
		UpdateCleanEnabled();
		EmptyState.Refresh(this);
	}

	private void RefreshPurgeFilters()
	{
		if (_kindFilter.IsDisposed || _categoryFilter.IsDisposed)
		{
			return;
		}
		_updatingFilters = true;
		string value = Convert.ToString(_kindFilter.SelectedItem);
		string value2 = Convert.ToString(_categoryFilter.SelectedItem);
		_kindFilter.BeginUpdate();
		_categoryFilter.BeginUpdate();
		_kindFilter.Items.Clear();
		_categoryFilter.Items.Clear();
		_kindFilter.Items.Add("All types");
		foreach (string item in _purgeFindings.Select((PurgeFinding x) => x.Kind).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy((string x) => x, StringComparer.CurrentCultureIgnoreCase))
		{
			_kindFilter.Items.Add(item);
		}
		_categoryFilter.Items.Add("All categories");
		foreach (string item2 in _purgeFindings.Select((PurgeFinding x) => x.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy((string x) => x, StringComparer.CurrentCultureIgnoreCase))
		{
			_categoryFilter.Items.Add(item2);
		}
		_kindFilter.SelectedIndex = Math.Max(0, _kindFilter.Items.IndexOf(value));
		_categoryFilter.SelectedIndex = Math.Max(0, _categoryFilter.Items.IndexOf(value2));
		_kindFilter.EndUpdate();
		_categoryFilter.EndUpdate();
		_updatingFilters = false;
		RefreshPurgeGrid();
	}

	private void RefreshPurgeGrid()
	{
		if (_purgeGrid.IsDisposed || _kindFilter.SelectedIndex < 0 || _categoryFilter.SelectedIndex < 0)
		{
			return;
		}
		string kind = ((_kindFilter.SelectedIndex == 0) ? null : Convert.ToString(_kindFilter.SelectedItem));
		string category = ((_categoryFilter.SelectedIndex == 0) ? null : Convert.ToString(_categoryFilter.SelectedItem));
		string query = (_candidateSearch.Text ?? string.Empty).Trim();
		List<PurgeFinding> list = _purgeFindings.Where((PurgeFinding item) => (kind == null || string.Equals(item.Kind, kind, StringComparison.CurrentCultureIgnoreCase)) && (category == null || string.Equals(item.Category, category, StringComparison.CurrentCultureIgnoreCase)) && (query.Length == 0 || item.Kind.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.Category.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.Id.Value.ToString().IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.Reason.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
		_purgeRangeAnchor = -1;
		_purgeGrid.Rows.Clear();
		foreach (PurgeFinding item in list)
		{
			_purgeGrid.Rows.Add(_selectedPurgeIds.Contains(item.Id), item.Kind, item.Category, item.Name, item.Id.Value.ToString(), item.Reason);
		}
		_purgeDetailsCount.Text = list.Count + " shown / " + _purgeFindings.Count + " candidate(s)";
		UpdateCleanEnabled();
	}

	private void RefreshReviewFilters()
	{
		_updatingReviewFilters = true;
		string value = Convert.ToString(_reviewGroupFilter.SelectedItem);
		_reviewGroupFilter.BeginUpdate();
		_reviewGroupFilter.Items.Clear();
		_reviewGroupFilter.Items.Add("All categories");
		foreach (string item in _reviewFindings.Select((ReviewFinding x) => x.Group).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy((string x) => x, StringComparer.CurrentCultureIgnoreCase))
		{
			_reviewGroupFilter.Items.Add(item);
		}
		int num = _reviewGroupFilter.Items.IndexOf(value);
		_reviewGroupFilter.SelectedIndex = ((num >= 0) ? num : 0);
		_reviewGroupFilter.EndUpdate();
		_updatingReviewFilters = false;
		RefreshReviewGrid();
	}

	private void RefreshReviewGrid()
	{
		if (_reviewGrid.IsDisposed || _reviewGroupFilter.SelectedIndex < 0)
		{
			return;
		}
		string group = ((_reviewGroupFilter.SelectedIndex == 0) ? null : Convert.ToString(_reviewGroupFilter.SelectedItem));
		string query = (_reviewSearch.Text ?? string.Empty).Trim();
		List<ReviewFinding> list = _reviewFindings.Where((ReviewFinding item) => (group == null || string.Equals(item.Group, group, StringComparison.CurrentCultureIgnoreCase)) && (query.Length == 0 || item.Group.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.Kind.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.Id.Value.ToString().IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.Reason.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
		_reviewRangeAnchor = -1;
		_reviewGrid.Rows.Clear();
		foreach (ReviewFinding item in list)
		{
			_reviewGrid.Rows.Add(_selectedReviewIds.Contains(item.Id), item.Group, item.Kind, item.Name, item.Id.Value.ToString(), item.Reason);
		}
		_reviewDetailsCount.Text = list.Count + " shown / " + _reviewFindings.Count + " review candidate(s)";
		UpdateReviewDeleteEnabled();
	}

	private void SetAllReviewChecks(bool value)
	{
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Expected Obj, but got Unknown
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Expected Obj, but got Unknown
		foreach (DataGridViewRow item in (IEnumerable)_reviewGrid.Rows)
		{
			if (long.TryParse(Convert.ToString(item.Cells["Id"].Value), out var result))
			{
				if (value)
				{
					_selectedReviewIds.Add(new ElementId(result));
				}
				else
				{
					_selectedReviewIds.Remove(new ElementId(result));
				}
			}
		}
		foreach (DataGridViewRow item2 in (IEnumerable)_reviewGrid.Rows)
		{
			item2.Cells["Pick"].Value = value;
		}
		UpdateReviewDeleteEnabled();
	}

	private void UpdateReviewDeleteEnabled()
	{
		if (_deleteReviewed != null && !_deleteReviewed.IsDisposed)
		{
			_deleteReviewed.Enabled = _selectedReviewIds.Count > 0;
		}
	}

	private void DeleteReviewedCandidates()
	{
	}

	private void EnableShiftCheckboxRange(DataGridView grid, Func<int> getAnchor, Action<int> setAnchor)
	{
		int pickColumn = grid.Columns["Pick"].Index;
		grid.CellMouseDown += (object sender, DataGridViewCellMouseEventArgs e) =>
		{
			if (e.RowIndex >= 0)
			{
				bool flag = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
				bool flag2 = (Control.ModifierKeys & Keys.Control) == Keys.Control;
				if (((e.ColumnIndex == pickColumn) & flag) && getAnchor() >= 0)
				{
					int first = Math.Min(getAnchor(), e.RowIndex);
					int last = Math.Max(getAnchor(), e.RowIndex);
					bool check = !Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[pickColumn].Value ?? ((object)false));
					setAnchor(e.RowIndex);
					grid.BeginInvoke((Action)(() =>
					{
						if (!grid.IsDisposed)
						{
							for (int i = first; i <= last && i < grid.Rows.Count; i++)
							{
								grid.Rows[i].Cells[pickColumn].Value = check;
							}
						}
					}));
				}
				else if (!flag)
				{
					setAnchor(e.RowIndex);
					if (flag2 && e.ColumnIndex != pickColumn)
					{
						grid.BeginInvoke((Action)(() =>
						{
							if (!grid.IsDisposed && e.RowIndex < grid.Rows.Count)
							{
								DataGridViewCell dataGridViewCell = grid.Rows[e.RowIndex].Cells[pickColumn];
								dataGridViewCell.Value = !Convert.ToBoolean(dataGridViewCell.Value ?? ((object)false));
							}
						}));
					}
				}
			}
		};
	}

	private void SetAllPurgeChecks(bool value)
	{
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Expected Obj, but got Unknown
		if (value)
		{
			foreach (PurgeFinding purgeFinding in _purgeFindings)
			{
				_selectedPurgeIds.Add(purgeFinding.Id);
			}
		}
		else
		{
			_selectedPurgeIds.Clear();
		}
		foreach (DataGridViewRow item in (IEnumerable)_purgeGrid.Rows)
		{
			if (long.TryParse(Convert.ToString(item.Cells["Id"].Value), out var result))
			{
				item.Cells["Pick"].Value = _selectedPurgeIds.Contains(new ElementId(result));
			}
		}
		UpdateCleanEnabled();
	}

	private int SelectedPurgeCount()
	{
		return _selectedPurgeIds.Count;
	}

	private void UpdateCleanEnabled()
	{
		if (_clean != null && !_clean.IsDisposed)
		{
			_clean.Enabled = SelectedPurgeCount() > 0;
		}
	}

	private void CleanSelected()
	{
	}

	private void LocateSelectedWarnings()
	{
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Expected Obj, but got Unknown
		try
		{
			if (_tabs.SelectedTab != null && (_tabs.SelectedTab.Text ?? string.Empty).IndexOf("Unused", StringComparison.CurrentCultureIgnoreCase) >= 0)
			{
				HashSet<ElementId> selectedDefinitions = new HashSet<ElementId>();
				foreach (DataGridViewRow row in _purgeGrid.Rows)
				{
					if (row.IsNewRow || !Convert.ToBoolean(row.Cells["Pick"].Value ?? false))
					{
						continue;
					}
					if (long.TryParse(Convert.ToString(row.Cells["Id"].Value), out long id))
					{
						selectedDefinitions.Add(new ElementId(id));
					}
				}
				HashSet<ElementId> placedInstances = new HashSet<ElementId>();
				if (selectedDefinitions.Count > 0)
				{
					foreach (Element element in new FilteredElementCollector(_doc).WhereElementIsNotElementType())
					{
						if (selectedDefinitions.Contains(element.GetTypeId()))
						{
							placedInstances.Add(element.Id);
						}
					}
				}
				if (placedInstances.Count > 0)
				{
					_ui.Selection.SetElementIds((ICollection<ElementId>)placedInstances);
					_ui.ShowElements((ICollection<ElementId>)placedInstances);
					_status.Text = "Located " + placedInstances.Count + " placed instance(s) for the selected definition(s).";
				}
				else
				{
					_status.Text = selectedDefinitions.Count == 0
						? "Select unused items with the checkboxes, then choose Locate Elements."
						: "No placed model instances were found. These unused definitions have no location to zoom to.";
				}
				return;
			}
			HashSet<ElementId> hashSet = new HashSet<ElementId>();
			foreach (DataGridViewRow selectedRow in _warningsGrid.SelectedRows)
			{
				string text = Convert.ToString(selectedRow.Cells["Ids"].Value);
				string[] array = text.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (string text2 in array)
				{
					if (long.TryParse(text2.Trim(), out var result))
					{
						ElementId val = new ElementId(result);
						if (_doc.GetElement(val) != null)
						{
							hashSet.Add(val);
						}
					}
				}
			}
			if (hashSet.Count != 0)
			{
				_ui.Selection.SetElementIds((ICollection<ElementId>)hashSet);
				_ui.ShowElements((ICollection<ElementId>)hashSet);
				_status.Text = "Selected " + hashSet.Count + " warning element(s) in the model.";
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "Could not locate elements", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}

	private void ExportCsv()
	{
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "CSV files (*.csv)|*.csv",
			FileName = "BIMlify_Model_Audit.csv",
			Title = "Export model audit"
		};
		if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		using (StreamWriter streamWriter = new StreamWriter(saveFileDialog.FileName, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
		{
			streamWriter.WriteLine("Finding Type,Name,Category / View Type,Status,Element ID,Reason / Details");
			foreach (PurgeFinding purgeFinding in _purgeFindings)
			{
				streamWriter.WriteLine(Csv("Unused candidate") + "," + Csv(purgeFinding.Name) + "," + Csv(purgeFinding.Kind + " / " + purgeFinding.Category) + "," + Csv("Revit reports unused") + "," + Csv(purgeFinding.Id.Value.ToString()) + "," + Csv(purgeFinding.Reason));
			}
			foreach (ReviewFinding reviewFinding in _reviewFindings)
			{
				streamWriter.WriteLine(Csv("Category review") + "," + Csv(reviewFinding.Name) + "," + Csv(reviewFinding.Group + " / " + reviewFinding.Kind) + "," + Csv("Review suggestion") + "," + Csv(reviewFinding.Id.Value.ToString()) + "," + Csv(reviewFinding.Reason));
			}
			foreach (ViewFinding viewFinding in _viewFindings)
			{
				streamWriter.WriteLine(Csv("View audit") + "," + Csv(viewFinding.Name) + "," + Csv(viewFinding.ViewType) + "," + Csv(viewFinding.Status) + "," + Csv(viewFinding.Id.Value.ToString()) + "," + Csv(viewFinding.Reason + " Template: " + viewFinding.Template));
			}
			foreach (WarningFinding warningFinding in _warningFindings)
			{
				streamWriter.WriteLine(Csv("Warning") + "," + Csv(warningFinding.Description) + "," + Csv(warningFinding.Categories) + "," + Csv(warningFinding.Severity + "; " + warningFinding.Ids.Count + " element(s)") + "," + Csv(string.Join(";", warningFinding.Ids.Select((ElementId id) => id.Value.ToString()))) + "," + Csv("Review in Revit; no automatic deletion."));
			}
		}
		_status.Text = "Audit exported to " + saveFileDialog.FileName;
	}

	private string Csv(string value)
	{
		return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
	}
}



