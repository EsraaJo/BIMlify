using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ComboBox = System.Windows.Forms.ComboBox;
using Color = System.Drawing.Color;
using Control = System.Windows.Forms.Control;
using Form = System.Windows.Forms.Form;
using Panel = System.Windows.Forms.Panel;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using TextBox = System.Windows.Forms.TextBox;

namespace BIMlify.CleanerUi;

public static class EmptyState
{
	private sealed class State
	{
		public Panel Overlay;

		public Label Message;

		public Button Run;

		public DataGridView Grid;

		public object Owner;

		public string GridField;

		public bool InitialSelectionNormalized;
	}

	private sealed class CleanerExternalHandler : IExternalEventHandler
	{
		internal Form Form;

		internal string MethodName;

		internal readonly object Gate = new object();

		public string GetName()
		{
			return "BIMlify Smart Cleaner Actions";
		}

		public void Execute(UIApplication application)
		{
			string methodName;
			lock (Gate)
			{
				methodName = MethodName;
				MethodName = null;
			}
			if (Form == null || Form.IsDisposed || string.IsNullOrEmpty(methodName))
			{
				return;
			}
			try
			{
				if (string.Equals(methodName, "DeleteSelectedUnused", StringComparison.Ordinal))
				{
					DeleteSelectedUnused(Form, application);
				}
				else
				{
					Form.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(Form, null);
				}
			}
			catch (TargetInvocationException ex)
			{
				MessageBox.Show(Form, ex.InnerException?.Message ?? ex.Message, "Smart Cleaner", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			catch (Exception ex2)
			{
				MessageBox.Show(Form, ex2.Message, "Smart Cleaner", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
	}

	private sealed class CleanerExternalState
	{
		internal CleanerExternalHandler Handler;

		internal ExternalEvent Event;
	}

	private sealed class CueState
	{
		public string Text;

		public bool Subscribed;
	}

	private static readonly List<State> states = new List<State>();

	private static readonly ConditionalWeakTable<TextBox, CueState> cueStates = new ConditionalWeakTable<TextBox, CueState>();

	private static readonly ConditionalWeakTable<ComboBox, object> goldFilterStates = new ConditionalWeakTable<ComboBox, object>();

	private static readonly ConditionalWeakTable<Form, object> categoryQuickLists = new ConditionalWeakTable<Form, object>();

	private static readonly ConditionalWeakTable<Form, object> cleanerWatchers = new ConditionalWeakTable<Form, object>();

	private static readonly ConditionalWeakTable<Form, Button> purgeDeleteButtons = new ConditionalWeakTable<Form, Button>();

	private static readonly ConditionalWeakTable<Form, CleanerExternalState> cleanerExternalEvents = new ConditionalWeakTable<Form, CleanerExternalState>();

	private static readonly ConditionalWeakTable<TextBox, object> insetSearchBoxes = new ConditionalWeakTable<TextBox, object>();

	private static readonly ConditionalWeakTable<Panel, object> footerCentering = new ConditionalWeakTable<Panel, object>();

	private static readonly ConditionalWeakTable<Form, object> sidebarHooks = new ConditionalWeakTable<Form, object>();

	private static readonly ConditionalWeakTable<Panel, object> patternBodyLayouts = new ConditionalWeakTable<Panel, object>();

	private static readonly ConditionalWeakTable<Panel, object> patternCardStyles = new ConditionalWeakTable<Panel, object>();

	private static readonly ConditionalWeakTable<Panel, object> patternPreviewBorders = new ConditionalWeakTable<Panel, object>();

	
	private static readonly ConditionalWeakTable<Form, object> planRangeFooterHooks = new ConditionalWeakTable<Form, object>();

	private static readonly ConditionalWeakTable<DataGridView, object> filledPickBoxHandlers = new ConditionalWeakTable<DataGridView, object>();

	private static readonly ConditionalWeakTable<DataGridView, object> emptyPurgeRowHandlers = new ConditionalWeakTable<DataGridView, object>();

	private static readonly ConditionalWeakTable<TabControl, object> themedTabStates = new ConditionalWeakTable<TabControl, object>();

	private static readonly ConditionalWeakTable<Button, object> patternButtonOffsets = new ConditionalWeakTable<Button, object>();

	private static TabControl themedTabs;

	public static bool IncludeLoadedLinks { get; private set; }

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);

	[DllImport("user32.dll", EntryPoint = "SendMessageW")]
	private static extern IntPtr SendSearchMargin(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

	public static void ApplySearchTextInset(TextBox box)
	{
		try
		{
			if (box == null || insetSearchBoxes.TryGetValue(box, out var _))
			{
				return;
			}
			insetSearchBoxes.Add(box, new object());
			string accessibleName = box.AccessibleName;
			if (accessibleName?.StartsWith("Search", StringComparison.OrdinalIgnoreCase) == true ||
				string.Equals(accessibleName, "Advanced search", StringComparison.OrdinalIgnoreCase))
			{
				BIMlify.SearchControls.SearchGlyph.Attach(box, accessibleName, box.GetType().Name != "AdvancedSearchTextBox");
				return;
			}
			string paddedCue = (string.IsNullOrWhiteSpace(accessibleName) ? null : (new string('\u00a0', 9) + accessibleName));
			Action apply = () =>
			{
				if (box.IsHandleCreated)
				{
					SendSearchMargin(box.Handle, 211, new IntPtr(1), new IntPtr(786474));
					if (paddedCue != null)
					{
						SendMessage(box.Handle, 5377, new IntPtr(1), paddedCue);
					}
				}
			};
			box.HandleCreated += (object s, EventArgs e) =>
			{
				apply();
			};
			if (box.IsHandleCreated)
			{
				apply();
			}
		}
		catch
		{
		}
	}

	private static void SetCue(TextBox box, string cue)
	{
		if (box == null)
		{
			return;
		}
		CueState state = cueStates.GetValue(box, (TextBox key) => new CueState
		{
			Text = cue
		});
		state.Text = cue;
		if (!state.Subscribed)
		{
			state.Subscribed = true;
			box.HandleCreated += (object s, EventArgs e) =>
			{
				if (box.IsHandleCreated)
				{
					SendMessage(box.Handle, 5377, new IntPtr(1), state.Text);
				}
			};
		}
		if (box.IsHandleCreated)
		{
			SendMessage(box.Handle, 5377, new IntPtr(1), state.Text);
		}
	}

	public static void NormalizeWindowFooter(Form form)
	{
		try
		{
			if (form == null || form.IsDisposed)
			{
				return;
			}
			Color color = Color.FromArgb(216, 221, 227);
			foreach (Control control in form.Controls)
			{
				Panel panel = control as Panel;
				if (panel == null || panel.Dock != DockStyle.Bottom || panel.Height < 24 || panel.Height > 80 || !(panel.BackColor == color))
				{
					continue;
				}
				panel.Height = 64;
				panel.BackColor = color;
				CenterFooterButtons(panel);
				foreach (Button item in from b in All(panel).OfType<Button>()
					where string.Equals(b.Text?.Trim(), "Close", StringComparison.OrdinalIgnoreCase)
					select b)
				{
					StyleBimlifyCloseButton(item);
				}
				if (!footerCentering.TryGetValue(panel, out var _))
				{
					footerCentering.Add(panel, new object());
					panel.SizeChanged += (object s, EventArgs e) =>
					{
						CenterFooterButtons(panel);
					};
				}
			}
		}
		catch
		{
		}
	}

	public static void NormalizePlanRangeFooter(Form form)
	{
		try
		{
			if (form == null || form.IsDisposed)
			{
				return;
			}
			Action layout = () =>
			{
				if (!form.IsDisposed)
				{
					NormalizePlanRangeCards(form);
					foreach (Label item in from l in All(form).OfType<Label>()
						where l.Text.IndexOf("offset", StringComparison.OrdinalIgnoreCase) >= 0 || l.Text.IndexOf("elevation", StringComparison.OrdinalIgnoreCase) >= 0
						select l)
					{
						if (Math.Abs(item.Font.SizeInPoints - 8f) > 0.01f)
						{
							item.Font = new Font(item.Font.FontFamily, 8f, item.Font.Style, GraphicsUnit.Point);
						}
						if (item.Parent is TableLayoutPanel tableLayoutPanel && tableLayoutPanel.ColumnStyles.Count >= 2 && tableLayoutPanel.ColumnStyles[0].SizeType == SizeType.Percent)
						{
							if (Math.Abs(tableLayoutPanel.ColumnStyles[0].Width - 37f) > 0.01f) tableLayoutPanel.ColumnStyles[0].Width = 37f;
							if (Math.Abs(tableLayoutPanel.ColumnStyles[1].Width - 63f) > 0.01f) tableLayoutPanel.ColumnStyles[1].Width = 63f;
						}
					}
					Button button = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals(b.Text?.Trim(), "Close", StringComparison.OrdinalIgnoreCase));
					if (button != null && !button.IsDisposed && button.Parent != null)
					{
						Control parent = button.Parent;
						button.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
						button.AutoSize = false;
						if (button.Size != new Size(96, 30))
					{
						button.Size = new Size(96, 30);
					}
						button.Margin = new Padding(0);
						button.Left = Math.Max(8, parent.ClientSize.Width - 24 - button.Width);
						button.Top = Math.Max(0, (parent.ClientSize.Height - button.Height) / 2);
						StyleBimlifyCloseButton(button);
					}
				}
			};
			layout();
			if (planRangeFooterHooks.TryGetValue(form, out var _))
			{
				return;
			}
			planRangeFooterHooks.Add(form, new object());
			EventHandler value2 = (object s, EventArgs e) =>
			{
				if (form.IsDisposed || !form.IsHandleCreated)
				{
					return;
				}
				try
				{
					form.BeginInvoke((MethodInvoker)(() =>
					{
						layout();
					}));
				}
				catch
				{
				}
			};
			form.Shown += value2;
			form.SizeChanged += value2;

		}
		catch
		{
		}
	}

	private static void StyleBimlifyCloseButton(Button button)
	{
		try
		{
			if (button != null && !button.IsDisposed)
			{
				button.AutoSize = false;
				button.Size = new Size(96, 30);
				button.Font = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
				button.ForeColor = Color.FromArgb(65, 72, 79);
				button.BackColor = Color.FromArgb(216, 221, 227);
				button.UseVisualStyleBackColor = false;
				button.FlatStyle = FlatStyle.Flat;
				button.FlatAppearance.BorderSize = 1;
				button.FlatAppearance.BorderColor = Color.FromArgb(132, 140, 147);
				button.FlatAppearance.MouseOverBackColor = Color.FromArgb(216, 221, 227);
				button.FlatAppearance.MouseDownBackColor = Color.FromArgb(207, 212, 218);
			}
		}
		catch
		{
		}
	}

	private static void NormalizePlanRangeCards(Form form)
	{
		try
		{
			List<Control> source = All(form).ToList();
			Label label = source.OfType<Label>().FirstOrDefault((Label c) => c.Text.IndexOf("VIEW RANGE DATA", StringComparison.OrdinalIgnoreCase) >= 0);
			Label previewTitle = source.OfType<Label>().FirstOrDefault((Label c) => c.Text.IndexOf("3D VIEW RANGE", StringComparison.OrdinalIgnoreCase) >= 0);
			if (label == null || previewTitle == null)
			{
				return;
			}
			List<Control> list = new List<Control>();
			for (Control parent = label.Parent; parent != null; parent = parent.Parent)
			{
				list.Add(parent);
			}
			Control control = list.FirstOrDefault((Control a) => previewTitle.Parent != null && IsDescendantOf(previewTitle, a));
			if (control == null || control.ClientSize.Width < 100)
			{
				return;
			}
			Control control2 = DirectChildContaining(control, label);
			Control control3 = DirectChildContaining(control, previewTitle);
			if (control2 == null || control3 == null || control2 == control3)
			{
				return;
			}
			if (control is TableLayoutPanel tableLayoutPanel)
			{
				int column = tableLayoutPanel.GetColumn(control2);
				int column2 = tableLayoutPanel.GetColumn(control3);
				if (column >= 0 && column2 >= 0 && column < tableLayoutPanel.ColumnStyles.Count && column2 < tableLayoutPanel.ColumnStyles.Count)
				{
					if (tableLayoutPanel.ColumnStyles[column].SizeType != SizeType.Percent) tableLayoutPanel.ColumnStyles[column].SizeType = SizeType.Percent;
					if (tableLayoutPanel.ColumnStyles[column2].SizeType != SizeType.Percent) tableLayoutPanel.ColumnStyles[column2].SizeType = SizeType.Percent;
					if (Math.Abs(tableLayoutPanel.ColumnStyles[column].Width - 50f) > 0.01f) tableLayoutPanel.ColumnStyles[column].Width = 50f;
					if (Math.Abs(tableLayoutPanel.ColumnStyles[column2].Width - 50f) > 0.01f) tableLayoutPanel.ColumnStyles[column2].Width = 50f;
					return;
				}
			}
			int num = Math.Max(control2.Margin.Right, control3.Margin.Left);
			int width = (control2.Width = Math.Max(100, (control.ClientSize.Width - num) / 2));
			control3.Width = width;
			control2.Left = 0;
			control3.Left = control2.Right + num;
		}
		catch
		{
		}
	}

	private static Control DirectChildContaining(Control parent, Control descendant)
	{
		if (parent == null || descendant == null)
		{
			return null;
		}
		Control control = descendant;
		while (control.Parent != null && control.Parent != parent)
		{
			control = control.Parent;
		}
		return (control.Parent == parent) ? control : null;
	}

	private static bool IsDescendantOf(Control control, Control ancestor)
	{
		for (Control control2 = control; control2 != null; control2 = control2.Parent)
		{
			if (control2 == ancestor)
			{
				return true;
			}
		}
		return false;
	}

	public static void NormalizeSharedSidebar(Form form)
	{
		try
		{
			if (form == null || form.IsDisposed)
			{
				return;
			}
			bool normalizing = false;
			Action normalize = () =>
			{
				if (!(form.IsDisposed | normalizing))
				{
					Control control = All(form).FirstOrDefault((Control c) => string.Equals(c.GetType().Name, "BIMlifySidebar", StringComparison.Ordinal));
					if (control != null)
					{
						normalizing = true;
						try
						{
							if (form.Padding != Padding.Empty)
							{
								form.Padding = Padding.Empty;
							}
							if (control.Margin != Padding.Empty)
							{
								control.Margin = Padding.Empty;
							}
							if (control.Dock != DockStyle.Left)
							{
								control.Dock = DockStyle.Left;
							}
							if (control.MinimumSize.Width != 58)
							{
								control.MinimumSize = new Size(58, 0);
							}
							if (control.MaximumSize.Width != 58)
							{
								control.MaximumSize = new Size(58, 0);
							}
							if (control.Width != 58)
							{
								control.Width = 58;
							}
							form.PerformLayout();
						}
						finally
						{
							normalizing = false;
						}
					}
				}
			};
			normalize();
			if (sidebarHooks.TryGetValue(form, out var _))
			{
				return;
			}
			sidebarHooks.Add(form, new object());
			EventHandler defer = (object s, EventArgs e) =>
			{
				if (form.IsDisposed || !form.IsHandleCreated)
				{
					return;
				}
				try
				{
					form.BeginInvoke((MethodInvoker)(() =>
					{
						normalize();
					}));
				}
				catch
				{
				}
			};
			form.DpiChanged += (object s, DpiChangedEventArgs e) =>
			{
				defer(s, e);
			};
			form.Layout += (object s, LayoutEventArgs e) =>
			{
				normalize();
			};
			form.SizeChanged += defer;
			form.Shown += defer;
		}
		catch
		{
		}
	}

	public static void RestoreReviewCleanWindow(Form form)
	{
		try
		{
			if (form == null || form.IsDisposed || !form.Text.EndsWith("Review & Clean", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			EventHandler apply = null;
			apply = (object s, EventArgs e) =>
			{
				form.Shown -= apply;
				Action restore = () =>
				{
					if (!form.IsDisposed)
					{
						Rectangle workingArea = Screen.FromControl(form).WorkingArea;
						double num = (double)((form.DeviceDpi > 0) ? form.DeviceDpi : 96) / 96.0;
						int num2 = Math.Min((int)Math.Round(1080.0 * num), workingArea.Width);
						int num3 = Math.Min((int)Math.Round(720.0 * num), workingArea.Height);
						form.MinimumSize = new Size(Math.Min((int)Math.Round(980.0 * num), workingArea.Width), Math.Min((int)Math.Round(640.0 * num), workingArea.Height));
						form.WindowState = FormWindowState.Normal;
						form.Size = new Size(num2, num3);
						form.Location = new Point(workingArea.Left + (workingArea.Width - num2) / 2, workingArea.Top + (workingArea.Height - num3) / 2);
						NormalizeSharedSidebar(form);
						form.PerformLayout();
						FitReviewCleanGridHeaders(form);
					}
				};
				if (form.IsHandleCreated)
				{
					form.BeginInvoke((MethodInvoker)(() =>
					{
						restore();
					}));
				}
				else
				{
					restore();
				}
			};
			form.Shown += apply;
		}
		catch
		{
		}
	}

	private static void CenterFooterButtons(Panel footer)
	{
		try
		{
			if (footer == null || footer.IsDisposed)
			{
				return;
			}
			foreach (Button item in All(footer).OfType<Button>())
			{
				if (item.Parent != null && !item.IsDisposed)
				{
					int y = footer.PointToClient(item.Parent.PointToScreen(item.Location)).Y;
					int num = (footer.ClientSize.Height - item.Height) / 2;
					item.Top += num - y;
				}
			}
		}
		catch
		{
		}
	}

	public static void Attach(TabPage page, object owner)
	{
		AttachGrid(page, owner, "_purgeGrid");
	}

	public static void AttachWarnings(TabPage page, object owner)
	{
		AttachGrid(page, owner, "_warningsGrid");
	}

	public static void WatchCleanerForm(Form form)
	{
		try
		{
			if (form == null || cleanerWatchers.TryGetValue(form, out var _))
			{
				return;
			}
			cleanerWatchers.Add(form, new object());
			CleanerExternalHandler cleanerExternalHandler = new CleanerExternalHandler
			{
				Form = form
			};
			cleanerExternalEvents.Add(form, new CleanerExternalState
			{
				Handler = cleanerExternalHandler,
				Event = ExternalEvent.Create((IExternalEventHandler)(object)cleanerExternalHandler)
			});
			form.FormClosed += (object s, FormClosedEventArgs e) =>
			{
				try
				{
					if (cleanerExternalEvents.TryGetValue(form, out var value2) && value2 != null)
					{
						ExternalEvent val = value2.Event;
						if (val != null)
						{
							val.Dispose();
						}
					}
					cleanerExternalEvents.Remove(form);
				}
				catch
				{
				}
			};
			form.Shown += (object s, EventArgs e) =>
			{
				try
				{
					NormalizeWindowFooter(form);
					DataGridView dataGridView = Get<DataGridView>(form, "_purgeGrid");
					DataGridView dataGridView2 = Get<DataGridView>(form, "_warningsGrid");
					if (dataGridView != null)
					{
						InstallPurgeDeleteButton(form, dataGridView);
					}
					TabPage[] array = All(form).OfType<TabPage>().ToArray();
					foreach (TabPage tabPage in array)
					{
						if (dataGridView != null && All(tabPage).Contains(dataGridView))
						{
							Attach(tabPage, form);
						}
						if (dataGridView2 != null && All(tabPage).Contains(dataGridView2))
						{
							AttachWarnings(tabPage, form);
						}
					}
					EnableCheckLists(form);
					EnableFilters(form);
					ThemeTabs(form);
					Refresh(form);
				}
				catch
				{
				}
			};
		}
		catch
		{
		}
	}

	public static void PostCleanerAction(Form form, string methodName)
	{
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Invalid comparison between Unknown and I4
		if (form != null && !form.IsDisposed && !string.IsNullOrWhiteSpace(methodName) && cleanerExternalEvents.TryGetValue(form, out var value) && value?.Event != null)
		{
			lock (value.Handler.Gate)
			{
				value.Handler.MethodName = methodName;
			}
			ExternalEventRequest val = value.Event.Raise();
			if ((int)val != 0 && (int)val != 1)
			{
				MessageBox.Show(form, "Revit is busy. Please try again.", "Smart Cleaner", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
		}
	}

	private static void ApplyCleanerSecondaryButtonStyle(Button button)
	{
		if (button == null || button.IsDisposed) return;
		button.ForeColor = Color.FromArgb(78, 85, 92);
		button.BackColor = Color.FromArgb(232, 235, 239);
		button.FlatStyle = FlatStyle.Flat;
		button.UseVisualStyleBackColor = false;
		button.FlatAppearance.BorderColor = Color.FromArgb(96, 108, 120);
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 242, 245);
		button.FlatAppearance.MouseDownBackColor = Color.FromArgb(216, 221, 227);
	}

	private static void ApplyCleanerContinueButtonStyle(Button button)
	{
		if (button == null || button.IsDisposed) return;
		PropertyInfo primary = button.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (primary != null && primary.CanWrite)
		{
			try { primary.SetValue(button, true, null); } catch { }
		}
		button.UseVisualStyleBackColor = false;
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = Color.FromArgb(250, 230, 168);
		button.ForeColor = Color.FromArgb(55, 67, 79);
		button.FlatAppearance.BorderColor = Color.FromArgb(198, 151, 42);
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 239, 190);
		button.FlatAppearance.MouseDownBackColor = Color.FromArgb(250, 230, 168);
		button.Invalidate();
	}

	private static void CopyActionButtonAppearance(Button source, Button target)
	{
		if (source == null || target == null || source.IsDisposed || target.IsDisposed) return;
		target.AutoSize = false;
		target.Size = source.Size;
		target.Font = source.Font;
		target.ForeColor = source.ForeColor;
		target.BackColor = source.BackColor;
		target.FlatStyle = source.FlatStyle;
		target.UseVisualStyleBackColor = source.UseVisualStyleBackColor;
		target.FlatAppearance.BorderColor = source.FlatAppearance.BorderColor;
		target.FlatAppearance.BorderSize = source.FlatAppearance.BorderSize;
		target.FlatAppearance.MouseOverBackColor = source.FlatAppearance.MouseOverBackColor;
		target.FlatAppearance.MouseDownBackColor = source.FlatAppearance.MouseDownBackColor;
		target.Padding = source.Padding;
		target.TextAlign = source.TextAlign;
		target.Cursor = source.Cursor;
		PropertyInfo sourcePrimary = source.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		PropertyInfo targetPrimary = target.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (sourcePrimary != null && targetPrimary != null && targetPrimary.CanWrite)
			try { targetPrimary.SetValue(target, sourcePrimary.GetValue(source, null), null); } catch { }
		if (source.Region != null)
		{
			Region previous = target.Region;
			target.Region = source.Region.Clone();
			previous?.Dispose();
		}
		target.Invalidate();
	}

	private static void InstallPurgeDeleteButton(Form form, DataGridView grid)
	{
		try
		{
			if (form == null || grid == null || purgeDeleteButtons.TryGetValue(form, out var _))
			{
				return;
			}
			Button[] locateButtons = All(form).OfType<Button>().Where(IsPurgeLocateButton).ToArray();
			Button locate = locateButtons.OrderByDescending(b => b.Visible).FirstOrDefault();
			Button close = All(form).OfType<Button>().FirstOrDefault((Button b) => b.Visible && string.Equals(b.Text, "Close", StringComparison.OrdinalIgnoreCase));
			TabControl cleanerTabs = All(form).OfType<TabControl>().FirstOrDefault(t => t.TabPages.Cast<TabPage>().Any(p => string.Equals((p.Text ?? string.Empty).Trim(), "Overview", StringComparison.OrdinalIgnoreCase)) && t.TabPages.Cast<TabPage>().Any(p => (p.Text ?? string.Empty).IndexOf("Unused", StringComparison.OrdinalIgnoreCase) >= 0));
			TabPage overviewPage = cleanerTabs?.TabPages.Cast<TabPage>().FirstOrDefault(p => string.Equals((p.Text ?? string.Empty).Trim(), "Overview", StringComparison.OrdinalIgnoreCase));
			TabPage unusedPage = cleanerTabs?.TabPages.Cast<TabPage>().FirstOrDefault(p => (p.Text ?? string.Empty).IndexOf("Unused", StringComparison.OrdinalIgnoreCase) >= 0);
			Button[] oldContinueButtons = All(form).OfType<Button>().Where(b => (b.Text ?? string.Empty).Trim().StartsWith("Continue", StringComparison.OrdinalIgnoreCase)).ToArray();
			foreach (Button oldContinue in oldContinueButtons)
			{
				oldContinue.Parent?.Controls.Remove(oldContinue);
				oldContinue.Visible = false;
				oldContinue.Enabled = false;
				oldContinue.Name = "BIMlifyLegacyContinueHidden";
			}
			Button continueButton = null;
			Panel footer = All(form).OfType<Panel>()
				.Where(p => p.Dock == DockStyle.Bottom && p.Height >= 24 && p.Height <= 100 && !(p is FlowLayoutPanel) && !(p is TableLayoutPanel) &&
					(close == null || IsDescendantOf(close, p)))
				.OrderByDescending(p => p.ClientSize.Width)
				.FirstOrDefault();
			footer = footer ?? close?.Parent as Panel ?? locate?.Parent as Panel;
			if (footer == null)
			{
				return;
			}
			// Make a fresh Overview action by copying the actual Close control. This
			// avoids reviving an old hidden Continue instance with stale styling.
			if (close != null)
			{
				try { continueButton = Activator.CreateInstance(close.GetType(), true) as Button; } catch { }
				if (continueButton == null) continueButton = new Button();
				continueButton.Name = "BIMlifyOverviewContinueButton";
				continueButton.Text = "Continue";
				continueButton.AutoSize = false;
				continueButton.MinimumSize = close.Size;
				continueButton.MaximumSize = close.Size;
				CopyActionButtonAppearance(close, continueButton);
				continueButton.Visible = false;
				continueButton.Enabled = true;
				continueButton.Click += (s, e) =>
				{
					if (cleanerTabs != null && unusedPage != null && !unusedPage.IsDisposed)
						cleanerTabs.SelectedTab = unusedPage;
				};
			}
			Button delete = new Button
			{
				Name = "BIMlifyPurgeDeleteButton",
				Text = "Delete",
				Size = new Size(96, 30),
				Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
				ForeColor = Color.FromArgb(65, 72, 79),
				BackColor = Color.FromArgb(239, 205, 120),
				FlatStyle = FlatStyle.Flat,
				UseVisualStyleBackColor = false,
				Enabled = false,
				Anchor = (AnchorStyles.Top | AnchorStyles.Left),
				Cursor = Cursors.Hand
			};
			delete.FlatAppearance.BorderSize = 1;
			delete.FlatAppearance.BorderColor = Color.FromArgb(204, 156, 74);
			delete.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 218, 150);
			delete.FlatAppearance.MouseDownBackColor = Color.FromArgb(201, 153, 45);
			Panel actions = new Panel
			{
				Name = "BIMlifyPurgeFooterActions",
				BackColor = footer.BackColor,
				Height = footer.ClientSize.Height,
				AutoSize = false,
				Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
				Margin = Padding.Empty,
				Padding = Padding.Empty
			};
			footer.Controls.Add(actions);
			if (locate != null)
			{
				locate.AutoSize = false;
				locate.Text = "Locate Elements";
				locate.Width = Math.Max(132, TextRenderer.MeasureText(locate.Text, locate.Font).Width + 28);
				locate.Height = close?.Height ?? locate.Height;
				ApplyCleanerSecondaryButtonStyle(locate);
			}
			delete.AutoSize = false;
			delete.Height = close?.Height ?? delete.Height;
			ApplyCleanerSecondaryButtonStyle(delete);
			if (close != null) close.AutoSize = false;
			if (locate != null) { locate.Parent?.Controls.Remove(locate); actions.Controls.Add(locate); }
			if (close != null) { close.Parent?.Controls.Remove(close); actions.Controls.Add(close); }
			if (continueButton != null) { continueButton.Parent?.Controls.Remove(continueButton); actions.Controls.Add(continueButton); }
			actions.Controls.Add(delete);
			foreach (Button originalDelete in All(form).OfType<Button>().Where(b => b != delete && (string.Equals((b.Text ?? string.Empty).Trim(), "Delete", StringComparison.OrdinalIgnoreCase) || string.Equals((b.Text ?? string.Empty).Trim(), "Purge", StringComparison.OrdinalIgnoreCase))).ToArray())
			{
				originalDelete.Visible = false;
				originalDelete.Enabled = false;
			}
			if (continueButton != null)
			{
				Button protectedContinue = continueButton;
				protectedContinue.EnabledChanged += (object sender, EventArgs e) =>
				{
					if (!protectedContinue.IsDisposed && !protectedContinue.Enabled) protectedContinue.Enabled = true;
					CopyActionButtonAppearance(close, protectedContinue);
				};
			}			// This panel owns the action buttons. Place them from measured widths on
			// every footer/tab pass so a stale native anchor cannot overlap them.
			actions.Controls.Clear();
			const int fixedGap = 12;
			if (continueButton != null && !continueButton.IsDisposed) actions.Controls.Add(continueButton);
			if (locate != null && !locate.IsDisposed) actions.Controls.Add(locate);
			actions.Controls.Add(delete);
			if (close != null && !close.IsDisposed) actions.Controls.Add(close);
			actions.BringToFront();
			purgeDeleteButtons.Add(form, delete);
			Action layout = null;
			layout = () =>
			{
				if (!form.IsDisposed && !footer.IsDisposed && !actions.IsDisposed && !delete.IsDisposed)
				{
					// ModelCleaner can add its Locate button after Smart Cleaner has
					// already built the footer. Re-scan at layout time so a late button
					// is moved into this single action row instead of remaining behind
					// Delete in the original footer.
					Button latestLocate = All(form).OfType<Button>()
						.Where(IsPurgeLocateButton)
						.OrderByDescending(b => b.Visible)
						.ThenByDescending(b => b.Parent == actions)
						.FirstOrDefault();
					if (latestLocate != null && latestLocate != locate)
					{
						if (locate != null && !locate.IsDisposed)
						{
							locate.Visible = false;
						locate.Enabled = false;
						locate.Name = "BIMlifyLegacyLocateHidden";
					}
					locate = latestLocate;
						// The late-created Locate button is often resized by Smart Cleaner
						// immediately after it is added. Watch this actual button too, so
						// Delete and Close are recalculated using its final width.
						EventHandler relayoutLocate = (object s, EventArgs e) =>
						{
							if (form.IsDisposed || !form.IsHandleCreated) return;
							try { form.BeginInvoke((MethodInvoker)(() => { if (!form.IsDisposed) layout(); })); } catch { }
						};
						locate.SizeChanged += relayoutLocate;
						locate.VisibleChanged += relayoutLocate;
						locate.ParentChanged += relayoutLocate;
				}
					foreach (Button extraContinue in All(form).OfType<Button>().Where(b => b != continueButton && (b.Text ?? string.Empty).Trim().StartsWith("Continue", StringComparison.OrdinalIgnoreCase)).ToArray())
					{
						extraContinue.Parent?.Controls.Remove(extraContinue);
						extraContinue.Visible = false;
						extraContinue.Enabled = false;
						extraContinue.Name = "BIMlifyLegacyContinueHidden";
					}
					bool onOverview = cleanerTabs != null && overviewPage != null && cleanerTabs.SelectedTab == overviewPage;
					bool onPurge = cleanerTabs != null && unusedPage != null && cleanerTabs.SelectedTab == unusedPage;
					if (continueButton != null)
					{
						continueButton.Visible = onOverview;
						continueButton.Enabled = true;
						CopyActionButtonAppearance(close, continueButton);
					}
					if (onOverview || onPurge)
					{
						if (locate != null) locate.Visible = onPurge;
						delete.Visible = onPurge;
					}
					if (locate != null && locate.Parent != actions)
					{
						locate.Parent?.Controls.Remove(locate);
						actions.Controls.Add(locate);
					}
					if (locate != null)
					{
						// Keep Locate at its final caption and width from the first frame. The
						// legacy control used a shorter disabled caption on initial load, then
						// expanded after a row selection and collided with Delete.
						locate.Text = "Locate Elements";
						locate.AutoSize = false;
						locate.Dock = DockStyle.None;
						locate.Width = Math.Max(132, TextRenderer.MeasureText(locate.Text, locate.Font).Width + 28);
						locate.Height = close?.Height ?? locate.Height;
					}
					if (continueButton != null && close != null)
					{
						continueButton.MinimumSize = close.Size;
						continueButton.MaximumSize = close.Size;
						continueButton.Size = close.Size;
						continueButton.Font = close.Font;
						continueButton.Cursor = close.Cursor;
						continueButton.TextAlign = close.TextAlign;
						continueButton.Padding = close.Padding;
						CopyActionButtonAppearance(close, continueButton);
						continueButton.Invalidate();
					}
					foreach (Button extraLocate in All(form).OfType<Button>().Where(b => b != locate && IsPurgeLocateButton(b)).ToArray())
					{
						extraLocate.Visible = false;
						extraLocate.Enabled = false;
					}
					const int rightInset = 16;
					List<Button> visibleActions = new List<Button>();
					if (onOverview)
					{
						if (continueButton != null && continueButton.Parent == actions && continueButton.Visible) visibleActions.Add(continueButton);
					}
					else
					{
						if (locate != null && locate.Parent == actions && locate.Visible) visibleActions.Add(locate);
						if (delete.Visible) visibleActions.Add(delete);
					}
					if (close != null && close.Parent == actions && close.Visible) visibleActions.Add(close);
					const int leftInset = 4;
					int actionWidth = visibleActions.Sum(b => b.Width) + Math.Max(0, visibleActions.Count - 1) * fixedGap + leftInset + rightInset;
					actions.Width = actionWidth;
					actions.Height = footer.ClientSize.Height;
					actions.Left = Math.Max(0, footer.ClientSize.Width - actions.Width);
					actions.Top = 0;
					actions.BackColor = footer.BackColor;
					int tallestAction = visibleActions.Count == 0 ? 0 : visibleActions.Max(b => b.Height);
					int verticalOffset = Math.Max(0, (actions.ClientSize.Height - tallestAction) / 2);
					actions.Padding = new Padding(leftInset, verticalOffset, rightInset, 0);
				int actionLeft = leftInset;
				foreach (Button action in visibleActions)
				{
					action.Dock = DockStyle.None;
					action.Anchor = AnchorStyles.Top | AnchorStyles.Left;
					action.Margin = Padding.Empty;
					action.SetBounds(actionLeft, verticalOffset, action.Width, action.Height);
					actionLeft += action.Width + fixedGap;
				}
					actions.BringToFront();
					// Keep a single visible Locate action and enforce the row's z-order
					// after every late control/size update. The coordinates remain
					// Locate, Delete, Close from left to right with fixedGap between them.
					foreach (Button extraLocate in All(form).OfType<Button>().Where(b => b != locate && IsPurgeLocateButton(b)).ToArray())
					{
						extraLocate.Visible = false;
						extraLocate.Enabled = false;
					}
					// Smart Cleaner can create its legacy Delete control after this
					// footer is installed. Hide every duplicate on each pass so that late
					// controls cannot sit over Locate or the unified Delete button.
					foreach (Button extraDelete in All(form).OfType<Button>().Where(b => b != delete &&
						(string.Equals((b.Text ?? string.Empty).Trim(), "Delete", StringComparison.OrdinalIgnoreCase) ||
						 string.Equals((b.Text ?? string.Empty).Trim(), "Purge", StringComparison.OrdinalIgnoreCase))).ToArray())
					{
						extraDelete.Visible = false;
						extraDelete.Enabled = false;
					}
				}
			};
			Action scheduleLayout = () =>
			{
				if (form.IsDisposed) return;
				if (!form.IsHandleCreated) { layout(); return; }
				try { form.BeginInvoke((MethodInvoker)(() => { if (!form.IsDisposed) layout(); })); }
				catch { layout(); }
			};
			// Reflow only when the form, tab, footer or an action button changes.
			// Fixed button positions do not need a polling timer.
			// Watch nested footer controls too: some Smart Cleaner controls are
			// created lazily after Shown, which previously left a second Locate
			// button at its original position and visually under Delete.
			Action<Control> watchControlTree = null;
			watchControlTree = parent =>
			{
				if (parent == null || parent.IsDisposed) return;
				foreach (Control child in parent.Controls) watchControlTree(child);
				parent.ControlAdded += (object sender, ControlEventArgs e) =>
				{
					watchControlTree(e.Control);
					scheduleLayout();
				};
			};
			watchControlTree(form);
			// Reposition after other Smart Cleaner styling code changes a footer
			// button's width or height, avoiding stale overlapping coordinates.
			if (locate != null)
			{
				locate.SizeChanged += (object s, EventArgs e) => scheduleLayout();
				locate.TextChanged += (object s, EventArgs e) => scheduleLayout();
				// A later Smart Cleaner/UI-polish pass may re-anchor Locate after our
				// first layout. Reflow after that pass so it cannot slide under Delete.
				locate.LocationChanged += (object s, EventArgs e) => scheduleLayout();
			}
			delete.SizeChanged += (object s, EventArgs e) => scheduleLayout();
			delete.LocationChanged += (object s, EventArgs e) => scheduleLayout();
			if (close != null) close.SizeChanged += (object s, EventArgs e) => scheduleLayout();
			if (close != null) close.LocationChanged += (object s, EventArgs e) => scheduleLayout();
			if (continueButton != null) continueButton.SizeChanged += (object s, EventArgs e) => scheduleLayout();
			if (continueButton != null) continueButton.LocationChanged += (object s, EventArgs e) => scheduleLayout();
			Action update = () =>
			{
				if (!delete.IsDisposed)
				{
					delete.Enabled = GetSelectedPurgeIds(form).Count > 0;
				}
			};
			bool updatePending = false;
			Action updateAfterGridClick = () =>
			{
				if (form.IsDisposed || !form.IsHandleCreated)
				{
					update();
					return;
				}
				if (updatePending)
				{
					return;
				}
				updatePending = true;
				try
				{
					form.BeginInvoke((MethodInvoker)(() =>
					{
						updatePending = false;
						update();
					}));
				}
				catch
				{
					updatePending = false;
					update();
				}
			};
			delete.Click += (object s, EventArgs e) =>
			{
				try
				{
					grid.EndEdit();
				}
				catch
				{
				}
				SyncPurgeSelectionFromGrid(form, grid);
				PostCleanerAction(form, "DeleteSelectedUnused");
			};
			grid.CellValueChanged += (object s, DataGridViewCellEventArgs e) =>
			{
				updateAfterGridClick();
			};
			grid.CurrentCellDirtyStateChanged += (object s, EventArgs e) =>
			{
				if (grid.IsCurrentCellDirty)
				{
					grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
				}
				updateAfterGridClick();
			};
			grid.CellClick += (object s, DataGridViewCellEventArgs e) =>
			{
				updateAfterGridClick();
			};
			grid.RowsAdded += (object s, DataGridViewRowsAddedEventArgs e) =>
			{
				updateAfterGridClick();
			};
			grid.RowsRemoved += (object s, DataGridViewRowsRemovedEventArgs e) =>
			{
				updateAfterGridClick();
			};
			footer.Resize += (object s, EventArgs e) =>
			{
				layout();
			};
			footer.HandleCreated += (object s, EventArgs e) => scheduleLayout();
			footer.VisibleChanged += (object s, EventArgs e) => { if (footer.Visible) scheduleLayout(); };
			if (cleanerTabs != null)
			{
				cleanerTabs.SelectedIndexChanged += (object s, EventArgs e) =>
				{
					scheduleLayout();
				};
				if (unusedPage != null) unusedPage.Enter += (object s, EventArgs e) => scheduleLayout();
				form.Shown += (object s, EventArgs e) => scheduleLayout();
				form.Activated += (object s, EventArgs e) => scheduleLayout();
			}
			form.Shown += (object s, EventArgs e) =>
			{
				scheduleLayout();
				update();
			};
			grid.SelectionChanged += (object s, EventArgs e) => scheduleLayout();
			grid.DataBindingComplete += (object s, DataGridViewBindingCompleteEventArgs e) => scheduleLayout();
			layout();
			scheduleLayout();
			update();
		}
		catch
		{
		}
	}

	private static bool IsPurgeLocateButton(Button button)
	{
		if (button == null || button.IsDisposed || string.Equals(button.Name, "BIMlifyLegacyLocateHidden", StringComparison.Ordinal)) return false;
		string text = (button.Text ?? string.Empty).Trim();
		return text.IndexOf("Locate", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(button.Name, "_clean", StringComparison.OrdinalIgnoreCase);
	}

	private static List<ElementId> GetSelectedPurgeIds(Form form)
	{
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Expected Obj, but got Unknown
		try
		{
			DataGridView dataGridView = Get<DataGridView>(form, "_purgeGrid");
			if (dataGridView == null)
			{
				return new List<ElementId>();
			}
			DataGridViewColumn dataGridViewColumn = dataGridView.Columns.Cast<DataGridViewColumn>().FirstOrDefault((DataGridViewColumn c) => string.Equals(c.Name, "Pick", StringComparison.OrdinalIgnoreCase) || c is DataGridViewCheckBoxColumn);
			DataGridViewColumn dataGridViewColumn2 = dataGridView.Columns.Cast<DataGridViewColumn>().FirstOrDefault((DataGridViewColumn c) => string.Equals(c.Name, "Id", StringComparison.OrdinalIgnoreCase) || string.Equals(c.DataPropertyName, "Id", StringComparison.OrdinalIgnoreCase));
			if (dataGridViewColumn == null || dataGridViewColumn2 == null)
			{
				return new List<ElementId>();
			}
			List<ElementId> list = new List<ElementId>();
			foreach (DataGridViewRow item in (IEnumerable)dataGridView.Rows)
			{
				if (!item.IsNewRow)
				{
					bool flag = false;
					try
					{
						flag = Convert.ToBoolean(item.Cells[dataGridViewColumn.Index].Value);
					}
					catch
					{
					}
					if (flag && long.TryParse(Convert.ToString(item.Cells[dataGridViewColumn2.Index].Value), out var result))
					{
						list.Add(new ElementId(result));
					}
				}
			}
			return list.Distinct().ToList();
		}
		catch
		{
			return new List<ElementId>();
		}
	}

	private static void SyncPurgeSelectionFromGrid(Form form, DataGridView grid)
	{
		try
		{
			object obj = (form?.GetType().GetField("_selectedPurgeIds", BindingFlags.Instance | BindingFlags.NonPublic))?.GetValue(form);
			if (obj == null)
			{
				return;
			}
			obj.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(obj, null);
			MethodInfo method = obj.GetType().GetMethod("Add", new Type[1] { typeof(ElementId) });
			foreach (ElementId selectedPurgeId in GetSelectedPurgeIds(form))
			{
				method?.Invoke(obj, new object[1] { selectedPurgeId });
			}
		}
		catch
		{
		}
	}

	private static void DeleteSelectedUnused(Form form, UIApplication application)
	{
		//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e2: Invalid comparison between Unknown and I4
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Invalid comparison between Unknown and I4
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Expected Obj, but got Unknown
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a9: Invalid comparison between Unknown and I4
		//IL_0183: Unknown result type (might be due to invalid IL or missing references)
		//IL_0189: Invalid comparison between Unknown and I4
		//IL_0193: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			Document val = Get<Document>(form, "_doc");
			List<ElementId> selectedPurgeIds = GetSelectedPurgeIds(form);
			Label label = Get<Label>(form, "_status");
			if (val == null || val.IsReadOnly || selectedPurgeIds.Count == 0)
			{
				if (label != null)
				{
					label.Text = ((selectedPurgeIds.Count == 0) ? "Select unused items with the check circles, then choose Delete." : "This document is read-only; unused items cannot be deleted.");
				}
				return;
			}
			TaskDialogResult val2 = TaskDialog.Show("Confirm unused-item deletion", "Delete the " + selectedPurgeIds.Count + " selected item(s) that Revit still reports as unused?\n\nEach item is checked again before deletion. Items that would also delete other elements will be skipped.", (TaskDialogCommonButtons)6);
			if ((int)val2 != 6)
			{
				return;
			}
			int num = 0;
			int num2 = 0;
			HashSet<ElementId> hashSet = new HashSet<ElementId>();
			// GetUnusedElements performs a model-wide scan. Capture it once for this
			// delete operation instead of rescanning the entire document per row.
			ISet<ElementId> unusedElements = val.GetUnusedElements((ISet<ElementId>)new HashSet<ElementId>());
			Transaction batchTransaction = new Transaction(val, "BIMlify — delete selected unused items");
			try
			{
				batchTransaction.Start();
				foreach (ElementId id in selectedPurgeIds)
				{
					if (id == (ElementId)null || !unusedElements.Contains(id) || val.GetElement(id) == null)
					{
						num2++;
						continue;
					}
					SubTransaction itemTransaction = null;
					try
					{
						itemTransaction = new SubTransaction(val);
						itemTransaction.Start();
						ICollection<ElementId> collection = val.Delete(id);
						if (collection == null || !collection.Contains(id) || collection.Any((ElementId other) => !((object)other).Equals((object)id)))
						{
							if ((int)itemTransaction.GetStatus() == 1)
							{
								itemTransaction.RollBack();
							}
							num2++;
						}
						else if ((int)itemTransaction.Commit() == 3)
						{
							num++;
							hashSet.Add(id);
						}
						else
						{
							num2++;
						}
					}
					catch
					{
						try
						{
							if (itemTransaction != null && (int)itemTransaction.GetStatus() == 1)
							{
								itemTransaction.RollBack();
							}
						}
						catch
						{
						}
						num2++;
					}
					finally
					{
						if (itemTransaction != null)
						{
							itemTransaction.Dispose();
						}
					}
				}
				if (num > 0)
				{
					if ((int)batchTransaction.Commit() != 3)
					{
						num2 += num;
						num = 0;
						hashSet.Clear();
					}
				}
				else if ((int)batchTransaction.GetStatus() == 1)
				{
					batchTransaction.RollBack();
				}
			}
			catch
			{
				if ((int)batchTransaction.GetStatus() == 1)
				{
					batchTransaction.RollBack();
				}
				throw;
			}
			finally
			{
				batchTransaction.Dispose();
			}
			object obj3 = form.GetType().GetField("_selectedPurgeIds", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form);
			MethodInfo methodInfo = obj3?.GetType().GetMethod("Remove", new Type[1] { typeof(ElementId) });
			foreach (ElementId item in hashSet)
			{
				methodInfo?.Invoke(obj3, new object[1] { item });
			}
			if (form.GetType().GetField("_purgeFindings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) is IList list)
			{
				for (int num3 = list.Count - 1; num3 >= 0; num3--)
				{
					object obj4 = list[num3];
					object obj5 = obj4?.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj4, null) ?? obj4?.GetType().GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj4);
					ElementId val4 = (ElementId)((obj5 is ElementId) ? obj5 : null);
					if (val4 != null && hashSet.Contains(val4))
					{
						list.RemoveAt(num3);
					}
				}
			}
			try
			{
				form.GetType().GetMethod("RefreshPurgeGrid", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(form, null);
			}
			catch
			{
			}
			try
			{
				form.GetType().GetMethod("UpdateCleanEnabled", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(form, null);
			}
			catch
			{
			}
			if (label != null)
			{
				label.Text = "Deleted " + num + " unused item(s); skipped " + num2 + ".";
			}
			Button button = (purgeDeleteButtons.TryGetValue(form, out var value) ? value : null);
			if (button != null)
			{
				button.Enabled = GetSelectedPurgeIds(form).Count > 0;
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(form, ex.Message, "Smart Cleaner", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	public static void AttachCategoryQuickList(Form form)
	{
		try
		{
			if (form == null || categoryQuickLists.TryGetValue(form, out var _))
			{
				return;
			}
			List<DataGridView> source = All(form).OfType<DataGridView>().ToList();
			DataGridView grid = source.FirstOrDefault((DataGridView g) => g.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => c.HeaderText == "Category") && g.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => c.HeaderText == "In view") && g.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => c.HeaderText == "In project"));
			TextBox search = All(form).OfType<TextBox>().FirstOrDefault((TextBox t) => string.Equals(t.AccessibleName, "Search categories", StringComparison.OrdinalIgnoreCase));
			ComboBox type = All(form).OfType<ComboBox>().FirstOrDefault((ComboBox c) => c.Items.Cast<object>().Any((object x) => string.Equals(x?.ToString() ?? "", "All types", StringComparison.OrdinalIgnoreCase)));
			if (grid == null || search == null || type == null || search.Parent == null)
			{
				return;
			}
			type.BackColor = Color.FromArgb(255, 239, 190);
			type.ForeColor = Color.FromArgb(67, 75, 82);
			categoryQuickLists.Add(form, new object());
			Control panel = search.Parent;
			Label searchLabel = panel.Controls.OfType<Label>().FirstOrDefault((Label l) => l.Text == "Search");
			Label typeLabel = panel.Controls.OfType<Label>().FirstOrDefault((Label l) => l.Text == "Category type");
			Label label = panel.Controls.OfType<Label>().FirstOrDefault((Label l) => l.Text.IndexOf("Loaded links", StringComparison.OrdinalIgnoreCase) >= 0);
			IncludeLoadedLinks = false;
			if (label != null)
			{
				CheckBox includeLinks = new CheckBox
				{
					Text = "Include loaded links",
					Checked = false,
					AutoSize = false,
					AutoEllipsis = true,
					Width = 160,
					Height = 28,
					ForeColor = Color.FromArgb(78, 85, 92),
					Font = new Font("Segoe UI", 8.5f),
					TextAlign = ContentAlignment.MiddleLeft,
					CheckAlign = ContentAlignment.MiddleLeft,
					Anchor = (AnchorStyles.Top | AnchorStyles.Right)
				};
				includeLinks.CheckedChanged += (object s, EventArgs e) =>
				{
					IncludeLoadedLinks = includeLinks.Checked;
				};
				panel.Controls.Remove(label);
				label.Dispose();
				panel.Controls.Add(includeLinks);
				includeLinks.SetBounds(Math.Max(0, panel.ClientSize.Width - 174), 5, 162, 30);
				includeLinks.BringToFront();
			}
			Label label2 = All(form).OfType<Label>().FirstOrDefault((Label l) => l.Text.StartsWith("Select includes matching elements", StringComparison.OrdinalIgnoreCase));
			if (label2 != null)
			{
				label2.Text = "Select or isolate matching elements in loaded links when Include loaded links is checked.";
			}
			Label quickLabel = new Label
			{
				Text = "Category",
				AutoSize = false,
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = Color.FromArgb(78, 85, 92),
				Font = new Font("Segoe UI", 8.5f)
			};
			ComboBox quick = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				FlatStyle = FlatStyle.Flat,
				DrawMode = DrawMode.OwnerDrawFixed,
				Font = new Font("Segoe UI", 8.5f),
				BackColor = Color.FromArgb(255, 239, 190),
				ForeColor = Color.FromArgb(67, 75, 82),
				MaxDropDownItems = 18,
				DropDownHeight = 360,
				IntegralHeight = false,
				ItemHeight = 25
			};
			quick.Items.Add("All categories");
			foreach (string item in (from DataGridViewRow r in grid.Rows
				where !r.IsNewRow
				select Convert.ToString(r.Cells[1].Value) into x
				where !string.IsNullOrWhiteSpace(x)
				select x).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy((string x) => x, StringComparer.CurrentCultureIgnoreCase))
			{
				quick.Items.Add(item);
			}
			quick.SelectedIndex = 0;
			quick.DrawItem += (object s, DrawItemEventArgs e) =>
			{
				if (e.Bounds.Width > 0 && e.Bounds.Height > 0)
				{
					using (SolidBrush brush = new SolidBrush(((e.State & DrawItemState.Selected) == DrawItemState.Selected) ? Color.FromArgb(255, 226, 153) : Color.FromArgb(255, 247, 220)))
					{
						e.Graphics.FillRectangle(brush, e.Bounds);
					}
					string text = ((e.Index >= 0) ? quick.GetItemText(quick.Items[e.Index]) : quick.Text);
					TextRenderer.DrawText(e.Graphics, text, quick.Font, e.Bounds, Color.FromArgb(67, 75, 82), TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
					e.DrawFocusRectangle();
				}
			};
			panel.Controls.Add(quickLabel);
			panel.Controls.Add(quick);
			bool fromQuick = false;
			bool resettingQuickFromSearch = false;
			bool quickApplied = false;
			quick.SelectedIndexChanged += (object s, EventArgs e) =>
			{
				if (quick.SelectedIndex > 0)
				{
					fromQuick = true;
					search.Text = string.Empty;
					search.SelectionStart = 0;
					fromQuick = false;
					quickApplied = true;
					string b = Convert.ToString(quick.SelectedItem);
					{
						foreach (DataGridViewRow item2 in (IEnumerable)grid.Rows)
						{
							if (!item2.IsNewRow)
							{
								item2.Visible = string.Equals(Convert.ToString(item2.Cells[1].Value), b, StringComparison.OrdinalIgnoreCase);
							}
						}
						return;
					}
				}
				if (quickApplied && !resettingQuickFromSearch)
				{
					fromQuick = true;
					search.Text = string.Empty;
					search.SelectionStart = 0;
					fromQuick = false;
					quickApplied = false;
				}
			};
			search.TextChanged += (object s, EventArgs e) =>
			{
				if (!fromQuick && quick.SelectedIndex > 0)
				{
					resettingQuickFromSearch = true;
					quick.SelectedIndex = 0;
					resettingQuickFromSearch = false;
					quickApplied = false;
				}
			};
			Action layout = () =>
			{
				int width = panel.ClientSize.Width;
				double num = Math.Min(1.0, Math.Max(0.82, (double)width / 1022.0));
				int num2 = Math.Max(132, (int)Math.Round(162.0 * num));
				int num3 = Math.Max(140, (int)Math.Round(225.0 * num));
				int num4 = Math.Max(58, (int)Math.Round(70.0 * num));
				int num5 = Math.Max(108, (int)Math.Round(126.0 * num));
				int num6 = Math.Max(80, (int)Math.Round(92.0 * num));
				int num7 = 12;
				int num8 = 8;
				int num9 = 6;
				int num10 = 74;
				int num11 = width - num7 - num2;
				CheckBox checkBox = panel.Controls.OfType<CheckBox>().FirstOrDefault((CheckBox c) => c.Text == "Include loaded links");
				if (checkBox != null)
				{
					checkBox.SetBounds(Math.Max(0, num11), 5, num2, 30);
					checkBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
				}
				int num12 = num11 - num8 - num3;
				int num13 = num12 - num9 - num4;
				int num14 = num13 - num8 - num5;
				int num15 = num14 - num9 - num6;
				int width2 = Math.Max(100, num15 - num8 - num10);
				if (searchLabel != null)
				{
					searchLabel.AutoSize = false;
					searchLabel.AutoEllipsis = false;
					searchLabel.UseMnemonic = false;
					searchLabel.TextAlign = ContentAlignment.MiddleLeft;
					searchLabel.SetBounds(10, 5, 56, 30);
				}
				search.SetBounds(num10, 6, width2, 28);
				search.Anchor = AnchorStyles.Top | AnchorStyles.Left;
				search.AutoSize = false;
				if (typeLabel != null)
				{
					typeLabel.AutoSize = false;
					typeLabel.AutoEllipsis = true;
					typeLabel.UseMnemonic = false;
					typeLabel.TextAlign = ContentAlignment.MiddleLeft;
					typeLabel.SetBounds(num15, 5, num6, 30);
				}
				type.SetBounds(num14, 6, num5, 28);
				type.Anchor = AnchorStyles.Top | AnchorStyles.Left;
				quickLabel.AutoSize = false;
				quickLabel.AutoEllipsis = true;
				quickLabel.UseMnemonic = false;
				quickLabel.SetBounds(num13, 5, num4, 30);
				quick.SetBounds(num12, 6, num3, 28);
				quick.Anchor = AnchorStyles.Top | AnchorStyles.Left;
			};
			layout();
			panel.Resize += (object s, EventArgs e) =>
			{
				layout();
			};
			quick.BringToFront();
			quickLabel.BringToFront();
		}
		catch
		{
		}
	}

	public static void FilterCategoryTargets(object targets)
	{
		try
		{
			if (IncludeLoadedLinks || targets == null)
			{
				return;
			}
			BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			Type type = targets.GetType();
			IList list = type.GetField("References", bindingAttr)?.GetValue(targets) as IList;
			IList list2 = type.GetField("IsolationIds", bindingAttr)?.GetValue(targets) as IList;
			if (list == null)
			{
				return;
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			for (int num = list.Count - 1; num >= 0; num--)
			{
				object obj = list[num];
				if (obj != null)
				{
					Type type2 = obj.GetType();
					object obj2 = type2.GetProperty("LinkedElementId", bindingAttr)?.GetValue(obj, null);
					if (obj2 != null)
					{
						object obj3 = obj2.GetType().GetProperty("Value", bindingAttr)?.GetValue(obj2, null) ?? obj2.GetType().GetProperty("IntegerValue", bindingAttr)?.GetValue(obj2, null);
						if (obj3 != null && Convert.ToInt64(obj3) != -1)
						{
							object obj4 = type2.GetProperty("ElementId", bindingAttr)?.GetValue(obj, null);
							if (obj4 != null)
							{
								object obj5 = obj4.GetType().GetProperty("Value", bindingAttr)?.GetValue(obj4, null) ?? obj4.GetType().GetProperty("IntegerValue", bindingAttr)?.GetValue(obj4, null);
								if (obj5 != null)
								{
									hashSet.Add(Convert.ToString(obj5));
								}
							}
							list.RemoveAt(num);
						}
					}
				}
			}
			if (list2 == null || hashSet.Count <= 0)
			{
				return;
			}
			for (int num2 = list2.Count - 1; num2 >= 0; num2--)
			{
				object obj6 = list2[num2];
				if (obj6 != null)
				{
					object obj7 = obj6.GetType().GetProperty("Value", bindingAttr)?.GetValue(obj6, null) ?? obj6.GetType().GetProperty("IntegerValue", bindingAttr)?.GetValue(obj6, null);
					if (obj7 != null && hashSet.Contains(Convert.ToString(obj7)))
					{
						list2.RemoveAt(num2);
					}
				}
			}
		}
		catch
		{
		}
	}

	public static void CompactPatternMaker(Form form)
	{
		try
		{
			if (form != null)
			{
				EventHandler apply = null;
				apply = (object s, EventArgs e) =>
				{
					form.Shown -= apply;
					NormalizeWindowFooter(form);
					CompactPatternMakerNow(form);
				};
				form.Shown += apply;
			}
		}
		catch
		{
		}
	}

	private static void CompactPatternMakerNow(Form form)
	{
		try
		{
			Label label = null;
			Label status = null;
			foreach (Control item in All(form))
			{
				if (item is Label label2)
				{
					if (string.Equals(label2.Text, "Preview", StringComparison.OrdinalIgnoreCase))
					{
						label = label2;
					}
					if (label2.Dock == DockStyle.Bottom && label2.Height >= 40)
					{
						status = label2;
					}
				}
			}
			if (status != null)
			{
				status.Height = 0;
				if (status.Parent is Panel panel)
				{
					panel.Padding = new Padding(panel.Padding.Left, panel.Padding.Top, panel.Padding.Right, 0);
				}
				Action update = () =>
				{
					bool flag = !string.IsNullOrWhiteSpace(status.Text);
					status.Height = (flag ? 30 : 0);
					status.Visible = flag;
					status.Parent?.PerformLayout();
				};
				status.TextChanged += (object s, EventArgs e) =>
				{
					update();
				};
				update();
			}
			Color color = Color.FromArgb(238, 241, 245);
			Color color2 = Color.FromArgb(246, 247, 249);
			Color backColor = Color.White;
			foreach (Control item2 in All(form))
			{
				if (item2 is Button button)
				{
					if (string.Equals(button.Text, "Read Description", StringComparison.OrdinalIgnoreCase) || string.Equals(button.Text, "Write with AI", StringComparison.OrdinalIgnoreCase) || string.Equals(button.Text, "Import .PAT", StringComparison.OrdinalIgnoreCase))
					{
						button.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(button, true, null);
						button.Invalidate();
					}
					if ((string.Equals(button.Text, "Read Description", StringComparison.OrdinalIgnoreCase) || string.Equals(button.Text, "Write with AI", StringComparison.OrdinalIgnoreCase)) && !patternButtonOffsets.TryGetValue(button, out var _))
					{
						button.Top += Math.Max(2, (int)Math.Round(9.0 * (double)((form.DeviceDpi > 0) ? form.DeviceDpi : 96) / 96.0));
						patternButtonOffsets.Add(button, new object());
					}
				}
				else if (item2 is Panel && (item2.BackColor == color || item2.BackColor == color2) && !All(item2).OfType<Label>().Any((Label l) => string.Equals(l.Text, "Pattern Maker", StringComparison.OrdinalIgnoreCase)))
				{
					item2.BackColor = backColor;
				}
			}
			foreach (Control control3 in form.Controls)
			{
				if (control3 is Panel { Dock: DockStyle.Fill } panel2)
				{
					panel2.Padding = new Padding(panel2.Padding.Left, panel2.Padding.Top, panel2.Padding.Right, 24);
				}
			}
			ApplyPatternMakerCardOffset(form);
			if (form.IsHandleCreated)
			{
				try
				{
					form.BeginInvoke((MethodInvoker)(() =>
					{
						if (!form.IsDisposed)
						{
							ApplyPatternMakerCardOffset(form);
						}
					}));
				}
				catch
				{
				}
			}
			if (label == null || label.Parent == null)
			{
				return;
			}
			Panel preview = label.Parent.Controls.OfType<Panel>().FirstOrDefault((Panel p) => p.Dock == DockStyle.Top && p.Height >= 190 && p.Height <= 260);
			if (preview == null)
			{
				return;
			}
			Control[] array = preview.Controls.Cast<Control>().ToArray();
			foreach (Control control2 in array)
			{
				if (string.IsNullOrWhiteSpace(control2.Text) && control2.BackColor == Color.White && control2.Width <= 120 && control2.Height <= 80)
				{
					control2.Visible = false;
				}
			}
			preview.BackColor = Color.White;
			RemoveFirstControlHandler(preview, "s_paintEvent");
			preview.BorderStyle = BorderStyle.None;
			preview.TabStop = false;
			MethodInfo method = typeof(Control).GetMethod("SetStyle", BindingFlags.Instance | BindingFlags.NonPublic);
			if (method != null)
			{
				method.Invoke(preview, new object[2]
				{
					ControlStyles.Selectable,
					false
				});
			}
			PropertyInfo property = typeof(Panel).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
			if (property != null)
			{
				property.SetValue(preview, true, null);
			}
			form.Shown += (object s, EventArgs e) =>
			{
				preview.Invalidate();
			};
		}
		catch
		{
		}
	}

	private static void ApplyPatternMakerCardOffset(Form form)
	{
		try
		{
			if (form == null || form.IsDisposed)
			{
				return;
			}
			Panel body = form.Controls.OfType<Panel>().FirstOrDefault((Panel p) => p.Dock == DockStyle.Fill && p.Controls.OfType<Panel>().Any((Panel card) => All(card).OfType<Label>().Any((Label l) => string.Equals(l.Text, "Description", StringComparison.OrdinalIgnoreCase))) && p.Controls.OfType<Panel>().Any((Panel card) => All(card).OfType<Label>().Any((Label l) => string.Equals(l.Text, "Preview", StringComparison.OrdinalIgnoreCase))));
			if (body == null)
			{
				return;
			}
			Panel editor = body.Controls.OfType<Panel>().FirstOrDefault((Panel card) => All(card).OfType<Label>().Any((Label l) => string.Equals(l.Text, "Description", StringComparison.OrdinalIgnoreCase)));
			Panel preview = body.Controls.OfType<Panel>().FirstOrDefault((Panel card) => All(card).OfType<Label>().Any((Label l) => string.Equals(l.Text, "Preview", StringComparison.OrdinalIgnoreCase)));
			if (editor == null || preview == null)
			{
				return;
			}
			body.BackColor = Color.White;
			StylePatternMakerCard(editor);
			StylePatternMakerCard(preview);
			editor.Dock = DockStyle.None;
			preview.Dock = DockStyle.None;
			editor.Margin = Padding.Empty;
			preview.Margin = Padding.Empty;
			editor.MinimumSize = Size.Empty;
			preview.MinimumSize = Size.Empty;
			editor.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
			preview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			if (!patternBodyLayouts.TryGetValue(body, out var _))
			{
				patternBodyLayouts.Add(body, new object());
				body.Resize += (object s, EventArgs e) =>
				{
					LayoutPatternMakerCards(form, body, editor, preview);
				};
				form.Resize += (object s, EventArgs e) =>
				{
					LayoutPatternMakerCards(form, body, editor, preview);
				};
				form.Layout += (object s, LayoutEventArgs e) =>
				{
					LayoutPatternMakerCards(form, body, editor, preview);
				};
				form.Shown += (object s, EventArgs e) =>
				{
					if (form.IsDisposed || !form.IsHandleCreated)
					{
						return;
					}
					try
					{
						form.BeginInvoke((MethodInvoker)(() =>
						{
							LayoutPatternMakerCards(form, body, editor, preview);
						}));
					}
					catch
					{
					}
				};
			}
			LayoutPatternMakerCards(form, body, editor, preview);
		}
		catch
		{
		}
	}

	private static void LayoutPatternMakerCards(Form form, Panel body, Panel editor, Panel preview)
	{
		bool flag = false;
		try
		{
			if (form != null && !form.IsDisposed && body != null && !body.IsDisposed && editor != null && preview != null)
			{
				int num = ((form.DeviceDpi > 0) ? form.DeviceDpi : 96);
				int num2 = Math.Max(1, (int)Math.Round(20.0 * (double)num / 96.0));
				int num3 = Math.Max(1, (int)Math.Round(24.0 * (double)num / 96.0));
				int bottom = Math.Max(18, (int)Math.Round(18.0 * (double)num / 96.0));
				Padding padding = new Padding(num2, num2, num2, bottom);
				if (body.Padding != padding)
				{
					body.Padding = padding;
				}
				int top = body.Padding.Top;
				int num4 = Math.Max(0, body.ClientSize.Width - body.Padding.Horizontal);
				int num5 = Math.Max(0, (num4 - num3) / 2);
				int num6 = body.Padding.Left + Math.Max(0, (num4 - (num5 * 2 + num3)) / 2);
				int num7 = Math.Max(0, body.ClientSize.Height - body.Padding.Top - body.Padding.Bottom);
				int num8 = (from Control c in editor.Controls
					where c.Visible
					select c.Bottom).DefaultIfEmpty(0).Max();
				int height = Math.Min(num7, Math.Max(0, num8 + Scale(form, 34)));
				body.SuspendLayout();
				flag = true;
				Rectangle editorBounds = new Rectangle(num6, top, num5, height);
				Rectangle previewBounds = new Rectangle(num6 + num5 + num3, top, num5, num7);
				if (editor.Bounds != editorBounds)
				{
					editor.Bounds = editorBounds;
				}
				if (preview.Bounds != previewBounds)
				{
					preview.Bounds = previewBounds;
				}
				body.ResumeLayout(performLayout: true);
				flag = false;
			}
		}
		catch
		{
			if (flag)
			{
				try
				{
					body?.ResumeLayout(performLayout: true);
					return;
				}
				catch
				{
					return;
				}
			}
		}
	}

	private static void StylePatternMakerCard(Panel card)
	{
		try
		{
			if (card == null)
			{
				return;
			}
			if (!patternCardStyles.TryGetValue(card, out var _))
			{
				patternCardStyles.Add(card, new object());
			}
			RemoveControlHandlers(card, "EventPaint");
			RemoveControlHandlers(card, "s_paintEvent");
			RemoveControlHandlers(card, "EventResize");
			RemoveControlHandlers(card, "s_resizeEvent");
			card.Region = null;
			card.BorderStyle = BorderStyle.None;
			card.BackColor = Color.White;
			card.Paint += (object sender, PaintEventArgs e) =>
			{
				if (card.ClientSize.Width < 5 || card.ClientSize.Height < 5)
					return;
				using Pen border = new Pen(Color.FromArgb(166, 175, 185));
				e.Graphics.DrawRectangle(border, 0, 0, card.ClientSize.Width - 1, card.ClientSize.Height - 1);
			};
			foreach (Panel item in All(card).OfType<Panel>())
			{
				if (item != card)
				{
					item.BorderStyle = BorderStyle.None;
				}
			}
			card.Invalidate();
		}
		catch
		{
		}
	}

	private static void RemoveControlHandlers(Control control, string eventKeyName)
	{
		try
		{
			if (control == null)
			{
				return;
			}
			BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
			EventHandlerList eventHandlerList = typeof(Control).GetProperty("Events", bindingAttr)?.GetValue(control, null) as EventHandlerList;
			object obj = typeof(Control).GetField(eventKeyName, bindingAttr)?.GetValue(null);
			if (eventHandlerList != null && obj != null)
			{
				Delegate obj2 = eventHandlerList[obj];
				if ((object)obj2 != null)
				{
					eventHandlerList.RemoveHandler(obj, obj2);
				}
			}
		}
		catch
		{
		}
	}

	private static void RemoveFirstControlHandler(Control control, string eventKeyName)
	{
		try
		{
			if (control == null)
			{
				return;
			}
			BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
			EventHandlerList eventHandlerList = typeof(Control).GetProperty("Events", bindingAttr)?.GetValue(control, null) as EventHandlerList;
			object obj = typeof(Control).GetField(eventKeyName, bindingAttr)?.GetValue(null);
			if (eventHandlerList != null && obj != null)
			{
				Delegate obj2 = eventHandlerList[obj]?.GetInvocationList().FirstOrDefault();
				if ((object)obj2 != null)
				{
					eventHandlerList.RemoveHandler(obj, obj2);
				}
			}
		}
		catch
		{
		}
	}

	private static void DrawPatternMakerWorkspace(Panel body, Graphics graphics)
	{
		try
		{
			if (body == null || body.IsDisposed || body.ClientSize.Width < 40 || body.ClientSize.Height < 40)
			{
				return;
			}
			int num = Math.Max(8, Scale(body, 10));
			Rectangle bounds = new Rectangle(num, num, body.ClientSize.Width - 2 * num - 1, body.ClientSize.Height - 2 * num - 1);
			if (bounds.Width < 20 || bounds.Height < 20)
			{
				return;
			}
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = RoundedCardPath(bounds, 8);
			using Pen pen = new Pen(Color.FromArgb(205, 211, 217), 1f);
			graphics.DrawPath(pen, path);
		}
		catch
		{
		}
	}

	private static GraphicsPath RoundedCardPath(Rectangle bounds, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		int num = Math.Max(2, radius * 2);
		graphicsPath.AddArc(bounds.Left, bounds.Top, num, num, 180f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Top, num, num, 270f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(bounds.Left, bounds.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	public static void FitViewTemplateTabs(Form form)
	{
		try
		{
			if (form == null || form.IsDisposed)
			{
				return;
			}
			TabControl tabs = All(form).OfType<TabControl>().FirstOrDefault((TabControl t) => t.TabPages.Cast<TabPage>().Any((TabPage p) => string.Equals(p.Text, "3D Views", StringComparison.Ordinal)) && t.TabPages.Cast<TabPage>().Any((TabPage p) => string.Equals(p.Text, "Plan Views", StringComparison.Ordinal)) && t.TabPages.Cast<TabPage>().Any((TabPage p) => string.Equals(p.Text, "RCP Views", StringComparison.Ordinal)));
			if (tabs == null)
			{
				return;
			}
			tabs.SizeMode = TabSizeMode.Fixed;
			tabs.Multiline = false;
			tabs.Appearance = TabAppearance.Normal;
			ToolTip tips = new ToolTip
			{
				AutoPopDelay = 5000,
				InitialDelay = 350,
				ReshowDelay = 100
			};
			bool fitting = false;
			Action fit = () =>
			{
				if (fitting || tabs.IsDisposed)
				{
					return;
				}
				fitting = true;
				try
				{
					int num = ((form.DeviceDpi > 0) ? form.DeviceDpi : 96);
					int width = Math.Max(90, (int)Math.Round(90.0 * (double)num / 96.0));
					int height = Math.Max((int)Math.Round(28.0 * (double)num / 96.0), tabs.ItemSize.Height);
					Size size = new Size(width, height);
					if (tabs.ItemSize != size)
					{
						tabs.ItemSize = size;
					}
					if (tabs.Font.Size < 9f)
					{
						tabs.Font = new Font("Segoe UI", 9f, tabs.Font.Style);
					}
				}
				finally
				{
					fitting = false;
				}
			};
			tabs.Resize += (object s, EventArgs e) =>
			{
				fit();
			};
			tabs.MouseMove += (object s, MouseEventArgs e) =>
			{
				for (int i = 0; i < tabs.TabPages.Count; i++)
				{
					if (tabs.GetTabRect(i).Contains(e.Location))
					{
						tips.SetToolTip(tabs, tabs.TabPages[i].Text);
						break;
					}
				}
			};
			form.Shown += (object s, EventArgs e) =>
			{
				fit();
				if (form.IsHandleCreated)
				{
					form.BeginInvoke(fit);
				}
			};
			fit();
		}
		catch
		{
		}
	}

	public static void StyleSheetOrganization(Form form)
	{
		try
		{
			if (form != null)
			{
				EventHandler apply = null;
				apply = (object s, EventArgs e) =>
				{
					form.Shown -= apply;
					StyleSheetOrganizationNow(form);
				};
				form.Shown += apply;
			}
		}
		catch
		{
		}
	}

	private static void StyleSheetOrganizationNow(Form form)
	{
		try
		{
			DataGridView dataGridView = All(form).OfType<DataGridView>().FirstOrDefault((DataGridView g) => g.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => c.HeaderText == "Preview Number") && g.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => c.HeaderText == "Group Code"));
			if (dataGridView != null)
			{
				Label instructions = All(form).OfType<Label>().FirstOrDefault((Label l) => (l.Text ?? string.Empty).StartsWith("Edit Group Code or Sheet Name", StringComparison.OrdinalIgnoreCase));
				if (instructions != null)
				{
					instructions.Text = "Group Code = number prefix. Edit it or Sheet Name; select rows to batch edit.";
					instructions.AutoSize = true;
				}
				if (dataGridView.Columns.Count > 4)
				{
					dataGridView.Columns[2].HeaderCell.ToolTipText = "Generated from Group Code, Separator, Digits, and Start.";
					dataGridView.Columns[3].HeaderCell.ToolTipText = "Current Revit sheet number. Read-only; Apply Organization updates it.";
					dataGridView.Columns[4].HeaderCell.ToolTipText = "Editable. Double-click or type; selected rows receive the same name.";
				}
				dataGridView.CellDoubleClick += (object s, DataGridViewCellEventArgs e) =>
				{
					if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.ColumnIndex >= dataGridView.Columns.Count)
					{
						return;
					}
					if (e.ColumnIndex == 4 || e.ColumnIndex == 1)
					{
						dataGridView.CurrentCell = dataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex];
						dataGridView.BeginEdit(selectAll: true);
					}
					else if (e.ColumnIndex == 2 || e.ColumnIndex == 3 && dataGridView.Columns[3].ReadOnly)
					{
						dataGridView.CancelEdit();
						dataGridView.CurrentCell = dataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex];
					}
				};
				Color color = Color.FromArgb(78, 85, 92);
				dataGridView.GridColor = Color.FromArgb(210, 215, 222);
				dataGridView.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
				dataGridView.ColumnHeadersHeight = 34;
				dataGridView.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 241, 245);
				dataGridView.ColumnHeadersDefaultCellStyle.ForeColor = color;
				dataGridView.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
				dataGridView.DefaultCellStyle.BackColor = Color.FromArgb(237, 240, 244);
				dataGridView.DefaultCellStyle.ForeColor = color;
				dataGridView.DefaultCellStyle.SelectionBackColor = Color.FromArgb(252, 243, 218);
				dataGridView.DefaultCellStyle.SelectionForeColor = color;
				dataGridView.DefaultCellStyle.Padding = new Padding(4);
				dataGridView.RowTemplate.Height = 27;
				dataGridView.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(226, 230, 235);
				dataGridView.AlternatingRowsDefaultCellStyle.ForeColor = color;
				dataGridView.EnableHeadersVisualStyles = false;
				dataGridView.Invalidate();
			}
		}
		catch
		{
		}
	}

	private static void AttachGrid(TabPage page, object owner, string gridField)
	{
		try
		{
			Button scan = Get<Button>(owner, "_scan");
			DataGridView grid = Get<DataGridView>(owner, gridField);
			Label label = Get<Label>(owner, "_status");
			if (page == null || scan == null || grid == null || grid.Parent == null)
			{
				return;
			}
			if (states.Any((State x) => x.Grid == grid))
			{
				Refresh(owner);
				return;
			}
			EnableGridPicking(grid, owner, gridField);
			EnableFilters(owner);
			ThemeTabs(owner);
			State state = new State
			{
				Grid = grid,
				Owner = owner,
				GridField = gridField,
				Overlay = new Panel
				{
					BackColor = Color.White
				}
			};
			state.Message = new Label
			{
				Text = "No scan results yet. Click Scan Model to check this project.",
				AutoSize = false,
				TextAlign = ContentAlignment.MiddleCenter,
				ForeColor = Color.FromArgb(102, 109, 118),
				Font = new Font("Segoe UI", 9f)
			};
			state.Run = new Button
			{
				Text = "Scan Model",
				Size = new Size(126, 34),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(239, 205, 120),
				ForeColor = Color.FromArgb(65, 72, 79),
				Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
				Cursor = Cursors.Hand
			};
			state.Run.FlatAppearance.BorderColor = Color.FromArgb(204, 156, 74);
			state.Run.FlatAppearance.BorderSize = 1;
			state.Overlay.Controls.Add(state.Message);
			state.Overlay.Controls.Add(state.Run);
			Control parent = grid.Parent;
			Panel host = new Panel
			{
				BackColor = Color.White,
				Padding = Padding.Empty,
				Margin = grid.Margin
			};
			if (parent is TableLayoutPanel tableLayoutPanel)
			{
				TableLayoutPanelCellPosition cellPosition = tableLayoutPanel.GetCellPosition(grid);
				int columnSpan = tableLayoutPanel.GetColumnSpan(grid);
				int rowSpan = tableLayoutPanel.GetRowSpan(grid);
				tableLayoutPanel.Controls.Remove(grid);
				host.Dock = DockStyle.Fill;
				tableLayoutPanel.Controls.Add(host, cellPosition.Column, cellPosition.Row);
				tableLayoutPanel.SetColumnSpan(host, columnSpan);
				tableLayoutPanel.SetRowSpan(host, rowSpan);
			}
			else
			{
				Rectangle bounds = grid.Bounds;
				DockStyle dock = grid.Dock;
				AnchorStyles anchor = grid.Anchor;
				parent.Controls.Remove(grid);
				host.Bounds = bounds;
				host.Dock = dock;
				host.Anchor = anchor;
				parent.Controls.Add(host);
			}
			grid.Dock = DockStyle.Fill;
			host.Controls.Add(grid);
			state.Overlay.Dock = DockStyle.None;
			state.Overlay.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			host.Controls.Add(state.Overlay);
			state.Overlay.BringToFront();
			host.BringToFront();
			Action layout = () =>
			{
				int num2 = (grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0);
				state.Overlay.SetBounds(0, num2, host.ClientSize.Width, Math.Max(0, host.ClientSize.Height - num2));
				state.Message.SetBounds(8, Math.Max(12, (state.Overlay.Height - 64) / 2 - 12), Math.Max(100, state.Overlay.Width - 16), 32);
				state.Run.Left = (state.Overlay.ClientSize.Width - state.Run.Width) / 2;
				state.Run.Top = state.Message.Bottom + 4;
			};
			host.SizeChanged += (object s, EventArgs e) =>
			{
				layout();
			};
			layout();
			state.Run.Click += (object s, EventArgs e) =>
			{
				state.Overlay.Visible = false;
				scan.PerformClick();
			};
			if (label != null)
			{
				label.TextChanged += (object s, EventArgs e) =>
				{
					Refresh(owner);
				};
			}
			string[] array = new string[4] { "_kindFilter", "_categoryFilter", "_candidateSearch", "_reviewSearch" };
			foreach (string name in array)
			{
				Control control = Get<Control>(owner, name);
				if (control is ComboBox comboBox)
				{
					comboBox.SelectedIndexChanged += (object s, EventArgs e) =>
					{
						Refresh(owner);
					};
				}
				else if (control is TextBox textBox)
				{
					textBox.TextChanged += (object s, EventArgs e) =>
					{
						Refresh(owner);
					};
				}
			}
			states.RemoveAll((State x) => x.Grid == grid);
			states.Add(state);
			Refresh(owner);
		}
		catch
		{
		}
	}

	private static void EnableGridPicking(DataGridView grid, object owner, string gridField)
	{
		if (grid == null)
		{
			return;
		}
		grid.EnableHeadersVisualStyles = false;
		grid.BackgroundColor = Color.White;
		grid.BorderStyle = BorderStyle.None;
		grid.GridColor = Color.FromArgb(220, 224, 229);
		grid.RowHeadersVisible = false;
		grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, 34);
		grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 245);
		grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
		grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
		grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
		grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
		grid.Font = new Font("Segoe UI", 8.5f);
		grid.DefaultCellStyle.BackColor = Color.White;
		grid.DefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
		grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
		grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(78, 85, 92);
		grid.DefaultCellStyle.Padding = new Padding(4);
		grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 252);
		grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
		grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
		grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.FromArgb(78, 85, 92);
		grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, 34);
		grid.RowTemplate.MinimumHeight = Math.Max(grid.RowTemplate.MinimumHeight, 34);
		grid.AllowUserToAddRows = false;
		grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
		grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
		grid.AlternatingRowsDefaultCellStyle.WrapMode = DataGridViewTriState.False;
		foreach (DataGridViewRow item in (IEnumerable)grid.Rows)
		{
			if (!item.IsNewRow)
			{
				item.Height = Math.Max(item.Height, 34);
			}
		}
		if (gridField == "_purgeGrid" && !emptyPurgeRowHandlers.TryGetValue(grid, out var value))
		{
			emptyPurgeRowHandlers.Add(grid, new object());
			Action hideEmptyRows = () =>
			{
				try
				{
					foreach (DataGridViewRow item2 in (IEnumerable)grid.Rows)
					{
						if (!item2.IsNewRow)
						{
							bool flag = false;
							foreach (DataGridViewColumn column in grid.Columns)
							{
								if (!string.Equals(column.Name, "Pick", StringComparison.OrdinalIgnoreCase))
								{
									object value2 = item2.Cells[column.Index].Value;
									if (value2 != null && value2 != DBNull.Value && !string.IsNullOrWhiteSpace(Convert.ToString(value2)))
									{
										flag = true;
										break;
									}
								}
							}
							if (!flag)
							{
								item2.Visible = false;
							}
						}
					}
				}
				catch
				{
				}
			};
			grid.DataBindingComplete += (object s, DataGridViewBindingCompleteEventArgs e) =>
			{
				hideEmptyRows();
			};
			grid.RowsAdded += (object s, DataGridViewRowsAddedEventArgs e) =>
			{
				if (grid.IsHandleCreated)
				{
					try
					{
						grid.BeginInvoke((MethodInvoker)(() =>
						{
							hideEmptyRows();
						}));
						return;
					}
					catch
					{
						hideEmptyRows();
						return;
					}
				}
				hideEmptyRows();
			};
		}
		if (string.Equals(gridField, "_purgeGrid", StringComparison.Ordinal))
		{
			var array = new[]
			{
				new
				{
					Key = "Name",
					Index = 1
				},
				new
				{
					Key = "Kind",
					Index = 2
				},
				new
				{
					Key = "Id",
					Index = 3
				},
				new
				{
					Key = "Reason",
					Index = 4
				},
				new
				{
					Key = "Category",
					Index = 5
				}
			};
			foreach (var anon in array)
			{
				if (grid.Columns.Contains(anon.Key))
				{
					grid.Columns[anon.Key].DisplayIndex = anon.Index;
				}
			}
			if (grid.Columns.Contains("Pick"))
			{
				grid.Columns["Pick"].DisplayIndex = 0;
			}
			if (grid.Columns.Contains("Pick"))
			{
				grid.Columns["Pick"].HeaderText = "Delete";
			}
			if (grid.Columns.Contains("Reason"))
			{
				grid.Columns["Reason"].HeaderText = "Review note";
			}
		}
		grid.MultiSelect = true;
		grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
		List<DataGridViewCheckBoxColumn> checks = grid.Columns.OfType<DataGridViewCheckBoxColumn>().ToList();
		if (checks.Count == 0)
		{
			return;
		}
		grid.ReadOnly = false;
		foreach (DataGridViewColumn column2 in grid.Columns)
		{
			column2.ReadOnly = !(column2 is DataGridViewCheckBoxColumn);
		}
		foreach (DataGridViewCheckBoxColumn item3 in checks)
		{
			item3.ThreeState = false;
			item3.ValueType = typeof(bool);
			item3.TrueValue = true;
			item3.FalseValue = false;
			item3.IndeterminateValue = false;
			DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn = item3;
			string headerText;
			if (gridField == "_purgeGrid")
			{
				headerText = "Delete";
			}
			else
			{
				headerText = ((gridField == "_reviewGrid") ? "Delete" : "Use");
			}
			dataGridViewCheckBoxColumn.HeaderText = headerText;
			int num2 = ((gridField == "_reviewGrid") ? 80 : 66);
			item3.Width = Math.Max(num2, Scale(grid, num2));
			item3.MinimumWidth = Math.Max(num2, Scale(grid, num2));
			item3.Resizable = DataGridViewTriState.False;
			item3.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
			item3.ReadOnly = gridField == "_purgeGrid";
		}
		if (gridField == "_reviewGrid" || gridField == "_purgeGrid")
		{
			FitCleanerGridHeaders(grid);
		}
		DataGridViewColumn idColumn = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault((DataGridViewColumn c) => string.Equals(c.Name, "Id", StringComparison.OrdinalIgnoreCase) || string.Equals(c.DataPropertyName, "Id", StringComparison.OrdinalIgnoreCase));
		if (gridField == "_purgeGrid" && !filledPickBoxHandlers.TryGetValue(grid, out value))
		{
			filledPickBoxHandlers.Add(grid, new object());
			foreach (DataGridViewCheckBoxColumn item4 in checks)
			{
				item4.ReadOnly = true;
			}
			grid.CellPainting += (object s, DataGridViewCellPaintingEventArgs e) =>
			{
				if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && checks.Any((DataGridViewCheckBoxColumn c) => c.Index == e.ColumnIndex))
				{
					e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border | DataGridViewPaintParts.SelectionBackground);
					DataGridViewRow dataGridViewRow2 = grid.Rows[e.RowIndex];
					bool fallback = false;
					try
					{
						fallback = Convert.ToBoolean(dataGridViewRow2.Cells[e.ColumnIndex].Value);
					}
					catch
					{
					}
					string id = ((idColumn == null) ? null : Convert.ToString(dataGridViewRow2.Cells[idColumn.Index].Value));
					bool flag = ReadGridCheckedState(owner, gridField, id, fallback);
					int num3 = Math.Max(13, Scale(grid, 15));
					Rectangle rect = new Rectangle(e.CellBounds.Left + (e.CellBounds.Width - num3) / 2, e.CellBounds.Top + (e.CellBounds.Height - num3) / 2, num3, num3);
					using (SolidBrush brush = new SolidBrush(flag ? Color.FromArgb(217, 169, 51) : Color.White))
					{
						e.Graphics.FillRectangle(brush, rect);
					}
					using (Pen pen = new Pen(Color.FromArgb(118, 126, 134)))
					{
						e.Graphics.DrawRectangle(pen, rect);
					}
					if (flag)
					{
						using Pen pen2 = new Pen(Color.White, Math.Max(1.5f, Scale(grid, 2)));
						pen2.StartCap = LineCap.Round;
						pen2.EndCap = LineCap.Round;
						e.Graphics.DrawLine(pen2, rect.Left + Scale(grid, 4), rect.Top + Scale(grid, 8), rect.Left + Scale(grid, 7), rect.Top + Scale(grid, 11));
						e.Graphics.DrawLine(pen2, rect.Left + Scale(grid, 7), rect.Top + Scale(grid, 11), rect.Left + Scale(grid, 12), rect.Top + Scale(grid, 4));
					}
					e.Handled = true;
				}
			};
		}
		int rangeAnchor = -1;
		int[] lastMultiSelection = Array.Empty<int>();
		int[] pendingCheckboxSelection = Array.Empty<int>();
		bool applyingCheckboxGroup = false;
		grid.SelectionChanged += (object s, EventArgs e) =>
		{
			int[] selectedRows = grid.SelectedRows.Cast<DataGridViewRow>().Select((DataGridViewRow r) => r.Index).ToArray();
			if (selectedRows.Length > 1)
			{
				lastMultiSelection = selectedRows;
			}
		};
		grid.MouseDown += (object s, MouseEventArgs e) =>
		{
			if (e.Button != MouseButtons.Left)
			{
				return;
			}
			DataGridView.HitTestInfo hit = grid.HitTest(e.X, e.Y);
			bool onCheckbox = hit.RowIndex >= 0 && hit.ColumnIndex >= 0 && checks.Any((DataGridViewCheckBoxColumn c) => c.Index == hit.ColumnIndex);
			if (!onCheckbox)
			{
				lastMultiSelection = Array.Empty<int>();
				pendingCheckboxSelection = Array.Empty<int>();
				return;
			}
			int[] selectedRows = grid.SelectedRows.Cast<DataGridViewRow>().Select((DataGridViewRow r) => r.Index).ToArray();
			if (selectedRows.Length > 1 && selectedRows.Contains(hit.RowIndex))
			{
				pendingCheckboxSelection = selectedRows;
			}
			else if (lastMultiSelection.Length > 1 && lastMultiSelection.Contains(hit.RowIndex))
			{
				pendingCheckboxSelection = lastMultiSelection;
			}
			else
			{
				pendingCheckboxSelection = new[] { hit.RowIndex };
			}
		};
		grid.CurrentCellDirtyStateChanged += (object s, EventArgs e) =>
		{
			if (grid.IsCurrentCellDirty && grid.CurrentCell != null && checks.Any((DataGridViewCheckBoxColumn c) => c.Index == grid.CurrentCell.ColumnIndex))
			{
				grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
			}
		};
		grid.CellClick += (object s, DataGridViewCellEventArgs e) =>
		{
			if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
			{
				if (checks.Any((DataGridViewCheckBoxColumn c) => c.Index == e.ColumnIndex) && gridField == "_purgeGrid")
				{
					DataGridViewRow dataGridViewRow2 = grid.Rows[e.RowIndex];
					DataGridViewCell dataGridViewCell = dataGridViewRow2.Cells[e.ColumnIndex];
					bool fallback = false;
					try
					{
						fallback = Convert.ToBoolean(dataGridViewCell.Value);
					}
					catch
					{
					}
					string id = ((idColumn == null) ? null : Convert.ToString(dataGridViewRow2.Cells[idColumn.Index].Value));
					bool flag = ReadGridCheckedState(owner, gridField, id, fallback);
					bool flag2 = !flag;
					int[] targets = ((Control.ModifierKeys & Keys.Shift) == Keys.Shift && rangeAnchor >= 0)
						? Enumerable.Range(Math.Min(rangeAnchor, e.RowIndex), Math.Abs(rangeAnchor - e.RowIndex) + 1).ToArray()
						: (pendingCheckboxSelection.Length > 1 && pendingCheckboxSelection.Contains(e.RowIndex)
							? pendingCheckboxSelection
							: ((grid.SelectedRows.Count > 1 && grid.SelectedRows.Cast<DataGridViewRow>().Any((DataGridViewRow r) => r.Index == e.RowIndex))
								? grid.SelectedRows.Cast<DataGridViewRow>().Select((DataGridViewRow r) => r.Index).ToArray()
								: new[] { e.RowIndex }));
					pendingCheckboxSelection = Array.Empty<int>();
					applyingCheckboxGroup = true;
					grid.SuspendLayout();
					try
					{
						foreach (int rowIndex in targets)
						{
							if (rowIndex >= 0 && rowIndex < grid.Rows.Count)
							{
								DataGridViewRow targetRow = grid.Rows[rowIndex];
								if (targetRow.IsNewRow || !targetRow.Visible)
								{
									continue;
								}
								if (Convert.ToBoolean(targetRow.Cells[e.ColumnIndex].Value ?? false) != flag2)
								{
									targetRow.Cells[e.ColumnIndex].Value = flag2;
								}
								if (idColumn != null)
								{
									SetSelectedId(owner, gridField, Convert.ToString(targetRow.Cells[idColumn.Index].Value), flag2);
								}
							}
						}
					}
					finally
					{
						grid.ResumeLayout(performLayout: true);
						applyingCheckboxGroup = false;
					}
					rangeAnchor = e.RowIndex;
					grid.InvalidateColumn(e.ColumnIndex);
					RefreshCleanerSelection(owner);
				}
				else if (!checks.Any((DataGridViewCheckBoxColumn c) => c.Index == e.ColumnIndex) && (Control.ModifierKeys & Keys.Shift) == Keys.Shift && rangeAnchor >= 0)
				{
					grid.ClearSelection();
					for (int num6 = Math.Min(rangeAnchor, e.RowIndex); num6 <= Math.Max(rangeAnchor, e.RowIndex); num6++)
					{
						if (grid.Rows[num6].Visible)
						{
							grid.Rows[num6].Selected = true;
						}
					}
				}
				else if (!checks.Any((DataGridViewCheckBoxColumn c) => c.Index == e.ColumnIndex))
				{
					rangeAnchor = e.RowIndex;
				}
			}
		};
		grid.CellValueChanged += (object s, DataGridViewCellEventArgs e) =>
		{
			if (!applyingCheckboxGroup && e.RowIndex >= 0 && e.ColumnIndex >= 0 && checks.Any((DataGridViewCheckBoxColumn c) => c.Index == e.ColumnIndex) && idColumn != null)
			{
				DataGridViewRow dataGridViewRow2 = grid.Rows[e.RowIndex];
				if (!dataGridViewRow2.IsNewRow)
				{
					string id = Convert.ToString(dataGridViewRow2.Cells[idColumn.Index].Value);
					bool selected = false;
					try
					{
						selected = Convert.ToBoolean(dataGridViewRow2.Cells[e.ColumnIndex].Value);
					}
					catch
					{
					}
					if (SetSelectedId(owner, gridField, id, selected))
					{
						RefreshCleanerSelection(owner);
					}
				}
			}
		};
		grid.KeyDown += (object s, KeyEventArgs e) =>
		{
			if (e.KeyCode == Keys.Space && grid.CurrentCell != null && checks.Any((DataGridViewCheckBoxColumn c) => c.Index == grid.CurrentCell.ColumnIndex) && grid.CurrentCell.RowIndex >= 0)
			{
				DataGridViewRow dataGridViewRow2 = grid.Rows[grid.CurrentCell.RowIndex];
				bool fallback = false;
				try
				{
					fallback = Convert.ToBoolean(grid.CurrentCell.Value);
				}
				catch
				{
				}
				string id = ((idColumn == null) ? null : Convert.ToString(dataGridViewRow2.Cells[idColumn.Index].Value));
				bool flag = !ReadGridCheckedState(owner, gridField, id, fallback);
				grid.CurrentCell.Value = flag;
				if (idColumn != null)
				{
					SetSelectedId(owner, gridField, id, flag);
				}
				grid.InvalidateCell(grid.CurrentCell);
				RefreshCleanerSelection(owner);
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		};
	}

	private static void FitCleanerGridHeaders(DataGridView grid)
	{
		try
		{
			if (grid == null)
			{
				return;
			}
			Font font = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
			foreach (DataGridViewColumn column in grid.Columns)
			{
				string text = column.HeaderText ?? string.Empty;
				int value;
				if (text.Equals("Delete", StringComparison.OrdinalIgnoreCase) || text.Equals("Select", StringComparison.OrdinalIgnoreCase))
				{
					value = 80;
				}
				else if (text.Equals("Name", StringComparison.OrdinalIgnoreCase))
				{
					value = 145;
				}
				else if (text.Equals("Type", StringComparison.OrdinalIgnoreCase))
				{
					value = 82;
				}
				else if (text.Equals("Element ID", StringComparison.OrdinalIgnoreCase))
				{
					value = 100;
				}
				else if (text.Equals("Review note", StringComparison.OrdinalIgnoreCase))
				{
					value = 125;
				}
				else
				{
					value = (text.Equals("Category", StringComparison.OrdinalIgnoreCase) ? 95 : 72);
				}
				int val = TextRenderer.MeasureText(text, font).Width + Scale(grid, 24);
				column.MinimumWidth = Math.Max(column.MinimumWidth, Math.Max(Scale(grid, value), val));
				column.Resizable = ((!(column is DataGridViewCheckBoxColumn)) ? DataGridViewTriState.True : DataGridViewTriState.False);
				if (column is DataGridViewCheckBoxColumn)
				{
					column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
					column.Width = column.MinimumWidth;
				}
				else
				{
					column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
					DataGridViewColumn dataGridViewColumn2 = column;
					float fillWeight;
					if (text.Equals("Name", StringComparison.OrdinalIgnoreCase))
					{
						fillWeight = 22f;
					}
					else if (text.Equals("Type", StringComparison.OrdinalIgnoreCase))
					{
						fillWeight = 10f;
					}
					else if (text.Equals("Element ID", StringComparison.OrdinalIgnoreCase))
					{
						fillWeight = 11f;
					}
					else if (text.Equals("Review note", StringComparison.OrdinalIgnoreCase))
					{
						fillWeight = 27f;
					}
					else
					{
						fillWeight = (text.Equals("Category", StringComparison.OrdinalIgnoreCase) ? 14f : 10f);
					}
					dataGridViewColumn2.FillWeight = fillWeight;
				}
			}
			grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
			grid.PerformLayout();
			grid.Invalidate();
		}
		catch
		{
		}
	}

	private static void FitReviewCleanGridHeaders(Form form)
	{
		try
		{
			foreach (DataGridView item in All(form).OfType<DataGridView>())
			{
				if (item.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => string.Equals(c.HeaderText, "Delete", StringComparison.OrdinalIgnoreCase)) && item.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => string.Equals(c.HeaderText, "Element ID", StringComparison.OrdinalIgnoreCase)) && item.Columns.Cast<DataGridViewColumn>().Any((DataGridViewColumn c) => string.Equals(c.HeaderText, "Review note", StringComparison.OrdinalIgnoreCase)))
				{
					FitCleanerGridHeaders(item);
				}
			}
		}
		catch
		{
		}
	}

	private static bool SelectedIdExists(object owner, string gridField, string id)
	{
		try
		{
			if (owner == null || string.IsNullOrWhiteSpace(id))
			{
				return false;
			}
			string text;
			if (gridField == "_purgeGrid")
			{
				text = "_selectedPurgeIds";
			}
			else
			{
				text = ((gridField == "_reviewGrid") ? "_selectedReviewIds" : null);
			}
			if (text == null)
			{
				return false;
			}
			if (!(owner.GetType().GetField(text, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) is IEnumerable enumerable))
			{
				return false;
			}
			foreach (object item in enumerable)
			{
				if (item != null)
				{
					if (string.Equals(Convert.ToString(item), id, StringComparison.Ordinal))
					{
						return true;
					}
					object obj = item.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(item, null) ?? item.GetType().GetProperty("IntegerValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(item, null);
					if (obj != null && string.Equals(Convert.ToString(obj), id, StringComparison.Ordinal))
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static bool ReadGridCheckedState(object owner, string gridField, string id, bool fallback)
	{
		try
		{
			if (owner == null || string.IsNullOrWhiteSpace(id))
			{
				return fallback;
			}
			string text;
			if (gridField == "_purgeGrid")
			{
				text = "_selectedPurgeIds";
			}
			else
			{
				text = ((gridField == "_reviewGrid") ? "_selectedReviewIds" : null);
			}
			if (((text == null) ? null : owner.GetType().GetField(text, BindingFlags.Instance | BindingFlags.NonPublic))?.GetValue(owner) is IEnumerable)
			{
				return SelectedIdExists(owner, gridField, id);
			}
		}
		catch
		{
		}
		return fallback;
	}

	private static bool SetSelectedId(object owner, string gridField, string id, bool selected)
	{
		try
		{
			if (owner == null || !long.TryParse(id, out var result))
			{
				return false;
			}
			string text;
			if (gridField == "_purgeGrid")
			{
				text = "_selectedPurgeIds";
			}
			else
			{
				text = ((gridField == "_reviewGrid") ? "_selectedReviewIds" : null);
			}
			object obj = ((text == null) ? null : owner.GetType().GetField(text, BindingFlags.Instance | BindingFlags.NonPublic))?.GetValue(owner);
			if (obj == null)
			{
				return false;
			}
			Type type = obj.GetType().GetGenericArguments().FirstOrDefault();
			if (type == null)
			{
				return false;
			}
			object obj2;
			if (type == typeof(int))
			{
				obj2 = (int)result;
			}
			else if (type == typeof(long))
			{
				obj2 = result;
			}
			else
			{
				ConstructorInfo constructorInfo = type.GetConstructor(new Type[1] { typeof(long) }) ?? type.GetConstructor(new Type[1] { typeof(int) });
				if (constructorInfo == null)
				{
					return false;
				}
				obj2 = ((constructorInfo.GetParameters()[0].ParameterType == typeof(long)) ? constructorInfo.Invoke(new object[1] { result }) : constructorInfo.Invoke(new object[1] { (int)result }));
			}
			MethodInfo method = obj.GetType().GetMethod(selected ? "Add" : "Remove", new Type[1] { type });
			if (method == null)
			{
				return false;
			}
			method.Invoke(obj, new object[1] { obj2 });
			return SelectedIdExists(owner, gridField, id) == selected;
		}
		catch
		{
			return false;
		}
	}

	private static void RefreshCleanerSelection(object owner)
	{
		try
		{
			owner?.GetType().GetMethod("UpdateCleanEnabled", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(owner, null);
		}
		catch
		{
		}
	}

	private static void EnableCheckLists(Form form)
	{
		foreach (CheckedListBox item in All(form).OfType<CheckedListBox>())
		{
			item.Enabled = true;
			item.CheckOnClick = true;
			item.SelectionMode = SelectionMode.MultiExtended;
		}
		foreach (ListView item2 in All(form).OfType<ListView>())
		{
			if (item2.CheckBoxes)
			{
				item2.Enabled = true;
				item2.MultiSelect = true;
			}
		}
	}

	private static void EnableFilters(object owner)
	{
		Label label = Get<Label>(owner, "_status");
		bool flag = label == null || label.Text.StartsWith("Ready", StringComparison.OrdinalIgnoreCase) || label.Text.StartsWith("No scan results", StringComparison.OrdinalIgnoreCase);
		string[] array = new string[4] { "_kindFilter", "_categoryFilter", "_candidateSearch", "_reviewSearch" };
		foreach (string text in array)
		{
			Control control = Get<Control>(owner, text);
			if (control == null)
			{
				continue;
			}
			control.Enabled = true;
			control.Visible = true;
			if (control is TextBox box)
			{
				SetCue(box, "Search category, item name, type or ID");
			}
			if (!(control is ComboBox comboBox))
			{
				continue;
			}
			comboBox.DrawMode = DrawMode.Normal;
			comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
			comboBox.FlatStyle = FlatStyle.Flat;
			comboBox.BackColor = Color.FromArgb(255, 239, 190);
			comboBox.ForeColor = Color.FromArgb(50, 58, 66);
			comboBox.DropDownHeight = 360;
			comboBox.MaxDropDownItems = 18;
			comboBox.IntegralHeight = false;
			if (comboBox.Items.Count == 0)
			{
				if (text == "_kindFilter")
				{
					comboBox.Items.Add("All types");
				}
				else if (text == "_categoryFilter")
				{
					comboBox.Items.Add("All categories");
				}
			}
			if ((text == "_kindFilter") & flag)
			{
				string[] array2 = new string[5] { "Family / Type", "View", "Line Pattern", "Fill Pattern", "Material" };
				foreach (string kind in array2)
				{
					if (!comboBox.Items.Cast<object>().Any((object item) => string.Equals(item?.ToString() ?? "", kind, StringComparison.OrdinalIgnoreCase)))
					{
						comboBox.Items.Add(kind);
					}
				}
			}
			if (comboBox.Items.Count > 0 && comboBox.SelectedIndex < 0)
			{
				comboBox.SelectedIndex = 0;
			}
			comboBox.BringToFront();
		}
	}

	private static void ThemeTabs(object owner)
	{
		TabControl tabs = Get<TabControl>(owner, "_tabs");
		if (tabs == null)
		{
			return;
		}
		if (!themedTabStates.TryGetValue(tabs, out var _))
		{
			themedTabStates.Add(tabs, new object());
			themedTabs = tabs;
			tabs.DrawItem += (object s, DrawItemEventArgs e) =>
			{
				TabControl tabControl = (TabControl)s;
				if (e.Index < 0 || e.Index >= tabControl.TabPages.Count)
				{
					return;
				}
				Rectangle bounds = e.Bounds;
				Color color = Color.FromArgb(255, 239, 190);
				using (SolidBrush brush = new SolidBrush(color))
				{
					e.Graphics.FillRectangle(brush, bounds);
				}
				using (Pen pen = new Pen(Color.FromArgb(198, 151, 42)))
				{
					e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
				}
				using Font font = new Font(tabControl.Font.FontFamily, tabControl.Font.Size, (e.Index == tabControl.SelectedIndex) ? FontStyle.Bold : FontStyle.Regular);
				TextRenderer.DrawText(e.Graphics, tabControl.TabPages[e.Index].Text, font, bounds, Color.FromArgb(67, 75, 82), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
			};
			tabs.SelectedIndexChanged += (object s, EventArgs e) =>
			{
				tabs.Invalidate();
			};
		}
		if (tabs.DrawMode != TabDrawMode.OwnerDrawFixed)
		{
			tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
		}
		if (tabs.Appearance != TabAppearance.Normal)
		{
			tabs.Appearance = TabAppearance.Normal;
		}
		tabs.SizeMode = TabSizeMode.Fixed;
		tabs.Multiline = false;
		tabs.Visible = true;
		Size size = new Size(Math.Max(100, tabs.ItemSize.Width), Math.Max(28, tabs.ItemSize.Height));
		if (tabs.ItemSize != size)
		{
			tabs.ItemSize = size;
		}
		tabs.Invalidate();
	}

	public static void Refresh(object owner)
	{
		try
		{
			Label label = Get<Label>(owner, "_status");
			if (label == null)
			{
				return;
			}
			bool flag = label.Text.StartsWith("Scan complete", StringComparison.OrdinalIgnoreCase) || label.Text.StartsWith("Scan failed", StringComparison.OrdinalIgnoreCase);
			if (label.Text.StartsWith("Scanning", StringComparison.OrdinalIgnoreCase))
			{
				foreach (State state in states)
				{
					if (state.Owner == owner)
					{
						state.Grid.Visible = true;
						state.Overlay.Visible = false;
					}
				}
				EnableFilters(owner);
				return;
			}
			foreach (State state2 in states)
			{
				if (state2.Owner != owner)
				{
					continue;
				}
				if (flag && state2.GridField == "_purgeGrid" && !state2.InitialSelectionNormalized)
				{
					ClearInitialPurgeSelection(owner, state2.Grid);
					state2.InitialSelectionNormalized = true;
				}
				SyncGridChecksFromSelection(owner, state2);
				int num = 0;
				int num2 = 0;
				foreach (DataGridViewRow item in (IEnumerable)state2.Grid.Rows)
				{
					if (!item.IsNewRow)
					{
						num++;
						if (item.Visible)
						{
							num2++;
						}
					}
				}
				state2.Grid.Visible = true;
				if (num2 > 0)
				{
					state2.Overlay.Visible = false;
					continue;
				}
				Label message = state2.Message;
				string text;
				if (num > 0)
				{
					text = "No rows match the selected filters. Choose All types and All categories, or clear Search.";
				}
				else if (label.Text.StartsWith("Scan failed", StringComparison.OrdinalIgnoreCase))
				{
					text = "The scan could not finish. Review the status below, then try again.";
				}
				else if (flag)
				{
					text = ((state2.GridField == "_warningsGrid") ? "Scan complete. Revit reported no model warnings." : "Scan complete. Revit reported no unused items for this model.");
				}
				else
				{
					text = "No scan results yet. Click Scan Model to check this project.";
				}
				message.Text = text;
				state2.Run.Visible = num == 0;
				state2.Run.Text = (flag ? "Scan Again" : "Scan Model");
				if (state2.Grid.Parent is Panel panel)
				{
					int num3 = (state2.Grid.ColumnHeadersVisible ? state2.Grid.ColumnHeadersHeight : 0);
					state2.Overlay.SetBounds(0, num3, panel.ClientSize.Width, Math.Max(0, panel.ClientSize.Height - num3));
				}
				state2.Overlay.Visible = true;
				state2.Overlay.BringToFront();
			}
			EnableFilters(owner);
		}
		catch
		{
		}
	}

	private static void ClearInitialPurgeSelection(object owner, DataGridView grid)
	{
		try
		{
			FieldInfo field = owner.GetType().GetField("_selectedPurgeIds", BindingFlags.Instance | BindingFlags.NonPublic);
			(field?.GetValue(owner) as IList)?.Clear();
			object obj = field?.GetValue(owner);
			obj?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(obj, null);
			if (grid != null && grid.Columns.Contains("Pick"))
			{
				foreach (DataGridViewRow item in (IEnumerable)grid.Rows)
				{
					if (!item.IsNewRow)
					{
						if (item.Cells["Pick"].Value is bool isChecked && isChecked)
                        {
                            item.Cells["Pick"].Value = false;
                        }
					}
				}
			}
			RefreshCleanerSelection(owner);
		}
		catch
		{
		}
	}

	private static void SyncGridChecksFromSelection(object owner, State state)
	{
		try
		{
			if (owner == null || state?.Grid == null || state.Grid.IsDisposed)
			{
				return;
			}
			string text;
			if (state.GridField == "_purgeGrid")
			{
				text = "_selectedPurgeIds";
			}
			else
			{
				text = ((state.GridField == "_reviewGrid") ? "_selectedReviewIds" : null);
			}
			if (text == null || !(owner.GetType().GetField(text, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) is IEnumerable enumerable))
			{
				return;
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (object item in enumerable)
			{
				if (item != null)
				{
					object obj = item.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(item, null) ?? item.GetType().GetProperty("IntegerValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(item, null);
					if (obj != null)
					{
						hashSet.Add(Convert.ToString(obj));
					}
				}
			}
			DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn = state.Grid.Columns.OfType<DataGridViewCheckBoxColumn>().FirstOrDefault();
			DataGridViewColumn dataGridViewColumn = state.Grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault((DataGridViewColumn c) => string.Equals(c.Name, "Id", StringComparison.OrdinalIgnoreCase) || string.Equals(c.DataPropertyName, "Id", StringComparison.OrdinalIgnoreCase));
			if (dataGridViewCheckBoxColumn == null || dataGridViewColumn == null)
			{
				return;
			}
			foreach (DataGridViewRow item2 in (IEnumerable)state.Grid.Rows)
			{
				if (!item2.IsNewRow)
				{
					string text2 = Convert.ToString(item2.Cells[dataGridViewColumn.Index].Value);
					bool flag = text2 != null && hashSet.Contains(text2);
					DataGridViewCell dataGridViewCell = item2.Cells[dataGridViewCheckBoxColumn.Index];
					bool flag2 = false;
					try
					{
						flag2 = Convert.ToBoolean(dataGridViewCell.Value);
					}
					catch
					{
					}
					if (flag2 != flag)
					{
						dataGridViewCell.Value = flag;
					}
				}
			}
		}
		catch
		{
		}
	}

	private static int CountRows(DataGridView grid)
	{
		int num = 0;
		foreach (DataGridViewRow item in (IEnumerable)grid.Rows)
		{
			if (!item.IsNewRow)
			{
				num++;
			}
		}
		return num;
	}

	private static int Scale(Control control, int value)
	{
		int num = 96;
		try
		{
			if (control != null && control.DeviceDpi > 0)
			{
				num = control.DeviceDpi;
			}
		}
		catch
		{
		}
		return Math.Max(1, (int)Math.Round((double)(value * num) / 96.0));
	}

	private static T Get<T>(object owner, string name) where T : class
	{
		try
		{
			FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
			return (field == null) ? null : (field.GetValue(owner) as T);
		}
		catch
		{
			return null;
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







