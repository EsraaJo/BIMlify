using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using Control = System.Windows.Forms.Control;
using ComboBox = System.Windows.Forms.ComboBox;
using Panel = System.Windows.Forms.Panel;
using TextBox = System.Windows.Forms.TextBox;
using Button = System.Windows.Forms.Button;
using Rectangle = System.Drawing.Rectangle;
using Form = System.Windows.Forms.Form;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMlify.SmartOpenings;

internal sealed class SmartOpeningsForm : UserControl
{
	private readonly OpeningsEvent handler;

	private readonly DataGridView mepPreview = MakePreview();

	private readonly DataGridView structPreview = MakePreview();

	private readonly NumericUpDown mepClearance = new NumericUpDown();

	private readonly NumericUpDown structClearance = new NumericUpDown();

	private readonly NumericUpDown width = new NumericUpDown();

	private readonly NumericUpDown height = new NumericUpDown();

	private readonly ComboBox mepShape = new ComboBox();

	private readonly ComboBox structShape = new ComboBox();

	private readonly ComboBox mepSource = new ComboBox();

	private readonly ComboBox structSource = new ComboBox();

	private readonly Button create;

	private readonly Label status = new Label
	{
		Text = "Choose MEP openings or structural openings to begin.",
		Font = new Font("Segoe UI", 8f)
	};

	private readonly TabControl tabs;

	internal double ClearanceMm = 50.0;

	internal double WidthMm = 300.0;

	internal double HeightMm = 300.0;

	internal string Shape = "Auto by service";

	internal long SelectedSourceLinkId => ((tabs.SelectedIndex == 1) ? (structSource.SelectedItem as OpeningSource) : (mepSource.SelectedItem as OpeningSource))?.LinkInstanceId ?? 0;

	internal SmartOpeningsForm(UIDocument ui)
	{
		Text = "BIMlify — Smart Openings";
		Dock = DockStyle.Fill;
		Font = new Font("Segoe UI", 9f);
		BackColor = Color.FromArgb(238, 241, 245);
		AutoScaleMode = AutoScaleMode.Dpi;
		tabs = new TabControl
		{
			Dock = DockStyle.Fill,
			Padding = new Point(14, 5),
			Font = new Font("Segoe UI", 9f),
			BackColor = Color.FromArgb(238, 241, 245)
		};
		TabPage tabPage = new TabPage("MEP openings")
		{
			BackColor = Color.FromArgb(238, 241, 245),
			Padding = new Padding(8)
		};
		TabPage tabPage2 = new TabPage("Structural openings")
		{
			BackColor = Color.FromArgb(238, 241, 245),
			Padding = new Padding(8)
		};
		tabs.TabPages.Add(tabPage);
		tabs.TabPages.Add(tabPage2);
		tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
		tabs.SizeMode = TabSizeMode.Fixed;
		tabs.ItemSize = new Size(170, 30);
		tabs.DrawItem += (object s, DrawItemEventArgs e) =>
		{
			if (e.Index >= 0 && e.Index < tabs.TabPages.Count)
			{
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				bool flag = e.Index == tabs.SelectedIndex;
				using (SolidBrush brush = new SolidBrush(flag ? Color.FromArgb(247, 247, 245) : Color.FromArgb(216, 221, 227)))
				{
					e.Graphics.FillRectangle(brush, e.Bounds);
				}
				using (Font font2 = new Font("Segoe UI", 9f, flag ? FontStyle.Bold : FontStyle.Regular))
				{
					TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, font2, e.Bounds, Color.FromArgb(78, 85, 92), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
				}
				using (Pen pen = new Pen(Color.FromArgb(185, 185, 185), 1f))
				{
					e.Graphics.DrawRectangle(pen, e.Bounds.Left, e.Bounds.Top, e.Bounds.Width - 1, e.Bounds.Height - 1);
				}
				if (flag)
				{
					using (Pen pen2 = new Pen(Color.FromArgb(198, 151, 42), 3f))
					{
						e.Graphics.DrawLine(pen2, e.Bounds.Left + 2, e.Bounds.Bottom - 2, e.Bounds.Right - 3, e.Bounds.Bottom - 2);
					}
				}
			}
		};
		tabs.SelectedIndexChanged += (object s, EventArgs e) =>
		{
			tabs.Invalidate();
		};
		ConfigureCombo(mepShape, "Auto by service", "Circular", "Rectangular");
		ConfigureCombo(structShape, "Rectangular", "Circular");
		ConfigureNumber(mepClearance, 0m, 500m, 50m);
		ConfigureNumber(structClearance, 0m, 500m, 50m);
		ConfigureNumber(width, 50m, 10000m, 300m);
		ConfigureNumber(height, 50m, 10000m, 300m);
		ConfigureSources(ui.Document, mepSource);
		ConfigureSources(ui.Document, structSource);
		Font font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
		mepSource.Font = font;
		mepShape.Font = font;
		structSource.Font = font;
		structShape.Font = font;
		Panel mepOptions = new Panel
		{
			Dock = DockStyle.Top,
			Height = 92,
			BackColor = Color.White,
			Padding = Padding.Empty
		};
		Color white = Color.White;
		Color foreColor = Color.FromArgb(49, 82, 111);
		Label mepSourceLabel = new Label
		{
			Text = "Service source",
			AutoSize = false,
			Height = 28,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = white,
			ForeColor = foreColor
		};
		Label mepShapeLabel = new Label
		{
			Text = "Shape",
			AutoSize = false,
			Height = 36,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = white,
			ForeColor = foreColor
		};
		Label mepClearanceLabel = new Label
		{
			Text = "Clearance (mm)",
			AutoSize = false,
			Height = 36,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = white,
			ForeColor = foreColor
		};
		mepOptions.Controls.AddRange(new Control[6] { mepSourceLabel, mepSource, mepShapeLabel, mepShape, mepClearanceLabel, mepClearance });
		Action layoutMepOptions = () =>
		{
			int num = Math.Max(400, mepOptions.ClientSize.Width - 24);
			// Use the same left edge as the structural source and shape dropdowns.
			int num2 = 98;
			int num3 = num2;
			int num4 = 120;
			int num5 = 84;
			int num6 = 12;
			int val = Math.Max(180, num - num3 - num4 - num5 - num6 * 3);
			int num7 = Math.Max(180, Math.Min(520, val));
			int num8 = 12;
			mepSourceLabel.SetBounds(num8, 12, num2, 28);
			mepSource.SetBounds(num8 + num2 + num6, 12, num7, 28);
			mepShapeLabel.SetBounds(num8, 49, num3, 36);
			num8 += num3 + num6;
			mepShape.SetBounds(num8, 53, num7, 28);
			num8 += num7 + num6;
			mepClearanceLabel.SetBounds(num8, 49, num4, 36);
			num8 += num4 + num6;
			mepClearance.SetBounds(num8, 53, num5, 28);
		};
		layoutMepOptions();
		mepOptions.Resize += (object s, EventArgs e) =>
		{
			layoutMepOptions();
		};
		Label value = new Label
		{
			Text = "Select hosts and services manually, or use Auto-create to scan the host model and loaded links.",
			Dock = DockStyle.Top,
			Height = 38,
			Padding = new Padding(12, 7, 8, 0),
			ForeColor = Color.FromArgb(78, 85, 92)
		};
		Panel panel = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 48,
			BackColor = Color.White
		};
		Button button = MakeButton("Select hosts and services", 190, primary: true);
		button.SetBounds(12, 8, 190, 32);
		Button button2 = MakeButton("Auto-create all openings", 210, primary: true);
		button2.SetBounds(214, 8, 210, 32);
		panel.Controls.Add(button);
		panel.Controls.Add(button2);
		tabPage.Controls.Add(mepPreview);
		tabPage.Controls.Add(panel);
		tabPage.Controls.Add(value);
		tabPage.Controls.Add(mepOptions);
		Panel structOptions = new Panel
		{
			Dock = DockStyle.Top,
			Height = 92,
			BackColor = Color.White,
			Padding = Padding.Empty
		};
		Color white2 = Color.White;
		Color foreColor2 = Color.FromArgb(49, 82, 111);
		Label structSourceLabel = new Label
		{
			Text = "Service source",
			AutoSize = false,
			Height = 28,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = white2,
			ForeColor = foreColor2
		};
		Label structShapeLabel = new Label
		{
			Text = "Shape",
			AutoSize = false,
			Height = 36,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = white2,
			ForeColor = foreColor2
		};
		Label widthLabel = new Label
		{
			Text = "Width / diameter (mm)",
			AutoSize = false,
			Height = 36,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = white2,
			ForeColor = foreColor2
		};
		Label heightLabel = new Label
		{
			Text = "Height (mm)",
			AutoSize = false,
			Height = 36,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = white2,
			ForeColor = foreColor2
		};
		Label structClearanceLabel = new Label
		{
			Text = "Clearance (mm)",
			AutoSize = false,
			Height = 36,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = white2,
			ForeColor = foreColor2
		};
		structOptions.Controls.AddRange(new Control[10] { structSourceLabel, structSource, structShapeLabel, structShape, widthLabel, width, heightLabel, height, structClearanceLabel, structClearance });
		Action layoutStructOptions = () =>
		{
			int num = Math.Max(400, structOptions.ClientSize.Width - 24);
			structSourceLabel.SetBounds(12, 12, 98, 28);
			structSource.SetBounds(122, 12, Math.Max(220, Math.Min(520, num - 110)), 28);
			int num2 = 98;
			int num3 = 120;
			int num4 = 160;
			int num5 = 70;
			int num6 = 100;
			int num7 = 70;
			int num8 = 120;
			int num9 = 70;
			int num10 = num2 + num3 + num4 + num5 + num6 + num7 + num8 + num9;
			int num11 = Math.Max(4, (num - num10) / 7);
			int num12 = 12;
			structShapeLabel.SetBounds(num12, 49, num2, 36);
			// Align the shape dropdown with the service-source dropdown above.
			structShape.SetBounds(122, 53, num3, 28);
			num12 = 122 + num3 + num11;
			widthLabel.SetBounds(num12, 49, num4, 36);
			num12 += num4 + num11;
			width.SetBounds(num12, 53, num5, 28);
			num12 += num5 + num11;
			heightLabel.SetBounds(num12, 49, num6, 36);
			num12 += num6 + num11;
			height.SetBounds(num12, 53, num7, 28);
			num12 += num7 + num11;
			structClearanceLabel.SetBounds(num12, 49, num8, 36);
			num12 += num8 + num11;
			structClearance.SetBounds(num12, 53, num9, 28);
		};
		layoutStructOptions();
		structOptions.Resize += (object s, EventArgs e) =>
		{
			layoutStructOptions();
		};
		Label value2 = new Label
		{
			Text = "Pick a host and centre point. Revit's native wall openings are rectangular.",
			Dock = DockStyle.Top,
			Height = 38,
			Padding = new Padding(12, 7, 8, 0),
			ForeColor = Color.FromArgb(78, 85, 92)
		};
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 48,
			BackColor = Color.White
		};
		Button button3 = MakeButton("Pick host and opening centre", 220);
		button3.SetBounds(12, 8, 220, 32);
		Button button4 = MakeButton("Auto-create all openings", 210, primary: true);
		button4.SetBounds(244, 8, 210, 32);
		panel2.Controls.Add(button3);
		panel2.Controls.Add(button4);
		tabPage2.Controls.Add(structPreview);
		tabPage2.Controls.Add(panel2);
		tabPage2.Controls.Add(value2);
		tabPage2.Controls.Add(structOptions);
		Panel header = new Panel
		{
			Dock = DockStyle.Top,
			Height = 128,
			BackColor = Color.FromArgb(247, 247, 245),
			Padding = new Padding(18, 9, 4, 9)
		};
		Label title = new Label
		{
			Text = "Smart Openings",
			AutoSize = false,
			Bounds = new Rectangle(18, 12, 650, 24),
			Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
			ForeColor = Color.FromArgb(78, 85, 92),
			BackColor = header.BackColor,
			TextAlign = ContentAlignment.MiddleLeft,
			Padding = Padding.Empty
		};
		Label subtitle = new Label
		{
			Text = "Preview and create MEP and structural openings in your model.",
			AutoSize = false,
			Bounds = new Rectangle(18, 39, 680, 52),
			Font = new Font("Segoe UI", 8f),
			ForeColor = Color.FromArgb(102, 109, 118),
			BackColor = header.BackColor,
			TextAlign = ContentAlignment.TopLeft,
			Padding = Padding.Empty
		};
		header.Controls.Add(title);
		header.Controls.Add(subtitle);
		AddBrandLockup(header);
		AddSharedHeaderAccent(header);
		header.Resize += (object s, EventArgs e) =>
		{
			int num = Math.Max(300, header.ClientSize.Width - 240);
			title.Width = num;
			subtitle.Width = num;
		};
		Button button5 = MakeButton("Close", 92);
		create = MakeButton("Create openings", 132, primary: true);
		create.Enabled = false;
		Button locate = MakeButton("Locate Elements", 122);
        locate.Enabled = false;
        Panel value3 = CreateSharedFooter(status, locate, create, button5);
		Panel panel3 = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(14, 12, 14, 5),
			BackColor = Color.FromArgb(238, 241, 245)
		};
		panel3.Controls.Add(tabs);
		Controls.Add(panel3);
		Controls.Add(value3);
		Controls.Add(header);
		button5.Click += (object s, EventArgs e) =>
		{
			FindForm()?.Close();
		};
        BIMlify.CleanerUi.DashboardEnhancements.ApplyLightGoldDropdowns(this);
		handler = new OpeningsEvent(this);
        Func<DataGridView> activeGrid = () => tabs.SelectedIndex == 1 ? structPreview : mepPreview;
        Action updateLocate = () => locate.Enabled = activeGrid().CurrentRow?.Tag is OpeningCandidate;
        locate.Click += (sender, args) => LocateOpening(activeGrid(), activeGrid().CurrentRow?.Index ?? -1);
        mepPreview.SelectionChanged += (sender, args) => updateLocate();
        structPreview.SelectionChanged += (sender, args) => updateLocate();
        tabs.SelectedIndexChanged += (sender, args) => updateLocate();
        mepPreview.CellDoubleClick += (sender, args) => LocateOpening(mepPreview, args.RowIndex);
        structPreview.CellDoubleClick += (sender, args) => LocateOpening(structPreview, args.RowIndex);
		button.Click += (object s, EventArgs e) =>
		{
			ClearanceMm = (double)mepClearance.Value;
			Shape = mepShape.SelectedItem?.ToString() ?? "";
			handler.Request("mep");
		};
		button2.Click += (object s, EventArgs e) =>
		{
			if (SelectedSourceLinkId == 0)
			{
				ShowAfterAction("Choose a service source model before processing.");
			}
			else
			{
				ClearanceMm = (double)mepClearance.Value;
				Shape = mepShape.SelectedItem?.ToString() ?? "";
				handler.Request("auto");
			}
		};
		button3.Click += (object s, EventArgs e) =>
		{
			ClearanceMm = (double)structClearance.Value;
			WidthMm = (double)width.Value;
			HeightMm = (double)height.Value;
			Shape = structShape.SelectedItem?.ToString() ?? "";
			handler.Request("structural");
		};
		button4.Click += (object s, EventArgs e) =>
		{
			if (SelectedSourceLinkId == 0)
			{
				ShowAfterAction("Choose a service source model before processing.");
			}
			else
			{
				ClearanceMm = (double)structClearance.Value;
				Shape = structShape.SelectedItem?.ToString() ?? "";
				handler.Request("auto-structural");
			}
		};
		create.Click += (object s, EventArgs e) =>
		{
			if (MessageBox.Show(this, "Create the previewed openings in the model?", "Smart Openings", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				handler.Request("create");
			}
		};
		Disposed += (object s, EventArgs e) =>
		{
			handler.Dispose();
		};
	}

	private static void ConfigureSources(Document document, ComboBox combo)
	{
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		combo.DropDownStyle = ComboBoxStyle.DropDownList;
		combo.Font = new Font("Segoe UI", 8.5f);
		combo.Items.Add(new OpeningSource
		{
			Name = "Select a service source model…",
			LinkInstanceId = 0L
		});
		combo.Items.Add(new OpeningSource
		{
			Name = "Host model",
			LinkInstanceId = -1L
		});
		foreach (RevitLinkInstance item in ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance))).Cast<RevitLinkInstance>())
		{
			try
			{
				if (item.GetLinkDocument() != null)
				{
					combo.Items.Add(new OpeningSource
					{
						Name = ((Element)item).Name + " [" + ((Element)item).Id.Value + "]",
						LinkInstanceId = ((Element)item).Id.Value
					});
				}
			}
			catch
			{
			}
		}
		combo.SelectedIndex = 0;
	}

	private void AddSharedHeaderAccent(Control header)
	{
		try
		{
			((from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null)?.GetMethod("AddYellowPanelAccent", BindingFlags.Static | BindingFlags.NonPublic))?.Invoke(null, new object[3]
			{
				header,
				Color.FromArgb(198, 151, 42),
				false
			});
		}
		catch
		{
		}
	}

	private void AddBrandLockup(Control header)
	{
		try
		{
			if (((from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null)?.GetMethod("CreateBrandLockup", BindingFlags.Static | BindingFlags.NonPublic))?.Invoke(null, new object[2] { header.BackColor, null }) is Control value)
			{
				header.Controls.Add(value);
			}
		}
		catch
		{
		}
	}

	private static Panel CreateSharedFooter(Label message, Button locate, Button primary, Button close)
	{
		try
		{
			Type type = (from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
			MethodInfo methodInfo = type?.GetMethod("CreateDialogFooter", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo methodInfo2 = type?.GetMethod("CreateFooterButtonGroup", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo methodInfo3 = type?.GetMethod("PlaceFooterButton", BindingFlags.Static | BindingFlags.NonPublic);
			Panel panel = methodInfo?.Invoke(null, null) as Panel;
			Panel panel2 = methodInfo2?.Invoke(null, new object[2] { true, 388 }) as Panel;
			if (panel != null && panel2 != null && methodInfo3 != null)
			{
				panel.Height = 64;
				panel.Padding = new Padding(14, 8, 14, 8);
				message.Dock = DockStyle.Fill;
				message.Padding = new Padding(2, 0, 8, 0);
				message.ForeColor = Color.FromArgb(78, 85, 92);
				message.Font = new Font("Segoe UI", 8f);
				message.TextAlign = ContentAlignment.MiddleLeft;
				methodInfo3.Invoke(null, new object[4] { panel2, locate, 0, 122 });
                methodInfo3.Invoke(null, new object[4] { panel2, primary, 136, 132 });
				methodInfo3.Invoke(null, new object[4] { panel2, close, 284, 92 });
				panel.Controls.Add(message);
				panel.Controls.Add(panel2);
				return panel;
			}
		}
		catch
		{
		}
		Panel fallback = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 64,
			BackColor = Color.FromArgb(216, 221, 227),
			Padding = new Padding(12, 8, 12, 8)
		};
		fallback.Paint += (object s, PaintEventArgs e) =>
		{
			using Pen pen = new Pen(Color.FromArgb(198, 151, 42), 1.35f);
			e.Graphics.DrawLine(pen, 0, fallback.Height - 2, fallback.Width, fallback.Height - 2);
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Right,
			Width = 388,
			Height = 36,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			BackColor = fallback.BackColor
		};
		primary.Margin = new Padding(4, 2, 4, 0);
		close.Margin = new Padding(4, 2, 4, 0);
		flowLayoutPanel.Controls.Add(locate);
        flowLayoutPanel.Controls.Add(primary);
		flowLayoutPanel.Controls.Add(close);
		message.Dock = DockStyle.Fill;
		message.TextAlign = ContentAlignment.MiddleLeft;
		message.ForeColor = Color.FromArgb(78, 85, 92);
		fallback.Controls.Add(message);
		fallback.Controls.Add(flowLayoutPanel);
		return fallback;
	}

    private static DataGridView MakePreview()
    {
        var grid = new DataGridView
        {
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Dock = DockStyle.Fill,
            BackgroundColor = Color.White
        };
        BIMlify.CleanerUi.ReportGridStyle.Apply(grid);
        grid.Columns.Add("Opening", "Opening ID");
        grid.Columns.Add("Host", "Host ID");
        grid.Columns.Add("Summary", "Opening details");
        grid.Columns[2].FillWeight = 340;
        return grid;
    }

    private void LocateOpening(DataGridView grid, int index)
    {
        if (index >= 0 && grid.Rows[index].Tag is OpeningCandidate candidate)
            handler.Request(candidate.CreatedOpeningId == null ? "locate-host" : "locate", candidate.CreatedOpeningId ?? candidate.HostId);
    }

	private static Button MakeButton(string text, int width, bool primary = false)
	{
		try
		{
			Type type = (from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null)?.GetNestedType("GlassButton", BindingFlags.NonPublic);
			Button button = ((type == null) ? null : (Activator.CreateInstance(type, nonPublic: true) as Button));
			if (button != null)
			{
				button.Text = text;
				button.Width = width;
				button.Height = 30;
				type.GetProperty("Primary", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(button, primary || string.Equals(text, "Close", StringComparison.OrdinalIgnoreCase), null);
				return button;
			}
		}
		catch
		{
		}
		Button button2 = new Button
		{
			Text = text,
			Width = width,
			Height = 30,
			FlatStyle = FlatStyle.Flat,
			UseVisualStyleBackColor = false,
			BackColor = (primary || string.Equals(text, "Close", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(250, 230, 168) : Color.FromArgb(255, 248, 226)),
			ForeColor = Color.FromArgb(67, 75, 82),
			Font = new Font("Segoe UI", 8.5f),
			Cursor = Cursors.Hand
		};
		button2.FlatAppearance.BorderColor = string.Equals(text, "Close", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(198, 151, 42) : Color.FromArgb(176, 184, 192);
		if (string.Equals(text, "Close", StringComparison.OrdinalIgnoreCase))
		{
			button2.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 239, 190);
			button2.FlatAppearance.MouseDownBackColor = Color.FromArgb(250, 230, 168);
		}
		return button2;
	}

	private static Button MakePrimaryActionButton(string text, int width)
	{
		Button button = new Button
		{
			Text = text,
			Width = width,
			Height = 30,
			FlatStyle = FlatStyle.Flat,
			UseVisualStyleBackColor = false,
			BackColor = Color.FromArgb(239, 205, 120),
			ForeColor = Color.FromArgb(67, 75, 82),
			Font = new Font("Segoe UI", 8.5f),
			Cursor = Cursors.Hand
		};
		button.FlatAppearance.BorderColor = Color.FromArgb(198, 151, 42);
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 218, 150);
		button.FlatAppearance.MouseDownBackColor = Color.FromArgb(201, 153, 45);
		return button;
	}

	private static void ConfigureCombo(ComboBox c, params string[] values)
	{
		c.DropDownStyle = ComboBoxStyle.DropDownList;
		c.Items.AddRange(values);
		c.SelectedIndex = 0;
	}

	private static void ConfigureNumber(NumericUpDown n, decimal min, decimal max, decimal value)
	{
		n.Font = new Font("Segoe UI", 8.5f);
		n.Minimum = min;
		n.Maximum = max;
		n.Value = value;
	}

	internal void StartWorkflow(string mode)
	{
		create.Enabled = false;
		if (string.Equals(mode, "Structural", StringComparison.OrdinalIgnoreCase))
		{
			structPreview.Rows.Clear();
			tabs.SelectedIndex = 1;
		}
		else
		{
			mepPreview.Rows.Clear();
			tabs.SelectedIndex = 0;
		}
	}

	internal void SetCandidates(List<OpeningCandidate> candidates, string title)
	{
        bool structural = title.StartsWith("Structural", StringComparison.OrdinalIgnoreCase)
            || candidates.FirstOrDefault()?.PreviewTab == "Structural";
        mepPreview.Rows.Clear();
        structPreview.Rows.Clear();
        DataGridView grid = structural ? structPreview : mepPreview;
        foreach (OpeningCandidate candidate in candidates)
        {
            int row = grid.Rows.Add(candidate.CreatedOpeningId?.Value.ToString() ?? "Pending",
                candidate.HostId?.Value.ToString(), candidate.Summary);
            grid.Rows[row].Tag = candidate;
        }
        if (candidates.Count > 0)
        {
            tabs.SelectedIndex = structural ? 1 : 0;
            grid.ClearSelection();
            grid.CurrentCell = grid.Rows[0].Cells[0];
            grid.Rows[0].Selected = true;
        }
        create.Enabled = candidates.Any(candidate => candidate.CreatedOpeningId == null);
        if (create.Enabled) ShowAfterAction(candidates.Count + " proposed openings. Choose Create openings to cut them in Revit.");

	}

	internal void ShowMessage(string text)
	{
		if (IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke((MethodInvoker)(() =>
			{
				status.Text = text;
			}));
		}
		else
		{
			status.Text = text;
		}
	}

	internal void ShowAfterAction(string text)
	{
		if (IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke((MethodInvoker)(() =>
			{
				ShowAfterAction(text);
			}));
			return;
		}
		status.Text = text;
		if (!Visible)
		{
			Show();
		}
		Form form = FindForm();
		if (form != null)
		{
			if (form.WindowState == FormWindowState.Minimized)
			{
				form.WindowState = FormWindowState.Normal;
			}
			form.Show();
			form.Activate();
		}
	}
}







