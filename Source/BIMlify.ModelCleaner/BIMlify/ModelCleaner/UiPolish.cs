using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Form = System.Windows.Forms.Form;
using Control = System.Windows.Forms.Control;
using Panel = System.Windows.Forms.Panel;
using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;
using Point = System.Drawing.Point;
using ComboBox = System.Windows.Forms.ComboBox;
using TextBox = System.Windows.Forms.TextBox;
using View = Autodesk.Revit.DB.View;
using BIMlify.CleanerUi;
using BIMlify.PlanRangeUi;

namespace BIMlify.ModelCleaner;

public static class UiPolish
{
	private sealed class PlanRangePlane
	{
		public string Name;

		public string Level;

		public double OffsetMm;

		public double ElevationMm;

		public double RelativeMm;

		public bool Unlimited;
	}

	private sealed class PlanViewChoice
	{
		public readonly ViewPlan View;
		public readonly string ViewName;
		public readonly string LevelName;
		public readonly List<PlanRangePlane> Planes;

		public PlanViewChoice(ViewPlan view, UIDocument ui)
		{
			View = view;
			ViewName = ((Element)view).Name;
			Level level = ((View)view).GenLevel;
			LevelName = (level == null) ? "Not assigned" : ((Element)level).Name;
			Planes = ReadPlanPlanes(ui, view);
		}

		public override string ToString()
		{
			return ViewName + ((LevelName == "Not assigned") ? string.Empty : ("  ·  " + LevelName));
		}
	}

	private sealed class PlanRangePreview : Control
	{
		private IList<PlanRangePlane> _planes;

		public float Zoom = 1f;

		public IList<PlanRangePlane> Planes
		{
			get
			{
				return _planes;
			}
			set
			{
				_planes = value ?? new List<PlanRangePlane>();
				Invalidate();
			}
		}

		public PlanRangePreview(IList<PlanRangePlane> planes)
		{
			_planes = planes;
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.Clear(Color.White);
			if (ClientSize.Width < 120 || ClientSize.Height < 100)
			{
				return;
			}
			int num = Math.Min(190, Math.Max(130, ClientSize.Width / 3));
			float num2 = 26f;
			float num3 = Math.Max(num2 + 100f, ClientSize.Width - num - 22);
			float top = 28f;
			float bottom = Math.Max(top + 80f, ClientSize.Height - 26);
			double[] values = _planes.Select((PlanRangePlane p) => (p.Unlimited || double.IsNaN(p.ElevationMm)) ? p.RelativeMm : p.ElevationMm).ToArray();
			double min = ((values.Length == 0) ? (-1000.0) : values.Min());
			double max = ((values.Length == 0) ? 1000.0 : values.Max());
			if (Math.Abs(max - min) < 10.0)
			{
				min -= 500.0;
				max += 500.0;
			}
			double num4 = (max - min) * 0.18;
			min -= num4;
			max += num4;
			double center = (min + max) / 2.0;
			float centerY = (top + bottom) / 2f;
			Func<double, float> yFor = (double v) => centerY - (float)((v - center) / (max - min) * (double)(bottom - top) * (double)Zoom);
			float num5 = num3 - num2;
			float num6 = num2 + num5 * 0.48f;
			float num7 = Math.Min(34f, num5 * 0.13f);
			float num8 = num7 * 0.48f;
			float num9 = num6 + num7 * 3f;
			float num10 = Math.Min(ClientSize.Width - num - 8, num9 + 10f);
			float num11 = (float)ClientSize.Width - num10 - 8f;
			float[] planeYs = _planes.Select((PlanRangePlane p, int i) =>
			{
				double arg = ((p.Unlimited || double.IsNaN(p.ElevationMm)) ? p.RelativeMm : p.ElevationMm);
				float val2 = yFor(arg);
				if (values.Length > 1 && values.Max() - values.Min() < 1.0)
				{
					val2 = top + 16f + (float)i * ((bottom - top - 32f) / (float)(values.Length - 1));
				}
				return Math.Max(top + 3f, Math.Min(bottom - 3f, val2));
			}).ToArray();
			int[] array = (from i in Enumerable.Range(0, planeYs.Length)
				orderby planeYs[i]
				select i).ToArray();
			float[] array2 = (float[])planeYs.Clone();
			for (int num12 = 1; num12 < array.Length; num12++)
			{
				array2[array[num12]] = Math.Max(array2[array[num12]], array2[array[num12 - 1]] + 25f);
			}
			if (array.Length != 0 && array2[array[array.Length - 1]] > bottom - 10f)
			{
				float num13 = array2[array[array.Length - 1]] - (bottom - 10f);
				int[] array3 = array;
				foreach (int num15 in array3)
				{
					array2[num15] -= num13;
				}
			}
			using Pen pen = new Pen(Color.FromArgb(198, 151, 42), 1.2f);
			using Pen pen2 = new Pen(Color.FromArgb(230, 196, 119), 1f);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(24, 198, 151, 42));
			using Font font = new Font("Segoe UI", 8.5f);
			float num16 = num6 - num7;
			top = (top - centerY) * Zoom + centerY;
			bottom = (bottom - centerY) * Zoom + centerY;
			PointF pointF = new PointF(num16, top + num8);
			PointF pointF2 = new PointF(num6 + num7, top);
			PointF pointF3 = new PointF(num6 + num7, bottom - num8);
			PointF pointF4 = new PointF(num6 - num7, bottom);
			PointF pt = new PointF(pointF.X + num7 * 2f, pointF.Y - num8 * 2f);
			PointF pt2 = new PointF(pointF2.X + num7 * 2f, pointF2.Y - num8 * 2f);
			PointF pt3 = new PointF(pointF3.X + num7 * 2f, pointF3.Y - num8 * 2f);
			PointF pt4 = new PointF(pointF4.X + num7 * 2f, pointF4.Y - num8 * 2f);
			graphics.FillPolygon(brush, new PointF[4] { pointF, pointF2, pointF3, pointF4 });
			graphics.DrawPolygon(pen2, new PointF[4] { pointF, pointF2, pointF3, pointF4 });
			graphics.DrawLine(pen2, pointF, pt);
			graphics.DrawLine(pen2, pointF2, pt2);
			graphics.DrawLine(pen2, pointF3, pt3);
			graphics.DrawLine(pen2, pointF4, pt4);
			for (int num17 = 0; num17 < _planes.Count; num17++)
			{
				PlanRangePlane planRangePlane = _planes[num17];
				float val = planeYs[num17];
				val = Math.Max(top, Math.Min(bottom, val));
				PointF pointF5 = new PointF(num6 - num7, val + num8);
				PointF pointF6 = new PointF(num6 + num7, val);
				PointF pointF7 = new PointF(num6 + num7 * 3f, val - num8 * 2f);
				PointF pointF8 = new PointF(num6 + num7, val + num8);
				using (SolidBrush brush2 = new SolidBrush(Color.FromArgb((planRangePlane.Name == "Cut Plane") ? 35 : 18, 198, 151, 42)))
				{
					graphics.FillPolygon(brush2, new PointF[4] { pointF5, pointF6, pointF7, pointF8 });
				}
				graphics.DrawPolygon((planRangePlane.Name == "Cut Plane") ? pen : pen2, new PointF[4] { pointF5, pointF6, pointF7, pointF8 });
				string text = planRangePlane.Name + (planRangePlane.Unlimited ? " · not set" : (" · " + planRangePlane.ElevationMm.ToString("0", CultureInfo.CurrentCulture) + " mm"));
				float num18 = Math.Max(top, Math.Min(bottom - 22f, array2[num17] - 10f));
				if (num17 == 3)
				{
					num18 += 16f;
				}
				using (Pen pen3 = new Pen(Color.FromArgb(215, 219, 224), 1f))
				{
					graphics.DrawLine(pen3, num9, val, num10 - 4f, array2[num17]);
				}
				TextRenderer.DrawText(graphics, text, font, new Rectangle((int)num10, (int)num18, Math.Max(1, (int)num11), 22), Color.FromArgb(78, 85, 92), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
			TextRenderer.DrawText(graphics, "3D view-range schematic", font, new Rectangle(10, 5, Math.Max(220, ClientSize.Width - 20), 22), Color.FromArgb(102, 109, 118), TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
	}

	private sealed class RoundedMetricCard : Panel
	{
		public RoundedMetricCard()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			if (Width < 4 || Height < 4)
			{
				return;
			}
			using GraphicsPath path = RoundedSearchPath(new Rectangle(0, 0, Width, Height), 9);
			Region region = Region;
			Region = new Region(path);
			region?.Dispose();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = RoundedSearchPath(new Rectangle(0, 0, Width - 1, Height - 1), 9);
			using Pen pen = new Pen(Color.FromArgb(222, 225, 229), 1f);
			e.Graphics.DrawPath(pen, path);
		}
	}

	private static readonly ConditionalWeakTable<Form, object> CtrlAForms = new ConditionalWeakTable<Form, object>();
	private static readonly ConditionalWeakTable<Form, object> LegacyCleanerDeferredForms = new ConditionalWeakTable<Form, object>();

	private static readonly ConditionalWeakTable<TabControl, object> StyledTabControls = new ConditionalWeakTable<TabControl, object>();

	private static readonly ConditionalWeakTable<TabControl, object> InsetTabControls = new ConditionalWeakTable<TabControl, object>();

	private static readonly ConditionalWeakTable<Control, object> RoundedCardControls = new ConditionalWeakTable<Control, object>();

	private static readonly ConditionalWeakTable<PictureBox, object> ShadowedDashboardIcons = new ConditionalWeakTable<PictureBox, object>();

	private static readonly ConditionalWeakTable<Control, object> AlignedSmartCleanerFooters = new ConditionalWeakTable<Control, object>();

	private static readonly ConditionalWeakTable<Form, object> ReviewCleanLocateButtons = new ConditionalWeakTable<Form, object>();

	private static readonly ConditionalWeakTable<TabControl, object> LegacyDeleteVisibilityTabs = new ConditionalWeakTable<TabControl, object>();

	private static readonly ConditionalWeakTable<Button, object> StandardActionButtons = new ConditionalWeakTable<Button, object>();

	private static readonly Color PaleGold = Color.FromArgb(255, 244, 216);

	private static readonly Color PaleSelected = Color.FromArgb(255, 240, 201);

	private static readonly Color SoftCard = Color.FromArgb(246, 247, 249);

	public static void EnableCtrlA(Form form)
	{
		if (form == null || form.IsDisposed || CtrlAForms.TryGetValue(form, out var _))
		{
			return;
		}
		CtrlAForms.Add(form, new object());
		form.KeyPreview = true;
		form.KeyDown += (object sender, KeyEventArgs e) =>
		{
			if (e.Control && e.KeyCode == Keys.A)
			{
				Control control = FindFocusedControl(form);
				if (control is TextBoxBase textBoxBase)
				{
					textBoxBase.SelectAll();
				}
				else if (control is ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList } comboBox)
				{
					comboBox.SelectAll();
				}
				else if (control is DataGridView dataGridView)
				{
					DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn = dataGridView.Columns.OfType<DataGridViewCheckBoxColumn>().FirstOrDefault();
					if (dataGridViewCheckBoxColumn != null)
					{
						foreach (DataGridViewRow item in (IEnumerable)dataGridView.Rows)
						{
							if (item.Visible)
							{
								item.Cells[dataGridViewCheckBoxColumn.Index].Value = true;
							}
						}
					}
					else
					{
						dataGridView.SelectAll();
					}
				}
				else if (control is ListBox { SelectionMode: >SelectionMode.None } listBox)
				{
					for (int i = 0; i < listBox.Items.Count; i++)
					{
						listBox.SetSelected(i, value: true);
					}
				}
				else
				{
					if (!(control is ListView { MultiSelect: not false } listView))
					{
						return;
					}
					foreach (ListViewItem item2 in listView.Items)
					{
						item2.Selected = true;
					}
				}
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		};
	}

	private static Control FindFocusedControl(Control parent)
	{
		if (parent == null)
		{
			return null;
		}
		foreach (Control control3 in parent.Controls)
		{
			if (!control3.ContainsFocus)
			{
				continue;
			}
			Control control2 = FindFocusedControl(control3);
			return control2 ?? control3;
		}
		return parent.Focused ? parent : null;
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

	public static void ApplyGlobalTabStyle(Form form)
	{
		if (form == null || form.IsDisposed)
		{
			return;
		}
		CenterWindowOnCurrentScreen(form);
		ApplyStandardActionButtons(form);
		ApplySearchBars(form);
		foreach (TabControl tabs in All(form).OfType<TabControl>())
		{
			InsetTabFromLeft(tabs);
			if (tabs.IsDisposed)
			{
				continue;
			}
			tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
			tabs.SizeMode = TabSizeMode.Fixed;
			tabs.Padding = new Point(12, 4);
			tabs.ItemSize = new Size(170, 32);
			tabs.BackColor = Color.FromArgb(238, 241, 245);
			if (!StyledTabControls.TryGetValue(tabs, out var _))
			{
				StyledTabControls.Add(tabs, new object());
				tabs.DrawItem += DrawUniformTab;
				tabs.Resize += delegate
				{
					tabs.Invalidate();
				};
				tabs.SelectedIndexChanged += delegate
				{
					tabs.Invalidate();
				};
			}
		}
	}

	private static void CenterWindowOnCurrentScreen(Form form)
	{
		if (form == null || form.IsDisposed || form.WindowState != FormWindowState.Normal || form.TopMost ||
			string.Equals(form.Text, "BIMlify — Quick Bar", StringComparison.OrdinalIgnoreCase)) return;
		try
		{
			Screen screen = Screen.FromControl(form);
			Rectangle area = screen.WorkingArea;
			int width = Math.Min(form.Width, area.Width);
			int height = Math.Min(form.Height, area.Height);
			form.Location = new Point(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2);
		}
		catch { }
	}

	private static void InsetTabFromLeft(TabControl tabs)
	{
		if (tabs == null || tabs.IsDisposed || InsetTabControls.TryGetValue(tabs, out var _))
		{
			return;
		}
		InsetTabControls.Add(tabs, new object());
		Control parent = tabs.Parent;
		if (tabs.Dock == DockStyle.Fill && parent != null)
		{
			if (parent.Padding.Left != 18)
			{
				parent.Padding = new Padding(18, parent.Padding.Top, parent.Padding.Right, parent.Padding.Bottom);
			}
		}
		else
		{
			Rectangle bounds = tabs.Bounds;
			tabs.Dock = DockStyle.None;
			tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			tabs.SetBounds(bounds.Left + 18, bounds.Top, Math.Max(240, bounds.Width - 18), bounds.Height);
		}
	}

	public static void ApplyStandardActionButtons(Form form)
	{
		foreach (Button item in All(form).OfType<Button>().ToList())
		{
			string text = (item.Text ?? string.Empty).Trim();
			// Smart Cleaner's unified footer owns these buttons' appearance and layout.
			// Do not let the generic action pass restyle or resize them afterward.
			if (item.Parent != null && string.Equals(item.Parent.Name, "BIMlifyPurgeFooterActions", StringComparison.Ordinal))
			{
				continue;
			}
			if (item.IsDisposed || StandardActionButtons.TryGetValue(item, out var _) || text.Length == 0)
			{
				continue;
			}
			StandardActionButtons.Add(item, new object());
			bool flag = item.Width > 50 && (text.Length > 2 || item.Width > 64);
			bool closeAction = string.Equals(text, "Close", StringComparison.OrdinalIgnoreCase);
			bool primaryAction = closeAction;
			if (flag)
			{
				int height = item.Height;
				if (item.Dock == DockStyle.None)
				{
					item.Top += (height - 30) / 2;
				}
				item.Height = 30;
			}
			Control parent = item.Parent;
			while (parent != null && parent.BackColor.A == 0)
			{
				parent = parent.Parent;
			}
			Color backColor = parent?.BackColor ?? form.BackColor;
			if (backColor.A == 0)
			{
				backColor = form.BackColor;
			}
			PropertyInfo property = item.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.CanWrite)
			{
				property.SetValue(item, primaryAction, null);
			}
			Color gold = Color.FromArgb(250, 230, 168);
			Color goldBorder = Color.FromArgb(198, 151, 42);
			item.BackColor = primaryAction ? gold : backColor;
			if (primaryAction) item.ForeColor = Color.FromArgb(55, 67, 79);
			if (item.GetType().Name != "GlassButton")
			{
				item.UseVisualStyleBackColor = false;
				if (flag)
				{
					item.FlatStyle = FlatStyle.Flat;
					item.FlatAppearance.BorderSize = 0;
					item.FlatAppearance.MouseOverBackColor = primaryAction ? Color.FromArgb(255, 239, 190) : Color.FromArgb(Math.Min(255, backColor.R + (255 - backColor.R) / 3), Math.Min(255, backColor.G + (255 - backColor.G) / 3), Math.Min(255, backColor.B + (255 - backColor.B) / 3));
					if (primaryAction) item.FlatAppearance.MouseDownBackColor = Color.FromArgb(250, 230, 168);
					ApplyRoundedCardOutline(item, 6, primaryAction ? goldBorder : Color.FromArgb(205, 211, 217), 1f);
				}
			}
		}
	}

	private static void MatchButtonAppearance(Button target, Button reference)
	{
		if (target == null || reference == null || target.IsDisposed || reference.IsDisposed) return;
		target.Width = reference.Width;
		target.Height = reference.Height;
		target.Font = reference.Font;
		target.ForeColor = reference.ForeColor;
		target.BackColor = reference.BackColor;
		target.FlatStyle = reference.FlatStyle;
		target.UseVisualStyleBackColor = reference.UseVisualStyleBackColor;
		target.FlatAppearance.BorderColor = reference.FlatAppearance.BorderColor;
		target.FlatAppearance.BorderSize = reference.FlatAppearance.BorderSize;
		target.FlatAppearance.MouseOverBackColor = reference.FlatAppearance.MouseOverBackColor;
		target.FlatAppearance.MouseDownBackColor = reference.FlatAppearance.MouseDownBackColor;
		target.Padding = reference.Padding;
		target.TextAlign = reference.TextAlign;
		target.Cursor = reference.Cursor;
		PropertyInfo sourcePrimary = reference.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		PropertyInfo targetPrimary = target.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (sourcePrimary != null && targetPrimary != null && targetPrimary.CanWrite)
			targetPrimary.SetValue(target, sourcePrimary.GetValue(reference, null), null);
		if (reference.Region != null)
		{
			Region old = target.Region;
			target.Region = reference.Region.Clone();
			old?.Dispose();
		}
	}

	private static void MatchSecondaryButtonAppearance(Button target, Button reference)
	{
		if (target == null || reference == null || target.IsDisposed || reference.IsDisposed) return;
		target.Width = reference.Width;
		target.Height = reference.Height;
		target.Font = reference.Font;
		target.ForeColor = reference.ForeColor;
		target.BackColor = reference.BackColor;
		target.FlatStyle = reference.FlatStyle;
		target.UseVisualStyleBackColor = reference.UseVisualStyleBackColor;
		target.FlatAppearance.BorderColor = reference.FlatAppearance.BorderColor;
		target.FlatAppearance.BorderSize = reference.FlatAppearance.BorderSize;
		target.FlatAppearance.MouseOverBackColor = reference.FlatAppearance.MouseOverBackColor;
		target.FlatAppearance.MouseDownBackColor = reference.FlatAppearance.MouseDownBackColor;
		target.Padding = reference.Padding;
		target.TextAlign = reference.TextAlign;
		target.Cursor = reference.Cursor;
		PropertyInfo primary = target.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		PropertyInfo referencePrimary = reference.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (primary != null && primary.CanWrite && referencePrimary != null) primary.SetValue(target, referencePrimary.GetValue(reference, null), null);
		if (reference.Region != null)
		{
			Region old = target.Region;
			target.Region = reference.Region.Clone();
			old?.Dispose();
		}
	}

	private static void ApplyCleanerNeutralActionAppearance(Button button)
	{
		if (button == null || button.IsDisposed) return;
		button.ForeColor = Color.FromArgb(78, 85, 92);
		button.BackColor = Color.FromArgb(216, 221, 227);
		button.FlatStyle = FlatStyle.Flat;
		button.UseVisualStyleBackColor = false;
		button.FlatAppearance.BorderColor = Color.FromArgb(88, 101, 112);
		button.FlatAppearance.BorderSize = 2;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 233, 237);
		button.FlatAppearance.MouseDownBackColor = Color.FromArgb(216, 221, 227);
		// Continue is the existing GlassButton. Its own anti-aliased renderer paints
		// the neutral fill and defined outline; do not draw a second border on top.
	}

	private static void DrawUniformTab(object sender, DrawItemEventArgs e)
	{
		if (!(sender is TabControl tabControl) || e.Index < 0 || e.Index >= tabControl.TabPages.Count)
		{
			return;
		}
		Rectangle bounds = e.Bounds;
		if (tabControl.TabPages.Count == 1 && string.Equals(tabControl.TabPages[e.Index].Text, "Advanced Search", StringComparison.CurrentCultureIgnoreCase))
		{
			using SolidBrush brush = new SolidBrush(tabControl.BackColor);
			e.Graphics.FillRectangle(brush, e.Bounds);
		}
		bool flag = (e.State & DrawItemState.Selected) == DrawItemState.Selected || tabControl.SelectedIndex == e.Index;
		Color color = (flag ? Color.White : Color.FromArgb(222, 226, 231));
		Color color2 = Color.FromArgb(161, 169, 178);
		using (SolidBrush brush2 = new SolidBrush(color))
		{
			e.Graphics.FillRectangle(brush2, bounds);
		}
		using (Pen pen = new Pen(color2, 1f))
		{
			e.Graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
		}
		string text = tabControl.TabPages[e.Index].Text ?? string.Empty;
		Font font = (flag ? new Font(tabControl.Font, FontStyle.Bold) : tabControl.Font);
		TextRenderer.DrawText(e.Graphics, text, font, Rectangle.Inflate(bounds, -6, -2), Color.FromArgb(67, 76, 86), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		if (flag)
		{
			using Pen pen2 = new Pen(Color.FromArgb(220, 177, 82), 2f);
			e.Graphics.DrawLine(pen2, bounds.Left + 1, bounds.Bottom - 2, bounds.Right - 2, bounds.Bottom - 2);
		}
		if (flag && font != tabControl.Font)
		{
			font.Dispose();
		}
	}

	public static void ShowPlanViewRange(UIDocument ui)
	{
		try
		{
			ShowPlanViewRangeCore(ui);
		}
		catch (Exception ex)
		{
			MessageBox.Show("Plan View Range could not open.\r\n\r\n" + ex.Message, "BIMlify — Plan View Range", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private static void ShowPlanViewRangeCore(UIDocument ui)
	{
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_039d: Unknown result type (might be due to invalid IL or missing references)
		if (ui == null)
		{
			return;
		}
		View activeView = ui.ActiveGraphicalView;
		View val = activeView;
		ViewPlan val2 = (ViewPlan)(object)((val is ViewPlan) ? val : null);
		if (val2 == null && activeView != null)
		{
			try
			{
				val2 = ((IEnumerable)new FilteredElementCollector(ui.Document).OfClass(typeof(ViewPlan))).Cast<ViewPlan>().FirstOrDefault((ViewPlan candidate) => ((object)((Element)candidate).Id).Equals((object)((Element)activeView).Id));
			}
			catch
			{
			}
		}
		if (val2 == null)
		{
			string text = ((activeView == null) ? "No active view is available." : ("Active view: " + ((Element)activeView).Name + " (" + ((object)activeView.ViewType/*cast due to constrained. prefix*/).ToString() + ")."));
			MessageBox.Show("Plan View Range could not resolve the active view as a plan.\r\n" + text + "\r\n\r\nOpen a floor, ceiling, area, or engineering plan and try again.", "BIMlify — Plan View Range", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		List<PlanRangePlane> planes;
		try
		{
			planes = ReadPlanPlanes(ui, val2);
		}
		catch (Exception ex)
		{
			MessageBox.Show("Revit could not read the view range for this plan.\r\n\r\n" + ex.Message, "BIMlify — Plan View Range", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		Form form = CreatePlanRangeWindow();
		bool deferredDialog = false;
		try
		{
			Color color = Color.FromArgb(55, 67, 79);
			Color color2 = Color.FromArgb(102, 109, 118);
			Color gold = Color.FromArgb(198, 151, 42);
			Color color3 = Color.FromArgb(255, 244, 216);
			Color color4 = Color.FromArgb(215, 219, 224);
			
			Panel header = new Panel
			{
				Dock = DockStyle.Top,
				Height = 128,
				BackColor = Color.FromArgb(247, 247, 245),
				Padding = new Padding(18, 10, 18, 6)
			};
			Label title = new Label
			{
				Text = "Plan View Range",
				Left = 18,
				Top = 60,
				Width = 420,
				Height = 25,
				Font = new Font("Segoe UI", 9f, FontStyle.Bold),
				ForeColor = color
			};
			Label subtitle = new Label
			{
				Text = "Inspect the selected plan view's clipping planes in a fitted 3D view-range schematic.",
				Left = 18,
				Top = 88,
				Width = 650,
				Height = 24,
				Font = new Font("Segoe UI", 8.5f),
				ForeColor = color2
			};
			Control control = Branding.Create(header.BackColor, null);
			ComboBox viewSelector = new ComboBox
			{
				Name = "PlanViewRangeSelector",
				Left = 156,
				Top = 8,
				Width = 520,
				Height = 30,
				Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
				DropDownStyle = ComboBoxStyle.DropDownList,
				BackColor = color3,
				ForeColor = Color.FromArgb(78, 85, 92),
				FlatStyle = FlatStyle.Flat
			};
			foreach (ViewPlan item in from ViewPlan p in (IEnumerable)new FilteredElementCollector(ui.Document).OfClass(typeof(ViewPlan))
				where !((View)p).IsTemplate
				orderby ((Element)p).Name
				select p)
			{
				viewSelector.Items.Add(new PlanViewChoice(item, ui));
			}
			for (int num = 0; num < viewSelector.Items.Count; num++)
			{
				if (((object)((Element)((PlanViewChoice)viewSelector.Items[num]).View).Id).Equals((object)((Element)val2).Id))
				{
					viewSelector.SelectedIndex = num;
					break;
				}
			}
			header.Controls.Add(title);
			header.Controls.Add(subtitle);
			header.Controls.Add(control);
			header.Paint += (object s, PaintEventArgs e) =>
			{
				using (Pen pen = new Pen(Color.FromArgb(224, 194, 116), 1.25f))
				{
					e.Graphics.DrawLine(pen, 9, title.Top - 2, 9, subtitle.Bottom + 2);
				}
				using Pen pen2 = new Pen(gold, 2f);
				e.Graphics.DrawLine(pen2, 1, header.Height - 2, header.Width - 2, header.Height - 2);
			};
			Panel footer = new Panel
			{
				Dock = DockStyle.Bottom,
				Height = 54,
				BackColor = Color.FromArgb(216, 221, 227),
				Padding = new Padding(12, 8, 12, 8)
			};
			Button close = new Button
			{
				Text = "Close",
				Width = 92,
				Height = 30,
				Anchor = (AnchorStyles.Top | AnchorStyles.Right),
				Left = 0,
				Top = 2,
				DialogResult = DialogResult.Cancel,
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.White,
				ForeColor = color,
				UseVisualStyleBackColor = false,
				Font = new Font("Segoe UI", 9f)
			};
			close.FlatAppearance.BorderSize = 0;
			close.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 247, 249);
			ApplyRoundedCardOutline(close, 6);
			footer.Controls.Add(close);
			footer.Resize += delegate
			{
				close.Left = footer.ClientSize.Width - close.Width - 4;
			};
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 2,
				BackColor = Color.FromArgb(238, 241, 245),
				Padding = new Padding(12),
				Margin = Padding.Empty
			};
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			Panel panel = CreatePlanRangeCard("VIEW RANGE DATA", Color.White, color4, gold);
			Panel panel2 = CreatePlanRangeCard("3D VIEW RANGE — SELECTED PLAN", Color.White, color4, gold);
			Panel planSelectorCard = new Panel
			{
				Name = "BIMlifyPlanSelectorCard",
				Dock = DockStyle.Fill,
				BackColor = Color.White,
				Margin = new Padding(7, 5, 7, 5),
				Padding = new Padding(10, 5, 10, 5)
			};
			ApplyRoundedCardOutline(planSelectorCard, 8);
			Label value = new Label
			{
				Text = "CURRENT PLAN VIEW",
				Left = 8,
				Top = 7,
				Width = 146,
				Height = 30,
				ForeColor = color,
				Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoSize = false,
				AutoEllipsis = true,
				UseMnemonic = false
			};
			viewSelector.Left = 162;
			planSelectorCard.Controls.Add(value);
			planSelectorCard.Controls.Add(viewSelector);
			Action resizePlanSelector = () =>
			{
				viewSelector.Width = Math.Max(240, planSelectorCard.ClientSize.Width - viewSelector.Left - planSelectorCard.Padding.Right - 8);
			};
			planSelectorCard.Resize += delegate
			{
				resizePlanSelector();
			};
			resizePlanSelector();
			TableLayoutPanel data = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 6,
				BackColor = Color.White,
				Padding = new Padding(16, 12, 16, 12),
				AutoScroll = true
			};
			data.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43f));
			data.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57f));
			AddRangeValue(data, 0, "Associated Level", (((View)val2).GenLevel == null) ? "Not assigned" : ((Element)((View)val2).GenLevel).Name, color, color2, Color.White);
			for (int num2 = 0; num2 < planes.Count; num2++)
			{
				PlanRangePlane planRangePlane = planes[num2];
				AddRangeValue(data, num2 + 1, planRangePlane.Name, FormatPlaneValue(planRangePlane), color, color2, (num2 % 2 == 0) ? Color.White : Color.FromArgb(249, 250, 251));
			}
			AddRangeValue(data, 5, "Source", "Revit ViewPlan.GetViewRange()", color, color2, color3);
			panel.Controls.Add(data);
			panel.Controls.OfType<Label>().FirstOrDefault((Label l) => l.Text == "VIEW RANGE DATA")?.BringToFront();
			PlanRangePreview preview = new PlanRangePreview(planes)
			{
				Dock = DockStyle.Fill,
				BackColor = Color.White
			};
			Panel zoomBar = new Panel
			{
				Dock = DockStyle.Bottom,
				Height = 48,
				BackColor = Color.FromArgb(246, 247, 249),
				Padding = new Padding(10, 7, 10, 7)
			};
			Label zoomText = new Label
			{
				Text = "3D view-range schematic · " + ((Element)val2).Name,
				Dock = DockStyle.Left,
				Width = 360,
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = color2,
				AutoEllipsis = true
			};
			Button fit = new Button
			{
				Text = "Fit",
				Width = 44,
				Height = 30,
				Dock = DockStyle.None,
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.White,
				ForeColor = color,
				Anchor = (AnchorStyles.Top | AnchorStyles.Right)
			};
			Button zoomIn = new Button
			{
				Text = "+",
				Width = 44,
				Height = 30,
				Dock = DockStyle.None,
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.White,
				ForeColor = color,
				Anchor = (AnchorStyles.Top | AnchorStyles.Right)
			};
			Button zoomOut = new Button
			{
				Text = "−",
				Width = 44,
				Height = 30,
				Dock = DockStyle.None,
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.White,
				ForeColor = color,
				Anchor = (AnchorStyles.Top | AnchorStyles.Right)
			};
			Button[] array = new Button[3] { fit, zoomIn, zoomOut };
			foreach (Button button in array)
			{
				button.FlatAppearance.BorderColor = color4;
				button.FlatAppearance.BorderSize = 0;
				button.UseVisualStyleBackColor = false;
				button.Font = new Font("Segoe UI", 9f);
				ApplyRoundedCardOutline(button, 6);
			}
			fit.Click += delegate
			{
				preview.Zoom = 1f;
				preview.Invalidate();
			};
			zoomIn.Click += delegate
			{
				preview.Zoom = Math.Min(2f, preview.Zoom * 1.15f);
				preview.Invalidate();
			};
			zoomOut.Click += delegate
			{
				preview.Zoom = Math.Max(0.6f, preview.Zoom / 1.15f);
				preview.Invalidate();
			};
			zoomBar.Controls.Add(zoomText);
			zoomBar.Controls.Add(fit);
			zoomBar.Controls.Add(zoomIn);
			zoomBar.Controls.Add(zoomOut);
			panel2.Controls.Add(preview);
			panel2.Controls.Add(zoomBar);
			Action layoutZoomButtons = () =>
			{
				int x = zoomBar.ClientSize.Width - 10 - 144;
				int y = Math.Max(0, (zoomBar.ClientSize.Height - 30) / 2);
				fit.SetBounds(x, y, 44, 30);
				zoomIn.SetBounds(fit.Right + 6, y, 44, 30);
				zoomOut.SetBounds(zoomIn.Right + 6, y, 44, 30);
			};
			zoomBar.Resize += delegate
			{
				layoutZoomButtons();
			};
			layoutZoomButtons();
			tableLayoutPanel.Controls.Add(planSelectorCard, 0, 0);
			tableLayoutPanel.SetColumnSpan(planSelectorCard, 2);
			tableLayoutPanel.Controls.Add(panel, 0, 1);
			tableLayoutPanel.Controls.Add(panel2, 1, 1);
			panel2.Controls.OfType<Label>().FirstOrDefault((Label l) => l.Text.StartsWith("3D VIEW RANGE", StringComparison.Ordinal))?.BringToFront();
			Control control2 = CreateSharedNavigationRail(form);
			if (control2 == null)
			{
				Panel fallbackRail = new Panel
				{
					Name = "BIMlifyNavigationRail",
					Dock = DockStyle.Left,
					Width = 58,
					BackColor = Color.FromArgb(248, 242, 222),
					Padding = new Padding(7, 10, 7, 8)
				};
				Button button2 = new Button
				{
					Text = "‹",
					Width = 42,
					Height = 42,
					Left = 8,
					Top = 10,
					FlatStyle = FlatStyle.Flat,
					BackColor = Color.White,
					ForeColor = gold,
					Font = new Font("Segoe UI", 20f),
					DialogResult = DialogResult.Cancel
				};
				Button button3 = new Button
				{
					Text = "⌂",
					Width = 42,
					Height = 42,
					Left = 8,
					Top = 58,
					FlatStyle = FlatStyle.Flat,
					BackColor = Color.White,
					ForeColor = gold,
					Font = new Font("Segoe UI Symbol", 17f),
					DialogResult = DialogResult.Cancel
				};
				FlatButtonAppearance flatAppearance = button2.FlatAppearance;
				Color borderColor = (button3.FlatAppearance.BorderColor = gold);
				flatAppearance.BorderColor = borderColor;
				Button contact = new Button
				{
					Text = "✉",
					Width = 42,
					Height = 42,
					Left = 8,
					Anchor = (AnchorStyles.Bottom | AnchorStyles.Left),
					FlatStyle = FlatStyle.Flat,
					BackColor = Color.FromArgb(248, 242, 222),
					ForeColor = color,
					Font = new Font("Segoe UI Symbol", 17f),
					UseVisualStyleBackColor = false
				};
				Button settings = new Button
				{
					Text = "⚙",
					Width = 42,
					Height = 42,
					Left = 8,
					Anchor = (AnchorStyles.Bottom | AnchorStyles.Left),
					FlatStyle = FlatStyle.Flat,
					BackColor = Color.FromArgb(248, 242, 222),
					ForeColor = color,
					Font = new Font("Segoe UI Symbol", 17f),
					UseVisualStyleBackColor = false
				};
				Button account = new Button
				{
					Text = "♙",
					Width = 42,
					Height = 42,
					Left = 8,
					Anchor = (AnchorStyles.Bottom | AnchorStyles.Left),
					FlatStyle = FlatStyle.Flat,
					BackColor = Color.FromArgb(248, 242, 222),
					ForeColor = color,
					Font = new Font("Segoe UI Symbol", 17f),
					UseVisualStyleBackColor = false
				};
				Button[] array2 = new Button[3] { contact, settings, account };
				foreach (Button button4 in array2)
				{
					button4.FlatAppearance.BorderSize = 0;
					button4.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 252, 242);
					ApplyRoundedCardOutline(button4, 8);
				}
				contact.Top = 0;
				settings.Top = 0;
				account.Top = 0;
				Action layoutRail = () =>
				{
					contact.Top = Math.Max(68, fallbackRail.ClientSize.Height - 158);
					settings.Top = Math.Max(68, fallbackRail.ClientSize.Height - 108);
					account.Top = Math.Max(68, fallbackRail.ClientSize.Height - 58);
				};
				fallbackRail.Resize += delegate
				{
					layoutRail();
				};
				layoutRail();
				contact.Click += delegate
				{
					InvokeBimlifyWindowAction("ShowContactOptions", form);
				};
				settings.Click += delegate
				{
					InvokeBimlifyWindowAction("ShowSettings", form);
				};
				account.Click += delegate
				{
					InvokeBimlifyWindowAction("ShowAccount", form);
				};
				button2.Click += delegate
				{
					form.Close();
				};
				button3.Click += delegate
				{
					form.Close();
				};
				fallbackRail.Controls.Add(button2);
				fallbackRail.Controls.Add(button3);
				fallbackRail.Controls.Add(contact);
				fallbackRail.Controls.Add(settings);
				fallbackRail.Controls.Add(account);
				fallbackRail.Paint += (object s, PaintEventArgs e) =>
				{
					using Pen pen = new Pen(gold, 1f);
					e.Graphics.DrawLine(pen, fallbackRail.Width - 1, 0, fallbackRail.Width - 1, fallbackRail.Height);
				};
				control2 = fallbackRail;
			}
			DashboardEnhancements.AttachToolSearchButton(control2, form, 108);
			form.Controls.Add(tableLayoutPanel);
			form.Controls.Add(footer);
			form.Controls.Add(header);
			form.Controls.Add(control2);
			form.CancelButton = close;
			close.Click += delegate
			{
				form.Close();
			};
			AddSharedBimlifyTitleBar(form);
			viewSelector.SelectedIndexChanged += delegate
			{
				if (!(viewSelector.SelectedItem is PlanViewChoice planViewChoice))
				{
					return;
				}
				try
				{
					planes = planViewChoice.Planes;
					SetRangeValue(data, "Associated Level", planViewChoice.LevelName);
					for (int i = 0; i < planes.Count; i++)
					{
						SetRangeValue(data, planes[i].Name, FormatPlaneValue(planes[i]));
					}
					SetRangeValue(data, "Source", "Revit ViewPlan.GetViewRange()");
					preview.Planes = planes;
					preview.Zoom = 1f;
					zoomText.Text = "3D view-range schematic · " + planViewChoice.ViewName;
					preview.Invalidate();
				}
				catch (Exception ex2)
				{
					MessageBox.Show("Revit could not read this plan view's range.\r\n\r\n" + ex2.Message, "BIMlify — Plan View Range", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				}
			};
			Form form2 = form;
			PlanRangeInteraction.Attach(form2);
			EmptyState.NormalizePlanRangeFooter(form2);
			form2.FormClosed += delegate
			{
				foreach (PictureBox item2 in All(control).OfType<PictureBox>())
				{
					if (item2.Image != null)
					{
						item2.Image.Dispose();
						item2.Image = null;
					}
				}
			};
			PlanRangeInteraction.ShowOwnedModelessDialog(form2, GetPlanRangeOwnerHandle(ui.Application.MainWindowHandle));
			deferredDialog = true;
		}
		finally
		{
			if (form != null && !deferredDialog)
			{
				((IDisposable)form).Dispose();
			}
		}
	}

	private static Form CreatePlanRangeWindow()
	{
		Form form = null;
		try
		{
			Type type = (from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
			Type type2 = ((type == null) ? null : type.GetNestedType("BIMlifyForm", BindingFlags.NonPublic));
			ConstructorInfo constructorInfo = ((type2 == null) ? null : type2.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null));
			form = ((constructorInfo == null) ? null : (constructorInfo.Invoke(null) as Form));
			if (form != null)
			{
				form.Text = "BIMlify — Plan View Range";
				form.StartPosition = FormStartPosition.CenterScreen;
				form.Width = 1080;
				form.Height = 720;
				form.MinimumSize = new Size(960, 620);
				form.BackColor = Color.FromArgb(238, 241, 245);
				form.ForeColor = Color.FromArgb(78, 85, 92);
				form.Font = new Font("Segoe UI", 9f);
				form.AutoScaleMode = AutoScaleMode.Dpi;
				form.MinimizeBox = true;
				form.MaximizeBox = true;
				PropertyInfo property = type2.GetProperty("AllowResize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property != null && property.CanWrite)
				{
					property.SetValue(form, true, null);
				}
			}
		}
		catch
		{
			form = null;
		}
		return form ?? new Form
		{
			Text = "BIMlify — Plan View Range",
			StartPosition = FormStartPosition.CenterScreen,
			Width = 1080,
			Height = 720,
			MinimumSize = new Size(960, 620),
			BackColor = Color.FromArgb(238, 241, 245),
			ForeColor = Color.FromArgb(78, 85, 92),
			Font = new Font("Segoe UI", 9f),
			AutoScaleMode = AutoScaleMode.Dpi
		};
	}

	private static void AddSharedBimlifyTitleBar(Form form)
	{
		try
		{
			Type type = (from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
			Type type2 = ((type == null) ? null : type.GetNestedType("BIMlifyForm", BindingFlags.NonPublic));
			if (!(type2 == null) && type2.IsInstanceOfType(form))
			{
				MethodInfo method = type.GetMethod("AddWindowTitleBar", BindingFlags.Static | BindingFlags.NonPublic);
				if (method != null)
				{
					method.Invoke(null, new object[1] { form });
				}
			}
		}
		catch
		{
		}
	}

	private static Control CreateSharedNavigationRail(Form owner)
	{
		try
		{
			Type type = (from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
			Type type2 = ((type == null) ? null : type.GetNestedType("BIMlifySidebar", BindingFlags.NonPublic));
			ConstructorInfo constructorInfo = ((type2 == null) ? null : type2.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((ConstructorInfo c) => c.GetParameters().Length == 3));
			Control rail = (constructorInfo == null) ? null : (constructorInfo.Invoke(new object[3] { owner, false, null }) as Control);
			if (rail != null) DashboardEnhancements.AttachToolSearchButton(rail, owner, 108);
			return rail;
		}
		catch
		{
			return null;
		}
	}

	private static void InvokeBimlifyWindowAction(string methodName, Form owner)
	{
		try
		{
			Type type = (from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
			MethodInfo methodInfo = ((type == null) ? null : type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic));
			if (methodInfo != null)
			{
				methodInfo.Invoke(null, new object[1] { owner });
			}
		}
		catch
		{
		}
	}

	private static IntPtr GetPlanRangeOwnerHandle(IntPtr fallback)
	{
		try
		{
			Type type = (from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
			FieldInfo field = (type == null) ? null : type.GetField("dashboardWindow", BindingFlags.Static | BindingFlags.NonPublic);
			Form dashboard = (field == null) ? null : field.GetValue(null) as Form;
			if (dashboard != null && !dashboard.IsDisposed) return dashboard.Handle;
		}
		catch
		{
		}
		return fallback;
	}
	private static List<PlanRangePlane> ReadPlanPlanes(UIDocument ui, ViewPlan view)
	{
		PlanViewRange viewRange = view.GetViewRange();
		List<PlanRangePlane> list = new List<PlanRangePlane>();
		PlanViewPlane[] array2 = new PlanViewPlane[4]
		{
			PlanViewPlane.TopClipPlane,
			PlanViewPlane.CutPlane,
			PlanViewPlane.BottomClipPlane,
			PlanViewPlane.ViewDepthPlane
		};
		string[] array3 = new string[4] { "Top Clip Plane", "Cut Plane", "Bottom Clip Plane", "View Depth" };
		for (int i = 0; i < array2.Length; i++)
		{
			ElementId levelId = viewRange.GetLevelId(array2[i]);
			double offset = viewRange.GetOffset(array2[i]);
			object obj;
			if (!(levelId == ElementId.InvalidElementId))
			{
				Element element = ui.Document.GetElement(levelId);
				obj = ((element is Level) ? element : null);
			}
			else
			{
				obj = null;
			}
			Level val = (Level)obj;
			bool flag = val == null;
			double num = (flag ? 0.0 : val.Elevation);
			list.Add(new PlanRangePlane
			{
				Name = array3[i],
				Level = (flag ? "Unlimited / not set" : ((Element)val).Name),
				OffsetMm = UnitUtils.ConvertFromInternalUnits(offset, UnitTypeId.Millimeters),
				ElevationMm = (flag ? double.NaN : UnitUtils.ConvertFromInternalUnits(num + offset, UnitTypeId.Millimeters)),
				RelativeMm = UnitUtils.ConvertFromInternalUnits(offset, UnitTypeId.Millimeters),
				Unlimited = flag
			});
		}
		return list;
	}

	private static string FormatPlaneValue(PlanRangePlane plane)
	{
		return plane.Unlimited ? "Unlimited / not set" : (plane.Level + "  |  " + plane.OffsetMm.ToString("+0.##;-0.##;0", CultureInfo.CurrentCulture) + " mm offset  |  " + plane.ElevationMm.ToString("0.##", CultureInfo.CurrentCulture) + " mm elevation");
	}

	private static void SetRangeValue(Control root, string label, string value)
	{
		Label label2 = All(root).OfType<Label>().FirstOrDefault((Label l) => string.Equals(Convert.ToString(l.Tag), "RangeValue:" + label, StringComparison.Ordinal));
		if (label2 != null)
		{
			label2.Text = value;
		}
	}

	private static Panel CreatePlanRangeCard(string caption, Color fill, Color edge, Color gold)
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = fill,
			Margin = new Padding(7),
			Padding = new Padding(1)
		};
		Label heading = new Label
		{
			Text = caption,
			Dock = DockStyle.Top,
			Height = 40,
			Padding = new Padding(14, 0, 8, 0),
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = Color.FromArgb(246, 247, 249),
			ForeColor = Color.FromArgb(55, 67, 79),
			Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
		};
		heading.Paint += (object s, PaintEventArgs e) =>
		{
			using Pen pen = new Pen(gold, 1f);
			e.Graphics.DrawLine(pen, 0, heading.Height - 1, heading.Width, heading.Height - 1);
		};
		panel.Controls.Add(heading);
		ApplyRoundedCardOutline(panel, 9);
		return panel;
	}

	private static void AddRangeValue(TableLayoutPanel table, int row, string label, string value, Color ink, Color muted, Color fill)
	{
		table.RowStyles.Add(new RowStyle(SizeType.Absolute, (row == 0) ? 37 : 64));
		Label control = new Label
		{
			Text = label,
			Tag = "RangeLabel:" + label,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = ink,
			Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
			BackColor = fill,
			Padding = new Padding(7, 2, 4, 2),
			AutoEllipsis = true
		};
		Label control2 = new Label
		{
			Text = value,
			Tag = "RangeValue:" + label,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = muted,
			Font = new Font("Segoe UI", 8.5f),
			BackColor = fill,
			Padding = new Padding(7, 2, 5, 2),
			AutoEllipsis = true
		};
		table.Controls.Add(control, 0, row);
		table.Controls.Add(control2, 1, row);
	}

	public static void ApplyLegacySmartCleaner(Form form, UIDocument ui)
	{
		//IL_04aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f3: Unknown result type (might be due to invalid IL or missing references)
		if (form == null || ui == null)
		{
			return;
		}
		ApplyGlobalTabStyle(form);
		ApplySearchBars(form);
		TabControl tabs = All(form).OfType<TabControl>().FirstOrDefault((TabControl t) => t.TabPages.Cast<TabPage>().Any((TabPage p) => (p.Text ?? string.Empty).IndexOf("Unused", StringComparison.CurrentCultureIgnoreCase) >= 0) && t.TabPages.Cast<TabPage>().Any((TabPage p) => (p.Text ?? string.Empty).IndexOf("Warning", StringComparison.CurrentCultureIgnoreCase) >= 0));
		if (tabs == null)
		{
			if (form.Visible)
			{
				return;
			}
			EventHandler retry = null;
			retry = delegate
			{
				form.Shown -= retry;
				if (!form.IsDisposed)
				{
					ApplyLegacySmartCleaner(form, ui);
				}
			};
			form.Shown += retry;
			return;
		}
		if (!form.Visible)
		{
			if (LegacyCleanerDeferredForms.TryGetValue(form, out var _)) return;
			LegacyCleanerDeferredForms.Add(form, new object());
			EventHandler ensureOverview = null;
			ensureOverview = delegate
			{
				form.Shown -= ensureOverview;
				LegacyCleanerDeferredForms.Remove(form);
				if (!form.IsDisposed)
				{
					ApplyLegacySmartCleaner(form, ui);
				}
			};
			form.Shown += ensureOverview;
			return;
		}
				if (tabs.TabPages.Cast<TabPage>().Any((TabPage p) => string.Equals((p.Text ?? string.Empty).Trim(), "Overview", StringComparison.CurrentCultureIgnoreCase)))
		{
			TabPage existingOverviewTab = tabs.TabPages.Cast<TabPage>().First((TabPage p) => string.Equals((p.Text ?? string.Empty).Trim(), "Overview", StringComparison.CurrentCultureIgnoreCase));
			TabPage existingPurgeTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault((TabPage p) => (p.Text ?? string.Empty).IndexOf("Unused", StringComparison.CurrentCultureIgnoreCase) >= 0);
			if (existingPurgeTab != null) existingPurgeTab.Text = "Unused (Purge)";
			TabPage existingWarningsTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault((TabPage p) => (p.Text ?? string.Empty).IndexOf("Warning", StringComparison.CurrentCultureIgnoreCase) >= 0);
			if (existingWarningsTab != null) existingWarningsTab.Text = "Model Warnings";
				Button existingDelete = All(form).OfType<Button>().FirstOrDefault((Button b) => !string.Equals(b.Name, "BIMlifyPurgeDeleteButton", StringComparison.Ordinal) && (string.Equals((b.Text ?? string.Empty).Trim(), "Purge", StringComparison.CurrentCultureIgnoreCase) || string.Equals((b.Text ?? string.Empty).Trim(), "Delete", StringComparison.CurrentCultureIgnoreCase)));
			if (existingDelete != null)
			{
				existingDelete.Text = "Delete";
				PropertyInfo primary = existingDelete.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (primary != null && primary.CanWrite) primary.SetValue(existingDelete, true, null);
				Button close = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Close", StringComparison.CurrentCultureIgnoreCase));
				PropertyInfo closePrimary = close?.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (closePrimary != null && closePrimary.CanWrite) closePrimary.SetValue(close, true, null);
				else if (close != null)
				{
					close.UseVisualStyleBackColor = false;
					close.FlatStyle = FlatStyle.Flat;
					close.BackColor = Color.FromArgb(250, 230, 168);
					close.ForeColor = Color.FromArgb(55, 67, 79);
					close.FlatAppearance.BorderColor = Color.FromArgb(198, 151, 42);
					close.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 239, 190);
					close.FlatAppearance.MouseDownBackColor = Color.FromArgb(250, 230, 168);
					ApplyRoundedCardOutline(close, 6, Color.FromArgb(198, 151, 42), 1f);
				}
				Button locate = All(form).OfType<Button>().Where((Button b) => (string.Equals((b.Text ?? string.Empty).Trim(), "Locate Elements", StringComparison.CurrentCultureIgnoreCase) || string.Equals((b.Text ?? string.Empty).Trim(), "Locate Element", StringComparison.CurrentCultureIgnoreCase))).OrderByDescending((Button b) => close != null && b.Parent == close.Parent).ThenByDescending((Button b) => b.Visible).FirstOrDefault();
				foreach (Button duplicateLocate in All(form).OfType<Button>().Where((Button b) => b != locate && (string.Equals((b.Text ?? string.Empty).Trim(), "Locate Elements", StringComparison.CurrentCultureIgnoreCase) || string.Equals((b.Text ?? string.Empty).Trim(), "Locate Element", StringComparison.CurrentCultureIgnoreCase))))
				{
					duplicateLocate.Visible = false;
					duplicateLocate.Enabled = false;
				}
				foreach (Button duplicateDelete in All(form).OfType<Button>().Where((Button b) => b != existingDelete && !string.Equals(b.Name, "BIMlifyPurgeDeleteButton", StringComparison.Ordinal) && (string.Equals((b.Text ?? string.Empty).Trim(), "Purge", StringComparison.CurrentCultureIgnoreCase) || string.Equals((b.Text ?? string.Empty).Trim(), "Delete", StringComparison.CurrentCultureIgnoreCase))))
				{
					duplicateDelete.Visible = false;
					duplicateDelete.Enabled = false;
				}
				bool hasUnifiedPurgeFooter = All(form).OfType<Panel>().Any(p => string.Equals(p.Name, "BIMlifyPurgeFooterActions", StringComparison.Ordinal));
				Control footer = close?.Parent ?? existingDelete.Parent;
				// Locate can be nested in a separate footer container from Delete and
				// Close. The legacy footer needs a common coordinate space, but the
				// unified action panel already owns placement; moving controls out of it
				// here caused Locate to overlap Delete on first display.
				if (!hasUnifiedPurgeFooter)
				{
					while (footer != null && (!ContainsControl(footer, existingDelete) || (locate != null && !ContainsControl(footer, locate))))
					{
						footer = footer.Parent;
					}
					if (footer != null)
					{
						Button[] footerButtons = new[] { locate, existingDelete, close }.Where(b => b != null && !b.IsDisposed).ToArray();
						foreach (Button footerButton in footerButtons)
						{
							if (footerButton.Parent != footer)
							{
								footerButton.Parent?.Controls.Remove(footerButton);
								footer.Controls.Add(footerButton);
							}
						}
					}
				}
				// EmptyState owns this footer and already creates/styles Continue. Reuse it
				// instead of creating a second button at the same coordinates.
				Button continueButton = hasUnifiedPurgeFooter
					? All(form).OfType<Button>().FirstOrDefault(b => b.Parent != null && string.Equals(b.Parent.Name, "BIMlifyPurgeFooterActions", StringComparison.Ordinal) && (b.Text ?? string.Empty).Trim().StartsWith("Continue", StringComparison.OrdinalIgnoreCase))
					: All(form).OfType<Button>().FirstOrDefault(b => string.Equals(b.Name, "BIMlifyLegacyContinueButton", StringComparison.Ordinal));
					Action layoutButtons = () => { };
				if (footer != null && close != null && close.Parent == footer)
				{
					if (continueButton == null && !hasUnifiedPurgeFooter)
					{
						try { continueButton = Activator.CreateInstance(close.GetType(), nonPublic: true) as Button; } catch { }
						if (continueButton == null) continueButton = new Button();
					}
					if (continueButton != null && !continueButton.IsDisposed && !string.Equals(continueButton.Name, "BIMlifyLegacyContinueHidden", StringComparison.Ordinal))
					{
						// Keep the unified footer's replacement button distinguishable from
						// the legacy control. The old name was making the live UI appear to
						// keep the stale Continue button, and its styling path matched Close's
						// gold appearance instead of the requested neutral gray.
						continueButton.Name = hasUnifiedPurgeFooter ? "BIMlifyOverviewContinueButton" : "BIMlifyLegacyContinueButton";
						continueButton.Text = "Continue";
						continueButton.Width = close.Width;
						continueButton.Height = close.Height;
						continueButton.FlatStyle = close.FlatStyle;
						continueButton.Font = close.Font;
						ApplyStandardActionButtons(form);
						// Apply Close's appearance once after global styling, so no duplicate
						// neutral palette assignment can override the matching button style.
						continueButton.Width = close.Width;
						continueButton.Height = close.Height;
						MatchSecondaryButtonAppearance(continueButton, close);
						continueButton.Visible = false;
						if (continueButton.Parent == null)
						{
							continueButton.Click += (object sender, EventArgs e) => { if (existingPurgeTab != null) tabs.SelectedTab = existingPurgeTab; };
							footer.Controls.Add(continueButton);
						}
					}
					if (locate != null)
					{
						locate.Text = "Locate Elements";
						locate.Width = Math.Max(locate.Width, TextRenderer.MeasureText(locate.Text, locate.Font).Width + 24);
					}
					layoutButtons = () =>
					{
						if (All(form).OfType<Panel>().Any(p => string.Equals(p.Name, "BIMlifyPurgeFooterActions", StringComparison.Ordinal))) return;
						if (footer == null || footer.IsDisposed) return;
						// Keep Continue in the Overview footer group and mirror Close on
						// every tab/layout pass so later theme passes cannot change its
						// gold fill, outline, or dimensions.
						if (continueButton != null && !continueButton.IsDisposed && close != null && !close.IsDisposed)
						{
							if (continueButton.Parent != footer)
							{
								continueButton.Parent?.Controls.Remove(continueButton);
								footer.Controls.Add(continueButton);
							}
							continueButton.Width = close.Width;
						continueButton.Height = close.Height;
						MatchSecondaryButtonAppearance(continueButton, close);
						}
					if (locate != null && !locate.IsDisposed && locate.Parent != footer)
					{
						locate.Parent?.Controls.Remove(locate);
						footer.Controls.Add(locate);
					}
						int gap = 8;
						Button[] visibleButtons = tabs.SelectedTab == existingOverviewTab && continueButton != null
							? new[] { continueButton, close }.Where((Button button) => button.Visible && button.Parent == footer).ToArray()
							: new[] { locate, existingDelete, close }.Where((Button button) => button != null && button.Visible && button.Parent == footer).ToArray();
						if (visibleButtons.Length == 0) return;
						int requiredWidth = visibleButtons.Sum((Button button) => button.Width) + gap * (visibleButtons.Length - 1) + 16;
						if (footer is Panel && footer.Dock == DockStyle.Right) footer.Width = Math.Max(footer.Width, requiredWidth);
						int left = Math.Max(8, footer.ClientSize.Width - requiredWidth + 8);
						foreach (Button button in visibleButtons)
						{
							button.Dock = DockStyle.None;
							button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
							button.Left = left;
							button.Top = close.Top + (close.Height - button.Height) / 2;
							left += button.Width + gap;
						}
					};
					footer.Resize += (object sender, EventArgs e) => layoutButtons();
					form.Shown += (object sender, EventArgs e) => { if (!form.IsDisposed) layoutButtons(); };
					layoutButtons();
				}
				Action updateDeleteAvailability = () =>
				{
					bool onPurge = existingPurgeTab != null && tabs.SelectedTab == existingPurgeTab;
					bool onOverview = tabs.SelectedTab == existingOverviewTab;
					bool onWarnings = tabs.SelectedTab != null && (tabs.SelectedTab.Text ?? string.Empty).IndexOf("Warning", StringComparison.CurrentCultureIgnoreCase) >= 0;
					bool hasChecks = false;
					try { hasChecks = All(form).OfType<CheckedListBox>().Any((CheckedListBox list) => list.CheckedItems.Count > 0); } catch { }
					bool hasWarningSelection = All(form).OfType<DataGridView>().Any((DataGridView grid) => grid.SelectedRows.Count > 0 && grid != All(form).OfType<DataGridView>().FirstOrDefault((DataGridView g) => g.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => string.Equals(c.HeaderText, "Delete", StringComparison.OrdinalIgnoreCase))));
					bool unifiedPurgeFooter = All(form).OfType<Panel>().Any(p => string.Equals(p.Name, "BIMlifyPurgeFooterActions", StringComparison.Ordinal));
					existingDelete.Visible = onPurge && !unifiedPurgeFooter;
					existingDelete.Enabled = onPurge && hasChecks && !unifiedPurgeFooter;
					if (unifiedPurgeFooter)
					{
						Button actionDelete = All(form).OfType<Button>().FirstOrDefault(b => b.Parent != null && string.Equals(b.Parent.Name, "BIMlifyPurgeFooterActions", StringComparison.Ordinal) && string.Equals(b.Name, "BIMlifyPurgeDeleteButton", StringComparison.Ordinal));
						foreach (Button duplicateDelete in All(form).OfType<Button>().Where(b => b != actionDelete &&
							(string.Equals((b.Text ?? string.Empty).Trim(), "Delete", StringComparison.OrdinalIgnoreCase) || string.Equals((b.Text ?? string.Empty).Trim(), "Purge", StringComparison.OrdinalIgnoreCase))))
						{
							duplicateDelete.Visible = false;
							duplicateDelete.Enabled = false;
						}
						Button actionLocate = All(form).OfType<Button>().FirstOrDefault(b => b.Parent != null && string.Equals(b.Parent.Name, "BIMlifyPurgeFooterActions", StringComparison.Ordinal) && ((b.Text ?? string.Empty).IndexOf("Locate", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(b.Name, "_clean", StringComparison.OrdinalIgnoreCase)));
						foreach (Button candidate in All(form).OfType<Button>().Where(b => ((b.Text ?? string.Empty).IndexOf("Locate", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(b.Name, "_clean", StringComparison.OrdinalIgnoreCase))))
						{
							if (candidate != actionLocate)
							{
								candidate.Visible = false;
								candidate.Enabled = false;
							}
						}
						if (actionLocate != null) actionLocate.Enabled = hasChecks;
					}
					if (!unifiedPurgeFooter && continueButton != null && !continueButton.IsDisposed && !string.Equals(continueButton.Name, "BIMlifyLegacyContinueHidden", StringComparison.Ordinal))
					{
						continueButton.Visible = onOverview;
						continueButton.Enabled = onOverview && existingPurgeTab != null;
					}
					if (locate != null)
					{
						if (!unifiedPurgeFooter)
						{
							locate.Visible = onPurge || (onWarnings && hasWarningSelection);
							locate.Enabled = onPurge ? hasChecks : onWarnings && hasWarningSelection;
						}
					}
					layoutButtons();
					if (locate != null && locate.Visible && !unifiedPurgeFooter) locate.BringToFront();
					if (existingDelete.Visible) existingDelete.BringToFront();
					// EmptyState's unified action panel owns button placement and order.
					// BringToFront would change which action is painted over another.
					if (!unifiedPurgeFooter) close.BringToFront();
				};
				if (!LegacyDeleteVisibilityTabs.TryGetValue(tabs, out var _))
				{
					LegacyDeleteVisibilityTabs.Add(tabs, new object());
					tabs.SelectedIndexChanged += (object sender, EventArgs e) => updateDeleteAvailability();
					CheckedListBox legacyPurgeChecks = All(form).OfType<CheckedListBox>().FirstOrDefault();
					if (legacyPurgeChecks != null) legacyPurgeChecks.ItemCheck += (object sender, ItemCheckEventArgs e) => { try { form.BeginInvoke((Action)updateDeleteAvailability); } catch { } };
					foreach (DataGridView warningGrid in All(form).OfType<DataGridView>())
						warningGrid.SelectionChanged += (object sender, EventArgs e) => updateDeleteAvailability();
					if (locate != null) locate.Click += (object sender, EventArgs e) =>
					{
						if (tabs.SelectedTab != existingPurgeTab) return;
						try
						{
							List<ElementId> located = new List<ElementId>();
							foreach (object checkedItem in All(form).OfType<CheckedListBox>().SelectMany((CheckedListBox list) => list.CheckedItems.Cast<object>()))
							{
								FieldInfo idField = checkedItem.GetType().GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								ElementId id = idField?.GetValue(checkedItem) as ElementId;
								Element candidate = id == null ? null : ui.Document.GetElement(id);
								if (candidate is ElementType)
								{
									foreach (Element instance in new FilteredElementCollector(ui.Document).WhereElementIsNotElementType())
										try { if (instance.GetTypeId() == id) located.Add(instance.Id); } catch { }
								}
								else if (candidate != null) located.Add(candidate.Id);
							}
							located = located.Distinct().ToList();
							if (located.Count == 0) { TaskDialog.Show("Locate Element", "No placed model instances were found for the checked unused items. These types have no location in the model."); return; }
							ui.Selection.SetElementIds((ICollection<ElementId>)located);
							try { ui.ShowElements((ICollection<ElementId>)located); } catch { }
						}
						catch (Exception ex) { TaskDialog.Show("Locate Element", "Revit could not locate the checked elements.\n\n" + ex.Message); }
					};
					form.Shown += (object sender, EventArgs e) =>
					{
						try { form.BeginInvoke((Action)(() =>
						{
							if (existingPurgeTab != null && !existingPurgeTab.IsDisposed) existingPurgeTab.Text = "Unused (Purge)";
							if (existingWarningsTab != null && !existingWarningsTab.IsDisposed) existingWarningsTab.Text = "Model Warnings";
							tabs.Invalidate();
							updateDeleteAvailability();
						})); } catch { updateDeleteAvailability(); }
					};
				}
				updateDeleteAvailability();
			}
			return;
		}
		TabPage purgeTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault((TabPage p) => (p.Text ?? string.Empty).IndexOf("Unused", StringComparison.CurrentCultureIgnoreCase) >= 0);
		Button deleteButton = null;
		foreach (Button item in All(form).OfType<Button>())
		{
			if (string.Equals(item.Name, "BIMlifyPurgeDeleteButton", StringComparison.Ordinal)) continue;
			string a = (item.Text ?? string.Empty).Trim();
						if (string.Equals(a, "Purge", StringComparison.CurrentCultureIgnoreCase))
			{
				item.Text = "Delete";
				item.Visible = true;
				deleteButton = item;
			}
			else if (string.Equals(a, "Select All", StringComparison.CurrentCultureIgnoreCase) || string.Equals(a, "Clear", StringComparison.CurrentCultureIgnoreCase))
			{
				item.Visible = false;
			}
		}
				Button selectAllButton = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Select All", StringComparison.CurrentCultureIgnoreCase));
		Button clearSelectionButton = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Clear", StringComparison.CurrentCultureIgnoreCase));
						Button locateButton = All(form).OfType<Button>().FirstOrDefault((Button b) => (string.Equals((b.Text ?? string.Empty).Trim(), "Locate Elements", StringComparison.CurrentCultureIgnoreCase) || string.Equals((b.Text ?? string.Empty).Trim(), "Locate Element", StringComparison.CurrentCultureIgnoreCase)));
		Button closeButton = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Close", StringComparison.CurrentCultureIgnoreCase));
		Button overviewContinue = null;
		TabPage overviewTab = null;
		Control footerGroup = closeButton?.Parent ?? deleteButton?.Parent ?? locateButton?.Parent;
		while (footerGroup != null &&
			((locateButton != null && !ContainsControl(footerGroup, locateButton)) ||
			 (deleteButton != null && !ContainsControl(footerGroup, deleteButton)) ||
			 (closeButton != null && !ContainsControl(footerGroup, closeButton)))
		)
			footerGroup = footerGroup.Parent;
		if (footerGroup != null)
		{
			// Create a fresh Overview Continue action from Close and route it to Unused (Purge).
			if (closeButton != null && !closeButton.IsDisposed)
			{
				try { overviewContinue = Activator.CreateInstance(closeButton.GetType(), true) as Button; } catch { }
				if (overviewContinue == null) overviewContinue = new Button();
				overviewContinue.Name = "BIMlifyOverviewContinueButton";
				overviewContinue.Text = "Continue";
				overviewContinue.AutoSize = false;
				overviewContinue.Size = closeButton.Size;
				overviewContinue.Visible = false;
				overviewContinue.Click += (sender, args) => { if (purgeTab != null && !purgeTab.IsDisposed) tabs.SelectedTab = purgeTab; };
				footerGroup.Controls.Add(overviewContinue);
				MatchSecondaryButtonAppearance(overviewContinue, closeButton);
				foreach (Button oldContinue in All(form).OfType<Button>().Where(b => b != overviewContinue && (b.Text ?? string.Empty).Trim().StartsWith("Continue", StringComparison.OrdinalIgnoreCase)))
				{
					oldContinue.Visible = false;
					oldContinue.Enabled = false;
				}
			}
			foreach (Button action in new[] { locateButton, deleteButton, closeButton, overviewContinue }.Where(b => b != null && !b.IsDisposed))
			{
				if (action.Parent != footerGroup)
				{
					action.Parent?.Controls.Remove(action);
					footerGroup.Controls.Add(action);
				}
			}
		}
		foreach (Button duplicateLocate in All(form).OfType<Button>().Where(b => b != locateButton && (b.Text ?? string.Empty).IndexOf("Locate", StringComparison.OrdinalIgnoreCase) >= 0))
		{
			duplicateLocate.Visible = false;
			duplicateLocate.Enabled = false;
		}
		foreach (Button duplicateDelete in All(form).OfType<Button>().Where(b => b != deleteButton && !string.Equals(b.Name, "BIMlifyPurgeDeleteButton", StringComparison.Ordinal) && (string.Equals((b.Text ?? string.Empty).Trim(), "Delete", StringComparison.OrdinalIgnoreCase) || string.Equals((b.Text ?? string.Empty).Trim(), "Purge", StringComparison.OrdinalIgnoreCase))))
		{
			duplicateDelete.Visible = false;
			duplicateDelete.Enabled = false;
		}
		if (locateButton != null)
		{
			locateButton.Text = "Locate Elements";
			locateButton.Width = Math.Max(locateButton.Width, TextRenderer.MeasureText(locateButton.Text, locateButton.Font).Width + 24);
		}
		Action layoutFooterButtons = () =>
		{
			if (footerGroup == null || footerGroup.IsDisposed || closeButton == null || closeButton.IsDisposed) return;
			if (overviewContinue != null && !overviewContinue.IsDisposed)
			{
				overviewContinue.Text = "Continue";
				overviewContinue.AutoSize = false;
				overviewContinue.Size = closeButton.Size;
				MatchSecondaryButtonAppearance(overviewContinue, closeButton);
			}
			foreach (Button action in new[] { locateButton, deleteButton, closeButton, overviewContinue }.Where(b => b != null && !b.IsDisposed))
			{
				if (action.Parent != footerGroup)
				{
					action.Parent?.Controls.Remove(action);
					footerGroup.Controls.Add(action);
				}
			}
			List<Button> visibleButtons = new List<Button>();
			if (tabs.SelectedTab == overviewTab && overviewContinue != null && overviewContinue.Visible) visibleButtons.Add(overviewContinue);
			else if (locateButton != null && locateButton.Visible) visibleButtons.Add(locateButton);
			if (deleteButton != null && deleteButton.Visible) visibleButtons.Add(deleteButton);
			visibleButtons.Add(closeButton);
			int gap = 8;
			int width = visibleButtons.Sum((Button b) => b.Width) + gap * Math.Max(0, visibleButtons.Count - 1) + 16;
			if (footerGroup is Panel && footerGroup.Dock == DockStyle.Right) footerGroup.Width = Math.Max(footerGroup.Width, width);
			int right = footerGroup.ClientSize.Width - 8;
			for (int i = visibleButtons.Count - 1; i >= 0; i--)
			{
				Button button = visibleButtons[i];
				button.Dock = DockStyle.None;
				button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
				button.Left = right - button.Width;
				button.Top = closeButton.Top + (closeButton.Height - button.Height) / 2;
				right = button.Left - gap;
			}
		};
		if (footerGroup != null)
		{
			footerGroup.Resize += (object sender, EventArgs e) => layoutFooterButtons();
			if (footerGroup.Parent != null) footerGroup.Parent.Resize += (object sender, EventArgs e) => layoutFooterButtons();
		}
		form.Resize += (object sender, EventArgs e) => layoutFooterButtons();
		CheckedListBox purgeChecks = All(form).OfType<CheckedListBox>().FirstOrDefault();
		DataGridView[] cleanerGrids = All(form).OfType<DataGridView>().ToArray();
		DataGridView purgeGrid = cleanerGrids.FirstOrDefault((DataGridView g) => g.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => string.Equals(c.Name, "Pick", StringComparison.OrdinalIgnoreCase) || string.Equals(c.HeaderText, "Delete", StringComparison.OrdinalIgnoreCase)));
		DataGridView warningRows = cleanerGrids.FirstOrDefault((DataGridView g) => g != purgeGrid);
		TabPage warningsTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault((TabPage p) => (p.Text ?? string.Empty).IndexOf("Warning", StringComparison.CurrentCultureIgnoreCase) >= 0);
		if (selectAllButton != null && clearSelectionButton != null)
		{
			selectAllButton.Text = "Select All";
			clearSelectionButton.Text = "Clear Selection";
			selectAllButton.Width = 106;
			clearSelectionButton.Width = 126;
			selectAllButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
			clearSelectionButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
			selectAllButton.Left = 208;
			clearSelectionButton.Left = 322;
			selectAllButton.Top = 8;
			clearSelectionButton.Top = 8;
			Action updateSelectionButtons = () =>
			{
				bool onPurge = tabs.SelectedTab == purgeTab;
				bool onWarnings = tabs.SelectedTab == warningsTab;
				bool onOverview = overviewTab != null && tabs.SelectedTab == overviewTab;
				selectAllButton.Visible = onPurge || onWarnings;
				clearSelectionButton.Visible = onPurge || onWarnings;
				bool checkedPurgeGrid = purgeGrid != null && purgeGrid.Rows.Cast<DataGridViewRow>().Any((DataGridViewRow r) => !r.IsNewRow && Convert.ToBoolean(r.Cells.Cast<DataGridViewCell>().FirstOrDefault((DataGridViewCell c) => string.Equals(c.OwningColumn.Name, "Pick", StringComparison.OrdinalIgnoreCase) || string.Equals(c.OwningColumn.HeaderText, "Delete", StringComparison.OrdinalIgnoreCase))?.Value ?? false));
				bool hasPurgeRows = purgeGrid != null && purgeGrid.Rows.Cast<DataGridViewRow>().Any((DataGridViewRow r) => !r.IsNewRow);
				bool hasPurgeSelection = checkedPurgeGrid || purgeChecks != null && purgeChecks.CheckedItems.Count > 0;
				if (deleteButton != null)
				{
					deleteButton.Visible = onPurge;
					deleteButton.Enabled = onPurge && hasPurgeSelection;
				}
				bool hasWarningSelection = warningRows != null && warningRows.SelectedRows.Count > 0;
				if (locateButton != null)
				{
					locateButton.Visible = onPurge || (onWarnings && hasWarningSelection);
					locateButton.Enabled = onPurge ? hasPurgeSelection : onWarnings && hasWarningSelection;
				}
				if (overviewContinue != null)
				{
					overviewContinue.Visible = onOverview;
					overviewContinue.Enabled = onOverview && purgeTab != null && !purgeTab.IsDisposed;
				}
				selectAllButton.Enabled = onPurge ? hasPurgeRows || purgeChecks != null && purgeChecks.Items.Count > 0 : onWarnings && warningRows != null && warningRows.Rows.Count > 0;
				clearSelectionButton.Enabled = onPurge ? hasPurgeSelection : onWarnings && warningRows != null && warningRows.SelectedRows.Count > 0;
				layoutFooterButtons();
			};
			tabs.SelectedIndexChanged += (object sender, EventArgs e) => updateSelectionButtons();
			if (purgeChecks != null)
			{
				purgeChecks.ItemCheck += (object sender, ItemCheckEventArgs e) =>
				{
					try { form.BeginInvoke((Action)(() => updateSelectionButtons())); } catch { }
				};
			}
			if (purgeGrid != null)
			{
				purgeGrid.CellValueChanged += (object sender, DataGridViewCellEventArgs e) => updateSelectionButtons();
				purgeGrid.CurrentCellDirtyStateChanged += (object sender, EventArgs e) =>
				{
					if (purgeGrid.IsCurrentCellDirty) purgeGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
					updateSelectionButtons();
				};
			}
			if (warningRows != null)
			{
				warningRows.SelectionChanged += (object sender, EventArgs e) => updateSelectionButtons();
			warningRows.CellValueChanged += (object sender, DataGridViewCellEventArgs e) => updateSelectionButtons();
				clearSelectionButton.Click += (object sender, EventArgs e) => { if (tabs.SelectedTab == warningsTab) warningRows.ClearSelection(); };
			}
			updateSelectionButtons();
		}
		if (locateButton != null)
		{
			ToolTip locateTip = new ToolTip { ShowAlways = true, AutoPopDelay = 8000, InitialDelay = 400 };
			locateTip.SetToolTip(locateButton, "Locate selected warning elements or any placed instances of the selected unused definitions. Unused definitions with no instances have no model location to zoom to.");
		}// The Smart Cleaner list owns its checked state and initializes it before
		// the window is shown. Do not rewrite every item on Shown or cancel
		// ItemCheck here: doing so queues one UI update per item and blocks the
		// purge selection grid from mirroring checkbox edits.
		Color backColor = Color.FromArgb(238, 241, 245);
		Color background = Color.FromArgb(247, 247, 245);
		Color color = Color.FromArgb(78, 85, 92);
		Color foreColor = Color.FromArgb(102, 109, 118);
		Color gold = Color.FromArgb(198, 151, 42);
		TabPage tabPage = new TabPage("Overview")
		{
			BackColor = backColor,
			ForeColor = color,
			Padding = new Padding(8),
			UseVisualStyleBackColor = false
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			BackColor = backColor,
			Padding = new Padding(2)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 108f));
		// Keep the stacked overview notes compact while preserving room for the wrapped first note.
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			BackColor = backColor,
			Padding = new Padding(2, 2, 2, 2),
			Margin = Padding.Empty
		};
		for (int num = 0; num < 3; num++)
		{
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
		}
		CheckedListBox checkedListBox = All(form).OfType<CheckedListBox>().FirstOrDefault();
		DataGridView dataGridView = All(form).OfType<DataGridView>().FirstOrDefault();
		int num2 = 0;
		try
		{
			Document document = ui.Document;
			HashSet<ElementId> placed = new HashSet<ElementId>(from Viewport v in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(Viewport))
				select v.ViewId);
			num2 = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(View))).Cast<View>().Count((View v) =>
			{
				//IL_0009: Unknown result type (might be due to invalid IL or missing references)
				//IL_000f: Invalid comparison between Unknown and I4
				//IL_0012: Unknown result type (might be due to invalid IL or missing references)
				//IL_0018: Invalid comparison between Unknown and I4
				return !v.IsTemplate && (int)v.ViewType != 6 && (int)v.ViewType != 7 && !placed.Contains(((Element)v).Id);
			});
		}
		catch
		{
		}
		AddLegacyMetric(tableLayoutPanel2, 0, "CLEANUP CANDIDATES", (checkedListBox == null) ? "0" : checkedListBox.Items.Count.ToString(), color, gold, background);
		AddLegacyMetric(tableLayoutPanel2, 1, "VIEWS TO REVIEW", num2.ToString(), color, gold, background);
		AddLegacyMetric(tableLayoutPanel2, 2, "MODEL WARNINGS", (dataGridView == null) ? "0" : dataGridView.Rows.Count.ToString(), color, gold, background);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		Panel notesCard = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.FromArgb(248, 249, 250),
			Padding = new Padding(8, 8, 8, 8),
			Margin = new Padding(6, 6, 6, 0)
		};
		TableLayoutPanel notesLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = notesCard.BackColor,
			ColumnCount = 2,
			RowCount = 2,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		notesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22f));
		notesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		notesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60f));
		notesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));
		string[] noteTexts =
		{
			"Review Revit-reported unused items, views that may need review, and model warnings. Smart Cleaner provides findings only; use Review & Clean to choose and confirm any deletion.",
			"A view not placed on a sheet is a review suggestion, not proof that it is unused. Warnings and view findings are never deleted by Smart Cleaner."
		};
		for (int noteIndex = 0; noteIndex < noteTexts.Length; noteIndex++)
		{
			Label bullet = new Label
			{
				Text = "•",
				Dock = DockStyle.Fill,
				ForeColor = gold,
				Font = new Font("Segoe UI", 10f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleCenter,
				BackColor = notesCard.BackColor,
				Margin = Padding.Empty
			};
			Label noteText = new Label
			{
				Dock = DockStyle.Fill,
				BackColor = notesCard.BackColor,
				ForeColor = foreColor,
				Font = new Font("Segoe UI", 8.25f),
				Padding = new Padding(2, 3, 8, 3),
				TextAlign = ContentAlignment.MiddleLeft,
				Text = noteTexts[noteIndex],
				Margin = Padding.Empty
			};
			notesLayout.Controls.Add(bullet, 0, noteIndex);
			notesLayout.Controls.Add(noteText, 1, noteIndex);
		}
		notesCard.Controls.Add(notesLayout);
		ApplyRoundedCardOutline(notesCard, 9);
		tableLayoutPanel.Controls.Add(notesCard, 0, 1);
		tabPage.Controls.Add(tableLayoutPanel);
		tabs.TabPages.Insert(0, tabPage);
		overviewTab = tabPage;
		tabs.SelectedTab = tabPage;
		layoutFooterButtons();
		ApplyGlobalTabStyle(form);
		ApplyStandardActionButtons(form);
		layoutFooterButtons();
	}

	private static void AddLegacyMetric(TableLayoutPanel parent, int column, string title, string value, Color ink, Color gold, Color background)
	{
		RoundedMetricCard roundedMetricCard = new RoundedMetricCard
		{
			Dock = DockStyle.Fill,
			BackColor = Color.White,
			Margin = new Padding(6, 4, 6, 4),
			Padding = new Padding(12, 10, 10, 8)
		};
		Label value2 = new Label
		{
			Text = title,
			Left = 12,
			Top = 9,
			AutoSize = true,
			ForeColor = ink,
			Font = new Font("Segoe UI", 8f, FontStyle.Bold)
		};
		Label value3 = new Label
		{
			Text = value,
			Left = 12,
			Top = 34,
			AutoSize = true,
			ForeColor = gold,
			Font = new Font("Segoe UI", 18f, FontStyle.Bold)
		};
		roundedMetricCard.Controls.Add(value2);
		roundedMetricCard.Controls.Add(value3);
		parent.Controls.Add(roundedMetricCard, column, 0);
	}

	public static void ApplyPatternMaker(Form form)
	{
		if (form == null)
		{
			return;
		}
		ApplyGlobalTabStyle(form);
		ApplySearchBars(form);
		form.Width = 1080;
		form.Height = 720;
		form.MinimumSize = new Size(980, 640);
		Label description = FindLabel(form, "Description");
		Label previewHeading = FindLabel(form, "Preview");
		StyleHeading(description);
		StyleHeading(previewHeading);
		Panel editor = ((description == null) ? null : (description.Parent as Panel));
		if (editor == null)
		{
			return;
		}
		Control body = editor.Parent;
		Panel previewPanel = ((previewHeading == null) ? null : (previewHeading.Parent as Panel));
		Panel previewCanvas = ((previewPanel == null) ? null : previewPanel.Controls.OfType<Panel>().FirstOrDefault((Panel p) => p.Height > 100));
		Panel searchHeader = ((previewPanel == null) ? null : previewPanel.Controls.OfType<Panel>().FirstOrDefault((Panel p) => p.Controls.OfType<Label>().Any((Label l) => (l.Text ?? string.Empty).StartsWith("Search patterns in this project", StringComparison.Ordinal))));
		Label searchHeading = ((searchHeader == null) ? null : searchHeader.Controls.OfType<Label>().FirstOrDefault());
		TextBox librarySearch = ((previewPanel == null) ? null : previewPanel.Controls.OfType<TextBox>().FirstOrDefault());
		ComboBox patternLibrary = ((previewPanel == null) ? null : previewPanel.Controls.OfType<ComboBox>().FirstOrDefault());
		Label zoomInfo = All(form).OfType<Label>().FirstOrDefault((Label l) => (l.Text ?? string.Empty).StartsWith("Preview zoom:", StringComparison.Ordinal));
		Panel zoomBar = ((zoomInfo == null) ? null : (zoomInfo.Parent as Panel));
		FlowLayoutPanel zoomButtons = ((zoomBar == null) ? null : zoomBar.Controls.OfType<FlowLayoutPanel>().FirstOrDefault());
		Label patternStatus = ((previewPanel == null) ? null : previewPanel.Controls.OfType<Label>().FirstOrDefault((Label l) => l.Dock == DockStyle.Bottom));

		if (previewCanvas != null)
		{
			previewCanvas.Controls.Clear();
			previewCanvas.BackColor = Color.White;
			ApplyRoundedCardOutline(previewCanvas, 7, Color.FromArgb(163, 174, 185), 1.5f);
		}
		if (zoomBar != null)
		{
			zoomBar.Dock = DockStyle.None;
			zoomBar.Height = 34;
			zoomBar.Padding = new Padding(4, 2, 4, 2);
			zoomBar.BackColor = Color.White;
			if (zoomInfo != null)
			{
				zoomInfo.Dock = DockStyle.None;
				zoomInfo.Height = 30;
				zoomInfo.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
			}
			if (zoomButtons != null)
			{
				zoomButtons.Dock = DockStyle.None;
				zoomButtons.Height = 30;
				zoomButtons.WrapContents = false;
				zoomButtons.Padding = Padding.Empty;
				zoomButtons.Margin = Padding.Empty;
				foreach (Button item in zoomButtons.Controls.OfType<Button>())
				{
					item.BackColor = Color.FromArgb(238, 241, 245);
					item.UseVisualStyleBackColor = false;
					item.FlatStyle = FlatStyle.Flat;
					item.FlatAppearance.BorderColor = Color.FromArgb(190, 198, 207);
					item.FlatAppearance.BorderSize = 1;
				}
			}
		}
		if (previewHeading != null)
		{
			previewHeading.Dock = DockStyle.None;
			previewHeading.Height = 26;
			previewHeading.BackColor = Color.White;
		}
		if (previewCanvas != null)
		{
			previewCanvas.Dock = DockStyle.None;
			previewCanvas.BackColor = Color.White;
		}
		if (searchHeader != null)
		{
			searchHeader.Dock = DockStyle.None;
			searchHeader.Height = 36;
		}
		if (searchHeading != null)
		{
			searchHeading.Dock = DockStyle.None;
			searchHeading.Height = 30;
			searchHeading.BackColor = Color.White;
		}
		if (librarySearch != null)
		{
			librarySearch.Dock = DockStyle.None;
			librarySearch.Height = 30;
		}
		if (patternLibrary != null)
		{
			patternLibrary.Dock = DockStyle.None;
			patternLibrary.Height = 32;
		}
		if (patternStatus != null)
		{
			patternStatus.Dock = DockStyle.None;
			patternStatus.Height = 24;
			patternStatus.BackColor = Color.Transparent;
			patternStatus.Visible = !string.IsNullOrWhiteSpace(patternStatus.Text);
			patternStatus.TextChanged += (sender, args) => patternStatus.Visible = !string.IsNullOrWhiteSpace(patternStatus.Text);
			patternStatus.Font = new Font("Segoe UI", 8f);
		}
		Panel centerDivider = null;
		if (body != null)
		{
			centerDivider = body.Controls.OfType<Panel>().FirstOrDefault((Panel p) => p.Name == "BIMlifyPatternMakerDivider");
			if (centerDivider == null)
			{
				centerDivider = new Panel
				{
					Name = "BIMlifyPatternMakerDivider",
					Width = 1,
					BackColor = Color.White,
					Anchor = (AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left)
				};
				body.Controls.Add(centerDivider);
				centerDivider.BringToFront();
			}
			editor.Padding = new Padding(8, editor.Padding.Top, 22, editor.Padding.Bottom);
			if (previewPanel != null)
			{
				previewPanel.Padding = Padding.Empty;
			}
		}
		TextBox[] tileBoxes = (from c in editor.Controls.OfType<TextBox>()
			where c.Top >= 210 && c.Top < 255
			orderby c.Left
			select c).ToArray();
		Dictionary<Control, int> editorTop = editor.Controls.Cast<Control>().ToDictionary((Control c) => c, (Control c) => c.Top);
		Dictionary<Control, int> editorLeft = editor.Controls.Cast<Control>().ToDictionary((Control c) => c, (Control c) => c.Left);
		Action layout = () =>
		{
			if (body != null && body.ClientSize.Width > 0)
			{
				int x = 16;
				int width = (body.ClientSize.Width - 2 * x - 28) / 2;
				editor.Dock = DockStyle.None;
				editor.SetBounds(x, 16, width, Math.Max(0, body.ClientSize.Height - 2 * x));
				if (previewPanel != null)
				{
					previewPanel.Dock = DockStyle.None;
					previewPanel.SetBounds(editor.Right + 28, 16, width, Math.Max(0, body.ClientSize.Height - 2 * x));
				}
				if (centerDivider != null)
				{
					centerDivider.SetBounds(editor.Right + (28 - centerDivider.Width) / 2, 16, 1, Math.Max(0, body.ClientSize.Height - 2 * x));
				}
			}
			if (previewPanel != null)
			{
				int num = 20;
				int num2 = 16;
				int num3 = Math.Max(100, previewPanel.ClientSize.Width - num - num2);
				int num4 = 4;
				int num5 = 26;
				int num6 = 36;
				int num7 = 36;
				int num8 = 30;
				int num9 = 32;
				int num10 = 30;
				int num11 = 4;
				int num12 = Math.Max(num4 + num5 + num6 + 120, previewPanel.ClientSize.Height - num10);
				int num13 = num12 - num11 - num9;
				int num14 = num13 - num11 - num8;
				int num15 = num14 - num7;
				if (previewHeading != null)
				{
					previewHeading.SetBounds(num, num4, num3, num5);
				}
				if (zoomBar != null)
				{
					int y = num4 + num5 + num11;
					zoomBar.SetBounds(num, y, num3, num6);
					int num16 = ((zoomButtons != null) ? Math.Min(num3, Math.Max(220, zoomButtons.Controls.Cast<Control>().Sum((Control c) => c.Width + c.Margin.Horizontal))) : 0);
					if (zoomInfo != null)
					{
						zoomInfo.SetBounds(4, 1, Math.Max(100, num3 - num16 - 12), 30);
					}
					if (zoomButtons != null)
					{
						zoomButtons.SetBounds(Math.Max(0, num3 - num16 - 4), 1, num16, 30);
					}
				}
				if (previewCanvas != null)
				{
					previewCanvas.SetBounds(num, (zoomBar == null) ? (num4 + num5 + num11) : (zoomBar.Bottom + num11), num3, Math.Max(80, num15 - ((zoomBar == null) ? (num4 + num5 + num11) : (zoomBar.Bottom + num11)) - num11));
				}
				if (searchHeader != null)
				{
					searchHeader.SetBounds(num, num15, num3, num7);
				}
				if (searchHeading != null)
				{
					searchHeading.SetBounds(0, 3, Math.Max(180, num3 - 112), 30);
				}
				Button button = ((searchHeader == null) ? null : searchHeader.Controls.OfType<Button>().FirstOrDefault());
				if (button != null)
				{
					button.Dock = DockStyle.None;
					button.SetBounds(Math.Max(0, num3 - 108), 3, 104, 30);
				}
				if (librarySearch != null)
				{
					librarySearch.SetBounds(num, num14, num3, num8);
				}
				if (patternLibrary != null)
				{
					patternLibrary.SetBounds(num, num13, num3, num9);
				}
				if (patternStatus != null)
				{
					patternStatus.SetBounds(num, num12, num3, Math.Max(0, Math.Min(24, previewPanel.ClientSize.Height - num12 - 8)));
				}
				if (previewCanvas != null)
				{
					previewCanvas.BringToFront();
				}
				if (patternStatus != null)
				{
					patternStatus.BringToFront();
				}
			}
			foreach (Control control in editor.Controls)
			{
				if (editorTop.TryGetValue(control, out var value))
				{
						if (value >= 26) 						{ 							Button actionButton = control as Button; 							bool isDescriptionAction = actionButton != null && (string.Equals(actionButton.Text, "Read Description", StringComparison.OrdinalIgnoreCase) || string.Equals(actionButton.Text, "Write with AI", StringComparison.OrdinalIgnoreCase)); 							control.Top = value + (isDescriptionAction ? 14 : 8); 						} 						else control.Top = value;
				}
				if (editorLeft.TryGetValue(control, out var value2))
				{
					control.Left = value2 + 8;
				}
			}
			int num17 = Math.Max(300, editor.ClientSize.Width - editor.Padding.Horizontal - 4);
			int width2 = Math.Max(180, num17 - 16);
			SetWidth(editor, description, width2);
			TextBox child = ((description == null) ? null : editor.Controls.OfType<TextBox>().FirstOrDefault((TextBox c) => c.Top >= 20 && c.Top < 100));
			SetWidth(editor, child, width2);
			Label nameLabel = FindChildLabel(editor, "Pattern name");
			TextBox child2 = ((nameLabel == null) ? null : editor.Controls.OfType<TextBox>().FirstOrDefault((TextBox c) => c.Top >= nameLabel.Bottom && c.Top < 195));
			SetWidth(editor, child2, width2);
			string[] array = new string[3] { "Tile width (mm)", "Tile height (mm)", "Grout joint (mm)" };
			int num18 = 10;
			int num19 = Math.Max(70, (num17 - 16 - 2 * num18) / 3);
			for (int num20 = 0; num20 < array.Length; num20++)
			{
				Label label = FindChildLabel(editor, array[num20]);
				if (label != null)
				{
					int left = (label.Left = 16 + num20 * (num19 + num18));
					label.Width = num19;
					TextBox textBox = ((num20 < tileBoxes.Length) ? tileBoxes[num20] : null);
					if (textBox != null)
					{
						textBox.Left = left;
						textBox.Width = num19;
					}
				}
			}
			Label label2 = FindChildLabel(editor, "Layout");
			Label label3 = FindChildLabel(editor, "Pattern type");
			int num22 = Math.Max(120, (num17 - 16 - 10) / 2);
			if (label2 != null)
			{
				label2.Left = 16;
				label2.Width = num22;
			}
			if (label3 != null)
			{
				label3.Left = 26 + num22;
				label3.Width = num22;
			}
			ComboBox[] array2 = editor.Controls.OfType<ComboBox>().ToArray();
			ComboBox[] array3 = array2;
			foreach (ComboBox comboBox in array3)
			{
				comboBox.BackColor = PaleGold;
			}
			if (array2.Length >= 2)
			{
				array2[0].SetBounds(16, 293, num22, 28);
				array2[1].SetBounds(26 + num22, 293, num22, 28);
			}
			Label child3 = editor.Controls.OfType<Label>().FirstOrDefault((Label c) => c.Text.StartsWith("Sizes are entered", StringComparison.Ordinal));
			SetWidth(editor, child3, width2);
		};
		editor.Resize += delegate
		{
			layout();
		};
		if (body != null)
		{
			body.Resize += delegate
			{
				layout();
			};
		}
		layout();
		Button closeButton = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Close", StringComparison.OrdinalIgnoreCase));
		Button createPatternButton = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Create Pattern", StringComparison.OrdinalIgnoreCase) || string.Equals((b.Text ?? string.Empty).Trim(), "Make Pattern", StringComparison.OrdinalIgnoreCase));
		Control footerButtons = closeButton?.Parent;
		if (closeButton != null && createPatternButton != null && footerButtons != null && createPatternButton.Parent == footerButtons)
		{
			footerButtons.BackColor = Color.FromArgb(216, 221, 227);
			closeButton.BackColor = footerButtons.BackColor;
			closeButton.UseVisualStyleBackColor = false;
			PropertyInfo closePrimary = closeButton.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (closePrimary != null && closePrimary.CanWrite) closePrimary.SetValue(closeButton, true, null);
			Action layoutFooterButtons = () =>
			{
				if (footerButtons.IsDisposed) return;
				const int gap = 8;
				closeButton.Dock = DockStyle.None;
				createPatternButton.Dock = DockStyle.None;
				closeButton.Width = Math.Max(closeButton.Width, 96);
				createPatternButton.Width = Math.Max(createPatternButton.Width, 104);
				closeButton.Left = Math.Max(0, footerButtons.ClientSize.Width - closeButton.Width - 12);
				createPatternButton.Left = Math.Max(0, closeButton.Left - createPatternButton.Width - gap);
				closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
				createPatternButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
				createPatternButton.Top = closeButton.Top;
				Region previous = closeButton.Region;
				using (GraphicsPath path = RoundedSearchPath(new Rectangle(0, 0, Math.Max(1, closeButton.Width - 1), Math.Max(1, closeButton.Height - 1)), 7))
				{
					closeButton.Region = new Region(path);
				}
				previous?.Dispose();
				createPatternButton.BringToFront();
				closeButton.BringToFront();
			};
			footerButtons.Resize += (object sender, EventArgs e) => layoutFooterButtons();
			form.Shown += (object sender, EventArgs e) => layoutFooterButtons();
			layoutFooterButtons();
		}
	}

	public static void ApplyReviewTabs(Form form)
	{
		if (form == null)
		{
			return;
		}
		ApplyGlobalTabStyle(form);
		foreach (TabControl item in All(form).OfType<TabControl>())
		{
			if (!item.TabPages.Cast<TabPage>().Any((TabPage p) => p.Text == "Families") || !item.TabPages.Cast<TabPage>().Any((TabPage p) => p.Text == "Materials") || !form.Text.Contains("Review & Clean"))
			{
				continue;
			}
			TabPage[] source = item.TabPages.Cast<TabPage>().ToArray();
			var array = (from p in source
				select new
				{
					Page = p,
					Grid = All(p).OfType<DataGridView>().FirstOrDefault()
				} into x
				where x.Grid != null
				select x).ToArray();
			if (array.Length == 0)
			{
				continue;
			}
			var main = array.FirstOrDefault(x => x.Page.Text == "Families");
			if (main == null)
			{
				continue;
			}
			var list = array.SelectMany(anon => from DataGridViewRow row in anon.Grid.Rows
				where !row.IsNewRow && row.Tag != null
				select new
				{
					Category = anon.Page.Text,
					Name = row.Cells[1].Value,
					Type = row.Cells[2].Value,
					Id = row.Cells[3].Value,
					Note = row.Cells[4].Value,
					Candidate = row.Tag
				}).ToList();
			DataGridView results = main.Grid;
			foreach (var item2 in array.Where(x => x.Grid != results))
			{
				item2.Grid.Rows.Clear();
			}
			results.Rows.Clear();
			results.Columns[0].HeaderText = "Delete";
			results.Columns[0].Width = 54;
			results.Columns[0].FillWeight = 7f;
			results.Columns[1].HeaderText = "Name";
			results.Columns[1].FillWeight = 25f;
			results.Columns[2].HeaderText = "Type";
			results.Columns[2].FillWeight = 13f;
			results.Columns[3].HeaderText = "Element ID";
			results.Columns[3].FillWeight = 9f;
			results.Columns[4].HeaderText = "Review note";
			results.Columns[4].FillWeight = 32f;
			DataGridViewColumn dataGridViewColumn = results.Columns.Cast<DataGridViewColumn>().FirstOrDefault((DataGridViewColumn c) => c.Name == "AdvancedCategory");
			if (dataGridViewColumn == null)
			{
				dataGridViewColumn = new DataGridViewTextBoxColumn
				{
					Name = "AdvancedCategory",
					HeaderText = "Category",
					ReadOnly = true,
					FillWeight = 14f
				};
				results.Columns.Add(dataGridViewColumn);
			}
			results.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
			results.Columns[0].HeaderText = "Delete";
			results.Columns[1].HeaderText = "Name";
			results.Columns[2].HeaderText = "Type";
			results.Columns[3].HeaderText = "Element ID";
			results.Columns[4].HeaderText = "Review note";
			results.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
			results.ColumnHeadersHeight = 32;
			results.RowTemplate.Height = 27;
			results.AllowUserToResizeRows = false;
			results.BorderStyle = BorderStyle.FixedSingle;
			results.BackgroundColor = Color.White;
			results.GridColor = Color.FromArgb(210, 215, 222);
			results.EnableHeadersVisualStyles = false;
			results.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 241, 245);
			results.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 67, 79);
			results.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
			results.DefaultCellStyle.SelectionBackColor = PaleSelected;
			results.DefaultCellStyle.SelectionForeColor = Color.FromArgb(78, 85, 92);
			results.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 248, 249);
			foreach (var item3 in list)
			{
				int index = results.Rows.Add(false, item3.Name, item3.Type, item3.Id, item3.Note, item3.Category);
				results.Rows[index].Tag = item3.Candidate;
			}
			main.Page.Text = "Advanced Search";
			main.Page.ToolTipText = "Search cleanup candidates across all categories";
			main.Page.Padding = Padding.Empty;
			main.Page.Controls.Remove(results);
			Panel filterBar = new Panel
			{
				Dock = DockStyle.Top,
				Height = 46,
				BackColor = Color.FromArgb(238, 241, 245),
				Padding = new Padding(8, 4, 8, 5)
			};
			Label value = new Label
			{
				Text = "Category",
				Left = 4,
				Top = 7,
				Width = 68,
				Height = 27,
				ForeColor = Color.FromArgb(78, 85, 92),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoSize = false
			};
			ComboBox category = new ComboBox
			{
				Left = 76,
				Top = 7,
				Width = 170,
				Height = 27,
				DropDownStyle = ComboBoxStyle.DropDownList,
				BackColor = PaleGold,
				ForeColor = Color.FromArgb(78, 85, 92),
				FlatStyle = FlatStyle.Flat
			};
			category.Items.Add("All categories");
			foreach (string item4 in from x in list.Select(x => x.Category).Distinct()
				orderby x
				select x)
			{
				category.Items.Add(item4);
			}
			category.SelectedIndex = 0;
			Label value2 = new Label
			{
				Text = "Type",
				Left = 258,
				Top = 7,
				Width = 38,
				Height = 27,
				ForeColor = Color.FromArgb(78, 85, 92),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoSize = false
			};
			ComboBox kind = new ComboBox
			{
				Left = 298,
				Top = 7,
				Width = 160,
				Height = 27,
				DropDownStyle = ComboBoxStyle.DropDownList,
				BackColor = PaleGold,
				ForeColor = Color.FromArgb(78, 85, 92),
				FlatStyle = FlatStyle.Flat
			};
			kind.Items.Add("All types");
			foreach (string item5 in from x in (from x in list
					select Convert.ToString(x.Type) into x
					where !string.IsNullOrWhiteSpace(x)
					select x).Distinct()
				orderby x
				select x)
			{
				kind.Items.Add(item5);
			}
			kind.SelectedIndex = 0;
			Panel searchBox = new Panel
			{
				Name = "BIMlifySearchOutline",
				Left = 470,
				Top = 6,
				Height = 30,
				Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
				BackColor = Color.White
			};
			Label value3 = new Label
			{
				Text = "⌕",
				Left = 7,
				Top = 1,
				Width = 20,
				Height = 26,
				ForeColor = Color.FromArgb(198, 151, 42),
				Font = new Font("Segoe UI", 14f),
				TextAlign = ContentAlignment.MiddleCenter
			};
			TextBox search = new TextBox
			{
				Name = "AdvancedSearchTextBox",
				Left = 30,
				Top = 3,
				Width = 480,
				Height = 22,
				Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
				BorderStyle = BorderStyle.None,
				BackColor = Color.White,
				ForeColor = Color.FromArgb(78, 85, 92),
				Font = new Font("Segoe UI", 9f)
			};
			search.AccessibleName = "Advanced search all fields";
			search.AccessibleDescription = "Search name, type, category, element ID and review note. Every word narrows the results.";
			search.HandleCreated += delegate
			{
				SendMessage(search.Handle, 5377, new IntPtr(1), "Search name, type, category, element ID, or review note");
			};
			searchBox.Controls.Add(value3);
			searchBox.Controls.Add(search);
			BIMlify.SearchControls.SearchGlyph.Attach(search, "Search name, type, category, ID, or note", false, true);
			searchBox.Paint += (object sender, PaintEventArgs e) =>
			{
				using Pen pen = new Pen(Color.FromArgb(178, 187, 197), 1.25f);
				e.Graphics.DrawRectangle(pen, 0, 0, searchBox.Width - 1, searchBox.Height - 1);
			};
			searchBox.Resize += delegate
			{
				searchBox.Invalidate();
			};
			search.Enter += delegate
			{
				searchBox.Invalidate();
			};
			search.Leave += delegate
			{
				searchBox.Invalidate();
			};
			filterBar.Resize += delegate
			{
				searchBox.Width = Math.Max(180, filterBar.ClientSize.Width - searchBox.Left - 8);
				search.Width = Math.Max(100, searchBox.ClientSize.Width - 38);
			};
			filterBar.Controls.Add(value);
			filterBar.Controls.Add(category);
			filterBar.Controls.Add(value2);
			filterBar.Controls.Add(kind);
			filterBar.Controls.Add(searchBox);
			results.Dock = DockStyle.Fill;
			results.ColumnHeadersVisible = true;
			results.ColumnHeadersHeight = 32;
			results.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
			results.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
			results.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
			foreach (DataGridViewColumn column in results.Columns)
			{
				column.Visible = true;
			}
			results.RowHeadersVisible = false;
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 2,
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				BackColor = Color.White
			};
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			filterBar.Dock = DockStyle.Fill;
			tableLayoutPanel.Controls.Add(filterBar, 0, 0);
			tableLayoutPanel.Controls.Add(results, 0, 1);
			main.Page.Controls.Add(tableLayoutPanel);
			Action applyFilter = () =>
			{
				if (results.CurrentCell != null)
				{
					results.CurrentCell = null;
				}
				results.ClearSelection();
				string[] source2 = search.Text.Split(new char[4] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
				string text = Convert.ToString(category.SelectedItem);
				string text2 = Convert.ToString(kind.SelectedItem);
				foreach (DataGridViewRow row in (IEnumerable)results.Rows)
				{
					string haystack = string.Join(" ", from i in Enumerable.Range(1, 5)
						select Convert.ToString(row.Cells[i].Value)).ToLowerInvariant();
					bool visible = (text == "All categories" || Convert.ToString(row.Cells[5].Value) == text) && (text2 == "All types" || Convert.ToString(row.Cells[2].Value) == text2) && source2.All((string term) => haystack.Contains(term.ToLowerInvariant()));
					row.Visible = visible;
				}
			};
			results.CurrentCellDirtyStateChanged += delegate
			{
				if (results.IsCurrentCellDirty)
				{
					results.CommitEdit(DataGridViewDataErrorContexts.Commit);
				}
			};
			search.TextChanged += delegate
			{
				applyFilter();
			};
			category.SelectedIndexChanged += delegate
			{
				applyFilter();
			};
			kind.SelectedIndexChanged += delegate
			{
				applyFilter();
			};
			foreach (TabPage item6 in source.Where((TabPage p) => p != main.Page))
			{
				item.TabPages.Remove(item6);
			}
			item.SelectedTab = main.Page;
			InsetTabFromLeft(item);
			item.Invalidate();
			applyFilter();
			ApplyGlobalTabStyle(form);
		}
	}

	public static void ApplyClash(Form form)
	{
		if (form == null)
		{
			return;
		}
		ApplySearchBars(form);
		form.BackColor = Color.FromArgb(238, 241, 245);
		Label label = FindLabel(form, "Set clash rules, choose the scan scope, then run the test");
		if (label != null)
		{
			label.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
			label.ForeColor = Color.FromArgb(55, 67, 79);
			label.BackColor = Color.Transparent;
			label.Margin = new Padding(0, 0, 0, 8);
		}
		foreach (Control item in All(form))
		{
			if ((item is TableLayoutPanel || item is FlowLayoutPanel) && (item.BackColor == Color.WhiteSmoke || item.BackColor == Color.FromArgb(238, 241, 245)))
			{
				item.BackColor = Color.FromArgb(238, 241, 245);
			}
			Label label2 = item as Label;
			if (label2 != null && (label2.Text == "Clash details" || label2.Text == "Detected clashes" || label2.Text == "SCAN SCOPE"))
			{
				label2.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
				label2.ForeColor = Color.FromArgb(55, 67, 79);
			}
			if (label2 != null && (label2.Text == "DISCIPLINE A" || label2.Text == "DISCIPLINE B"))
			{
				Control parent = label2.Parent;
				if (parent != null)
				{
					parent.BackColor = Color.FromArgb(238, 241, 245);
					foreach (Control control3 in parent.Controls)
					{
						if (control3 is Panel || control3 is FlowLayoutPanel || control3 is TableLayoutPanel)
						{
							control3.BackColor = Color.FromArgb(238, 241, 245);
						}
					}
					parent.Margin = new Padding(8, 6, 8, 6);
					if (parent is Panel)
					{
						((Panel)parent).Padding = new Padding(10, 8, 10, 8);
					}
					ComboBox comboBox = parent.Controls.OfType<ComboBox>().FirstOrDefault();
					if (comboBox != null && comboBox.Top < 48)
					{
						comboBox.Top = 48;
					}
					ApplyRoundedCardOutline(parent, 9);
				}
				if (parent != null && parent.Parent != null)
				{
					parent.Parent.BackColor = SoftCard;
				}
			}
			if (item.Text == "Review in Revit")
			{
				PropertyInfo property = item.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property != null && property.CanWrite)
				{
					property.SetValue(item, true, null);
				}
			}
			if (item is ComboBox comboBox2)
			{
				comboBox2.BackColor = PaleGold;
				comboBox2.ForeColor = Color.FromArgb(78, 85, 92);
				comboBox2.FlatStyle = FlatStyle.Flat;
				comboBox2.Invalidate();
			}
		}
		Label scopeTitle = FindLabel(form, "SCAN SCOPE");
		ComboBox scopeChoice = ((scopeTitle == null || scopeTitle.Parent == null) ? null : scopeTitle.Parent.Controls.OfType<ComboBox>().FirstOrDefault());
		Label label3 = FindLabel(form, "Set clash rules, choose the scan scope, then run the test");
		if (scopeTitle == null || label3 == null || scopeTitle.Parent == null || label3.Parent == null)
		{
			return;
		}
		Control host = scopeTitle.Parent.Parent;
		if (host == null || label3.Parent.Parent != host || All(host).OfType<Panel>().Any((Panel p) => p.Name == "BIMlifyClashResultsCard"))
		{
			return;
		}
		SplitContainer split = All(host).OfType<SplitContainer>().FirstOrDefault();
		Label detail = All(host).OfType<Label>().FirstOrDefault((Label l) => l.Text.StartsWith("Select a clash to see the reason", StringComparison.Ordinal));
		Label status = All(host).OfType<Label>().FirstOrDefault((Label l) => l != detail && l.Height == 34 && l.Dock == DockStyle.Bottom);
		Panel scanCard = scopeTitle.Parent as Panel;
		Panel rulesCard = label3.Parent as Panel;
		Label label4 = FindLabel(form, "Detected clashes");
		Label label5 = FindLabel(form, "Clash details");
		Label label6 = FindLabel(form, "DISCIPLINE A");
		Label label7 = FindLabel(form, "DISCIPLINE B");
		Button reviewAction = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Review in Revit", StringComparison.CurrentCultureIgnoreCase));
		Button runAction = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Run Clash Test", StringComparison.CurrentCultureIgnoreCase));
		Button button = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Close", StringComparison.CurrentCultureIgnoreCase));
		Button button2 = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Export Report", StringComparison.CurrentCultureIgnoreCase));
		Control control2 = button?.Parent;
		if (scanCard != null)
		{
			scanCard.Margin = Padding.Empty;
			scanCard.BackColor = Color.FromArgb(238, 241, 245);
		}
		if (rulesCard != null)
		{
			rulesCard.Margin = Padding.Empty;
		}
		if (scanCard != null)
		{
			ApplyRoundedCardOutline(scanCard, 10);
		}
		if (rulesCard != null)
		{
			ApplyRoundedCardOutline(rulesCard, 10);
		}
		if (control2 is Panel)
		{
			control2.Visible = true;
			control2.Dock = DockStyle.Right;
			control2.Width = 230;
			control2.Margin = Padding.Empty;
			control2.BackColor = ((control2.Parent == null) ? form.BackColor : control2.Parent.BackColor);
		}
		if (rulesCard != null && host != null)
		{
			foreach (Button item2 in new Button[2] { reviewAction, runAction }.Where((Button b) => b != null))
			{
				item2.Parent.Controls.Remove(item2);
				item2.Dock = DockStyle.None;
				item2.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
				host.Controls.Add(item2);
			}
		}
		host.BackColor = Color.White;
		if (split == null)
		{
			return;
		}
		split.BackColor = Color.White;
		split.Panel1.BackColor = Color.White;
		split.Panel2.BackColor = Color.White;
		ApplyRoundedCardOutline(split.Panel1, 8);
		ApplyRoundedCardOutline(split.Panel2, 8);
		Panel resultsCard = new Panel
		{
			Name = "BIMlifyClashResultsCard",
			BackColor = Color.White,
			Padding = new Padding(1)
		};
		Panel summaryCard = new Panel
		{
			Name = "BIMlifyClashSummaryCard",
			BackColor = Color.White,
			Padding = new Padding(8)
		};
		ApplyRoundedCardOutline(summaryCard, 8);
		// resultsCard is only a layout wrapper; outline each child card independently
		// to avoid a second frame joining the two result cards together.
		host.Controls.Remove(split);
		if (detail != null)
		{
			host.Controls.Remove(detail);
		}
		if (status != null)
		{
			host.Controls.Remove(status);
		}
		split.Dock = DockStyle.Fill;
		resultsCard.Controls.Add(split);
		resultsCard.Controls.Add(summaryCard);
		if (detail != null)
		{
			summaryCard.Controls.Add(detail);
		}
		if (status != null)
		{
			summaryCard.Controls.Add(status);
		}
		host.Controls.Add(resultsCard);
		resultsCard.SendToBack();
		split.Dock = DockStyle.None;
		if (detail != null)
		{
			detail.Dock = DockStyle.None;
		}
		if (status != null)
		{
			status.Dock = DockStyle.None;
		}
		if (label4 != null)
		{
			label4.Visible = true;
			label4.Height = 30;
			label4.Padding = new Padding(10, 0, 0, 0);
			label4.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
			label4.ForeColor = Color.FromArgb(55, 67, 79);
			label4.BackColor = Color.White;
		}
		if (label5 != null)
		{
			label5.Visible = true;
			label5.Height = 30;
			label5.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
			label5.ForeColor = Color.FromArgb(55, 67, 79);
			label5.BackColor = Color.White;
		}
		if (detail != null)
		{
			detail.Visible = true;
			detail.BackColor = Color.White;
			detail.ForeColor = Color.FromArgb(102, 109, 118);
			detail.Font = new Font("Segoe UI", 8.5f);
		}
		if (status != null)
		{
			status.Visible = true;
			status.BackColor = Color.White;
			status.ForeColor = Color.FromArgb(102, 109, 118);
			status.Font = new Font("Segoe UI", 8.5f);
		}
		Action layoutCards = () =>
		{
			int x = 20;
			int width = Math.Max(200, host.ClientSize.Width - 40);
			int num = 12;
			int num2 = 10;
			if (scanCard != null)
			{
				scanCard.Dock = DockStyle.None;
				scanCard.SetBounds(x, num, width, 48);
				int height = scanCard.ClientSize.Height;
				if (scopeTitle != null)
				{
					scopeTitle.Top = Math.Max(0, (height - scopeTitle.Height) / 2);
				}
				if (scopeChoice != null)
				{
					scopeChoice.Top = Math.Max(0, (height - scopeChoice.Height) / 2);
				}
			}
			num += 48 + num2;
			if (rulesCard != null)
			{
				rulesCard.Dock = DockStyle.None;
				rulesCard.SetBounds(x, num, width, 160);
			}
			if (rulesCard != null)
			{
				int y = Math.Max(0, rulesCard.Bottom - 36);
				int num3 = rulesCard.Right - 16;
				Button[] array = new Button[2] { runAction, reviewAction };
				foreach (Button button3 in array)
				{
					if (button3 != null)
					{
						int num4 = Math.Max(button3.Width, button3.PreferredSize.Width);
						button3.SetBounds(num3 - num4, y, num4, 30);
						num3 -= num4 + 10;
						button3.BringToFront();
					}
				}
			}
			num += 160 + num2;
			int height2 = Math.Max(150, host.ClientSize.Height - num - 12);
			resultsCard.SetBounds(x, num, width, height2);
			resultsCard.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			resultsCard.Padding = new Padding(1);
			int num5 = Math.Max(1, resultsCard.ClientSize.Width - 2);
			int num6 = Math.Max(1, resultsCard.ClientSize.Height - 2);
			int num7 = 78;
			summaryCard.SetBounds(8, num6 - num7 - 7, num5 - 16, num7);
			if (detail != null)
			{
				detail.SetBounds(10, 5, summaryCard.ClientSize.Width - 20, 29);
			}
			if (status != null)
			{
				status.SetBounds(10, 38, summaryCard.ClientSize.Width - 20, 29);
			}
			split.SetBounds(7, 7, num5 - 14, Math.Max(50, num6 - num7 - 21));
			split.SplitterWidth = 8;
			split.Panel1MinSize = 0;
			split.Panel2MinSize = 0;
			int num8 = split.ClientSize.Width - split.SplitterWidth;
			if (num8 > 2)
			{
				split.SplitterDistance = Math.Max(1, Math.Min(num8 - 1, num8 / 2));
			}
		};
		split.SizeChanged += delegate
		{
			if (split.IsDisposed)
			{
				return;
			}
			try
			{
				split.Panel1MinSize = 0;
				split.Panel2MinSize = 0;
				int num = split.ClientSize.Width - split.SplitterWidth;
				if (num > 2)
				{
					split.SplitterDistance = Math.Max(1, Math.Min(num - 1, num / 2));
				}
			}
			catch
			{
			}
		};
		host.Padding = Padding.Empty;
		host.Resize += delegate
		{
			layoutCards();
		};
		layoutCards();
	}

	private static void ApplyRoundedCardOutline(Control control, int radius)
	{
		ApplyRoundedCardOutline(control, radius, Color.FromArgb(205, 211, 217), 1f);
	}

	private static void ApplyRoundedCardOutline(Control control, int radius, Color edgeColor, float edgeWidth, int inset = 0)
	{
		if (control == null || control.IsDisposed || RoundedCardControls.TryGetValue(control, out var _))
		{
			return;
		}
		RoundedCardControls.Add(control, new object());
		control.Paint += (object sender, PaintEventArgs e) =>
		{
			if (!(sender is Control { Width: >=2, Height: >=2 } control2))
			{
				return;
			}
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = RoundedSearchPath(new Rectangle(inset, inset, control2.Width - 1 - 2 * inset, control2.Height - 1 - 2 * inset), radius);
			using Pen pen = new Pen(edgeColor, edgeWidth);
			e.Graphics.DrawPath(pen, path);
		};
		Action updateRegion = () =>
		{
			if (!control.IsDisposed && control.Width >= 2 && control.Height >= 2)
			{
				using (GraphicsPath path = RoundedSearchPath(new Rectangle(0, 0, control.Width, control.Height), radius))
				{
					Region region = control.Region;
					control.Region = new Region(path);
					region?.Dispose();
				}
				control.Invalidate();
			}
		};
		control.Resize += delegate
		{
			updateRegion();
		};
		control.HandleCreated += delegate
		{
			updateRegion();
		};
		if (control.IsHandleCreated)
		{
			updateRegion();
		}
	}

	public static void DrawSquareSearchFrame(TextBox search)
	{
		if (search == null || search.IsDisposed || search.ClientSize.Width < 4 || search.ClientSize.Height < 4)
		{
			return;
		}
		try
		{
			Region region = search.Region;
			search.Region = null;
			region?.Dispose();
			using Graphics graphics = Graphics.FromHwnd(search.Handle);
			using Pen pen = new Pen(Color.FromArgb(180, 188, 197), 1f);
			using Pen pen2 = new Pen(Color.FromArgb(198, 151, 42), 1f);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.DrawRectangle(pen, 0, 0, search.ClientSize.Width - 1, search.ClientSize.Height - 1);
			float num = (float)search.ClientSize.Height / 2f;
			graphics.DrawEllipse(pen2, 7f, num - 5f, 9f, 9f);
			graphics.DrawLine(pen2, 14f, num + 2f, 19f, num + 7f);
		}
		catch
		{
		}
	}

	public static void ApplySheetOrganizationStyle(Form form)
	{
		if (form == null || form.IsDisposed)
		{
			return;
		}
		DataGridView dataGridView = All(form).OfType<DataGridView>().FirstOrDefault((DataGridView g) => g.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => c.HeaderText == "Group Code") && g.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => c.HeaderText == "Preview Number") && g.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => c.HeaderText == "Sheet Number"));
		if (dataGridView != null)
		{
			dataGridView.BackgroundColor = Color.White;
			dataGridView.BorderStyle = BorderStyle.None;
			dataGridView.GridColor = Color.FromArgb(210, 215, 222);
			dataGridView.Font = new Font("Segoe UI", 9f);
			dataGridView.EnableHeadersVisualStyles = false;
			dataGridView.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 241, 245);
			dataGridView.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
			dataGridView.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
			dataGridView.ColumnHeadersHeight = 34;
			dataGridView.DefaultCellStyle.BackColor = Color.FromArgb(237, 240, 244);
			dataGridView.DefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
			dataGridView.DefaultCellStyle.SelectionBackColor = Color.FromArgb(252, 243, 218);
			dataGridView.DefaultCellStyle.SelectionForeColor = Color.FromArgb(78, 85, 92);
			dataGridView.DefaultCellStyle.Padding = new Padding(4);
			dataGridView.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(226, 230, 235);
			dataGridView.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
			dataGridView.RowTemplate.Height = 27;
		}
		TextBox textBox = All(form).OfType<TextBox>().FirstOrDefault((TextBox t) => (t.AccessibleName ?? string.Empty).IndexOf("Search sheet number", StringComparison.CurrentCultureIgnoreCase) >= 0);
		if (textBox != null)
		{
			textBox.BorderStyle = BorderStyle.None;
			textBox.BackColor = Color.White;
			textBox.Region = null;
			textBox.Invalidate();
		}
	}

	public static void ApplyDashboard(Form form)
	{
		if (form == null)
		{
			return;
		}
		ApplyGlobalTabStyle(form);
		ApplySearchBars(form);
		Action hideScaleTags = () =>
		{
			foreach (Control item in All(form).ToList())
			{
				string a = (item.Text ?? string.Empty).Trim();
				if (string.Equals(a, "Scale Tags", StringComparison.CurrentCultureIgnoreCase) || string.Equals(a, "Scale Annotations", StringComparison.CurrentCultureIgnoreCase))
				{
					Control parent = item.Parent;
					for (int i = 0; i < 3; i++)
					{
						if (parent == null)
						{
							break;
						}
						bool flag = parent.Controls.OfType<PictureBox>().Any();
						if ((parent is Panel) & flag)
						{
							parent.Visible = false;
							break;
						}
						parent = parent.Parent;
					}
				}
			}
		};
		hideScaleTags();
		EventHandler hideAgain = null;
		hideAgain = delegate
		{
			form.Shown -= hideAgain;
			if (!form.IsDisposed)
			{
				hideScaleTags();
			}
		};
		form.Shown += hideAgain;
		foreach (PictureBox icon in (from p in All(form).OfType<PictureBox>()
			where p.Image != null && p.Width <= 80 && p.Height <= 80
			select p).ToList())
		{
			Panel tile = icon.Parent as Panel;
			if (tile == null || ShadowedDashboardIcons.TryGetValue(icon, out var _))
			{
				continue;
			}
			ShadowedDashboardIcons.Add(icon, new object());
			tile.Paint += (object sender, PaintEventArgs e) =>
			{
				try
				{
					if (icon.IsDisposed || !icon.Visible || icon.Parent != tile)
					{
						return;
					}
					e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
					float num = Math.Max(18f, (float)icon.Width * 0.66f);
					float num2 = (float)icon.Left + ((float)icon.Width - num) / 2f;
					float num3 = (float)icon.Bottom + 1f;
					using (SolidBrush brush = new SolidBrush(Color.FromArgb(13, 62, 69, 77)))
					{
						e.Graphics.FillEllipse(brush, num2 - 2f, num3, num + 4f, 6f);
					}
					using SolidBrush brush2 = new SolidBrush(Color.FromArgb(21, 62, 69, 77));
					e.Graphics.FillEllipse(brush2, num2 + 2f, num3 + 1f, Math.Max(12f, num - 4f), 3f);
				}
				catch
				{
				}
			};
		}
	}

	private static Label FindLabel(Control root, string text)
	{
		return All(root).OfType<Label>().FirstOrDefault((Label x) => x.Text == text);
	}

	private static GraphicsPath RoundedSearchPath(Rectangle bounds, int radius)
	{
		int num = Math.Max(2, radius * 2);
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(bounds.Left, bounds.Top, num, num, 180f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Top, num, num, 270f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(bounds.Left, bounds.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private static void ApplySearchBars(Form form)
	{
		foreach (TextBox item in All(form).OfType<TextBox>())
		{
			string text = ((item.Name ?? string.Empty) + " " + (item.AccessibleName ?? string.Empty) + " " + (item.AccessibleDescription ?? string.Empty) + " " + (item.Text ?? string.Empty)).ToLowerInvariant();
			if (text.Contains("search"))
			{
				item.BackColor = Color.White;
				item.ForeColor = Color.FromArgb(78, 85, 92);
				item.Font = new Font("Segoe UI", 9f);
				bool flag = item.Parent != null && string.Equals(item.Parent.Name, "BIMlifySearchOutline", StringComparison.Ordinal);
				bool flag2 = string.Equals(item.GetType().Name, "AdvancedSearchTextBox", StringComparison.Ordinal);
				item.BorderStyle = ((!(flag | flag2)) ? BorderStyle.FixedSingle : BorderStyle.None);
				if (flag2)
				{
					item.Region = null;
				}
			}
		}
	}

	public static void ApplyReviewCleanInset(Form form, TabControl tabs)
	{
		if (form == null || tabs == null || tabs.Parent != form)
		{
			return;
		}
		Panel header = All(form).OfType<Panel>().FirstOrDefault((Panel p) => p.Dock == DockStyle.Top && All(p).OfType<Label>().Any((Label l) => string.Equals((l.Text ?? string.Empty).Trim(), "Review & Clean", StringComparison.OrdinalIgnoreCase)));
		Panel footer = All(form).OfType<Panel>().FirstOrDefault((Panel p) => p.Dock == DockStyle.Bottom && All(p).OfType<Button>().Any((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Close", StringComparison.OrdinalIgnoreCase)));
		Label candidateCount = All(form).OfType<Label>().FirstOrDefault((Label l) => (l.Text ?? string.Empty).StartsWith("Candidates:", StringComparison.OrdinalIgnoreCase));
		if (candidateCount != null)
		{
			candidateCount.Font = new Font("Segoe UI", 8.2f);
			candidateCount.TextAlign = ContentAlignment.MiddleLeft;
			Action centerCandidateCount = () =>
			{
				if (candidateCount.IsDisposed || candidateCount.Parent == null) return;
				candidateCount.Top = Math.Max(0, (candidateCount.Parent.ClientSize.Height - candidateCount.Height) / 2);
			};
			candidateCount.Parent.Resize += (object sender, EventArgs e) => centerCandidateCount();
			centerCandidateCount();
		}
		EnsureReviewCleanLocateButton(form, tabs, footer);
		if (header != null)
		{
			Label heading = All(header).OfType<Label>().FirstOrDefault((Label l) => string.Equals((l.Text ?? string.Empty).Trim(), "Review & Clean", StringComparison.OrdinalIgnoreCase));
			Label description = All(header).OfType<Label>().FirstOrDefault((Label l) => (l.Text ?? string.Empty).StartsWith("Ctrl+A selects all", StringComparison.OrdinalIgnoreCase));
			if (heading != null)
			{
				heading.Top = 60;
				heading.BringToFront();
			}
			if (description != null)
			{
				description.AutoSize = false;
				description.AutoEllipsis = false;
				description.Width = Math.Min(780, Math.Max(1, header.ClientSize.Width - 175));
				description.Height = 22;
				description.Top = 88;
				description.BringToFront();
			}
			form.Paint += delegate(object sender, PaintEventArgs e)
			{
				int edgeX = form.ClientSize.Width - 2;
				int edgeTop = header.Bottom + 1;
				int edgeBottom = form.ClientSize.Height - 14;
				if (edgeX >= 0 && edgeBottom > edgeTop)
				{
					using (SolidBrush brush = new SolidBrush(form.BackColor))
					{
						e.Graphics.FillRectangle(brush, edgeX, edgeTop, 2, edgeBottom - edgeTop);
					}
				}
			};
		}
		Control rail = form.Controls.Cast<Control>().FirstOrDefault((Control c) => c.Dock == DockStyle.Left && c.Width <= 72);
		tabs.Dock = DockStyle.None;
		tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
		Action layout = () =>
		{
			if (!form.IsDisposed && !tabs.IsDisposed && tabs.Parent == form)
			{
				int num = ((rail != null) ? rail.Right : 0) + 14;
				int num2 = ((header != null) ? header.Bottom : 0) + 12;
				int num3 = 14;
				int num4 = 5;
				int num5 = ((footer == null) ? form.ClientSize.Height : footer.Top);
				tabs.SetBounds(num, num2, Math.Max(160, form.ClientSize.Width - num - num3), Math.Max(120, num5 - num2 - num4));
			}
		};
		form.Resize += delegate
		{
			layout();
		};
		form.Shown += delegate
		{
			layout();
		};
		layout();
	}

	private static void EnsureReviewCleanLocateButton(Form form, TabControl tabs, Panel footer)
	{
		if (form == null || tabs == null || footer == null || ReviewCleanLocateButtons.TryGetValue(form, out var _)) return;
		Button delete = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Delete", StringComparison.OrdinalIgnoreCase));
		Button close = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Close", StringComparison.OrdinalIgnoreCase));
		if (delete == null || close == null || delete.Parent == null || delete.Parent != close.Parent) return;
		Button locate;
		try { locate = Activator.CreateInstance(delete.GetType(), nonPublic: true) as Button; }
		catch { locate = null; }
		if (locate == null) return;
		ReviewCleanLocateButtons.Add(form, new object());
		locate.Text = "Locate Element";
		locate.Width = Math.Max(delete.Width, TextRenderer.MeasureText(locate.Text, locate.Font).Width + 24);
		locate.Height = delete.Height;
		locate.Enabled = false;
		PropertyInfo primary = locate.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (primary != null && primary.CanWrite) primary.SetValue(locate, false, null);
		Control group = delete.Parent;
		group.Controls.Add(locate);
		Action layout = () =>
		{
			if (form.IsDisposed || group.IsDisposed) return;
			int gap = 8;
			int width = locate.Width + delete.Width + close.Width + gap * 2 + 16;
			if (group is Panel && group.Dock == DockStyle.Right) group.Width = Math.Max(group.Width, width);
			int left = Math.Max(8, group.ClientSize.Width - width + 8);
			foreach (Button button in new[] { locate, delete, close })
			{
				button.Dock = DockStyle.None;
				button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
				button.Left = left;
				button.Top = close.Top + (close.Height - button.Height) / 2;
				left += button.Width + gap;
			}
		};
		Func<List<ElementId>> getLocatedIds = () =>
		{
			List<ElementId> result = new List<ElementId>();
			DataGridView grid = All(tabs.SelectedTab).OfType<DataGridView>().FirstOrDefault();
			if (grid == null || grid.Columns.Count == 0) return result;
			foreach (DataGridViewRow row in grid.Rows)
			{
				if (row.IsNewRow || row.Cells[0].Value == null || !Convert.ToBoolean(row.Cells[0].Value)) continue;
				object tag = row.Tag;
				FieldInfo idField = tag == null ? null : tag.GetType().GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (idField?.GetValue(tag) is ElementId id) result.Add(id);
			}
			return result.Distinct().ToList();
		};
		Action update = () => locate.Enabled = getLocatedIds().Count > 0;
		foreach (DataGridView grid in All(tabs).OfType<DataGridView>())
		{
			grid.CellValueChanged += (object sender, DataGridViewCellEventArgs e) => update();
			grid.CurrentCellDirtyStateChanged += (object sender, EventArgs e) =>
			{
				if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
				update();
			};
		}
		tabs.SelectedIndexChanged += (object sender, EventArgs e) => update();
		locate.Click += (object sender, EventArgs e) =>
		{
			try
			{
				Type actions = AppDomain.CurrentDomain.GetAssemblies().Select((Assembly a) => a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
				FieldInfo appField = actions?.GetField("dashboardApplication", BindingFlags.Static | BindingFlags.NonPublic);
				UIApplication application = appField?.GetValue(null) as UIApplication;
				UIDocument ui = application?.ActiveUIDocument;
				Document doc = ui?.Document;
				if (doc == null) { TaskDialog.Show("Locate Element", "Open this window from the BIMlify main interface to locate model elements."); return; }
				List<ElementId> visibleIds = new List<ElementId>();
				foreach (ElementId id in getLocatedIds())
				{
					Element candidate = doc.GetElement(id);
					if (candidate == null) continue;
					if (candidate is ElementType)
					{
						foreach (Element instance in new FilteredElementCollector(doc).WhereElementIsNotElementType())
							try { if (instance.GetTypeId() == id) visibleIds.Add(instance.Id); } catch { }
					}
					else visibleIds.Add(id);
				}
				visibleIds = visibleIds.Distinct().ToList();
				if (visibleIds.Count == 0) { TaskDialog.Show("Locate Element", "No placed model instances were found for the checked candidates. Some unused types have no location in the model."); return; }
				ui.Selection.SetElementIds((ICollection<ElementId>)visibleIds);
				try { ui.ShowElements((ICollection<ElementId>)visibleIds); } catch { }
			}
			catch (Exception ex) { TaskDialog.Show("Locate Element", "Revit could not locate the checked elements.\n\n" + ex.Message); }
		};
		group.Resize += (object sender, EventArgs e) => layout();
		form.Shown += (object sender, EventArgs e) => { if (!form.IsDisposed) { layout(); update(); } };
		layout();
		update();
	}

	private static void RightAlignSmartCleanerFooter(Form form)
	{
		Button close = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Close", StringComparison.CurrentCultureIgnoreCase));
		Button locate = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Locate Elements", StringComparison.CurrentCultureIgnoreCase));
		if (close == null || locate == null)
		{
			return;
		}
		Control footer = close.Parent;
		while (footer != null && !ContainsControl(footer, locate))
		{
			footer = footer.Parent;
		}
		if (footer == null || AlignedSmartCleanerFooters.TryGetValue(footer, out var _))
		{
			return;
		}
		AlignedSmartCleanerFooters.Add(footer, new object());
		Action layout = () =>
		{
			Button deleteButton = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Delete", StringComparison.CurrentCultureIgnoreCase));
			Button[] array = new Button[3]
			{
				locate,
				deleteButton ?? All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals((b.Text ?? string.Empty).Trim(), "Purge", StringComparison.CurrentCultureIgnoreCase)),
				close
			}.Where((Button b) => b != null && b.Visible && b.Parent == footer).ToArray();
			int num = 10;
			int num2 = array.Sum((Button b) => b.Width) + Math.Max(0, array.Length - 1) * num;
			Point point = footer.PointToClient(close.Parent.PointToScreen(close.Location));
			int num3 = Math.Max(8, footer.ClientSize.Width - num2 - 10);
			Button[] array2 = array;
			foreach (Button button in array2)
			{
				button.Dock = DockStyle.None;
				button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
				button.Left = num3;
				button.Top = Math.Max(0, point.Y + (close.Height - button.Height) / 2);
				num3 += button.Width + num;
			}
		};
		footer.Resize += delegate
		{
			layout();
		};
		form.Shown += delegate
		{
			if (!form.IsDisposed)
			{
				layout();
			}
		};
		layout();
	}

	private static bool ContainsControl(Control parent, Control child)
	{
		for (Control control = child; control != null; control = control.Parent)
		{
			if (control == parent)
			{
				return true;
			}
		}
		return false;
	}

	private static Label FindChildLabel(Control root, string text)
	{
		return root.Controls.OfType<Label>().FirstOrDefault((Label x) => x.Text == text);
	}

	private static void StyleHeading(Label label)
	{
		if (label != null)
		{
			label.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
			label.ForeColor = Color.FromArgb(78, 85, 92);
		}
	}

	private static void SetWidth(Control parent, Control child, int width)
	{
		if (child != null)
		{
			child.Width = Math.Min(width, Math.Max(80, parent.ClientSize.Width - child.Left - parent.Padding.Right));
		}
	}

	private static IEnumerable<Control> All(Control root)
	{
		foreach (Control c in root.Controls)
		{
			yield return c;
			foreach (Control item in All(c))
			{
				yield return item;
			}
		}
	}
}












