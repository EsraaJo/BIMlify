using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using OperationCanceledException = Autodesk.Revit.Exceptions.OperationCanceledException;
using BIMlify.CategorySelectSupport;
using BIMlify.ClashLinked;
using BIMlify.CleanerUi;
using BIMlify.ModelCleaner;
using BIMlify.ReviewRunGate;
using BIMlify.SmartOpenings;

namespace BIMlify;

internal static class BimActions
{
	private struct NativeRect
	{
		internal int Left;

		internal int Top;

		internal int Right;

		internal int Bottom;
	}

	private sealed class BIMlifyForm : Form
	{
		private ResizeGripControl resizeGrip;

		internal bool NativeRoundedCorners { get; set; }

		internal bool ForceTaskbarButton { get; set; }

		internal bool AllowResize { get; set; }


		protected override CreateParams CreateParams
		{
			get
			{
				CreateParams createParams = base.CreateParams;
				if (ForceTaskbarButton)
				{
					createParams.ExStyle |= 262144;
					createParams.ExStyle &= -129;
				}
				return createParams;
			}
		}

		internal BIMlifyForm()
		{
			AllowResize = true;
			FormBorderStyle = FormBorderStyle.None;
			Padding = Padding.Empty;
			BackColor = Color.FromArgb(238, 241, 245);
			MinimumSize = new Size(560, 360);
			PaintEventHandler value = (object sender, PaintEventArgs e) =>
			{
				if (WindowState != FormWindowState.Maximized && Width > 20 && Height > 20)
				{
					e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
					if (!NativeRoundedCorners)
					{
						Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);
						using Pen pen = new Pen(Color.FromArgb(132, 141, 151), 1f);
						using GraphicsPath path = RoundedPath(rect, 12);
						e.Graphics.DrawPath(pen, path);
					}
				}
											
			};
			Paint += value;
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			EmptyState.NormalizeWindowFooter(this);
			UiPolish.ApplyGlobalTabStyle(this);
			if (AllowResize && resizeGrip == null)
			{
				resizeGrip = new ResizeGripControl(this);
				Controls.Add(resizeGrip);
				resizeGrip.BringToFront();
			}
			if (resizeGrip != null)
			{
				resizeGrip.Visible = AllowResize && WindowState != FormWindowState.Maximized;
				EmptyState.NormalizeWindowFooter(this);
				EmptyState.NormalizeSharedSidebar(this);
			}
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (resizeGrip != null)
			{
				resizeGrip.Visible = AllowResize && WindowState != FormWindowState.Maximized;
			}
		}

		protected override void WndProc(ref Message m)
		{
			if (AllowResize && m.Msg == 132 && WindowState != FormWindowState.Maximized)
			{
				base.WndProc(ref m);
				if (m.Result.ToInt32() == 1)
				{
					long num = m.LParam.ToInt64();
					int num2 = (short)(num & 0xFFFF);
					int num3 = (short)((num >> 16) & 0xFFFF);
					Point point = PointToClient(new Point(num2, num3));
					bool flag = point.X <= 12;
					bool flag2 = point.X >= ClientSize.Width - 12;
					bool flag3 = point.Y <= 12;
					bool flag4 = point.Y >= ClientSize.Height - 12;
					if (flag3 && flag)
					{
						m.Result = (IntPtr)13;
					}
					else if (flag3 && flag2)
					{
						m.Result = (IntPtr)14;
					}
					else if (flag4 && flag)
					{
						m.Result = (IntPtr)16;
					}
					else if (flag4 && flag2)
					{
						m.Result = (IntPtr)17;
					}
					else if (flag)
					{
						m.Result = (IntPtr)10;
					}
					else if (flag2)
					{
						m.Result = (IntPtr)11;
					}
					else if (flag3)
					{
						m.Result = (IntPtr)12;
					}
					else if (flag4)
					{
						m.Result = (IntPtr)15;
					}
				}
			}
			else
			{
				base.WndProc(ref m);
			}
		}
	}

	private sealed class ResizeGripControl : Control
	{
		private readonly BIMlifyForm owner;

		internal ResizeGripControl(BIMlifyForm form)
		{
			owner = form;
			Width = 16;
			Height = 16;
			Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
			Left = Math.Max(0, form.ClientSize.Width - Width - 2);
			Top = Math.Max(0, form.ClientSize.Height - Height - 2);
			Cursor = Cursors.SizeNWSE;
			AccessibleName = "Resize window";
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = owner.BackColor;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Color color = owner.BackColor;
			Point pt = new Point(owner.ClientSize.Width - 4, owner.ClientSize.Height - 4);
			for (int num = owner.Controls.Count - 1; num >= 0; num--)
			{
				Control control = owner.Controls[num];
				if (control != this && control.Visible && control.Bounds.Contains(pt))
				{
					Color backColor = control.BackColor;
					if (backColor.A == byte.MaxValue)
					{
						color = backColor;
						break;
					}
				}
			}
			BackColor = color;
			using (SolidBrush brush = new SolidBrush(color))
			{
				e.Graphics.FillRectangle(brush, ClientRectangle);
			}
			using Pen pen = new Pen(Color.FromArgb(115, 158, 166), 1f);
			LineCap startCap = (pen.EndCap = LineCap.Round);
			pen.StartCap = startCap;
			for (int i = 0; i < 2; i++)
			{
				float num2 = 4f + (float)i * 5f;
				float x = (float)Width - num2 - 2f;
				float y = (float)Height - 3f;
				float x2 = (float)Width - 3f;
				float y2 = (float)Height - num2 - 2f;
				e.Graphics.DrawLine(pen, x, y, x2, y2);
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				ReleaseCapture();
				SendMessage(owner.Handle, 161, (IntPtr)17, IntPtr.Zero);
			}
			base.OnMouseDown(e);
		}
	}

	private sealed class CaptionButton : Control
	{
		private readonly int kind;

		private readonly BIMlifyForm owner;

		private readonly Color surface;

		private bool hovered;

		private bool pressed;

		internal CaptionButton(BIMlifyForm form, int buttonKind, Color surfaceColor)
		{
			owner = form;
			kind = buttonKind;
			surface = surfaceColor;
			SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			Width = 32;
			Height = 32;
			Margin = Padding.Empty;
			Cursor = Cursors.Hand;
			BackColor = surfaceColor;
			BIMlifyForm bIMlifyForm = owner;
			EventHandler value = (object param0, EventArgs param1) =>
			{
				Invalidate();
			};
			bIMlifyForm.Resize += value;
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			using SolidBrush brush = new SolidBrush(surface);
			e.Graphics.FillRectangle(brush, ClientRectangle);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			hovered = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			hovered = false;
			pressed = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				pressed = true;
			}
			Invalidate();
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			pressed = false;
			Invalidate();
			base.OnMouseUp(e);
		}

		protected override void OnClick(EventArgs e)
		{
			base.OnClick(e);
			LinkedClashSupport.HandleCaptionClick(this);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			if (Width < 8 || Height < 8)
			{
				return;
			}
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle rect = new Rectangle(3, 4, Width - 7, Height - 8);
			Color color;
			if (kind == 2 && hovered)
			{
				color = BlendColor(surface, Color.FromArgb(215, 112, 78, 78), 0.32f);
			}
			else
			{
				color = (pressed ? BlendColor(surface, Color.FromArgb(65, 75, 84), 0.1f) : surface);
			}
			using (SolidBrush brush = new SolidBrush(surface))
			{
				e.Graphics.FillRectangle(brush, ClientRectangle);
			}
			using (GraphicsPath path = RoundedPath(rect, 7))
			{
				using SolidBrush brush2 = new SolidBrush(color);
				e.Graphics.FillPath(brush2, path);
			}

			if (kind == 0 || kind == 1 || kind == 2)
			{
				using GraphicsPath outline = RoundedPath(rect, 7);
				using Pen pen = new Pen(Color.FromArgb(hovered ? 245 : 220, 112, 124, 136), 1.1f);
				e.Graphics.DrawPath(pen, outline);
			}
			using Pen pen2 = new Pen(Color.FromArgb(78, 85, 92), 1.5f);
			float num = (float)Width / 2f;
			float num2 = (float)Height / 2f;
			pen2.StartCap = LineCap.Round;
			pen2.EndCap = LineCap.Round;
			if (kind == 0)
			{
				e.Graphics.DrawLine(pen2, num - 5f, num2, num + 5f, num2);
			}
			else if (kind == 1 && owner.WindowState == FormWindowState.Maximized)
			{
				e.Graphics.DrawRectangle(pen2, num - 5f, num2 - 3f, 9f, 8f);
				e.Graphics.DrawLine(pen2, num - 3f, num2 - 5f, num + 5f, num2 - 5f);
				e.Graphics.DrawLine(pen2, num + 5f, num2 - 5f, num + 5f, num2 + 3f);
			}
			else if (kind == 1)
			{
				e.Graphics.DrawRectangle(pen2, num - 5f, num2 - 5f, 10f, 10f);
			}
			else if (kind == 3)
			{
				e.Graphics.DrawRectangle(pen2, num - 6f, num2 - 4f, 10f, 9f);
				e.Graphics.DrawLine(pen2, num - 3f, num2 - 7f, num + 6f, num2 - 7f);
				e.Graphics.DrawLine(pen2, num + 6f, num2 - 7f, num + 6f, num2 + 2f);
			}
			else
			{
				e.Graphics.DrawLine(pen2, num - 4f, num2 - 4f, num + 4f, num2 + 4f);
				e.Graphics.DrawLine(pen2, num + 4f, num2 - 4f, num - 4f, num2 + 4f);
			}
		}
	}

	private struct AccentPolicy
	{
		public int State;

		public int Flags;

		public int GradientColor;

		public int AnimationId;
	}

	private struct WindowCompositionAttributeData
	{
		public int Attribute;

		public IntPtr Data;

		public int SizeOfData;
	}

	private sealed class AdvancedSearchTextBox : TextBox
	{
		internal AdvancedSearchTextBox()
		{
			BorderStyle = BorderStyle.FixedSingle;
			EventHandler value = (object param0, EventArgs param1) =>
			{
				SetSearchMargin();
			};
			HandleCreated += value;
		}

		internal void SetSearchMargin()
		{
			if (!IsHandleCreated)
			{
				return;
			}
			IntPtr lParam = (IntPtr)786457;
			SendMessage(Handle, 211, (IntPtr)1, lParam);
			int num = Math.Max(12, Font.Height);
			int num2 = Math.Max(2, (ClientSize.Height - num) / 2 - 1);
			NativeRect structure = new NativeRect
			{
				Left = 42,
				Top = num2,
				Right = Math.Max(25, ClientSize.Width - 12),
				Bottom = Math.Min(ClientSize.Height - 2, num2 + num + 2)
			};
			IntPtr intPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeRect)));
			try
			{
				Marshal.StructureToPtr(structure, intPtr, fDeleteOld: false);
				SendMessage(Handle, 180, IntPtr.Zero, intPtr);
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr);
			}
			if (ClientSize.Width > 4 && ClientSize.Height > 4)
			{
				using GraphicsPath path = RoundedPath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 8);
				Region region = Region;
				Region = new Region(path);
				region?.Dispose();
			}
			Invalidate();
		}

		protected override void WndProc(ref Message message)
		{
			base.WndProc(ref message);
			if (message.Msg == 15)
			{
				UiPolish.DrawSquareSearchFrame(this);
			}
		}
	}

	private sealed class FitLabelFontState
	{
		public string FamilyName;

		public FontStyle Style;

		public float BaseSize;

		public Font OwnedFont;
	}

	private sealed class NativeWindowOwner : IWin32Window
	{
		private readonly IntPtr handle;

		public IntPtr Handle => handle;

		internal NativeWindowOwner(IntPtr hwnd)
		{
			handle = hwnd;
		}
	}

	private sealed class QuickToolButton : Control
	{
		private readonly string glyph;

		private Image icon;

		private bool hovered;

		private bool pressed;

		private readonly Timer hoverTimer;

		internal QuickToolButton(string text, string path, string specialGlyph)
		{
			glyph = specialGlyph;
			SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Color.FromArgb(214, 219, 225);
			Cursor = Cursors.Hand;
			AccessibleName = text;
			try
			{
				if (!string.IsNullOrEmpty(path) && File.Exists(path))
				{
					icon = LoadQuickBarIcon(path);
				}
			}
			catch
			{
			}
			hoverTimer = new Timer
			{
				Interval = 80
			};
			hoverTimer.Tick += (object param0, EventArgs param1) =>
			{
				if (!IsDisposed)
				{
					Point pt = PointToClient(Cursor.Position);
					if (hovered && !ClientRectangle.Contains(pt))
					{
						hovered = false;
						pressed = false;
						Invalidate();
					}
				}
			};
			hoverTimer.Start();
			Disposed += (object param0, EventArgs param1) =>
			{
				hoverTimer.Stop();
				hoverTimer.Dispose();
				if (icon != null)
				{
					icon.Dispose();
				}
			};
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			hovered = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			hovered = false;
			pressed = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				pressed = true;
			}
			Invalidate();
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			pressed = false;
			Invalidate();
			base.OnMouseUp(e);
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			e.Graphics.Clear(BackColor);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			if (Width < 8 || Height < 8)
			{
				return;
			}
			e.Graphics.Clear(BackColor);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			float num = (float)Width / 2f;
			float num2 = (float)Height / 2f + 11f;
			BIMlify.CleanerUi.QuickBarShadowRenderer.Draw(e.Graphics, num, num2);

			if (hovered)
			{
				using Pen pen = new Pen(Color.FromArgb(90, 214, 171, 61), 1f);
				e.Graphics.DrawEllipse(pen, new RectangleF(num - 21f, num2 - 4f, 42f, 8f));
			}
			e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			Rectangle rect = new Rectangle(0, 1, Width - 1, Height - 3);
			if (hovered || pressed)
			{
				using GraphicsPath path = RoundedPath(rect, 3);
				using SolidBrush brush3 = new SolidBrush(pressed ? Color.FromArgb(237, 225, 194) : Color.FromArgb(255, 247, 226));
				e.Graphics.FillPath(brush3, path);
			}
			if (glyph == "restore")
			{
				using (Pen pen2 = new Pen(Color.FromArgb(214, 171, 61), 2.1f))
				{
					float num3 = (float)Width / 2f;
					float num4 = (float)Height / 2f;
					e.Graphics.DrawRectangle(pen2, num3 - 9f, num4 - 8f, 18f, 15f);
					e.Graphics.DrawLine(pen2, num3 - 5f, num4 - 10f, num3 + 8f, num4 - 10f);
					e.Graphics.DrawLine(pen2, num3 + 8f, num4 - 10f, num3 + 8f, num4 + 3f);
					return;
				}
			}
			if (glyph == "close")
			{
				using (Pen pen2 = new Pen(Color.FromArgb(105, 111, 117), 2f))
				{
					Pen pen3 = pen2;
					LineCap startCap = (pen2.EndCap = LineCap.Round);
					pen3.StartCap = startCap;
					e.Graphics.DrawLine(pen2, (float)Width / 2f - 6f, (float)Height / 2f - 6f, (float)Width / 2f + 6f, (float)Height / 2f + 6f);
					e.Graphics.DrawLine(pen2, (float)Width / 2f + 6f, (float)Height / 2f - 6f, (float)Width / 2f - 6f, (float)Height / 2f + 6f);
					return;
				}
			}
			if (icon == null)
			{
				return;
			}
			int num5 = Math.Min(30, Math.Max(22, Math.Min(Width - 10, Height - 8)));
			if (hovered)
			{
				using SolidBrush brush4 = new SolidBrush(Color.FromArgb(24, 218, 174, 65));
				e.Graphics.FillEllipse(brush4, new Rectangle((Width - num5) / 2 - 4, (Height - num5) / 2 - 4, num5 + 8, num5 + 8));
			}
			e.Graphics.DrawImage(icon, new Rectangle((Width - num5) / 2, (Height - num5) / 2, num5, num5), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel);
		}
	}

	private sealed class RoundedCardPanel : Panel
	{
		internal RoundedCardPanel()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			ApplyRoundedRegion();
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			ApplyRoundedRegion();
		}

		private void ApplyRoundedRegion()
		{
			if (Width < 2 || Height < 2)
			{
				return;
			}
			try
			{
				using GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width, Height), 12);
				Region region = Region;
				Region = new Region(path);
				region?.Dispose();
			}
			catch
			{
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			if (Width < 3 || Height < 3)
			{
				return;
			}
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12);
			using Pen pen = new Pen(Color.FromArgb(224, 226, 224), 1f);
			e.Graphics.DrawPath(pen, path);
		}
	}

	private enum SideNavAction
	{
		Back,
		Home,
		QuickBar,
		Contact,
		Settings,
		Account
	}

	private sealed class BIMlifySidebar : Panel
	{
		private readonly Form owner;

		private readonly bool dashboard;

		private readonly Action<string> launchAction;

		private SideNavAction? hoveredAction;

		private readonly ToolTip toolTip = new ToolTip
		{
			InitialDelay = 450,
			ReshowDelay = 100,
			AutoPopDelay = 4500,
			ShowAlways = true
		};

		private readonly Color surface = Color.FromArgb(246, 240, 222);

		private readonly Color iconColor = Color.FromArgb(86, 94, 101);

		private readonly Color borderColor = Color.FromArgb(220, 216, 203);

		internal BIMlifySidebar(Form owner, bool dashboard, Action<string> launchAction = null)
		{
			this.owner = owner;
			this.dashboard = dashboard;
			this.launchAction = launchAction;
			Width = 58;
			MinimumSize = new Size(58, 0);
			Dock = DockStyle.Left;
			BackColor = surface;
			Padding = Padding.Empty;
			SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			Cursor = Cursors.Hand;
			AccessibleRole = AccessibleRole.ToolBar;
			AccessibleName = "BIMlify navigation";
			MouseEventHandler value = (object sender, MouseEventArgs e) =>
			{
				SideNavAction? sideNavAction = HitTest(e.Location);
				if (sideNavAction != hoveredAction)
				{
					hoveredAction = sideNavAction;
					Invalidate();
					if (sideNavAction.HasValue)
					{
						toolTip.SetToolTip(this, SideNavDescription(sideNavAction.Value));
					}
					else
					{
						toolTip.SetToolTip(this, null);
					}
				}
			};
			MouseMove += value;
			MouseLeave += (object param0, EventArgs param1) =>
			{
				if (hoveredAction.HasValue)
				{
					hoveredAction = null;
					Invalidate();
				}
			};
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			using (SolidBrush brush = new SolidBrush(surface))
			{
				e.Graphics.FillRectangle(brush, ClientRectangle);
			}
			using Pen pen = new Pen(Color.FromArgb(205, 202, 189), 1f);
			e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			using (SolidBrush brush = new SolidBrush(surface))
			{
				e.Graphics.FillRectangle(brush, ClientRectangle);
			}
			using (Pen pen = new Pen(Color.FromArgb(205, 202, 189), 1f))
			{
				e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
			}
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			foreach (SideNavAction item in VisibleActions())
			{
				Rectangle rectangle = ButtonBounds(item);
				bool flag = hoveredAction.HasValue && hoveredAction.Value == item;
				Color color = ((item == SideNavAction.Home || item == SideNavAction.QuickBar || item == SideNavAction.Back) ? Color.FromArgb(255, 252, 242) : surface);
				using (GraphicsPath path = RoundedPath(rectangle, 8))
				{
					using SolidBrush brush2 = new SolidBrush(flag ? Color.FromArgb(255, 249, 231) : color);
					e.Graphics.FillPath(brush2, path);
				}
				Color color2;
				if (flag)
				{
					color2 = Color.FromArgb(214, 171, 61);
				}
				else
				{
					color2 = ((item == SideNavAction.Home || item == SideNavAction.QuickBar || item == SideNavAction.Back) ? Color.FromArgb(214, 171, 61) : borderColor);
				}
				using (GraphicsPath path = RoundedPath(rectangle, 8))
				{
					using Pen pen2 = new Pen(color2, flag ? 1.15f : 0.6f);
					e.Graphics.DrawPath(pen2, path);
				}
				if (item == SideNavAction.Contact || item == SideNavAction.Settings || item == SideNavAction.Account)
				{
					BIMlify.CleanerUi.QuickBarShadowRenderer.DrawSidebarIconShadow(e.Graphics, rectangle);
				}
				GraphicsState gstate = e.Graphics.Save();
				float num = (flag ? 1.14f : 1f);
				e.Graphics.TranslateTransform((float)rectangle.Left + (float)rectangle.Width / 2f, (float)rectangle.Top + (float)rectangle.Height / 2f);
				e.Graphics.ScaleTransform(num, num);
				e.Graphics.TranslateTransform(0f - ((float)rectangle.Left + (float)rectangle.Width / 2f), 0f - ((float)rectangle.Top + (float)rectangle.Height / 2f));
				DrawGlyph(e.Graphics, item, rectangle);
				e.Graphics.Restore(gstate);
			}
		}

		protected override void OnMouseClick(MouseEventArgs e)
		{
			base.OnMouseClick(e);
			if (e.Button != MouseButtons.Left)
			{
				return;
			}
			PlayClickSound();
			SideNavAction? sideNavAction = HitTest(e.Location);
			if (!sideNavAction.HasValue)
			{
				return;
			}
			switch (sideNavAction.Value)
			{
			case SideNavAction.Back:
			case SideNavAction.Home:
				if (!dashboard)
				{
					ReturnToMainMenu = true;
					owner.DialogResult = DialogResult.Cancel;
					owner.Close();
				}
				break;
			case SideNavAction.QuickBar:
				if (launchAction != null)
				{
					launchAction("quickbar");
					break;
				}
				owner.Tag = "quickbar";
				owner.DialogResult = DialogResult.OK;
				owner.Close();
				break;
			case SideNavAction.Contact:
				ShowContactOptions(owner);
				break;
			case SideNavAction.Settings:
				ShowSettings(owner);
				break;
			case SideNavAction.Account:
				ShowAccount(owner);
				break;
			}
		}

		private string SideNavDescription(SideNavAction action)
		{
			return action switch
			{
				SideNavAction.Back => "Back to previous window", 
				SideNavAction.Home => "Home — return to BIMlify tools", 
				SideNavAction.QuickBar => "Quick Bar — drag freely; move near the bottom center to snap into place. The bar stays fully visible.", 
				SideNavAction.Contact => "Contact BIMlify by email or LinkedIn", 
				SideNavAction.Settings => "BIMlify settings", 
				_ => "Account and subscription", 
			};
		}

		private IEnumerable<SideNavAction> VisibleActions()
		{
			// Search is a real child control supplied by DashboardEnhancements. Do not
			// paint Home underneath it on the dashboard. In secondary windows Home takes
			// Back's top slot, while Search occupies the next slot.
			if (!dashboard)
			{
				yield return SideNavAction.Home;
			}
			yield return SideNavAction.Contact;
			yield return SideNavAction.Settings;
			yield return SideNavAction.Account;
		}

		private Rectangle ButtonBounds(SideNavAction action)
		{
			int num = 0;
			return new Rectangle(y: action switch
			{
				SideNavAction.Back => 10, 
				SideNavAction.Home => 10, 
				SideNavAction.QuickBar => 56, 
				SideNavAction.Settings => Math.Max(68, ClientSize.Height - 112), 
				SideNavAction.Contact => Math.Max(18, Math.Max(68, ClientSize.Height - 112) - 48), 
				_ => Math.Max(112, ClientSize.Height - 62), 
			}, x: (ClientSize.Width - 42) / 2, width: 42, height: 42);
		}

		private SideNavAction? HitTest(Point point)
		{
			foreach (SideNavAction item in VisibleActions())
			{
				if (ButtonBounds(item).Contains(point))
				{
					return item;
				}
			}
			return null;
		}

		private void DrawGlyph(Graphics g, SideNavAction action, Rectangle bounds)
		{
			Color color = ((action == SideNavAction.Home || action == SideNavAction.QuickBar || action == SideNavAction.Back) ? Color.FromArgb(214, 171, 61) : iconColor);
			using Pen pen = new Pen(color, 2f);
			LineCap startCap = (pen.EndCap = LineCap.Round);
			pen.StartCap = startCap;
			float num = (float)bounds.Left + (float)bounds.Width / 2f;
			float num2 = (float)bounds.Top + (float)bounds.Height / 2f;
			switch (action)
			{
			case SideNavAction.Back:
				g.DrawLine(pen, num + 2f, num2 - 8f, num - 6f, num2);
				g.DrawLine(pen, num - 6f, num2, num + 2f, num2 + 8f);
				g.DrawLine(pen, num - 6f, num2, num + 6f, num2);
				break;
			case SideNavAction.Home:
				g.DrawLines(pen, new PointF[3]
				{
					new PointF(num - 10f, num2 - 1f),
					new PointF(num, num2 - 10f),
					new PointF(num + 10f, num2 - 1f)
				});
				g.DrawLine(pen, num - 7f, num2 - 2f, num - 7f, num2 + 9f);
				g.DrawLine(pen, num + 7f, num2 - 2f, num + 7f, num2 + 9f);
				g.DrawLine(pen, num - 7f, num2 + 9f, num - 2f, num2 + 9f);
				g.DrawLine(pen, num + 2f, num2 + 9f, num + 7f, num2 + 9f);
				g.DrawLine(pen, num - 2f, num2 + 9f, num - 2f, num2 + 3f);
				g.DrawLine(pen, num + 2f, num2 + 9f, num + 2f, num2 + 3f);
				g.DrawLine(pen, num - 2f, num2 + 3f, num + 2f, num2 + 3f);
				break;
			case SideNavAction.QuickBar:
				g.DrawLine(pen, num - 9f, num2 - 7f, num + 9f, num2 - 7f);
				g.DrawLine(pen, num - 9f, num2, num + 9f, num2);
				g.DrawLine(pen, num - 9f, num2 + 7f, num + 9f, num2 + 7f);
				g.DrawLine(pen, num - 9f, num2 - 10f, num - 9f, num2 - 4f);
				g.DrawLine(pen, num + 9f, num2 - 3f, num + 9f, num2 + 3f);
				g.DrawLine(pen, num - 9f, num2 + 4f, num - 9f, num2 + 10f);
				break;
			case SideNavAction.Settings:
			{
				for (int i = 0; i < 8; i++)
				{
					double num3 = (double)i * Math.PI / 4.0;
					g.DrawLine(pen, num + (float)Math.Cos(num3) * 8f, num2 + (float)Math.Sin(num3) * 8f, num + (float)Math.Cos(num3) * 11f, num2 + (float)Math.Sin(num3) * 11f);
				}
				g.DrawEllipse(pen, num - 8f, num2 - 8f, 16f, 16f);
				g.DrawEllipse(pen, num - 3f, num2 - 3f, 6f, 6f);
				break;
			}
			case SideNavAction.Contact:
				g.DrawRectangle(pen, num - 10f, num2 - 7f, 20f, 14f);
				g.DrawLine(pen, num - 10f, num2 - 6f, num, num2 + 1f);
				g.DrawLine(pen, num, num2 + 1f, num + 10f, num2 - 6f);
				break;
			default:
				g.DrawEllipse(pen, num - 4f, num2 - 10f, 8f, 8f);
				g.DrawArc(pen, num - 10f, num2 - 1f, 20f, 17f, 190f, 160f);
				break;
			}
		}
	}

	private sealed class GlassButton : Button
	{
		private bool primary;

		private bool hovered;

		private bool pressed;

		internal bool Primary
		{
			get
			{
				return primary;
			}
			set
			{
				primary = value;
				Invalidate();
			}
		}

		public GlassButton()
		{
			FlatStyle = FlatStyle.Flat;
			FlatAppearance.BorderSize = 0;
			FlatAppearance.MouseOverBackColor = Color.FromArgb(216, 221, 227);
			FlatAppearance.MouseDownBackColor = Color.FromArgb(207, 212, 218);
			UseVisualStyleBackColor = false;
			ForeColor = Color.FromArgb(78, 85, 92);
			Font = new Font("Segoe UI", 8f, FontStyle.Regular);
			Height = 30;
			MinimumSize = new Size(96, 30);
			Margin = new Padding(6, 0, 6, 0);
			Cursor = Cursors.Hand;
			TextAlign = ContentAlignment.MiddleCenter;
			UseCompatibleTextRendering = false;
			BackColor = Color.FromArgb(216, 221, 227);
			SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		protected override void OnClick(EventArgs e)
		{
			PlayClickSound();
			base.OnClick(e);
		}

		private Color ResolveSurfaceColor()
		{
			List<Color> list = new List<Color>();
			for (Control control = Parent; control != null; control = control.Parent)
			{
				Color backColor = control.BackColor;
				if (backColor.A != 0 && !(backColor == Color.Transparent))
				{
					list.Add(backColor);
					if (backColor.A == byte.MaxValue)
					{
						break;
					}
				}
			}
			Color result = SystemColors.Control;
			for (int num = list.Count - 1; num >= 0; num--)
			{
				Color backColor = list[num];
				if (backColor.A == byte.MaxValue)
				{
					result = backColor;
				}
				else
				{
					int a = backColor.A;
					int num2 = 255 - a;
					result = Color.FromArgb(255, (backColor.R * a + result.R * num2 + 127) / 255, (backColor.G * a + result.G * num2 + 127) / 255, (backColor.B * a + result.B * num2 + 127) / 255);
				}
			}
			return result;
		}

		protected override void OnParentChanged(EventArgs e)
		{
			base.OnParentChanged(e);
			BackColor = ResolveSurfaceColor();
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			using SolidBrush brush = new SolidBrush(ResolveSurfaceColor());
			e.Graphics.FillRectangle(brush, ClientRectangle);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			hovered = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			hovered = false;
			pressed = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				pressed = true;
			}
			Invalidate();
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			pressed = false;
			Invalidate();
			base.OnMouseUp(e);
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			base.OnEnabledChanged(e);
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			if (Width < 8 || Height < 8)
			{
				return;
			}
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
			Rectangle rectangle = new Rectangle(1, 1, Width - 3, Height - 3);
			Color color = ResolveSurfaceColor();
			using (SolidBrush brush = new SolidBrush(color))
			{
				e.Graphics.FillRectangle(brush, ClientRectangle);
			}
			Color color2;
			bool isConfirmContinue = string.Equals(Name, "BIMlifyConfirmContinue", StringComparison.Ordinal);
			if (isConfirmContinue)
			{
				color2 = pressed ? Color.FromArgb(250, 230, 168) : (hovered ? Color.FromArgb(255, 242, 202) : Color.FromArgb(255, 239, 190));
			}
			else if (primary)
			{
				color2 = pressed ? Color.FromArgb(250, 230, 168) : (hovered ? Color.FromArgb(255, 239, 190) : Color.FromArgb(250, 230, 168));
			}
			else if (primary)
			{
				if (pressed)
				{
					color2 = Color.FromArgb(250, 230, 168);
				}
				else
				{
					color2 = (hovered ? Color.FromArgb(255, 242, 202) : Color.FromArgb(250, 229, 171));
				}
			}
			else
			{
				color2 = (pressed ? BlendColor(color, Color.FromArgb(50, 62, 72), 0.08f) : color);
			}
			if (!Enabled)
			{
				color2 = color;
			}
			bool isOverviewContinue = string.Equals(Name, "BIMlifyOverviewContinueButton", StringComparison.Ordinal);
		Color color3;
			if (isConfirmContinue)
			{
				color3 = pressed ? Color.FromArgb(225, 197, 128) : (hovered ? Color.FromArgb(225, 197, 128) : Color.FromArgb(198, 151, 42));
			}
			else if (primary)
			{
				color3 = Color.FromArgb(198, 151, 42);
			}
			else if (primary)
			{
				if (pressed)
				{
					color3 = Color.FromArgb(225, 197, 128);
				}
				else
				{
					color3 = (hovered ? Color.FromArgb(225, 197, 128) : Color.FromArgb(225, 197, 128));
				}
			}
			else if (pressed)
			{
				color3 = Color.FromArgb(92, 101, 109);
			}
			else
			{
				color3 = (hovered ? Color.FromArgb(112, 121, 129) : Color.FromArgb(132, 140, 147));
			}
			if (!Enabled)
			{
				color3 = Color.FromArgb(166, 171, 176);
			}
			if (isOverviewContinue) color3 = Color.FromArgb(88, 101, 112);
			Color color4 = (Enabled ? Color.FromArgb(65, 72, 79) : Color.FromArgb(135, 140, 145));
			BIMlify.CleanerUi.SelectionButtonColors.Apply(this, ref color2, ref color3);
		 bool flag = string.Equals(AccessibleName, "Back", StringComparison.OrdinalIgnoreCase);
			if (flag)
			{
				using (SolidBrush backBrush = new SolidBrush(color2))
				{
					e.Graphics.FillEllipse(backBrush, rectangle);
				}
				using (Pen pen = new Pen(color3, 0.35f))
				{
					e.Graphics.DrawEllipse(pen, rectangle);
				}
				using Pen pen2 = new Pen(color4, 1.8f);
				pen2.StartCap = LineCap.Round;
				pen2.EndCap = LineCap.Round;
				float num = (float)rectangle.Left + (float)rectangle.Width / 2f;
				float num2 = (float)rectangle.Top + (float)rectangle.Height / 2f;
				e.Graphics.DrawLine(pen2, num + 5f, num2, num - 5f, num2);
				e.Graphics.DrawLine(pen2, num - 5f, num2, num + 1f, num2 - 6f);
				e.Graphics.DrawLine(pen2, num - 5f, num2, num + 1f, num2 + 6f);
			}
			else
			{
				using (GraphicsPath fillPath = RoundedPath(rectangle, 6))
				{
					using SolidBrush fillBrush = new SolidBrush(color2);
					e.Graphics.FillPath(fillBrush, fillPath);
				}
				using GraphicsPath outlinePath = RoundedPath(rectangle, 6);
				using Pen pen = new Pen(color3, 0.85f);
				e.Graphics.DrawPath(pen, outlinePath);
			}
			if (flag)
			{
				return;
			}
			using SolidBrush textBrush = new SolidBrush(color4);
			StringFormat stringFormat = new StringFormat();
			stringFormat.Alignment = StringAlignment.Center;
			stringFormat.LineAlignment = StringAlignment.Center;
			stringFormat.Trimming = StringTrimming.EllipsisCharacter;
			stringFormat.FormatFlags = StringFormatFlags.NoWrap;
			using StringFormat format = stringFormat;
			e.Graphics.DrawString(Text, Font, textBrush, rectangle, format);
		}
	}

	private sealed class CleanupCandidate
	{
		internal ElementId Id;

		internal string Group;

		internal string Name;

		internal string Kind;

		internal string Reason;

		internal CleanupCandidate(Element element, string group, string kind, string reason)
		{
			Id = element.Id;
			Group = group;
			Name = element.Name;
			Kind = kind;
			Reason = reason;
		}

		public override string ToString()
		{
			return Group + " | " + Name;
		}
	}

	private sealed class PurgeSuggestion
	{
		internal ElementId Id;

		internal string Category;

		internal string Name;

		internal string Kind;

		public override string ToString()
		{
			return Category + "  |  " + Name + "  —  " + Kind + "  [" + Id.Value + "]";
		}
	}

	private sealed class ExternalBimResource
	{
		internal string Name;

		internal string Group;

		internal string Availability;

		internal string Description;

		internal string Url;

		public override string ToString()
		{
			return Name;
		}
	}

	private sealed class LocalFamilyFile
	{
		internal string Path;

		internal string Category;

		internal string Name;

		public override string ToString()
		{
			return Category + "  |  " + Name;
		}
	}

	private sealed class ModelHealthWarning
	{
		internal string Severity;

		internal string Description;

		internal List<ElementId> ElementIds;

		internal string Categories;
	}

	private sealed class CategoryEntry
	{
		internal Category Category;

		internal string Key;

		internal string Name;

		internal string Type;

		internal int InView;

		internal int InProject;
	}

	private sealed class CategoryPickerResult
	{
		internal string Action;

		internal List<string> CategoryKeys = new List<string>();
	}

	private sealed class DashboardActionHandler : IExternalEventHandler
	{
		private string pendingAction;

		internal void Queue(string action)
		{
			pendingAction = action;
		}

		public void Execute(UIApplication application)
		{
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			string text = pendingAction;
			pendingAction = null;
			try
			{
				if (!string.IsNullOrEmpty(text))
				{
					RunFromDashboard(application, text);
				}
			}
			finally
			{
				BIMlifyForm window = (text == "categorytools") ? null : dashboardWindow;
				if (window != null && !window.IsDisposed)
				{
					Action action = () =>
					{
						if (!window.IsDisposed)
						{
							if (window.WindowState == FormWindowState.Minimized)
							{
								window.WindowState = FormWindowState.Normal;
							}
							window.Show();
							window.Activate();
						}
					};
					if (window.InvokeRequired)
					{
						window.BeginInvoke(action);
					}
					else
					{
						action();
					}
				}
			}
		}

		public string GetName()
		{
			return "BIMlify Dashboard Actions";
		}
	}

	private sealed class WelcomeLogoView : Control
	{
		public Image Logo;

		public double Motion;

		public double AccentMotion;

		public double SparkMotion;

		public double OrbitMotion;

		public WelcomeLogoView()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Color.White;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			if (Logo == null || ClientSize.Width < 1 || ClientSize.Height < 1)
			{
				return;
			}
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			double num = Math.Max(0.0, Math.Min(1.0, Motion));
			double num2 = 1.0 - Math.Pow(1.0 - num, 3.0);
			float num3 = (float)ClientSize.Width / 2f;
			float num4 = (float)ClientSize.Height / 2f;
			float num5 = 1f;
			float matrix = (float)Math.Min(1.0, Math.Max(0.0, (num - 0.02) / 0.48));
			float num6 = 180f;
			float num7 = num6 * (float)Logo.Height / (float)Logo.Width;
			using (GraphicsPath graphicsPath = new GraphicsPath())
			{
				graphicsPath.AddEllipse(num3 - num6 * 0.48f, num4 - num7 * 0.5f, num6 * 0.96f, num7 * 0.98f);
				using PathGradientBrush pathGradientBrush = new PathGradientBrush(graphicsPath);
				pathGradientBrush.CenterColor = Color.FromArgb(16, 214, 171, 61);
				pathGradientBrush.SurroundColors = new Color[1] { Color.FromArgb(0, 214, 171, 61) };
				e.Graphics.FillPath(pathGradientBrush, graphicsPath);
			}
			RectangleF rect = new RectangleF((float)ClientSize.Width / 2f - 116f, num4 + num7 / 2f - 2f, 232f, 22f);
			using (GraphicsPath graphicsPath2 = new GraphicsPath())
			{
				graphicsPath2.AddEllipse(rect);
				using PathGradientBrush pathGradientBrush2 = new PathGradientBrush(graphicsPath2);
				pathGradientBrush2.CenterColor = Color.FromArgb(44, 79, 76, 68);
				pathGradientBrush2.SurroundColors = new Color[1] { Color.FromArgb(0, 79, 76, 68) };
				e.Graphics.FillPath(pathGradientBrush2, graphicsPath2);
			}
			GraphicsState gstate = e.Graphics.Save();
			double num8 = Math.Max(0.0, Math.Min(1.0, AccentMotion));
			e.Graphics.TranslateTransform(num3, num4);
			e.Graphics.ScaleTransform(num5, num5);
			float num9 = num6 / num5;
			float num10 = num7 / num5;
			using (ImageAttributes imageAttributes = new ImageAttributes())
			{
				ColorMatrix colorMatrix = new ColorMatrix();
				colorMatrix.Matrix33 = matrix;
				imageAttributes.SetColorMatrix(colorMatrix);
				Rectangle destRect = new Rectangle((int)((0f - num9) / 2f), (int)((0f - num10) / 2f), (int)num9, (int)num10);
				e.Graphics.DrawImage(Logo, destRect, 0, 0, Logo.Width, Logo.Height, GraphicsUnit.Pixel, imageAttributes);
			}
			e.Graphics.Restore(gstate);
			double num11 = Math.Max(0.0, Math.Min(1.0, OrbitMotion));
			if (!(num11 > 0.0))
			{
				return;
			}
			double num12 = Math.Min(1.0, num11);
			double num13 = ((num11 <= 1.0) ? 1.0 : Math.Max(0.0, 1.0 - Math.Min(1.0, (num11 - 1.0) / 0.38)));
			int num14 = (int)(105.0 * (0.55 + 0.45 * Math.Sin(num12 * Math.PI)) * num13);
			float num15 = 316f;
			float num16 = num15;
			float num17 = num15;
			float num18 = num3 - num15 / 2f - 10f;
			float num19 = num4 - num15 / 2f;
			using (Pen pen = new Pen(Color.FromArgb(num14, 214, 171, 61), 1.15f))
			{
				pen.StartCap = LineCap.Round;
				pen.EndCap = LineCap.Round;
				float sweepAngle = Math.Min(360f, 360f * (float)num12);
				e.Graphics.DrawArc(pen, num18, num19, num16, num17, -18f, sweepAngle);
			}
			using Pen pen2 = new Pen(Color.FromArgb(Math.Max(0, num14 / 3), 214, 171, 61), 1f);
			e.Graphics.DrawArc(pen2, num18 + 8f, num19 + 5f, num16 - 16f, num17 - 10f, -20f, Math.Min(360f, 360f * (float)num12));
		}
	}

	private sealed class CadFilter : ISelectionFilter
	{
		public bool AllowElement(Element e)
		{
			return e is ImportInstance;
		}

		public bool AllowReference(Reference r, XYZ p)
		{
			return true;
		}
	}

	private sealed class LevelBubbleSelection
	{
		internal List<View> Views = new List<View>();

		internal List<Level> Levels = new List<Level>();
	}

	private sealed class CategoryTargets
	{
		internal List<ElementId> HostIds = new List<ElementId>();

		internal List<Reference> References = new List<Reference>();

		internal List<ElementId> IsolationIds = new List<ElementId>();
	}

	private sealed class TagFamilyOption
	{
		internal Family Family;

		internal string Category;

		internal int TypeCount;

		public override string ToString()
		{
			return Category + "  |  " + ((Element)Family).Name + "  (" + TypeCount + " type(s))";
		}
	}

	private sealed class TagFamilyLoadOptions : IFamilyLoadOptions
	{
		public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
		{
			overwriteParameterValues = true;
			return true;
		}

		public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
		{
			source = (FamilySource)1;
			overwriteParameterValues = true;
			return true;
		}
	}

	private sealed class RoomLinkFilter : ISelectionFilter
	{
		public bool AllowElement(Element e)
		{
			return e is RevitLinkInstance;
		}

		public bool AllowReference(Reference r, XYZ p)
		{
			return true;
		}
	}

	private sealed class HostRoomFilter : ISelectionFilter
	{
		public bool AllowElement(Element e)
		{
			return e is Room;
		}

		public bool AllowReference(Reference r, XYZ p)
		{
			return true;
		}
	}

	private sealed class BlockworkWallFilter : ISelectionFilter
	{
		public bool AllowElement(Element e)
		{
			return e is Wall;
		}

		public bool AllowReference(Reference r, XYZ p)
		{
			return true;
		}
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass365
	{
		public Document doc;

		public UIDocument ui;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass367
	{
		public ListBox results;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass369
	{
		public TabControl discipline;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass36b
	{
		public ComboBox scope;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass36d
	{
		public ComboBox disciplineA;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass36f
	{
		public ComboBox disciplineB;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass371
	{
		public Panel disciplinePair;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass374
	{
		public _003C_003Ec__DisplayClass371 CS_0024_003C_003E8__locals372;

		public _003C_003Ec__DisplayClass36f CS_0024_003C_003E8__locals370;

		public _003C_003Ec__DisplayClass36d CS_0024_003C_003E8__locals36e;

		public _003C_003Ec__DisplayClass36b CS_0024_003C_003E8__locals36c;

		public _003C_003Ec__DisplayClass369 CS_0024_003C_003E8__locals36a;

		public _003C_003Ec__DisplayClass367 CS_0024_003C_003E8__locals368;

		public _003C_003Ec__DisplayClass365 CS_0024_003C_003E8__locals366;

		public Panel rulesBox;

		public void _003CClashDetection_003Eb__34b(object sender, PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = RoundedPath(new Rectangle(0, 0, rulesBox.Width - 1, rulesBox.Height - 1), 10);
			using Pen pen = new Pen(Color.FromArgb(205, 211, 217), 1f);
			e.Graphics.DrawPath(pen, path);
		}
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass378
	{
		public _003C_003Ec__DisplayClass374 CS_0024_003C_003E8__locals375;

		public _003C_003Ec__DisplayClass371 CS_0024_003C_003E8__locals372;

		public _003C_003Ec__DisplayClass36f CS_0024_003C_003E8__locals370;

		public _003C_003Ec__DisplayClass36d CS_0024_003C_003E8__locals36e;

		public _003C_003Ec__DisplayClass36b CS_0024_003C_003E8__locals36c;

		public _003C_003Ec__DisplayClass369 CS_0024_003C_003E8__locals36a;

		public _003C_003Ec__DisplayClass367 CS_0024_003C_003E8__locals368;

		public _003C_003Ec__DisplayClass365 CS_0024_003C_003E8__locals366;

		public Panel scanOptions;

		public void _003CClashDetection_003Eb__349(object sender, PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = RoundedPath(new Rectangle(0, 0, scanOptions.Width - 1, scanOptions.Height - 1), 8);
			using Pen pen = new Pen(Color.FromArgb(205, 211, 217), 1f);
			e.Graphics.DrawPath(pen, path);
		}

		public void _003CClashDetection_003Eb__34a(object param0, EventArgs param1)
		{
			if (scanOptions.Width > 4 && scanOptions.Height > 4)
			{
				using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, scanOptions.Width - 1, scanOptions.Height - 1), 8))
				{
					Region region = scanOptions.Region;
					scanOptions.Region = new Region(path);
					region?.Dispose();
				}
			}
		}
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass37c
	{
		public _003C_003Ec__DisplayClass378 CS_0024_003C_003E8__locals379;

		public _003C_003Ec__DisplayClass374 CS_0024_003C_003E8__locals375;

		public _003C_003Ec__DisplayClass371 CS_0024_003C_003E8__locals372;

		public _003C_003Ec__DisplayClass36f CS_0024_003C_003E8__locals370;

		public _003C_003Ec__DisplayClass36d CS_0024_003C_003E8__locals36e;

		public _003C_003Ec__DisplayClass36b CS_0024_003C_003E8__locals36c;

		public _003C_003Ec__DisplayClass369 CS_0024_003C_003E8__locals36a;

		public _003C_003Ec__DisplayClass367 CS_0024_003C_003E8__locals368;

		public _003C_003Ec__DisplayClass365 CS_0024_003C_003E8__locals366;

		public SplitContainer clashSplit;

		public void _003CClashDetection_003Eb__345()
		{
			int width = clashSplit.ClientSize.Width;
			int num = 0 + clashSplit.SplitterWidth;
			if (width < num)
			{
				return;
			}
			int splitterDistance = Math.Max(0, Math.Min(width - 0 - clashSplit.SplitterWidth, 360));
			try
			{
				clashSplit.Panel1MinSize = 0;
				clashSplit.Panel2MinSize = 0;
				clashSplit.SplitterDistance = splitterDistance;
				clashSplit.Panel1MinSize = 0;
				clashSplit.Panel2MinSize = 0;
			}
			catch (System.ArgumentException)
			{
			}
		}

		public void _003CClashDetection_003Eb__347(object sender, PaintEventArgs e)
		{
			using Pen pen = new Pen(Color.FromArgb(210, 216, 222), 1f);
			e.Graphics.DrawRectangle(pen, 0, 0, clashSplit.Panel1.Width - 1, clashSplit.Panel1.Height - 1);
		}
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass37f
	{
		public _003C_003Ec__DisplayClass37c CS_0024_003C_003E8__locals37d;

		public _003C_003Ec__DisplayClass378 CS_0024_003C_003E8__locals379;

		public _003C_003Ec__DisplayClass374 CS_0024_003C_003E8__locals375;

		public _003C_003Ec__DisplayClass371 CS_0024_003C_003E8__locals372;

		public _003C_003Ec__DisplayClass36f CS_0024_003C_003E8__locals370;

		public _003C_003Ec__DisplayClass36d CS_0024_003C_003E8__locals36e;

		public _003C_003Ec__DisplayClass36b CS_0024_003C_003E8__locals36c;

		public _003C_003Ec__DisplayClass369 CS_0024_003C_003E8__locals36a;

		public _003C_003Ec__DisplayClass367 CS_0024_003C_003E8__locals368;

		public _003C_003Ec__DisplayClass365 CS_0024_003C_003E8__locals366;

		public Panel previewPanel;

		public void _003CClashDetection_003Eb__348(object sender, PaintEventArgs e)
		{
			using Pen pen = new Pen(Color.FromArgb(210, 216, 222), 1f);
			e.Graphics.DrawRectangle(pen, 0, 0, previewPanel.Width - 1, previewPanel.Height - 1);
		}
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass381
	{
		public Label previewTitle;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass383
	{
		public Label previewText;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass385
	{
		public GlassButton copyA;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass387
	{
		public GlassButton copyB;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass389
	{
		public GlassButton findIds;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass38b
	{
		public Label detail;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass38d
	{
		public GlassButton review;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass38f
	{
		public Label status;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass391
	{
		private sealed class _003C_003Ec__DisplayClass395
		{
			public _003C_003Ec__DisplayClass391 CS_0024_003C_003E8__locals392;

			public _003C_003Ec__DisplayClass38f CS_0024_003C_003E8__locals390;

			public _003C_003Ec__DisplayClass38d CS_0024_003C_003E8__locals38e;

			public _003C_003Ec__DisplayClass38b CS_0024_003C_003E8__locals38c;

			public _003C_003Ec__DisplayClass389 CS_0024_003C_003E8__locals38a;

			public _003C_003Ec__DisplayClass387 CS_0024_003C_003E8__locals388;

			public _003C_003Ec__DisplayClass385 CS_0024_003C_003E8__locals386;

			public _003C_003Ec__DisplayClass383 CS_0024_003C_003E8__locals384;

			public _003C_003Ec__DisplayClass381 CS_0024_003C_003E8__locals382;

			public _003C_003Ec__DisplayClass37f CS_0024_003C_003E8__locals380;

			public _003C_003Ec__DisplayClass37c CS_0024_003C_003E8__locals37d;

			public _003C_003Ec__DisplayClass378 CS_0024_003C_003E8__locals379;

			public _003C_003Ec__DisplayClass374 CS_0024_003C_003E8__locals375;

			public _003C_003Ec__DisplayClass371 CS_0024_003C_003E8__locals372;

			public _003C_003Ec__DisplayClass36f CS_0024_003C_003E8__locals370;

			public _003C_003Ec__DisplayClass36d CS_0024_003C_003E8__locals36e;

			public _003C_003Ec__DisplayClass36b CS_0024_003C_003E8__locals36c;

			public _003C_003Ec__DisplayClass369 CS_0024_003C_003E8__locals36a;

			public _003C_003Ec__DisplayClass367 CS_0024_003C_003E8__locals368;

			public _003C_003Ec__DisplayClass365 CS_0024_003C_003E8__locals366;

			public string selected;

			public bool _003CClashDetection_003Eb__355(Tuple<ElementId, ElementId, string, string, string> x)
			{
				return x.Item3 == selected;
			}
		}

		private sealed class _003C_003Ec__DisplayClass397
		{
			public _003C_003Ec__DisplayClass391 CS_0024_003C_003E8__locals392;

			public _003C_003Ec__DisplayClass38f CS_0024_003C_003E8__locals390;

			public _003C_003Ec__DisplayClass38d CS_0024_003C_003E8__locals38e;

			public _003C_003Ec__DisplayClass38b CS_0024_003C_003E8__locals38c;

			public _003C_003Ec__DisplayClass389 CS_0024_003C_003E8__locals38a;

			public _003C_003Ec__DisplayClass387 CS_0024_003C_003E8__locals388;

			public _003C_003Ec__DisplayClass385 CS_0024_003C_003E8__locals386;

			public _003C_003Ec__DisplayClass383 CS_0024_003C_003E8__locals384;

			public _003C_003Ec__DisplayClass381 CS_0024_003C_003E8__locals382;

			public _003C_003Ec__DisplayClass37f CS_0024_003C_003E8__locals380;

			public _003C_003Ec__DisplayClass37c CS_0024_003C_003E8__locals37d;

			public _003C_003Ec__DisplayClass378 CS_0024_003C_003E8__locals379;

			public _003C_003Ec__DisplayClass374 CS_0024_003C_003E8__locals375;

			public _003C_003Ec__DisplayClass371 CS_0024_003C_003E8__locals372;

			public _003C_003Ec__DisplayClass36f CS_0024_003C_003E8__locals370;

			public _003C_003Ec__DisplayClass36d CS_0024_003C_003E8__locals36e;

			public _003C_003Ec__DisplayClass36b CS_0024_003C_003E8__locals36c;

			public _003C_003Ec__DisplayClass369 CS_0024_003C_003E8__locals36a;

			public _003C_003Ec__DisplayClass367 CS_0024_003C_003E8__locals368;

			public _003C_003Ec__DisplayClass365 CS_0024_003C_003E8__locals366;

			public int i;

			public bool _003CClashDetection_003Eb__357(Tuple<ElementId, ElementId, string, string, string> x)
			{
				return x.Item3 == CS_0024_003C_003E8__locals368.results.Items[i] as string;
			}
		}

		private sealed class _003C_003Ec__DisplayClass399
		{
			public _003C_003Ec__DisplayClass391 CS_0024_003C_003E8__locals392;

			public _003C_003Ec__DisplayClass38f CS_0024_003C_003E8__locals390;

			public _003C_003Ec__DisplayClass38d CS_0024_003C_003E8__locals38e;

			public _003C_003Ec__DisplayClass38b CS_0024_003C_003E8__locals38c;

			public _003C_003Ec__DisplayClass389 CS_0024_003C_003E8__locals38a;

			public _003C_003Ec__DisplayClass387 CS_0024_003C_003E8__locals388;

			public _003C_003Ec__DisplayClass385 CS_0024_003C_003E8__locals386;

			public _003C_003Ec__DisplayClass383 CS_0024_003C_003E8__locals384;

			public _003C_003Ec__DisplayClass381 CS_0024_003C_003E8__locals382;

			public _003C_003Ec__DisplayClass37f CS_0024_003C_003E8__locals380;

			public _003C_003Ec__DisplayClass37c CS_0024_003C_003E8__locals37d;

			public _003C_003Ec__DisplayClass378 CS_0024_003C_003E8__locals379;

			public _003C_003Ec__DisplayClass374 CS_0024_003C_003E8__locals375;

			public _003C_003Ec__DisplayClass371 CS_0024_003C_003E8__locals372;

			public _003C_003Ec__DisplayClass36f CS_0024_003C_003E8__locals370;

			public _003C_003Ec__DisplayClass36d CS_0024_003C_003E8__locals36e;

			public _003C_003Ec__DisplayClass36b CS_0024_003C_003E8__locals36c;

			public _003C_003Ec__DisplayClass369 CS_0024_003C_003E8__locals36a;

			public _003C_003Ec__DisplayClass367 CS_0024_003C_003E8__locals368;

			public _003C_003Ec__DisplayClass365 CS_0024_003C_003E8__locals366;

			public int i;

			public bool _003CClashDetection_003Eb__359(Tuple<ElementId, ElementId, string, string, string> x)
			{
				return x.Item3 == CS_0024_003C_003E8__locals368.results.Items[i] as string;
			}
		}

		private sealed class _003C_003Ec__DisplayClass39b
		{
			public _003C_003Ec__DisplayClass391 CS_0024_003C_003E8__locals392;

			public _003C_003Ec__DisplayClass38f CS_0024_003C_003E8__locals390;

			public _003C_003Ec__DisplayClass38d CS_0024_003C_003E8__locals38e;

			public _003C_003Ec__DisplayClass38b CS_0024_003C_003E8__locals38c;

			public _003C_003Ec__DisplayClass389 CS_0024_003C_003E8__locals38a;

			public _003C_003Ec__DisplayClass387 CS_0024_003C_003E8__locals388;

			public _003C_003Ec__DisplayClass385 CS_0024_003C_003E8__locals386;

			public _003C_003Ec__DisplayClass383 CS_0024_003C_003E8__locals384;

			public _003C_003Ec__DisplayClass381 CS_0024_003C_003E8__locals382;

			public _003C_003Ec__DisplayClass37f CS_0024_003C_003E8__locals380;

			public _003C_003Ec__DisplayClass37c CS_0024_003C_003E8__locals37d;

			public _003C_003Ec__DisplayClass378 CS_0024_003C_003E8__locals379;

			public _003C_003Ec__DisplayClass374 CS_0024_003C_003E8__locals375;

			public _003C_003Ec__DisplayClass371 CS_0024_003C_003E8__locals372;

			public _003C_003Ec__DisplayClass36f CS_0024_003C_003E8__locals370;

			public _003C_003Ec__DisplayClass36d CS_0024_003C_003E8__locals36e;

			public _003C_003Ec__DisplayClass36b CS_0024_003C_003E8__locals36c;

			public _003C_003Ec__DisplayClass369 CS_0024_003C_003E8__locals36a;

			public _003C_003Ec__DisplayClass367 CS_0024_003C_003E8__locals368;

			public _003C_003Ec__DisplayClass365 CS_0024_003C_003E8__locals366;

			public int i;

			public bool _003CClashDetection_003Eb__35b(Tuple<ElementId, ElementId, string, string, string> x)
			{
				return x.Item3 == CS_0024_003C_003E8__locals368.results.Items[i] as string;
			}
		}

		private sealed class _003C_003Ec__DisplayClass39d
		{
			public _003C_003Ec__DisplayClass391 CS_0024_003C_003E8__locals392;

			public _003C_003Ec__DisplayClass38f CS_0024_003C_003E8__locals390;

			public _003C_003Ec__DisplayClass38d CS_0024_003C_003E8__locals38e;

			public _003C_003Ec__DisplayClass38b CS_0024_003C_003E8__locals38c;

			public _003C_003Ec__DisplayClass389 CS_0024_003C_003E8__locals38a;

			public _003C_003Ec__DisplayClass387 CS_0024_003C_003E8__locals388;

			public _003C_003Ec__DisplayClass385 CS_0024_003C_003E8__locals386;

			public _003C_003Ec__DisplayClass383 CS_0024_003C_003E8__locals384;

			public _003C_003Ec__DisplayClass381 CS_0024_003C_003E8__locals382;

			public _003C_003Ec__DisplayClass37f CS_0024_003C_003E8__locals380;

			public _003C_003Ec__DisplayClass37c CS_0024_003C_003E8__locals37d;

			public _003C_003Ec__DisplayClass378 CS_0024_003C_003E8__locals379;

			public _003C_003Ec__DisplayClass374 CS_0024_003C_003E8__locals375;

			public _003C_003Ec__DisplayClass371 CS_0024_003C_003E8__locals372;

			public _003C_003Ec__DisplayClass36f CS_0024_003C_003E8__locals370;

			public _003C_003Ec__DisplayClass36d CS_0024_003C_003E8__locals36e;

			public _003C_003Ec__DisplayClass36b CS_0024_003C_003E8__locals36c;

			public _003C_003Ec__DisplayClass369 CS_0024_003C_003E8__locals36a;

			public _003C_003Ec__DisplayClass367 CS_0024_003C_003E8__locals368;

			public _003C_003Ec__DisplayClass365 CS_0024_003C_003E8__locals366;

			public int i;

			public bool _003CClashDetection_003Eb__35d(Tuple<ElementId, ElementId, string, string, string> x)
			{
				return x.Item3 == CS_0024_003C_003E8__locals368.results.Items[i] as string;
			}
		}

		public _003C_003Ec__DisplayClass38f CS_0024_003C_003E8__locals390;

		public _003C_003Ec__DisplayClass38d CS_0024_003C_003E8__locals38e;

		public _003C_003Ec__DisplayClass38b CS_0024_003C_003E8__locals38c;

		public _003C_003Ec__DisplayClass389 CS_0024_003C_003E8__locals38a;

		public _003C_003Ec__DisplayClass387 CS_0024_003C_003E8__locals388;

		public _003C_003Ec__DisplayClass385 CS_0024_003C_003E8__locals386;

		public _003C_003Ec__DisplayClass383 CS_0024_003C_003E8__locals384;

		public _003C_003Ec__DisplayClass381 CS_0024_003C_003E8__locals382;

		public _003C_003Ec__DisplayClass37f CS_0024_003C_003E8__locals380;

		public _003C_003Ec__DisplayClass37c CS_0024_003C_003E8__locals37d;

		public _003C_003Ec__DisplayClass378 CS_0024_003C_003E8__locals379;

		public _003C_003Ec__DisplayClass374 CS_0024_003C_003E8__locals375;

		public _003C_003Ec__DisplayClass371 CS_0024_003C_003E8__locals372;

		public _003C_003Ec__DisplayClass36f CS_0024_003C_003E8__locals370;

		public _003C_003Ec__DisplayClass36d CS_0024_003C_003E8__locals36e;

		public _003C_003Ec__DisplayClass36b CS_0024_003C_003E8__locals36c;

		public _003C_003Ec__DisplayClass369 CS_0024_003C_003E8__locals36a;

		public _003C_003Ec__DisplayClass367 CS_0024_003C_003E8__locals368;

		public _003C_003Ec__DisplayClass365 CS_0024_003C_003E8__locals366;

		public Color bg;

		public Color ink;

		public Panel firstCard;

		public Panel secondCard;

		public Label intersectLabel;

		public Action layoutPair;

		public Action ensureClashSplit;

		public List<Tuple<ElementId, ElementId, string, string, string>> clashRows;

		public Func<string, string> disciplineOf;

		public Func<string, string> choiceCode;

		public Action refreshResults;

		public Action runScan;

		public Action updatePairLabel;

		private static Func<Tuple<ElementId, ElementId, string, string, string>, int, string> CS_0024_003C_003E9__CachedAnonymousMethodDelegate39f;

		public void _003CClashDetection_003Eb__33d(object sender, DrawItemEventArgs e)
		{
			bool flag = e.Index == CS_0024_003C_003E8__locals36a.discipline.SelectedIndex;
			using (SolidBrush brush = new SolidBrush(flag ? Color.White : bg))
			{
				e.Graphics.FillRectangle(brush, e.Bounds);
			}
			using (SolidBrush brush2 = new SolidBrush(ink))
			{
				using StringFormat format = new StringFormat
				{
					Alignment = StringAlignment.Center,
					LineAlignment = StringAlignment.Center,
					FormatFlags = StringFormatFlags.NoWrap
				};
				e.Graphics.DrawString(CS_0024_003C_003E8__locals36a.discipline.TabPages[e.Index].Text, CS_0024_003C_003E8__locals36a.discipline.Font, brush2, e.Bounds, format);
			}
			if (flag)
			{
				using (Pen pen = new Pen(Color.FromArgb(214, 171, 61), 2f))
				{
					e.Graphics.DrawLine(pen, e.Bounds.Left + 3, e.Bounds.Bottom - 2, e.Bounds.Right - 4, e.Bounds.Bottom - 2);
				}
			}
		}

		public void _003CClashDetection_003Eb__343()
		{
			int num = 260;
			int num2 = Math.Max(250, (CS_0024_003C_003E8__locals372.disciplinePair.ClientSize.Width - num) / 2);
			firstCard.SetBounds(0, 5, num2, 92);
			secondCard.SetBounds(num2 + num, 5, num2, 92);
			intersectLabel.SetBounds(num2 + (num - 240) / 2, 37, 240, 34);
			CS_0024_003C_003E8__locals36e.disciplineA.Width = Math.Max(190, num2 - 32);
			CS_0024_003C_003E8__locals370.disciplineB.Width = Math.Max(190, num2 - 32);
		}

		public void _003CClashDetection_003Eb__344(object param0, EventArgs param1)
		{
			layoutPair();
		}

		public void _003CClashDetection_003Eb__346(object param0, EventArgs param1)
		{
			ensureClashSplit();
		}

		public void _003CClashDetection_003Eb__34e()
		{
			CS_0024_003C_003E8__locals368.results.BeginUpdate();
			CS_0024_003C_003E8__locals368.results.Items.Clear();
			string text = "ALL";
			foreach (Tuple<ElementId, ElementId, string, string, string> item in clashRows)
			{
				if (text == "ALL" || item.Item4 == text)
				{
					CS_0024_003C_003E8__locals368.results.Items.Add(item.Item3);
				}
			}
			CS_0024_003C_003E8__locals368.results.EndUpdate();
			CS_0024_003C_003E8__locals390.status.Text = CS_0024_003C_003E8__locals368.results.Items.Count + " shown · " + clashRows.Count + " total potential clash(es).";
			CS_0024_003C_003E8__locals38e.review.Enabled = false;
			CS_0024_003C_003E8__locals386.copyA.Enabled = false;
			CS_0024_003C_003E8__locals388.copyB.Enabled = false;
			CS_0024_003C_003E8__locals38a.findIds.Enabled = false;
			CS_0024_003C_003E8__locals38c.detail.Text = "Select a clash to see the reason and suggested coordination action.";
		}

		public void _003CClashDetection_003Eb__34f()
		{
			//Discarded unreachable code: IL_0124, IL_0129, IL_0133, IL_0235, IL_0237, IL_0248, IL_025f, IL_026b, IL_0276, IL_02bd, IL_02c2, IL_02cf, IL_02d9, IL_02e3, IL_02f0, IL_02fa, IL_0304, IL_0311, IL_031e, IL_0328, IL_0332, IL_033c, IL_0346, IL_0347, IL_034a, IL_034b, IL_034e, IL_034f, IL_0352, IL_0353, IL_035e, IL_0363, IL_039e, IL_03b8, IL_03d2, IL_03ec, IL_0406, IL_0422, IL_0423, IL_042a, IL_042f, IL_0588, IL_058a, IL_058b, IL_0591, IL_05a4, IL_05aa, IL_05bd
			LinkedClashSupport.StartHostScanAsync(this);
		}

		public void _003CClashDetection_003Eb__350()
		{
			intersectLabel.Text = ((CS_0024_003C_003E8__locals36e.disciplineA.SelectedItem == null) ? "ANY" : CS_0024_003C_003E8__locals36e.disciplineA.SelectedItem.ToString()) + "  ×  " + ((CS_0024_003C_003E8__locals370.disciplineB.SelectedItem == null) ? "ANY" : CS_0024_003C_003E8__locals370.disciplineB.SelectedItem.ToString());
		}

		public void _003CClashDetection_003Eb__351(object param0, EventArgs param1)
		{
			refreshResults();
		}

		public void _003CClashDetection_003Eb__352(object param0, EventArgs param1)
		{
			updatePairLabel();
			runScan();
		}

		public void _003CClashDetection_003Eb__353(object param0, EventArgs param1)
		{
			updatePairLabel();
			runScan();
		}

		public void _003CClashDetection_003Eb__354(object param0, EventArgs param1)
		{
			_003C_003Ec__DisplayClass38f obj = CS_0024_003C_003E8__locals390;
			_003C_003Ec__DisplayClass38d obj2 = CS_0024_003C_003E8__locals38e;
			_003C_003Ec__DisplayClass38b obj3 = CS_0024_003C_003E8__locals38c;
			_003C_003Ec__DisplayClass389 obj4 = CS_0024_003C_003E8__locals38a;
			_003C_003Ec__DisplayClass387 obj5 = CS_0024_003C_003E8__locals388;
			_003C_003Ec__DisplayClass385 obj6 = CS_0024_003C_003E8__locals386;
			_003C_003Ec__DisplayClass383 obj7 = CS_0024_003C_003E8__locals384;
			_003C_003Ec__DisplayClass381 obj8 = CS_0024_003C_003E8__locals382;
			_003C_003Ec__DisplayClass37f obj9 = CS_0024_003C_003E8__locals380;
			_003C_003Ec__DisplayClass37c obj10 = CS_0024_003C_003E8__locals37d;
			_003C_003Ec__DisplayClass378 obj11 = CS_0024_003C_003E8__locals379;
			_003C_003Ec__DisplayClass374 obj12 = CS_0024_003C_003E8__locals375;
			_003C_003Ec__DisplayClass371 obj13 = CS_0024_003C_003E8__locals372;
			_003C_003Ec__DisplayClass36f obj14 = CS_0024_003C_003E8__locals370;
			_003C_003Ec__DisplayClass36d obj15 = CS_0024_003C_003E8__locals36e;
			_003C_003Ec__DisplayClass36b obj16 = CS_0024_003C_003E8__locals36c;
			_003C_003Ec__DisplayClass369 obj17 = CS_0024_003C_003E8__locals36a;
			_003C_003Ec__DisplayClass367 obj18 = CS_0024_003C_003E8__locals368;
			_003C_003Ec__DisplayClass365 obj19 = CS_0024_003C_003E8__locals366;
			int selectedIndex = CS_0024_003C_003E8__locals368.results.SelectedIndex;
			if (selectedIndex >= 0)
			{
				string selected = CS_0024_003C_003E8__locals368.results.Items[selectedIndex] as string;
				Tuple<ElementId, ElementId, string, string, string> tuple = clashRows.FirstOrDefault((Tuple<ElementId, ElementId, string, string, string> x) => x.Item3 == selected);
				if (tuple != null)
				{
					CS_0024_003C_003E8__locals38c.detail.Text = "Reason: " + tuple.Item5 + " Action: inspect both elements; revise the responsible discipline's layout.";
					CS_0024_003C_003E8__locals382.previewTitle.Text = "Clash details · " + tuple.Item4;
					CS_0024_003C_003E8__locals384.previewText.Text = "Element A: " + tuple.Item1.IntegerValue + Environment.NewLine + "Element B: " + tuple.Item2.IntegerValue + Environment.NewLine + Environment.NewLine + tuple.Item5 + Environment.NewLine + Environment.NewLine + "Use Review in Revit to select and frame both elements.";
					CS_0024_003C_003E8__locals38e.review.Enabled = true;
					CS_0024_003C_003E8__locals386.copyA.Enabled = true;
					CS_0024_003C_003E8__locals388.copyB.Enabled = true;
					CS_0024_003C_003E8__locals38a.findIds.Enabled = true;
				}
			}
		}

		public void _003CClashDetection_003Eb__356(object param0, EventArgs param1)
		{
			_003C_003Ec__DisplayClass38f obj = CS_0024_003C_003E8__locals390;
			_003C_003Ec__DisplayClass38d obj2 = CS_0024_003C_003E8__locals38e;
			_003C_003Ec__DisplayClass38b obj3 = CS_0024_003C_003E8__locals38c;
			_003C_003Ec__DisplayClass389 obj4 = CS_0024_003C_003E8__locals38a;
			_003C_003Ec__DisplayClass387 obj5 = CS_0024_003C_003E8__locals388;
			_003C_003Ec__DisplayClass385 obj6 = CS_0024_003C_003E8__locals386;
			_003C_003Ec__DisplayClass383 obj7 = CS_0024_003C_003E8__locals384;
			_003C_003Ec__DisplayClass381 obj8 = CS_0024_003C_003E8__locals382;
			_003C_003Ec__DisplayClass37f obj9 = CS_0024_003C_003E8__locals380;
			_003C_003Ec__DisplayClass37c obj10 = CS_0024_003C_003E8__locals37d;
			_003C_003Ec__DisplayClass378 obj11 = CS_0024_003C_003E8__locals379;
			_003C_003Ec__DisplayClass374 obj12 = CS_0024_003C_003E8__locals375;
			_003C_003Ec__DisplayClass371 obj13 = CS_0024_003C_003E8__locals372;
			_003C_003Ec__DisplayClass36f obj14 = CS_0024_003C_003E8__locals370;
			_003C_003Ec__DisplayClass36d obj15 = CS_0024_003C_003E8__locals36e;
			_003C_003Ec__DisplayClass36b obj16 = CS_0024_003C_003E8__locals36c;
			_003C_003Ec__DisplayClass369 obj17 = CS_0024_003C_003E8__locals36a;
			_003C_003Ec__DisplayClass367 obj18 = CS_0024_003C_003E8__locals368;
			_003C_003Ec__DisplayClass365 obj19 = CS_0024_003C_003E8__locals366;
			int i = CS_0024_003C_003E8__locals368.results.SelectedIndex;
			if (i < 0)
			{
				return;
			}
			Tuple<ElementId, ElementId, string, string, string> tuple = clashRows.FirstOrDefault((Tuple<ElementId, ElementId, string, string, string> x) => x.Item3 == obj18.results.Items[i] as string);
			if (tuple == null)
			{
				return;
			}
			CS_0024_003C_003E8__locals366.ui.Selection.SetElementIds((ICollection<ElementId>)new List<ElementId> { tuple.Item1, tuple.Item2 });
			try
			{
				CS_0024_003C_003E8__locals366.ui.ShowElements((ICollection<ElementId>)new List<ElementId> { tuple.Item1, tuple.Item2 });
			}
			catch
			{
			}
		}

		public void _003CClashDetection_003Eb__358(object param0, EventArgs param1)
		{
			_003C_003Ec__DisplayClass38f obj = CS_0024_003C_003E8__locals390;
			_003C_003Ec__DisplayClass38d obj2 = CS_0024_003C_003E8__locals38e;
			_003C_003Ec__DisplayClass38b obj3 = CS_0024_003C_003E8__locals38c;
			_003C_003Ec__DisplayClass389 obj4 = CS_0024_003C_003E8__locals38a;
			_003C_003Ec__DisplayClass387 obj5 = CS_0024_003C_003E8__locals388;
			_003C_003Ec__DisplayClass385 obj6 = CS_0024_003C_003E8__locals386;
			_003C_003Ec__DisplayClass383 obj7 = CS_0024_003C_003E8__locals384;
			_003C_003Ec__DisplayClass381 obj8 = CS_0024_003C_003E8__locals382;
			_003C_003Ec__DisplayClass37f obj9 = CS_0024_003C_003E8__locals380;
			_003C_003Ec__DisplayClass37c obj10 = CS_0024_003C_003E8__locals37d;
			_003C_003Ec__DisplayClass378 obj11 = CS_0024_003C_003E8__locals379;
			_003C_003Ec__DisplayClass374 obj12 = CS_0024_003C_003E8__locals375;
			_003C_003Ec__DisplayClass371 obj13 = CS_0024_003C_003E8__locals372;
			_003C_003Ec__DisplayClass36f obj14 = CS_0024_003C_003E8__locals370;
			_003C_003Ec__DisplayClass36d obj15 = CS_0024_003C_003E8__locals36e;
			_003C_003Ec__DisplayClass36b obj16 = CS_0024_003C_003E8__locals36c;
			_003C_003Ec__DisplayClass369 obj17 = CS_0024_003C_003E8__locals36a;
			_003C_003Ec__DisplayClass367 obj18 = CS_0024_003C_003E8__locals368;
			_003C_003Ec__DisplayClass365 obj19 = CS_0024_003C_003E8__locals366;
			int i = CS_0024_003C_003E8__locals368.results.SelectedIndex;
			if (i < 0)
			{
				return;
			}
			Tuple<ElementId, ElementId, string, string, string> tuple = clashRows.FirstOrDefault((Tuple<ElementId, ElementId, string, string, string> x) => x.Item3 == obj18.results.Items[i] as string);
			if (tuple == null)
			{
				return;
			}
			try
			{
				Clipboard.SetText(tuple.Item1.IntegerValue.ToString());
				CS_0024_003C_003E8__locals390.status.Text = "Element A ID copied: " + tuple.Item1.IntegerValue;
			}
			catch
			{
			}
		}

		public void _003CClashDetection_003Eb__35a(object param0, EventArgs param1)
		{
			_003C_003Ec__DisplayClass38f obj = CS_0024_003C_003E8__locals390;
			_003C_003Ec__DisplayClass38d obj2 = CS_0024_003C_003E8__locals38e;
			_003C_003Ec__DisplayClass38b obj3 = CS_0024_003C_003E8__locals38c;
			_003C_003Ec__DisplayClass389 obj4 = CS_0024_003C_003E8__locals38a;
			_003C_003Ec__DisplayClass387 obj5 = CS_0024_003C_003E8__locals388;
			_003C_003Ec__DisplayClass385 obj6 = CS_0024_003C_003E8__locals386;
			_003C_003Ec__DisplayClass383 obj7 = CS_0024_003C_003E8__locals384;
			_003C_003Ec__DisplayClass381 obj8 = CS_0024_003C_003E8__locals382;
			_003C_003Ec__DisplayClass37f obj9 = CS_0024_003C_003E8__locals380;
			_003C_003Ec__DisplayClass37c obj10 = CS_0024_003C_003E8__locals37d;
			_003C_003Ec__DisplayClass378 obj11 = CS_0024_003C_003E8__locals379;
			_003C_003Ec__DisplayClass374 obj12 = CS_0024_003C_003E8__locals375;
			_003C_003Ec__DisplayClass371 obj13 = CS_0024_003C_003E8__locals372;
			_003C_003Ec__DisplayClass36f obj14 = CS_0024_003C_003E8__locals370;
			_003C_003Ec__DisplayClass36d obj15 = CS_0024_003C_003E8__locals36e;
			_003C_003Ec__DisplayClass36b obj16 = CS_0024_003C_003E8__locals36c;
			_003C_003Ec__DisplayClass369 obj17 = CS_0024_003C_003E8__locals36a;
			_003C_003Ec__DisplayClass367 obj18 = CS_0024_003C_003E8__locals368;
			_003C_003Ec__DisplayClass365 obj19 = CS_0024_003C_003E8__locals366;
			int i = CS_0024_003C_003E8__locals368.results.SelectedIndex;
			if (i < 0)
			{
				return;
			}
			Tuple<ElementId, ElementId, string, string, string> tuple = clashRows.FirstOrDefault((Tuple<ElementId, ElementId, string, string, string> x) => x.Item3 == obj18.results.Items[i] as string);
			if (tuple == null)
			{
				return;
			}
			try
			{
				Clipboard.SetText(tuple.Item2.IntegerValue.ToString());
				CS_0024_003C_003E8__locals390.status.Text = "Element B ID copied: " + tuple.Item2.IntegerValue;
			}
			catch
			{
			}
		}

		public void _003CClashDetection_003Eb__35c(object param0, EventArgs param1)
		{
			_003C_003Ec__DisplayClass38f obj = CS_0024_003C_003E8__locals390;
			_003C_003Ec__DisplayClass38d obj2 = CS_0024_003C_003E8__locals38e;
			_003C_003Ec__DisplayClass38b obj3 = CS_0024_003C_003E8__locals38c;
			_003C_003Ec__DisplayClass389 obj4 = CS_0024_003C_003E8__locals38a;
			_003C_003Ec__DisplayClass387 obj5 = CS_0024_003C_003E8__locals388;
			_003C_003Ec__DisplayClass385 obj6 = CS_0024_003C_003E8__locals386;
			_003C_003Ec__DisplayClass383 obj7 = CS_0024_003C_003E8__locals384;
			_003C_003Ec__DisplayClass381 obj8 = CS_0024_003C_003E8__locals382;
			_003C_003Ec__DisplayClass37f obj9 = CS_0024_003C_003E8__locals380;
			_003C_003Ec__DisplayClass37c obj10 = CS_0024_003C_003E8__locals37d;
			_003C_003Ec__DisplayClass378 obj11 = CS_0024_003C_003E8__locals379;
			_003C_003Ec__DisplayClass374 obj12 = CS_0024_003C_003E8__locals375;
			_003C_003Ec__DisplayClass371 obj13 = CS_0024_003C_003E8__locals372;
			_003C_003Ec__DisplayClass36f obj14 = CS_0024_003C_003E8__locals370;
			_003C_003Ec__DisplayClass36d obj15 = CS_0024_003C_003E8__locals36e;
			_003C_003Ec__DisplayClass36b obj16 = CS_0024_003C_003E8__locals36c;
			_003C_003Ec__DisplayClass369 obj17 = CS_0024_003C_003E8__locals36a;
			_003C_003Ec__DisplayClass367 obj18 = CS_0024_003C_003E8__locals368;
			_003C_003Ec__DisplayClass365 obj19 = CS_0024_003C_003E8__locals366;
			int i = CS_0024_003C_003E8__locals368.results.SelectedIndex;
			if (i < 0)
			{
				return;
			}
			Tuple<ElementId, ElementId, string, string, string> tuple = clashRows.FirstOrDefault((Tuple<ElementId, ElementId, string, string, string> x) => x.Item3 == obj18.results.Items[i] as string);
			if (tuple == null)
			{
				return;
			}
			CS_0024_003C_003E8__locals366.ui.Selection.SetElementIds((ICollection<ElementId>)new List<ElementId> { tuple.Item1, tuple.Item2 });
			try
			{
				CS_0024_003C_003E8__locals366.ui.ShowElements((ICollection<ElementId>)new List<ElementId> { tuple.Item1, tuple.Item2 });
				CS_0024_003C_003E8__locals390.status.Text = "Both clash element IDs selected and framed in Revit.";
			}
			catch
			{
			}
		}

		public void _003CClashDetection_003Eb__35e(object param0, EventArgs param1)
		{
			try
			{
				string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BIMlify_Clash_Report.csv");
				File.WriteAllLines(path, new string[1] { "Clash,Element A,Element B,Discipline A,Reason" }.Concat(clashRows.Select((Tuple<ElementId, ElementId, string, string, string> x, int i) => "\"Clash " + (i + 1) + "\",\"" + x.Item1.IntegerValue + "\",\"" + x.Item2.IntegerValue + "\",\"" + x.Item4 + "\",\"" + x.Item5.Replace("\"", "'") + "\"")));
				CS_0024_003C_003E8__locals390.status.Text = "Report exported to Desktop: BIMlify_Clash_Report.csv";
			}
			catch (Exception ex)
			{
				CS_0024_003C_003E8__locals390.status.Text = "Could not export report: " + ex.Message;
			}
		}

		public void _003CClashDetection_003Eb__360(object param0, EventArgs param1)
		{
			runScan();
		}

		public void _003CClashDetection_003Eb__361(object param0, EventArgs param1)
		{
			ensureClashSplit();
			runScan();
		}

		private static string _003CClashDetection_003Eb__35f(Tuple<ElementId, ElementId, string, string, string> x, int i)
		{
			return "\"Clash " + (i + 1) + "\",\"" + x.Item1.IntegerValue + "\",\"" + x.Item2.IntegerValue + "\",\"" + x.Item4 + "\",\"" + x.Item5.Replace("\"", "'") + "\"";
		}
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass449
	{
		public Document doc;

		public List<View> allViews;

		public UIDocument ui;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass44b
	{
		public BIMlifyForm form;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass44d
	{
		public TabControl tabs;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass44f
	{
		public CheckedListBox viewList;
	}

	[CompilerGenerated]
	private sealed class _003C_003Ec__DisplayClass451
	{
		private sealed class _003C_003Ec__DisplayClass457
		{
			public _003C_003Ec__DisplayClass451 CS_0024_003C_003E8__locals452;

			public _003C_003Ec__DisplayClass44f CS_0024_003C_003E8__locals450;

			public _003C_003Ec__DisplayClass44d CS_0024_003C_003E8__locals44e;

			public _003C_003Ec__DisplayClass44b CS_0024_003C_003E8__locals44c;

			public _003C_003Ec__DisplayClass449 CS_0024_003C_003E8__locals44a;

			public int first;

			public int last;

			public bool check;

			public void _003CViewTemplates_003Eb__439()
			{
				if (CS_0024_003C_003E8__locals44c.form.IsDisposed)
				{
					return;
				}
				CS_0024_003C_003E8__locals452.applyingRange = true;
				try
				{
					for (int i = first; i <= last; i++)
					{
						CS_0024_003C_003E8__locals450.viewList.SetItemChecked(i, check);
					}
				}
				finally
				{
					CS_0024_003C_003E8__locals452.applyingRange = false;
				}
				if (CS_0024_003C_003E8__locals452.chosenTemplate != null)
				{
					CS_0024_003C_003E8__locals452.status.Text = CS_0024_003C_003E8__locals44a.allViews.Count((View v) =>
					{
						//IL_0001: Unknown result type (might be due to invalid IL or missing references)
						//IL_0011: Unknown result type (might be due to invalid IL or missing references)
						//IL_003b: Unknown result type (might be due to invalid IL or missing references)
						return v.ViewType == CS_0024_003C_003E8__locals452.chosenTemplate.ViewType && SearchTextMatches(CS_0024_003C_003E8__locals452.filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
					}) + " compatible · " + CS_0024_003C_003E8__locals450.viewList.CheckedItems.Count + " selected";
				}
			}

			public bool _003CViewTemplates_003Eb__43a(View v)
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0011: Unknown result type (might be due to invalid IL or missing references)
				//IL_003b: Unknown result type (might be due to invalid IL or missing references)
				return v.ViewType == CS_0024_003C_003E8__locals452.chosenTemplate.ViewType && SearchTextMatches(CS_0024_003C_003E8__locals452.filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
			}
		}

		public _003C_003Ec__DisplayClass44f CS_0024_003C_003E8__locals450;

		public _003C_003Ec__DisplayClass44d CS_0024_003C_003E8__locals44e;

		public _003C_003Ec__DisplayClass44b CS_0024_003C_003E8__locals44c;

		public _003C_003Ec__DisplayClass449 CS_0024_003C_003E8__locals44a;

		public Color bg;

		public Color ink;

		public Label viewsHeading;

		public Dictionary<string, ListBox> lists;

		public TextBox filter;

		public Label status;

		public GlassButton selectAll;

		public GlassButton clear;

		public GlassButton close;

		public GlassButton apply;

		public View chosenTemplate;

		public bool completed;

		public bool applyingRange;

		public bool rangePending;

		public bool rangeValue;

		public int rangeAnchor;

		public int rangeStart;

		public int rangeEnd;

		public HashSet<int> checkedIds;

		public Func<ListBox> activeTemplates;

		public Action captureChecks;

		public Action refreshViews;

		private static Func<View, bool> CS_0024_003C_003E9__CachedAnonymousMethodDelegate459;

		private static Func<View, ElementId> CS_0024_003C_003E9__CachedAnonymousMethodDelegate45a;

		public void _003CViewTemplates_003Eb__42f(object sender, DrawItemEventArgs e)
		{
			bool flag = e.Index == CS_0024_003C_003E8__locals44e.tabs.SelectedIndex;
			Rectangle bounds = e.Bounds;
			using (SolidBrush brush = new SolidBrush(flag ? Color.White : bg))
			{
				e.Graphics.FillRectangle(brush, bounds);
			}
			using (Font font = new Font("Segoe UI", 9f, flag ? FontStyle.Bold : FontStyle.Regular))
			{
				using SolidBrush brush2 = new SolidBrush(ink);
				using StringFormat format = new StringFormat
				{
					Alignment = StringAlignment.Center,
					LineAlignment = StringAlignment.Center,
					FormatFlags = StringFormatFlags.NoWrap,
					Trimming = StringTrimming.EllipsisCharacter
				};
				e.Graphics.DrawString(CS_0024_003C_003E8__locals44e.tabs.TabPages[e.Index].Text, font, brush2, bounds, format);
			}
			using (Pen pen = new Pen(Color.FromArgb(195, 198, 201), 1f))
			{
				e.Graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
			}
			if (flag)
			{
				using (Pen pen2 = new Pen(Color.FromArgb(214, 171, 61), 3f))
				{
					e.Graphics.DrawLine(pen2, bounds.Left + 2, bounds.Bottom - 2, bounds.Right - 3, bounds.Bottom - 2);
				}
			}
		}

		public ListBox _003CViewTemplates_003Eb__430()
		{
			return lists[CS_0024_003C_003E8__locals44e.tabs.SelectedTab.Text];
		}

		public void _003CViewTemplates_003Eb__431()
		{
			foreach (View item in CS_0024_003C_003E8__locals450.viewList.CheckedItems.Cast<View>())
			{
				checkedIds.Add(((Element)item).Id.IntegerValue);
			}
		}

		public void _003CViewTemplates_003Eb__432()
		{
			captureChecks();
			CS_0024_003C_003E8__locals450.viewList.Items.Clear();
			rangeAnchor = -1;
			rangePending = false;
			object selectedItem = activeTemplates().SelectedItem;
			chosenTemplate = (View)((selectedItem is View) ? selectedItem : null);
			apply.Enabled = chosenTemplate != null && !completed;
			if (chosenTemplate == null)
			{
				viewsHeading.Text = "2. SELECT VIEWS";
				status.Text = "Choose a template to load compatible views.";
				return;
			}
			viewsHeading.Text = "2. SELECT VIEWS · " + ((Element)chosenTemplate).Name;
			List<View> list = CS_0024_003C_003E8__locals44a.allViews.Where((View v) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_000c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0031: Unknown result type (might be due to invalid IL or missing references)
				return v.ViewType == chosenTemplate.ViewType && SearchTextMatches(filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
			}).ToList();
			foreach (View item in list)
			{
				int index = CS_0024_003C_003E8__locals450.viewList.Items.Add(item);
				if (checkedIds.Contains(((Element)item).Id.IntegerValue))
				{
					CS_0024_003C_003E8__locals450.viewList.SetItemChecked(index, value: true);
				}
			}
			status.Text = ((list.Count == 0) ? "No compatible views match this filter." : (list.Count + " compatible view(s) · " + CS_0024_003C_003E8__locals450.viewList.CheckedItems.Count + " selected."));
		}

		public void _003CViewTemplates_003Eb__434(object param0, EventArgs param1)
		{
			CS_0024_003C_003E8__locals450.viewList.Items.Clear();
			checkedIds.Clear();
			if (activeTemplates().Items.Count > 0)
			{
				activeTemplates().SelectedIndex = 0;
			}
			refreshViews();
		}

		public void _003CViewTemplates_003Eb__436(object param0, EventArgs param1)
		{
			refreshViews();
		}

		public void _003CViewTemplates_003Eb__437(object sender, MouseEventArgs e)
		{
			int num = CS_0024_003C_003E8__locals450.viewList.IndexFromPoint(e.Location);
			if (num >= 0)
			{
				if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift && rangeAnchor >= 0)
				{
					rangeStart = Math.Min(rangeAnchor, num);
					rangeEnd = Math.Max(rangeAnchor, num);
					rangeValue = !CS_0024_003C_003E8__locals450.viewList.GetItemChecked(num);
					rangePending = true;
				}
				else
				{
					rangeAnchor = num;
					rangePending = false;
				}
			}
		}

		public void _003CViewTemplates_003Eb__438(object sender, ItemCheckEventArgs e)
		{
			object obj = CS_0024_003C_003E8__locals450.viewList.Items[e.Index];
			View val = (View)((obj is View) ? obj : null);
			if (val != null)
			{
				if (e.NewValue == CheckState.Checked)
				{
					checkedIds.Add(((Element)val).Id.IntegerValue);
				}
				else
				{
					checkedIds.Remove(((Element)val).Id.IntegerValue);
				}
			}
			if (!applyingRange && rangePending && e.Index == rangeEnd)
			{
				_003C_003Ec__DisplayClass44f obj2 = CS_0024_003C_003E8__locals450;
				_003C_003Ec__DisplayClass44d obj3 = CS_0024_003C_003E8__locals44e;
				_003C_003Ec__DisplayClass44b obj4 = CS_0024_003C_003E8__locals44c;
				_003C_003Ec__DisplayClass449 obj5 = CS_0024_003C_003E8__locals44a;
				int first = rangeStart;
				int last = rangeEnd;
				bool check = rangeValue;
				rangePending = false;
				rangeAnchor = rangeEnd;
				CS_0024_003C_003E8__locals44c.form.BeginInvoke((Action)(() =>
				{
					if (!obj4.form.IsDisposed)
					{
						applyingRange = true;
						try
						{
							for (int i = first; i <= last; i++)
							{
								obj2.viewList.SetItemChecked(i, check);
							}
						}
						finally
						{
							applyingRange = false;
						}
						if (chosenTemplate != null)
						{
							status.Text = obj5.allViews.Count((View v) =>
							{
								//IL_0001: Unknown result type (might be due to invalid IL or missing references)
								//IL_0011: Unknown result type (might be due to invalid IL or missing references)
								//IL_003b: Unknown result type (might be due to invalid IL or missing references)
								return v.ViewType == chosenTemplate.ViewType && SearchTextMatches(filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
							}) + " compatible · " + obj2.viewList.CheckedItems.Count + " selected";
						}
					}
				}));
			}
			else
			{
				if (applyingRange)
				{
					return;
				}
				rangeAnchor = e.Index;
				CS_0024_003C_003E8__locals44c.form.BeginInvoke((Action)(() =>
				{
					if (!CS_0024_003C_003E8__locals44c.form.IsDisposed && chosenTemplate != null)
					{
						status.Text = CS_0024_003C_003E8__locals44a.allViews.Count((View v) =>
						{
							//IL_0001: Unknown result type (might be due to invalid IL or missing references)
							//IL_000c: Unknown result type (might be due to invalid IL or missing references)
							//IL_0031: Unknown result type (might be due to invalid IL or missing references)
							return v.ViewType == chosenTemplate.ViewType && SearchTextMatches(filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
						}) + " compatible · " + CS_0024_003C_003E8__locals450.viewList.CheckedItems.Count + " selected";
					}
				}));
			}
		}

		public void _003CViewTemplates_003Eb__43d(object param0, EventArgs param1)
		{
			for (int i = 0; i < CS_0024_003C_003E8__locals450.viewList.Items.Count; i++)
			{
				CS_0024_003C_003E8__locals450.viewList.SetItemChecked(i, value: true);
			}
			captureChecks();
			status.Text = CS_0024_003C_003E8__locals450.viewList.Items.Count + " view(s) selected.";
		}

		public void _003CViewTemplates_003Eb__43e(object param0, EventArgs param1)
		{
			checkedIds.Clear();
			for (int i = 0; i < CS_0024_003C_003E8__locals450.viewList.Items.Count; i++)
			{
				CS_0024_003C_003E8__locals450.viewList.SetItemChecked(i, value: false);
			}
			status.Text = "Selection cleared.";
		}

		public void _003CViewTemplates_003Eb__43f(object param0, EventArgs param1)
		{
			//IL_0084: Unknown result type (might be due to invalid IL or missing references)
			//IL_0124: Unknown result type (might be due to invalid IL or missing references)
			//IL_012a: Expected Obj, but got Unknown
			//IL_012c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0140: Unknown result type (might be due to invalid IL or missing references)
			//IL_014a: Expected Obj, but got Unknown
			//IL_014c: Unknown result type (might be due to invalid IL or missing references)
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "Revit projects (*.rvt)|*.rvt",
				Title = "Import View Templates from Revit project",
				CheckFileExists = true
			};
			if (openFileDialog.ShowDialog(CS_0024_003C_003E8__locals44c.form) != DialogResult.OK)
			{
				return;
			}
			Document val = null;
			try
			{
				status.Text = "Opening external project…";
				val = CS_0024_003C_003E8__locals44a.ui.Application.Application.OpenDocumentFile(openFileDialog.FileName);
				List<ElementId> list = (from View v in (IEnumerable)new FilteredElementCollector(val).OfClass(typeof(View))
					where v.IsTemplate
					select ((Element)v).Id).ToList();
				if (list.Count == 0)
				{
					status.Text = "No view templates were found in the selected project.";
					return;
				}
				Transaction val2 = new Transaction(CS_0024_003C_003E8__locals44a.doc, "BIMlify — Import View Templates");
				try
				{
					val2.Start();
					ElementTransformUtils.CopyElements(val, (ICollection<ElementId>)list, CS_0024_003C_003E8__locals44a.doc, (Transform)null, new CopyPasteOptions());
					val2.Commit();
				}
				finally
				{
					((IDisposable)val2)?.Dispose();
				}
				status.Text = "Imported " + list.Count + " view template(s). Reopen or refresh the template list to use them.";
			}
			catch (Exception ex)
			{
				status.Text = "Could not import templates: " + ex.Message;
			}
			finally
			{
				if (val != null)
				{
					try
					{
						val.Close(false);
					}
					catch
					{
					}
				}
			}
		}

		public void _003CViewTemplates_003Eb__442(object param0, EventArgs param1)
		{
			if (completed)
			{
				CS_0024_003C_003E8__locals44c.form.Close();
			}
		}

		public void _003CViewTemplates_003Eb__443(object param0, EventArgs param1)
		{
			//IL_025f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0265: Invalid comparison between Unknown and I4
			//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d8: Expected Obj, but got Unknown
			//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_013a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0140: Invalid comparison between Unknown and I4
			//IL_0275: Unknown result type (might be due to invalid IL or missing references)
			if (completed)
			{
				CS_0024_003C_003E8__locals44c.form.Close();
				return;
			}
			captureChecks();
			object selectedItem = activeTemplates().SelectedItem;
			chosenTemplate = (View)((selectedItem is View) ? selectedItem : null);
			List<View> list = CS_0024_003C_003E8__locals450.viewList.CheckedItems.Cast<View>().ToList();
			if (chosenTemplate == null)
			{
				status.Text = "Choose a view template first.";
				return;
			}
			if (list.Count == 0)
			{
				status.Text = "Check at least one compatible view.";
				return;
			}
			int num = 0;
			int num2 = 0;
			Transaction val = null;
			try
			{
				val = new Transaction(CS_0024_003C_003E8__locals44a.doc, "BIMlify — Apply view template");
				val.Start();
				foreach (View item in list)
				{
					try
					{
						item.ViewTemplateId = ((Element)chosenTemplate).Id;
						num++;
					}
					catch
					{
						num2++;
					}
				}
				if ((int)val.Commit() != 3)
				{
					status.Text = "Revit could not apply this template. Review the selection and try again.";
					return;
				}
				completed = true;
				apply.Text = "Done";
				apply.Enabled = true;
				close.Text = "Close";
				selectAll.Enabled = false;
				clear.Enabled = false;
				CS_0024_003C_003E8__locals44e.tabs.Enabled = false;
				filter.Enabled = false;
				CS_0024_003C_003E8__locals450.viewList.Enabled = false;
				status.Text = "Applied '" + ((Element)chosenTemplate).Name + "' to " + num + " view(s)." + ((num2 > 0) ? (" Skipped: " + num2 + ".") : "");
			}
			catch (Exception ex)
			{
				try
				{
					if (val != null && (int)val.GetStatus() == 1)
					{
						val.RollBack();
					}
				}
				catch
				{
				}
				status.Text = "Could not apply template: " + ex.Message;
			}
			finally
			{
				if (val != null)
				{
					val.Dispose();
				}
			}
		}

		public bool _003CViewTemplates_003Eb__444(string name)
		{
			return lists[name].Items.Count > 0;
		}

		public bool _003CViewTemplates_003Eb__433(View v)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			return v.ViewType == chosenTemplate.ViewType && SearchTextMatches(filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
		}

		public void _003CViewTemplates_003Eb__43b()
		{
			if (!CS_0024_003C_003E8__locals44c.form.IsDisposed && chosenTemplate != null)
			{
				status.Text = CS_0024_003C_003E8__locals44a.allViews.Count((View v) =>
				{
					//IL_0001: Unknown result type (might be due to invalid IL or missing references)
					//IL_000c: Unknown result type (might be due to invalid IL or missing references)
					//IL_0031: Unknown result type (might be due to invalid IL or missing references)
					return v.ViewType == chosenTemplate.ViewType && SearchTextMatches(filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
				}) + " compatible · " + CS_0024_003C_003E8__locals450.viewList.CheckedItems.Count + " selected";
			}
		}

		public bool _003CViewTemplates_003Eb__43c(View v)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			return v.ViewType == chosenTemplate.ViewType && SearchTextMatches(filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
		}

		private static bool _003CViewTemplates_003Eb__440(View v)
		{
			return v.IsTemplate;
		}

		private static ElementId _003CViewTemplates_003Eb__441(View v)
		{
			return ((Element)v).Id;
		}
	}

	private static SoundPlayer clickSoundPlayer;

	private static bool ReturnToMainMenu;

	private static BIMlifyForm dashboardWindow;

	private static BIMlifyForm dashboardQuickBarWindow;


	private static UIApplication dashboardApplication;

	private static IntPtr dashboardRevitHandle = IntPtr.Zero;

	private static bool dashboardClosing;

	private static ExternalEvent dashboardExternalEvent;

	private static DashboardActionHandler dashboardActionHandler;

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

	[DllImport("gdi32.dll", SetLastError = true)]
	private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

	[DllImport("gdi32.dll", SetLastError = true)]
	private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

	[DllImport("gdi32.dll", SetLastError = true)]
	private static extern bool DeleteObject(IntPtr handle);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

	private static void AddWindowTitleBar(BIMlifyForm form)
	{
		LinkedClashSupport.AttachSecondaryWindowBehavior(form);
		BIMlifyForm form2 = form;
		Panel bar = new Panel
		{
			Dock = DockStyle.Top,
			Height = 34,
			BackColor = Color.FromArgb(232, 234, 237)
		};
		bool flag = string.Equals(form2.Tag as string, "autohide", StringComparison.Ordinal) || string.Equals(form2.Tag as string, "manual", StringComparison.Ordinal);
		int num = 1 + (form2.MinimizeBox ? 1 : 0) + (form2.MaximizeBox ? 1 : 0) + (flag ? 1 : 0);
		int captionWidth = num * 32 + Math.Max(0, num - 1) * 6 + 14;
		Label title = new Label
		{
			Left = 0,
			Top = 0,
			Width = Math.Max(80, bar.Width - captionWidth),
			Height = 34,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			Padding = new Padding(11, 0, 0, 0),
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = Color.FromArgb(78, 85, 92),
			Font = new Font("Segoe UI", 8.5f),
			AutoEllipsis = true,
			UseMnemonic = false
		};
		title.Text = form2.Text;
		form2.TextChanged += (object param0, EventArgs param1) =>
		{
			title.Text = form2.Text;
		};
		title.MouseDown += (object sender, MouseEventArgs e) =>
		{
			if (e.Button == MouseButtons.Left)
			{
				ReleaseCapture();
				SendMessage(form2.Handle, 161, (IntPtr)2, IntPtr.Zero);
			}
		};
		title.DoubleClick += (object param0, EventArgs param1) =>
		{
			form2.WindowState = ((form2.WindowState != FormWindowState.Maximized) ? FormWindowState.Maximized : FormWindowState.Normal);
		};
		bar.Controls.Add(title);
		List<CaptionButton> captionButtons = new List<CaptionButton>();
		if (form2.MinimizeBox)
		{
			captionButtons.Add(new CaptionButton(form2, 0, bar.BackColor));
		}
		if (form2.MaximizeBox)
		{
			captionButtons.Add(new CaptionButton(form2, 1, bar.BackColor));
		}
		if (flag)
		{
			captionButtons.Add(new CaptionButton(form2, 3, bar.BackColor));
		}
		captionButtons.Add(new CaptionButton(form2, 2, bar.BackColor));
		foreach (CaptionButton item in captionButtons)
		{
			item.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			item.Top = 1;
			bar.Controls.Add(item);
		}
		Action layoutCaption = () =>
		{
			title.Left = 0;
			title.Width = Math.Max(40, bar.ClientSize.Width - captionWidth);
			int num2 = bar.ClientSize.Width - 14 - captionButtons.Count * 32 - Math.Max(0, captionButtons.Count - 1) * 6;
			foreach (CaptionButton item2 in captionButtons)
			{
				item2.Left = num2;
				num2 += item2.Width + 6;
			}
		};
		bar.Resize += (object param0, EventArgs param1) =>
		{
			layoutCaption();
		};
		layoutCaption();
		bar.MouseDown += (object sender, MouseEventArgs e) =>
		{
			if (e.Button == MouseButtons.Left)
			{
				ReleaseCapture();
				SendMessage(form2.Handle, 161, (IntPtr)2, IntPtr.Zero);
			}
		};
		form2.Controls.Add(bar);
	}

	private static void AddBackNavigation(BIMlifyForm form, Action goBack)
	{
		Panel row = new Panel
		{
			Dock = DockStyle.Top,
			Height = 40,
			BackColor = Color.FromArgb(238, 241, 245),
			Padding = new Padding(10, 2, 0, 2)
		};
		GlassButton glassButton = new GlassButton();
		glassButton.Text = "";
		glassButton.Width = 36;
		glassButton.Height = 36;
		glassButton.Left = 12;
		glassButton.Top = 2;
		glassButton.MinimumSize = new Size(36, 36);
		glassButton.AccessibleName = "Back";
		GlassButton glassButton2 = glassButton;
		ToolTip toolTip = new ToolTip();
		toolTip.SetToolTip(glassButton2, "Back");
		glassButton2.Click += (object param0, EventArgs param1) =>
		{
			if (goBack != null)
			{
				goBack();
			}
		};
		row.Controls.Add(glassButton2);
		row.Paint += (object sender, PaintEventArgs e) =>
		{
			using Pen pen = new Pen(Color.FromArgb(210, 214, 218), 1f);
			e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
		};
		form.Controls.Add(row);
	}

	private static void ApplyGlassBackdrop(IntPtr hwnd)
	{
		IntPtr intPtr = IntPtr.Zero;
		try
		{
			AccentPolicy structure = new AccentPolicy
			{
				State = 4,
				Flags = 2,
				GradientColor = -572135188,
				AnimationId = 0
			};
			int num = Marshal.SizeOf(typeof(AccentPolicy));
			intPtr = Marshal.AllocHGlobal(num);
			Marshal.StructureToPtr(structure, intPtr, fDeleteOld: false);
			WindowCompositionAttributeData data = new WindowCompositionAttributeData
			{
				Attribute = 19,
				Data = intPtr,
				SizeOfData = num
			};
			SetWindowCompositionAttribute(hwnd, ref data);
		}
		catch
		{
		}
		finally
		{
			if (intPtr != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(intPtr);
			}
		}
	}

	private static void UseSmoothCorners(Form form)
	{
		form.Opacity = 1.0;
		BIMlifyForm bimlifyForm = form as BIMlifyForm;
		bool nativeRounded = false;
		Action applyWindowShape = () =>
		{
			try
			{
				if (form.IsHandleCreated && !form.IsDisposed)
				{
					if (nativeRounded)
					{
						SetWindowRgn(form.Handle, IntPtr.Zero, redraw: true);
						if (bimlifyForm != null)
						{
							bimlifyForm.NativeRoundedCorners = true;
						}
					}
					else
					{
						IntPtr intPtr = ((form.WindowState == FormWindowState.Maximized) ? CreateRectRgn(0, 0, form.Width + 1, form.Height + 1) : CreateRoundRectRgn(0, 0, form.Width + 1, form.Height + 1, 24, 24));
						if (SetWindowRgn(form.Handle, intPtr, redraw: true) == 0)
						{
							DeleteObject(intPtr);
						}
					}
				}
			}
			catch
			{
			}
		};
		form.Resize += (object param0, EventArgs param1) =>
		{
			applyWindowShape();
		};
		form.Shown += (object param0, EventArgs param1) =>
		{
			try
			{
				int value = 3;
				nativeRounded = DwmSetWindowAttribute(form.Handle, 33, ref value, 4) == 0;
			}
			catch
			{
				nativeRounded = false;
			}
			if (bimlifyForm != null)
			{
				bimlifyForm.NativeRoundedCorners = nativeRounded;
			}
			try
			{
				int value2 = 1;
				DwmSetWindowAttribute(form.Handle, 20, ref value2, 4);
			}
			catch
			{
			}
			try
			{
				int value3 = 14804199;
				DwmSetWindowAttribute(form.Handle, 35, ref value3, 4);
			}
			catch
			{
			}
			try
			{
				int value4 = 3620164;
				DwmSetWindowAttribute(form.Handle, 36, ref value4, 4);
			}
			catch
			{
			}
			try
			{
				int value5 = 9213599;
				DwmSetWindowAttribute(form.Handle, 34, ref value5, 4);
			}
			catch
			{
			}
			applyWindowShape();
			try
			{
				form.PerformLayout();
				RefreshVisibleControls(form);
				form.Invalidate(invalidateChildren: true);
				form.Update();
			}
			catch
			{
			}
		};
	}

	private static void RefreshVisibleControls(Control root)
	{
		if (root == null || root.IsDisposed || !root.Visible) return;
		try
		{
			root.Invalidate(invalidateChildren: true);
			foreach (Control control in root.Controls)
			{
				if (control != null && !control.IsDisposed) RefreshVisibleControls(control);
			}
		}
		catch (ObjectDisposedException) { }
		catch (InvalidOperationException) { }
	}

	private static Control CreateBrandLockup(Color background, string description = null)
	{
		return Branding.Create(background, description);
	}

	private static PictureBox CreateSmallMark(Control parent)
	{
		string text = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location), "Branding", "LogoMarkOnlyGoldTransparent.png");
		PictureBox mark = new PictureBox
		{
			Width = 40,
			Height = 52,
			Left = Math.Max(0, parent.ClientSize.Width - 64),
			Top = 16,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right),
			SizeMode = PictureBoxSizeMode.Zoom,
			BackColor = parent.BackColor,
			Margin = Padding.Empty
		};
		if (File.Exists(text))
		{
			using Image original = Image.FromFile(text);
			mark.Image = new Bitmap(original);
		}
		parent.SizeChanged += (object param0, EventArgs param1) =>
		{
			mark.Left = Math.Max(0, parent.ClientSize.Width - mark.Width - 16);
		};
		return mark;
	}

	private static string PreferenceFilePath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIMlify", "settings.ini");
	}

	private static bool GetPreference(string key, bool defaultValue)
	{
		try
		{
			string path = PreferenceFilePath();
			if (!File.Exists(path))
			{
				return defaultValue;
			}
			string[] array = File.ReadAllLines(path);
			foreach (string text in array)
			{
				int num = text.IndexOf('=');
				bool result;
				if (num > 0 && string.Equals(text.Substring(0, num).Trim(), key, StringComparison.OrdinalIgnoreCase))
				{
					return bool.TryParse(text.Substring(num + 1).Trim(), out result) ? result : defaultValue;
				}
			}
		}
		catch
		{
		}
		return defaultValue;
	}

	private static void SetPreference(string key, bool value)
	{
		try
		{
			string path = PreferenceFilePath();
			string directoryName = Path.GetDirectoryName(path);
			if (!Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			List<string> list = (File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>());
			bool flag = false;
			for (int i = 0; i < list.Count; i++)
			{
				int num = list[i].IndexOf('=');
				if (num > 0 && string.Equals(list[i].Substring(0, num).Trim(), key, StringComparison.OrdinalIgnoreCase))
				{
					list[i] = key + "=" + value;
					flag = true;
				}
			}
			if (!flag)
			{
				list.Add(key + "=" + value);
			}
			File.WriteAllLines(path, list.ToArray());
		}
		catch
		{
		}
	}

	private static void StyleSearchBox(TextBox search, Color background, Color foreground, string cue)
	{
		TextBox search2 = search;
		search2.AutoSize = false;
		search2.BorderStyle = BorderStyle.None;
		if (search2.Height < 30)
		{
			search2.Height = 30;
		}
		search2.BackColor = Color.White;
		search2.ForeColor = Color.FromArgb(78, 85, 92);
		search2.Font = new Font("Segoe UI", 8.5f);
		search2.Margin = new Padding(4, 0, 4, 0);
		search2.Padding = new Padding(42, 0, 10, 0);
		search2.AccessibleName = (string.IsNullOrWhiteSpace(cue) ? "Advanced search" : cue);
		Action centerSearchText = () =>
		{
			if (!search2.IsHandleCreated || search2.ClientSize.Width < 4 || search2.ClientSize.Height < 4)
			{
				return;
			}
			int num = Math.Max(12, search2.Font.Height);
			int num2 = Math.Max(2, (search2.ClientSize.Height - num) / 2 - 1);
			NativeRect structure = new NativeRect
			{
				Left = 24,
				Top = num2,
				Right = Math.Max(25, search2.ClientSize.Width - 12),
				Bottom = Math.Min(search2.ClientSize.Height - 2, num2 + num + 2)
			};
			IntPtr intPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeRect)));
			try
			{
				Marshal.StructureToPtr(structure, intPtr, fDeleteOld: false);
				SendMessage(search2.Handle, 180, IntPtr.Zero, intPtr);
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr);
			}
		};
		Action roundSearch = () =>
		{
			if (search2.Width >= 4 && search2.Height >= 4)
			{
				using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, search2.Width - 1, search2.Height - 1), 8))
				{
					Region region = search2.Region;
					search2.Region = new Region(path);
					region?.Dispose();
				}
				search2.Invalidate();
			}
		};
		search2.HandleCreated += (object param0, EventArgs param1) =>
		{
			roundSearch();
			centerSearchText();
		};
		search2.Resize += (object param0, EventArgs param1) =>
		{
			roundSearch();
			centerSearchText();
		};
		search2.Paint += (object sender, PaintEventArgs e) =>
		{
			using Pen pen = new Pen(Color.FromArgb(210, 215, 220), 1f);
			using GraphicsPath path = RoundedPath(new Rectangle(0, 0, search2.Width - 1, search2.Height - 1), 8);
			e.Graphics.DrawPath(pen, path);
		};
		search2.AccessibleDescription = "Searches update as you type. Use multiple words (all must match), \"quoted phrases\", -word to exclude, and a trailing * for word-prefix matching. Press Esc to clear.";
		Action applyCue = () =>
		{
			if (search2.IsHandleCreated)
			{
				if (search2 is AdvancedSearchTextBox advancedSearchTextBox)
				{
					advancedSearchTextBox.SetSearchMargin();
				}
				if (!string.IsNullOrEmpty(cue))
				{
					IntPtr intPtr = Marshal.StringToHGlobalUni(cue);
					try
					{
						SendMessage(search2.Handle, 5377, (IntPtr)1, intPtr);
					}
					finally
					{
						Marshal.FreeHGlobal(intPtr);
					}
				}
			}
		};
		search2.HandleCreated += (object param0, EventArgs param1) =>
		{
			applyCue();
		};
		if (search2.IsHandleCreated)
		{
			applyCue();
		}
		if (!search2.ReadOnly)
		{
			search2.KeyDown += (object sender, KeyEventArgs e) =>
			{
				if (e.KeyCode == Keys.Escape && !string.IsNullOrEmpty(search2.Text))
				{
					search2.Clear();
					e.SuppressKeyPress = true;
					e.Handled = true;
				}
				else if (e.Control && e.KeyCode == Keys.Back && search2.SelectionLength == 0 && search2.SelectionStart > 0)
				{
					int selectionStart = search2.SelectionStart;
					int num = selectionStart;
					while (num > 0 && char.IsWhiteSpace(search2.Text[num - 1]))
					{
						num--;
					}
					while (num > 0 && !char.IsWhiteSpace(search2.Text[num - 1]))
					{
						num--;
					}
					search2.Text = search2.Text.Remove(num, selectionStart - num);
					search2.SelectionStart = num;
					e.SuppressKeyPress = true;
					e.Handled = true;
				}
			};
			ToolTip tip = new ToolTip
			{
				InitialDelay = 500,
				ReshowDelay = 120,
				AutoPopDelay = 7000,
				ShowAlways = true
			};
			tip.SetToolTip(search2, "Advanced search: words are combined, use \"phrases\", -word to exclude, prefix* to match word starts. Esc clears.");
			search2.Disposed += (object param0, EventArgs param1) =>
			{
				tip.Dispose();
			};
		}
		EmptyState.ApplySearchTextInset(search);
	}

	private static void DrawComboText(ComboBox combo, Color ink, Color gold)
	{
		combo.DrawItem += (object sender, DrawItemEventArgs e) =>
		{
			if (e.Index < 0)
			{
				return;
			}
			using (SolidBrush brush = new SolidBrush((e.Index == combo.SelectedIndex || (e.State & DrawItemState.Selected) != 0) ? Color.FromArgb(255, 240, 201) : Color.FromArgb(247, 248, 249)))
			{
				e.Graphics.FillRectangle(brush, e.Bounds);
			}
			using SolidBrush brush2 = new SolidBrush(ink);
			using StringFormat format = new StringFormat
			{
				LineAlignment = StringAlignment.Center,
				Alignment = StringAlignment.Near,
				FormatFlags = StringFormatFlags.NoWrap
			};
			e.Graphics.DrawString(combo.Items[e.Index].ToString(), combo.Font, brush2, new RectangleF(e.Bounds.Left + 10, e.Bounds.Top, e.Bounds.Width - 30, e.Bounds.Height), format);
		};
	}

	private static bool SearchTextMatches(string query, params string[] fields)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return true;
		}
		string text = string.Join(" ", fields.Where((string field) => !string.IsNullOrEmpty(field)));
		MatchCollection matchCollection = Regex.Matches(query, "(-?)\"([^\"]+)\"|([^\\s,;|]+)");
		List<Tuple<string, bool, bool>> list = new List<Tuple<string, bool, bool>>();
		foreach (Match item in matchCollection)
		{
			string text2 = (item.Groups[2].Success ? item.Groups[2].Value : item.Groups[3].Value);
			bool flag = (item.Groups[2].Success ? (item.Groups[1].Value == "-") : text2.StartsWith("-", StringComparison.Ordinal));
			if (item.Groups[2].Success && item.Groups[1].Value == "-")
			{
				flag = true;
			}
			else if (!item.Groups[2].Success && flag)
			{
				text2 = text2.Substring(1);
			}
			bool flag2 = !flag && text2.EndsWith("*", StringComparison.Ordinal);
			if (flag2)
			{
				text2 = text2.TrimEnd('*');
			}
			if (text2.Length > 0)
			{
				list.Add(Tuple.Create(text2, flag, flag2));
			}
		}
		string[] source = text.Split(new char[19]
		{
			' ', '\t', '\r', '\n', '-', '_', '/', '\\', '.', ',',
			':', ';', '|', '(', ')', '[', ']', '{', '}'
		}, StringSplitOptions.RemoveEmptyEntries);
		foreach (Tuple<string, bool, bool> term in list)
		{
			bool flag3 = (term.Item3 ? source.Any((string word) => word.StartsWith(term.Item1, StringComparison.CurrentCultureIgnoreCase)) : (text.IndexOf(term.Item1, StringComparison.CurrentCultureIgnoreCase) >= 0));
			if (term.Item2 && flag3)
			{
				return false;
			}
			if (!term.Item2 && !flag3)
			{
				return false;
			}
		}
		return true;
	}

	private static void FitLabelTextToOneLine(Label label, string text, float minimumSize)
	{
		label.Text = text ?? string.Empty;
		label.AutoSize = false;
		label.AutoEllipsis = false;
		label.UseMnemonic = false;
		if (label.ClientSize.Width < 20 || label.ClientSize.Height < 1 || string.IsNullOrEmpty(label.Text))
		{
			return;
		}
		FitLabelFontState fitLabelFontState = label.Tag as FitLabelFontState;
		if (fitLabelFontState == null)
		{
			Font font = label.Font;
			FitLabelFontState fitLabelFontState2 = new FitLabelFontState();
			fitLabelFontState2.FamilyName = font.FontFamily.Name;
			fitLabelFontState2.Style = font.Style;
			fitLabelFontState2.BaseSize = font.Size;
			fitLabelFontState = (FitLabelFontState)(label.Tag = fitLabelFontState2);
			label.Disposed += (object sender, EventArgs e) =>
			{
				if (sender is Label { Tag: FitLabelFontState { OwnedFont: not null } tag })
				{
					tag.OwnedFont.Dispose();
					tag.OwnedFont = null;
				}
			};
		}
		float num = Math.Max(minimumSize, Math.Min(fitLabelFontState.BaseSize, 80f));
		Font font2 = null;
		while (num >= minimumSize)
		{
			Font font3 = new Font(fitLabelFontState.FamilyName, num, fitLabelFontState.Style);
			if (TextRenderer.MeasureText(label.Text, font3).Width <= label.ClientSize.Width)
			{
				font2 = font3;
				break;
			}
			font3.Dispose();
			num -= 0.25f;
		}
		if (font2 == null)
		{
			font2 = new Font(fitLabelFontState.FamilyName, Math.Max(1f, minimumSize), fitLabelFontState.Style);
		}
		Font ownedFont = fitLabelFontState.OwnedFont;
		label.Font = font2;
		fitLabelFontState.OwnedFont = font2;
		if (ownedFont != null && !object.ReferenceEquals(ownedFont, font2))
		{
			ownedFont.Dispose();
		}
	}

	private static void PlayClickSound()
	{
		if (!GetPreference("ButtonClickSounds", defaultValue: true))
		{
			return;
		}
		try
		{
			if (clickSoundPlayer == null)
			{
				string text = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location), "Branding", "ClickTone.wav");
				if (!File.Exists(text))
				{
					return;
				}
				clickSoundPlayer = new SoundPlayer(text);
				clickSoundPlayer.Load();
			}
			clickSoundPlayer.Play();
		}
		catch
		{
		}
	}

	private static string ShowDashboard()
	{
		string result = null;
		BIMlifyForm form = null;
		form = CreateDashboard((string action) =>
		{
			form.Tag = action;
			form.DialogResult = DialogResult.OK;
			form.Close();
		});
		using (form)
		{
			if (BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(form) == DialogResult.OK)
			{
				result = form.Tag as string;
			}
		}
		return result;
	}

	private static BIMlifyForm CreateDashboard(Action<string> launchAction)
	{
		BIMlifyForm form = new BIMlifyForm();
		Color backColor = Color.FromArgb(253, 253, 252);
		Color cardColor = Color.FromArgb(255, 255, 255);
		Color color = Color.FromArgb(78, 85, 92);
		Color muted = Color.FromArgb(116, 120, 125);
		Color yellow = Color.FromArgb(214, 171, 61);
		form.Text = "BIMlify";
		form.Width = 1080;
		form.Height = 720;
		form.MinimumSize = new Size(980, 650);
		form.StartPosition = FormStartPosition.CenterScreen;
		form.ShowInTaskbar = true;
		form.ForceTaskbarButton = true;
		form.ShowIcon = false;
		form.MaximizeBox = true;
		form.MinimizeBox = true;
		form.AutoScaleMode = AutoScaleMode.Dpi;
		form.BackColor = backColor;
		form.ForeColor = color;
		form.Font = new Font("Segoe UI", 9f);
		UseSmoothCorners(form);
		Panel panel = new Panel();
		panel.Dock = DockStyle.Top;
		panel.Height = 154;
		panel.BackColor = Color.FromArgb(248, 242, 222);
		panel.Padding = new Padding(14, 8, 10, 8);
		Panel panel2 = panel;
		Control control = CreateBrandLockup(panel2.BackColor, "A collection of tools to simplify your BIM workflow.\nSimplify BIM. Amplify clarity.");
		control.Dock = DockStyle.Left;
		panel2.Controls.Add(control);
		AddYellowPanelAccent(panel2, yellow, atTop: false);
		TableLayoutPanel groups = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			Padding = new Padding(18, 22, 18, 20),
			BackColor = Color.FromArgb(252, 252, 251)
		};
		groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
		groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
		groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
		groups.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		AddDashboardGroup(groups, 0, "PROJECT REVIEW", cardColor, color, muted, new string[4] { "Clash Detection", "Review & Clean", "Smart Cleaner", "Smart Openings" }, new string[4] { "clashdetection", "cleanup", "smartcleaner", "smartopenings" }, new string[4] { "clash.png", "review.png", "smartcleaner.png", "smartopenings.png" }, launchAction);
		AddDashboardGroup(groups, 1, "VIEW TOOLS", cardColor, color, muted, new string[7] { "3D Room", "BIM Library", "Level Bubbles", "Pattern Maker", "Plan View Range", "Smart Sheets", "Templates" }, new string[7] { "wet3d", "bimlibrary", "bubbles", "patternmaker", "scaletags", "sheetorganization", "viewtemplates" }, new string[7] { "room3d.png", "library.png", "level.png", "patternmaker.png", "planviewrange.png", "sheetorganization.png", "viewtemplates.png" }, launchAction);
		AddDashboardGroup(groups, 2, "SELECTION", cardColor, color, muted, new string[2] { "Select / Isolate", "Select By ID" }, new string[2] { "categorytools", "selectbyid" }, new string[2] { "select.png", "selectbyid.png" }, launchAction);
		Panel footer = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 28,
			BackColor = backColor,
			Padding = new Padding(0, 0, 18, 0)
		};
		Font font = new Font("Segoe UI", 7f, FontStyle.Regular);
		Font font2 = new Font("Segoe UI", 7f, FontStyle.Bold);
		Color foreColor = Color.FromArgb(148, 151, 154);
		Color foreColor2 = Color.FromArgb(198, 151, 42);
		Label copyrightPrefix = new Label
		{
			Text = "© 2026 ",
			ForeColor = foreColor,
			Font = font,
			TextAlign = ContentAlignment.MiddleLeft,
			UseMnemonic = false,
			BackColor = backColor,
			Height = 28,
			Top = -4,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right)
		};
		Label copyrightBrand = new Label
		{
			Text = "BIMlify",
			ForeColor = foreColor2,
			Font = font2,
			TextAlign = ContentAlignment.MiddleLeft,
			UseMnemonic = false,
			BackColor = backColor,
			Height = 28,
			Top = -4,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right)
		};
		Label copyrightSuffix = new Label
		{
			Text = ". All rights reserved.",
			ForeColor = foreColor,
			Font = font,
			TextAlign = ContentAlignment.MiddleLeft,
			UseMnemonic = false,
			BackColor = backColor,
			Height = 28,
			Top = -4,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right)
		};
		int prefixWidth = TextRenderer.MeasureText(copyrightPrefix.Text, font, new Size(240, 28), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
		int brandWidth = TextRenderer.MeasureText(copyrightBrand.Text, font2, new Size(120, 28), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
		int suffixWidth = TextRenderer.MeasureText(copyrightSuffix.Text, font, new Size(240, 28), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
		copyrightPrefix.Width = prefixWidth;
		copyrightBrand.Width = brandWidth;
		copyrightSuffix.Width = suffixWidth;
		Action layoutCopyright = () =>
		{
			int num = footer.ClientSize.Width - footer.Padding.Right;
			copyrightSuffix.Left = num - suffixWidth;
			copyrightBrand.Left = copyrightSuffix.Left - brandWidth;
			copyrightPrefix.Left = copyrightBrand.Left - prefixWidth;
		};
		footer.Resize += (object param0, EventArgs param1) =>
		{
			layoutCopyright();
		};
		layoutCopyright();
		footer.Controls.Add(copyrightPrefix);
		footer.Controls.Add(copyrightBrand);
		footer.Controls.Add(copyrightSuffix);
		form.Controls.Add(groups);
		form.Controls.Add(footer);
		form.Controls.Add(panel2);
		AddWindowSidebar(form, dashboard: true, launchAction);
		form.Shown += (object param0, EventArgs param1) =>
		{
			groups.PerformLayout();
			RefreshVisibleControls(form);
		};
		BIMlifyForm bIMlifyForm = form;
		UiPolish.ApplyDashboard(bIMlifyForm);
		return bIMlifyForm;
	}

	private static string ShowBottomToolBar(UIApplication application)
	{
		string text = null;
		IntPtr intPtr = ((application == null) ? IntPtr.Zero : application.MainWindowHandle);
		using (BIMlifyForm bIMlifyForm = CreateQuickBarWindow(application, intPtr, autoHide: false))
		{
			IWin32Window owner = ((intPtr == IntPtr.Zero) ? null : new NativeWindowOwner(intPtr));
			if (bIMlifyForm.ShowDialog(owner) == DialogResult.OK)
			{
				text = bIMlifyForm.Tag as string;
			}
		}
		if (text == "restore" || text == "closebar")
		{
			return "home";
		}
		return text;
	}

	private static BIMlifyForm CreateQuickBarWindow(UIApplication application, IntPtr revitHandle, bool autoHide)
	{
		BIMlifyForm form = new BIMlifyForm();
		TableLayoutPanel tools = new TableLayoutPanel();
		Color backColor = Color.FromArgb(214, 219, 225);
		string[] array = new string[10] { "Review & Clean", "Smart Cleaner", "Level Bubbles", "3D Room", "Templates", "Pattern Maker", "Plan View Range", "Select / Isolate", "Select By ID", "BIM Library" };
		string[] array2 = new string[10] { "cleanup", "smartcleaner", "bubbles", "wet3d", "viewtemplates", "patternmaker", "scaletags", "categorytools", "selectbyid", "bimlibrary" };
		string[] array3 = new string[10] { "review.png", "smartcleaner.png", "level.png", "room3d.png", "viewtemplates.png", "patternmaker.png", "planviewrange.png", "select.png", "selectbyid.png", "library.png" };
		form.Text = "BIMlify — Quick Bar";
		form.Width = 1100;
		form.Height = 76;
		form.MinimumSize = new Size(900, 76);
		form.MaximumSize = new Size(Math.Max(900, QuickBarWorkingArea().Width), 76);
		form.AllowResize = false;
		form.Tag = (autoHide ? "autohide" : "manual");
		form.MaximizeBox = false;
		form.MinimizeBox = false;
		form.ShowIcon = false;
		form.ShowInTaskbar = false;
		form.TopMost = true;
		form.StartPosition = FormStartPosition.Manual;
		form.AutoScaleMode = AutoScaleMode.Dpi;
		form.BackColor = backColor;
		form.Font = new Font("Segoe UI", 8.5f);
		UseSmoothCorners(form);
		form.Opacity = 1.0;
		tools.Dock = DockStyle.Fill;
		tools.ColumnCount = array.Length;
		tools.RowCount = 1;
		tools.BackColor = backColor;
		tools.Padding = new Padding(8, 3, 8, 3);
		tools.Margin = Padding.Empty;
		for (int i = 0; i < array.Length; i++)
		{
			tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / (float)array.Length));
		}
		tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		ToolTip tips = new ToolTip
		{
			InitialDelay = 220,
			ReshowDelay = 80,
			AutoPopDelay = 3600,
			ShowAlways = true
		};
		form.Disposed += (object param0, EventArgs param1) =>
		{
			tips.Dispose();
		};
		for (int num = 0; num < array.Length; num++)
		{
			string toolAction = array2[num];
			string text = array3[num];
			string text2 = array[num];
			string path = ((text == "restore" || text == "close") ? string.Empty : Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location), "Icons", text));
			string specialGlyph;
			if (text == "restore")
			{
				specialGlyph = "restore";
			}
			else
			{
				specialGlyph = ((text == "close") ? "close" : null);
			}
			QuickToolButton quickToolButton = new QuickToolButton(text2, path, specialGlyph);
			quickToolButton.Height = 40;
			quickToolButton.Margin = Padding.Empty;
			quickToolButton.Dock = DockStyle.Fill;
			QuickToolButton quickToolButton2 = quickToolButton;
			tips.SetToolTip(quickToolButton2, DashboardToolDescription(toolAction));
			quickToolButton2.Click += (object param0, EventArgs param1) =>
			{
				//IL_0057: Unknown result type (might be due to invalid IL or missing references)
				PlayClickSound();
				if (string.Equals(form.Tag as string, "autohide", StringComparison.Ordinal) && dashboardActionHandler != null && dashboardExternalEvent != null)
				{
					dashboardActionHandler.Queue(toolAction);
					dashboardExternalEvent.Raise();
				}
				else
				{
					form.Tag = toolAction;
					form.DialogResult = DialogResult.OK;
					form.Close();
				}
			};
			tools.Controls.Add(quickToolButton2, num, 0);
		}
		form.Controls.Add(tools);
		AddWindowTitleBar(form);
		form.Shown += (object param0, EventArgs param1) =>
		{
			try
			{
			NativeRect rect = default;
			int num2 = ((revitHandle != IntPtr.Zero && GetWindowRect(revitHandle, out rect)) ? (rect.Right - rect.Left) : 0);
			if (num2 < 900)
			{
				num2 = QuickBarWorkingArea().Width;
			}
			num2 = Math.Min(1100, Math.Min(num2, QuickBarWorkingArea().Width - 24));
			form.Size = new Size(Math.Max(900, num2), 76);
			form.MinimumSize = new Size(900, 76);
			form.MaximumSize = new Size(1100, 76);
			form.Location = QuickBarLocation(revitHandle, form.Width, form.Height);
			bool magneticallyDocked = true;
			bool applyingDockPosition = false;
			bool clampingQuickBar = false;
			form.LocationChanged += (object sender, EventArgs e) =>
			{
				if (applyingDockPosition || clampingQuickBar) return;
				magneticallyDocked = false;
				// Clamp synchronously while the user drags, rather than waiting for the
				// next timer tick (which allowed the bar to disappear off-screen).
				clampingQuickBar = true;
				try { ClampQuickBarInsideScreen(form); }
				finally { clampingQuickBar = false; }
			};
			tools.PerformLayout();
			RefreshVisibleControls(form);
			Timer hoverTimer = new Timer
			{
				Interval = 60
			};
			DateTime shownAt = DateTime.Now;
			hoverTimer.Tick += (object obj, EventArgs e) =>
			{
			try
			{
				if (form.IsDisposed)
				{
					hoverTimer.Stop();
					hoverTimer.Dispose();
				}
				else
				{
					ClampQuickBarInsideScreen(form);
					NativeRect rect2 = default;
					bool flag = revitHandle != IntPtr.Zero && GetWindowRect(revitHandle, out rect2);
					if (flag)
					{
						int num3 = rect2.Right - rect2.Left;
						if (num3 < 900)
						{
							num3 = QuickBarWorkingArea().Width;
						}
						num3 = Math.Min(1100, Math.Min(num3, QuickBarWorkingArea().Width - 24));
						if (form.Width != Math.Max(900, num3))
						{
							form.Width = Math.Max(900, num3);
						}
						Point dockLocation = QuickBarLocation(revitHandle, form.Width, form.Height);
				// Compare the bar's top-left corner to the designed dock point. A wider
				// capture zone makes the magnetic return reliable after a free drag.
						int horizontalSnapRange = Math.Max(360, form.Width / 2);
						int verticalSnapRange = Math.Max(200, form.Height * 3);
						bool closeToDock = Math.Abs(form.Left - dockLocation.X) <= horizontalSnapRange && Math.Abs(form.Top - dockLocation.Y) <= verticalSnapRange;
						if (magneticallyDocked || closeToDock)
						{
							if (form.Location != dockLocation)
							{
								applyingDockPosition = true;
								try { form.Location = dockLocation; }
								finally { applyingDockPosition = false; }
							}
							magneticallyDocked = true;
						}
					}
					if (autoHide)
					{						// Base hover visibility on the bar's current bounds so dragging it
						// away from its dock position does not leave it dim while hovered.
						Rectangle hoverBounds = form.Bounds;
						hoverBounds.Inflate(18, 18);
						bool cursorNearBar = hoverBounds.Contains(Cursor.Position);
						// A subtle dim while idle keeps the bar readable instead of nearly transparent.
						double targetOpacity = (cursorNearBar || (DateTime.Now - shownAt).TotalMilliseconds < 1100.0) ? 1.0 : 0.82;
						double opacityDelta = targetOpacity - form.Opacity;
						if (Math.Abs(opacityDelta) > 0.005)
						{
							form.Opacity += Math.Sign(opacityDelta) * Math.Min(0.06, Math.Abs(opacityDelta));
						}
					}
				}
			
			}
			catch (Exception tickError)
			{
				LogQuickBarFailure(tickError);
				try { hoverTimer.Stop(); } catch { }
			}
		};
			form.FormClosed += (object obj, FormClosedEventArgs e) =>
			{
				hoverTimer.Stop();
				hoverTimer.Dispose();
			};
			hoverTimer.Start();
		
			}
			catch (Exception shownError)
			{
				LogQuickBarFailure(shownError);
			}
		};
		return form;
	}
	private static void LogQuickBarFailure(Exception exception)
	{
		try
		{
			string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMlify");
			Directory.CreateDirectory(directory);
			File.AppendAllText(Path.Combine(directory, "QuickBar.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + exception + Environment.NewLine);
		}
		catch { }
	}

	private static void ClampQuickBarInsideScreen(Form form)
	{
		if (form == null || form.IsDisposed) return;
		Rectangle workArea;
		try { workArea = Screen.FromRectangle(form.Bounds)?.WorkingArea ?? QuickBarWorkingArea(); }
		catch { workArea = QuickBarWorkingArea(); }
		int maxLeft = Math.Max(workArea.Left, workArea.Right - form.Width);
		int maxTop = Math.Max(workArea.Top, workArea.Bottom - form.Height);
		int left = Math.Min(Math.Max(workArea.Left, form.Left), maxLeft);
		int top = Math.Min(Math.Max(workArea.Top, form.Top), maxTop);
		if (form.Left != left || form.Top != top) form.Location = new Point(left, top);
	}


	private static Point QuickBarLocation(IntPtr revitHandle, int width, int height)
	{
		if (revitHandle != IntPtr.Zero && GetWindowRect(revitHandle, out var rect))
		{
			return new Point(rect.Left + Math.Max(0, (rect.Right - rect.Left - width) / 2), Math.Max(rect.Top, rect.Bottom - height - 38));
		}
		Rectangle workingArea = QuickBarWorkingArea();
		return new Point(workingArea.Left + Math.Max(0, (workingArea.Width - width) / 2), Math.Max(workingArea.Top, workingArea.Bottom - height - 8));
	}

	private static Rectangle QuickBarWorkingArea()
	{
		try
		{
			Screen primary = Screen.PrimaryScreen;
			if (primary != null && primary.WorkingArea.Width > 0 && primary.WorkingArea.Height > 0)
				return primary.WorkingArea;
		}
		catch { }
		try
		{
			Rectangle working = SystemInformation.WorkingArea;
			if (working.Width > 0 && working.Height > 0) return working;
		}
		catch { }
		Rectangle virtualBounds = SystemInformation.VirtualScreen;
		return virtualBounds.Width > 0 && virtualBounds.Height > 0 ? virtualBounds : new Rectangle(0, 0, 1920, 1080);
	}

	private static Image LoadQuickBarIcon(string path)
	{
		using Bitmap bitmap = new Bitmap(path);
		int num = bitmap.Width;
		int num2 = bitmap.Height;
		int num3 = -1;
		int num4 = -1;
		for (int i = 0; i < bitmap.Height; i++)
		{
			for (int j = 0; j < bitmap.Width; j++)
			{
				Color pixel = bitmap.GetPixel(j, i);
				if (pixel.A > 20 && (pixel.R > 20 || pixel.G > 20 || pixel.B > 20))
				{
					if (j < num)
					{
						num = j;
					}
					if (j > num3)
					{
						num3 = j;
					}
					if (i < num2)
					{
						num2 = i;
					}
					if (i > num4)
					{
						num4 = i;
					}
				}
			}
		}
		if (num3 < num)
		{
			num = 0;
			num2 = 0;
			num3 = bitmap.Width - 1;
			num4 = bitmap.Height - 1;
		}
		int num5 = Math.Max(1, num3 - num + 1);
		int num6 = Math.Max(1, num4 - num2 + 1);
		Bitmap bitmap2 = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap2))
		{
			graphics.Clear(Color.Transparent);
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			float num7 = Math.Min(28f / (float)num5, 28f / (float)num6);
			int num8 = Math.Max(1, (int)Math.Round((float)num5 * num7));
			int num9 = Math.Max(1, (int)Math.Round((float)num6 * num7));
			int x = (32 - num8) / 2;
			int y = (32 - num9) / 2;
			graphics.DrawImage(bitmap, new Rectangle(x, y, num8, num9), new Rectangle(num, num2, num5, num6), GraphicsUnit.Pixel);
		}
		return bitmap2;
	}

	private static string DashboardToolDescription(string action)
	{
		return action switch
		{
			"cleanup" => "Review project content and remove items you no longer need.", 
			"smartcleaner" => "Scan unused items, audit views and warnings, then clean selected content.", 
			"clashdetection" => "Find overlapping model elements and review potential coordination clashes.", 
			"bubbles" => "Reposition elevation markers on selected levels in your project model.", 
			"wet3d" => "Create a focused 3D view for selected rooms or spaces.", 
			"wetplan" => "Create a cropped plan view for a selected room.", 
			"blockplan" => "Create a cropped plan view around selected blockwork walls.", 
			"style3d" => "Apply BIMlify presentation graphics to the active 3D view.", 
			"viewtemplates" => "Browse and apply templates to plans, RCPs, elevations, sections, and 3D views.", 
			"patternmaker" => "Generate a model or drafting tile fill pattern from dimensions and a short description.", 
			"scaletags" => "Read the active plan view range and inspect its vertical planes in a 3D schematic.", 
			"bimlibrary" => "Browse trusted Revit family libraries or load families from your BIMlify Library folder.", 
			"sheetorganization" => "Group, sequence, and renumber project sheets using your company naming convention.", 
			"categorytools" => "Choose categories, then select or temporarily isolate matching elements.", 
			"selectcategories" => "Select model elements by BIM category.", 
			"isolatecategories" => "Temporarily isolate model elements by BIM category.", 
			"selectbyid" => "Enter one or more element IDs to select those elements in your model.", 
			_ => "A helpful tool for your BIM workflow.", 
		};
	}

	private static void AddDashboardGroup(TableLayoutPanel host, int column, string name, Color cardColor, Color ink, Color muted, string[] labels, string[] actions, string[] icons, Action<string> launchAction)
	{
		RoundedCardPanel roundedCardPanel = new RoundedCardPanel();
		roundedCardPanel.Dock = DockStyle.Fill;
		roundedCardPanel.BackColor = cardColor;
		roundedCardPanel.Margin = new Padding(8);
		roundedCardPanel.Padding = new Padding(12);
		Panel panel = roundedCardPanel;
		Font tipFont = new Font("Segoe UI", 8f, FontStyle.Regular);
		ToolTip toolTip = new ToolTip
		{
			InitialDelay = 550,
			ReshowDelay = 120,
			AutoPopDelay = 9000,
			ShowAlways = true,
			IsBalloon = false,
			OwnerDraw = true
		};
		panel.Tag = toolTip;
		panel.Disposed += (object param0, EventArgs param1) =>
		{
			toolTip.Dispose();
		};
		toolTip.Disposed += (object param0, EventArgs param1) =>
		{
			tipFont.Dispose();
		};
		toolTip.Popup += (object sender, PopupEventArgs e) =>
		{
			Size size = TextRenderer.MeasureText(toolTip.GetToolTip(e.AssociatedControl), tipFont, new Size(320, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
			e.ToolTipSize = new Size(Math.Min(340, Math.Max(150, size.Width + 16)), Math.Min(96, size.Height + 12));
		};
		toolTip.Draw += (object sender, DrawToolTipEventArgs e) =>
		{
			e.DrawBackground();
			e.DrawBorder();
			Rectangle bounds = e.Bounds;
			bounds.Inflate(-8, -5);
			TextRenderer.DrawText(e.Graphics, e.ToolTipText, tipFont, bounds, Color.FromArgb(78, 85, 92), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
		};
		Label label = new Label();
		label.Text = name;
		label.Dock = DockStyle.Top;
		label.Height = 34;
		label.ForeColor = muted;
		label.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
		label.TextAlign = ContentAlignment.MiddleLeft;
		Label value = label;
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
		flowLayoutPanel.Dock = DockStyle.Fill;
		flowLayoutPanel.FlowDirection = FlowDirection.LeftToRight;
		flowLayoutPanel.WrapContents = true;
		flowLayoutPanel.AutoScroll = true;
		flowLayoutPanel.BackColor = cardColor;
		flowLayoutPanel.Padding = new Padding(0, 8, 0, 0);
		FlowLayoutPanel flowLayoutPanel2 = flowLayoutPanel;
		panel.Controls.Add(flowLayoutPanel2);
		panel.Controls.Add(value);
		for (int num = 0; num < labels.Length; num++)
		{
			string action = actions[num];
			string path = icons[num];
			Panel tile = new Panel
			{
				Width = 84,
				Height = 94,
				Margin = new Padding(2),
				BackColor = cardColor,
				Cursor = Cursors.Hand
			};
			PictureBox picture = new PictureBox
			{
				Width = 40,
				Height = 40,
				Left = 22,
				Top = 3,
				SizeMode = PictureBoxSizeMode.Zoom,
				BackColor = cardColor,
				Cursor = Cursors.Hand
			};
			Label label2 = new Label
			{
				Width = 82,
				Height = 38,
				Left = 1,
				Top = 54,
				Text = labels[num],
				UseMnemonic = false,
				TextAlign = ContentAlignment.TopCenter,
				ForeColor = ink,
				Font = new Font("Segoe UI", 7.5f),
				BackColor = cardColor,
				Cursor = Cursors.Hand
			};
			string path2 = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location), "Icons", path);
			if (File.Exists(path2))
			{
				picture.Image = LoadDashboardIcon(path2);
			}
			Action activate = () =>
			{
				PlayClickSound();
				if (launchAction != null)
				{
					launchAction(action);
				}
			};
			tile.Click += (object param0, EventArgs param1) =>
			{
				activate();
			};
			picture.Click += (object param0, EventArgs param1) =>
			{
				activate();
			};
			label2.Click += (object param0, EventArgs param1) =>
			{
				activate();
			};
			int iconTarget = 40;
			bool tileHovered = false;
			int pulse = 0;
			bool pulseForward = true;
			tile.Paint += (object sender, PaintEventArgs e) =>
			{
				int num2 = 8 + pulse * 10 / 100;
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(0, 218, 174, 65)))
				{
					e.Graphics.FillEllipse(brush, new Rectangle(picture.Left - 3, picture.Top - 3, picture.Width + 6, picture.Height + 6));
				}
				if (!tileHovered)
				{
					return;
				}
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				Rectangle rect = new Rectangle(1, 0, tile.Width - 2, tile.Height - 8);
				using (GraphicsPath pathGlow = RoundedPath(rect, 12))
				{
					using SolidBrush brush2 = new SolidBrush(Color.FromArgb(255, 252, 245));
					e.Graphics.FillPath(brush2, pathGlow);
				}
				using GraphicsPath pathBorder = RoundedPath(rect, 12);
				using Pen pen = new Pen(Color.FromArgb(239, 226, 197), 0.65f);
				e.Graphics.DrawPath(pen, pathBorder);
			};
			Timer pulseTimer = new Timer
			{
				Interval = 80
			};
			pulseTimer.Tick += (object param0, EventArgs param1) =>
			{
				if (pulseForward)
				{
					pulse += 5;
				}
				else
				{
					pulse -= 5;
				}
				if (pulse >= 100)
				{
					pulse = 100;
					pulseForward = false;
				}
				else if (pulse <= 0)
				{
					pulse = 0;
					pulseForward = true;
				}
				tile.Invalidate();
			};
			Timer iconMotion = new Timer
			{
				Interval = 15
			};
			iconMotion.Tick += (object param0, EventArgs param1) =>
			{
				int width = picture.Width;
				if (width == iconTarget)
				{
					iconMotion.Stop();
				}
				else
				{
					width += ((width < iconTarget) ? 3 : (-3));
					if (Math.Abs(width - iconTarget) < 3)
					{
						width = iconTarget;
					}
					picture.Size = new Size(width, width);
					picture.Left = (tile.Width - width) / 2;
					picture.Top = Math.Max(0, 3 - (width - 42) / 2);
				}
			};
			tile.Disposed += (object param0, EventArgs param1) =>
			{
				pulseTimer.Stop();
				pulseTimer.Dispose();
				iconMotion.Stop();
				iconMotion.Dispose();
			};
			pulseTimer.Start();
			Action<bool> setHover = (bool active) =>
			{
				tileHovered = active;
				iconTarget = active ? 46 : 40;
				tile.BackColor = cardColor;
				picture.BackColor = Color.Transparent;
				label2.BackColor = Color.Transparent;
				label2.ForeColor = (active ? Color.FromArgb(174, 137, 53) : ink);
				tile.Invalidate();
				iconMotion.Start();
			};
			Timer hoverWatch = new Timer
			{
				Interval = 70
			};
			hoverWatch.Tick += (object param0, EventArgs param1) =>
			{
				if (tileHovered && !tile.ClientRectangle.Contains(tile.PointToClient(Cursor.Position)))
				{
					setHover(obj: false);
				}
			};
			tile.Disposed += (object param0, EventArgs param1) =>
			{
				hoverWatch.Stop();
				hoverWatch.Dispose();
			};
			EventHandler value2 = (object param0, EventArgs param1) =>
			{
				setHover(obj: true);
			};
			EventHandler value3 = (object param0, EventArgs param1) =>
			{
				if (!tile.ClientRectangle.Contains(tile.PointToClient(Control.MousePosition)))
				{
					setHover(obj: false);
				}
			};
			tile.MouseEnter += value2;
			picture.MouseEnter += value2;
			label2.MouseEnter += value2;
			tile.MouseLeave += value3;
			picture.MouseLeave += value3;
			label2.MouseLeave += value3;
			hoverWatch.Start();
			string caption = DashboardToolDescription(action);
			toolTip.SetToolTip(tile, caption);
			toolTip.SetToolTip(picture, caption);
			toolTip.SetToolTip(label2, caption);
			tile.Controls.Add(picture);
			tile.Controls.Add(label2);
			flowLayoutPanel2.Controls.Add(tile);
		}
		host.Controls.Add(panel, column, 0);
	}

	private static Image LoadDashboardIcon(string path)
	{
		using Image image = Image.FromFile(path);
		int num = image.Width;
		int num2 = image.Height;
		int num3 = -1;
		int num4 = -1;
		using Bitmap bitmap = new Bitmap(image);
		for (int i = 0; i < bitmap.Height; i++)
		{
			for (int j = 0; j < bitmap.Width; j++)
			{
				if (bitmap.GetPixel(j, i).A > 16)
				{
					if (j < num)
					{
						num = j;
					}
					if (j > num3)
					{
						num3 = j;
					}
					if (i < num2)
					{
						num2 = i;
					}
					if (i > num4)
					{
						num4 = i;
					}
				}
			}
		}
		if (num3 < num || num4 < num2)
		{
			return new Bitmap(bitmap);
		}
		Rectangle rect = new Rectangle(num, num2, num3 - num + 1, num4 - num2 + 1);
		using Bitmap original = bitmap.Clone(rect, PixelFormat.Format32bppArgb);
		return new Bitmap(original);
	}

	private static void AddYellowPanelAccent(Panel panel, Color yellow, bool atTop)
	{
		if (!atTop)
		{
			List<Label> list = (from label in panel.Controls.OfType<Label>()
				where label.Visible
				orderby label.Top
				select label).ToList();
			if (list.Count > 0)
			{
				foreach (Label item in list)
				{
					if (item.Height > 28)
					{
						if (TextRenderer.MeasureText(item.Text ?? string.Empty, item.Font, new Size(Math.Max(1, item.Width), 1000), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height <= 22)
						{
							item.Height = 22;
							item.TextAlign = ContentAlignment.MiddleLeft;
						}
						else
						{
							item.TextAlign = ContentAlignment.TopLeft;
						}
					}
				}
				int num = list.Max((Label label) => label.Bottom) - list.Min((Label label) => label.Top);
				int num2 = Math.Max(0, panel.Height - 2 - 14 - num);
				int num3 = num2 - list.Min((Label label) => label.Top);
				foreach (Label item2 in list)
				{
					item2.Top += num3;
				}
			}
		}
		panel.Paint += (object sender, PaintEventArgs e) =>
		{
			int num4 = (atTop ? 1 : (panel.Height - 2));
			using (Pen pen = new Pen(yellow, 2f))
			{
				e.Graphics.DrawLine(pen, 1, num4, panel.Width - 2, num4);
			}
			if (!atTop)
			{
				List<Label> list2 = (from label in panel.Controls.OfType<Label>()
					where label.Visible && label.Width > 120
					orderby label.Top
					select label).ToList();
				if (list2.Count > 0)
				{
					int y = Math.Max(8, list2.Min((Label label) => label.Top) - 2);
					int y2 = Math.Min(panel.Height - 12, list2.Max((Label label) => label.Bottom) + 2);
					using Pen pen2 = new Pen(Color.FromArgb(224, 194, 116), 1.25f);
					e.Graphics.DrawLine(pen2, 9, y, 9, y2);
				}
			}
		};
	}

	private static GraphicsPath RoundedPath(Rectangle rect, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		if (rect.Width < 2 || rect.Height < 2)
		{
			return graphicsPath;
		}
		int val = Math.Min(rect.Width, rect.Height) / 2;
		radius = Math.Max(1, Math.Min(radius, val));
		int num = radius * 2;
		graphicsPath.AddArc(rect.X, rect.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(rect.X, rect.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private static Color BlendColor(Color baseColor, Color overlay, float amount)
	{
		amount = Math.Max(0f, Math.Min(1f, amount));
		return Color.FromArgb((int)((float)(int)baseColor.R + (float)(overlay.R - baseColor.R) * amount), (int)((float)(int)baseColor.G + (float)(overlay.G - baseColor.G) * amount), (int)((float)(int)baseColor.B + (float)(overlay.B - baseColor.B) * amount));
	}

	private static void AddWindowSidebar(BIMlifyForm form, bool dashboard, Action<string> launchAction = null)
	{
		form.Controls.Add(new BIMlifySidebar(form, dashboard, launchAction));
		BIMlify.CleanerUi.DashboardEnhancements.AttachToolSearchButton(form, dashboard ? 60 : 108);
		AddWindowTitleBar(form);
	}

	private static void ShowContactOptions(Form owner)
	{
		Color backColor = Color.FromArgb(238, 241, 245);
		Color foreColor = Color.FromArgb(78, 85, 92);
		Color foreColor2 = Color.FromArgb(102, 109, 118);
		using BIMlifyForm bIMlifyForm = new BIMlifyForm();
		bIMlifyForm.Text = "BIMlify — Contact";
		bIMlifyForm.Width = 600;
		bIMlifyForm.Height = 430;
		bIMlifyForm.MinimumSize = new Size(560, 410);
		bIMlifyForm.StartPosition = FormStartPosition.CenterScreen;
		bIMlifyForm.MaximizeBox = false;
		bIMlifyForm.MinimizeBox = false;
		bIMlifyForm.ShowIcon = false;
		bIMlifyForm.AutoScaleMode = AutoScaleMode.Dpi;
		bIMlifyForm.BackColor = backColor;
		bIMlifyForm.Font = new Font("Segoe UI", 9f);
		UseSmoothCorners(bIMlifyForm);
		Panel panel = new Panel();
		panel.Dock = DockStyle.Top;
		panel.Height = 75;
		panel.BackColor = Color.FromArgb(247, 247, 245);
		panel.Padding = new Padding(18, 12, 18, 6);
		Panel panel2 = panel;
		panel2.Controls.Add(new Label
		{
			Text = "Contact BIMlify",
			Left = 18,
			Top = 10,
			Width = 300,
			Height = 24,
			ForeColor = foreColor,
			Font = new Font("Segoe UI", 10f, FontStyle.Bold)
		});
		panel2.Controls.Add(new Label
		{
			Text = "Choose a way to get in touch.",
			Left = 18,
			Top = 37,
			Width = 340,
			Height = 20,
			ForeColor = foreColor2,
			Font = new Font("Segoe UI", 8f)
		});
		AddYellowPanelAccent(panel2, Color.FromArgb(214, 171, 61), atTop: false);
		Panel panel3 = new Panel();
		panel3.Dock = DockStyle.Fill;
		panel3.BackColor = backColor;
		panel3.Padding = new Padding(18);
		Panel panel4 = panel3;
		RoundedCardPanel contactCard = new RoundedCardPanel
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(28),
			BackColor = Color.White,
			Padding = new Padding(14)
		};
		Label status = new Label
		{
			Text = "",
			Left = 14,
			Top = 75,
			Width = 332,
			Height = 24,
			ForeColor = foreColor2,
			TextAlign = ContentAlignment.MiddleCenter,
			Font = new Font("Segoe UI", 8f)
		};
		Label emailIcon = new Label
		{
			Text = "✉",
			Left = 54,
			Top = 29,
			Width = 24,
			Height = 30,
			ForeColor = Color.FromArgb(214, 171, 61),
			Font = new Font("Segoe UI Symbol", 14f),
			TextAlign = ContentAlignment.MiddleCenter
		};
		Label linkedinIcon = new Label
		{
			Text = "in",
			Left = 206,
			Top = 31,
			Width = 24,
			Height = 26,
			ForeColor = Color.FromArgb(214, 171, 61),
			Font = new Font("Segoe UI", 10f, FontStyle.Bold),
			TextAlign = ContentAlignment.MiddleCenter
		};
		GlassButton email = new GlassButton
		{
			Text = "Email",
			Width = 128,
			Height = 34,
			Left = 40,
			Top = 27
		};
		GlassButton linkedin = new GlassButton
		{
			Text = "LinkedIn",
			Width = 128,
			Height = 34,
			Left = 192,
			Top = 27
		};
		contactCard.Controls.Add(email);
		contactCard.Controls.Add(linkedin);
		contactCard.Controls.Add(emailIcon);
		contactCard.Controls.Add(linkedinIcon);
		contactCard.Controls.Add(status);
		emailIcon.BringToFront();
		linkedinIcon.BringToFront();
		Action layoutContact = () =>
		{
			int num = Math.Max(20, (contactCard.ClientSize.Width - 280) / 2);
			int num2 = Math.Max(18, (contactCard.ClientSize.Height - 34) / 2);
			email.Left = num;
			email.Top = num2;
			linkedin.Left = num + 152;
			linkedin.Top = num2;
			emailIcon.Left = num + 14;
			emailIcon.Top = num2 + 2;
			linkedinIcon.Left = num + 166;
			linkedinIcon.Top = num2 + 4;
		};
		contactCard.Resize += (object param0, EventArgs param1) =>
		{
			layoutContact();
		};
		panel4.Controls.Add(contactCard);
		layoutContact();
		Action<string> open = (string url) =>
		{
			try
			{
				Process.Start(new ProcessStartInfo(url)
				{
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				status.Text = "Could not open the link: " + ex.Message;
			}
		};
		email.Click += (object param0, EventArgs param1) =>
		{
			open("mailto:Ealjohari@yahoo.com");
		};
		linkedin.Click += (object param0, EventArgs param1) =>
		{
			open("https://www.linkedin.com/in/esra-a-e-al-johari-msc-434056a5/");
		};
		bIMlifyForm.Controls.Add(panel4);
		bIMlifyForm.Controls.Add(panel2);
		AddWindowTitleBar(bIMlifyForm);
		bIMlifyForm.ShowDialog(owner);
	}

	private static Panel CreateDialogFooter()
	{
		Panel panel = new Panel();
		panel.Dock = DockStyle.Bottom;
		panel.Height = 64;
		panel.BackColor = Color.FromArgb(216, 221, 227);
		panel.Padding = new Padding(12, 12, 12, 8);
		Panel panel2 = panel;
		AddFooterAccent(panel2);
		return panel2;
	}

	private static void AddFooterAccent(Panel footer)
	{
		footer.Paint += (object sender, PaintEventArgs e) =>
		{
			using Pen pen = new Pen(Color.FromArgb(214, 171, 61), 1.35f);
			e.Graphics.DrawLine(pen, 0, footer.Height - 2, footer.Width, footer.Height - 2);
		};
	}

	private static Panel CreateFooterButtonGroup(bool rightAligned, int width)
	{
		Panel panel = new Panel();
		panel.Dock = (rightAligned ? DockStyle.Right : DockStyle.Left);
		panel.Width = width;
		panel.Height = 38;
		panel.BackColor = Color.FromArgb(216, 221, 227);
		panel.Margin = Padding.Empty;
		panel.Padding = Padding.Empty;
		return panel;
	}

	private static void PlaceFooterButton(Panel group, GlassButton button, int left, int width)
	{
		int num = Math.Max(width, button.MinimumSize.Width);
		int num2 = ((group.Dock == DockStyle.Right) ? 18 : 0);
		if (group.Width < left + num + num2)
		{
			group.Width = left + num + num2;
		}
		button.Dock = DockStyle.None;
		button.Margin = Padding.Empty;
		button.Left = left;
		button.Top = 3;
		button.Width = num;
		button.Height = 30;
		group.Controls.Add(button);
		BIMlify.CleanerUi.FooterCloseOrder.Attach(group);
	}

	private static void EnableShiftRangeChecks(CheckedListBox list, Form owner)
	{
		int anchor = -1;
		bool applyingRange = false;
		list.MouseDown += (object sender, MouseEventArgs e) =>
		{
			int num = list.IndexFromPoint(e.Location);
			if (num >= 0)
			{
				if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift && anchor >= 0)
				{
					int first = Math.Min(anchor, num);
					int last = Math.Max(anchor, num);
					bool check = !list.GetItemChecked(num);
					anchor = num;
					list.BeginInvoke((Action)(() =>
					{
						if (owner.IsDisposed || list.IsDisposed)
						{
							return;
						}
						applyingRange = true;
						try
						{
							for (int i = first; i <= last; i++)
							{
								list.SetItemChecked(i, check);
							}
						}
						finally
						{
							applyingRange = false;
						}
					}));
				}
				else
				{
					anchor = num;
				}
			}
		};
		list.ItemCheck += (object sender, ItemCheckEventArgs e) =>
		{
			if (!applyingRange && (Control.ModifierKeys & Keys.Shift) != Keys.Shift)
			{
				anchor = e.Index;
			}
		};
	}

	private static void ShowSettings(Form owner)
	{
		string text = null;
		BIMlifyForm form = new BIMlifyForm();
		try
		{
			form.Text = "BIMlify — Settings";
			form.Tag = "settings";
			form.Width = 600;
			form.Height = 430;
			form.MinimumSize = new Size(560, 410);
			form.StartPosition = FormStartPosition.CenterScreen;
			form.MaximizeBox = true;
			form.MinimizeBox = true;
			form.ShowIcon = false;
			form.AutoScaleMode = AutoScaleMode.Dpi;
			form.BackColor = Color.FromArgb(238, 241, 245);
			form.Font = new Font("Segoe UI", 9f);
			UseSmoothCorners(form);
			Panel panel = new Panel();
			panel.Dock = DockStyle.Fill;
			panel.BackColor = form.BackColor;
			panel.Padding = new Padding(24, 14, 82, 12);
			Panel panel2 = panel;
			Color foreColor = Color.FromArgb(78, 85, 92);
			Color foreColor2 = Color.FromArgb(102, 109, 118);
			Label label = new Label();
			label.Text = "GENERAL";
			label.Left = 24;
			label.Top = 18;
			label.Width = 210;
			label.Height = 22;
			label.ForeColor = foreColor;
			label.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
			Label value = label;
			CheckBox tips = new CheckBox
			{
				Text = "Show tooltips on the main menu",
				Left = 24,
				Top = 51,
				Width = 390,
				Height = 22,
				Checked = GetPreference("ShowTooltips", defaultValue: true),
				ForeColor = foreColor,
				Font = new Font("Segoe UI", 8.8f),
				FlatStyle = FlatStyle.System
			};
			Label label2 = new Label();
			label2.Text = "Show a short description when you point to a tool.";
			label2.Left = 48;
			label2.Top = 76;
			label2.Width = 390;
			label2.Height = 22;
			label2.ForeColor = foreColor2;
			label2.Font = new Font("Segoe UI", 8f);
			Label value2 = label2;
			CheckBox sounds = new CheckBox
			{
				Text = "Play a quiet click sound for buttons",
				Left = 24,
				Top = 105,
				Width = 390,
				Height = 22,
				Checked = GetPreference("ButtonClickSounds", defaultValue: true),
				ForeColor = foreColor,
				Font = new Font("Segoe UI", 8.8f),
				FlatStyle = FlatStyle.System
			};
			Label label3 = new Label();
			label3.Text = "MODEL SAFETY";
			label3.Left = 24;
			label3.Top = 145;
			label3.Width = 210;
			label3.Height = 22;
			label3.ForeColor = foreColor;
			label3.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
			Label value3 = label3;
			CheckBox confirm = new CheckBox
			{
				Text = "Confirm before deleting model items",
				Left = 24,
				Top = 178,
				Width = 390,
				Height = 22,
				Checked = GetPreference("ConfirmBeforeDelete", defaultValue: true),
				ForeColor = foreColor,
				Font = new Font("Segoe UI", 8.8f),
				FlatStyle = FlatStyle.System
			};
			Label label4 = new Label();
			label4.Text = "Applies to cleanup, CAD removal, and unplaced view deletion.";
			label4.Left = 48;
			label4.Top = 203;
			label4.Width = 400;
			label4.Height = 22;
			label4.ForeColor = foreColor2;
			label4.Font = new Font("Segoe UI", 8f);
			Label value4 = label4;
			Label label5 = new Label();
			label5.Text = "ABOUT";
			label5.Left = 24;
			label5.Top = 238;
			label5.Width = 210;
			label5.Height = 22;
			label5.ForeColor = foreColor;
			label5.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
			Label value5 = label5;
			Version version = typeof(App).Assembly.GetName().Version;
			Label label6 = new Label();
			label6.Text = "BIMlify for Revit 2024  ·  Version " + ((version == null) ? "1.0" : version.ToString(2));
			label6.Left = 24;
			label6.Top = 267;
			label6.Width = 400;
			label6.Height = 22;
			label6.ForeColor = foreColor2;
			label6.Font = new Font("Segoe UI", 8f);
			Label value6 = label6;
			tips.CheckedChanged += (object param0, EventArgs param1) =>
			{
				SetPreference("ShowTooltips", tips.Checked);
			};
			sounds.CheckedChanged += (object param0, EventArgs param1) =>
			{
				SetPreference("ButtonClickSounds", sounds.Checked);
			};
			confirm.CheckedChanged += (object param0, EventArgs param1) =>
			{
				SetPreference("ConfirmBeforeDelete", confirm.Checked);
			};
			panel2.Controls.Add(value);
			panel2.Controls.Add(tips);
			panel2.Controls.Add(value2);
			panel2.Controls.Add(sounds);
			panel2.Controls.Add(value3);
			panel2.Controls.Add(confirm);
			panel2.Controls.Add(value4);
			panel2.Controls.Add(value5);
			panel2.Controls.Add(value6);
			panel2.Controls.Add(CreateSmallMark(panel2));
			Panel panel3 = CreateDialogFooter();
		panel3.Padding = new Padding(12, 12, 0, 8);
			GlassButton glassButton = new GlassButton();
			glassButton.Text = "Close";
			glassButton.Width = 96;
			glassButton.DialogResult = DialogResult.Cancel;
			GlassButton glassButton2 = glassButton;
			GlassButton glassButton3 = new GlassButton();
			glassButton3.Text = "Account";
			glassButton3.Width = 96;
			GlassButton glassButton4 = glassButton3;
			glassButton4.Click += (object param0, EventArgs param1) =>
			{
				form.Tag = "FORWARD_ACCOUNT";
				form.Close();
			};
			Panel panel4 = CreateFooterButtonGroup(rightAligned: true, 208);
			PlaceFooterButton(panel4, glassButton4, 0, 96);
			PlaceFooterButton(panel4, glassButton2, 112, 96);
			panel3.Controls.Add(panel4);
			form.Controls.Add(panel2);
			form.Controls.Add(panel3);
			AddWindowTitleBar(form);
			form.CancelButton = glassButton2;
			form.ShowDialog(owner);
			text = form.Tag as string;
		}
		finally
		{
			if (form != null)
			{
				((IDisposable)form).Dispose();
			}
		}
		if (text == "FORWARD_ACCOUNT")
		{
			ShowAccount(owner);
		}
	}

	private static void ShowAccount(Form owner)
	{
		DashboardEnhancements.ShowAccount(owner);
	}

	internal static Result OpenModelessDashboard(UIApplication application)
	{
		//IL_01aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (application == null || application.ActiveUIDocument == null)
			{
				TaskDialog.Show("BIMlify", "Open a Revit project first.");
				return (Result)1;
			}
			if (dashboardWindow != null && !dashboardWindow.IsDisposed)
			{
				if (dashboardQuickBarWindow != null && !dashboardQuickBarWindow.IsDisposed)
				{
					dashboardQuickBarWindow.Tag = "home";
					dashboardQuickBarWindow.Close();
					return (Result)0;
				}
				if (dashboardWindow.WindowState == FormWindowState.Minimized)
				{
					dashboardWindow.WindowState = FormWindowState.Normal;
				}
				dashboardWindow.Show();
				dashboardWindow.Activate();
				return (Result)0;
			}
			if (dashboardActionHandler == null)
			{
				dashboardActionHandler = new DashboardActionHandler();
			}
			if (dashboardExternalEvent == null)
			{
				dashboardExternalEvent = ExternalEvent.Create((IExternalEventHandler)(object)dashboardActionHandler);
			}
			BIMlifyForm window = null;
			window = CreateDashboard((string action) =>
			{
				//IL_001e: Unknown result type (might be due to invalid IL or missing references)
				//IL_0023: Unknown result type (might be due to invalid IL or missing references)
				//IL_0024: Unknown result type (might be due to invalid IL or missing references)
				//IL_0027: Unknown result type (might be due to invalid IL or missing references)
				//IL_0029: Invalid comparison between Unknown and I4
				//IL_0056: Unknown result type (might be due to invalid IL or missing references)
				window.Hide();
				dashboardActionHandler.Queue(action);
				ExternalEventRequest val = dashboardExternalEvent.Raise();
				if ((int)val != 0 && (int)val != 1)
				{
					window.Show();
					window.Activate();
					TaskDialog.Show("BIMlify", "Revit is busy. Please try this tool again in a moment.");
				}
			});
			window.ShowInTaskbar = true;
			window.Tag = "dashboard_modeless";
			dashboardWindow = window;
			dashboardApplication = application;
			dashboardRevitHandle = application.MainWindowHandle;
			window.FormClosed += (object param0, FormClosedEventArgs param1) =>
			{
				if (object.ReferenceEquals(dashboardWindow, window))
				{
					dashboardWindow = null;
				}
			};
			BIMlify.CleanerUi.WelcomeSplash.Show();
window.Show();
			window.Activate();
			return (Result)0;
		}
		catch (Exception ex)
		{
			TaskDialog.Show("BIMlify", "The dashboard could not open.\n\n" + ex.Message);
			return (Result)(-1);
		}
	}

	private static void MinimizeDashboardToQuickBar(BIMlifyForm owner)
	{
		if (owner == null || owner.IsDisposed)
		{
			return;
		}
		if (dashboardQuickBarWindow != null && !dashboardQuickBarWindow.IsDisposed)
		{
			dashboardQuickBarWindow.Activate();
			return;
		}
		dashboardWindow = owner;
		owner.Hide();
		BIMlifyForm bar = CreateQuickBarWindow(dashboardApplication, dashboardRevitHandle, autoHide: true);
		dashboardQuickBarWindow = bar;
		bar.FormClosed += (object param0, FormClosedEventArgs param1) =>
		{
			//IL_01cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d0: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d4: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d6: Invalid comparison between Unknown and I4
			//IL_0203: Unknown result type (might be due to invalid IL or missing references)
			if (object.ReferenceEquals(dashboardQuickBarWindow, bar))
			{
				dashboardQuickBarWindow = null;
			}
			if (!dashboardClosing && !owner.IsDisposed)
			{
				string text = bar.Tag as string;
				if (string.IsNullOrEmpty(text) || text == "home" || text == "restore" || text == "closebar")
				{
					if (owner.WindowState == FormWindowState.Minimized)
					{
						owner.WindowState = FormWindowState.Normal;
					}
					if (text == "restore")
					{
						owner.WindowState = FormWindowState.Normal;
						owner.Size = new Size(1080, 720);
						Rectangle workingArea = QuickBarWorkingArea();
						owner.Location = new Point(workingArea.Left + Math.Max(0, (workingArea.Width - owner.Width) / 2), workingArea.Top + Math.Max(0, (workingArea.Height - owner.Height) / 2));
					}
					owner.Show();
					owner.Activate();
				}
				else if (dashboardActionHandler == null || dashboardExternalEvent == null)
				{
					owner.Show();
					owner.Activate();
				}
				else
				{
					owner.Show();
					owner.Activate();
					dashboardActionHandler.Queue(text);
					ExternalEventRequest val = dashboardExternalEvent.Raise();
					if ((int)val != 0 && (int)val != 1)
					{
						owner.Show();
						owner.Activate();
						TaskDialog.Show("BIMlify", "Revit is busy. Please try this tool again in a moment.");
					}
				}
			}
		};
		IWin32Window win32Window = ((dashboardRevitHandle == IntPtr.Zero) ? null : new NativeWindowOwner(dashboardRevitHandle));
		if (win32Window == null)
		{
			bar.Show();
		}
		else
		{
			bar.Show(win32Window);
		}
	}


	private static void ShowWelcomeSplash()
	{
		BIMlifyForm splash = new BIMlifyForm();
		try
		{
			Timer timer = new Timer();
			try
			{
				Color white = Color.White;
				Color backColor = Color.FromArgb(214, 171, 61);
				splash.Text = "BIMlify";
				splash.FormBorderStyle = FormBorderStyle.None;
				splash.ClientSize = new Size(1080, 720);
				splash.StartPosition = FormStartPosition.CenterScreen;
				splash.ShowInTaskbar = false;
				splash.ShowIcon = false;
				splash.TopMost = true;
				splash.BackColor = white;
				splash.Opacity = 0.0;
				UseSmoothCorners(splash);
				WelcomeLogoView logo = new WelcomeLogoView
				{
					Size = new Size(500, 425)
				};
				string text = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location), "Branding", "LogoWelcome.png");
				if (File.Exists(text))
				{
					using Image original = Image.FromFile(text);
					logo.Logo = new Bitmap(original);
				}
				splash.Controls.Add(logo);
				Label message = new Label
				{
					Text = "Opening your BIMlify workspace",
					Width = 360,
					Height = 24,
					ForeColor = Color.FromArgb(102, 109, 118),
					Font = new Font("Segoe UI", 9f),
					TextAlign = ContentAlignment.MiddleCenter,
					BackColor = white
				};
				splash.Controls.Add(message);
				Panel track = new Panel
				{
					Width = 280,
					Height = 3,
					BackColor = Color.FromArgb(235, 232, 224)
				};
				Panel fill = new Panel
				{
					Dock = DockStyle.Left,
					Width = 0,
					BackColor = backColor
				};
				track.Controls.Add(fill);
				splash.Controls.Add(track);
				DateTime started = DateTime.MinValue;
				Action action = () =>
				{
					logo.Left = (splash.ClientSize.Width - logo.Width) / 2;
					logo.Top = 120;
					message.Left = (splash.ClientSize.Width - message.Width) / 2;
					message.Top = 555;
					track.Left = (splash.ClientSize.Width - track.Width) / 2;
					track.Top = 594;
				};
				action();
				timer.Interval = 16;
				timer.Tick += (object param0, EventArgs param1) =>
				{
					double totalMilliseconds = (DateTime.Now - started).TotalMilliseconds;
					logo.Motion = Math.Min(1.0, totalMilliseconds / 750.0);
					logo.AccentMotion = Math.Min(1.0, Math.Max(0.0, (totalMilliseconds - 650.0) / 550.0));
					logo.SparkMotion = 0.0;
					logo.OrbitMotion = Math.Min(1.38, Math.Max(0.0, totalMilliseconds / 1950.0));
					logo.Invalidate();
					BIMlifyForm bIMlifyForm = splash;
					double opacity = ((totalMilliseconds < 240.0) ? Math.Min(0.98, totalMilliseconds / 240.0) : ((totalMilliseconds > 2100.0) ? Math.Max(0.0, 0.98 - (totalMilliseconds - 2100.0) / 220.0) : 0.98));
					bIMlifyForm.Opacity = opacity;
					fill.Width = (int)((double)track.Width * Math.Min(1.0, totalMilliseconds / 1400.0));
					if (totalMilliseconds >= 2400.0)
					{
						splash.Close();
					}
				};
				splash.Shown += (object param0, EventArgs param1) =>
				{
					started = DateTime.Now;
					timer.Start();
				};
				splash.FormClosed += (object param0, FormClosedEventArgs param1) =>
				{
					timer.Stop();
					if (logo.Logo != null)
					{
						logo.Logo.Dispose();
						logo.Logo = null;
					}
				};
				BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(splash);
			}
			finally
			{
				if (timer != null)
				{
					((IDisposable)timer).Dispose();
				}
			}
		}
		finally
		{
			if (splash != null)
			{
				((IDisposable)splash).Dispose();
			}
		}
	}

	internal static void CloseModelessDashboard()
	{
		dashboardClosing = true;
		try
		{
			if (dashboardQuickBarWindow != null && !dashboardQuickBarWindow.IsDisposed)
			{
				dashboardQuickBarWindow.Tag = "shutdown";
				dashboardQuickBarWindow.Close();
			}
		}
		catch
		{
		}
		try
		{
			if (dashboardWindow != null && !dashboardWindow.IsDisposed)
			{
				dashboardWindow.Close();
			}
		}
		catch
		{
		}
		dashboardWindow = null;
		try
		{
			if (dashboardExternalEvent != null)
			{
				dashboardExternalEvent.Dispose();
			}
		}
		catch
		{
		}
		dashboardExternalEvent = null;
		dashboardActionHandler = null;
		dashboardApplication = null;
		dashboardRevitHandle = IntPtr.Zero;
		dashboardClosing = false;
	}

	internal static Result Run(ExternalCommandData data, ref string message, ElementSet elements, string action)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		return RunInternal(data.Application, ref message, action, modelessDashboard: false);
	}

	private static Result RunFromDashboard(UIApplication application, string action)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		string message = null;
		return RunInternal(application, ref message, action, modelessDashboard: true);
	}

	private static Result RunInternal(UIApplication application, ref string message, string action, bool modelessDashboard)
	{
		//IL_04f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0515: Unknown result type (might be due to invalid IL or missing references)
		//IL_051c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0520: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_04de: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			UIDocument activeUIDocument = application.ActiveUIDocument;
			if (activeUIDocument == null)
			{
				TaskDialog.Show("BIMlify", "Open a Revit project first.");
				return (Result)1;
			}
			if (modelessDashboard && action == "quickbar")
			{
				action = ShowBottomToolBar(application);
				if (string.IsNullOrEmpty(action))
				{
					return (Result)1;
				}
			}
			while (true)
			{
				bool flag = true;
				if (action == "dashboard")
				{
					action = ShowDashboard();
					if (string.IsNullOrEmpty(action))
					{
						return (Result)1;
					}
					if (action == "quickbar")
					{
						action = ShowBottomToolBar(application);
						if (string.IsNullOrEmpty(action))
						{
							return (Result)1;
						}
					}
				}
				if (action == "home")
				{
					if (modelessDashboard)
					{
						return (Result)0;
					}
					action = "dashboard";
					continue;
				}
				ReturnToMainMenu = false;
				string text = action;
				if (action == "smartopenings")
				{
					SmartOpeningsCommand.Launch(activeUIDocument);
					return (Result)0;
				}
				switch (text)
				{
				case "cleanup":
					ReviewCleanup(activeUIDocument, cadOnly: false);
					break;
				case "smartcleaner":
					SmartCleaner(activeUIDocument);
					break;
				case "clashdetection":
					ClashDetection(activeUIDocument);
					break;
				case "cadreview":
					ReviewCleanup(activeUIDocument, cadOnly: true);
					break;
				case "families":
					ReviewCleanup(activeUIDocument, cadOnly: false, "Families & Types");
					break;
				case "materials":
					ReviewCleanup(activeUIDocument, cadOnly: false, "Materials");
					break;
				case "patterns":
					ReviewCleanup(activeUIDocument, cadOnly: false, "Patterns");
					break;
				case "templates":
					ReviewCleanup(activeUIDocument, cadOnly: false, "View Templates");
					break;
				case "filters":
					ReviewCleanup(activeUIDocument, cadOnly: false, "Filters");
					break;
				case "cad":
					ReviewCleanup(activeUIDocument, cadOnly: true);
					break;
				case "views":
					ReviewCleanup(activeUIDocument, cadOnly: false, "Unplaced Views");
					break;
				case "deletecad":
					DeleteCad(activeUIDocument);
					break;
				case "deleteviews":
					DeleteViews(activeUIDocument);
					break;
				case "bubbles":
					LevelBubbles(activeUIDocument);
					break;
				case "wet3d":
					try
					{
						CreateWetArea3D(activeUIDocument);
					}
					catch (OperationCanceledException)
					{
					}
					ReturnToMainMenu = true;
					break;
				case "wetplan":
					try
					{
						CreateRoomPlan(activeUIDocument);
					}
					catch (OperationCanceledException)
					{
					}
					ReturnToMainMenu = true;
					break;
				case "blockplan":
					try
					{
						CreateBlockworkPlan(activeUIDocument);
					}
					catch (OperationCanceledException)
					{
					}
					ReturnToMainMenu = true;
					break;
				case "style3d":
					Style3D(activeUIDocument);
					break;
				case "viewtemplates":
					ViewTemplates(activeUIDocument);
					break;
				case "patternmaker":
					PatternMaker(activeUIDocument);
					break;
				case "scaletags":
					ScaleTagFamilies(activeUIDocument);
					break;
				case "bimlibrary":
					BimLibrary(application);
					break;
				case "sheetorganization":
					SheetOrganization(activeUIDocument);
					break;
				case "selectcategories":
					CategoryTools(activeUIDocument, "select");
					break;
				case "isolatecategories":
					CategoryTools(activeUIDocument, "isolate");
					break;
				case "categorytools":
					CategoryTools(activeUIDocument);
					break;
				case "selectbyid":
					SelectElementsById(activeUIDocument);
					ReturnToMainMenu = true;
					break;
				case "resetisolate":
					ResetCategoryIsolation(activeUIDocument);
					break;
				}
				if (modelessDashboard)
				{
					return (Result)0;
				}
				if (!ReturnToMainMenu)
				{
					break;
				}
				action = "dashboard";
			}
			return (Result)0;
		}
		catch (OperationCanceledException)
		{
			return (Result)1;
		}
		catch (Exception ex5)
		{
			message = ex5.ToString();
			TaskDialog.Show("BIMlify", "The command could not finish.\n\n" + ex5.Message);
			return (Result)(-1);
		}
	}

	private static List<PurgeSuggestion> GetPurgeSuggestions(Document doc)
	{
		List<PurgeSuggestion> list = new List<PurgeSuggestion>();
		ISet<ElementId> unusedElements = doc.GetUnusedElements((ISet<ElementId>)new HashSet<ElementId>());
		foreach (ElementId item in unusedElements)
		{
			Element element = doc.GetElement(item);
			if (element != null)
			{
				string category = Cat(element);
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
				list.Add(new PurgeSuggestion
				{
					Id = item,
					Category = category,
					Name = (element.Name ?? ((object)element).GetType().Name),
					Kind = kind
				});
			}
		}
		return list.OrderBy((PurgeSuggestion x) => x.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy((PurgeSuggestion x) => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	private static List<ModelHealthWarning> GetModelHealthWarnings(Document doc)
	{
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Invalid comparison between Unknown and I4
		List<ModelHealthWarning> list = new List<ModelHealthWarning>();
		foreach (FailureMessage warning in doc.GetWarnings())
		{
			List<ElementId> list2 = new List<ElementId>();
			try
			{
				foreach (ElementId failingElement in warning.GetFailingElements())
				{
					Element element = doc.GetElement(failingElement);
					if (element != null && element.Category != null && (int)element.Category.CategoryType == 1)
					{
						list2.Add(failingElement);
					}
				}
			}
			catch
			{
			}
			if (list2.Count != 0)
			{
				string[] value = (from id in list2
					select doc.GetElement(id) into e
					where e != null && e.Category != null
					select e.Category.Name).Distinct().ToArray();
				list.Add(new ModelHealthWarning
				{
					Severity = ((object)warning.GetSeverity()).ToString(),
					Description = warning.GetDescriptionText(),
					ElementIds = list2.Distinct().ToList(),
					Categories = string.Join(", ", value)
				});
			}
		}
		return list.OrderByDescending((ModelHealthWarning x) => x.Severity == "Error").ThenBy((ModelHealthWarning x) => x.Categories, StringComparer.CurrentCultureIgnoreCase).ThenBy((ModelHealthWarning x) => x.Description, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	private static void SmartCleaner(UIDocument ui)
	{
		if (!BIMlify.ReviewRunGate.Entry.ConfirmSmartCleaner())
		{
			return;
		}
		UIDocument ui2 = ui;
		Document doc = ui2.Document;
		BIMlifyForm form = new BIMlifyForm();
		try
		{
			TabControl tabs = new TabControl();
			try
			{
				CheckedListBox purgeList = new CheckedListBox();
				try
				{
					ComboBox purgeTypeFilter = new LightGoldComboBox();
					try
					{
						ComboBox purgeCategoryFilter = new LightGoldComboBox();
						try
						{
							TextBox purgeSearch = new AdvancedSearchTextBox();
							try
							{
								DataGridView warningsGrid = new DataGridView();
								try
								{
									Color color = Color.FromArgb(238, 241, 245);
									Color panel = Color.FromArgb(247, 247, 245);
									Color ink = Color.FromArgb(78, 85, 92);
									Color foreColor = Color.FromArgb(102, 109, 118);
									Color gold = Color.FromArgb(214, 171, 61);
									Color footerColor = Color.FromArgb(216, 221, 227);
									form.Text = "BIMlify — Smart Cleaner";
									form.Width = 1080;
									form.Height = 720;
									form.MinimumSize = new Size(980, 640);
									form.StartPosition = FormStartPosition.CenterScreen;
									form.ShowIcon = false;
									form.MaximizeBox = true;
									form.MinimizeBox = true;
									form.AutoScaleMode = AutoScaleMode.Dpi;
									form.BackColor = color;
									form.ForeColor = ink;
									form.Font = new Font("Segoe UI", 9f);
									UseSmoothCorners(form);
									Panel panel2 = new Panel();
									panel2.Dock = DockStyle.Top;
									panel2.Height = 128;
									panel2.BackColor = panel;
									panel2.Padding = new Padding(18, 10, 18, 6);
									Panel panel3 = panel2;
									Label label = new Label();
									label.Text = "Smart Cleaner";
									label.Left = 18;
									label.Top = 10;
									label.Width = 520;
									label.Height = 25;
									label.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
									label.ForeColor = ink;
									Label value = label;
									Label label2 = new Label();
									label2.Text = "Review Revit-reported unused items and inspect model warnings before cleanup.";
									label2.Left = 18;
									label2.Top = 38;
									label2.Width = 760;
									label2.Height = 24;
									label2.ForeColor = foreColor;
									label2.Font = new Font("Segoe UI", 8f);
									Label value2 = label2;
									panel3.Controls.Add(value);
									panel3.Controls.Add(value2);
									panel3.Controls.Add(CreateBrandLockup(panel));
									AddYellowPanelAccent(panel3, gold, atTop: false);
									Panel panel4 = new Panel();
									panel4.Dock = DockStyle.Bottom;
									panel4.Height = 64;
									panel4.BackColor = footerColor;
									panel4.Padding = new Padding(14, 8, 14, 8);
									Panel panel5 = panel4;
									AddFooterAccent(panel5);
									Panel panel6 = new Panel();
									panel6.Dock = DockStyle.Fill;
									panel6.BackColor = footerColor;
									Panel panel7 = panel6;
									Panel panel8 = new Panel();
									panel8.Dock = DockStyle.Right;
									panel8.Width = 338;
									panel8.BackColor = footerColor;
									Panel panel9 = panel8;
									Label count = new Label
									{
										Dock = DockStyle.Left,
										Width = 205,
										Height = 38,
										TextAlign = ContentAlignment.MiddleLeft,
										ForeColor = ink,
										BackColor = footerColor,
										AutoSize = false,
										AutoEllipsis = false,
										UseMnemonic = false,
										Visible = true
									};
									count.Font = new Font("Segoe UI", 8.2f);
									count.TextChanged += (object param0, EventArgs param1) =>
									{
										FitLabelTextToOneLine(count, count.Text, 7.2f);
									};
									count.SizeChanged += (object param0, EventArgs param1) =>
									{
										FitLabelTextToOneLine(count, count.Text, 7.2f);
									};
									GlassButton glassButton = new GlassButton();
									glassButton.Text = "Select All";
									glassButton.Width = 96;
									GlassButton glassButton2 = glassButton;
									GlassButton glassButton3 = new GlassButton();
									glassButton3.Text = "Clear";
									glassButton3.Width = 82;
									GlassButton glassButton4 = glassButton3;
									GlassButton glassButton5 = new GlassButton();
									glassButton5.Text = "Close";
									glassButton5.Width = 92;
									glassButton5.DialogResult = DialogResult.Cancel;
									GlassButton glassButton6 = glassButton5;
									GlassButton select3D = new GlassButton
									{
										Text = "Locate Elements",
										Width = 112,
										Enabled = false
									};
									GlassButton purge = new GlassButton
									{
										Text = "Purge",
										Width = 96,
										Primary = true,
										Enabled = false
									};
									panel7.Controls.Add(count);
									PlaceFooterButton(panel7, glassButton2, 208, 96);
									PlaceFooterButton(panel7, glassButton4, 312, 82);
									count.BringToFront();
									PlaceFooterButton(panel9, glassButton6, 0, 92);
									PlaceFooterButton(panel9, select3D, 104, 112);
									PlaceFooterButton(panel9, purge, 232, 96);
									panel5.Controls.Add(panel7);
									panel5.Controls.Add(panel9);
									Panel panel10 = new Panel();
									panel10.Dock = DockStyle.Fill;
									panel10.BackColor = color;
									panel10.Padding = new Padding(14, 10, 14, 10);
									Panel panel11 = panel10;
									tabs.Dock = DockStyle.Fill;
									tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
									tabs.ItemSize = new Size(170, 30);
									tabs.SizeMode = TabSizeMode.Fixed;
									tabs.BackColor = color;
									tabs.ForeColor = ink;
									TabPage purgePage = new TabPage("Unused (Purge)")
									{
										BackColor = color,
										ForeColor = ink,
										Padding = new Padding(8),
										UseVisualStyleBackColor = false
									};
									TabPage healthPage = new TabPage("Model Warnings")
									{
										BackColor = color,
										ForeColor = ink,
										Padding = new Padding(8),
										UseVisualStyleBackColor = false
									};
									purgeList.Dock = DockStyle.Fill;
									purgeList.CheckOnClick = true;
									purgeList.BorderStyle = BorderStyle.FixedSingle;
									purgeList.BackColor = panel;
									purgeList.ForeColor = ink;
									purgeList.IntegralHeight = false;
									TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
									tableLayoutPanel.Dock = DockStyle.Fill;
									tableLayoutPanel.ColumnCount = 1;
									tableLayoutPanel.RowCount = 3;
									tableLayoutPanel.BackColor = color;
									tableLayoutPanel.Padding = Padding.Empty;
									TableLayoutPanel tableLayoutPanel2 = tableLayoutPanel;
									tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
									tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
									tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
									Label label3 = new Label();
									label3.Text = "Revit only returns items its unused-elements API identifies. Filters apply to these suggestions; they are not a complete unused-category audit.";
									label3.Dock = DockStyle.Fill;
									label3.ForeColor = foreColor;
									label3.TextAlign = ContentAlignment.MiddleLeft;
									label3.Font = new Font("Segoe UI", 8f);
									Label control = label3;
									Panel panel12 = new Panel();
									panel12.Dock = DockStyle.Fill;
									panel12.BackColor = color;
									Panel panel13 = panel12;
									Label label4 = new Label();
									label4.Text = "Type";
									label4.Left = 0;
									label4.Top = 7;
									label4.Width = 34;
									label4.Height = 24;
									label4.ForeColor = ink;
									label4.TextAlign = ContentAlignment.MiddleLeft;
									label4.Font = new Font("Segoe UI", 8f);
									Label value3 = label4;
									purgeTypeFilter.Left = 36;
									purgeTypeFilter.Top = 5;
									purgeTypeFilter.Width = 190;
									purgeTypeFilter.Height = 26;
									purgeTypeFilter.DropDownStyle = ComboBoxStyle.DropDownList;
									purgeTypeFilter.BackColor = panel;
									purgeTypeFilter.ForeColor = ink;
									Label label5 = new Label();
									label5.Text = "Category";
									label5.Left = 238;
									label5.Top = 7;
									label5.Width = 72;
									label5.Height = 24;
									label5.AutoSize = false;
									label5.UseMnemonic = false;
									label5.AutoEllipsis = false;
									label5.ForeColor = ink;
									label5.TextAlign = ContentAlignment.MiddleLeft;
									label5.Font = new Font("Segoe UI", 8f);
									Label value4 = label5;
									purgeCategoryFilter.SetBounds(312, 5, 240, 26);
									purgeCategoryFilter.DropDownStyle = ComboBoxStyle.DropDownList;
									purgeCategoryFilter.BackColor = panel;
									purgeCategoryFilter.ForeColor = ink;
									purgeSearch.SetBounds(564, 5, 386, 26);
									StyleSearchBox(purgeSearch, panel, ink, "Search category, item name, type or ID");
									purgeSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
									panel13.Controls.Add(value3);
									panel13.Controls.Add(purgeTypeFilter);
									panel13.Controls.Add(value4);
									panel13.Controls.Add(purgeCategoryFilter);
									panel13.Controls.Add(purgeSearch);
									Label label6 = new Label();
									label6.Text = "Revit-reported warnings involving model elements. Select rows and locate their elements; this is not a complete BIM audit or deletion list.";
									label6.Dock = DockStyle.Top;
									label6.Height = 34;
									label6.ForeColor = foreColor;
									label6.TextAlign = ContentAlignment.MiddleLeft;
									label6.Font = new Font("Segoe UI", 8f);
									Label value5 = label6;
									warningsGrid.Dock = DockStyle.Fill;
									warningsGrid.AllowUserToAddRows = false;
									warningsGrid.AllowUserToDeleteRows = false;
									warningsGrid.ReadOnly = true;
									warningsGrid.RowHeadersVisible = false;
									warningsGrid.AutoGenerateColumns = false;
									warningsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
									warningsGrid.MultiSelect = true;
									warningsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
									warningsGrid.BackgroundColor = color;
									warningsGrid.BorderStyle = BorderStyle.None;
									warningsGrid.GridColor = Color.FromArgb(210, 215, 222);
									warningsGrid.EnableHeadersVisualStyles = false;
									warningsGrid.Font = new Font("Segoe UI", 8.5f);
									warningsGrid.ColumnHeadersDefaultCellStyle.BackColor = panel;
									warningsGrid.ColumnHeadersDefaultCellStyle.ForeColor = ink;
									warningsGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
									warningsGrid.ColumnHeadersHeight = 34;
									warningsGrid.DefaultCellStyle.BackColor = Color.FromArgb(237, 240, 244);
									warningsGrid.DefaultCellStyle.ForeColor = ink;
									warningsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
									warningsGrid.DefaultCellStyle.SelectionForeColor = ink;
									warningsGrid.DefaultCellStyle.Padding = new Padding(4);
									warningsGrid.RowTemplate.Height = 32;
									warningsGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
									warningsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
									warningsGrid.Columns.Add(new DataGridViewTextBoxColumn
									{
										HeaderText = "Severity",
										FillWeight = 12f
									});
									warningsGrid.Columns.Add(new DataGridViewTextBoxColumn
									{
										HeaderText = "Category",
										FillWeight = 20f
									});
									warningsGrid.Columns.Add(new DataGridViewTextBoxColumn
									{
										HeaderText = "Revit warning",
										FillWeight = 54f
									});
									warningsGrid.Columns.Add(new DataGridViewTextBoxColumn
									{
										HeaderText = "Elements",
										FillWeight = 10f
									});
									tableLayoutPanel2.Controls.Add(control, 0, 0);
									tableLayoutPanel2.Controls.Add(panel13, 0, 1);
									tableLayoutPanel2.Controls.Add(purgeList, 0, 2);
									purgePage.Controls.Add(tableLayoutPanel2);
									healthPage.Controls.Add(warningsGrid);
									healthPage.Controls.Add(value5);
									tabs.TabPages.Add(purgePage);
									tabs.TabPages.Add(healthPage);
									panel11.Controls.Add(tabs);
									form.Controls.Add(panel11);
									form.Controls.Add(panel5);
									form.Controls.Add(panel3);
									AddWindowSidebar(form, dashboard: false);
									form.CancelButton = glassButton6;
									List<PurgeSuggestion> purgeItems = new List<PurgeSuggestion>();
									List<PurgeSuggestion> allPurgeItems = new List<PurgeSuggestion>();
									List<ModelHealthWarning> healthItems = new List<ModelHealthWarning>();
									HashSet<long> uncheckedPurgeIds = new HashSet<long>();
									int purgeRangeAnchor = -1;
									bool applyingPurgeRange = false;
									Action refreshPurgeList = () =>
									{
										string selectedType = ((purgeTypeFilter.SelectedIndex <= 0) ? null : (purgeTypeFilter.SelectedItem as string));
										string selectedCategory = ((purgeCategoryFilter.SelectedIndex <= 0) ? null : (purgeCategoryFilter.SelectedItem as string));
										string query = purgeSearch.Text ?? string.Empty;
										purgeItems = allPurgeItems.Where((PurgeSuggestion item) => (selectedType == null || string.Equals(item.Kind, selectedType, StringComparison.CurrentCultureIgnoreCase)) && (selectedCategory == null || string.Equals(item.Category, selectedCategory, StringComparison.CurrentCultureIgnoreCase)) && SearchTextMatches(query, item.Category, item.Name, item.Kind, item.Id.Value.ToString())).ToList();
										purgeList.BeginUpdate();
										purgeList.Items.Clear();
										foreach (PurgeSuggestion item in purgeItems)
										{
											purgeList.Items.Add(item, !uncheckedPurgeIds.Contains(item.Id.Value));
										}
										purgeList.EndUpdate();
										count.Text = purgeItems.Count + "/" + allPurgeItems.Count + " · " + purgeItems.Count((PurgeSuggestion item) => !uncheckedPurgeIds.Contains(item.Id.Value)) + " checked";
										purge.Enabled = tabs.SelectedTab == purgePage && purgeItems.Any((PurgeSuggestion item) => !uncheckedPurgeIds.Contains(item.Id.Value));
									};
									bool purgeDefaultsInitialized = default;
									Action refresh = () =>
									{
										allPurgeItems = GetPurgeSuggestions(doc);
										healthItems = GetModelHealthWarnings(doc);
										HashSet<long> currentIds = new HashSet<long>(allPurgeItems.Select((PurgeSuggestion item) => item.Id.Value));
										if (!purgeDefaultsInitialized)
										{
											uncheckedPurgeIds.UnionWith(currentIds);
											purgeDefaultsInitialized = true;
										}
										uncheckedPurgeIds.RemoveWhere((long id) => !currentIds.Contains(id));
										if (purgeTypeFilter.Items.Count == 0)
										{
											purgeTypeFilter.Items.Add("All types");
											foreach (string item2 in allPurgeItems.Select((PurgeSuggestion item) => item.Kind).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy((string kind) => kind, StringComparer.CurrentCultureIgnoreCase))
											{
												purgeTypeFilter.Items.Add(item2);
											}
											purgeTypeFilter.SelectedIndex = 0;
										}
										if (purgeCategoryFilter.Items.Count == 0)
										{
											purgeCategoryFilter.Items.Add("All categories");
											foreach (string item3 in allPurgeItems.Select((PurgeSuggestion item) => item.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy((string category) => category, StringComparer.CurrentCultureIgnoreCase))
											{
												purgeCategoryFilter.Items.Add(item3);
											}
											purgeCategoryFilter.SelectedIndex = 0;
										}
										refreshPurgeList();
										warningsGrid.Rows.Clear();
										foreach (ModelHealthWarning item4 in healthItems)
										{
											int index = warningsGrid.Rows.Add(item4.Severity, item4.Categories, item4.Description, item4.ElementIds.Count);
											warningsGrid.Rows[index].Tag = item4;
										}
										count.Text = purgeItems.Count + "/" + allPurgeItems.Count + " · " + purgeItems.Count((PurgeSuggestion item) => !uncheckedPurgeIds.Contains(item.Id.Value)) + " checked";
										purge.Enabled = tabs.SelectedTab == purgePage && purgeItems.Any((PurgeSuggestion item) => !uncheckedPurgeIds.Contains(item.Id.Value));
										select3D.Enabled = tabs.SelectedTab == healthPage && healthItems.Count > 0;
									};
									refresh();
									purgeTypeFilter.SelectedIndexChanged += (object param0, EventArgs param1) =>
									{
										refreshPurgeList();
									};
									purgeCategoryFilter.SelectedIndexChanged += (object param0, EventArgs param1) =>
									{
										refreshPurgeList();
									};
									purgeSearch.TextChanged += (object param0, EventArgs param1) =>
									{
										refreshPurgeList();
									};
									purgeList.MouseDown += (object sender, MouseEventArgs e) =>
									{
										int num = purgeList.IndexFromPoint(e.Location);
										if (num >= 0)
										{
											if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift && purgeRangeAnchor >= 0)
											{
												int first = Math.Min(purgeRangeAnchor, num);
												int last = Math.Max(purgeRangeAnchor, num);
												bool check = !purgeList.GetItemChecked(num);
												purgeRangeAnchor = num;
												form.BeginInvoke((Action)(() =>
												{
													if (!form.IsDisposed)
													{
														applyingPurgeRange = true;
														try
														{
															for (int i = first; i <= last; i++)
															{
																purgeList.SetItemChecked(i, check);
															}
														}
														finally
														{
															applyingPurgeRange = false;
														}
														count.Text = purgeItems.Count + "/" + allPurgeItems.Count + " · " + purgeItems.Count((PurgeSuggestion item) => !uncheckedPurgeIds.Contains(item.Id.Value)) + " checked";
														purge.Enabled = purgeItems.Any((PurgeSuggestion item) => !uncheckedPurgeIds.Contains(item.Id.Value));
													}
												}));
											}
											else
											{
												purgeRangeAnchor = num;
											}
										}
									};
									purgeList.ItemCheck += (object sender, ItemCheckEventArgs e) =>
									{
										if (purgeList.Items[e.Index] is PurgeSuggestion purgeSuggestion)
										{
											if (e.NewValue == CheckState.Checked)
											{
												uncheckedPurgeIds.Remove(purgeSuggestion.Id.Value);
											}
											else
											{
												uncheckedPurgeIds.Add(purgeSuggestion.Id.Value);
											}
											if (!applyingPurgeRange)
											{
												form.BeginInvoke((Action)(() =>
												{
													if (!form.IsDisposed)
													{
														count.Text = purgeItems.Count + "/" + allPurgeItems.Count + " · " + purgeItems.Count((PurgeSuggestion x) => !uncheckedPurgeIds.Contains(x.Id.Value)) + " checked";
														purge.Enabled = purgeItems.Any((PurgeSuggestion x) => !uncheckedPurgeIds.Contains(x.Id.Value));
													}
												}));
											}
										}
									};
									tabs.SelectedIndexChanged += (object param0, EventArgs param1) =>
									{
										refresh();
									};
									glassButton2.Click += (object param0, EventArgs param1) =>
									{
										if (tabs.SelectedTab == purgePage)
										{
											foreach (PurgeSuggestion item5 in purgeItems)
											{
												uncheckedPurgeIds.Remove(item5.Id.Value);
											}
											refreshPurgeList();
										}
									};
									glassButton4.Click += (object param0, EventArgs param1) =>
									{
										if (tabs.SelectedTab == purgePage)
										{
											foreach (PurgeSuggestion item6 in purgeItems)
											{
												uncheckedPurgeIds.Add(item6.Id.Value);
											}
											refreshPurgeList();
										}
									};
									select3D.Click += (object param0, EventArgs param1) =>
									{
										List<ElementId> list = (from id in warningsGrid.SelectedRows.Cast<DataGridViewRow>().SelectMany((DataGridViewRow r) => ((ModelHealthWarning)r.Tag).ElementIds).Distinct()
											where doc.GetElement(id) != null
											select id).ToList();
										if (list.Count == 0)
										{
											return;
										}
										ui2.Selection.SetElementIds((ICollection<ElementId>)list);
										try
										{
											ui2.ShowElements((ICollection<ElementId>)list);
										}
										catch
										{
										}
									};
									purge.Click += (object param0, EventArgs param1) =>
									{
										//IL_025b: Unknown result type (might be due to invalid IL or missing references)
										//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
										//IL_00d8: Invalid comparison between Unknown and I4
										//IL_0168: Unknown result type (might be due to invalid IL or missing references)
										//IL_016e: Expected Obj, but got Unknown
										//IL_0170: Unknown result type (might be due to invalid IL or missing references)
										//IL_01e1: Unknown result type (might be due to invalid IL or missing references)
										//IL_01e7: Invalid comparison between Unknown and I4
										//IL_01c7: Unknown result type (might be due to invalid IL or missing references)
										//IL_01d7: Unknown result type (might be due to invalid IL or missing references)
										//IL_0223: Unknown result type (might be due to invalid IL or missing references)
										//IL_0211: Unknown result type (might be due to invalid IL or missing references)
										List<PurgeSuggestion> list = purgeItems.Where((PurgeSuggestion item) => !uncheckedPurgeIds.Contains(item.Id.Value)).ToList();
										if (list.Count != 0)
										{
											string text = "Purge " + list.Count + " selected item(s) that Revit reports as unused?\n\nReview the checked items first. Save a backup before cleanup. 3D warnings are not included in this deletion.";
											if ((int)TaskDialog.Show("BIMlify — Smart Cleaner", text, (TaskDialogCommonButtons)6) == 6)
											{
												List<ElementId> ids = (from x in list
													select x.Id into id
													where doc.GetElement(id) != null
													select id).Distinct().ToList();
												if (ids.Count == 0)
												{
													refresh();
												}
												else
												{
													try
													{
														Transaction val = new Transaction(doc, "BIMlify — Purge unused project items");
														try
														{
															val.Start();
															ICollection<ElementId> collection = doc.Delete((ICollection<ElementId>)ids);
															List<ElementId> list2 = collection.Where((ElementId id) => !ids.Contains(id)).ToList();
															if (list2.Count > 0)
															{
																val.RollBack();
																TaskDialog.Show("BIMlify — Smart Cleaner", "Cleanup was cancelled because Revit reported dependent elements outside the checked list.");
															}
															else if ((int)val.Commit() == 3)
															{
																TaskDialog.Show("BIMlify — Smart Cleaner", "Removed " + collection.Count + " unused item(s). Newly unused items are now shown in the refreshed list for review.");
															}
															else
															{
																TaskDialog.Show("BIMlify — Smart Cleaner", "Revit did not complete the cleanup transaction.");
															}
														}
														finally
														{
															((IDisposable)val)?.Dispose();
														}
													}
													catch (Exception ex)
													{
														TaskDialog.Show("BIMlify — Smart Cleaner", "Revit could not purge the selected items.\n\n" + ex.Message);
													}
													refresh();
												}
											}
										}
									};
									form.Shown += (object param0, EventArgs param1) =>
									{
										RefreshVisibleControls(form);
									};
									BIMlifyForm bIMlifyForm = form;
									LegacyWarningFilters.Apply(form);
									LegacyPurgeSelection.Apply(bIMlifyForm);
									purgePage.Text = "Unused (Purge)";
									healthPage.Text = "Model Warnings";
									UiPolish.ApplyLegacySmartCleaner(form, ui);
															Action restoreCleanerTabCaptions = () =>
															{
																if (purgePage.IsDisposed || healthPage.IsDisposed || tabs.IsDisposed) return;
																purgePage.Text = "Unused (Purge)";
																healthPage.Text = "Model Warnings";
																tabs.Invalidate();
															};
															restoreCleanerTabCaptions();
															form.Shown += (object sender, EventArgs e) =>
															{
																try { form.BeginInvoke(restoreCleanerTabCaptions); } catch { restoreCleanerTabCaptions(); }
															};
									BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(bIMlifyForm);
								}
								finally
								{
									if (warningsGrid != null)
									{
										((IDisposable)warningsGrid).Dispose();
									}
								}
							}
							finally
							{
								if (purgeSearch != null)
								{
									((IDisposable)purgeSearch).Dispose();
								}
							}
						}
						finally
						{
							if (purgeCategoryFilter != null)
							{
								((IDisposable)purgeCategoryFilter).Dispose();
							}
						}
					}
					finally
					{
						if (purgeTypeFilter != null)
						{
							((IDisposable)purgeTypeFilter).Dispose();
						}
					}
				}
				finally
				{
					if (purgeList != null)
					{
						((IDisposable)purgeList).Dispose();
					}
				}
			}
			finally
			{
				if (tabs != null)
				{
					((IDisposable)tabs).Dispose();
				}
			}
		}
		finally
		{
			if (form != null)
			{
				((IDisposable)form).Dispose();
			}
		}
	}

	private static void ReviewCleanup(UIDocument ui, bool cadOnly, string onlyGroup = null)
	{
		//IL_039f: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a5: Invalid comparison between Unknown and I4
		//IL_03b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Expected Obj, but got Unknown
		//IL_01df: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0237: Unknown result type (might be due to invalid IL or missing references)
		//IL_023d: Invalid comparison between Unknown and I4
		//IL_0322: Unknown result type (might be due to invalid IL or missing references)
		//IL_0328: Invalid comparison between Unknown and I4
		//IL_02d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02db: Invalid comparison between Unknown and I4
		//IL_024a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Invalid comparison between Unknown and I4
		//IL_02e8: Unknown result type (might be due to invalid IL or missing references)
		if (!cadOnly && string.IsNullOrEmpty(onlyGroup) && !BIMlify.ReviewRunGate.Entry.Confirm())
		{
			return;
		}
		Document document = ui.Document;
		List<CleanupCandidate> list = (cadOnly ? BuildCadCandidates(document) : BuildCleanupCandidates(document));
		if (!cadOnly && !string.IsNullOrEmpty(onlyGroup))
		{
			list = list.Where((CleanupCandidate x) => x.Group == onlyGroup).ToList();
		}
		if (list.Count == 0)
		{
			TaskDialog.Show("BIMlify", "No review candidates were found in this category.");
			return;
		}
		List<CleanupCandidate> list2 = ShowCandidateGrid(list, cadOnly ? "CAD Review" : "Review & Clean");
		if (list2.Count == 0)
		{
			return;
		}
		string text = "\n\nCandidates are not confirmed unused. Only checked rows will be considered. Items whose deletion would also remove other model elements will be skipped.";
		if (GetPreference("ConfirmBeforeDelete", defaultValue: true) && (int)TaskDialog.Show("Confirm cleanup", "Delete " + list2.Count + " checked item(s)?" + text, (TaskDialogCommonButtons)6) != 6)
		{
			return;
		}
		List<string> list3 = new List<string>();
		List<string> list4 = new List<string>();
		foreach (CleanupCandidate item in list2)
		{
			Transaction val = null;
			try
			{
				if (document.GetElement(item.Id) == null)
				{
					list4.Add(item.Group + " | " + item.Name + " — no longer exists");
					continue;
				}
				val = new Transaction(document, "BIMlify — remove reviewed item");
				val.Start();
				ICollection<ElementId> collection = document.Delete(item.Id);
				List<ElementId> list5 = collection.Where((ElementId id) => id != item.Id).ToList();
				if (list5.Count > 0)
				{
					if ((int)val.GetStatus() == 1)
					{
						val.RollBack();
					}
					list4.Add(item.Group + " | " + item.Name + " — deletion would also remove " + list5.Count + " dependent element(s)");
				}
				else if (!collection.Contains(item.Id))
				{
					if ((int)val.GetStatus() == 1)
					{
						val.RollBack();
					}
					list4.Add(item.Group + " | " + item.Name + " — Revit did not delete the selected element");
				}
				else if ((int)val.Commit() == 3)
				{
					list3.Add(item.Group + " | " + item.Name);
				}
				else
				{
					list4.Add(item.Group + " | " + item.Name + " — Revit could not commit the deletion");
				}
			}
			catch (Exception ex)
			{
				try
				{
					if (val != null && (int)val.GetStatus() == 1)
					{
						val.RollBack();
					}
				}
				catch
				{
				}
				list4.Add(item.Group + " | " + item.Name + " — " + ex.Message);
			}
			finally
			{
				if (val != null)
				{
					val.Dispose();
				}
			}
		}
		string text2 = "Deleted: " + list3.Count + "\nSkipped: " + list4.Count;
		if (list4.Count > 0)
		{
			text2 = text2 + "\n\n" + string.Join("\n", list4.Take(8));
		}
		TaskDialog.Show("BIMlify cleanup result", text2);
	}

	private static List<CleanupCandidate> BuildCleanupCandidates(Document doc)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b9: Expected Obj, but got Unknown
		//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Invalid comparison between Unknown and I4
		//IL_031f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0326: Expected Obj, but got Unknown
		//IL_0328: Unknown result type (might be due to invalid IL or missing references)
		//IL_032e: Invalid comparison between Unknown and I4
		//IL_042f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0727: Unknown result type (might be due to invalid IL or missing references)
		//IL_0741: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_04cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_0570: Unknown result type (might be due to invalid IL or missing references)
		//IL_0854: Unknown result type (might be due to invalid IL or missing references)
		//IL_081b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0821: Invalid comparison between Unknown and I4
		//IL_0825: Unknown result type (might be due to invalid IL or missing references)
		//IL_082c: Invalid comparison between Unknown and I4
		List<CleanupCandidate> list = new List<CleanupCandidate>();
		foreach (ImportInstance item in ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).WhereElementIsNotElementType()).Cast<ImportInstance>())
		{
			string kind = "Imported CAD";
			try
			{
				if (item.IsLinked)
				{
					kind = "Linked CAD";
				}
			}
			catch
			{
			}
			list.Add(new CleanupCandidate((Element)(object)item, "CAD Imports", kind, "CAD instance listed for review; check its use and view ownership."));
		}
		List<Element> list2 = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements().ToList();
		List<ElementType> list3 = ((IEnumerable)new FilteredElementCollector(doc).WhereElementIsElementType()).Cast<ElementType>().ToList();
		HashSet<int> hashSet = new HashSet<int>();
		HashSet<int> hashSet2 = new HashSet<int>();
		HashSet<int> hashSet3 = new HashSet<int>();
		foreach (Element item2 in list2)
		{
			try
			{
				ElementId typeId = item2.GetTypeId();
				if (typeId != ElementId.InvalidElementId)
				{
					hashSet.Add(typeId.IntegerValue);
				}
			}
			catch
			{
			}
			try
			{
				foreach (ElementId materialId in item2.GetMaterialIds(false))
				{
					if (materialId != ElementId.InvalidElementId)
					{
						hashSet2.Add(materialId.IntegerValue);
					}
				}
			}
			catch
			{
			}
			try
			{
				foreach (Parameter parameter in item2.Parameters)
				{
					Parameter val = parameter;
					if ((int)val.StorageType != 4)
					{
						continue;
					}
					ElementId typeId = val.AsElementId();
					if (!(typeId == ElementId.InvalidElementId))
					{
						hashSet3.Add(typeId.IntegerValue);
						if (doc.GetElement(typeId) is Material)
						{
							hashSet2.Add(typeId.IntegerValue);
						}
					}
				}
			}
			catch
			{
			}
		}
		foreach (ElementType item3 in list3)
		{
			try
			{
				foreach (ElementId materialId2 in ((Element)item3).GetMaterialIds(false))
				{
					if (materialId2 != ElementId.InvalidElementId)
					{
						hashSet2.Add(materialId2.IntegerValue);
					}
				}
			}
			catch
			{
			}
			try
			{
				foreach (Parameter parameter2 in ((Element)item3).Parameters)
				{
					Parameter val = parameter2;
					if ((int)val.StorageType == 4)
					{
						ElementId typeId = val.AsElementId();
						if (typeId != ElementId.InvalidElementId)
						{
							hashSet3.Add(typeId.IntegerValue);
						}
					}
				}
			}
			catch
			{
			}
		}
		foreach (ElementType item4 in list3)
		{
			if (!hashSet.Contains(((Element)item4).Id.IntegerValue))
			{
				list.Add(new CleanupCandidate((Element)(object)item4, "Families & Types", Cat((Element)(object)item4), "No placed instance using this type was detected."));
			}
		}
		foreach (Material item5 in ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(Material))).Cast<Material>())
		{
			if (!hashSet2.Contains(((Element)item5).Id.IntegerValue))
			{
				list.Add(new CleanupCandidate((Element)(object)item5, "Materials", "Material", "No direct, geometry, or type material use was detected."));
			}
		}
		IEnumerable<Element> enumerable = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).ToElements().Concat(new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).ToElements());
		foreach (Element item6 in enumerable)
		{
			if (!hashSet3.Contains(item6.Id.IntegerValue))
			{
				list.Add(new CleanupCandidate(item6, "Patterns", (item6 is FillPatternElement) ? "Fill Pattern" : "Line Pattern", "No direct ElementId reference was detected."));
			}
		}
		List<View> list4 = ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(View))).Cast<View>().ToList();
		HashSet<int> hashSet4 = new HashSet<int>();
		HashSet<int> hashSet5 = new HashSet<int>();
		foreach (View item7 in list4.Where((View v) => !v.IsTemplate))
		{
			try
			{
				if (item7.ViewTemplateId != ElementId.InvalidElementId)
				{
					hashSet4.Add(item7.ViewTemplateId.IntegerValue);
				}
			}
			catch
			{
			}
			try
			{
				foreach (ElementId filter in item7.GetFilters())
				{
					hashSet5.Add(filter.IntegerValue);
				}
			}
			catch
			{
			}
		}
		foreach (View item8 in list4.Where((View v) => v.IsTemplate))
		{
			if (!hashSet4.Contains(((Element)item8).Id.IntegerValue))
			{
				list.Add(new CleanupCandidate((Element)(object)item8, "View Templates", "View Template", "No view currently uses this template."));
			}
		}
		foreach (Element item9 in new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).ToElements().Concat(new FilteredElementCollector(doc).OfClass(typeof(SelectionFilterElement)).ToElements()))
		{
			if (!hashSet5.Contains(item9.Id.IntegerValue))
			{
				list.Add(new CleanupCandidate(item9, "Filters", ((object)item9).GetType().Name, "No non-template view currently applies this filter."));
			}
		}
		HashSet<int> hashSet6 = PlacedViewIds(doc);
		foreach (View item10 in list4)
		{
			if (!item10.IsTemplate && !(item10 is ViewSheet) && !hashSet6.Contains(((Element)item10).Id.IntegerValue) && (int)item10.ViewType != 7 && (int)item10.ViewType != 12 && item10.GetDependentViewIds().Count == 0)
			{
				list.Add(new CleanupCandidate((Element)(object)item10, "Unplaced Views", ((object)item10.ViewType).ToString(), "Not placed on a sheet; this does not prove the view is unused."));
			}
		}
		return (from x in list
			orderby x.Group, x.Name
			select x).ToList();
	}

	private static List<CleanupCandidate> BuildCadCandidates(Document doc)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		List<CleanupCandidate> list = new List<CleanupCandidate>();
		foreach (ImportInstance item in ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).WhereElementIsNotElementType()).Cast<ImportInstance>())
		{
			string kind = "Imported CAD";
			try
			{
				if (item.IsLinked)
				{
					kind = "Linked CAD";
				}
			}
			catch
			{
			}
			list.Add(new CleanupCandidate((Element)(object)item, "CAD Imports", kind, "Review where this CAD instance is used before removal."));
		}
		return list.OrderBy((CleanupCandidate x) => x.Name).ToList();
	}

	private static List<CleanupCandidate> ShowCandidateGrid(List<CleanupCandidate> candidates, string title)
	{
		List<CleanupCandidate> list = new List<CleanupCandidate>();
		using (BIMlifyForm bIMlifyForm = new BIMlifyForm())
		{
			TabControl tabs = new TabControl();
			try
			{
				Color color = Color.FromArgb(238, 241, 245);
				Color panel = Color.White;
				Color yellow = Color.FromArgb(214, 171, 61);
				Color foreColor = Color.FromArgb(78, 85, 92);
				bIMlifyForm.Text = "BIMlify — " + title;
				bIMlifyForm.Width = 1080;
				bIMlifyForm.Height = 720;
				bIMlifyForm.MinimumSize = new Size(980, 640);
				bIMlifyForm.StartPosition = FormStartPosition.CenterScreen;
				bIMlifyForm.ShowIcon = false;
				bIMlifyForm.MaximizeBox = true;
				bIMlifyForm.MinimizeBox = true;
				bIMlifyForm.AutoScaleMode = AutoScaleMode.Dpi;
				bIMlifyForm.BackColor = color;
				bIMlifyForm.ForeColor = foreColor;
				bIMlifyForm.Font = new Font("Segoe UI", 9f);
				UseSmoothCorners(bIMlifyForm);
				Color color2 = Color.FromArgb(238, 241, 245);
				Panel panel2 = new Panel();
				panel2.Dock = DockStyle.Top;
				panel2.Height = 128;
				panel2.BackColor = Color.FromArgb(247, 247, 245);
				panel2.Padding = new Padding(18, 9, 4, 9);
				Panel panel3 = panel2;
				Label label = new Label();
				label.Left = 18;
				label.Top = 10;
				label.Width = 650;
				label.Height = 24;
				label.AutoEllipsis = false;
				label.UseMnemonic = false;
				label.Text = title;
				label.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
				label.ForeColor = Color.FromArgb(78, 85, 92);
				label.TextAlign = ContentAlignment.MiddleLeft;
				label.Padding = Padding.Empty;
				Label value = label;
				Label label2 = new Label();
				label2.Left = 18;
				label2.Top = 38;
				label2.Width = 680;
				label2.Height = 24;
				label2.AutoEllipsis = false;
				label2.AutoSize = false;
				label2.Text = "Ctrl+A selects all; Shift-click selects a range; Ctrl-click selects individual items. CAD imports may have dependencies.";
				label2.ForeColor = Color.FromArgb(102, 109, 118);
				label2.TextAlign = ContentAlignment.TopLeft;
				label2.Padding = Padding.Empty;
				label2.Font = new Font("Segoe UI", 8f);
				Label value2 = label2;
				panel3.Controls.Add(value2);
				panel3.Controls.Add(value);
				panel3.Controls.Add(CreateBrandLockup(panel2.BackColor));
				AddYellowPanelAccent(panel3, yellow, atTop: false);
		// Keep the Review & Clean title block at its earlier upper position instead
		// of letting the shared accent helper push it down toward the separator.
		value.Top = 38;
		value2.Top = 66;
				tabs.Dock = DockStyle.Fill;
				tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
				tabs.ItemSize = new Size(112, 30);
				tabs.SizeMode = TabSizeMode.Fixed;
				tabs.BackColor = color;
				tabs.ForeColor = foreColor;
				tabs.ShowToolTips = true;
				Dictionary<TabPage, DataGridView> grids = new Dictionary<TabPage, DataGridView>();
				Dictionary<DataGridView, int> checkboxAnchors = new Dictionary<DataGridView, int>();
				Dictionary<DataGridView, bool> checkboxPriorValues = new Dictionary<DataGridView, bool>();
				Dictionary<DataGridView, List<DataGridViewRow>> checkboxSelectionSnapshot = new Dictionary<DataGridView, List<DataGridViewRow>>();
				string[] array = new string[7] { "Families & Types", "Materials", "Patterns", "View Templates", "Filters", "CAD Imports", "Unplaced Views" };
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				dictionary.Add("Families & Types", "Families");
				dictionary.Add("Materials", "Materials");
				dictionary.Add("Patterns", "Patterns");
				dictionary.Add("View Templates", "Templates");
				dictionary.Add("Filters", "Filters");
				dictionary.Add("CAD Imports", "CAD Imports");
				dictionary.Add("Unplaced Views", "Views");
				Dictionary<string, string> dictionary2 = dictionary;
				string[] array2 = array;
				foreach (string group in array2)
				{
					List<CleanupCandidate> source = candidates;
					Func<CleanupCandidate, bool> predicate = (CleanupCandidate c) => c.Group == group;
					List<CleanupCandidate> list2 = (from c in source.Where(predicate)
						orderby c.Name
						select c).ToList();
					if (list2.Count == 0)
					{
						continue;
					}
					TabPage tabPage = new TabPage(dictionary2[group]);
					tabPage.BackColor = color;
					tabPage.ForeColor = Color.FromArgb(78, 85, 92);
					tabPage.Padding = new Padding(8);
					tabPage.UseVisualStyleBackColor = false;
					tabPage.ToolTipText = group + " — " + list2.Count + " candidates";
					TabPage tabPage2 = tabPage;
					DataGridView grid = new DataGridView();
					grid.Dock = DockStyle.Fill;
					grid.AllowUserToAddRows = false;
					grid.AllowUserToDeleteRows = false;
					grid.RowHeadersVisible = false;
					grid.AutoGenerateColumns = false;
					grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
					grid.MultiSelect = true;
					grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
					grid.BackgroundColor = color;
					grid.BorderStyle = BorderStyle.None;
					grid.GridColor = Color.FromArgb(210, 215, 222);
					grid.EnableHeadersVisualStyles = false;
					grid.Font = new Font("Segoe UI", 9f);
					grid.ColumnHeadersDefaultCellStyle.BackColor = panel;
					grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
					grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
					grid.ColumnHeadersHeight = 34;
					grid.DefaultCellStyle.BackColor = Color.FromArgb(237, 240, 244);
					grid.DefaultCellStyle.ForeColor = foreColor;
					grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
					grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(78, 85, 92);
					grid.DefaultCellStyle.Padding = new Padding(4);
					grid.RowTemplate.Height = 27;
					grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(226, 230, 235);
					grid.AlternatingRowsDefaultCellStyle.ForeColor = foreColor;
					DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn = new DataGridViewCheckBoxColumn();
					dataGridViewCheckBoxColumn.HeaderText = "Delete";
					dataGridViewCheckBoxColumn.Width = 66;
					dataGridViewCheckBoxColumn.FillWeight = 8f;
					dataGridViewCheckBoxColumn.FlatStyle = FlatStyle.Flat;
					DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn2 = dataGridViewCheckBoxColumn;
					dataGridViewCheckBoxColumn2.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
					dataGridViewCheckBoxColumn2.DefaultCellStyle.BackColor = Color.FromArgb(237, 240, 244);
					dataGridViewCheckBoxColumn2.DefaultCellStyle.ForeColor = yellow;
					dataGridViewCheckBoxColumn2.DefaultCellStyle.SelectionBackColor = yellow;
					grid.Columns.Add(dataGridViewCheckBoxColumn2);
					grid.Columns.Add(new DataGridViewTextBoxColumn
					{
						HeaderText = "Name",
						ReadOnly = true,
						FillWeight = 22f
					});
					grid.Columns.Add(new DataGridViewTextBoxColumn
					{
						HeaderText = "Type",
						ReadOnly = true,
						FillWeight = 12f
					});
					grid.Columns.Add(new DataGridViewTextBoxColumn
					{
						HeaderText = "Element ID",
						ReadOnly = true,
						FillWeight = 8f
					});
					grid.Columns.Add(new DataGridViewTextBoxColumn
					{
						HeaderText = "Review note",
						ReadOnly = true,
						FillWeight = 34f
					});
					grid.SuspendLayout();
					using (List<CleanupCandidate>.Enumerator enumerator = list2.GetEnumerator())
					{
						while (enumerator.MoveNext())
						{
							CleanupCandidate current = enumerator.Current;
							int index = grid.Rows.Add(false, current.Name, current.Kind, current.Id.IntegerValue, current.Reason);
							grid.Rows[index].Tag = current;
						}
						grid.ResumeLayout();
					}
					grid.CurrentCellDirtyStateChanged += (object param0, EventArgs param1) =>
					{
						if (grid.IsCurrentCellDirty)
						{
							grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
						}
					};
					grid.CellPainting += (object sender, DataGridViewCellPaintingEventArgs e) =>
					{
						if (e.RowIndex >= 0 && e.ColumnIndex == 0)
						{
							e.PaintBackground(e.CellBounds, cellsPaintSelectionBackground: true);
							bool flag = e.Value != null && e.Value != DBNull.Value && Convert.ToBoolean(e.Value);
							bool flag2 = (e.State & DataGridViewElementStates.Selected) != 0;
							int num = 15;
							Rectangle rect = new Rectangle(e.CellBounds.Left + (e.CellBounds.Width - num) / 2, e.CellBounds.Top + (e.CellBounds.Height - num) / 2, num, num);
							Color color3 = (flag ? yellow : (flag2 ? Color.FromArgb(255, 240, 201) : Color.FromArgb(237, 240, 244)));
							Color color4 = Color.FromArgb(110, 117, 127);
							using (SolidBrush brush = new SolidBrush(color3))
							{
								e.Graphics.FillRectangle(brush, rect);
							}
							using (Pen pen = new Pen(color4, 1.5f))
							{
								e.Graphics.DrawRectangle(pen, rect);
							}
							if (flag)
							{
								using Pen pen2 = new Pen(Color.Black, 2f);
								pen2.StartCap = LineCap.Round;
								pen2.EndCap = LineCap.Round;
								e.Graphics.DrawLine(pen2, rect.Left + 3, rect.Top + 7, rect.Left + 6, rect.Top + 10);
								e.Graphics.DrawLine(pen2, rect.Left + 6, rect.Top + 10, rect.Left + 12, rect.Top + 3);
							}
							e.Handled = true;
						}
					};
					tabPage2.Controls.Add(grid);
					grids.Add(tabPage2, grid);
					checkboxAnchors[grid] = -1;
					checkboxPriorValues[grid] = false;
					checkboxSelectionSnapshot[grid] = new List<DataGridViewRow>();
					tabs.TabPages.Add(tabPage2);
				}
				tabs.DrawItem += (object sender, DrawItemEventArgs e) =>
				{
					e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
					bool flag = e.Index == tabs.SelectedIndex;
					using (SolidBrush brush = new SolidBrush(flag ? panel : Color.FromArgb(216, 221, 227)))
					{
						e.Graphics.FillRectangle(brush, e.Bounds);
					}
					string text = tabs.TabPages[e.Index].Text;
					using (SolidBrush brush = new SolidBrush(Color.FromArgb(78, 85, 92)))
					{
						using Font font = new Font("Segoe UI", 9f, flag ? FontStyle.Bold : FontStyle.Regular);
						using StringFormat format = new StringFormat
						{
							Alignment = StringAlignment.Center,
							LineAlignment = StringAlignment.Center,
							Trimming = StringTrimming.EllipsisCharacter,
							FormatFlags = StringFormatFlags.NoWrap
						};
						e.Graphics.DrawString(text, font, brush, e.Bounds, format);
					}
					using (Pen pen = new Pen(Color.FromArgb(185, 185, 185), 1f))
					{
						e.Graphics.DrawRectangle(pen, e.Bounds.Left, e.Bounds.Top, e.Bounds.Width - 1, e.Bounds.Height - 1);
					}
					if (flag)
					{
						using (Pen pen2 = new Pen(yellow, 3f))
						{
							e.Graphics.DrawLine(pen2, e.Bounds.Left + 2, e.Bounds.Bottom - 2, e.Bounds.Right - 3, e.Bounds.Bottom - 2);
						}
					}
				};
				Panel panel4 = new Panel();
				panel4.Dock = DockStyle.Bottom;
				panel4.Height = 64;
				panel4.BackColor = Color.FromArgb(216, 221, 227);
				panel4.Padding = new Padding(14, 8, 14, 8);
				Panel panel5 = panel4;
				AddFooterAccent(panel5);
				Label count = new Label
				{
					Width = 230,
					Height = 30,
					Top = 3,
					TextAlign = ContentAlignment.MiddleLeft,
					ForeColor = Color.FromArgb(78, 85, 92),
					Text = "Candidates: " + candidates.Count + "   |   Selected: 0"
				};
				GlassButton glassButton = new GlassButton();
				glassButton.Text = "Select All";
				glassButton.Width = 96;
				GlassButton glassButton2 = glassButton;
				GlassButton glassButton3 = new GlassButton();
				glassButton3.Text = "Clear All";
				glassButton3.Width = 96;
				GlassButton glassButton4 = glassButton3;
				GlassButton glassButton5 = new GlassButton();
				glassButton5.Text = "Close";
				glassButton5.Width = 96;
				glassButton5.DialogResult = DialogResult.Cancel;
				GlassButton glassButton6 = glassButton5;
				GlassButton delete = new GlassButton
				{
					Text = "Delete",
					Width = 96,
					DialogResult = DialogResult.OK,
					Primary = true
				};
				Panel panel6 = CreateFooterButtonGroup(rightAligned: false, 440);
				Panel panel7 = CreateFooterButtonGroup(rightAligned: true, 208);
				panel6.Controls.Add(count);
				PlaceFooterButton(panel6, glassButton2, 236, 96);
				PlaceFooterButton(panel6, glassButton4, 340, 96);
				PlaceFooterButton(panel7, glassButton6, 0, 96);
				PlaceFooterButton(panel7, delete, 104, 96);
				Func<int> selectedCount = () => grids.Values.Sum((DataGridView g) => g.Rows.Cast<DataGridViewRow>().Count((DataGridViewRow r) => r.Cells[0].Value != null && Convert.ToBoolean(r.Cells[0].Value)));
				Action updateCount = () =>
				{
					count.Text = "Candidates: " + candidates.Count + "   |   Selected: " + selectedCount();
					delete.Enabled = selectedCount() > 0;
				};
				foreach (DataGridView grid2 in grids.Values)
				{
					DataGridView dataGridView = grid2;
					DataGridViewCellMouseEventHandler value3 = (object sender, DataGridViewCellMouseEventArgs e) =>
					{
						if (e.RowIndex >= 0)
						{
							if (e.ColumnIndex == 0)
							{
								checkboxSelectionSnapshot[grid2] = (from DataGridViewRow r in grid2.Rows
									where r.Selected && r.Visible
									select r).ToList();
							}
							object value4 = grid2.Rows[e.RowIndex].Cells[0].Value;
							checkboxPriorValues[grid2] = value4 != null && value4 != DBNull.Value && Convert.ToBoolean(value4);
						}
					};
					dataGridView.CellMouseDown += value3;
					grid2.CellClick += (object sender, DataGridViewCellEventArgs e) =>
					{
						if (e.RowIndex >= 0)
						{
							bool flag = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
							if (e.ColumnIndex == 0)
							{
								bool flag2 = !checkboxPriorValues[grid2];
								List<DataGridViewRow> list3 = checkboxSelectionSnapshot[grid2].Where((DataGridViewRow r) => r.Visible).ToList();
								if (list3.Count == 0)
								{
									list3 = (from DataGridViewRow r in grid2.Rows
										where r.Selected && r.Visible
										select r).ToList();
								}
								if (!list3.Contains(grid2.Rows[e.RowIndex]))
								{
									list3.Add(grid2.Rows[e.RowIndex]);
								}
								if (list3.Count > 1)
								{
									foreach (DataGridViewRow item in list3)
									{
										item.Cells[0].Value = flag2;
									}
								}
								else
								{
									grid2.Rows[e.RowIndex].Cells[0].Value = flag2;
								}
							}
							else if (flag && checkboxAnchors[grid2] >= 0)
							{
								grid2.ClearSelection();
								int num = Math.Min(checkboxAnchors[grid2], e.RowIndex);
								int num2 = Math.Max(checkboxAnchors[grid2], e.RowIndex);
								for (int num3 = num; num3 <= num2; num3++)
								{
									grid2.Rows[num3].Selected = true;
								}
							}
							else
							{
								checkboxAnchors[grid2] = e.RowIndex;
							}
							updateCount();
						}
					};
				}
				glassButton2.Click += (object param0, EventArgs param1) =>
				{
					DataGridView dataGridView2 = grids[tabs.SelectedTab];
					foreach (DataGridViewRow item2 in (IEnumerable)dataGridView2.Rows)
					{
						item2.Cells[0].Value = true;
						item2.Selected = true;
					}
					updateCount();
				};
				glassButton4.Click += (object param0, EventArgs param1) =>
				{
					DataGridView dataGridView2 = grids[tabs.SelectedTab];
					foreach (DataGridViewRow item3 in (IEnumerable)dataGridView2.Rows)
					{
						item3.Cells[0].Value = false;
						item3.Selected = false;
					}
					dataGridView2.ClearSelection();
					updateCount();
				};
				foreach (DataGridView value5 in grids.Values)
				{
					value5.CellValueChanged += (object param0, DataGridViewCellEventArgs param1) =>
					{
						updateCount();
					};
				}
				updateCount();
				panel5.Controls.Add(panel6);
				panel5.Controls.Add(panel7);
				bIMlifyForm.Controls.Add(tabs);
				bIMlifyForm.Controls.Add(panel3);
				bIMlifyForm.Controls.Add(panel5);
				AddWindowSidebar(bIMlifyForm, dashboard: false);
				bIMlifyForm.AcceptButton = delete;
				bIMlifyForm.CancelButton = glassButton6;
				UiPolish.ApplyReviewTabs(bIMlifyForm);
				UiPolish.ApplyReviewCleanInset(bIMlifyForm, tabs);
				bIMlifyForm.Size = new Size(1080, 720);
				bIMlifyForm.StartPosition = FormStartPosition.CenterScreen;
				EmptyState.RestoreReviewCleanWindow(bIMlifyForm);
				if (BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(bIMlifyForm) == DialogResult.OK)
				{
					foreach (DataGridView value6 in grids.Values)
					{
						foreach (DataGridViewRow item4 in (IEnumerable)value6.Rows)
						{
							if (item4.Cells[0].Value != null && Convert.ToBoolean(item4.Cells[0].Value))
							{
								list.Add((CleanupCandidate)item4.Tag);
							}
						}
					}
				}
			}
			finally
			{
				if (tabs != null)
				{
					((IDisposable)tabs).Dispose();
				}
			}
		}
		return list;
	}

	private static HashSet<int> PlacedViewIds(Document doc)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		HashSet<int> hashSet = new HashSet<int>();
		foreach (ViewSheet item in ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))).Cast<ViewSheet>())
		{
			foreach (ElementId allViewport in item.GetAllViewports())
			{
				Element element = doc.GetElement(allViewport);
				Viewport val = (Viewport)(object)((element is Viewport) ? element : null);
				if (val != null)
				{
					hashSet.Add(val.ViewId.IntegerValue);
				}
			}
			foreach (ScheduleSheetInstance item2 in ((IEnumerable)new FilteredElementCollector(doc, ((Element)item).Id).OfClass(typeof(ScheduleSheetInstance))).Cast<ScheduleSheetInstance>())
			{
				hashSet.Add(item2.ScheduleId.IntegerValue);
			}
		}
		return hashSet;
	}

	private static List<View> UnplacedViews(Document doc)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		HashSet<int> placed = PlacedViewIds(doc);
		return ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(View))).Cast<View>().Where((View v) =>
		{
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Invalid comparison between Unknown and I4
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			//IL_0039: Invalid comparison between Unknown and I4
			return !v.IsTemplate && !(v is ViewSheet) && !placed.Contains(((Element)v).Id.IntegerValue) && (int)v.ViewType != 7 && (int)v.ViewType != 12;
		}).ToList();
	}

	private static string Cat(Element e)
	{
		try
		{
			return (e.Category == null) ? "No Category" : e.Category.Name;
		}
		catch
		{
			return "Unknown Category";
		}
	}

	private static void DeleteCad(UIDocument ui)
	{
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Expected Obj, but got Unknown
		//IL_0105: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Invalid comparison between Unknown and I4
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_01af: Unknown result type (might be due to invalid IL or missing references)
		IList<Reference> source = ui.Selection.PickObjects((ObjectType)1, (ISelectionFilter)(object)new CadFilter(), "Select CAD import/link instances to remove, then click Finish.");
		List<ElementId> list = source.Select((Reference r) => r.ElementId).Distinct().ToList();
		if (list.Count == 0)
		{
			return;
		}
		string text = string.Join("\n", list.Select((ElementId id) =>
		{
			Element element2 = ui.Document.GetElement(id);
			return (element2 == null) ? id.IntegerValue.ToString() : (element2.Name + " — ID " + id.IntegerValue);
		}).Take(15));
		if (GetPreference("ConfirmBeforeDelete", defaultValue: true) && (int)TaskDialog.Show("Confirm CAD removal", "Remove " + list.Count + " selected CAD instance(s) from this model?\n\n" + text + ((list.Count > 15) ? "\n…" : "") + "\n\nSource DWG files on disk will not be deleted.", (TaskDialogCommonButtons)6) != 6)
		{
			return;
		}
		Transaction val = new Transaction(ui.Document, "BIMlify — Remove selected CAD instances");
		try
		{
			val.Start();
			foreach (ElementId item in list)
			{
				Element element = ui.Document.GetElement(item);
				if (element != null && !element.Pinned)
				{
					ui.Document.Delete(item);
				}
			}
			val.Commit();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static List<T> SelectMultiple<T>(string title, IEnumerable<T> objects, Func<T, string> label) where T : class
	{
		using BIMlifyForm bIMlifyForm = new BIMlifyForm();
		DataGridView grid = new DataGridView();
		try
		{
			Color color = Color.FromArgb(238, 241, 245);
			Color backColor = Color.FromArgb(238, 241, 245);
			Color color2 = Color.FromArgb(214, 171, 61);
			Color foreColor = Color.FromArgb(78, 85, 92);
			bIMlifyForm.Text = "BIMlify — " + title;
			bIMlifyForm.Width = 1080;
			bIMlifyForm.Height = 720;
			bIMlifyForm.MinimumSize = new Size(980, 640);
			bIMlifyForm.StartPosition = FormStartPosition.CenterScreen;
			bIMlifyForm.ShowIcon = false;
			bIMlifyForm.MaximizeBox = true;
			bIMlifyForm.MinimizeBox = true;
			bIMlifyForm.AutoScaleMode = AutoScaleMode.Dpi;
			bIMlifyForm.BackColor = color;
			bIMlifyForm.ForeColor = foreColor;
			bIMlifyForm.Font = new Font("Segoe UI", 9f);
			bIMlifyForm.AutoScaleMode = AutoScaleMode.Dpi;
			UseSmoothCorners(bIMlifyForm);
			grid.Dock = DockStyle.Fill;
			grid.AllowUserToAddRows = false;
			grid.AllowUserToDeleteRows = false;
			grid.AllowUserToResizeRows = false;
			grid.RowHeadersVisible = false;
			grid.ReadOnly = true;
			grid.MultiSelect = true;
			grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
			grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
			grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
			grid.BackgroundColor = color;
			grid.BorderStyle = BorderStyle.None;
			grid.GridColor = Color.FromArgb(210, 215, 222);
			grid.EnableHeadersVisualStyles = false;
			grid.Font = new Font("Segoe UI", 9f);
			grid.RowTemplate.Height = 32;
			grid.ColumnHeadersDefaultCellStyle.BackColor = backColor;
			grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
			grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
			grid.ColumnHeadersHeight = 36;
			grid.DefaultCellStyle.BackColor = Color.FromArgb(237, 240, 244);
			grid.DefaultCellStyle.ForeColor = foreColor;
			grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
			grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(78, 85, 92);
			grid.DefaultCellStyle.Padding = new Padding(7, 3, 5, 3);
			grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(226, 230, 235);
			grid.AlternatingRowsDefaultCellStyle.ForeColor = foreColor;
			grid.Columns.Add(new DataGridViewTextBoxColumn
			{
				HeaderText = "Available items — use Shift/Ctrl to select",
				ReadOnly = true,
				SortMode = DataGridViewColumnSortMode.NotSortable
			});
			List<T> items = objects.ToList();
			foreach (T item in items)
			{
				int index = grid.Rows.Add(label(item));
				grid.Rows[index].Tag = item;
			}
			Color color3 = Color.FromArgb(238, 241, 245);
			Panel panel = new Panel();
			panel.Dock = DockStyle.Top;
			panel.Height = 128;
			panel.BackColor = Color.FromArgb(247, 247, 245);
			panel.Padding = new Padding(16, 9, 4, 9);
			Panel panel2 = panel;
			Label label2 = new Label();
			label2.Left = 18;
			label2.Top = 39;
			label2.Width = 680;
			label2.Height = 52;
			label2.AutoEllipsis = false;
			label2.Text = "Select rows; Shift-click for a range, Ctrl-click to toggle.";
			label2.ForeColor = Color.FromArgb(102, 109, 118);
			label2.TextAlign = ContentAlignment.TopLeft;
			label2.AutoSize = false;
			label2.Padding = Padding.Empty;
			label2.Font = new Font("Segoe UI", 8f);
			Label value = label2;
			Label label3 = new Label();
			label3.Left = 18;
			label3.Top = 12;
			label3.Width = 650;
			label3.Height = 24;
			label3.AutoEllipsis = false;
			label3.UseMnemonic = false;
			label3.Text = title;
			label3.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
			label3.ForeColor = Color.FromArgb(78, 85, 92);
			label3.TextAlign = ContentAlignment.MiddleLeft;
			label3.Padding = Padding.Empty;
			Label value2 = label3;
			panel2.Controls.Add(value);
			panel2.Controls.Add(value2);
			panel2.Controls.Add(CreateBrandLockup(panel2.BackColor));
			AddYellowPanelAccent(panel2, Color.FromArgb(214, 171, 61), atTop: false);
			Panel panel3 = new Panel();
			panel3.Dock = DockStyle.Bottom;
			panel3.Height = 64;
			panel3.BackColor = Color.FromArgb(216, 221, 227);
			panel3.Padding = new Padding(10, 8, 10, 8);
			Panel panel4 = panel3;
			AddFooterAccent(panel4);
			Label count = new Label
			{
				Width = 180,
				Height = 30,
				Top = 3,
				Text = "Selected: 0",
				ForeColor = Color.FromArgb(78, 85, 92),
				TextAlign = ContentAlignment.MiddleLeft
			};
			GlassButton glassButton = new GlassButton();
			glassButton.Text = "Clear";
			glassButton.Width = 82;
			GlassButton glassButton2 = glassButton;
			GlassButton glassButton3 = new GlassButton();
			glassButton3.Text = "Select All";
			glassButton3.Width = 96;
			GlassButton glassButton4 = glassButton3;
			GlassButton glassButton5 = new GlassButton();
			glassButton5.Text = "Continue";
			glassButton5.Width = 96;
			glassButton5.DialogResult = DialogResult.OK;
			glassButton5.Primary = true;
			GlassButton glassButton6 = glassButton5;
			GlassButton glassButton7 = new GlassButton();
			glassButton7.Text = "Cancel";
			glassButton7.Width = 96;
			glassButton7.DialogResult = DialogResult.Cancel;
			GlassButton glassButton8 = glassButton7;
			Panel panel5 = CreateFooterButtonGroup(rightAligned: false, 180);
			Panel panel6 = CreateFooterButtonGroup(rightAligned: true, 440);
			panel5.Controls.Add(count);
			PlaceFooterButton(panel6, glassButton2, 0, 82);
			PlaceFooterButton(panel6, glassButton4, 104, 96);
			PlaceFooterButton(panel6, glassButton8, 228, 96);
			PlaceFooterButton(panel6, glassButton6, 344, 96);
			Action updateCount = () =>
			{
				count.Text = "Selected: " + grid.SelectedRows.Count + " / " + items.Count;
			};
			grid.SelectionChanged += (object param0, EventArgs param1) =>
			{
				updateCount();
			};
			grid.KeyDown += (object sender, KeyEventArgs e) =>
			{
				if (e.Control && e.KeyCode == Keys.A)
				{
					grid.SelectAll();
					e.Handled = true;
				}
			};
			glassButton2.Click += (object param0, EventArgs param1) =>
			{
				grid.ClearSelection();
				updateCount();
			};
			glassButton4.Click += (object param0, EventArgs param1) =>
			{
				grid.SelectAll();
				updateCount();
			};
			panel4.Controls.Add(panel5);
			panel4.Controls.Add(panel6);
			bIMlifyForm.Controls.Add(grid);
			bIMlifyForm.Controls.Add(panel4);
			bIMlifyForm.Controls.Add(panel2);
			AddWindowSidebar(bIMlifyForm, dashboard: false);
			bIMlifyForm.AcceptButton = glassButton6;
			bIMlifyForm.CancelButton = glassButton8;
			if (BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(bIMlifyForm) != DialogResult.OK)
			{
				return new List<T>();
			}
			return (from DataGridViewRow r in grid.SelectedRows
				select (T)r.Tag).ToList();
		}
		finally
		{
			if (grid != null)
			{
				((IDisposable)grid).Dispose();
			}
		}
	}

	private static LevelBubbleSelection SelectLevelBubbleInputs(IEnumerable<View> views, IEnumerable<Level> levels)
	{
		LevelBubbleSelection levelBubbleSelection = new LevelBubbleSelection();
		using (BIMlifyForm bIMlifyForm = new BIMlifyForm())
		{
			using TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
			DataGridView viewGrid = new DataGridView();
			try
			{
				DataGridView levelGrid = new DataGridView();
				try
				{
					TextBox viewSearch = new AdvancedSearchTextBox();
					try
					{
						TextBox levelSearch = new AdvancedSearchTextBox();
						try
						{
							bIMlifyForm.Text = "BIMlify — Level Bubbles";
							bIMlifyForm.Width = 1080;
							bIMlifyForm.Height = 720;
							bIMlifyForm.MinimumSize = new Size(900, 600);
							bIMlifyForm.StartPosition = FormStartPosition.CenterScreen;
							bIMlifyForm.ShowIcon = false;
							bIMlifyForm.MaximizeBox = true;
							bIMlifyForm.MinimizeBox = true;
							bIMlifyForm.AutoScaleMode = AutoScaleMode.Dpi;
							bIMlifyForm.BackColor = Color.FromArgb(238, 241, 245);
							UseSmoothCorners(bIMlifyForm);
							Panel panel = new Panel();
							panel.Dock = DockStyle.Top;
							panel.Height = 128;
							panel.BackColor = Color.FromArgb(247, 247, 245);
							panel.Padding = new Padding(16, 9, 4, 9);
							Panel panel2 = panel;
							Label label = new Label();
							label.Left = 18;
							label.Top = 12;
							label.Width = 650;
							label.Height = 24;
							label.Text = "Level Bubbles";
							label.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
							label.ForeColor = Color.FromArgb(78, 85, 92);
							Label value = label;
							Label label2 = new Label();
							label2.Left = 18;
							label2.Top = 39;
							label2.Width = 730;
							label2.Height = 52;
							label2.Text = "Choose elevation views and levels in one window, then continue to set bubble ends.";
							label2.ForeColor = Color.FromArgb(102, 109, 118);
							label2.Font = new Font("Segoe UI", 8f);
							Label value2 = label2;
							panel2.Controls.Add(value);
							panel2.Controls.Add(value2);
							panel2.Controls.Add(CreateBrandLockup(panel2.BackColor));
							AddYellowPanelAccent(panel2, Color.FromArgb(214, 171, 61), atTop: false);
							Action<DataGridView> action = (DataGridView grid) =>
							{
								grid.Dock = DockStyle.Fill;
								grid.AllowUserToAddRows = false;
								grid.AllowUserToDeleteRows = false;
								grid.AllowUserToResizeRows = false;
								grid.RowHeadersVisible = false;
								grid.ReadOnly = true;
								grid.MultiSelect = true;
								grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
								grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
								grid.ScrollBars = ScrollBars.Vertical;
								grid.BackgroundColor = Color.White;
								grid.BorderStyle = BorderStyle.None;
								grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
								grid.GridColor = Color.FromArgb(215, 220, 225);
								grid.Font = new Font("Segoe UI", 9f);
								grid.RowTemplate.Height = 32;
								grid.EnableHeadersVisualStyles = false;
								grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 245);
								grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
								grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
								grid.ColumnHeadersHeight = 34;
								grid.DefaultCellStyle.BackColor = Color.White;
								grid.DefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
								grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
								grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(78, 85, 92);
								grid.DefaultCellStyle.Padding = new Padding(18, 3, 5, 3);
								grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 248, 249);
								grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
							};
							action(viewGrid);
							action(levelGrid);
							viewGrid.Columns.Add(new DataGridViewTextBoxColumn
							{
								HeaderText = "Elevation views — Shift/Ctrl to select"
							});
							levelGrid.Columns.Add(new DataGridViewTextBoxColumn
							{
								HeaderText = "Levels — Shift/Ctrl to select"
							});
							List<View> list = views.ToList();
							List<Level> list2 = levels.ToList();
							foreach (View item in list)
							{
								int index = viewGrid.Rows.Add(((Element)item).Name + " — ID " + ((Element)item).Id.IntegerValue);
								viewGrid.Rows[index].Tag = item;
							}
							foreach (Level item2 in list2)
							{
								int index = levelGrid.Rows.Add(((Element)item2).Name + " | Elevation " + item2.Elevation.ToString("0.###") + " | ID " + ((Element)item2).Id.IntegerValue);
								levelGrid.Rows[index].Tag = item2;
							}
							Panel panel3 = new Panel();
							panel3.Dock = DockStyle.Fill;
							panel3.BackColor = Color.White;
							panel3.Padding = new Padding(10, 8, 10, 8);
							Panel panel4 = panel3;
							Panel panel5 = new Panel();
							panel5.Dock = DockStyle.Fill;
							panel5.BackColor = Color.White;
							panel5.Padding = new Padding(10, 8, 10, 8);
							Panel panel6 = panel5;
							Label label3 = new Label();
							label3.Text = "Elevation Views — Shift/Ctrl to select";
							label3.Dock = DockStyle.Top;
							label3.Height = 34;
							label3.TextAlign = ContentAlignment.MiddleLeft;
							label3.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
							label3.ForeColor = Color.FromArgb(78, 85, 92);
							Label value3 = label3;
							Label label4 = new Label();
							label4.Text = "Levels — Shift/Ctrl to select";
							label4.Dock = DockStyle.Top;
							label4.Height = 34;
							label4.TextAlign = ContentAlignment.MiddleLeft;
							label4.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
							label4.ForeColor = Color.FromArgb(78, 85, 92);
							Label value4 = label4;
							viewGrid.ColumnHeadersVisible = false;
							levelGrid.ColumnHeadersVisible = false;
							viewSearch.Dock = DockStyle.Top;
							viewSearch.Height = 32;
							viewSearch.Font = new Font("Segoe UI", 8.5f);
							StyleSearchBox(viewSearch, Color.FromArgb(244, 246, 248), Color.FromArgb(78, 85, 92), "Search elevation views");
							levelSearch.Dock = DockStyle.Top;
							levelSearch.Height = 32;
							levelSearch.Font = new Font("Segoe UI", 8.5f);
							StyleSearchBox(levelSearch, Color.FromArgb(244, 246, 248), Color.FromArgb(78, 85, 92), "Search levels");
							panel4.Controls.Add(viewGrid);
							panel4.Controls.Add(viewSearch);
							panel4.Controls.Add(value3);
							panel6.Controls.Add(levelGrid);
							panel6.Controls.Add(levelSearch);
							panel6.Controls.Add(value4);
							Action<Panel> action2 = (Panel card) =>
							{
								Action round = () =>
								{
									if (card.Width > 2 && card.Height > 2)
									{
										using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, card.Width, card.Height), 11))
										{
											Region region = card.Region;
											card.Region = new Region(path);
											region?.Dispose();
										}
									}
								};
								card.Resize += (object param0, EventArgs param1) =>
								{
									round();
								};
								card.HandleCreated += (object param0, EventArgs param1) =>
								{
									round();
								};
								card.Paint += (object sender, PaintEventArgs e) =>
								{
									e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
									using GraphicsPath path = RoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 10);
									using Pen pen = new Pen(Color.FromArgb(205, 211, 217), 1f);
									e.Graphics.DrawPath(pen, path);
								};
							};
							action2(panel4);
							action2(panel6);
							tableLayoutPanel.Dock = DockStyle.Fill;
							tableLayoutPanel.ColumnCount = 2;
							tableLayoutPanel.RowCount = 1;
							tableLayoutPanel.BackColor = bIMlifyForm.BackColor;
							tableLayoutPanel.Padding = new Padding(10, 8, 10, 8);
							tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
							tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
							tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
							tableLayoutPanel.Controls.Add(panel4, 0, 0);
							tableLayoutPanel.Controls.Add(panel6, 1, 0);
							Action applyViewFilter = () =>
							{
								string text = viewSearch.Text ?? "";
								foreach (DataGridViewRow item3 in (IEnumerable)viewGrid.Rows)
								{
									string text2 = Convert.ToString(item3.Cells[0].Value) ?? "";
									item3.Visible = string.IsNullOrWhiteSpace(text) || SearchTextMatches(text, text2);
								}
							};
							Action applyLevelFilter = () =>
							{
								string text = levelSearch.Text ?? "";
								foreach (DataGridViewRow item4 in (IEnumerable)levelGrid.Rows)
								{
									string text2 = Convert.ToString(item4.Cells[0].Value) ?? "";
									item4.Visible = string.IsNullOrWhiteSpace(text) || SearchTextMatches(text, text2);
								}
							};
							viewSearch.TextChanged += (object param0, EventArgs param1) =>
							{
								applyViewFilter();
							};
							levelSearch.TextChanged += (object param0, EventArgs param1) =>
							{
								applyLevelFilter();
							};
							Panel panel7 = new Panel();
							panel7.Dock = DockStyle.Bottom;
							panel7.Height = 64;
							panel7.BackColor = Color.FromArgb(216, 221, 227);
							panel7.Padding = new Padding(10, 8, 10, 8);
							Panel panel8 = panel7;
							AddFooterAccent(panel8);
							Label count = new Label
							{
								Dock = DockStyle.Left,
								Width = 260,
								Text = "Views: 0 | Levels: 0",
								TextAlign = ContentAlignment.MiddleLeft,
								ForeColor = Color.FromArgb(78, 85, 92)
							};
							GlassButton glassButton = new GlassButton();
							glassButton.Text = "Clear";
							glassButton.Width = 82;
							GlassButton glassButton2 = glassButton;
							GlassButton glassButton3 = new GlassButton();
							glassButton3.Text = "Select All";
							glassButton3.Width = 96;
							GlassButton glassButton4 = glassButton3;
							GlassButton glassButton5 = new GlassButton();
							glassButton5.Text = "Cancel";
							glassButton5.Width = 96;
							glassButton5.DialogResult = DialogResult.Cancel;
							GlassButton glassButton6 = glassButton5;
							GlassButton glassButton7 = new GlassButton();
							glassButton7.Text = "Continue";
							glassButton7.Width = 96;
							glassButton7.Primary = true;
							glassButton7.DialogResult = DialogResult.OK;
							GlassButton glassButton8 = glassButton7;
							Panel panel9 = CreateFooterButtonGroup(rightAligned: true, 400);
							PlaceFooterButton(panel9, glassButton2, 0, 82);
							PlaceFooterButton(panel9, glassButton4, 104, 96);
							PlaceFooterButton(panel9, glassButton6, 228, 96);
							PlaceFooterButton(panel9, glassButton8, 344, 96);
							panel8.Controls.Add(count);
							panel8.Controls.Add(panel9);
							Action update = () =>
							{
								count.Text = "Views: " + viewGrid.SelectedRows.Count + " | Levels: " + levelGrid.SelectedRows.Count;
							};
							viewGrid.SelectionChanged += (object param0, EventArgs param1) =>
							{
								update();
							};
							levelGrid.SelectionChanged += (object param0, EventArgs param1) =>
							{
								update();
							};
							glassButton2.Click += (object param0, EventArgs param1) =>
							{
								viewGrid.ClearSelection();
								levelGrid.ClearSelection();
								update();
							};
							glassButton4.Click += (object param0, EventArgs param1) =>
							{
								viewGrid.SelectAll();
								levelGrid.SelectAll();
								update();
							};
							bIMlifyForm.Controls.Add(tableLayoutPanel);
							bIMlifyForm.Controls.Add(panel8);
							bIMlifyForm.Controls.Add(panel2);
							AddWindowSidebar(bIMlifyForm, dashboard: false);
							bIMlifyForm.AcceptButton = glassButton8;
							bIMlifyForm.CancelButton = glassButton6;
							if (BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(bIMlifyForm) == DialogResult.OK)
							{
								levelBubbleSelection.Views = viewGrid.SelectedRows.Cast<DataGridViewRow>().Select((Func<DataGridViewRow, View>)((DataGridViewRow r) =>
								{
									//IL_0006: Unknown result type (might be due to invalid IL or missing references)
									//IL_000c: Expected Obj, but got Unknown
									return (View)r.Tag;
								})).ToList();
								levelBubbleSelection.Levels = levelGrid.SelectedRows.Cast<DataGridViewRow>().Select((Func<DataGridViewRow, Level>)((DataGridViewRow r) =>
								{
									//IL_0006: Unknown result type (might be due to invalid IL or missing references)
									//IL_000c: Expected Obj, but got Unknown
									return (Level)r.Tag;
								})).ToList();
							}
						}
						finally
						{
							if (levelSearch != null)
							{
								((IDisposable)levelSearch).Dispose();
							}
						}
					}
					finally
					{
						if (viewSearch != null)
						{
							((IDisposable)viewSearch).Dispose();
						}
					}
				}
				finally
				{
					if (levelGrid != null)
					{
						((IDisposable)levelGrid).Dispose();
					}
				}
			}
			finally
			{
				if (viewGrid != null)
				{
					((IDisposable)viewGrid).Dispose();
				}
			}
		}
		return levelBubbleSelection;
	}

	private static string CategoryKey(Category category)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			return (category == null) ? null : (((object)category.CategoryType).ToString() + "\u001f" + category.Name);
		}
		catch
		{
			return null;
		}
	}

	private static void SheetOrganization(UIDocument ui)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		if (ui == null || ui.Document == null)
		{
			return;
		}
		Document doc = ui.Document;
		List<ViewSheet> list = (from ViewSheet s in (IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
			where !s.IsPlaceholder
			select s).OrderBy((ViewSheet s) => s.SheetNumber, StringComparer.OrdinalIgnoreCase).ToList();
		if (list.Count == 0)
		{
			TaskDialog.Show("BIMlify", "No project sheets were found.");
			return;
		}
		using BIMlifyForm bIMlifyForm = new BIMlifyForm();
		DataGridView grid = new DataGridView();
		try
		{
			TextBox sheetSearch = new AdvancedSearchTextBox();
			try
			{
				Panel gridCard = new Panel();
				try
				{
					bIMlifyForm.Text = "BIMlify — Smart Sheets";
					bIMlifyForm.Width = 1120;
					bIMlifyForm.Height = 760;
					bIMlifyForm.MinimumSize = new Size(980, 640);
					bIMlifyForm.StartPosition = FormStartPosition.CenterScreen;
					bIMlifyForm.ShowIcon = false;
					bIMlifyForm.MaximizeBox = true;
					bIMlifyForm.MinimizeBox = true;
					bIMlifyForm.AutoScaleMode = AutoScaleMode.Dpi;
					bIMlifyForm.BackColor = Color.FromArgb(238, 241, 245);
					UseSmoothCorners(bIMlifyForm);
					Panel panel = new Panel();
					panel.Dock = DockStyle.Top;
					panel.Height = 128;
					panel.BackColor = Color.FromArgb(247, 247, 245);
					panel.Padding = new Padding(14, 10, 14, 8);
					Panel panel2 = panel;
					Label label = new Label();
					label.Left = 18;
					label.Top = 12;
					label.Width = 620;
					label.Height = 24;
					label.Text = "Smart Sheets";
					label.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
					label.ForeColor = Color.FromArgb(78, 85, 92);
					Label value = label;
					Label label2 = new Label();
					label2.Left = 18;
					label2.Top = 39;
					label2.Width = 740;
					label2.Height = 40;
					label2.Text = "Group and number sheets using the company organization code you enter below. Review the preview before applying.";
					label2.ForeColor = Color.FromArgb(102, 109, 118);
					label2.Font = new Font("Segoe UI", 8f);
					Label value2 = label2;
					panel2.Controls.Add(value);
					panel2.Controls.Add(value2);
					panel2.Controls.Add(CreateBrandLockup(panel2.BackColor));
					AddYellowPanelAccent(panel2, Color.FromArgb(214, 171, 61), atTop: false);
					Panel panel3 = new Panel();
					panel3.Dock = DockStyle.Top;
					panel3.Height = 84;
					panel3.BackColor = bIMlifyForm.BackColor;
					panel3.Padding = new Padding(18, 7, 18, 9);
					Panel panel4 = panel3;
					Label label3 = new Label();
					label3.Text = "Separator";
					label3.AutoSize = true;
					label3.Left = 18;
					label3.Top = 9;
					label3.ForeColor = Color.FromArgb(78, 85, 92);
					Label value3 = label3;
					TextBox separator = new TextBox
					{
						Text = "-",
						Left = 86,
						Top = 5,
						Width = 42,
						Height = 25
					};
					Label label4 = new Label();
					label4.Text = "Digits";
					label4.AutoSize = true;
					label4.Left = 146;
					label4.Top = 9;
					label4.ForeColor = Color.FromArgb(78, 85, 92);
					Label value4 = label4;
					NumericUpDown digits = new NumericUpDown
					{
						Left = 192,
						Top = 5,
						Width = 52,
						Minimum = 1m,
						Maximum = 5m,
						Value = 3m
					};
					Label label5 = new Label();
					label5.Text = "Start";
					label5.AutoSize = true;
					label5.Left = 264;
					label5.Top = 9;
					label5.ForeColor = Color.FromArgb(78, 85, 92);
					Label value5 = label5;
					NumericUpDown start = new NumericUpDown
					{
						Left = 304,
						Top = 5,
						Width = 64,
						Minimum = 1m,
						Maximum = 9999m,
						Value = 1m
					};
					Label label6 = new Label();
					label6.Text = "Edit Group Code or Sheet Name; select multiple rows for batch editing.";
					label6.AutoSize = true;
					label6.Left = 392;
					label6.Top = 9;
					label6.ForeColor = Color.FromArgb(102, 109, 118);
					Label value6 = label6;
					Label label7 = new Label();
					label7.Text = "Filter sheets";
					label7.AutoSize = true;
					label7.Left = 18;
					label7.Top = 50;
					label7.ForeColor = Color.FromArgb(78, 85, 92);
					Label value7 = label7;
					sheetSearch.SetBounds(108, 45, 360, 30);
					sheetSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left;
					sheetSearch.Font = new Font("Segoe UI", 8.5f);
					StyleSearchBox(sheetSearch, Color.FromArgb(238, 241, 245), Color.FromArgb(78, 85, 92), "Search sheet number, name, or group");
					panel4.Controls.Add(value3);
					panel4.Controls.Add(separator);
					panel4.Controls.Add(value4);
					panel4.Controls.Add(digits);
					panel4.Controls.Add(value5);
					panel4.Controls.Add(start);
					panel4.Controls.Add(value6);
					panel4.Controls.Add(value7);
					panel4.Controls.Add(sheetSearch);
					grid.Dock = DockStyle.Fill;
					grid.AllowUserToAddRows = false;
					grid.AllowUserToDeleteRows = false;
					grid.AllowUserToResizeRows = false;
					grid.RowHeadersVisible = false;
					grid.MultiSelect = true;
					grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
					grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
					grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
					grid.BackgroundColor = Color.White;
					grid.BorderStyle = BorderStyle.None;
					grid.Font = new Font("Segoe UI", 8.5f);
					grid.RowTemplate.Height = 30;
					grid.EnableHeadersVisualStyles = false;
					grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 241, 245);
					grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
					grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
					grid.ColumnHeadersHeight = 32;
					grid.DefaultCellStyle.BackColor = Color.FromArgb(237, 240, 244);
					grid.DefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
					grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
					grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(78, 85, 92);
					grid.DefaultCellStyle.Padding = new Padding(6, 2, 4, 2);
					grid.Columns.Add(new DataGridViewCheckBoxColumn
					{
						HeaderText = "Use",
						Width = 48,
						FillWeight = 8f
					});
					grid.Columns.Add(new DataGridViewTextBoxColumn
					{
						HeaderText = "Group Code",
						FillWeight = 18f
					});
					grid.Columns.Add(new DataGridViewTextBoxColumn
					{
						HeaderText = "Preview Number",
						ReadOnly = true,
						FillWeight = 20f
					});
					grid.Columns.Add(new DataGridViewTextBoxColumn
					{
						HeaderText = "Sheet Number",
						ReadOnly = true,
						FillWeight = 20f
					});
					grid.Columns.Add(new DataGridViewTextBoxColumn
					{
						HeaderText = "Sheet Name",
						ReadOnly = false,
						FillWeight = 44f
					});
					foreach (ViewSheet item in list)
					{
						string text = item.SheetNumber ?? "";
						string text2 = text;
						int num = text.IndexOf('-');
						if (num > 0)
						{
							text2 = text.Substring(0, num);
						}
						else
						{
							text2 = new string(text.TakeWhile((char c) => char.IsLetter(c)).ToArray());
							if (string.IsNullOrEmpty(text2))
							{
								text2 = "G";
							}
						}
						int index = grid.Rows.Add(true, text2.ToUpperInvariant(), "", text, ((Element)item).Name);
						grid.Rows[index].Tag = item;
					}
					Action refresh = () =>
					{
						Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
						int num2 = (int)digits.Value;
						string text3 = separator.Text ?? "-";
						string text4 = (sheetSearch.Text ?? "").Trim();
						foreach (DataGridViewRow item2 in (IEnumerable)grid.Rows)
						{
							bool flag = Convert.ToBoolean(item2.Cells[0].Value);
							string text5 = (Convert.ToString(item2.Cells[1].Value) ?? "").Trim().ToUpperInvariant();
							if (string.IsNullOrEmpty(text5))
							{
								text5 = "G";
							}
							if (!dictionary.ContainsKey(text5))
							{
								dictionary[text5] = (int)start.Value;
							}
							int num3 = dictionary[text5]++;
							item2.Cells[2].Value = (flag ? (text5 + text3 + num3.ToString("D" + num2)) : "(unchanged)");
							string text6 = Convert.ToString(item2.Cells[1].Value) + " " + Convert.ToString(item2.Cells[3].Value) + " " + Convert.ToString(item2.Cells[4].Value);
							item2.Visible = string.IsNullOrEmpty(text4) || SearchTextMatches(text4, text6);
						}
					};
					bool syncingGroup = false;
					List<int> groupBatchRows = new List<int>();
					grid.CellDoubleClick += (object sender, DataGridViewCellEventArgs e) =>
					{
						if (e.RowIndex >= 0 && e.ColumnIndex >= 2)
						{
							grid.CurrentCell = grid.Rows[e.RowIndex].Cells[1];
							grid.BeginEdit(selectAll: true);
						}
					};
					grid.CellMouseDown += (object sender, DataGridViewCellMouseEventArgs e) =>
					{
						if (e.RowIndex >= 0 && (e.ColumnIndex == 1 || e.ColumnIndex == 4))
						{
							groupBatchRows = (from DataGridViewRow row in grid.SelectedRows
								select row.Index).Distinct().ToList();
						}
					};
					grid.CellBeginEdit += (object sender, DataGridViewCellCancelEventArgs e) =>
					{
						if (e.RowIndex >= 0 && (e.ColumnIndex == 1 || e.ColumnIndex == 4))
						{
							if (groupBatchRows.Count == 0)
							{
								groupBatchRows = (from DataGridViewRow row in grid.SelectedRows
									select row.Index).Distinct().ToList();
							}
							if (!groupBatchRows.Contains(e.RowIndex))
							{
								groupBatchRows.Add(e.RowIndex);
							}
						}
					};
					grid.CellValueChanged += (object sender, DataGridViewCellEventArgs e) =>
					{
						if (!syncingGroup && e.RowIndex >= 0 && (e.ColumnIndex == 1 || e.ColumnIndex == 4))
						{
							syncingGroup = true;
							try
							{
								string value8 = Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) ?? "";
								List<int> list2 = ((groupBatchRows.Count > 0) ? groupBatchRows : (from DataGridViewRow row in grid.SelectedRows
									select row.Index).ToList());
								foreach (int item3 in list2)
								{
									if (item3 >= 0 && item3 < grid.Rows.Count && item3 != e.RowIndex)
									{
										grid.Rows[item3].Cells[e.ColumnIndex].Value = value8;
									}
								}
							}
							finally
							{
								syncingGroup = false;
								groupBatchRows.Clear();
							}
						}
						refresh();
					};
					grid.CurrentCellDirtyStateChanged += (object param0, EventArgs param1) =>
					{
						if (grid.IsCurrentCellDirty)
						{
							grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
						}
					};
					separator.TextChanged += (object param0, EventArgs param1) =>
					{
						refresh();
					};
					digits.ValueChanged += (object param0, EventArgs param1) =>
					{
						refresh();
					};
					start.ValueChanged += (object param0, EventArgs param1) =>
					{
						refresh();
					};
					sheetSearch.TextChanged += (object param0, EventArgs param1) =>
					{
						refresh();
					};
					refresh();
					Panel panel5 = new Panel();
					panel5.Dock = DockStyle.Bottom;
					panel5.Height = 64;
					panel5.BackColor = Color.FromArgb(216, 221, 227);
					panel5.Padding = new Padding(10, 8, 10, 8);
					Panel panel6 = panel5;
					AddFooterAccent(panel6);
					Label status = new Label
					{
						Dock = DockStyle.Left,
						Width = 520,
						Text = list.Count + " sheets loaded",
						TextAlign = ContentAlignment.MiddleLeft,
						ForeColor = Color.FromArgb(78, 85, 92)
					};
					GlassButton glassButton = new GlassButton();
					glassButton.Text = "Close"; glassButton.Name = "BIMlifyConfirmContinue";
					glassButton.Width = 96;
					glassButton.DialogResult = DialogResult.Cancel;
					GlassButton glassButton2 = glassButton;
					GlassButton apply = new GlassButton
					{
						Text = "Apply Organization",
						Width = 150,
						Primary = true
					};
					Panel panel7 = CreateFooterButtonGroup(rightAligned: true, 270);
					PlaceFooterButton(panel7, glassButton2, 0, 96);
					PlaceFooterButton(panel7, apply, 112, 150);
					panel6.Controls.Add(status);
					panel6.Controls.Add(panel7);
					apply.Click += (object param0, EventArgs param1) =>
					{
						//IL_0352: Unknown result type (might be due to invalid IL or missing references)
						//IL_0095: Unknown result type (might be due to invalid IL or missing references)
						//IL_009c: Expected Obj, but got Unknown
						//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
						//IL_01c3: Expected Obj, but got Unknown
						//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
						//IL_02ea: Unknown result type (might be due to invalid IL or missing references)
						try
						{
							Dictionary<ViewSheet, string> dictionary = new Dictionary<ViewSheet, string>();
							Dictionary<ViewSheet, string> dictionary2 = new Dictionary<ViewSheet, string>();
							Dictionary<string, int> dictionary3 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
							int num2 = (int)digits.Value;
							string text3 = separator.Text ?? "-";
							foreach (DataGridViewRow item4 in (IEnumerable)grid.Rows)
							{
								if (Convert.ToBoolean(item4.Cells[0].Value))
								{
									ViewSheet val = (ViewSheet)item4.Tag;
									string text4 = (Convert.ToString(item4.Cells[1].Value) ?? "").Trim().ToUpperInvariant();
									if (string.IsNullOrEmpty(text4))
									{
										text4 = "G";
									}
									if (!dictionary3.ContainsKey(text4))
									{
										dictionary3[text4] = (int)start.Value;
									}
									dictionary[val] = text4 + text3 + dictionary3[text4]++.ToString("D" + num2);
									dictionary2[val] = Convert.ToString(item4.Cells[4].Value) ?? ((Element)val).Name;
								}
							}
							Transaction val2 = new Transaction(doc, "BIMlify Sheet Organization");
							try
							{
								val2.Start();
								foreach (ViewSheet key in dictionary.Keys)
								{
									key.SheetNumber = "BIMlify_TMP_" + ((Element)key).Id.IntegerValue;
								}
								foreach (KeyValuePair<ViewSheet, string> item5 in dictionary)
								{
									item5.Key.SheetNumber = item5.Value;
								}
								foreach (KeyValuePair<ViewSheet, string> item6 in dictionary2)
								{
									if (!string.IsNullOrWhiteSpace(item6.Value) && ((Element)item6.Key).Name != item6.Value)
									{
										((Element)item6.Key).Name = item6.Value;
									}
								}
								val2.Commit();
							}
							finally
							{
								((IDisposable)val2)?.Dispose();
							}
							status.Text = dictionary.Count + " sheets organized successfully";
							apply.Enabled = false;
						}
						catch (Exception ex)
						{
							TaskDialog.Show("BIMlify", "Could not organize sheets.\\n\\n" + ex.Message);
						}
					};
					gridCard.Dock = DockStyle.Fill;
					gridCard.BackColor = Color.White;
					gridCard.Padding = new Padding(8, 6, 8, 8);
					gridCard.Controls.Add(grid);
					bIMlifyForm.Controls.Add(gridCard);
					bIMlifyForm.Controls.Add(panel4);
					bIMlifyForm.Controls.Add(panel6);
					bIMlifyForm.Controls.Add(panel2);
					AddWindowSidebar(bIMlifyForm, dashboard: false);
					bIMlifyForm.CancelButton = glassButton2;
					UiPolish.ApplySheetOrganizationStyle(bIMlifyForm);
					EmptyState.StyleSheetOrganization(bIMlifyForm);
					BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(bIMlifyForm);
				}
				finally
				{
					if (gridCard != null)
					{
						((IDisposable)gridCard).Dispose();
					}
				}
			}
			finally
			{
				if (sheetSearch != null)
				{
					((IDisposable)sheetSearch).Dispose();
				}
			}
		}
		finally
		{
			if (grid != null)
			{
				((IDisposable)grid).Dispose();
			}
		}
	}

	private static Dictionary<string, int> CountElementsByCategory(Document doc, View view)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Expected Obj, but got Unknown
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		try
		{
			FilteredElementCollector val = ((view == null) ? new FilteredElementCollector(doc) : new FilteredElementCollector(doc, ((Element)view).Id));
			foreach (Element item in val.WhereElementIsNotElementType())
			{
				string text = CategoryKey(item.Category);
				if (text != null)
				{
					dictionary[text] = ((!dictionary.ContainsKey(text)) ? 1 : (dictionary[text] + 1));
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private static List<CategoryEntry> BuildCategoryEntries(Document doc, View view)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Expected Obj, but got Unknown
		//IL_01a5: Unknown result type (might be due to invalid IL or missing references)
		Dictionary<string, CategoryEntry> entries = new Dictionary<string, CategoryEntry>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, int> projectCounts = CountElementsByCategory(doc, null);
		Dictionary<string, int> viewCounts = CountElementsByCategory(doc, view);
		List<Document> list = new List<Document>();
		List<RevitLinkInstance> list2 = new List<RevitLinkInstance>();
		try
		{
			foreach (RevitLinkInstance item in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)))
			{
				RevitLinkInstance val = item;
				Document linkDocument = val.GetLinkDocument();
				if (linkDocument != null)
				{
					list2.Add(val);
					list.Add(linkDocument);
				}
			}
		}
		catch
		{
		}
		foreach (Document item2 in list)
		{
			foreach (KeyValuePair<string, int> item3 in CountElementsByCategory(item2, null))
			{
				projectCounts[item3.Key] = (projectCounts.ContainsKey(item3.Key) ? (projectCounts[item3.Key] + item3.Value) : item3.Value);
			}
		}
		foreach (RevitLinkInstance item4 in list2)
		{
			try
			{
				foreach (Element item5 in new FilteredElementCollector(doc, ((Element)view).Id, ((Element)item4).Id).WhereElementIsNotElementType())
				{
					string text = CategoryKey(item5.Category);
					if (text != null)
					{
						viewCounts[text] = ((!viewCounts.ContainsKey(text)) ? 1 : (viewCounts[text] + 1));
					}
				}
			}
			catch
			{
			}
		}
		Action<Category> add = (Category category) =>
		{
			//IL_005d: Unknown result type (might be due to invalid IL or missing references)
			if (category.Parent != null)
			{
				return;
			}
			try
			{
				string text2 = CategoryKey(category);
				if (text2 != null && !entries.ContainsKey(text2))
				{
					entries.Add(text2, new CategoryEntry
					{
						Category = category,
						Key = text2,
						Name = category.Name,
						Type = ((object)category.CategoryType).ToString(),
						InView = (viewCounts.ContainsKey(text2) ? viewCounts[text2] : 0),
						InProject = (projectCounts.ContainsKey(text2) ? projectCounts[text2] : 0)
					});
				}
			}
			catch
			{
			}
		};
		Action<Document> action = (Document source) =>
		{
			//IL_00de: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0025: Expected Obj, but got Unknown
			//IL_005a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0060: Expected Obj, but got Unknown
			try
			{
				foreach (Category item6 in (CategoryNameMap)source.Settings.Categories)
				{
					Category val2 = item6;
					add(val2);
					try
					{
						if (val2.SubCategories != null)
						{
							foreach (Category subCategory in val2.SubCategories)
							{
								Category obj3 = subCategory;
								add(obj3);
							}
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			try
			{
				foreach (Element item7 in new FilteredElementCollector(source).WhereElementIsNotElementType())
				{
					add(item7.Category);
				}
			}
			catch
			{
			}
		};
		action(doc);
		foreach (Document item8 in list)
		{
			action(item8);
		}
		return entries.Values.OrderBy((CategoryEntry x) => x.Type).ThenBy((CategoryEntry x) => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	private static CategoryPickerResult ShowCategoryPicker(Document doc, View activeView, string requestedAction = null, ICollection<string> initiallySelected = null)
	{
		List<CategoryEntry> list = BuildCategoryEntries(doc, activeView);
		CategoryPickerResult result = new CategoryPickerResult();
		BIMlifyForm form = new BIMlifyForm();
		try
		{
			DataGridView grid = new DataGridView();
			try
			{
				TextBox search = new AdvancedSearchTextBox();
				try
				{
					ComboBox typeFilter = new LightGoldComboBox();
					try
					{
						Color color = Color.FromArgb(238, 241, 245);
						Color color2 = Color.FromArgb(238, 241, 245);
						Color color3 = Color.FromArgb(214, 171, 61);
						Color color4 = Color.FromArgb(78, 85, 92);
						form.Text = "BIMlify — Category Select";
						form.Width = 1080;
						form.Height = 720;
						form.MinimumSize = new Size(980, 640);
						form.StartPosition = FormStartPosition.CenterScreen;
						form.ShowIcon = false;
						form.MaximizeBox = true;
						form.MinimizeBox = true;
						form.AutoScaleMode = AutoScaleMode.Dpi;
						form.BackColor = color;
						form.ForeColor = color4;
						form.Font = new Font("Segoe UI", 9f);
						UseSmoothCorners(form);
						Color color5 = Color.FromArgb(238, 241, 245);
						Panel panel = new Panel();
						panel.Dock = DockStyle.Top;
						panel.Height = 128;
						panel.BackColor = Color.FromArgb(247, 247, 245);
						panel.Padding = new Padding(16, 9, 4, 9);
						Panel panel2 = panel;
						Label label = new Label();
						label.Left = 18;
						label.Top = 12;
						label.Width = 650;
						label.Height = 24;
						label.AutoEllipsis = false;
						label.Text = "Select or isolate categories";
						label.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
						label.ForeColor = Color.FromArgb(78, 85, 92);
						label.TextAlign = ContentAlignment.MiddleLeft;
						label.Padding = Padding.Empty;
						Label value = label;
						Label label2 = new Label();
						label2.Left = 18;
						label2.Top = 39;
						label2.Width = 760;
						label2.Height = 66;
						label2.AutoEllipsis = false;
						label2.Text = "Select includes matching elements in loaded links. Isolate keeps each matching link instance visible as a whole.";
						label2.ForeColor = Color.FromArgb(102, 109, 118);
						label2.TextAlign = ContentAlignment.TopLeft;
						label2.AutoSize = false;
						label2.Padding = Padding.Empty;
						label2.Font = new Font("Segoe UI", 8f);
						Label value2 = label2;
						panel2.Controls.Add(value2);
						panel2.Controls.Add(value);
						panel2.Controls.Add(CreateBrandLockup(panel2.BackColor));
						AddYellowPanelAccent(panel2, color3, atTop: false);
						Panel panel3 = new Panel();
						panel3.Dock = DockStyle.Top;
						panel3.Height = 44;
						panel3.BackColor = color;
						panel3.Padding = new Padding(14, 6, 14, 5);
						Panel panel4 = panel3;
						Label label3 = new Label();
						label3.Text = "Search";
						label3.Dock = DockStyle.Left;
						label3.Width = 52;
						label3.ForeColor = color4;
						label3.TextAlign = ContentAlignment.MiddleLeft;
						Label value3 = label3;
						search.Dock = DockStyle.Left;
						search.Width = 310;
						StyleSearchBox(search, color2, color4, "Search categories");
						Label label4 = new Label();
						label4.Text = "Category type";
						label4.Dock = DockStyle.Left;
						label4.Width = 126;
						label4.Height = 30;
						label4.AutoSize = false;
						label4.UseMnemonic = false;
						label4.ForeColor = color4;
						label4.TextAlign = ContentAlignment.MiddleCenter;
						Label label5 = label4;
						label5.AutoEllipsis = false;
						label5.Font = new Font("Segoe UI", 8.5f);
						label5.MinimumSize = new Size(126, 30);
						typeFilter.Dock = DockStyle.Left;
						typeFilter.Width = 170;
						typeFilter.DropDownStyle = ComboBoxStyle.DropDownList;
						typeFilter.FlatStyle = FlatStyle.Flat;
						typeFilter.BackColor = Color.FromArgb(255, 239, 190);
						typeFilter.ForeColor = color4;
						typeFilter.Items.Add("All types");
						foreach (string item in from x in list.Select((CategoryEntry x) => x.Type).Distinct()
							orderby x
							select x)
						{
							typeFilter.Items.Add(item);
						}
						typeFilter.SelectedIndex = 0;
						panel4.Controls.Add(typeFilter);
						panel4.Controls.Add(label5);
						panel4.Controls.Add(search);
						panel4.Controls.Add(value3);
						Label label6 = new Label();
						label6.Text = "✓ Loaded links included";
						label6.Dock = DockStyle.Right;
						label6.Width = 170;
						label6.ForeColor = color3;
						label6.TextAlign = ContentAlignment.MiddleRight;
						label6.Font = new Font("Segoe UI", 8.2f, FontStyle.Bold);
						Label value4 = label6;
						panel4.Controls.Add(value4);
						grid.Dock = DockStyle.Fill;
						grid.AllowUserToAddRows = false;
						grid.AllowUserToDeleteRows = false;
						grid.AllowUserToResizeRows = false;
						grid.RowHeadersVisible = false;
						grid.AutoGenerateColumns = false;
						grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
						grid.MultiSelect = true;
						grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
						grid.BackgroundColor = color;
						grid.BorderStyle = BorderStyle.None;
						grid.GridColor = Color.FromArgb(210, 215, 222);
						grid.EnableHeadersVisualStyles = false;
						grid.Font = new Font("Segoe UI", 9f);
						grid.RowTemplate.Height = 29;
						grid.ColumnHeadersDefaultCellStyle.BackColor = color2;
						grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(78, 85, 92);
						grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
						grid.ColumnHeadersHeight = 34;
						grid.DefaultCellStyle.BackColor = Color.FromArgb(237, 240, 244);
						grid.DefaultCellStyle.ForeColor = color4;
						grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
						grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(78, 85, 92);
						grid.DefaultCellStyle.Padding = new Padding(5, 2, 4, 2);
						grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(226, 230, 235);
						grid.AlternatingRowsDefaultCellStyle.ForeColor = color4;
						DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn = new DataGridViewCheckBoxColumn();
						dataGridViewCheckBoxColumn.HeaderText = "Use";
						dataGridViewCheckBoxColumn.Width = 58;
						dataGridViewCheckBoxColumn.FillWeight = 8f;
						dataGridViewCheckBoxColumn.FlatStyle = FlatStyle.Flat;
						DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn2 = dataGridViewCheckBoxColumn;
						dataGridViewCheckBoxColumn2.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
						grid.Columns.Add(dataGridViewCheckBoxColumn2);
						grid.Columns.Add(new DataGridViewTextBoxColumn
						{
							HeaderText = "Category",
							ReadOnly = true,
							FillWeight = 40f
						});
						grid.Columns.Add(new DataGridViewTextBoxColumn
						{
							HeaderText = "Type",
							ReadOnly = true,
							FillWeight = 18f
						});
						grid.Columns.Add(new DataGridViewTextBoxColumn
						{
							HeaderText = "In view",
							ReadOnly = true,
							FillWeight = 12f
						});
						grid.Columns.Add(new DataGridViewTextBoxColumn
						{
							HeaderText = "In project",
							ReadOnly = true,
							FillWeight = 14f
						});
						HashSet<string> hashSet = new HashSet<string>(initiallySelected ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
						foreach (CategoryEntry item2 in list)
						{
							int index = grid.Rows.Add(hashSet.Contains(item2.Key), item2.Name, item2.Type, item2.InView, item2.InProject);
							grid.Rows[index].Tag = item2;
						}
						grid.CurrentCellDirtyStateChanged += (object param0, EventArgs param1) =>
						{
							if (grid.IsCurrentCellDirty)
							{
								grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
							}
						};
						Func<List<CategoryEntry>> selectedCategories = () => (from DataGridViewRow r in grid.Rows
							where r.Cells[0].Value != null && Convert.ToBoolean(r.Cells[0].Value)
							select (CategoryEntry)r.Tag).ToList();
						int categoryCheckAnchor = -1;
						bool categoryCheckPriorValue = false;
						List<DataGridViewRow> categoryCheckboxSelectionSnapshot = new List<DataGridViewRow>();
						Action applyFilter = () =>
						{
							string query = (search.Text ?? "").Trim();
							string text = ((typeFilter.SelectedIndex <= 0) ? null : typeFilter.SelectedItem.ToString());
							foreach (DataGridViewRow item3 in (IEnumerable)grid.Rows)
							{
								CategoryEntry categoryEntry = (CategoryEntry)item3.Tag;
								bool flag = SearchTextMatches(query, categoryEntry.Name, categoryEntry.Type, categoryEntry.Key, categoryEntry.InView.ToString(), categoryEntry.InProject.ToString());
								bool flag2 = text == null || categoryEntry.Type == text;
								item3.Visible = flag && flag2;
							}
						};
						search.TextChanged += (object param0, EventArgs param1) =>
						{
							applyFilter();
						};
						typeFilter.SelectedIndexChanged += (object param0, EventArgs param1) =>
						{
							applyFilter();
						};
						Panel panel5 = new Panel();
						panel5.Dock = DockStyle.Bottom;
						panel5.Height = 64;
						panel5.BackColor = Color.FromArgb(216, 221, 227);
						panel5.Padding = new Padding(10, 8, 10, 8);
						Panel panel6 = panel5;
						AddFooterAccent(panel6);
						Label count = new Label
						{
							Text = "0 selected | 0 in view",
							ForeColor = Color.FromArgb(78, 85, 92),
							TextAlign = ContentAlignment.MiddleLeft,
							AutoSize = false,
							UseMnemonic = false,
							AutoEllipsis = false,
							Font = new Font("Segoe UI", 8.5f)
						};
						GlassButton glassButton = new GlassButton();
						glassButton.Text = "Select All";
						glassButton.Width = 96;
						GlassButton glassButton2 = glassButton;
						GlassButton glassButton3 = new GlassButton();
						glassButton3.Text = "Clear";
						glassButton3.Width = 82;
						GlassButton glassButton4 = glassButton3;
						GlassButton glassButton5 = new GlassButton();
						glassButton5.Text = "Close";
						glassButton5.Width = 92;
						glassButton5.DialogResult = DialogResult.Cancel;
						GlassButton glassButton6 = glassButton5;
						GlassButton glassButton7 = new GlassButton();
						glassButton7.Text = "Reset Isolate";
						glassButton7.Width = 104;
						GlassButton glassButton8 = glassButton7;
						GlassButton isolate = new GlassButton
						{
							Text = "Isolate",
							Width = 92,
							Primary = true,
							Enabled = false
						};
						GlassButton select = new GlassButton
						{
							Text = "Select",
							Width = 92,
							Enabled = false
						};
						Panel panel7 = CreateFooterButtonGroup(rightAligned: false, 390);
						Panel panel8 = CreateFooterButtonGroup(rightAligned: true, 408);
						PlaceFooterButton(panel7, glassButton2, 190, 96);
						PlaceFooterButton(panel7, glassButton4, 294, 82);
						PlaceFooterButton(panel8, glassButton6, 0, 92);
						PlaceFooterButton(panel8, glassButton8, 100, 104);
						PlaceFooterButton(panel8, isolate, 212, 92);
						PlaceFooterButton(panel8, select, 312, 92);
						count.Left = 0;
						count.Top = 3;
						count.Width = 184;
						count.Height = 30;
						panel7.Controls.Add(count);
						if (requestedAction == "select")
						{
							isolate.Visible = false;
						}
						else if (requestedAction == "isolate")
						{
							select.Visible = false;
						}
						Action updateCount = () =>
						{
							List<CategoryEntry> list2 = selectedCategories();
							int num = list2.Sum((CategoryEntry c) => c.InView);
							count.Text = list2.Count + " selected | " + num + " in view";
							select.Enabled = list2.Count > 0;
							isolate.Enabled = list2.Count > 0;
						};
						grid.CellMouseDown += (object sender, DataGridViewCellMouseEventArgs e) =>
						{
							if (e.RowIndex >= 0 && e.ColumnIndex == 0)
							{
								categoryCheckboxSelectionSnapshot = (from DataGridViewRow r in grid.Rows
									where r.Selected && r.Visible
									select r).ToList();
								object value5 = grid.Rows[e.RowIndex].Cells[0].Value;
								categoryCheckPriorValue = value5 != null && value5 != DBNull.Value && Convert.ToBoolean(value5);
							}
						};
						grid.CellClick += (object sender, DataGridViewCellEventArgs e) =>
						{
							if (e.RowIndex >= 0)
							{
								bool flag = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
								if (e.ColumnIndex == 0)
								{
									bool flag2 = !categoryCheckPriorValue;
									List<DataGridViewRow> list2 = categoryCheckboxSelectionSnapshot.Where((DataGridViewRow r) => r.Visible).ToList();
									if (list2.Count == 0)
									{
										list2 = (from DataGridViewRow r in grid.Rows
											where r.Selected && r.Visible
											select r).ToList();
									}
									if (!list2.Contains(grid.Rows[e.RowIndex]))
									{
										list2.Add(grid.Rows[e.RowIndex]);
									}
									if (list2.Count > 1)
									{
										foreach (DataGridViewRow item4 in list2)
										{
											item4.Cells[0].Value = flag2;
										}
									}
									else
									{
										grid.Rows[e.RowIndex].Cells[0].Value = flag2;
									}
								}
								else if (flag && categoryCheckAnchor >= 0)
								{
									grid.ClearSelection();
									int num = Math.Min(categoryCheckAnchor, e.RowIndex);
									int num2 = Math.Max(categoryCheckAnchor, e.RowIndex);
									for (int num3 = num; num3 <= num2; num3++)
									{
										if (grid.Rows[num3].Visible)
										{
											grid.Rows[num3].Selected = true;
										}
									}
								}
								else
								{
									categoryCheckAnchor = e.RowIndex;
								}
								updateCount();
							}
						};
						grid.CellValueChanged += (object param0, DataGridViewCellEventArgs param1) =>
						{
							updateCount();
						};
						glassButton8.Click += (object param0, EventArgs param1) =>
						{
							result.Action = "reset";
							form.DialogResult = DialogResult.OK;
							form.Close();
						};
						glassButton2.Click += (object param0, EventArgs param1) =>
						{
							foreach (DataGridViewRow item5 in (IEnumerable)grid.Rows)
							{
								if (item5.Visible)
								{
									item5.Cells[0].Value = true;
								}
							}
							updateCount();
						};
						glassButton4.Click += (object param0, EventArgs param1) =>
						{
							foreach (DataGridViewRow item6 in (IEnumerable)grid.Rows)
							{
								item6.Cells[0].Value = false;
							}
							updateCount();
						};
						select.Click += (object param0, EventArgs param1) =>
						{
							result.Action = "select";
							result.CategoryKeys = (from c in selectedCategories()
								select c.Key).ToList();
							form.DialogResult = DialogResult.OK;
							form.Close();
						};
						isolate.Click += (object param0, EventArgs param1) =>
						{
							result.Action = "isolate";
							result.CategoryKeys = (from c in selectedCategories()
								select c.Key).ToList();
							form.DialogResult = DialogResult.OK;
							form.Close();
						};
						panel6.Controls.Add(panel7);
						panel6.Controls.Add(panel8);
						form.Controls.Add(grid);
						form.Controls.Add(panel6);
						form.Controls.Add(panel4);
						form.Controls.Add(panel2);
						AddWindowSidebar(form, dashboard: false);
						form.CancelButton = glassButton6;
						BIMlifyForm bIMlifyForm = form;
						EmptyState.AttachCategoryQuickList(bIMlifyForm);
						BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(bIMlifyForm);
					}
					finally
					{
						if (typeFilter != null)
						{
							((IDisposable)typeFilter).Dispose();
						}
					}
				}
				finally
				{
					if (search != null)
					{
						((IDisposable)search).Dispose();
					}
				}
			}
			finally
			{
				if (grid != null)
				{
					((IDisposable)grid).Dispose();
				}
			}
		}
		finally
		{
			if (form != null)
			{
				((IDisposable)form).Dispose();
			}
		}
		return result;
	}

	private static CategoryTargets GetElementsForCategories(Document doc, View view, IList<string> categoryKeys)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Expected Obj, but got Unknown
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Expected Obj, but got Unknown
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		HashSet<string> hashSet = new HashSet<string>(categoryKeys, StringComparer.OrdinalIgnoreCase);
		CategoryTargets categoryTargets = new CategoryTargets();
		foreach (Element item in new FilteredElementCollector(doc, ((Element)view).Id).WhereElementIsNotElementType())
		{
			try
			{
				if (hashSet.Contains(CategoryKey(item.Category)))
				{
					categoryTargets.HostIds.Add(item.Id);
					categoryTargets.References.Add(new Reference(item));
					categoryTargets.IsolationIds.Add(item.Id);
				}
			}
			catch
			{
			}
		}
		foreach (RevitLinkInstance item2 in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)))
		{
			RevitLinkInstance val = item2;
			try
			{
				if (val.GetLinkDocument() == null)
				{
					continue;
				}
				bool flag = false;
				foreach (Element item3 in new FilteredElementCollector(doc, ((Element)view).Id, ((Element)val).Id).WhereElementIsNotElementType())
				{
					try
					{
						if (hashSet.Contains(CategoryKey(item3.Category)))
						{
							categoryTargets.References.Add(new Reference(item3).CreateLinkReference(val));
							flag = true;
						}
					}
					catch
					{
					}
				}
				if (flag)
				{
					categoryTargets.IsolationIds.Add(((Element)val).Id);
				}
			}
			catch
			{
			}
		}
		return categoryTargets;
	}

	private static void SelectElementsById(UIDocument ui)
	{
		string pendingIds = "";
		while (true)
		{
			bool flag = true;
			BIMlifyForm form = new BIMlifyForm();
			try
			{
				form.Text = "BIMlify — Select By ID";
				form.Width = 600;
				form.Height = 430;
				form.MinimumSize = new Size(560, 410);
				form.StartPosition = FormStartPosition.CenterScreen;
				form.MaximizeBox = true;
				form.MinimizeBox = true;
				form.ShowIcon = false;
				form.AutoScaleMode = AutoScaleMode.Dpi;
				form.BackColor = Color.FromArgb(238, 241, 245);
				form.Font = new Font("Segoe UI", 9f);
				UseSmoothCorners(form);
				Panel panel = new Panel();
				panel.Dock = DockStyle.Fill;
				panel.BackColor = Color.FromArgb(238, 241, 245);
				panel.Padding = new Padding(24, 12, 76, 10);
				Panel panel2 = panel;
				panel2.Controls.Add(CreateSmallMark(panel2));
				TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
				tableLayoutPanel.Dock = DockStyle.Fill;
				tableLayoutPanel.ColumnCount = 1;
				tableLayoutPanel.RowCount = 4;
				tableLayoutPanel.BackColor = panel2.BackColor;
				tableLayoutPanel.Padding = Padding.Empty;
				TableLayoutPanel tableLayoutPanel2 = tableLayoutPanel;
				tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
				tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
				tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
				tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
				Label label = new Label();
				label.Dock = DockStyle.Fill;
				label.Text = "SELECT ELEMENTS BY ID";
				label.ForeColor = Color.FromArgb(78, 85, 92);
				label.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
				label.TextAlign = ContentAlignment.MiddleLeft;
				Label control = label;
				Label label2 = new Label();
				label2.Dock = DockStyle.Fill;
				label2.Text = "Type IDs, or pick an element in Revit to add its ID below.";
				label2.ForeColor = Color.FromArgb(102, 109, 118);
				label2.Font = new Font("Segoe UI", 8f);
				label2.TextAlign = ContentAlignment.MiddleLeft;
				Label control2 = label2;
				TextBox idsBox = new TextBox
				{
					Dock = DockStyle.Fill,
					Multiline = true,
					AcceptsReturn = true,
					ScrollBars = ScrollBars.Vertical,
					BorderStyle = BorderStyle.FixedSingle,
					Font = new Font("Segoe UI", 10f),
					BackColor = Color.White,
					ForeColor = Color.FromArgb(55, 62, 70),
					Margin = new Padding(0, 4, 0, 4),
					Text = pendingIds
				};
				Label hint = new Label
				{
					Dock = DockStyle.Fill,
					Text = "Separate multiple IDs with commas, spaces, or line breaks. Example: 12345, 67890",
					ForeColor = Color.FromArgb(116, 120, 125),
					Font = new Font("Segoe UI", 8.1f),
					TextAlign = ContentAlignment.MiddleLeft
				};
				tableLayoutPanel2.Controls.Add(control, 0, 0);
				tableLayoutPanel2.Controls.Add(control2, 0, 1);
				tableLayoutPanel2.Controls.Add(idsBox, 0, 2);
				tableLayoutPanel2.Controls.Add(hint, 0, 3);
				panel2.Controls.Add(tableLayoutPanel2);
				Panel panel3 = CreateDialogFooter();
				GlassButton glassButton = new GlassButton();
				glassButton.Text = "Close";
				glassButton.Width = 92;
				glassButton.DialogResult = DialogResult.Cancel;
				GlassButton glassButton2 = glassButton;
				GlassButton glassButton3 = new GlassButton();
				glassButton3.Text = "SELECT";
				glassButton3.Width = 92;
				glassButton3.Primary = true;
				GlassButton glassButton4 = glassButton3;
				GlassButton glassButton5 = new GlassButton();
				glassButton5.Text = "PICK ELEMENT";
				glassButton5.Width = 116;
				GlassButton glassButton6 = glassButton5;
				Panel panel4 = CreateFooterButtonGroup(rightAligned: false, 132);
				Panel panel5 = CreateFooterButtonGroup(rightAligned: true, 220);
				PlaceFooterButton(panel4, glassButton6, 0, 116);
				PlaceFooterButton(panel5, glassButton2, 0, 96);
				PlaceFooterButton(panel5, glassButton4, 108, 96);
				panel3.Controls.Add(panel4);
				panel3.Controls.Add(panel5);
				form.Controls.Add(panel2);
				form.Controls.Add(panel3);
				AddWindowTitleBar(form);
				form.CancelButton = glassButton2;
				form.AcceptButton = glassButton4;
				glassButton6.Click += (object param0, EventArgs param1) =>
				{
					pendingIds = idsBox.Text;
					form.Tag = "PICK";
					form.Close();
				};
				glassButton4.Click += (object param0, EventArgs param1) =>
				{
					//IL_0099: Unknown result type (might be due to invalid IL or missing references)
					//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
					//IL_00dc: Expected Obj, but got Unknown
					//IL_013c: Unknown result type (might be due to invalid IL or missing references)
					string[] array = idsBox.Text.Split(new char[6] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
					HashSet<int> hashSet = new HashSet<int>();
					int num = 0;
					string[] array2 = array;
					foreach (string s in array2)
					{
						if (int.TryParse(s, out var result) && result > 0)
						{
							hashSet.Add(result);
						}
						else
						{
							num++;
						}
					}
					if (hashSet.Count == 0)
					{
						TaskDialog.Show("Select By ID", "Enter at least one valid positive element ID.");
					}
					else
					{
						List<ElementId> list = new List<ElementId>();
						foreach (int item in hashSet)
						{
							Element element = ui.Document.GetElement(new ElementId(item));
							if (element != null)
							{
								list.Add(element.Id);
							}
							else
							{
								num++;
							}
						}
						if (list.Count == 0)
						{
							TaskDialog.Show("Select By ID", "No elements were found for the entered IDs.");
						}
						else
						{
							ui.Selection.SetElementIds((ICollection<ElementId>)list);
							pendingIds = idsBox.Text;
							hint.Text = list.Count + " element(s) selected." + ((num > 0) ? (" " + num + " value(s) were invalid or not found.") : " You can continue editing or picking IDs.");
						}
					}
				};
				form.Shown += (object param0, EventArgs param1) =>
				{
					idsBox.Focus();
					RefreshVisibleControls(form);
				};
				BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(form);
				if (!string.Equals(form.Tag as string, "PICK", StringComparison.Ordinal))
				{
					break;
				}
				pendingIds = idsBox.Text;
			}
			finally
			{
				if (form != null)
				{
					((IDisposable)form).Dispose();
				}
			}
			try
			{
				Reference val = ui.Selection.PickObject((ObjectType)1, "Pick an element to add its ID, or press Esc to return.");
				if (val != null)
				{
					string text = val.ElementId.IntegerValue.ToString();
					pendingIds = (string.IsNullOrWhiteSpace(pendingIds) ? text : (pendingIds.TrimEnd(' ', ',', ';', '\r', '\n', '\t') + ", " + text));
				}
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private static void CategoryTools(UIDocument ui, string requestedAction = null)
	{
		BIMlify.CategorySelectSupport.Entry.Run(ui, requestedAction);
	}

	private static void ResetCategoryIsolation(UIDocument ui)
	{
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected Obj, but got Unknown
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		View activeGraphicalView = ui.ActiveGraphicalView;
		if (activeGraphicalView == null)
		{
			return;
		}
		try
		{
			Transaction val = new Transaction(ui.Document, "BIMlify — Reset temporary isolate");
			try
			{
				val.Start();
				activeGraphicalView.DisableTemporaryViewMode((TemporaryViewMode)2);
				val.Commit();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch (Exception ex)
		{
			TaskDialog.Show("Reset Isolate", "Could not reset temporary isolation in this view.\n\n" + ex.Message);
		}
	}

	private static void DeleteViews(UIDocument ui)
	{
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Invalid comparison between Unknown and I4
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Expected Obj, but got Unknown
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		Document document = ui.Document;
		HashSet<int> placed = PlacedViewIds(document);
		List<View> objects = (from v in UnplacedViews(document)
			where !placed.Contains(((Element)v).Id.IntegerValue) && v.GetDependentViewIds().Count == 0
			select v).ToList();
		List<View> list = SelectMultiple("Select unplaced views to delete", objects, (View v) =>
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			return $"{v.ViewType} — {((Element)v).Name} — ID {((Element)v).Id.IntegerValue}";
		});
		if (list.Count == 0 || (GetPreference("ConfirmBeforeDelete", defaultValue: true) && (int)TaskDialog.Show("Confirm view deletion", "Delete " + list.Count + " selected unplaced view(s)?", (TaskDialogCommonButtons)6) != 6))
		{
			return;
		}
		Transaction val = new Transaction(document, "BIMlify — Remove selected unplaced views");
		try
		{
			val.Start();
			foreach (View item in list)
			{
				Element element = document.GetElement(((Element)item).Id);
				if (element != null)
				{
					try
					{
						document.Delete(((Element)item).Id);
					}
					catch
					{
					}
				}
			}
			val.Commit();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static List<Level> PickLevels(Document doc)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		return SelectMultiple("Select levels to adjust", from Level x in (IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(Level))
			orderby x.Elevation
			select x, (Level l) => $"{((Element)l).Name} | Elevation {l.Elevation:0.###} | ID {((Element)l).Id.IntegerValue}");
	}

	private static void LevelBubbles(UIDocument ui)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Invalid comparison between Unknown and I4
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Expected Obj, but got Unknown
		//IL_02a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_030c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0227: Unknown result type (might be due to invalid IL or missing references)
		//IL_0229: Unknown result type (might be due to invalid IL or missing references)
		//IL_0232: Unknown result type (might be due to invalid IL or missing references)
		//IL_0236: Unknown result type (might be due to invalid IL or missing references)
		//IL_0242: Unknown result type (might be due to invalid IL or missing references)
		Document document = ui.Document;
		List<View> list = (from v in ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(View))).Cast<View>().Where((View v) =>
			{
				//IL_0009: Unknown result type (might be due to invalid IL or missing references)
				//IL_000f: Invalid comparison between Unknown and I4
				return !v.IsTemplate && (int)v.ViewType == 3;
			})
			orderby ((Element)v).Name
			select v).ToList();
		if (list.Count == 0)
		{
			TaskDialog.Show("BIMlify", "No elevation views were found.");
			return;
		}
		LevelBubbleSelection levelBubbleSelection = SelectLevelBubbleInputs(list, from Level x in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(Level))
			orderby x.Elevation
			select x);
		List<View> views = levelBubbleSelection.Views;
		List<Level> levels = levelBubbleSelection.Levels;
		if (views.Count == 0 || levels.Count == 0)
		{
			return;
		}
		bool flag = (int)TaskDialog.Show("Level Bubble Control", "Show level bubbles on the left end in the selected elevations? Choose No for the right end.", (TaskDialogCommonButtons)6) == 6;
		int num = 0;
		int num2 = 0;
		Transaction val = new Transaction(document, "BIMlify — Level bubble control");
		try
		{
			val.Start();
			foreach (View item in views)
			{
				foreach (Level item2 in levels)
				{
					try
					{
						IList<Curve> list2 = null;
						try
						{
							list2 = ((DatumPlane)item2).GetCurvesInView((DatumExtentType)1, item);
						}
						catch
						{
						}
						if (list2 == null || list2.Count == 0)
						{
							list2 = ((DatumPlane)item2).GetCurvesInView((DatumExtentType)0, item);
						}
						if (list2 == null || list2.Count == 0)
						{
							num2++;
							continue;
						}
						XYZ endPoint = list2[0].GetEndPoint(0);
						XYZ endPoint2 = list2[0].GetEndPoint(1);
						bool flag2 = endPoint.DotProduct(item.RightDirection) <= endPoint2.DotProduct(item.RightDirection);
						DatumEnds val2 = (DatumEnds)((flag != flag2) ? 1 : 0);
						DatumEnds val3 = (DatumEnds)((int)val2 == 0);
						((DatumPlane)item2).ShowBubbleInView(val2, item);
						((DatumPlane)item2).HideBubbleInView(val3, item);
						num++;
					}
					catch
					{
						num2++;
					}
				}
			}
			val.Commit();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		TaskDialog.Show("BIMlify", "Updated " + num + " level/view combinations. Skipped: " + num2 + ".");
	}

	private static bool IsPlanView(View view)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Invalid comparison between Unknown and I4
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Invalid comparison between Unknown and I4
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Invalid comparison between Unknown and I4
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Invalid comparison between Unknown and I4
		return view != null && ((int)view.ViewType == 1 || (int)view.ViewType == 2 || (int)view.ViewType == 115 || (int)view.ViewType == 116);
	}

	private static string TemplateViewGroup(View view)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected I4, but got Unknown
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Expected I4, but got Unknown
		if (view == null)
		{
			return "Other Views";
		}
		ViewType viewType = view.ViewType;
		switch (viewType - 1)
		{
		default:
			switch (viewType - 115)
			{
			case 0:
			case 1:
				break;
			case 2:
				return "Sections";
			default:
				return "Other Views";
			}
			goto case 0;
		case 3:
			return "3D Views";
		case 1:
			return "RCP Views";
		case 0:
			return "Plan Views";
		case 2:
			return "Elevations";
		}
	}

	private static void BimLibrary(UIApplication application)
	{
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_13fb: Unknown result type (might be due to invalid IL or missing references)
		if (application == null || application.ActiveUIDocument == null)
		{
			TaskDialog.Show("BIMlify", "Open a Revit project before using BIM Library.");
			return;
		}
		Document document = application.ActiveUIDocument.Document;
		Color backColor = Color.FromArgb(238, 241, 245);
		Color color = Color.FromArgb(250, 250, 249);
		Color ink = Color.FromArgb(78, 85, 92);
		Color color2 = Color.FromArgb(105, 112, 120);
		Color gold = Color.FromArgb(214, 171, 61);
		string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "BIMlify", "Library");
		try
		{
			if (!Directory.Exists(root))
			{
				Directory.CreateDirectory(root);
			}
		}
		catch (Exception ex)
		{
			TaskDialog.Show("BIMlify", "Could not create the BIMlify Library folder.\n\n" + ex.Message);
			return;
		}
		List<string> selectedFiles = null;
		BIMlifyForm form = new BIMlifyForm();
		try
		{
			form.Text = "BIMlify — BIM Library";
			form.Width = 1060;
			form.Height = 700;
			form.MinimumSize = new Size(940, 620);
			form.StartPosition = FormStartPosition.CenterScreen;
			form.ShowIcon = false;
			form.MaximizeBox = true;
			form.MinimizeBox = true;
			form.AutoScaleMode = AutoScaleMode.Dpi;
			form.BackColor = backColor;
			form.Font = new Font("Segoe UI", 8.5f);
			UseSmoothCorners(form);
			Panel panel = new Panel();
			panel.Dock = DockStyle.Top;
			panel.Height = 128;
			panel.BackColor = Color.FromArgb(247, 247, 245);
			panel.Padding = new Padding(20, 10, 18, 6);
			Panel panel2 = panel;
			panel2.Controls.Add(new Label
			{
				Text = "BIM Library",
				Left = 20,
				Top = 8,
				Width = 420,
				Height = 24,
				ForeColor = ink,
				Font = new Font("Segoe UI", 10f, FontStyle.Bold),
				BackColor = panel2.BackColor
			});
			panel2.Controls.Add(new Label
			{
				Text = "Browse trusted Revit family sources or load families from your local BIMlify library.",
				Left = 20,
				Top = 36,
				Width = 760,
				Height = 22,
				ForeColor = color2,
				Font = new Font("Segoe UI", 8.5f),
				BackColor = panel2.BackColor
			});
			panel2.Controls.Add(CreateBrandLockup(panel2.BackColor));
			AddYellowPanelAccent(panel2, gold, atTop: false);
			Panel panel3 = CreateDialogFooter();
			panel3.Padding = new Padding(12, 12, 36, 8);
			GlassButton glassButton = new GlassButton();
			glassButton.Text = "Close";
			glassButton.Width = 96;
			glassButton.DialogResult = DialogResult.Cancel;
			GlassButton glassButton2 = glassButton;
			Panel panel4 = CreateFooterButtonGroup(rightAligned: true, 112);
			PlaceFooterButton(panel4, glassButton2, 0, 96);
			panel3.Controls.Add(panel4);
			TabControl tabs = new TabControl
			{
				Dock = DockStyle.Fill,
				Padding = new Point(14, 5),
				Font = new Font("Segoe UI", 9f),
				BackColor = backColor,
				DrawMode = TabDrawMode.OwnerDrawFixed,
				ItemSize = new Size(170, 30),
				SizeMode = TabSizeMode.Fixed
			};
			tabs.DrawItem += (object sender, DrawItemEventArgs e) =>
			{
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				bool flag = e.Index == tabs.SelectedIndex;
				using (SolidBrush brush = new SolidBrush(flag ? Color.FromArgb(247, 247, 245) : Color.FromArgb(216, 221, 227)))
				{
					e.Graphics.FillRectangle(brush, e.Bounds);
				}
				using (SolidBrush brush2 = new SolidBrush(ink))
				{
					using Font font = new Font("Segoe UI", 9f, flag ? FontStyle.Bold : FontStyle.Regular);
					using StringFormat format = new StringFormat
					{
						Alignment = StringAlignment.Center,
						LineAlignment = StringAlignment.Center,
						Trimming = StringTrimming.EllipsisCharacter,
						FormatFlags = StringFormatFlags.NoWrap
					};
					e.Graphics.DrawString(tabs.TabPages[e.Index].Text, font, brush2, e.Bounds, format);
				}
				using (Pen pen = new Pen(Color.FromArgb(185, 185, 185), 1f))
				{
					e.Graphics.DrawRectangle(pen, e.Bounds.Left, e.Bounds.Top, e.Bounds.Width - 1, e.Bounds.Height - 1);
				}
				if (flag)
				{
					using (Pen pen2 = new Pen(gold, 3f))
					{
						e.Graphics.DrawLine(pen2, e.Bounds.Left + 2, e.Bounds.Bottom - 2, e.Bounds.Right - 3, e.Bounds.Bottom - 2);
					}
				}
			};
			TabPage externalPage = new TabPage("External Libraries")
			{
				BackColor = backColor,
				Padding = new Padding(12)
			};
			TabPage localPage = new TabPage("BIMlify Library")
			{
				BackColor = backColor,
				Padding = new Padding(12)
			};
			tabs.TabPages.Add(externalPage);
			tabs.TabPages.Add(localPage);
			List<ExternalBimResource> resources = new List<ExternalBimResource>
			{
				new ExternalBimResource
				{
					Name = "Autodesk Revit Content",
					Group = "Official Revit content",
					Availability = "Free",
					Description = "Autodesk family content and Revit libraries. Depending on your Revit release and setup, families may be available through Load Autodesk Family or through Autodesk Account content downloads.",
					Url = "https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Where-to-find-Revit-Content-Libraries-to-download.html"
				},
				new ExternalBimResource
				{
					Name = "BIMobject",
					Group = "Families & furniture",
					Availability = "Free · account required",
					Description = "Search manufacturer BIM objects and Revit families. Account registration may be required before downloading; check each manufacturer's usage terms.",
					Url = "https://www.bimobject.com/en-us/collections/library"
				},
				new ExternalBimResource
				{
					Name = "NBS Source",
					Group = "BIM objects & specifications",
					Availability = "Free library",
					Description = "Browse manufacturer BIM objects and specification information. NBS also provides a Revit plugin; review its current availability and terms on the provider's site.",
					Url = "https://direct.source.thenbs.com/ca/en/bimlibrary"
				},
				new ExternalBimResource
				{
					Name = "BIMLibrary.co",
					Group = "Revit families",
					Availability = "Free browsing",
					Description = "Browse downloadable Revit families. Review the license and permitted use on each family page before using it in a project.",
					Url = "https://bimlibrary.co/"
				},
				new ExternalBimResource
				{
					Name = "Blocks® Families",
					Group = "Revit add-in",
					Availability = "Free tier · paid content",
					Description = "Revit add-in for browsing and placing families. Some content or functions may require payment; confirm current pricing and terms with the publisher.",
					Url = "https://apps.autodesk.com/RVT/oo/Detail/Index?id=6822018272796362427"
				},
				new ExternalBimResource
				{
					Name = "Autodesk App Store",
					Group = "Revit add-ins",
					Availability = "Free, trial & paid",
					Description = "Find Revit add-ins, including family and content tools. Pricing, compatibility, and licensing vary by publisher.",
					Url = "https://apps.autodesk.com/rvt/en/Home/Index"
				}
			};
			ComboBox resourceFilter = new LightGoldComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Left = 12,
				Top = 12,
				Width = 190,
				Height = 27,
				Font = new Font("Segoe UI", 8.5f)
			};
			resourceFilter.Items.AddRange(new object[3] { "All resources", "Libraries", "Revit add-ins" });
			resourceFilter.SelectedIndex = 0;
			TextBox resourceSearch = new AdvancedSearchTextBox
			{
				Left = 214,
				Top = 12,
				Width = 350,
				Height = 27
			};
			StyleSearchBox(resourceSearch, color, ink, "Search libraries and add-ins");
			ListBox resourceList = new ListBox
			{
				Left = 12,
				Top = 50,
				Width = 285,
				Height = 460,
				Anchor = (AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left),
				Font = new Font("Segoe UI", 8.5f),
				BackColor = color,
				ForeColor = ink,
				BorderStyle = BorderStyle.FixedSingle,
				IntegralHeight = false
			};
			Panel detail = new Panel
			{
				Left = 310,
				Top = 50,
				Width = 650,
				Height = 460,
				Anchor = (AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right),
				BackColor = color,
				Padding = new Padding(18)
			};
			Label resourceName = new Label
			{
				Left = 18,
				Top = 18,
				Width = 590,
				Height = 28,
				Font = new Font("Segoe UI", 10f, FontStyle.Bold),
				ForeColor = ink,
				BackColor = color
			};
			Label resourceGroup = new Label
			{
				Left = 18,
				Top = 50,
				Width = 590,
				Height = 22,
				Font = new Font("Segoe UI", 8f, FontStyle.Bold),
				ForeColor = gold,
				BackColor = color
			};
			Label resourceAvailability = new Label
			{
				Left = 18,
				Top = 78,
				Width = 590,
				Height = 22,
				Font = new Font("Segoe UI", 8f),
				ForeColor = color2,
				BackColor = color
			};
			Label resourceDescription = new Label
			{
				Left = 18,
				Top = 112,
				Width = 590,
				Height = 110,
				Font = new Font("Segoe UI", 8.5f),
				ForeColor = ink,
				BackColor = color
			};
			Label resourceUrl = new Label
			{
				Left = 18,
				Top = 232,
				Width = 590,
				Height = 48,
				Font = new Font("Segoe UI", 7.5f),
				ForeColor = color2,
				BackColor = color,
				AutoEllipsis = true
			};
			GlassButton openResource = new GlassButton
			{
				Text = "Open Website",
				Left = 18,
				Top = 300,
				Width = 130,
				Height = 32,
				Primary = true
			};
			detail.Controls.Add(resourceName);
			detail.Controls.Add(resourceGroup);
			detail.Controls.Add(resourceAvailability);
			detail.Controls.Add(resourceDescription);
			detail.Controls.Add(resourceUrl);
			detail.Controls.Add(openResource);
			Action refreshResources = () =>
			{
				string text2 = resourceSearch.Text;
				string text3 = ((resourceFilter.SelectedItem == null) ? "All resources" : resourceFilter.SelectedItem.ToString());
				resourceList.BeginUpdate();
				resourceList.Items.Clear();
				foreach (ExternalBimResource item in resources)
				{
					bool flag = item.Group.IndexOf("add-in", StringComparison.OrdinalIgnoreCase) >= 0;
					if ((!(text3 == "Libraries") || !flag) && (!(text3 == "Revit add-ins") || flag) && SearchTextMatches(text2, item.Name, item.Group, item.Availability, item.Description))
					{
						resourceList.Items.Add(item);
					}
				}
				if (resourceList.Items.Count > 0)
				{
					resourceList.SelectedIndex = 0;
				}
				resourceList.EndUpdate();
			};
			Action showResource = () =>
			{
				ExternalBimResource externalBimResource = resourceList.SelectedItem as ExternalBimResource;
				openResource.Enabled = externalBimResource != null;
				resourceName.Text = ((externalBimResource == null) ? "Select a resource" : externalBimResource.Name);
				resourceGroup.Text = ((externalBimResource == null) ? "" : externalBimResource.Group);
				resourceAvailability.Text = ((externalBimResource == null) ? "" : externalBimResource.Availability);
				resourceDescription.Text = ((externalBimResource == null) ? "" : externalBimResource.Description);
				resourceUrl.Text = ((externalBimResource == null) ? "" : externalBimResource.Url);
			};
			resourceSearch.TextChanged += (object param0, EventArgs param1) =>
			{
				refreshResources();
			};
			resourceFilter.SelectedIndexChanged += (object param0, EventArgs param1) =>
			{
				refreshResources();
			};
			resourceList.SelectedIndexChanged += (object param0, EventArgs param1) =>
			{
				showResource();
			};
			refreshResources();
			showResource();
			openResource.Click += (object param0, EventArgs param1) =>
			{
				//IL_0055: Unknown result type (might be due to invalid IL or missing references)
				if (!(resourceList.SelectedItem is ExternalBimResource externalBimResource))
				{
					return;
				}
				try
				{
					Process.Start(new ProcessStartInfo(externalBimResource.Url)
					{
						UseShellExecute = true
					});
				}
				catch (Exception ex2)
				{
					TaskDialog.Show("BIMlify", "Could not open the website.\n\n" + ex2.Message);
				}
			};
			externalPage.Controls.Add(resourceFilter);
			externalPage.Controls.Add(resourceSearch);
			externalPage.Controls.Add(resourceList);
			externalPage.Controls.Add(detail);
			externalPage.Resize += (object param0, EventArgs param1) =>
			{
				int width = externalPage.ClientSize.Width;
				int height = externalPage.ClientSize.Height;
				resourceList.Height = Math.Max(220, height - 62);
				detail.Left = 310;
				detail.Width = Math.Max(300, width - 334);
				detail.Height = Math.Max(220, height - 62);
			};
			Label label = new Label();
			label.Left = 12;
			label.Top = 10;
			label.Width = 630;
			label.Height = 24;
			label.Text = "Local family folder";
			label.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
			label.ForeColor = ink;
			label.BackColor = backColor;
			Label value = label;
			TextBox folderPath = new TextBox
			{
				Left = 12,
				Top = 40,
				Width = 620,
				Height = 25,
				ReadOnly = true,
				Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right)
			};
			StyleSearchBox(folderPath, color, color2, root);
			folderPath.Text = root;
			GlassButton openFolder = new GlassButton
			{
				Text = "Open Folder",
				Left = 644,
				Top = 38,
				Width = 110,
				Height = 30,
				Anchor = (AnchorStyles.Top | AnchorStyles.Right)
			};
			GlassButton refreshFolder = new GlassButton
			{
				Text = "Refresh",
				Left = 762,
				Top = 38,
				Width = 96,
				Height = 30,
				Anchor = (AnchorStyles.Top | AnchorStyles.Right)
			};
			TextBox familySearch = new AdvancedSearchTextBox
			{
				Left = 12,
				Top = 78,
				Width = 846,
				Height = 27,
				Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right)
			};
			StyleSearchBox(familySearch, color, ink, "Search local families");
			CheckedListBox familyList = new CheckedListBox
			{
				Left = 12,
				Top = 112,
				Width = 846,
				Height = 365,
				Anchor = (AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right),
				Font = new Font("Segoe UI", 8.5f),
				BackColor = color,
				ForeColor = ink,
				BorderStyle = BorderStyle.FixedSingle,
				CheckOnClick = true,
				IntegralHeight = false
			};
			Label libraryStatus = new Label
			{
				Left = 12,
				Top = 486,
				Width = 600,
				Height = 24,
				Anchor = (AnchorStyles.Bottom | AnchorStyles.Left),
				ForeColor = color2,
				BackColor = backColor
			};
			GlassButton loadSelected = new GlassButton
			{
				Text = "Load Selected",
				Left = 742,
				Top = 480,
				Width = 116,
				Height = 32,
				Anchor = (AnchorStyles.Bottom | AnchorStyles.Right),
				Primary = true
			};
			List<LocalFamilyFile> familyFiles = new List<LocalFamilyFile>();
			Action refreshFamilies = () =>
			{
				string query = familySearch.Text;
				familyList.BeginUpdate();
				familyList.Items.Clear();
				foreach (LocalFamilyFile item2 in familyFiles.Where((LocalFamilyFile x) => SearchTextMatches(query, x.Name, x.Category, x.Path)))
				{
					familyList.Items.Add(item2, isChecked: false);
				}
				familyList.EndUpdate();
				libraryStatus.Text = familyFiles.Count + " family file(s) found · Check the families you want to load.";
			};
			Action scanFamilies = () =>
			{
				familyFiles.Clear();
				try
				{
					string[] files = Directory.GetFiles(root, "*.rfa", SearchOption.AllDirectories);
					foreach (string path in files)
					{
						string category = string.Empty;
						try
						{
							category = Path.GetFileName(Path.GetDirectoryName(path));
						}
						catch
						{
						}
						familyFiles.Add(new LocalFamilyFile
						{
							Path = path,
							Category = category,
							Name = Path.GetFileNameWithoutExtension(path)
						});
					}
					familyFiles = familyFiles.OrderBy((LocalFamilyFile x) => x.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy((LocalFamilyFile x) => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
					libraryStatus.Text = familyFiles.Count + " family file(s) found · Check the families you want to load.";
				}
				catch (Exception ex2)
				{
					libraryStatus.Text = "Could not read the library folder: " + ex2.Message;
				}
				refreshFamilies();
			};
			familySearch.TextChanged += (object param0, EventArgs param1) =>
			{
				refreshFamilies();
			};
			refreshFolder.Click += (object param0, EventArgs param1) =>
			{
				scanFamilies();
			};
			openFolder.Click += (object param0, EventArgs param1) =>
			{
				//IL_0037: Unknown result type (might be due to invalid IL or missing references)
				try
				{
					Process.Start(new ProcessStartInfo(root)
					{
						UseShellExecute = true
					});
				}
				catch (Exception ex2)
				{
					TaskDialog.Show("BIMlify", "Could not open the library folder.\n\n" + ex2.Message);
				}
			};
			loadSelected.Click += (object param0, EventArgs param1) =>
			{
				selectedFiles = (from LocalFamilyFile x in familyList.CheckedItems
					select x.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				if (selectedFiles.Count == 0)
				{
					libraryStatus.Text = "Check at least one family before loading.";
				}
				else
				{
					form.DialogResult = DialogResult.OK;
					form.Close();
				}
			};
			localPage.Controls.Add(value);
			localPage.Controls.Add(folderPath);
			localPage.Controls.Add(openFolder);
			localPage.Controls.Add(refreshFolder);
			localPage.Controls.Add(familySearch);
			localPage.Controls.Add(familyList);
			localPage.Controls.Add(libraryStatus);
			localPage.Controls.Add(loadSelected);
			localPage.Resize += (object param0, EventArgs param1) =>
			{
				int width = localPage.ClientSize.Width;
				int height = localPage.ClientSize.Height;
				folderPath.Width = Math.Max(260, width - 258);
				openFolder.Left = width - 246;
				refreshFolder.Left = width - 126;
				familySearch.Width = Math.Max(260, width - 24);
				familyList.Width = Math.Max(260, width - 24);
				familyList.Height = Math.Max(180, height - 170);
				libraryStatus.Top = height - 39;
				loadSelected.Top = height - 45;
				loadSelected.Left = width - 138;
			};
			scanFamilies();
			form.Controls.Add(tabs);
			form.Controls.Add(panel3);
			form.Controls.Add(panel2);
			AddWindowSidebar(form, dashboard: false);
			form.CancelButton = glassButton2;
			IWin32Window owner = ((application.MainWindowHandle == IntPtr.Zero) ? null : new NativeWindowOwner(application.MainWindowHandle));
			if (form.ShowDialog(owner) != DialogResult.OK)
			{
				selectedFiles = null;
			}
		}
		finally
		{
			if (form != null)
			{
				((IDisposable)form).Dispose();
			}
		}
		if (selectedFiles == null || selectedFiles.Count == 0)
		{
			return;
		}
		int num = 0;
		List<string> list = new List<string>();
		Family val = default;
		foreach (string item3 in selectedFiles)
		{
			try
			{
				if (document.LoadFamily(item3, (IFamilyLoadOptions)(object)new TagFamilyLoadOptions(), ref val) && val != null)
				{
					num++;
				}
				else
				{
					list.Add(Path.GetFileName(item3) + " — Revit did not load this family.");
				}
			}
			catch (Exception ex)
			{
				list.Add(Path.GetFileName(item3) + " — " + ex.Message);
			}
		}
		string text = num + " family/families loaded successfully.";
		if (list.Count > 0)
		{
			object obj = text;
			text = string.Concat(obj, "\n\nCould not load ", list.Count, ":\n", string.Join("\n", list.Take(12).ToArray()), (list.Count > 12) ? "\n…" : "");
		}
		TaskDialog.Show("BIMlify — BIM Library", text);
	}

	private static bool IsTagFamily(Family family)
	{
		if (family == null || family.FamilyCategory == null || family.IsInPlace)
		{
			return false;
		}
		Category familyCategory = family.FamilyCategory;
		string text = Enum.GetName(typeof(BuiltInCategory), (int)familyCategory.Id.Value) ?? string.Empty;
		return text.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0 || familyCategory.Name.IndexOf("tag", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static string FamilyCategoryBuiltInName(Family family)
	{
		if (family == null || family.FamilyCategory == null)
		{
			return string.Empty;
		}
		return Enum.GetName(typeof(BuiltInCategory), (int)family.FamilyCategory.Id.Value) ?? string.Empty;
	}

	private static bool IsDetailComponentFamily(Family family)
	{
		if (family == null || family.FamilyCategory == null || family.IsInPlace || IsTagFamily(family))
		{
			return false;
		}
		string text = FamilyCategoryBuiltInName(family);
		string text2 = family.FamilyCategory.Name ?? string.Empty;
		return text.IndexOf("DetailComponent", StringComparison.OrdinalIgnoreCase) >= 0 || text2.IndexOf("detail component", StringComparison.OrdinalIgnoreCase) >= 0 || text2.IndexOf("detail item", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsAnnotationSymbolFamily(Family family)
	{
		if (family == null || family.FamilyCategory == null || family.IsInPlace || IsTagFamily(family) || IsDetailComponentFamily(family))
		{
			return false;
		}
		return string.Equals(FamilyCategoryBuiltInName(family), "OST_GenericAnnotation", StringComparison.OrdinalIgnoreCase);
	}

	private static string SafeFamilyFileName(string value)
	{
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			value = value.Replace(oldChar, '_');
		}
		return value.Trim().TrimEnd('.');
	}

	private static void ScaleTagFamilies(UIDocument ui)
	{
		UiPolish.ShowPlanViewRange(ui);
	}

	private static void ClashDetection(UIDocument ui)
	{
		_003C_003Ec__DisplayClass365 obj = new _003C_003Ec__DisplayClass365();
		obj.ui = ui;
		obj.doc = obj.ui.Document;
		BIMlifyForm bIMlifyForm = new BIMlifyForm();
		try
		{
			_003C_003Ec__DisplayClass367 obj2 = new _003C_003Ec__DisplayClass367();
			obj2.results = new ListBox();
			try
			{
				_003C_003Ec__DisplayClass369 obj3 = new _003C_003Ec__DisplayClass369();
				obj3.discipline = new TabControl();
				try
				{
					_003C_003Ec__DisplayClass36b obj4 = new _003C_003Ec__DisplayClass36b();
					obj4.scope = new LightGoldComboBox();
					try
					{
						_003C_003Ec__DisplayClass36d obj5 = new _003C_003Ec__DisplayClass36d();
						obj5.disciplineA = new LightGoldComboBox();
						try
						{
							_003C_003Ec__DisplayClass36f obj6 = new _003C_003Ec__DisplayClass36f();
							obj6.disciplineB = new LightGoldComboBox();
							try
							{
								_003C_003Ec__DisplayClass371 obj7 = new _003C_003Ec__DisplayClass371();
								obj7.disciplinePair = new Panel();
								try
								{
									_003C_003Ec__DisplayClass374 CS_0024_003C_003E8__locals194 = new _003C_003Ec__DisplayClass374();
									CS_0024_003C_003E8__locals194.CS_0024_003C_003E8__locals372 = obj7;
									CS_0024_003C_003E8__locals194.CS_0024_003C_003E8__locals370 = obj6;
									CS_0024_003C_003E8__locals194.CS_0024_003C_003E8__locals36e = obj5;
									CS_0024_003C_003E8__locals194.CS_0024_003C_003E8__locals36c = obj4;
									CS_0024_003C_003E8__locals194.CS_0024_003C_003E8__locals36a = obj3;
									CS_0024_003C_003E8__locals194.CS_0024_003C_003E8__locals368 = obj2;
									CS_0024_003C_003E8__locals194.CS_0024_003C_003E8__locals366 = obj;
									CS_0024_003C_003E8__locals194.rulesBox = new Panel();
									try
									{
										_003C_003Ec__DisplayClass378 CS_0024_003C_003E8__locals192 = new _003C_003Ec__DisplayClass378();
										CS_0024_003C_003E8__locals192.CS_0024_003C_003E8__locals375 = CS_0024_003C_003E8__locals194;
										CS_0024_003C_003E8__locals192.CS_0024_003C_003E8__locals372 = obj7;
										CS_0024_003C_003E8__locals192.CS_0024_003C_003E8__locals370 = obj6;
										CS_0024_003C_003E8__locals192.CS_0024_003C_003E8__locals36e = obj5;
										CS_0024_003C_003E8__locals192.CS_0024_003C_003E8__locals36c = obj4;
										CS_0024_003C_003E8__locals192.CS_0024_003C_003E8__locals36a = obj3;
										CS_0024_003C_003E8__locals192.CS_0024_003C_003E8__locals368 = obj2;
										CS_0024_003C_003E8__locals192.CS_0024_003C_003E8__locals366 = obj;
										CS_0024_003C_003E8__locals192.scanOptions = new Panel();
										try
										{
											_003C_003Ec__DisplayClass37c CS_0024_003C_003E8__locals182 = new _003C_003Ec__DisplayClass37c();
											CS_0024_003C_003E8__locals182.CS_0024_003C_003E8__locals379 = CS_0024_003C_003E8__locals192;
											CS_0024_003C_003E8__locals182.CS_0024_003C_003E8__locals375 = CS_0024_003C_003E8__locals194;
											CS_0024_003C_003E8__locals182.CS_0024_003C_003E8__locals372 = obj7;
											CS_0024_003C_003E8__locals182.CS_0024_003C_003E8__locals370 = obj6;
											CS_0024_003C_003E8__locals182.CS_0024_003C_003E8__locals36e = obj5;
											CS_0024_003C_003E8__locals182.CS_0024_003C_003E8__locals36c = obj4;
											CS_0024_003C_003E8__locals182.CS_0024_003C_003E8__locals36a = obj3;
											CS_0024_003C_003E8__locals182.CS_0024_003C_003E8__locals368 = obj2;
											CS_0024_003C_003E8__locals182.CS_0024_003C_003E8__locals366 = obj;
											CS_0024_003C_003E8__locals182.clashSplit = new SplitContainer();
											try
											{
												Panel panel = new Panel();
												try
												{
													Label label = new Label();
													try
													{
														_003C_003Ec__DisplayClass37f CS_0024_003C_003E8__locals184 = new _003C_003Ec__DisplayClass37f();
														CS_0024_003C_003E8__locals184.CS_0024_003C_003E8__locals37d = CS_0024_003C_003E8__locals182;
														CS_0024_003C_003E8__locals184.CS_0024_003C_003E8__locals379 = CS_0024_003C_003E8__locals192;
														CS_0024_003C_003E8__locals184.CS_0024_003C_003E8__locals375 = CS_0024_003C_003E8__locals194;
														CS_0024_003C_003E8__locals184.CS_0024_003C_003E8__locals372 = obj7;
														CS_0024_003C_003E8__locals184.CS_0024_003C_003E8__locals370 = obj6;
														CS_0024_003C_003E8__locals184.CS_0024_003C_003E8__locals36e = obj5;
														CS_0024_003C_003E8__locals184.CS_0024_003C_003E8__locals36c = obj4;
														CS_0024_003C_003E8__locals184.CS_0024_003C_003E8__locals36a = obj3;
														CS_0024_003C_003E8__locals184.CS_0024_003C_003E8__locals368 = obj2;
														CS_0024_003C_003E8__locals184.CS_0024_003C_003E8__locals366 = obj;
														CS_0024_003C_003E8__locals184.previewPanel = new Panel();
														try
														{
															_003C_003Ec__DisplayClass381 obj8 = new _003C_003Ec__DisplayClass381();
															obj8.previewTitle = new Label();
															try
															{
																_003C_003Ec__DisplayClass383 obj9 = new _003C_003Ec__DisplayClass383();
																obj9.previewText = new Label();
																try
																{
																	_003C_003Ec__DisplayClass385 obj10 = new _003C_003Ec__DisplayClass385();
																	obj10.copyA = new GlassButton();
																	try
																	{
																		_003C_003Ec__DisplayClass387 obj11 = new _003C_003Ec__DisplayClass387();
																		obj11.copyB = new GlassButton();
																		try
																		{
																			_003C_003Ec__DisplayClass389 obj12 = new _003C_003Ec__DisplayClass389();
																			obj12.findIds = new GlassButton();
																			try
																			{
																				_003C_003Ec__DisplayClass38b obj13 = new _003C_003Ec__DisplayClass38b();
																				obj13.detail = new Label();
																				try
																				{
																					GlassButton glassButton = new GlassButton();
																					try
																					{
																						_003C_003Ec__DisplayClass38d obj14 = new _003C_003Ec__DisplayClass38d();
																						obj14.review = new GlassButton();
																						try
																						{
																							GlassButton glassButton2 = new GlassButton();
																							try
																							{
																								GlassButton glassButton3 = new GlassButton();
																								try
																								{
																									_003C_003Ec__DisplayClass38f obj15 = new _003C_003Ec__DisplayClass38f();
																									obj15.status = new Label();
																									try
																									{
																										_003C_003Ec__DisplayClass391 CS_0024_003C_003E8__locals349 = new _003C_003Ec__DisplayClass391();
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390 = obj15;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38e = obj14;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38c = obj13;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38a = obj12;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals388 = obj11;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals386 = obj10;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals384 = obj9;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals382 = obj8;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals380 = CS_0024_003C_003E8__locals184;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals37d = CS_0024_003C_003E8__locals182;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals379 = CS_0024_003C_003E8__locals192;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals375 = CS_0024_003C_003E8__locals194;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals372 = obj7;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals370 = obj6;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36e = obj5;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36c = obj4;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36a = obj3;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368 = obj2;
																										CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals366 = obj;
																										CS_0024_003C_003E8__locals349.bg = Color.FromArgb(238, 241, 245);
																										CS_0024_003C_003E8__locals349.ink = Color.FromArgb(78, 85, 92);
																										Color color = Color.FromArgb(214, 171, 61);
																										bIMlifyForm.Text = "BIMlify — Clash Detection";
																										bIMlifyForm.Width = 1080;
																										bIMlifyForm.Height = 720;
																										bIMlifyForm.MinimumSize = new Size(980, 640);
																										bIMlifyForm.StartPosition = FormStartPosition.CenterScreen;
																										bIMlifyForm.ShowIcon = false;
																										bIMlifyForm.BackColor = CS_0024_003C_003E8__locals349.bg;
																										bIMlifyForm.Font = new Font("Segoe UI", 9f);
																										UseSmoothCorners(bIMlifyForm);
																										Panel panel2 = new Panel();
																										panel2.Dock = DockStyle.Top;
																										panel2.Height = 128;
																										panel2.BackColor = Color.FromArgb(247, 247, 245);
																										panel2.Padding = new Padding(18, 10, 18, 6);
																										Panel panel3 = panel2;
																										Label label2 = new Label();
																										label2.Text = "Clash Detection";
																										label2.Left = 18;
																										label2.Top = 10;
																										label2.Width = 420;
																										label2.Height = 25;
																										label2.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
																										label2.ForeColor = CS_0024_003C_003E8__locals349.ink;
																										Label value = label2;
																										Label label3 = new Label();
																										label3.Text = "Find potential overlaps between visible model elements before coordination review.";
																										label3.Left = 18;
																										label3.Top = 38;
																										label3.Width = 650;
																										label3.Height = 24;
																										label3.ForeColor = Color.FromArgb(102, 109, 118);
																										label3.Font = new Font("Segoe UI", 8.5f);
																										Label value2 = label3;
																										panel3.Controls.Add(value);
																										panel3.Controls.Add(value2);
																										panel3.Controls.Add(CreateBrandLockup(panel3.BackColor));
																										AddYellowPanelAccent(panel3, color, atTop: false);
																										Panel panel4 = new Panel();
																										panel4.Dock = DockStyle.Fill;
																										panel4.BackColor = CS_0024_003C_003E8__locals349.bg;
																										panel4.Padding = new Padding(20, 14, 20, 10);
																										Panel panel5 = panel4;
																										Label label4 = new Label();
																										label4.Text = "Set clash rules, choose the scan scope, then run the test";
																										label4.Dock = DockStyle.Top;
																										label4.Height = 28;
																										label4.ForeColor = CS_0024_003C_003E8__locals349.ink;
																										label4.BackColor = Color.White;
																										label4.Padding = new Padding(4, 2, 0, 0);
																										label4.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
																										Label value3 = label4;
																										obj3.discipline.Dock = DockStyle.Top;
																										obj3.discipline.Height = 36;
																										obj3.discipline.Font = new Font("Segoe UI", 8.5f);
																										obj3.discipline.SizeMode = TabSizeMode.Fixed;
																										obj3.discipline.ItemSize = new Size(106, 32);
																										obj3.discipline.DrawMode = TabDrawMode.OwnerDrawFixed;
																										obj3.discipline.BackColor = CS_0024_003C_003E8__locals349.bg;
																										obj3.discipline.TabPages.Add(new TabPage("Rules"));
																										obj3.discipline.TabPages.Add(new TabPage("Select"));
																										obj3.discipline.TabPages.Add(new TabPage("Results"));
																										obj3.discipline.TabPages.Add(new TabPage("Report"));
																										obj3.discipline.DrawItem += (object sender, DrawItemEventArgs e) =>
																										{
																											bool flag = e.Index == CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36a.discipline.SelectedIndex;
																											using (SolidBrush brush = new SolidBrush(flag ? Color.White : CS_0024_003C_003E8__locals349.bg))
																											{
																												e.Graphics.FillRectangle(brush, e.Bounds);
																											}
																											using (SolidBrush brush2 = new SolidBrush(CS_0024_003C_003E8__locals349.ink))
																											{
																												using StringFormat format = new StringFormat
																												{
																													Alignment = StringAlignment.Center,
																													LineAlignment = StringAlignment.Center,
																													FormatFlags = StringFormatFlags.NoWrap
																												};
																												e.Graphics.DrawString(CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36a.discipline.TabPages[e.Index].Text, CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36a.discipline.Font, brush2, e.Bounds, format);
																											}
																											if (flag)
																											{
																												using (Pen pen = new Pen(Color.FromArgb(214, 171, 61), 2f))
																												{
																													e.Graphics.DrawLine(pen, e.Bounds.Left + 3, e.Bounds.Bottom - 2, e.Bounds.Right - 4, e.Bounds.Bottom - 2);
																												}
																											}
																										};
																										obj4.scope.Dock = DockStyle.Top;
																										obj4.scope.Height = 30;
																										obj4.scope.DropDownStyle = ComboBoxStyle.DropDownList;
																										obj4.scope.FlatStyle = FlatStyle.Flat;
																										obj4.scope.BackColor = Color.FromArgb(228, 232, 236);
																										obj4.scope.ForeColor = CS_0024_003C_003E8__locals349.ink;
																										obj4.scope.Items.AddRange(new object[2] { "Scan Active View", "Scan Entire Model" });
																										obj4.scope.SelectedIndex = 0;
																										DrawComboText(obj4.scope, CS_0024_003C_003E8__locals349.ink, color);
																										obj7.disciplinePair.Dock = DockStyle.Top;
																										obj7.disciplinePair.Height = 108;
																										obj7.disciplinePair.BackColor = Color.White;
																										obj7.disciplinePair.Padding = new Padding(0, 5, 0, 5);
																										CS_0024_003C_003E8__locals349.firstCard = new Panel
																										{
																											BackColor = Color.White,
																											Height = 92
																										};
																										CS_0024_003C_003E8__locals349.secondCard = new Panel
																										{
																											BackColor = Color.White,
																											Height = 92
																										};
																										Action<Panel> action = (Panel card) =>
																										{
																											Action round = () =>
																											{
																												if (card.Width > 2 && card.Height > 2)
																												{
																													using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, card.Width, card.Height), 10))
																													{
																														Region region = card.Region;
																														card.Region = new Region(path);
																														region?.Dispose();
																													}
																												}
																											};
																											card.Resize += (object param0, EventArgs param1) =>
																											{
																												round();
																											};
																											card.HandleCreated += (object param0, EventArgs param1) =>
																											{
																												round();
																											};
																											card.Paint += (object sender, PaintEventArgs e) =>
																											{
																												e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
																												using GraphicsPath path = RoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 9);
																												using Pen pen = new Pen(Color.FromArgb(210, 216, 222), 1f);
																												e.Graphics.DrawPath(pen, path);
																											};
																										};
																										action(CS_0024_003C_003E8__locals349.firstCard);
																										action(CS_0024_003C_003E8__locals349.secondCard);
																										GlassButton glassButton4 = new GlassButton();
																										glassButton4.Text = "DISCIPLINE A";
																										glassButton4.Left = 12;
																										glassButton4.Top = 7;
																										glassButton4.Width = 200;
																										glassButton4.Height = 26;
																										glassButton4.MinimumSize = new Size(120, 26);
																										glassButton4.Primary = false;
																										glassButton4.TabStop = false;
																										GlassButton value4 = glassButton4;
																										GlassButton glassButton5 = new GlassButton();
																										glassButton5.Text = "DISCIPLINE B";
																										glassButton5.Left = 12;
																										glassButton5.Top = 7;
																										glassButton5.Width = 210;
																										glassButton5.Height = 26;
																										glassButton5.MinimumSize = new Size(120, 26);
																										glassButton5.Primary = false;
																										glassButton5.TabStop = false;
																										GlassButton value5 = glassButton5;
																										obj5.disciplineA.Width = 190;
																										obj6.disciplineB.Width = 190;
																										obj5.disciplineA.DropDownStyle = ComboBoxStyle.DropDownList;
																										obj6.disciplineB.DropDownStyle = ComboBoxStyle.DropDownList;
																										obj5.disciplineA.DropDownHeight = 220;
																										obj6.disciplineB.DropDownHeight = 220;
																										obj5.disciplineA.IntegralHeight = true;
																										obj6.disciplineB.IntegralHeight = true;
																										obj5.disciplineA.FlatStyle = FlatStyle.Flat;
																										obj6.disciplineB.FlatStyle = FlatStyle.Flat;
																										obj5.disciplineA.BackColor = Color.FromArgb(228, 232, 236);
																										obj6.disciplineB.BackColor = Color.FromArgb(228, 232, 236);
																										obj5.disciplineA.ForeColor = CS_0024_003C_003E8__locals349.ink;
																										obj6.disciplineB.ForeColor = CS_0024_003C_003E8__locals349.ink;
																										obj5.disciplineA.Height = 34;
																										obj6.disciplineB.Height = 34;
																										object[] items = new object[6] { "All disciplines", "Architecture", "Structure", "Mechanical / HVAC", "Electrical / Plumbing", "Other" };
																										obj5.disciplineA.Items.AddRange(items);
																										obj6.disciplineB.Items.AddRange(items);
																										obj5.disciplineA.SelectedIndex = 0;
																										obj6.disciplineB.SelectedIndex = 0;
																										obj5.disciplineA.Left = 16;
																										obj5.disciplineA.Top = 38;
																										obj6.disciplineB.Left = 16;
																										obj6.disciplineB.Top = 38;
																										obj5.disciplineA.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
																										obj6.disciplineB.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
																										CS_0024_003C_003E8__locals349.intersectLabel = new Label
																										{
																											Text = "All disciplines  ×  All disciplines",
																											Width = 240,
																											Height = 34,
																											TextAlign = ContentAlignment.MiddleCenter,
																											ForeColor = color,
																											Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
																											BackColor = Color.White
																										};
																										CS_0024_003C_003E8__locals349.firstCard.Controls.Add(obj5.disciplineA);
																										CS_0024_003C_003E8__locals349.firstCard.Controls.Add(value4);
																										CS_0024_003C_003E8__locals349.secondCard.Controls.Add(obj6.disciplineB);
																										CS_0024_003C_003E8__locals349.secondCard.Controls.Add(value5);
																										obj7.disciplinePair.Controls.Add(CS_0024_003C_003E8__locals349.secondCard);
																										obj7.disciplinePair.Controls.Add(CS_0024_003C_003E8__locals349.firstCard);
																										obj7.disciplinePair.Controls.Add(CS_0024_003C_003E8__locals349.intersectLabel);
																										CS_0024_003C_003E8__locals349.layoutPair = () =>
																										{
																											int num = 260;
																											int num2 = Math.Max(250, (CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals372.disciplinePair.ClientSize.Width - num) / 2);
																											CS_0024_003C_003E8__locals349.firstCard.SetBounds(0, 5, num2, 92);
																											CS_0024_003C_003E8__locals349.secondCard.SetBounds(num2 + num, 5, num2, 92);
																											CS_0024_003C_003E8__locals349.intersectLabel.SetBounds(num2 + (num - 240) / 2, 37, 240, 34);
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36e.disciplineA.Width = Math.Max(190, num2 - 32);
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals370.disciplineB.Width = Math.Max(190, num2 - 32);
																										};
																										obj7.disciplinePair.Resize += (object param0, EventArgs param1) =>
																										{
																											CS_0024_003C_003E8__locals349.layoutPair();
																										};
																										CS_0024_003C_003E8__locals349.layoutPair();
																										obj2.results.Dock = DockStyle.Fill;
																										obj2.results.BorderStyle = BorderStyle.None;
																										obj2.results.BackColor = Color.White;
																										obj2.results.ForeColor = CS_0024_003C_003E8__locals349.ink;
																										obj2.results.Font = new Font("Segoe UI", 9f);
																										obj2.results.IntegralHeight = false;
																										obj13.detail.Dock = DockStyle.Bottom;
																										obj13.detail.Height = 54;
																										obj13.detail.ForeColor = Color.FromArgb(102, 109, 118);
																										obj13.detail.Padding = new Padding(2, 4, 2, 2);
																										obj13.detail.Text = "Select a clash to see the reason and suggested coordination action.";
																										obj13.detail.AutoEllipsis = false;
																										obj15.status.Dock = DockStyle.Bottom;
																										obj15.status.Height = 34;
																										obj15.status.ForeColor = Color.FromArgb(102, 109, 118);
																										obj15.status.Padding = new Padding(20, 0, 0, 0);
																										obj15.status.Text = "Ready to scan the active view.";
																										obj15.status.TextAlign = ContentAlignment.MiddleLeft;
																										CS_0024_003C_003E8__locals182.clashSplit.Dock = DockStyle.Fill;
																										CS_0024_003C_003E8__locals182.clashSplit.Orientation = Orientation.Vertical;
																										CS_0024_003C_003E8__locals182.clashSplit.SplitterWidth = 10;
																										CS_0024_003C_003E8__locals182.clashSplit.BackColor = CS_0024_003C_003E8__locals349.bg;
																										CS_0024_003C_003E8__locals182.clashSplit.FixedPanel = FixedPanel.None;
																										CS_0024_003C_003E8__locals182.clashSplit.Panel1MinSize = 0;
																										CS_0024_003C_003E8__locals182.clashSplit.Panel2MinSize = 0;
																										CS_0024_003C_003E8__locals349.ensureClashSplit = () =>
																										{
																											int width = CS_0024_003C_003E8__locals182.clashSplit.ClientSize.Width;
																											int num = 0 + CS_0024_003C_003E8__locals182.clashSplit.SplitterWidth;
																											if (width < num)
																											{
																												return;
																											}
																											int splitterDistance = Math.Max(0, Math.Min(width - 0 - CS_0024_003C_003E8__locals182.clashSplit.SplitterWidth, 360));
																											try
																											{
																												CS_0024_003C_003E8__locals182.clashSplit.Panel1MinSize = 0;
																												CS_0024_003C_003E8__locals182.clashSplit.Panel2MinSize = 0;
																												CS_0024_003C_003E8__locals182.clashSplit.SplitterDistance = splitterDistance;
																												CS_0024_003C_003E8__locals182.clashSplit.Panel1MinSize = 0;
																												CS_0024_003C_003E8__locals182.clashSplit.Panel2MinSize = 0;
																											}
																											catch (ArgumentException)
																											{
																											}
																										};
																										CS_0024_003C_003E8__locals182.clashSplit.SizeChanged += (object param0, EventArgs param1) =>
																										{
																											CS_0024_003C_003E8__locals349.ensureClashSplit();
																										};
																										panel.Dock = DockStyle.Fill;
																										panel.BackColor = Color.White;
																										label.Dock = DockStyle.Top;
																										label.Height = 30;
																										label.Padding = new Padding(12, 7, 4, 0);
																										label.Text = "Detected clashes";
																										label.ForeColor = CS_0024_003C_003E8__locals349.ink;
																										label.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
																										obj2.results.Dock = DockStyle.Fill;
																										panel.Controls.Add(obj2.results);
																										panel.Controls.Add(label);
																										CS_0024_003C_003E8__locals182.clashSplit.Panel1.BackColor = Color.White;
																										CS_0024_003C_003E8__locals182.clashSplit.Panel1.Padding = new Padding(1);
																										CS_0024_003C_003E8__locals182.clashSplit.Panel1.Controls.Add(panel);
																										CS_0024_003C_003E8__locals182.clashSplit.Panel1.Paint += (object sender, PaintEventArgs e) =>
																										{
																											using Pen pen = new Pen(Color.FromArgb(210, 216, 222), 1f);
																											e.Graphics.DrawRectangle(pen, 0, 0, CS_0024_003C_003E8__locals182.clashSplit.Panel1.Width - 1, CS_0024_003C_003E8__locals182.clashSplit.Panel1.Height - 1);
																										};
																										CS_0024_003C_003E8__locals184.previewPanel.Dock = DockStyle.Fill;
																										CS_0024_003C_003E8__locals184.previewPanel.BackColor = Color.White;
																										CS_0024_003C_003E8__locals184.previewPanel.Padding = new Padding(16);
																										CS_0024_003C_003E8__locals184.previewPanel.Paint += (object sender, PaintEventArgs e) =>
																										{
																											using Pen pen = new Pen(Color.FromArgb(210, 216, 222), 1f);
																											e.Graphics.DrawRectangle(pen, 0, 0, CS_0024_003C_003E8__locals184.previewPanel.Width - 1, CS_0024_003C_003E8__locals184.previewPanel.Height - 1);
																										};
																										obj8.previewTitle.Dock = DockStyle.Top;
																										obj8.previewTitle.Height = 30;
																										obj8.previewTitle.Text = "Clash details";
																										obj8.previewTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
																										obj8.previewTitle.ForeColor = CS_0024_003C_003E8__locals349.ink;
																										obj9.previewText.Dock = DockStyle.Fill;
																										obj9.previewText.Text = "Select a clash from the left panel to inspect both elements.";
																										obj9.previewText.ForeColor = Color.FromArgb(102, 109, 118);
																										obj9.previewText.AutoEllipsis = false;
																										FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
																										flowLayoutPanel.Dock = DockStyle.Bottom;
																										flowLayoutPanel.Height = 36;
																										flowLayoutPanel.FlowDirection = FlowDirection.LeftToRight;
																										flowLayoutPanel.WrapContents = false;
																										flowLayoutPanel.Padding = new Padding(0, 2, 0, 2);
																										flowLayoutPanel.BackColor = Color.White;
																										FlowLayoutPanel flowLayoutPanel2 = flowLayoutPanel;
																										obj10.copyA.Text = "Copy A ID";
																										obj10.copyA.Width = 84;
																										obj10.copyA.Height = 28;
																										obj10.copyA.Enabled = false;
																										obj11.copyB.Text = "Copy B ID";
																										obj11.copyB.Width = 84;
																										obj11.copyB.Height = 28;
																										obj11.copyB.Enabled = false;
																										obj12.findIds.Text = "Find IDs";
																										obj12.findIds.Width = 84;
																										obj12.findIds.Height = 28;
																										obj12.findIds.Enabled = false;
																										flowLayoutPanel2.Controls.Add(obj10.copyA);
																										flowLayoutPanel2.Controls.Add(obj11.copyB);
																										flowLayoutPanel2.Controls.Add(obj12.findIds);
																										CS_0024_003C_003E8__locals184.previewPanel.Controls.Add(obj9.previewText);
																										CS_0024_003C_003E8__locals184.previewPanel.Controls.Add(flowLayoutPanel2);
																										CS_0024_003C_003E8__locals184.previewPanel.Controls.Add(obj8.previewTitle);
																										CS_0024_003C_003E8__locals182.clashSplit.Panel2.Controls.Add(CS_0024_003C_003E8__locals184.previewPanel);
																										CS_0024_003C_003E8__locals192.scanOptions.Dock = DockStyle.Top;
																										CS_0024_003C_003E8__locals192.scanOptions.Height = 64;
																										CS_0024_003C_003E8__locals192.scanOptions.Margin = new Padding(0, 6, 0, 0);
																										CS_0024_003C_003E8__locals192.scanOptions.BackColor = Color.White;
																										CS_0024_003C_003E8__locals192.scanOptions.Padding = new Padding(14, 8, 14, 8);
																										CS_0024_003C_003E8__locals192.scanOptions.Paint += (object sender, PaintEventArgs e) =>
																										{
																											e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
																											using GraphicsPath path = RoundedPath(new Rectangle(0, 0, CS_0024_003C_003E8__locals192.scanOptions.Width - 1, CS_0024_003C_003E8__locals192.scanOptions.Height - 1), 8);
																											using Pen pen = new Pen(Color.FromArgb(205, 211, 217), 1f);
																											e.Graphics.DrawPath(pen, path);
																										};
																										CS_0024_003C_003E8__locals192.scanOptions.Resize += (object param0, EventArgs param1) =>
																										{
																											if (CS_0024_003C_003E8__locals192.scanOptions.Width > 4 && CS_0024_003C_003E8__locals192.scanOptions.Height > 4)
																											{
																												using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, CS_0024_003C_003E8__locals192.scanOptions.Width - 1, CS_0024_003C_003E8__locals192.scanOptions.Height - 1), 8))
																												{
																													Region region = CS_0024_003C_003E8__locals192.scanOptions.Region;
																													CS_0024_003C_003E8__locals192.scanOptions.Region = new Region(path);
																													region?.Dispose();
																												}
																											}
																										};
																										Label label5 = new Label();
																										label5.Text = "SCAN SCOPE";
																										label5.Left = 14;
																										label5.Top = 18;
																										label5.Width = 132;
																										label5.Height = 22;
																										label5.ForeColor = CS_0024_003C_003E8__locals349.ink;
																										label5.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
																										Label value6 = label5;
																										obj4.scope.Dock = DockStyle.None;
																										obj4.scope.SetBounds(154, 10, 260, 32);
																										obj4.scope.Anchor = AnchorStyles.Top | AnchorStyles.Left;
																										CS_0024_003C_003E8__locals192.scanOptions.Controls.Add(value6);
																										CS_0024_003C_003E8__locals192.scanOptions.Controls.Add(obj4.scope);
																										CS_0024_003C_003E8__locals194.rulesBox.Dock = DockStyle.Top;
																		CS_0024_003C_003E8__locals194.rulesBox.Height = 156;
																										CS_0024_003C_003E8__locals194.rulesBox.BackColor = Color.White;
																										CS_0024_003C_003E8__locals194.rulesBox.Padding = new Padding(10, 8, 10, 8);
																										CS_0024_003C_003E8__locals194.rulesBox.Paint += (object sender, PaintEventArgs e) =>
																										{
																											e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
																											using GraphicsPath path = RoundedPath(new Rectangle(0, 0, CS_0024_003C_003E8__locals194.rulesBox.Width - 1, CS_0024_003C_003E8__locals194.rulesBox.Height - 1), 10);
																											using Pen pen = new Pen(Color.FromArgb(205, 211, 217), 1f);
																											e.Graphics.DrawPath(pen, path);
																										};
																										CS_0024_003C_003E8__locals194.rulesBox.Controls.Add(obj7.disciplinePair);
																										CS_0024_003C_003E8__locals194.rulesBox.Controls.Add(value3);
																										panel5.Controls.Add(CS_0024_003C_003E8__locals182.clashSplit);
																										panel5.Controls.Add(obj13.detail);
																										panel5.Controls.Add(CS_0024_003C_003E8__locals194.rulesBox);
																										panel5.Controls.Add(CS_0024_003C_003E8__locals192.scanOptions);
																										panel5.Controls.Add(obj15.status);
																										Panel panel6 = new Panel();
																										panel6.Dock = DockStyle.Bottom;
																										panel6.Height = 64;
																										panel6.BackColor = Color.FromArgb(216, 221, 227);
																										panel6.Padding = new Padding(12, 8, 12, 8);
																										Panel panel7 = panel6;
																										AddFooterAccent(panel7);
																										glassButton.Text = "Run Clash Test";
																										glassButton.Width = 126;
																										glassButton.Primary = true;
																										obj14.review.Text = "Review in Revit";
																										obj14.review.Width = 122;
																										obj14.review.Enabled = false;
																										glassButton2.Text = "Export Report";
																										glassButton2.Width = 112;
																										glassButton3.Text = "Close";
																										glassButton3.Width = 80;
																										glassButton3.DialogResult = DialogResult.Cancel;
																										Panel panel8 = new Panel();
																										panel8.Dock = DockStyle.Right;
																										panel8.Width = 470;
																										panel8.BackColor = panel7.BackColor;
																										Panel panel9 = panel8;
																										PlaceFooterButton(panel9, glassButton3, 0, 96);
																										PlaceFooterButton(panel9, glassButton2, 108, 112);
																										PlaceFooterButton(panel9, obj14.review, 232, 112);
																										PlaceFooterButton(panel9, glassButton, 356, 126);
																										panel7.Controls.Add(panel9);
																										bIMlifyForm.Controls.Add(panel5);
																										bIMlifyForm.Controls.Add(panel7);
																										bIMlifyForm.Controls.Add(panel3);
																										AddWindowSidebar(bIMlifyForm, dashboard: false);
																										bIMlifyForm.CancelButton = glassButton3;
																										CS_0024_003C_003E8__locals349.clashRows = new List<Tuple<ElementId, ElementId, string, string, string>>();
																										CS_0024_003C_003E8__locals349.disciplineOf = (string category) =>
																										{
																											string text = (category ?? "").ToLowerInvariant();
																											if (text.Contains("struct") || text.Contains("wall") || text.Contains("column") || text.Contains("beam") || text.Contains("floor") || text.Contains("foundation"))
																											{
																												return "STR";
																											}
																											if (text.Contains("duct") || text.Contains("mechanical") || text.Contains("air terminal") || text.Contains("pipe") || text.Contains("plumbing") || text.Contains("fire protection"))
																											{
																												return "MEP";
																											}
																											if (text.Contains("electrical") || text.Contains("lighting") || text.Contains("cable") || text.Contains("device"))
																											{
																												return "ELEC";
																											}
																											return (text.Contains("door") || text.Contains("window") || text.Contains("room") || text.Contains("ceiling") || text.Contains("casework")) ? "ARCH" : "OTHER";
																										};
																										CS_0024_003C_003E8__locals349.choiceCode = (string choice) => (string.IsNullOrEmpty(choice) || choice == "All disciplines") ? "ANY" : (choice switch
																										{
																											"Architecture" => "ARCH", 
																											"Structure" => "STR", 
																											"Mechanical / HVAC" => "MEP", 
																											"Electrical / Plumbing" => "ELEC", 
																											_ => "OTHER", 
																										});
																										CS_0024_003C_003E8__locals349.refreshResults = () =>
																										{
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.BeginUpdate();
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.Items.Clear();
																											string text = "ALL";
																											foreach (Tuple<ElementId, ElementId, string, string, string> item in CS_0024_003C_003E8__locals349.clashRows)
																											{
																												if (text == "ALL" || item.Item4 == text)
																												{
																													CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.Items.Add(item.Item3);
																												}
																											}
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.EndUpdate();
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390.status.Text = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.Items.Count + " shown · " + CS_0024_003C_003E8__locals349.clashRows.Count + " total potential clash(es).";
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38e.review.Enabled = false;
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals386.copyA.Enabled = false;
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals388.copyB.Enabled = false;
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38a.findIds.Enabled = false;
																											CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38c.detail.Text = "Select a clash to see the reason and suggested coordination action.";
																										};
																										CS_0024_003C_003E8__locals349.runScan = () =>
																										{
																											//Discarded unreachable code: IL_0124, IL_0129, IL_0133, IL_0235, IL_0237, IL_0248, IL_025f, IL_026b, IL_0276, IL_02bd, IL_02c2, IL_02cf, IL_02d9, IL_02e3, IL_02f0, IL_02fa, IL_0304, IL_0311, IL_031e, IL_0328, IL_0332, IL_033c, IL_0346, IL_0347, IL_034a, IL_034b, IL_034e, IL_034f, IL_0352, IL_0353, IL_035e, IL_0363, IL_039e, IL_03b8, IL_03d2, IL_03ec, IL_0406, IL_0422, IL_0423, IL_042a, IL_042f, IL_0588, IL_058a, IL_058b, IL_0591, IL_05a4, IL_05aa, IL_05bd
																											LinkedClashSupport.StartHostScanAsync(CS_0024_003C_003E8__locals349);
																										};
																										CS_0024_003C_003E8__locals349.updatePairLabel = () =>
																										{
																											CS_0024_003C_003E8__locals349.intersectLabel.Text = ((CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36e.disciplineA.SelectedItem == null) ? "ANY" : CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36e.disciplineA.SelectedItem.ToString()) + "  ×  " + ((CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals370.disciplineB.SelectedItem == null) ? "ANY" : CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals370.disciplineB.SelectedItem.ToString());
																										};
																										obj3.discipline.SelectedIndexChanged += (object param0, EventArgs param1) =>
																										{
																											CS_0024_003C_003E8__locals349.refreshResults();
																										};
																										obj5.disciplineA.SelectedIndexChanged += (object param0, EventArgs param1) =>
																										{
																											CS_0024_003C_003E8__locals349.updatePairLabel();
																											CS_0024_003C_003E8__locals349.runScan();
																										};
																										obj6.disciplineB.SelectedIndexChanged += (object param0, EventArgs param1) =>
																										{
																											CS_0024_003C_003E8__locals349.updatePairLabel();
																											CS_0024_003C_003E8__locals349.runScan();
																										};
																										CS_0024_003C_003E8__locals349.updatePairLabel();
																										obj2.results.SelectedIndexChanged += (object param0, EventArgs param1) =>
																										{
																											_003C_003Ec__DisplayClass391 obj16 = CS_0024_003C_003E8__locals349;
																											_003C_003Ec__DisplayClass38f obj17 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390;
																											_003C_003Ec__DisplayClass38d obj18 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38e;
																											_003C_003Ec__DisplayClass38b obj19 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38c;
																											_003C_003Ec__DisplayClass389 obj20 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38a;
																											_003C_003Ec__DisplayClass387 obj21 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals388;
																											_003C_003Ec__DisplayClass385 obj22 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals386;
																											_003C_003Ec__DisplayClass383 obj23 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals384;
																											_003C_003Ec__DisplayClass381 obj24 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals382;
																											_003C_003Ec__DisplayClass37f obj25 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals380;
																											_003C_003Ec__DisplayClass37c obj26 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals37d;
																											_003C_003Ec__DisplayClass378 obj27 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals379;
																											_003C_003Ec__DisplayClass374 obj28 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals375;
																											_003C_003Ec__DisplayClass371 obj29 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals372;
																											_003C_003Ec__DisplayClass36f obj30 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals370;
																											_003C_003Ec__DisplayClass36d obj31 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36e;
																											_003C_003Ec__DisplayClass36b obj32 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36c;
																											_003C_003Ec__DisplayClass369 obj33 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36a;
																											_003C_003Ec__DisplayClass367 obj34 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368;
																											_003C_003Ec__DisplayClass365 obj35 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals366;
																											int selectedIndex = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.SelectedIndex;
																											if (selectedIndex >= 0)
																											{
																												string selected = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.Items[selectedIndex] as string;
																												Tuple<ElementId, ElementId, string, string, string> tuple = CS_0024_003C_003E8__locals349.clashRows.FirstOrDefault((Tuple<ElementId, ElementId, string, string, string> x) => x.Item3 == selected);
																												if (tuple != null)
																												{
																													CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38c.detail.Text = "Reason: " + tuple.Item5 + " Action: inspect both elements; revise the responsible discipline's layout.";
																													CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals382.previewTitle.Text = "Clash details · " + tuple.Item4;
																													CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals384.previewText.Text = "Element A: " + tuple.Item1.IntegerValue + Environment.NewLine + "Element B: " + tuple.Item2.IntegerValue + Environment.NewLine + Environment.NewLine + tuple.Item5 + Environment.NewLine + Environment.NewLine + "Use Review in Revit to select and frame both elements.";
																													CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38e.review.Enabled = true;
																													CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals386.copyA.Enabled = true;
																													CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals388.copyB.Enabled = true;
																													CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38a.findIds.Enabled = true;
																												}
																											}
																										};
																										obj14.review.Click += (object param0, EventArgs param1) =>
																										{
																											_003C_003Ec__DisplayClass391 obj16 = CS_0024_003C_003E8__locals349;
																											_003C_003Ec__DisplayClass38f obj17 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390;
																											_003C_003Ec__DisplayClass38d obj18 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38e;
																											_003C_003Ec__DisplayClass38b obj19 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38c;
																											_003C_003Ec__DisplayClass389 obj20 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38a;
																											_003C_003Ec__DisplayClass387 obj21 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals388;
																											_003C_003Ec__DisplayClass385 obj22 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals386;
																											_003C_003Ec__DisplayClass383 obj23 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals384;
																											_003C_003Ec__DisplayClass381 obj24 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals382;
																											_003C_003Ec__DisplayClass37f obj25 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals380;
																											_003C_003Ec__DisplayClass37c obj26 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals37d;
																											_003C_003Ec__DisplayClass378 obj27 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals379;
																											_003C_003Ec__DisplayClass374 obj28 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals375;
																											_003C_003Ec__DisplayClass371 obj29 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals372;
																											_003C_003Ec__DisplayClass36f obj30 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals370;
																											_003C_003Ec__DisplayClass36d obj31 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36e;
																											_003C_003Ec__DisplayClass36b obj32 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36c;
																											_003C_003Ec__DisplayClass369 obj33 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36a;
																											_003C_003Ec__DisplayClass367 obj34 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368;
																											_003C_003Ec__DisplayClass365 obj35 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals366;
																											int i = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.SelectedIndex;
																											if (i >= 0)
																											{
																												Tuple<ElementId, ElementId, string, string, string> tuple = CS_0024_003C_003E8__locals349.clashRows.FirstOrDefault((Tuple<ElementId, ElementId, string, string, string> x) => x.Item3 == obj34.results.Items[i] as string);
																												if (tuple != null)
																												{
																													CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals366.ui.Selection.SetElementIds((ICollection<ElementId>)new List<ElementId> { tuple.Item1, tuple.Item2 });
																													try
																													{
																														CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals366.ui.ShowElements((ICollection<ElementId>)new List<ElementId> { tuple.Item1, tuple.Item2 });
																													}
																													catch
																													{
																													}
																												}
																											}
																										};
																										obj10.copyA.Click += (object param0, EventArgs param1) =>
																										{
																											_003C_003Ec__DisplayClass391 obj16 = CS_0024_003C_003E8__locals349;
																											_003C_003Ec__DisplayClass38f obj17 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390;
																											_003C_003Ec__DisplayClass38d obj18 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38e;
																											_003C_003Ec__DisplayClass38b obj19 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38c;
																											_003C_003Ec__DisplayClass389 obj20 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38a;
																											_003C_003Ec__DisplayClass387 obj21 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals388;
																											_003C_003Ec__DisplayClass385 obj22 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals386;
																											_003C_003Ec__DisplayClass383 obj23 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals384;
																											_003C_003Ec__DisplayClass381 obj24 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals382;
																											_003C_003Ec__DisplayClass37f obj25 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals380;
																											_003C_003Ec__DisplayClass37c obj26 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals37d;
																											_003C_003Ec__DisplayClass378 obj27 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals379;
																											_003C_003Ec__DisplayClass374 obj28 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals375;
																											_003C_003Ec__DisplayClass371 obj29 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals372;
																											_003C_003Ec__DisplayClass36f obj30 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals370;
																											_003C_003Ec__DisplayClass36d obj31 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36e;
																											_003C_003Ec__DisplayClass36b obj32 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36c;
																											_003C_003Ec__DisplayClass369 obj33 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36a;
																											_003C_003Ec__DisplayClass367 obj34 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368;
																											_003C_003Ec__DisplayClass365 obj35 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals366;
																											int i = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.SelectedIndex;
																											if (i >= 0)
																											{
																												Tuple<ElementId, ElementId, string, string, string> tuple = CS_0024_003C_003E8__locals349.clashRows.FirstOrDefault((Tuple<ElementId, ElementId, string, string, string> x) => x.Item3 == obj34.results.Items[i] as string);
																												if (tuple != null)
																												{
																													try
																													{
																														Clipboard.SetText(tuple.Item1.IntegerValue.ToString());
																														CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390.status.Text = "Element A ID copied: " + tuple.Item1.IntegerValue;
																													}
																													catch
																													{
																													}
																												}
																											}
																										};
																										obj11.copyB.Click += (object param0, EventArgs param1) =>
																										{
																											_003C_003Ec__DisplayClass391 obj16 = CS_0024_003C_003E8__locals349;
																											_003C_003Ec__DisplayClass38f obj17 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390;
																											_003C_003Ec__DisplayClass38d obj18 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38e;
																											_003C_003Ec__DisplayClass38b obj19 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38c;
																											_003C_003Ec__DisplayClass389 obj20 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38a;
																											_003C_003Ec__DisplayClass387 obj21 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals388;
																											_003C_003Ec__DisplayClass385 obj22 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals386;
																											_003C_003Ec__DisplayClass383 obj23 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals384;
																											_003C_003Ec__DisplayClass381 obj24 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals382;
																											_003C_003Ec__DisplayClass37f obj25 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals380;
																											_003C_003Ec__DisplayClass37c obj26 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals37d;
																											_003C_003Ec__DisplayClass378 obj27 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals379;
																											_003C_003Ec__DisplayClass374 obj28 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals375;
																											_003C_003Ec__DisplayClass371 obj29 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals372;
																											_003C_003Ec__DisplayClass36f obj30 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals370;
																											_003C_003Ec__DisplayClass36d obj31 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36e;
																											_003C_003Ec__DisplayClass36b obj32 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36c;
																											_003C_003Ec__DisplayClass369 obj33 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36a;
																											_003C_003Ec__DisplayClass367 obj34 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368;
																											_003C_003Ec__DisplayClass365 obj35 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals366;
																											int i = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.SelectedIndex;
																											if (i >= 0)
																											{
																												Tuple<ElementId, ElementId, string, string, string> tuple = CS_0024_003C_003E8__locals349.clashRows.FirstOrDefault((Tuple<ElementId, ElementId, string, string, string> x) => x.Item3 == obj34.results.Items[i] as string);
																												if (tuple != null)
																												{
																													try
																													{
																														Clipboard.SetText(tuple.Item2.IntegerValue.ToString());
																														CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390.status.Text = "Element B ID copied: " + tuple.Item2.IntegerValue;
																													}
																													catch
																													{
																													}
																												}
																											}
																										};
																										obj12.findIds.Click += (object param0, EventArgs param1) =>
																										{
																											_003C_003Ec__DisplayClass391 obj16 = CS_0024_003C_003E8__locals349;
																											_003C_003Ec__DisplayClass38f obj17 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390;
																											_003C_003Ec__DisplayClass38d obj18 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38e;
																											_003C_003Ec__DisplayClass38b obj19 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38c;
																											_003C_003Ec__DisplayClass389 obj20 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals38a;
																											_003C_003Ec__DisplayClass387 obj21 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals388;
																											_003C_003Ec__DisplayClass385 obj22 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals386;
																											_003C_003Ec__DisplayClass383 obj23 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals384;
																											_003C_003Ec__DisplayClass381 obj24 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals382;
																											_003C_003Ec__DisplayClass37f obj25 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals380;
																											_003C_003Ec__DisplayClass37c obj26 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals37d;
																											_003C_003Ec__DisplayClass378 obj27 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals379;
																											_003C_003Ec__DisplayClass374 obj28 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals375;
																											_003C_003Ec__DisplayClass371 obj29 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals372;
																											_003C_003Ec__DisplayClass36f obj30 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals370;
																											_003C_003Ec__DisplayClass36d obj31 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36e;
																											_003C_003Ec__DisplayClass36b obj32 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36c;
																											_003C_003Ec__DisplayClass369 obj33 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals36a;
																											_003C_003Ec__DisplayClass367 obj34 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368;
																											_003C_003Ec__DisplayClass365 obj35 = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals366;
																											int i = CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals368.results.SelectedIndex;
																											if (i >= 0)
																											{
																												Tuple<ElementId, ElementId, string, string, string> tuple = CS_0024_003C_003E8__locals349.clashRows.FirstOrDefault((Tuple<ElementId, ElementId, string, string, string> x) => x.Item3 == obj34.results.Items[i] as string);
																												if (tuple != null)
																												{
																													CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals366.ui.Selection.SetElementIds((ICollection<ElementId>)new List<ElementId> { tuple.Item1, tuple.Item2 });
																													try
																													{
																														CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals366.ui.ShowElements((ICollection<ElementId>)new List<ElementId> { tuple.Item1, tuple.Item2 });
																														CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390.status.Text = "Both clash element IDs selected and framed in Revit.";
																													}
																													catch
																													{
																													}
																												}
																											}
																										};
																										glassButton2.Click += (object param0, EventArgs param1) =>
																										{
																											try
																											{
																												string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BIMlify_Clash_Report.csv");
																												File.WriteAllLines(path, new string[1] { "Clash,Element A,Element B,Discipline A,Reason" }.Concat(CS_0024_003C_003E8__locals349.clashRows.Select((Tuple<ElementId, ElementId, string, string, string> x, int i) => "\"Clash " + (i + 1) + "\",\"" + x.Item1.IntegerValue + "\",\"" + x.Item2.IntegerValue + "\",\"" + x.Item4 + "\",\"" + x.Item5.Replace("\"", "'") + "\"")));
																												CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390.status.Text = "Report exported to Desktop: BIMlify_Clash_Report.csv";
																											}
																											catch (Exception ex)
																											{
																												CS_0024_003C_003E8__locals349.CS_0024_003C_003E8__locals390.status.Text = "Could not export report: " + ex.Message;
																											}
																										};
																										glassButton.Click += (object param0, EventArgs param1) =>
																										{
																											CS_0024_003C_003E8__locals349.runScan();
																										};
																										bIMlifyForm.Shown += (object param0, EventArgs param1) =>
																										{
																											CS_0024_003C_003E8__locals349.ensureClashSplit();
																											CS_0024_003C_003E8__locals349.runScan();
																										};
																										UiPolish.ApplyClash(bIMlifyForm);
																										LinkedClashSupport.Attach(bIMlifyForm, ui);
																										((Form)bIMlifyForm).Show();
																									}
																									finally
																									{
																										if (obj15.status != null)
																										{
																											_ = obj15.status;
																										}
																									}
																								}
																								finally
																								{
																									if (glassButton3 == null)
																									{
																									}
																								}
																							}
																							finally
																							{
																								if (glassButton2 == null)
																								{
																								}
																							}
																						}
																						finally
																						{
																							if (obj14.review != null)
																							{
																								_ = obj14.review;
																							}
																						}
																					}
																					finally
																					{
																						if (glassButton == null)
																						{
																						}
																					}
																				}
																				finally
																				{
																					if (obj13.detail != null)
																					{
																						_ = obj13.detail;
																					}
																				}
																			}
																			finally
																			{
																				if (obj12.findIds != null)
																				{
																					_ = obj12.findIds;
																				}
																			}
																		}
																		finally
																		{
																			if (obj11.copyB != null)
																			{
																				_ = obj11.copyB;
																			}
																		}
																	}
																	finally
																	{
																		if (obj10.copyA != null)
																		{
																			_ = obj10.copyA;
																		}
																	}
																}
																finally
																{
																	if (obj9.previewText != null)
																	{
																		_ = obj9.previewText;
																	}
																}
															}
															finally
															{
																if (obj8.previewTitle != null)
																{
																	_ = obj8.previewTitle;
																}
															}
														}
														finally
														{
															if (CS_0024_003C_003E8__locals184.previewPanel != null)
															{
																_ = CS_0024_003C_003E8__locals184.previewPanel;
															}
														}
													}
													finally
													{
														if (label == null)
														{
														}
													}
												}
												finally
												{
													if (panel == null)
													{
													}
												}
											}
											finally
											{
												if (CS_0024_003C_003E8__locals182.clashSplit != null)
												{
													_ = CS_0024_003C_003E8__locals182.clashSplit;
												}
											}
										}
										finally
										{
											if (CS_0024_003C_003E8__locals192.scanOptions != null)
											{
												_ = CS_0024_003C_003E8__locals192.scanOptions;
											}
										}
									}
									finally
									{
										if (CS_0024_003C_003E8__locals194.rulesBox != null)
										{
											_ = CS_0024_003C_003E8__locals194.rulesBox;
										}
									}
								}
								finally
								{
									if (obj7.disciplinePair != null)
									{
										_ = obj7.disciplinePair;
									}
								}
							}
							finally
							{
								if (obj6.disciplineB != null)
								{
									_ = obj6.disciplineB;
								}
							}
						}
						finally
						{
							if (obj5.disciplineA != null)
							{
								_ = obj5.disciplineA;
							}
						}
					}
					finally
					{
						if (obj4.scope != null)
						{
							_ = obj4.scope;
						}
					}
				}
				finally
				{
					if (obj3.discipline != null)
					{
						_ = obj3.discipline;
					}
				}
			}
			finally
			{
				if (obj2.results != null)
				{
					_ = obj2.results;
				}
			}
		}
		finally
		{
			if (bIMlifyForm == null)
			{
			}
		}
	}

	private static void PatternMaker(UIDocument ui)
	{
		Document doc = ui.Document;
		BIMlifyForm form = new BIMlifyForm();
		try
		{
			TextBox description = new TextBox();
			try
			{
				TextBox patternName = new TextBox();
				try
				{
					TextBox tileWidth = new TextBox();
					try
					{
						TextBox tileHeight = new TextBox();
						try
						{
							TextBox grout = new TextBox();
							try
							{
								ComboBox layout = new LightGoldComboBox();
								try
								{
									ComboBox patternType = new LightGoldComboBox();
									try
									{
										TextBox librarySearch = new AdvancedSearchTextBox();
										try
										{
											ComboBox library = new LightGoldComboBox();
											try
											{
												Panel preview = new Panel();
												try
												{
													using Panel panel = new Panel();
													Label zoomLabel = new Label();
													try
													{
														using FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
														using GlassButton glassButton = new GlassButton();
														using GlassButton glassButton2 = new GlassButton();
														using GlassButton glassButton3 = new GlassButton();
														Label status = new Label();
														try
														{
															Color backColor = Color.FromArgb(238, 241, 245);
															Color color = Color.FromArgb(238, 241, 245);
															Color color2 = Color.FromArgb(78, 85, 92);
															Color gold = Color.FromArgb(214, 171, 61);
															form.Text = "BIMlify — Pattern Maker";
															form.Width = 1040;
															form.Height = 700;
															form.MinimumSize = new Size(940, 640);
															form.StartPosition = FormStartPosition.CenterScreen;
															form.ShowIcon = false;
															form.MaximizeBox = true;
															form.MinimizeBox = true;
															form.AutoScaleMode = AutoScaleMode.Dpi;
															form.BackColor = backColor;
															form.ForeColor = color2;
															form.Font = new Font("Segoe UI", 9f);
															UseSmoothCorners(form);
															Panel panel2 = new Panel();
															panel2.Dock = DockStyle.Top;
															panel2.Height = 128;
															panel2.BackColor = Color.FromArgb(247, 247, 245);
															panel2.Padding = new Padding(18, 10, 18, 6);
															Panel panel3 = panel2;
															Label label = new Label();
															label.Text = "Pattern Maker";
															label.Left = 18;
															label.Top = 10;
															label.Width = 520;
															label.Height = 25;
															label.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
															label.ForeColor = color2;
															Label value = label;
															Label label2 = new Label();
															label2.Text = "Create reusable fill patterns with tile, grid, and drafting options in one workspace.";
															label2.Left = 18;
															label2.Top = 38;
															label2.Width = 790;
															label2.Height = 24;
															label2.ForeColor = Color.FromArgb(102, 109, 118);
															label2.Font = new Font("Segoe UI", 8f);
															Label value2 = label2;
															panel3.Controls.Add(value);
															panel3.Controls.Add(value2);
															panel3.Controls.Add(CreateBrandLockup(panel2.BackColor));
															AddYellowPanelAccent(panel3, gold, atTop: false);
															Panel panel4 = new Panel();
															panel4.Dock = DockStyle.Bottom;
															panel4.Height = 64;
															panel4.BackColor = Color.FromArgb(216, 221, 227);
															panel4.Padding = new Padding(12, 8, 12, 8);
															Panel panel5 = panel4;
															AddFooterAccent(panel5);
															Panel panel6 = new Panel();
															panel6.Dock = DockStyle.Fill;
															panel6.BackColor = panel5.BackColor;
															Panel value3 = panel6;
															Panel panel7 = new Panel();
															panel7.Dock = DockStyle.Right;
															panel7.Width = 220;
															panel7.BackColor = panel5.BackColor;
															Panel panel8 = panel7;
															GlassButton glassButton4 = new GlassButton();
															glassButton4.Text = "Close";
															glassButton4.Width = 90;
															glassButton4.DialogResult = DialogResult.Cancel;
															GlassButton glassButton5 = glassButton4;
															GlassButton glassButton6 = new GlassButton();
															glassButton6.Text = "Create Pattern";
															glassButton6.Width = 104;
															glassButton6.Primary = true;
															GlassButton glassButton7 = glassButton6;
															GlassButton glassButton8 = new GlassButton();
															glassButton8.Text = "Import .PAT";
															glassButton8.Width = 110;
															glassButton8.Primary = true;
															GlassButton glassButton9 = glassButton8;
										PlaceFooterButton(panel8, glassButton5, 0, 96);
										PlaceFooterButton(panel8, glassButton7, 108, 104);
															panel5.Controls.Add(value3);
															panel5.Controls.Add(panel8);
															Panel body = new Panel
															{
																Dock = DockStyle.Fill,
																BackColor = Color.White,
																Padding = new Padding(12)
															};
															Panel editor = new Panel
															{
																Dock = DockStyle.Left,
																Width = 540,
																BackColor = Color.White,
																Padding = new Padding(8, 4, 14, 4)
															};
															Panel panel9 = new Panel();
															panel9.Dock = DockStyle.Fill;
															panel9.BackColor = Color.White;
															panel9.Padding = new Padding(14, 4, 8, 4);
															Panel panel10 = panel9;
															Action<Panel> action = (Panel card) =>
															{
																card.Margin = new Padding(6);
																card.Paint += (object sender, PaintEventArgs e) =>
																{
																	e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
																	using GraphicsPath path = RoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 8);
																	using Pen pen = new Pen(Color.FromArgb(205, 211, 217), 1f);
																	e.Graphics.DrawPath(pen, path);
																};
																card.Resize += (object param0, EventArgs param1) =>
																{
																	if (card.Width > 4 && card.Height > 4)
																	{
																		using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 8))
																		{
																			Region region = card.Region;
																			card.Region = new Region(path);
																			region?.Dispose();
																		}
																	}
																};
															};
															action(editor);
															action(panel10);
															Label label3 = new Label();
															label3.Text = "Description";
															label3.Left = 8;
															label3.Top = 4;
															label3.Width = 180;
															label3.Height = 20;
															label3.ForeColor = color2;
															Label label4 = label3;
															description.Left = 8;
															description.Top = 26;
															description.Width = 500;
															description.Height = 64;
															description.Multiline = true;
															description.AcceptsReturn = true;
															description.ScrollBars = ScrollBars.Vertical;
															description.BorderStyle = BorderStyle.FixedSingle;
															description.BackColor = color;
															description.ForeColor = color2;
															description.Text = "600 x 600 mm square tile, 2 mm grout joint, straight grid, model pattern";
															GlassButton glassButton10 = new GlassButton();
															glassButton10.Text = "Read Description";
															glassButton10.Left = 8;
															glassButton10.Top = 91;
															glassButton10.Width = 138;
															glassButton10.Height = 30;
															GlassButton glassButton11 = glassButton10;
															GlassButton glassButton12 = new GlassButton();
															glassButton12.Text = "Write with AI";
															glassButton12.Left = 152;
															glassButton12.Top = 91;
															glassButton12.Width = 138;
															glassButton12.Height = 30;
															glassButton12.Primary = true;
															GlassButton glassButton13 = glassButton12;
															Label label5 = new Label();
															label5.Text = "Pattern name";
															label5.Left = 8;
															label5.Top = 133;
															label5.Width = 160;
															label5.Height = 20;
															label5.ForeColor = color2;
															Label label6 = label5;
															patternName.SetBounds(8, 155, 500, 27);
															patternName.Text = "BIMlify Tile 600x600";
															patternName.BorderStyle = BorderStyle.FixedSingle;
															patternName.BackColor = color;
															patternName.ForeColor = color2;
															Label label7 = new Label();
															label7.Text = "Tile width (mm)";
															label7.Left = 8;
															label7.Top = 198;
															label7.Width = 150;
															label7.Height = 20;
															label7.ForeColor = color2;
															Label label8 = label7;
															Label label9 = new Label();
															label9.Text = "Tile height (mm)";
															label9.Left = 180;
															label9.Top = 198;
															label9.Width = 150;
															label9.Height = 20;
															label9.ForeColor = color2;
															Label label10 = label9;
															Label label11 = new Label();
															label11.Text = "Grout joint (mm)";
															label11.Left = 352;
															label11.Top = 198;
															label11.Width = 150;
															label11.Height = 20;
															label11.ForeColor = color2;
															Label label12 = label11;
															tileWidth.SetBounds(8, 220, 150, 27);
															tileHeight.SetBounds(180, 220, 150, 27);
															grout.SetBounds(352, 220, 150, 27);
															TextBox[] array = new TextBox[3] { tileWidth, tileHeight, grout };
															foreach (TextBox textBox in array)
															{
																textBox.BorderStyle = BorderStyle.FixedSingle;
																textBox.BackColor = color;
																textBox.ForeColor = color2;
															}
															tileWidth.Text = "600";
															tileHeight.Text = "600";
															grout.Text = "2";
															Label label13 = new Label();
															label13.Text = "Layout";
															label13.Left = 8;
															label13.Top = 263;
															label13.Width = 220;
															label13.Height = 20;
															label13.ForeColor = color2;
															Label label14 = label13;
															Label label15 = new Label();
															label15.Text = "Pattern type";
															label15.Left = 268;
															label15.Top = 263;
															label15.Width = 230;
															label15.Height = 20;
															label15.ForeColor = color2;
															Label label16 = label15;
															layout.SetBounds(8, 285, 240, 28);
															layout.DropDownStyle = ComboBoxStyle.DropDownList;
															layout.FlatStyle = FlatStyle.Flat;
															layout.BackColor = color;
															layout.ForeColor = color2;
															layout.Items.AddRange(new object[3] { "Straight grid", "Running bond", "Diagonal grid" });
															layout.SelectedIndex = 0;
															patternType.SetBounds(268, 285, 240, 28);
															patternType.DropDownStyle = ComboBoxStyle.DropDownList;
															patternType.FlatStyle = FlatStyle.Flat;
															patternType.BackColor = color;
															patternType.ForeColor = color2;
															patternType.Items.AddRange(new object[2] { "Model — real tile size", "Drafting — fixed on sheet" });
															patternType.SelectedIndex = 0;
															Label label17 = new Label();
															label17.Text = "Sizes are entered in millimetres. A 2 mm joint is the clear gap between adjacent tile-edge lines. Model patterns keep real-world spacing; Drafting patterns keep sheet spacing.";
															label17.Left = 8;
															label17.Top = 324;
															label17.Width = 500;
															label17.Height = 52;
															label17.ForeColor = Color.FromArgb(102, 109, 118);
															label17.Font = new Font("Segoe UI", 8f);
															Label label18 = label17;
															editor.Controls.AddRange(new Control[17]
															{
																label4, description, glassButton11, glassButton13, label6, patternName, label8, label10, label12, tileWidth,
																tileHeight, grout, label14, label16, layout, patternType, label18
															});
															Label label19 = new Label();
															label19.Text = "Preview";
															label19.Dock = DockStyle.Top;
															label19.Height = 28;
															label19.ForeColor = color2;
															label19.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
															label19.Padding = new Padding(0, 3, 0, 0);
															Label value4 = label19;
															preview.Dock = DockStyle.Top;
															preview.Height = 228;
															preview.BackColor = Color.White;
															preview.BorderStyle = BorderStyle.None;
															preview.TabStop = true;
															preview.Paint += (object sender, PaintEventArgs e) =>
															{
																using Pen pen = new Pen(Color.FromArgb(210, 215, 220), 1f);
																using GraphicsPath path = RoundedPath(new Rectangle(0, 0, preview.Width - 1, preview.Height - 1), 7);
						e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
																e.Graphics.DrawPath(pen, path);
															};
															panel.Dock = DockStyle.Top;
															panel.Height = 64;
															panel.BackColor = backColor;
															panel.Padding = new Padding(4, 2, 4, 2);
															zoomLabel.Dock = DockStyle.Top;
															zoomLabel.Height = 27;
															zoomLabel.Width = 246;
															zoomLabel.AutoSize = false;
															zoomLabel.AutoEllipsis = true;
															zoomLabel.TextAlign = ContentAlignment.MiddleLeft;
															zoomLabel.ForeColor = color2;
															zoomLabel.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
															flowLayoutPanel.Dock = DockStyle.Bottom;
															flowLayoutPanel.Height = 30;
															flowLayoutPanel.Width = 246;
															flowLayoutPanel.FlowDirection = FlowDirection.RightToLeft;
															flowLayoutPanel.WrapContents = false;
															flowLayoutPanel.BackColor = backColor;
															flowLayoutPanel.Padding = new Padding(0, 1, 0, 0);
															glassButton.Text = "Zoom +";
															glassButton.Width = 78;
															glassButton.MinimumSize = new Size(74, 25);
															glassButton.Height = 25;
															glassButton.Margin = new Padding(1, 0, 1, 0);
															glassButton.AccessibleName = "Zoom in on pattern preview";
															glassButton2.Text = "Zoom −";
															glassButton2.Width = 78;
															glassButton2.MinimumSize = new Size(74, 25);
															glassButton2.Height = 25;
															glassButton2.Margin = new Padding(1, 0, 1, 0);
															glassButton2.AccessibleName = "Zoom out on pattern preview";
															glassButton3.Text = "Fit Preview";
															glassButton3.Width = 78;
															glassButton3.MinimumSize = new Size(74, 25);
															glassButton3.Height = 25;
															glassButton3.Margin = new Padding(1, 0, 1, 0);
															glassButton3.AccessibleName = "Fit pattern preview";
															flowLayoutPanel.Controls.AddRange(new Control[3] { glassButton3, glassButton, glassButton2 });
															panel.Controls.Add(flowLayoutPanel);
															panel.Controls.Add(zoomLabel);
															Label label20 = new Label();
															label20.Text = "Search patterns in this project";
															label20.Dock = DockStyle.Top;
															label20.Height = 26;
															label20.ForeColor = color2;
															label20.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
															label20.Padding = new Padding(0, 2, 0, 0);
															Label label21 = label20;
															Panel panel11 = new Panel();
															panel11.Dock = DockStyle.Top;
															panel11.Height = 36;
															panel11.BackColor = backColor;
															panel11.Padding = new Padding(0, 3, 0, 3);
															Panel panel12 = panel11;
															glassButton9.Dock = DockStyle.Right;
															glassButton9.Width = 104;
															glassButton9.Height = 30;
															glassButton9.Margin = new Padding(6, 0, 0, 0);
															label21.Dock = DockStyle.Left;
															label21.Width = 240;
															panel12.Controls.Add(label21);
															panel12.Controls.Add(glassButton9);
															librarySearch.Dock = DockStyle.Top;
															librarySearch.Height = 30;
															librarySearch.Margin = new Padding(4, 0, 4, 0);
															librarySearch.Font = new Font("Segoe UI", 9f);
															StyleSearchBox(librarySearch, Color.FromArgb(244, 246, 248), color2, "Search pattern name or type");
															library.Dock = DockStyle.Top;
															library.Height = 32;
															library.Margin = new Padding(4, 0, 4, 0);
															library.DropDownStyle = ComboBoxStyle.DropDownList;
															library.FlatStyle = FlatStyle.Flat;
															library.BackColor = Color.FromArgb(232, 235, 239);
															library.ForeColor = color2;
															library.Font = new Font("Segoe UI", 9f);
															library.DrawMode = DrawMode.OwnerDrawFixed;
															library.ItemHeight = 28;
															library.IntegralHeight = true;
															library.MaxDropDownItems = 14;
															DrawComboText(library, color2, gold);
															status.Dock = DockStyle.Bottom;
															status.Height = 48;
															status.ForeColor = Color.FromArgb(102, 109, 118);
															status.Font = new Font("Segoe UI", 8f);
															status.TextAlign = ContentAlignment.MiddleLeft;
															status.Padding = new Padding(2, 2, 2, 2);
															panel10.Controls.Add(library);
															panel10.Controls.Add(librarySearch);
															panel10.Controls.Add(panel12);
															panel10.Controls.Add(preview);
															panel10.Controls.Add(panel);
															panel10.Controls.Add(value4);
															panel10.Controls.Add(status);
															body.Resize += (object param0, EventArgs param1) =>
															{
																int width = Math.Max(420, (body.ClientSize.Width - body.Padding.Horizontal) / 2);
																editor.Width = width;
															};
															body.Controls.Add(panel10);
															body.Controls.Add(editor);
															form.Controls.Add(body);
															form.Controls.Add(panel5);
															form.Controls.Add(panel3);
															AddWindowSidebar(form, dashboard: false);
															form.CancelButton = glassButton5;
															Func<double> getWidth = () => double.TryParse(tileWidth.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var result) ? result : 0.0;
															Func<double> getHeight = () => double.TryParse(tileHeight.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var result) ? result : 0.0;
															Func<double> getGrout = () => double.TryParse(grout.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var result) ? result : 0.0;
															Action refreshLibrary = () =>
															{
																//IL_0049: Unknown result type (might be due to invalid IL or missing references)
																//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
																//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
																string query = (librarySearch.Text ?? "").Trim();
																library.BeginUpdate();
																library.Items.Clear();
																foreach (FillPatternElement item in ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))).Cast<FillPatternElement>().OrderBy((FillPatternElement x) => ((Element)x).Name, StringComparer.CurrentCultureIgnoreCase))
																{
																	if (SearchTextMatches(query, ((Element)item).Name, ((object)item.GetFillPattern().Target).ToString()))
																	{
																		library.Items.Add(((Element)item).Name + "  —  " + item.GetFillPattern().Target);
																	}
																}
																if (library.Items.Count > 0 && library.SelectedIndex < 0)
																{
																	library.SelectedIndex = 0;
																}
																library.EndUpdate();
															};
															float previewZoom = 1f;
															Action updateZoomLabel = () =>
															{
																zoomLabel.Text = "Preview zoom: " + Math.Round(previewZoom * 100f) + "%";
															};
															Action redraw = () =>
															{
																preview.Invalidate();
															};
															Action<float> changeZoom = (float factor) =>
															{
																previewZoom = Math.Max(1f, Math.Min(12f, previewZoom * factor));
																updateZoomLabel();
																redraw();
															};
															updateZoomLabel();
															Func<string, string> nextAvailablePatternName = (string currentName) =>
															{
																//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
																Match match = Regex.Match((currentName ?? "").Trim(), "^(.*?)(?: \\((\\d+)\\))?$");
																string text = match.Groups[1].Value.Trim();
																if (text.Length == 0)
																{
																	text = "BIMlify Tile Pattern";
																}
																int result = 2;
																if (match.Groups[2].Success)
																{
																	int.TryParse(match.Groups[2].Value, out result);
																}
																if (match.Groups[2].Success)
																{
																	result++;
																}
																HashSet<string> hashSet = new HashSet<string>(from FillPatternElement x in (IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))
																	select ((Element)x).Name, StringComparer.OrdinalIgnoreCase);
																if (!hashSet.Contains(text))
																{
																	return text;
																}
																string text2;
																do
																{
																	text2 = text + " (" + result++ + ")";
																}
																while (hashSet.Contains(text2));
																return text2;
															};
															glassButton.Click += (object param0, EventArgs param1) =>
															{
																changeZoom(1.35f);
															};
															glassButton2.Click += (object param0, EventArgs param1) =>
															{
																changeZoom(20f / 27f);
															};
															glassButton3.Click += (object param0, EventArgs param1) =>
															{
																previewZoom = 1f;
																updateZoomLabel();
																redraw();
															};
															preview.MouseEnter += (object param0, EventArgs param1) =>
															{
																preview.Focus();
															};
															preview.MouseWheel += (object sender, MouseEventArgs e) =>
															{
																if (e.Delta > 0)
																{
																	changeZoom(1.2f);
																}
																else if (e.Delta < 0)
																{
																	changeZoom(5f / 6f);
																}
															};
															layout.SelectedIndexChanged += (object param0, EventArgs param1) =>
															{
																redraw();
															};
															tileWidth.TextChanged += (object param0, EventArgs param1) =>
															{
																redraw();
															};
															tileHeight.TextChanged += (object param0, EventArgs param1) =>
															{
																redraw();
															};
															grout.TextChanged += (object param0, EventArgs param1) =>
															{
																redraw();
															};
															librarySearch.TextChanged += (object param0, EventArgs param1) =>
															{
																refreshLibrary();
															};
															preview.Paint += (object sender, PaintEventArgs e) =>
															{
																double num2 = getWidth();
																double num3 = getHeight();
																double num4 = getGrout();
																if (!(num2 <= 0.0) && !(num3 <= 0.0))
																{
																	Graphics graphics = e.Graphics;
																	graphics.SmoothingMode = SmoothingMode.AntiAlias;
																	graphics.Clear(Color.White);
																	graphics.TranslateTransform((float)preview.ClientSize.Width / 2f, (float)preview.ClientSize.Height / 2f);
																	float num5 = (float)Math.Min((double)(preview.ClientSize.Width - 34) / (num2 * 4.2), (double)(preview.ClientSize.Height - 30) / (num3 * 3.3)) * previewZoom;
																	if (!double.IsNaN(num5) && !double.IsInfinity(num5) && !(num5 <= 0f))
																	{
																		graphics.ScaleTransform(num5, num5);
																		graphics.TranslateTransform((float)((0.0 - num2) * 2.1), (float)((0.0 - num3) * 1.65));
																		using Pen pen = new Pen(gold, 1.15f / num5);
																		using SolidBrush brush = new SolidBrush(Color.FromArgb(252, 252, 250));
																		if (layout.SelectedIndex == 2)
																		{
																			graphics.RotateTransform(45f);
																		}
																		for (int i = -6; i < 8; i++)
																		{
																			double num6 = (double)i * (num3 + num4);
																			double num7 = ((layout.SelectedIndex == 1 && Math.Abs(i % 2) == 1) ? ((num2 + num4) / 2.0) : 0.0);
																			for (int j = -8; j < 10; j++)
																			{
																				double num8 = (double)j * (num2 + num4) + num7;
																				graphics.FillRectangle(brush, (float)num8, (float)num6, (float)num2, (float)num3);
																				graphics.DrawRectangle(pen, (float)num8, (float)num6, (float)num2, (float)num3);
																			}
																		}
																	}
																}
															
										graphics.ResetTransform();
										graphics.SmoothingMode = SmoothingMode.AntiAlias;
										using (Pen previewBorder = new Pen(Color.FromArgb(210, 215, 220), 1f))
										using (GraphicsPath previewBorderPath = RoundedPath(new Rectangle(0, 0, preview.ClientSize.Width - 1, preview.ClientSize.Height - 1), 7))
										{
											graphics.DrawPath(previewBorder, previewBorderPath);
										}
										};
															refreshLibrary();
															glassButton13.Click += (object param0, EventArgs param1) =>
															{
																string text = tileWidth.Text.Trim();
																string text2 = tileHeight.Text.Trim();
																string text3 = grout.Text.Trim();
																string text4 = ((layout.SelectedIndex == 1) ? "running bond" : ((layout.SelectedIndex == 2) ? "diagonal grid" : "straight grid"));
																string text5 = ((patternType.SelectedIndex == 1) ? "drafting" : "model");
																string text6 = ((layout.SelectedIndex == 1) ? "with staggered courses for a brick-like rhythm" : ((layout.SelectedIndex == 2) ? "rotated 45 degrees for a diagonal presentation" : "with aligned horizontal and vertical joints"));
																string text7 = ((patternType.SelectedIndex == 1) ? "Keep the spacing fixed on the sheet for documentation." : "Keep real-world spacing so the pattern follows the model scale.");
																description.Text = "Create a reusable " + text5 + " fill pattern using " + text + " x " + text2 + " mm tiles and a " + text3 + " mm grout joint, arranged as a " + text4 + " " + text6 + ". " + text7;
																status.ForeColor = Color.FromArgb(102, 109, 118);
																status.Text = "AI suggestion written from the current settings. Review or edit it before creating.";
																redraw();
															};
															glassButton11.Click += (object param0, EventArgs param1) =>
															{
																string text = description.Text ?? "";
																Match match = Regex.Match(text, "(\\d+(?:[\\.,]\\d+)?)\\s*[x×]\\s*(\\d+(?:[\\.,]\\d+)?)(?:\\s*(mm|millimet(?:er|re)s?|cm|m|in(?:ch(?:es)?)?|ملم|مم))?", RegexOptions.IgnoreCase);
																bool flag = match.Success;
																if (match.Success)
																{
																	Func<string, double> func = (string s) => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result2) ? result2 : 0.0;
																	double num2 = 1.0;
																	string text2 = match.Groups[3].Value.ToLowerInvariant();
																	if (text2 == "cm")
																	{
																		num2 = 10.0;
																	}
																	else if (text2 == "m")
																	{
																		num2 = 1000.0;
																	}
																	else if (text2.StartsWith("in"))
																	{
																		num2 = 25.4;
																	}
																	tileWidth.Text = (func(match.Groups[1].Value) * num2).ToString("0.##", CultureInfo.InvariantCulture);
																	tileHeight.Text = (func(match.Groups[2].Value) * num2).ToString("0.##", CultureInfo.InvariantCulture);
																}
																else
																{
																	Match match2 = Regex.Match(text, "(?:tile|بلاطة)\\s*(?:size\\s*)?(\\d+(?:[\\.,]\\d+)?)\\s*(mm|millimet(?:er|re)s?|cm|m|in(?:ch(?:es)?)?|ملم|مم)?|(\\d+(?:[\\.,]\\d+)?)\\s*(mm|millimet(?:er|re)s?|cm|m|in(?:ch(?:es)?)?|ملم|مم)\\s*(?:square\\s*)?(?:tile|بلاطة)", RegexOptions.IgnoreCase);
																	if (match2.Success)
																	{
																		string text3 = (match2.Groups[1].Success ? match2.Groups[1].Value : match2.Groups[3].Value);
																		string text2 = (match2.Groups[2].Success ? match2.Groups[2].Value : match2.Groups[4].Value).ToLowerInvariant();
																		double num2 = ((text2 == "cm") ? 10.0 : ((text2 == "m") ? 1000.0 : (text2.StartsWith("in") ? 25.4 : 1.0)));
																		if (double.TryParse(text3.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
																		{
																			tileWidth.Text = (result * num2).ToString("0.##", CultureInfo.InvariantCulture);
																			tileHeight.Text = tileWidth.Text;
																			flag = true;
																		}
																	}
																}
																Match match3 = Regex.Match(text, "(?:grout|joint|فاصل|جراوت)\\s*(?:=|:)?\\s*(\\d+(?:[\\.,]\\d+)?)\\s*(?:mm|ملم|مم)?|(\\d+(?:[\\.,]\\d+)?)\\s*(?:mm|ملم|مم)\\s*(?:grout|joint|فاصل|جراوت)", RegexOptions.IgnoreCase);
																if (match3.Success)
																{
																	grout.Text = (match3.Groups[1].Success ? match3.Groups[1].Value : match3.Groups[2].Value).Replace(',', '.');
																}
																string text4 = text.ToLowerInvariant();
																if (text4.Contains("running") || text4.Contains("brick") || text.Contains("متعاقب") || text.Contains("متداخل"))
																{
																	layout.SelectedIndex = 1;
																}
																else if (text4.Contains("diagonal") || text.Contains("قطري"))
																{
																	layout.SelectedIndex = 2;
																}
																else if (text4.Contains("straight") || text4.Contains("grid") || text.Contains("شبكي"))
																{
																	layout.SelectedIndex = 0;
																}
																if (text4.Contains("drafting") || text.Contains("تفصيلي"))
																{
																	patternType.SelectedIndex = 1;
																}
																else if (text4.Contains("model"))
																{
																	patternType.SelectedIndex = 0;
																}
																status.Text = (flag ? "Description read. Check the dimensions and preview before creating." : "Tile dimensions not found. Enter width and height below.");
																redraw();
															};
															glassButton9.Click += (object param0, EventArgs param1) =>
															{
																//IL_02df: Unknown result type (might be due to invalid IL or missing references)
																//IL_02e6: Expected Obj, but got Unknown
																//IL_02ff: Unknown result type (might be due to invalid IL or missing references)
																//IL_0306: Expected Obj, but got Unknown
																//IL_0309: Unknown result type (might be due to invalid IL or missing references)
																//IL_0324: Unknown result type (might be due to invalid IL or missing references)
																//IL_01b2: Unknown result type (might be due to invalid IL or missing references)
																//IL_01b9: Expected Obj, but got Unknown
																//IL_01f1: Unknown result type (might be due to invalid IL or missing references)
																//IL_01fb: Expected Obj, but got Unknown
																using OpenFileDialog openFileDialog = new OpenFileDialog();
																openFileDialog.Filter = "Revit pattern files (*.pat)|*.pat|All files (*.*)|*.*";
																openFileDialog.Title = "Import an external Revit pattern";
																if (openFileDialog.ShowDialog(form) != DialogResult.OK)
																{
																	return;
																}
																try
																{
																	string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
																	List<FillGrid> list = new List<FillGrid>();
																	string arg = fileNameWithoutExtension;
																	bool flag = true;
																	string[] array2 = File.ReadAllLines(openFileDialog.FileName);
																	foreach (string text in array2)
																	{
																		string text2 = (text ?? "").Trim();
																		if (text2.Length != 0 && !text2.StartsWith(";") && !text2.StartsWith("%"))
																		{
																			if (text2.StartsWith("*"))
																			{
																				int num2 = text2.IndexOf(',');
																				arg = ((num2 > 1) ? text2.Substring(1, num2 - 1) : text2.Substring(1)).Trim();
																			}
																			else
																			{
																				string[] array3 = text2.Split(',');
																				if (array3.Length >= 5 && double.TryParse(array3[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.TryParse(array3[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var result2) && double.TryParse(array3[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var result3) && double.TryParse(array3[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var result4) && double.TryParse(array3[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var result5))
																				{
																					FillGrid val = new FillGrid();
																					val.Angle = result * Math.PI / 180.0;
																					val.Origin = new UV(result2 / 12.0, result3 / 12.0);
																					val.Shift = result4 / 12.0;
																					val.Offset = result5 / 12.0;
																					FillGrid val2 = val;
																					List<double> list2 = new List<double>();
																					for (int j = 5; j < array3.Length; j++)
																					{
																						if (double.TryParse(array3[j], NumberStyles.Float, CultureInfo.InvariantCulture, out var result6))
																						{
																							list2.Add(result6 / 12.0);
																						}
																					}
																					val2.SetSegments((IList<double>)list2);
																					list.Add(val2);
																				}
																			}
																		}
																	}
																	if (list.Count == 0)
																	{
																		throw new InvalidDataException("No valid Revit pattern grid lines were found in this file.");
																	}
																	string text3 = nextAvailablePatternName(arg);
																	FillPattern val3 = new FillPattern(text3, (FillPatternTarget)(flag ? 1 : 0), (FillPatternHostOrientation)2);
																	val3.SetFillGrids((IList<FillGrid>)list);
																	Transaction val4 = new Transaction(doc, "BIMlify — Import external pattern");
																	try
																	{
																		val4.Start();
																		FillPatternElement.Create(doc, val3);
																		val4.Commit();
																	}
																	finally
																	{
																		((IDisposable)val4)?.Dispose();
																	}
																	refreshLibrary();
																	status.ForeColor = Color.FromArgb(54, 119, 75);
																	status.Text = "Imported external pattern: " + text3;
																}
																catch (Exception ex)
																{
																	status.ForeColor = Color.FromArgb(160, 70, 64);
																	status.Text = "Could not import the .PAT file: " + ex.Message;
																}
															};
															glassButton7.Click += (object param0, EventArgs param1) =>
															{
																//IL_01ba: Unknown result type (might be due to invalid IL or missing references)
																//IL_026d: Unknown result type (might be due to invalid IL or missing references)
																//IL_0274: Expected Obj, but got Unknown
																//IL_029b: Unknown result type (might be due to invalid IL or missing references)
																//IL_02a2: Expected Obj, but got Unknown
																//IL_02d4: Unknown result type (might be due to invalid IL or missing references)
																//IL_02de: Expected Obj, but got Unknown
																//IL_02fa: Unknown result type (might be due to invalid IL or missing references)
																//IL_0301: Expected Obj, but got Unknown
																//IL_032c: Unknown result type (might be due to invalid IL or missing references)
																//IL_0336: Expected Obj, but got Unknown
																//IL_0352: Unknown result type (might be due to invalid IL or missing references)
																//IL_0359: Expected Obj, but got Unknown
																//IL_03a5: Unknown result type (might be due to invalid IL or missing references)
																//IL_03af: Expected Obj, but got Unknown
																//IL_03f1: Unknown result type (might be due to invalid IL or missing references)
																//IL_03f8: Expected Obj, but got Unknown
																//IL_043c: Unknown result type (might be due to invalid IL or missing references)
																//IL_0446: Expected Obj, but got Unknown
																//IL_0488: Unknown result type (might be due to invalid IL or missing references)
																//IL_048f: Expected Obj, but got Unknown
																//IL_04d2: Unknown result type (might be due to invalid IL or missing references)
																//IL_04dc: Expected Obj, but got Unknown
																//IL_051e: Unknown result type (might be due to invalid IL or missing references)
																//IL_0525: Expected Obj, but got Unknown
																//IL_0572: Unknown result type (might be due to invalid IL or missing references)
																//IL_057c: Expected Obj, but got Unknown
																//IL_0608: Unknown result type (might be due to invalid IL or missing references)
																//IL_060f: Expected Obj, but got Unknown
																//IL_063a: Unknown result type (might be due to invalid IL or missing references)
																//IL_0644: Expected Obj, but got Unknown
																//IL_0660: Unknown result type (might be due to invalid IL or missing references)
																//IL_0667: Expected Obj, but got Unknown
																//IL_0684: Unknown result type (might be due to invalid IL or missing references)
																//IL_068e: Expected Obj, but got Unknown
																//IL_06d5: Unknown result type (might be due to invalid IL or missing references)
																//IL_06dc: Expected Obj, but got Unknown
																//IL_0710: Unknown result type (might be due to invalid IL or missing references)
																//IL_071a: Expected Obj, but got Unknown
																//IL_0736: Unknown result type (might be due to invalid IL or missing references)
																//IL_073d: Expected Obj, but got Unknown
																//IL_0763: Unknown result type (might be due to invalid IL or missing references)
																//IL_076d: Expected Obj, but got Unknown
																//IL_07a8: Unknown result type (might be due to invalid IL or missing references)
																//IL_07af: Expected Obj, but got Unknown
																//IL_07b2: Unknown result type (might be due to invalid IL or missing references)
																//IL_07d3: Unknown result type (might be due to invalid IL or missing references)
																//IL_07d9: Invalid comparison between Unknown and I4
																double num2 = getWidth();
																double num3 = getHeight();
																double num4 = getGrout();
																if (num2 <= 0.0 || num3 <= 0.0 || num4 < 0.0)
																{
																	status.Text = "Enter positive tile dimensions and a non-negative grout size.";
																}
																else
																{
																	string name = (patternName.Text ?? "").Trim();
																	if (name.Length != 0)
																	{
																		if (((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))).Cast<FillPatternElement>().Any((FillPatternElement x) => string.Equals(((Element)x).Name, name, StringComparison.OrdinalIgnoreCase)))
																		{
																			name = nextAvailablePatternName(name);
																			patternName.Text = name;
																		}
																		double num5 = UnitUtils.ConvertToInternalUnits(num2, UnitTypeId.Millimeters);
																		double num6 = UnitUtils.ConvertToInternalUnits(num3, UnitTypeId.Millimeters);
																		double num7 = UnitUtils.ConvertToInternalUnits(num4, UnitTypeId.Millimeters);
																		bool flag = patternType.SelectedIndex == 0;
																		FillPattern val = new FillPattern(name, (FillPatternTarget)(flag ? 1 : 0), (FillPatternHostOrientation)2);
																		List<FillGrid> list = new List<FillGrid>();
																		if (layout.SelectedIndex == 1)
																		{
																			FillGrid val2 = new FillGrid();
																			val2.Angle = 0.0;
																			val2.Offset = num6 + num7;
																			val2.Origin = new UV(0.0, 0.0);
																			FillGrid val3 = val2;
																			val3.SetSegments((IList<double>)new List<double>());
																			list.Add(val3);
																			FillGrid val4 = new FillGrid();
																			val4.Angle = 0.0;
																			val4.Offset = num6 + num7;
																			val4.Origin = new UV(0.0, num6);
																			FillGrid val5 = val4;
																			val5.SetSegments((IList<double>)new List<double>());
																			list.Add(val5);
																			FillGrid val6 = new FillGrid();
																			val6.Angle = Math.PI / 2.0;
																			val6.Offset = 2.0 * (num5 + num7);
																			val6.Shift = 0.0;
																			val6.Origin = new UV(0.0, 0.0);
																			FillGrid val7 = val6;
																			val7.SetSegments((IList<double>)new List<double>
																			{
																				num6,
																				0.0 - (num6 + 2.0 * num7)
																			});
																			list.Add(val7);
																			FillGrid val8 = new FillGrid();
																			val8.Angle = Math.PI / 2.0;
																			val8.Offset = 2.0 * (num5 + num7);
																			val8.Shift = 0.0;
																			val8.Origin = new UV(num5, 0.0);
																			FillGrid val9 = val8;
																			val9.SetSegments((IList<double>)new List<double>
																			{
																				num6,
																				0.0 - (num6 + 2.0 * num7)
																			});
																			list.Add(val9);
																			FillGrid val10 = new FillGrid();
																			val10.Angle = Math.PI / 2.0;
																			val10.Offset = 2.0 * (num5 + num7);
																			val10.Shift = num6 + num7;
																			val10.Origin = new UV(num5 + num7, 0.0);
																			FillGrid val11 = val10;
																			val11.SetSegments((IList<double>)new List<double>
																			{
																				num6,
																				0.0 - (num6 + 2.0 * num7)
																			});
																			list.Add(val11);
																			FillGrid val12 = new FillGrid();
																			val12.Angle = Math.PI / 2.0;
																			val12.Offset = 2.0 * (num5 + num7);
																			val12.Shift = num6 + num7;
																			val12.Origin = new UV(2.0 * num5 + num7, 0.0);
																			FillGrid val13 = val12;
																			val13.SetSegments((IList<double>)new List<double>
																			{
																				num6,
																				0.0 - (num6 + 2.0 * num7)
																			});
																			list.Add(val13);
																		}
																		else
																		{
																			double num8 = ((layout.SelectedIndex == 2) ? (Math.PI / 4.0) : 0.0);
																			double num9 = (0.0 - Math.Sin(num8)) * num6;
																			double num10 = Math.Cos(num8) * num6;
																			FillGrid val14 = new FillGrid();
																			val14.Angle = num8;
																			val14.Offset = num6 + num7;
																			val14.Origin = new UV(0.0, 0.0);
																			FillGrid val3 = val14;
																			val3.SetSegments((IList<double>)new List<double>());
																			list.Add(val3);
																			FillGrid val15 = new FillGrid();
																			val15.Angle = num8;
																			val15.Offset = num6 + num7;
																			val15.Origin = new UV(num9, num10);
																			FillGrid val5 = val15;
																			val5.SetSegments((IList<double>)new List<double>());
																			list.Add(val5);
																			double num11 = (0.0 - Math.Sin(num8 + Math.PI / 2.0)) * num5;
																			double num12 = Math.Cos(num8 + Math.PI / 2.0) * num5;
																			FillGrid val16 = new FillGrid();
																			val16.Angle = num8 + Math.PI / 2.0;
																			val16.Offset = num5 + num7;
																			val16.Origin = new UV(0.0, 0.0);
																			FillGrid val7 = val16;
																			val7.SetSegments((IList<double>)new List<double>());
																			list.Add(val7);
																			FillGrid val17 = new FillGrid();
																			val17.Angle = num8 + Math.PI / 2.0;
																			val17.Offset = num5 + num7;
																			val17.Origin = new UV(num11, num12);
																			FillGrid val11 = val17;
																			val11.SetSegments((IList<double>)new List<double>());
																			list.Add(val11);
																		}
																		val.SetFillGrids((IList<FillGrid>)list);
																		try
																		{
																			ElementId val18 = null;
																			Transaction val19 = new Transaction(doc, "BIMlify — Create tile fill pattern");
																			try
																			{
																				val19.Start();
																				val18 = ((Element)FillPatternElement.Create(doc, val)).Id;
																				if ((int)val19.Commit() != 3)
																				{
																					status.ForeColor = Color.FromArgb(160, 70, 64);
																					status.Text = "Revit did not confirm that the pattern was saved.";
																					return;
																				}
																			}
																			finally
																			{
																				((IDisposable)val19)?.Dispose();
																			}
																			if (val18 == (ElementId)null || doc.GetElement(val18) == null)
																			{
																				status.ForeColor = Color.FromArgb(160, 70, 64);
																				status.Text = "Revit did not confirm the new pattern in the project.";
																			}
																			else
																			{
																				refreshLibrary();
																				string text = nextAvailablePatternName(name);
																				patternName.Text = text;
																				patternName.SelectAll();
																				patternName.Focus();
																				status.ForeColor = Color.FromArgb(54, 119, 75);
																				status.Font = new Font("Segoe UI", 8.2f, FontStyle.Bold);
																				status.Text = "CREATED & SAVED: " + name + Environment.NewLine + "Next pattern name is ready: " + text;
																			}
																			return;
																		}
																		catch (Exception ex)
																		{
																			status.ForeColor = Color.FromArgb(160, 70, 64);
																			status.Text = "Could not create the pattern: " + ex.Message;
																			return;
																		}
																	}
																	status.ForeColor = Color.FromArgb(160, 70, 64);
																	status.Text = "Enter a name for the pattern.";
																}
															};
															form.Shown += (object param0, EventArgs param1) =>
															{
																RefreshVisibleControls(form);
																redraw();
															};
															UiPolish.ApplyPatternMaker(form);
															BIMlifyForm bIMlifyForm = form;
															EmptyState.CompactPatternMaker(bIMlifyForm);
															BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(bIMlifyForm);
														}
														finally
														{
															if (status != null)
															{
																((IDisposable)status).Dispose();
															}
														}
													}
													finally
													{
														if (zoomLabel != null)
														{
															((IDisposable)zoomLabel).Dispose();
														}
													}
												}
												finally
												{
													if (preview != null)
													{
														((IDisposable)preview).Dispose();
													}
												}
											}
											finally
											{
												if (library != null)
												{
													((IDisposable)library).Dispose();
												}
											}
										}
										finally
										{
											if (librarySearch != null)
											{
												((IDisposable)librarySearch).Dispose();
											}
										}
									}
									finally
									{
										if (patternType != null)
										{
											((IDisposable)patternType).Dispose();
										}
									}
								}
								finally
								{
									if (layout != null)
									{
										((IDisposable)layout).Dispose();
									}
								}
							}
							finally
							{
								if (grout != null)
								{
									((IDisposable)grout).Dispose();
								}
							}
						}
						finally
						{
							if (tileHeight != null)
							{
								((IDisposable)tileHeight).Dispose();
							}
						}
					}
					finally
					{
						if (tileWidth != null)
						{
							((IDisposable)tileWidth).Dispose();
						}
					}
				}
				finally
				{
					if (patternName != null)
					{
						((IDisposable)patternName).Dispose();
					}
				}
			}
			finally
			{
				if (description != null)
				{
					((IDisposable)description).Dispose();
				}
			}
		}
		finally
		{
			if (form != null)
			{
				((IDisposable)form).Dispose();
			}
		}
	}

	private static void ViewTemplates(UIDocument ui)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		Document doc = ui.Document;
		List<View> list = ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(View))).Cast<View>().Where((View v) =>
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Invalid comparison between Unknown and I4
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0019: Invalid comparison between Unknown and I4
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Invalid comparison between Unknown and I4
			return v.IsTemplate && (int)v.ViewType != 7 && (int)v.ViewType != 12 && (int)v.ViewType != 5;
		}).OrderBy((View v) => ((Element)v).Name, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
		List<View> allViews = ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(View))).Cast<View>().Where((View v) =>
		{
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Invalid comparison between Unknown and I4
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0021: Invalid comparison between Unknown and I4
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Invalid comparison between Unknown and I4
			return !v.IsTemplate && !(v is ViewSheet) && (int)v.ViewType != 7 && (int)v.ViewType != 12 && (int)v.ViewType != 5;
		}).OrderBy((View v) => ((Element)v).Name, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
		BIMlifyForm form = new BIMlifyForm();
		try
		{
			TabControl tabs = new TabControl();
			try
			{
				CheckedListBox viewList = new CheckedListBox();
				try
				{
					Color bg = Color.FromArgb(238, 241, 245);
					Color ink = Color.FromArgb(78, 85, 92);
					Color foreColor = Color.FromArgb(102, 109, 118);
					form.Text = "BIMlify — View Templates";
					form.Width = 1080;
					form.Height = 720;
					form.MinimumSize = new Size(980, 640);
					form.StartPosition = FormStartPosition.CenterScreen;
					form.ShowIcon = false;
					form.MaximizeBox = true;
					form.MinimizeBox = true;
					form.AutoScaleMode = AutoScaleMode.Dpi;
					form.BackColor = Color.FromArgb(238, 241, 245);
					form.Font = new Font("Segoe UI", 9f);
					UseSmoothCorners(form);
					Panel panel = new Panel();
					panel.Dock = DockStyle.Top;
					panel.Height = 128;
					panel.BackColor = Color.FromArgb(247, 247, 245);
					panel.Padding = new Padding(18, 9, 4, 9);
					Panel panel2 = panel;
					Label label = new Label();
					label.Text = "View Templates";
					label.Left = 18;
					label.Top = 12;
					label.Width = 650;
					label.Height = 24;
			label.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
					label.ForeColor = ink;
					label.Padding = Padding.Empty;
					label.TextAlign = ContentAlignment.MiddleLeft;
					Label value = label;
					Label label2 = new Label();
					label2.Text = "Choose a template for plans, RCPs, elevations, sections, or 3D views; then select compatible views.";
					label2.Left = 18;
			label2.Top = 38;
					label2.Width = 680;
			label2.Height = 24;
					label2.AutoSize = false;
					label2.Font = new Font("Segoe UI", 8f);
					label2.ForeColor = foreColor;
					label2.Padding = Padding.Empty;
					label2.TextAlign = ContentAlignment.TopLeft;
					Label value2 = label2;
					panel2.Controls.Add(value);
					panel2.Controls.Add(value2);
					panel2.Controls.Add(CreateBrandLockup(panel3.BackColor));
					AddYellowPanelAccent(panel2, Color.FromArgb(214, 171, 61), atTop: false);
					TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
					tableLayoutPanel.Dock = DockStyle.Fill;
					tableLayoutPanel.ColumnCount = 2;
					tableLayoutPanel.RowCount = 1;
					tableLayoutPanel.BackColor = bg;
					tableLayoutPanel.Padding = new Padding(12, 12, 12, 10);
					TableLayoutPanel tableLayoutPanel2 = tableLayoutPanel;
					tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
					tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
					Panel panel3 = new Panel();
					panel3.Dock = DockStyle.Fill;
					panel3.BackColor = Color.White;
					panel3.Margin = new Padding(6);
					panel3.Padding = new Padding(12);
					panel3.BorderStyle = BorderStyle.FixedSingle;
					Panel panel4 = panel3;
					Panel panel5 = new Panel();
					panel5.Dock = DockStyle.Fill;
					panel5.BackColor = Color.White;
					panel5.Margin = new Padding(6);
					panel5.Padding = new Padding(12);
					panel5.BorderStyle = BorderStyle.FixedSingle;
					Panel panel6 = panel5;
					Label label3 = new Label();
					label3.Text = "1. CHOOSE TEMPLATE";
					label3.Dock = DockStyle.Top;
					label3.Height = 28;
					label3.ForeColor = ink;
					label3.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
					label3.TextAlign = ContentAlignment.MiddleLeft;
					Label value3 = label3;
					Label viewsHeading = new Label
					{
						Text = "2. SELECT VIEWS",
						Dock = DockStyle.Top,
						Height = 28,
						ForeColor = ink,
						Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
						TextAlign = ContentAlignment.MiddleLeft,
						AutoSize = false,
						AutoEllipsis = false,
						UseMnemonic = false
					};
					tabs.Dock = DockStyle.Fill;
					tabs.Padding = new Point(10, 4);
					tabs.Font = new Font("Segoe UI", 9f);
					tabs.DrawMode = TabDrawMode.Normal;
					tabs.SizeMode = TabSizeMode.Fixed;
					tabs.ItemSize = new Size(86, 30);
					string[] array = new string[6] { "3D Views", "Plan Views", "RCP Views", "Elevations", "Sections", "Other Views" };
					Dictionary<string, ListBox> lists = new Dictionary<string, ListBox>();
					string[] array2 = array;
					foreach (string text in array2)
					{
						TabPage tabPage = new TabPage(text);
						tabPage.BackColor = bg;
						tabPage.Padding = new Padding(8);
						TabPage tabPage2 = tabPage;
						ListBox listBox = new ListBox();
						listBox.Dock = DockStyle.Fill;
						listBox.DisplayMember = "Name";
						listBox.IntegralHeight = false;
						listBox.SelectionMode = SelectionMode.One;
						listBox.Font = new Font("Segoe UI", 8.5f);
						listBox.BackColor = Color.White;
						listBox.ForeColor = ink;
						listBox.BorderStyle = BorderStyle.FixedSingle;
						listBox.ItemHeight = 24;
						lists[text] = listBox;
						tabPage2.Controls.Add(listBox);
						tabs.TabPages.Add(tabPage2);
					}
					foreach (View item in list)
					{
						lists[TemplateViewGroup(item)].Items.Add(item);
					}
					tabs.DrawItem += (object sender, DrawItemEventArgs e) =>
					{
						bool flag = e.Index == tabs.SelectedIndex;
						Rectangle bounds = e.Bounds;
						using (SolidBrush brush = new SolidBrush(flag ? Color.White : bg))
						{
							e.Graphics.FillRectangle(brush, bounds);
						}
						using (Font font = new Font("Segoe UI", 9f, flag ? FontStyle.Bold : FontStyle.Regular))
						{
							using SolidBrush brush2 = new SolidBrush(ink);
							using StringFormat format = new StringFormat
							{
								Alignment = StringAlignment.Center,
								LineAlignment = StringAlignment.Center,
								FormatFlags = StringFormatFlags.NoWrap,
								Trimming = StringTrimming.EllipsisCharacter
							};
							e.Graphics.DrawString(tabs.TabPages[e.Index].Text, font, brush2, bounds, format);
						}
						using (Pen pen = new Pen(Color.FromArgb(195, 198, 201), 1f))
						{
							e.Graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
						}
						if (flag)
						{
							using (Pen pen2 = new Pen(Color.FromArgb(214, 171, 61), 3f))
							{
								e.Graphics.DrawLine(pen2, bounds.Left + 2, bounds.Bottom - 2, bounds.Right - 3, bounds.Bottom - 2);
							}
						}
					};
					TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
					tableLayoutPanel3.Dock = DockStyle.Fill;
					tableLayoutPanel3.ColumnCount = 1;
					tableLayoutPanel3.RowCount = 3;
					tableLayoutPanel3.BackColor = Color.White;
					tableLayoutPanel3.Padding = Padding.Empty;
					TableLayoutPanel tableLayoutPanel4 = tableLayoutPanel3;
					tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
					tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
					tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
					Label label4 = new Label();
					label4.Text = "Find a view · Shift-click a range to check or clear";
					label4.Dock = DockStyle.Fill;
					label4.ForeColor = foreColor;
					label4.Font = new Font("Segoe UI", 8f);
					label4.TextAlign = ContentAlignment.MiddleLeft;
					Label control = label4;
					TextBox filter = new AdvancedSearchTextBox
					{
						Dock = DockStyle.Fill
					};
					StyleSearchBox(filter, Color.White, ink, "Search compatible views");
					viewList.Dock = DockStyle.Fill;
					viewList.CheckOnClick = true;
					viewList.IntegralHeight = false;
					viewList.Font = new Font("Segoe UI", 8.5f);
					viewList.BackColor = Color.White;
					viewList.ForeColor = ink;
					viewList.BorderStyle = BorderStyle.FixedSingle;
					viewList.DisplayMember = "Name";
					tableLayoutPanel4.Controls.Add(control, 0, 0);
					tableLayoutPanel4.Controls.Add(filter, 0, 1);
					tableLayoutPanel4.Controls.Add(viewList, 0, 2);
					panel4.Controls.Add(tabs);
					panel4.Controls.Add(value3);
					panel6.Controls.Add(tableLayoutPanel4);
					panel6.Controls.Add(viewsHeading);
					tableLayoutPanel2.Controls.Add(panel4, 0, 0);
					tableLayoutPanel2.Controls.Add(panel6, 1, 0);
					Panel panel7 = CreateDialogFooter();
					panel7.Padding = new Padding(12, 12, 36, 8);
					Label status = new Label
					{
						Text = "Select a template to load compatible views.",
						Dock = DockStyle.Left,
						Width = 360,
						ForeColor = ink,
						TextAlign = ContentAlignment.MiddleLeft
					};
					GlassButton selectAll = new GlassButton
					{
						Text = "Select All",
						Width = 92
					};
					GlassButton clear = new GlassButton
					{
						Text = "Clear",
						Width = 82
					};
					GlassButton glassButton = new GlassButton();
					glassButton.Text = "Import from RVT";
					glassButton.Width = 138;
					glassButton.Primary = true;
					GlassButton glassButton2 = glassButton;
					GlassButton close = new GlassButton
					{
						Text = "Close",
						Width = 88,
						DialogResult = DialogResult.Cancel
					};
					GlassButton apply = new GlassButton
					{
						Text = "Apply Template",
						Width = 126,
						Primary = true,
						Enabled = false
					};
					Panel panel8 = CreateFooterButtonGroup(rightAligned: true, 610);
					PlaceFooterButton(panel8, selectAll, 0, 92);
					PlaceFooterButton(panel8, clear, 112, 82);
					PlaceFooterButton(panel8, glassButton2, 216, 122);
					PlaceFooterButton(panel8, close, 356, 88);
					PlaceFooterButton(panel8, apply, 464, 126);
					panel7.Controls.Add(panel8);
					panel7.Controls.Add(status);
					status.AutoEllipsis = true;
					viewsHeading.AutoEllipsis = true;
					View chosenTemplate = null;
					bool completed = false;
					bool applyingRange = false;
					bool rangePending = false;
					bool rangeValue = false;
					int rangeAnchor = -1;
					int rangeStart = -1;
					int rangeEnd = -1;
					HashSet<int> checkedIds = new HashSet<int>();
					Func<ListBox> activeTemplates = () => lists[tabs.SelectedTab.Text];
					Action captureChecks = () =>
					{
						foreach (View item2 in viewList.CheckedItems.Cast<View>())
						{
							checkedIds.Add(((Element)item2).Id.IntegerValue);
						}
					};
					Action refreshViews = () =>
					{
						captureChecks();
						viewList.Items.Clear();
						rangeAnchor = -1;
						rangePending = false;
						object selectedItem = activeTemplates().SelectedItem;
						chosenTemplate = (View)((selectedItem is View) ? selectedItem : null);
						apply.Enabled = chosenTemplate != null && !completed;
						if (chosenTemplate == null)
						{
							viewsHeading.Text = "2. SELECT VIEWS";
							status.Text = "Choose a template to load compatible views.";
						}
						else
						{
							viewsHeading.Text = "2. SELECT VIEWS · " + ((Element)chosenTemplate).Name;
							List<View> list2 = allViews.Where((View v) =>
							{
								//IL_0001: Unknown result type (might be due to invalid IL or missing references)
								//IL_000c: Unknown result type (might be due to invalid IL or missing references)
								//IL_0031: Unknown result type (might be due to invalid IL or missing references)
								return v.ViewType == chosenTemplate.ViewType && SearchTextMatches(filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
							}).ToList();
							foreach (View item3 in list2)
							{
								int index = viewList.Items.Add(item3);
								if (checkedIds.Contains(((Element)item3).Id.IntegerValue))
								{
									viewList.SetItemChecked(index, value: true);
								}
							}
							status.Text = ((list2.Count == 0) ? "No compatible views match this filter." : (list2.Count + " compatible view(s) · " + viewList.CheckedItems.Count + " selected."));
						}
					};
					tabs.SelectedIndexChanged += (object param0, EventArgs param1) =>
					{
						viewList.Items.Clear();
						checkedIds.Clear();
						if (activeTemplates().Items.Count > 0)
						{
							activeTemplates().SelectedIndex = 0;
						}
						refreshViews();
					};
					foreach (KeyValuePair<string, ListBox> entry in lists)
					{
						KeyValuePair<string, ListBox> keyValuePair = entry;
						ListBox listBox = keyValuePair.Value;
						ListBox listBox2 = listBox;
						EventHandler value4 = (object param0, EventArgs param1) =>
						{
							if (tabs.SelectedTab != null)
							{
								string text2 = tabs.SelectedTab.Text;
								KeyValuePair<string, ListBox> keyValuePair2 = entry;
								if (text2 == keyValuePair2.Key)
								{
									viewList.Items.Clear();
									checkedIds.Clear();
									refreshViews();
								}
							}
						};
						listBox2.SelectedIndexChanged += value4;
					}
					filter.TextChanged += (object param0, EventArgs param1) =>
					{
						refreshViews();
					};
					viewList.MouseDown += (object sender, MouseEventArgs e) =>
					{
						int num3 = viewList.IndexFromPoint(e.Location);
						if (num3 >= 0)
						{
							if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift && rangeAnchor >= 0)
							{
								rangeStart = Math.Min(rangeAnchor, num3);
								rangeEnd = Math.Max(rangeAnchor, num3);
								rangeValue = !viewList.GetItemChecked(num3);
								rangePending = true;
							}
							else
							{
								rangeAnchor = num3;
								rangePending = false;
							}
						}
					};
					_003C_003Ec__DisplayClass451 CS_0024_003C_003E8__locals2;
					viewList.ItemCheck += (object sender, ItemCheckEventArgs e) =>
					{
						object obj = viewList.Items[e.Index];
						View val = (View)((obj is View) ? obj : null);
						if (val != null)
						{
							if (e.NewValue == CheckState.Checked)
							{
								checkedIds.Add(((Element)val).Id.IntegerValue);
							}
							else
							{
								checkedIds.Remove(((Element)val).Id.IntegerValue);
							}
						}
						if (!applyingRange && rangePending && e.Index == rangeEnd)
						{
							rangePending = false;
							rangeAnchor = rangeEnd;
							form.BeginInvoke((Action)(() =>
							{
								if (!form.IsDisposed)
								{
									applyingRange = true;
									try
									{
										for (int i = System.Runtime.CompilerServices.Unsafe.As<_003C_003Ec__DisplayClass451, int>(ref CS_0024_003C_003E8__locals2); i <= System.Runtime.CompilerServices.Unsafe.As<_003C_003Ec__DisplayClass451, int>(ref CS_0024_003C_003E8__locals2); i++)
										{
											viewList.SetItemChecked(i, System.Runtime.CompilerServices.Unsafe.As<_003C_003Ec__DisplayClass451, bool>(ref CS_0024_003C_003E8__locals2));
										}
									}
									finally
									{
										applyingRange = false;
									}
									if (chosenTemplate != null)
									{
										status.Text = allViews.Count((View v) =>
										{
											//IL_0001: Unknown result type (might be due to invalid IL or missing references)
											//IL_0011: Unknown result type (might be due to invalid IL or missing references)
											//IL_003b: Unknown result type (might be due to invalid IL or missing references)
											return v.ViewType == chosenTemplate.ViewType && SearchTextMatches(filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
										}) + " compatible · " + viewList.CheckedItems.Count + " selected";
									}
								}
							}));
						}
						else if (!applyingRange)
						{
							rangeAnchor = e.Index;
							form.BeginInvoke((Action)(() =>
							{
								if (!form.IsDisposed && chosenTemplate != null)
								{
									status.Text = allViews.Count((View v) =>
									{
										//IL_0001: Unknown result type (might be due to invalid IL or missing references)
										//IL_000c: Unknown result type (might be due to invalid IL or missing references)
										//IL_0031: Unknown result type (might be due to invalid IL or missing references)
										return v.ViewType == chosenTemplate.ViewType && SearchTextMatches(filter.Text, ((Element)v).Name, ((object)v.ViewType).ToString(), ((Element)v).Id.Value.ToString());
									}) + " compatible · " + viewList.CheckedItems.Count + " selected";
								}
							}));
						}
					};
					selectAll.Click += (object param0, EventArgs param1) =>
					{
						for (int i = 0; i < viewList.Items.Count; i++)
						{
							viewList.SetItemChecked(i, value: true);
						}
						captureChecks();
						status.Text = viewList.Items.Count + " view(s) selected.";
					};
					clear.Click += (object param0, EventArgs param1) =>
					{
						checkedIds.Clear();
						for (int i = 0; i < viewList.Items.Count; i++)
						{
							viewList.SetItemChecked(i, value: false);
						}
						status.Text = "Selection cleared.";
					};
					glassButton2.Click += (object param0, EventArgs param1) =>
					{
						//IL_0084: Unknown result type (might be due to invalid IL or missing references)
						//IL_0124: Unknown result type (might be due to invalid IL or missing references)
						//IL_012a: Expected Obj, but got Unknown
						//IL_012c: Unknown result type (might be due to invalid IL or missing references)
						//IL_0140: Unknown result type (might be due to invalid IL or missing references)
						//IL_014a: Expected Obj, but got Unknown
						//IL_014c: Unknown result type (might be due to invalid IL or missing references)
						using OpenFileDialog openFileDialog = new OpenFileDialog
						{
							Filter = "Revit projects (*.rvt)|*.rvt",
							Title = "Import View Templates from Revit project",
							CheckFileExists = true
						};
						if (openFileDialog.ShowDialog(form) != DialogResult.OK)
						{
							return;
						}
						Document val = null;
						try
						{
							status.Text = "Opening external project…";
							val = ui.Application.Application.OpenDocumentFile(openFileDialog.FileName);
							List<ElementId> list2 = (from View v in (IEnumerable)new FilteredElementCollector(val).OfClass(typeof(View))
								where v.IsTemplate
								select ((Element)v).Id).ToList();
							if (list2.Count == 0)
							{
								status.Text = "No view templates were found in the selected project.";
							}
							else
							{
								Transaction val2 = new Transaction(doc, "BIMlify — Import View Templates");
								try
								{
									val2.Start();
									ElementTransformUtils.CopyElements(val, (ICollection<ElementId>)list2, doc, (Transform)null, new CopyPasteOptions());
									val2.Commit();
								}
								finally
								{
									((IDisposable)val2)?.Dispose();
								}
								status.Text = "Imported " + list2.Count + " view template(s). Reopen or refresh the template list to use them.";
							}
						}
						catch (Exception ex)
						{
							status.Text = "Could not import templates: " + ex.Message;
						}
						finally
						{
							if (val != null)
							{
								try
								{
									val.Close(false);
								}
								catch
								{
								}
							}
						}
					};
					close.Click += (object param0, EventArgs param1) =>
					{
						if (completed)
						{
							form.Close();
						}
					};
					apply.Click += (object param0, EventArgs param1) =>
					{
						//IL_025f: Unknown result type (might be due to invalid IL or missing references)
						//IL_0265: Invalid comparison between Unknown and I4
						//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
						//IL_00d8: Expected Obj, but got Unknown
						//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
						//IL_013a: Unknown result type (might be due to invalid IL or missing references)
						//IL_0140: Invalid comparison between Unknown and I4
						//IL_0275: Unknown result type (might be due to invalid IL or missing references)
						if (completed)
						{
							form.Close();
						}
						else
						{
							captureChecks();
							object selectedItem = activeTemplates().SelectedItem;
							chosenTemplate = (View)((selectedItem is View) ? selectedItem : null);
							List<View> list2 = viewList.CheckedItems.Cast<View>().ToList();
							if (chosenTemplate == null)
							{
								status.Text = "Choose a view template first.";
							}
							else
							{
								if (list2.Count != 0)
								{
									int num3 = 0;
									int num4 = 0;
									Transaction val = null;
									try
									{
										val = new Transaction(doc, "BIMlify — Apply view template");
										val.Start();
										foreach (View item4 in list2)
										{
											try
											{
												item4.ViewTemplateId = ((Element)chosenTemplate).Id;
												num3++;
											}
											catch
											{
												num4++;
											}
										}
										if ((int)val.Commit() != 3)
										{
											status.Text = "Revit could not apply this template. Review the selection and try again.";
										}
										else
										{
											completed = true;
											apply.Text = "Done";
											apply.Enabled = true;
											close.Text = "Close";
											selectAll.Enabled = false;
											clear.Enabled = false;
											tabs.Enabled = false;
											filter.Enabled = false;
											viewList.Enabled = false;
											status.Text = "Applied '" + ((Element)chosenTemplate).Name + "' to " + num3 + " view(s)." + ((num4 > 0) ? (" Skipped: " + num4 + ".") : "");
										}
										return;
									}
									catch (Exception ex)
									{
										try
										{
											if (val != null && (int)val.GetStatus() == 1)
											{
												val.RollBack();
											}
										}
										catch
										{
										}
										status.Text = "Could not apply template: " + ex.Message;
										return;
									}
									finally
									{
										if (val != null)
										{
											val.Dispose();
										}
									}
								}
								status.Text = "Check at least one compatible view.";
							}
						}
					};
					form.Controls.Add(tableLayoutPanel2);
					form.Controls.Add(panel7);
					form.Controls.Add(panel2);
					AddWindowSidebar(form, dashboard: false);
					form.CancelButton = close;
					form.AcceptButton = apply;
					int num2 = Array.FindIndex(array, (string name) => lists[name].Items.Count > 0);
					if (num2 >= 0)
					{
						tabs.SelectedIndex = num2;
						lists[array[num2]].SelectedIndex = 0;
					}
					else
					{
						status.Text = "This project has no compatible view templates.";
					}
					refreshViews();
					BIMlifyForm bIMlifyForm = form;
					EmptyState.FitViewTemplateTabs(bIMlifyForm);
					BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(bIMlifyForm);
				}
				finally
				{
					if (viewList != null)
					{
						((IDisposable)viewList).Dispose();
					}
				}
			}
			finally
			{
				if (tabs != null)
				{
					((IDisposable)tabs).Dispose();
				}
			}
		}
		finally
		{
			if (form != null)
			{
				((IDisposable)form).Dispose();
			}
		}
	}

	private static BoundingBoxXYZ TransformBox(BoundingBoxXYZ source, Transform tr, double pad)
	{
		//IL_018b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Expected Obj, but got Unknown
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Expected Obj, but got Unknown
		//IL_0207: Unknown result type (might be due to invalid IL or missing references)
		//IL_020e: Expected Obj, but got Unknown
		//IL_020e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0215: Expected Obj, but got Unknown
		XYZ[] array = new XYZ[8];
		int num = 0;
		double[] array2 = new double[2]
		{
			source.Min.X,
			source.Max.X
		};
		foreach (double num2 in array2)
		{
			double[] array3 = new double[2]
			{
				source.Min.Y,
				source.Max.Y
			};
			foreach (double num3 in array3)
			{
				double[] array4 = new double[2]
				{
					source.Min.Z,
					source.Max.Z
				};
				foreach (double num4 in array4)
				{
					array[num++] = tr.OfPoint(source.Transform.OfPoint(new XYZ(num2, num3, num4)));
				}
			}
		}
		XYZ min = new XYZ(array.Min((XYZ p) => p.X) - pad, array.Min((XYZ p) => p.Y) - pad, array.Min((XYZ p) => p.Z) - pad);
		XYZ max = new XYZ(array.Max((XYZ p) => p.X) + pad, array.Max((XYZ p) => p.Y) + pad, array.Max((XYZ p) => p.Z) + pad);
		BoundingBoxXYZ val = new BoundingBoxXYZ();
		val.Min = min;
		val.Max = max;
		return val;
	}

	private static string SafeName(string name)
	{
		char[] array = new char[13]
		{
			'{', '}', '[', ']', '|', ';', '<', '>', '?', '`',
			'~', '/', '\\'
		};
		foreach (char oldChar in array)
		{
			name = name.Replace(oldChar, '-');
		}
		return (name.Length > 120) ? name.Substring(0, 120) : name;
	}

	private static string UniqueViewName(Document doc, string proposed)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		HashSet<string> hashSet = new HashSet<string>(from View v in (IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(View))
			select ((Element)v).Name, StringComparer.OrdinalIgnoreCase);
		if (!hashSet.Contains(proposed))
		{
			return proposed;
		}
		int num = 2;
		string text;
		while (true)
		{
			bool flag = true;
			text = proposed + " - " + num;
			if (!hashSet.Contains(text))
			{
				break;
			}
			num++;
		}
		return text;
	}

	private static void CreateWetArea3D(UIDocument ui)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Invalid comparison between Unknown and I4
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Invalid comparison between Unknown and I4
		//IL_02e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0351: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_0219: Unknown result type (might be due to invalid IL or missing references)
		//IL_0220: Expected Obj, but got Unknown
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0223: Unknown result type (might be due to invalid IL or missing references)
		//IL_0256: Unknown result type (might be due to invalid IL or missing references)
		Document document = ui.Document;
		TaskDialogResult val = TaskDialog.Show("3D Room", "Where is the room?\n\nYes — Current model\nNo — Linked model", (TaskDialogCommonButtons)14);
		if ((int)val == 2)
		{
			return;
		}
		Room val2 = null;
		Transform tr = Transform.Identity;
		string text = "Current model";
		if ((int)val == 6)
		{
			Reference val3 = ui.Selection.PickObject((ObjectType)1, (ISelectionFilter)(object)new HostRoomFilter(), "Select a room in the current model.");
			Element element = document.GetElement(val3.ElementId);
			val2 = (Room)(object)((element is Room) ? element : null);
		}
		else
		{
			Reference val3 = ui.Selection.PickObject((ObjectType)5, (ISelectionFilter)(object)new RoomLinkFilter(), "Select a room in a linked model.");
			Element element2 = document.GetElement(val3.ElementId);
			RevitLinkInstance val4 = (RevitLinkInstance)(object)((element2 is RevitLinkInstance) ? element2 : null);
			if (val4 == null || val4.GetLinkDocument() == null)
			{
				TaskDialog.Show("BIMlify", "The selected link is unavailable.");
				return;
			}
			Element element3 = val4.GetLinkDocument().GetElement(val3.LinkedElementId);
			val2 = (Room)(object)((element3 is Room) ? element3 : null);
			tr = ((Instance)val4).GetTotalTransform();
			text = "Linked model";
		}
		if (val2 == null)
		{
			TaskDialog.Show("BIMlify", "The selected element is not a room.");
			return;
		}
		BoundingBoxXYZ val5 = ((Element)val2)[(View)null];
		if (val5 == null)
		{
			TaskDialog.Show("BIMlify", "Room boundary box is unavailable.");
			return;
		}
		BoundingBoxXYZ sectionBox = TransformBox(val5, tr, 1.0);
		ViewFamilyType val6 = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))).Cast<ViewFamilyType>().FirstOrDefault((ViewFamilyType x) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Invalid comparison between Unknown and I4
			return (int)x.ViewFamily == 102;
		});
		if (val6 == null)
		{
			TaskDialog.Show("BIMlify", "No 3D view family type was found.");
			return;
		}
		string name = UniqueViewName(document, SafeName("BIMlify - " + ((SpatialElement)val2).Number + " - " + ((Element)val2).Name + " - 3D ROOM"));
		Transaction val7 = new Transaction(document, "BIMlify — Create room 3D view");
		try
		{
			val7.Start();
			View3D val8 = View3D.CreateIsometric(document, ((Element)val6).Id);
			((Element)val8).Name = name;
			val8.SetSectionBox(sectionBox);
			val8.IsSectionBoxActive = true;
			View val9 = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(View))).Cast<View>().FirstOrDefault((View x) =>
			{
				//IL_001d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0023: Invalid comparison between Unknown and I4
				return x.IsTemplate && ((Element)x).Name.IndexOf("AMWF 3D SECTIONS", StringComparison.OrdinalIgnoreCase) >= 0 && (int)x.ViewType == 4;
			});
			if (val9 != null)
			{
				try
				{
					((View)val8).ViewTemplateId = ((Element)val9).Id;
				}
				catch
				{
				}
			}
			try
			{
				((View)val8).DetailLevel = (ViewDetailLevel)3;
			}
			catch
			{
			}
			try
			{
				((View)val8).DisplayStyle = (DisplayStyle)4;
			}
			catch
			{
			}
			val7.Commit();
		}
		finally
		{
			((IDisposable)val7)?.Dispose();
		}
		TaskDialog.Show("BIMlify", "3D room view created for " + ((SpatialElement)val2).Number + " — " + ((Element)val2).Name + " (" + text + ").");
	}

	private static ViewPlan CreatePlanView(Document doc, Level level, BoundingBoxXYZ modelBounds, string name)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0234: Unknown result type (might be due to invalid IL or missing references)
		//IL_023b: Expected Obj, but got Unknown
		//IL_0157: Unknown result type (might be due to invalid IL or missing references)
		//IL_0161: Expected Obj, but got Unknown
		//IL_02cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d2: Expected Obj, but got Unknown
		ViewFamilyType val = ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))).Cast<ViewFamilyType>().FirstOrDefault((ViewFamilyType x) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Invalid comparison between Unknown and I4
			return (int)x.ViewFamily == 109;
		});
		if (val == null)
		{
			throw new InvalidOperationException("No floor plan view type was found.");
		}
		ViewPlan val2 = ViewPlan.Create(doc, ((Element)val).Id, ((Element)level).Id);
		((Element)val2).Name = UniqueViewName(doc, SafeName(name));
		BoundingBoxXYZ cropBox = ((View)val2).CropBox;
		Transform inverse = cropBox.Transform.Inverse;
		XYZ[] array = new XYZ[8];
		int num = 0;
		double[] array2 = new double[2]
		{
			modelBounds.Min.X,
			modelBounds.Max.X
		};
		foreach (double num3 in array2)
		{
			double[] array3 = new double[2]
			{
				modelBounds.Min.Y,
				modelBounds.Max.Y
			};
			foreach (double num5 in array3)
			{
				double[] array4 = new double[2]
				{
					modelBounds.Min.Z,
					modelBounds.Max.Z
				};
				foreach (double num7 in array4)
				{
					array[num++] = inverse.OfPoint(new XYZ(num3, num5, num7));
				}
			}
		}
		XYZ min = new XYZ(array.Min((XYZ p) => p.X) - 2.0, array.Min((XYZ p) => p.Y) - 2.0, array.Min((XYZ p) => p.Z) - 2.0);
		XYZ max = new XYZ(array.Max((XYZ p) => p.X) + 2.0, array.Max((XYZ p) => p.Y) + 2.0, array.Max((XYZ p) => p.Z) + 2.0);
		cropBox.Min = min;
		cropBox.Max = max;
		((View)val2).CropBox = cropBox;
		((View)val2).CropBoxActive = true;
		((View)val2).CropBoxVisible = true;
		return val2;
	}

	private static Level FindPlanLevel(Document host, Element source, Transform transform)
	{
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Expected Obj, but got Unknown
		Element element = source.Document.GetElement(source.LevelId);
		Level val = (Level)(object)((element is Level) ? element : null);
		BoundingBoxXYZ val2 = source[(View)null];
		if (val == null && val2 == null)
		{
			return null;
		}
		double elevation = ((val == null) ? val2.Min.Z : val.Elevation);
		if (transform != null && val != null)
		{
			elevation = transform.OfPoint(new XYZ(0.0, 0.0, elevation)).Z;
		}
		return (from Level x in (IEnumerable)new FilteredElementCollector(host).OfClass(typeof(Level))
			orderby Math.Abs(x.Elevation - elevation)
			select x).FirstOrDefault();
	}

	private static BoundingBoxXYZ CombineBoxes(IEnumerable<Element> elements, Transform transform, double padding)
	{
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Expected Obj, but got Unknown
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Expected Obj, but got Unknown
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Expected Obj, but got Unknown
		List<BoundingBoxXYZ> list = (from e in elements
			select e[(View)null] into b
			where b != null
			select TransformBox(b, transform ?? Transform.Identity, 0.0)).ToList();
		if (list.Count == 0)
		{
			return null;
		}
		BoundingBoxXYZ val = new BoundingBoxXYZ();
		val.Min = new XYZ(list.Min((BoundingBoxXYZ b) => b.Min.X) - padding, list.Min((BoundingBoxXYZ b) => b.Min.Y) - padding, list.Min((BoundingBoxXYZ b) => b.Min.Z) - padding);
		val.Max = new XYZ(list.Max((BoundingBoxXYZ b) => b.Max.X) + padding, list.Max((BoundingBoxXYZ b) => b.Max.Y) + padding, list.Max((BoundingBoxXYZ b) => b.Max.Z) + padding);
		return val;
	}

	private static void CreateRoomPlan(UIDocument ui)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Invalid comparison between Unknown and I4
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Invalid comparison between Unknown and I4
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_01aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Expected Obj, but got Unknown
		//IL_0186: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0205: Unknown result type (might be due to invalid IL or missing references)
		//IL_0289: Unknown result type (might be due to invalid IL or missing references)
		Document document = ui.Document;
		Room val = null;
		Transform val2 = Transform.Identity;
		string text = "Current model";
		TaskDialogResult val3 = TaskDialog.Show("Wet Areas Plan", "Where is the room?\n\nYes — Current model\nNo — Linked model", (TaskDialogCommonButtons)14);
		if ((int)val3 == 2)
		{
			return;
		}
		if ((int)val3 == 6)
		{
			Reference val4 = ui.Selection.PickObject((ObjectType)1, (ISelectionFilter)(object)new HostRoomFilter(), "Select a room for the wet areas plan.");
			Element element = document.GetElement(val4.ElementId);
			val = (Room)(object)((element is Room) ? element : null);
		}
		else
		{
			Reference val4 = ui.Selection.PickObject((ObjectType)5, (ISelectionFilter)(object)new RoomLinkFilter(), "Select a room in a linked model.");
			Element element2 = document.GetElement(val4.ElementId);
			RevitLinkInstance val5 = (RevitLinkInstance)(object)((element2 is RevitLinkInstance) ? element2 : null);
			if (val5 == null || val5.GetLinkDocument() == null)
			{
				TaskDialog.Show("BIMlify", "The selected link is unavailable.");
				return;
			}
			Element element3 = val5.GetLinkDocument().GetElement(val4.LinkedElementId);
			val = (Room)(object)((element3 is Room) ? element3 : null);
			val2 = ((Instance)val5).GetTotalTransform();
			text = "Linked model";
		}
		if (val == null)
		{
			TaskDialog.Show("BIMlify", "The selected element is not a room.");
			return;
		}
		BoundingBoxXYZ val6 = ((Element)val)[(View)null];
		if (val6 == null)
		{
			TaskDialog.Show("BIMlify", "Room boundary box is unavailable.");
			return;
		}
		Level val7 = FindPlanLevel(document, (Element)(object)val, (text == "Linked model") ? val2 : null);
		if (val7 == null)
		{
			TaskDialog.Show("BIMlify", "No matching host level was found for this room.");
			return;
		}
		BoundingBoxXYZ modelBounds = TransformBox(val6, val2, 2.0);
		Transaction val8 = new Transaction(document, "BIMlify — Create wet areas plan");
		try
		{
			val8.Start();
			CreatePlanView(document, val7, modelBounds, "BIMlify - " + ((SpatialElement)val).Number + " - " + ((Element)val).Name + " - WET AREAS PLAN");
			val8.Commit();
		}
		finally
		{
			((IDisposable)val8)?.Dispose();
		}
		TaskDialog.Show("BIMlify", "Wet areas plan created for " + ((SpatialElement)val).Number + " — " + ((Element)val).Name + " on " + ((Element)val7).Name + " (" + text + ").");
	}

	private static void CreateBlockworkPlan(UIDocument ui)
	{
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Expected Obj, but got Unknown
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_024e: Unknown result type (might be due to invalid IL or missing references)
		Document doc = ui.Document;
		IList<Reference> source = ui.Selection.PickObjects((ObjectType)1, (ISelectionFilter)(object)new BlockworkWallFilter(), "Select the blockwork walls to include in the plan, then click Finish.");
		List<Wall> list = (from w in source.Select((Reference r) =>
			{
				Element element = doc.GetElement(r.ElementId);
				return (Wall)(object)((element is Wall) ? element : null);
			})
			where w != null
			select w).ToList();
		if (list.Count == 0)
		{
			return;
		}
		var list2 = (from w in list
			select new
			{
				Wall = w,
				Level = FindPlanLevel(doc, (Element)(object)w, null)
			} into x
			where x.Level != null
			group x by ((Element)x.Level).Id.IntegerValue).ToList();
		if (list2.Count == 0)
		{
			TaskDialog.Show("BIMlify", "No matching level was found for the selected walls.");
			return;
		}
		int num = 0;
		Transaction val = new Transaction(doc, "BIMlify — Create blockwork plan");
		try
		{
			val.Start();
			foreach (var item in list2)
			{
				Level level = item.First().Level;
				BoundingBoxXYZ val2 = CombineBoxes(item.Select(x => (Element)(object)x.Wall), Transform.Identity, 2.0);
				if (val2 != null)
				{
					CreatePlanView(doc, level, val2, "BIMlify - " + ((Element)level).Name + " - BLOCKWORK PLAN");
					num++;
				}
			}
			val.Commit();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		TaskDialog.Show("BIMlify", "Created " + num + " blockwork plan(s) for " + list.Count + " selected wall(s).");
	}

	private static ElementId FindSolidFill(Document doc)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		foreach (FillPatternElement item in ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))).Cast<FillPatternElement>())
		{
			try
			{
				if (item.GetFillPattern().IsSolidFill)
				{
					return ((Element)item).Id;
				}
			}
			catch
			{
			}
		}
		return ElementId.InvalidElementId;
	}

	private static void ApplyLight3DColors(View view, ElementId solidFill)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected Obj, but got Unknown
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected Obj, but got Unknown
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected Obj, but got Unknown
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Expected Obj, but got Unknown
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Expected Obj, but got Unknown
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Expected Obj, but got Unknown
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Expected Obj, but got Unknown
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Expected Obj, but got Unknown
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Expected Obj, but got Unknown
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Expected Obj, but got Unknown
		Dictionary<BuiltInCategory, Autodesk.Revit.DB.Color> dictionary = new Dictionary<BuiltInCategory, Autodesk.Revit.DB.Color>();
		dictionary.Add((BuiltInCategory)(-2000011), new Autodesk.Revit.DB.Color((byte)205, (byte)216, (byte)222));
		dictionary.Add((BuiltInCategory)(-2000032), new Autodesk.Revit.DB.Color((byte)229, (byte)222, (byte)204));
		dictionary.Add((BuiltInCategory)(-2000038), new Autodesk.Revit.DB.Color((byte)229, (byte)232, (byte)235));
		dictionary.Add((BuiltInCategory)(-2000023), new Autodesk.Revit.DB.Color((byte)216, (byte)226, (byte)215));
		dictionary.Add((BuiltInCategory)(-2000014), new Autodesk.Revit.DB.Color((byte)207, (byte)224, (byte)232));
		dictionary.Add((BuiltInCategory)(-2001160), new Autodesk.Revit.DB.Color((byte)205, (byte)222, (byte)229));
		dictionary.Add((BuiltInCategory)(-2001000), new Autodesk.Revit.DB.Color((byte)228, (byte)218, (byte)202));
		dictionary.Add((BuiltInCategory)(-2000151), new Autodesk.Revit.DB.Color((byte)221, (byte)224, (byte)218));
		Dictionary<BuiltInCategory, Autodesk.Revit.DB.Color> dictionary2 = dictionary;
		foreach (KeyValuePair<BuiltInCategory, Autodesk.Revit.DB.Color> item in dictionary2)
		{
			try
			{
				ElementId val = new ElementId((int)item.Key);
				OverrideGraphicSettings categoryOverrides = view.GetCategoryOverrides(val);
				Autodesk.Revit.DB.Color val2 = new Autodesk.Revit.DB.Color((byte)65, (byte)72, (byte)76);
				categoryOverrides.SetProjectionLineColor(val2);
				categoryOverrides.SetCutLineColor(val2);
				if (solidFill != ElementId.InvalidElementId)
				{
					categoryOverrides.SetSurfaceForegroundPatternId(solidFill);
					categoryOverrides.SetSurfaceForegroundPatternColor(item.Value);
					categoryOverrides.SetSurfaceForegroundPatternVisible(true);
					categoryOverrides.SetCutForegroundPatternId(solidFill);
					categoryOverrides.SetCutForegroundPatternColor(item.Value);
					categoryOverrides.SetCutForegroundPatternVisible(true);
				}
				view.SetCategoryOverrides(val, categoryOverrides);
			}
			catch
			{
			}
		}
	}

	private static void Style3D(UIDocument ui)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Expected Obj, but got Unknown
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0269: Unknown result type (might be due to invalid IL or missing references)
		Document document = ui.Document;
		List<View3D> objects = (from View3D v in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(View3D))
			where !((View)v).IsTemplate
			orderby ((Element)v).Name
			select v).ToList();
		List<View3D> list = SelectMultiple("Choose 3D views", objects, (View3D v) => ((Element)v).Name + " — ID " + ((Element)v).Id.IntegerValue);
		if (list.Count == 0)
		{
			return;
		}
		View3D val = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(View3D))).Cast<View3D>().FirstOrDefault((View3D v) => ((View)v).IsTemplate && string.Equals(((Element)v).Name, "* AMWF 3D SECTIONS", StringComparison.OrdinalIgnoreCase));
		ElementId solidFill = FindSolidFill(document);
		int num = 0;
		int num2 = 0;
		Transaction val2 = new Transaction(document, "BIMlify — Apply 3D presentation");
		try
		{
			val2.Start();
			if (val != null)
			{
				try
				{
					((View)val).DetailLevel = (ViewDetailLevel)3;
				}
				catch
				{
				}
				try
				{
					((View)val).DisplayStyle = (DisplayStyle)4;
				}
				catch
				{
				}
				ApplyLight3DColors((View)(object)val, solidFill);
			}
			foreach (View3D item in list)
			{
				try
				{
					if (val != null)
					{
						((View)item).ViewTemplateId = ((Element)val).Id;
					}
					else
					{
						try
						{
							((View)item).DetailLevel = (ViewDetailLevel)3;
						}
						catch
						{
						}
						try
						{
							((View)item).DisplayStyle = (DisplayStyle)4;
						}
						catch
						{
						}
						ApplyLight3DColors((View)(object)item, solidFill);
					}
					num++;
				}
				catch
				{
					num2++;
				}
			}
			val2.Commit();
		}
		finally
		{
			((IDisposable)val2)?.Dispose();
		}
		TaskDialog.Show("BIMlify", "Updated " + num + " 3D view(s); skipped: " + num2 + ".\nFine detail, visible edges, and soft category colors were applied. Existing section boxes were preserved." + ((val == null) ? "\nTemplate not found; styling was applied directly." : ("\nTemplate applied: " + ((Element)val).Name)));
	}
}






