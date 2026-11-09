using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BIMlify.ReviewRunGate;

public static class Entry
{
	private sealed class PromptFooter
	{
		internal Panel Panel;

		internal Button Cancel;

		internal Button Proceed;
	}

	private sealed class WindowOwner : IWin32Window
	{
		public IntPtr Handle { get; private set; }

		public WindowOwner(IntPtr handle)
		{
			Handle = handle;
		}
	}

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	public static bool Confirm()
	{
		return ConfirmPrompt("BIMlify — Review & Clean", "Continue scanning?", "BIMlify will scan the selected category before opening Review & Clean.", compact: false);
	}

	public static bool ConfirmSmartCleaner()
	{
		return ConfirmPrompt("BIMlify — Smart Cleaner", "Scan model now?", "BIMlify will scan unused items and model warnings before showing the Smart Cleaner results.", compact: true);
	}

	public static bool ConfirmSmartSheets()
	{
		return ConfirmPrompt("BIMlify — Smart Sheets", "Scan project sheets?", "BIMlify will scan the project sheets before opening Smart Sheets.", compact: true);
	}

	private static bool ConfirmPrompt(string title, string heading, string messageText, bool compact)
	{
		Form dialog = CreateDialog();
		try
		{
			dialog.Text = title;
			dialog.StartPosition = FormStartPosition.CenterScreen;
			dialog.ShowInTaskbar = false;
			dialog.MaximizeBox = true;
			dialog.MinimizeBox = true;
			dialog.ControlBox = true;
			dialog.Width = (compact ? 480 : 600);
			dialog.Height = (compact ? 320 : 430);
			dialog.MinimumSize = (compact ? new Size(460, 300) : new Size(560, 410));
			dialog.BackColor = Color.FromArgb(238, 241, 245);
			dialog.Font = new Font("Segoe UI", 9f);
			dialog.AutoScaleMode = AutoScaleMode.Dpi;
			Panel body = new Panel
			{
				Dock = DockStyle.Fill,
				BackColor = Color.FromArgb(238, 241, 245),
				Padding = new Padding(24, 14, 82, 12)
			};
			Label instruction = new Label
			{
				Text = heading,
				AutoSize = false,
				Bounds = new Rectangle(24, 20, 430, 24),
				Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = Color.FromArgb(78, 85, 92),
				Font = new Font("Segoe UI", 9.2f, FontStyle.Bold)
			};
			Label message = new Label
			{
				Text = messageText,
				AutoSize = false,
				Bounds = new Rectangle(24, 54, 350, 48),
				Anchor = (AnchorStyles.Top | AnchorStyles.Left),
				TextAlign = ContentAlignment.TopLeft,
				ForeColor = Color.FromArgb(102, 109, 118),
				Font = new Font("Segoe UI", 8f)
			};
			body.Controls.Add(instruction);
			body.Controls.Add(message);
			AddSettingsLogo(body);
			Action layoutBody = () =>
			{
				int width = Math.Max(220, body.ClientSize.Width - 128);
				instruction.Width = width;
				message.Width = width;
			};
			body.Resize += (object s, EventArgs e) =>
			{
				layoutBody();
			};
			layoutBody();
			PromptFooter promptFooter = CreateSettingsFooter(dialog);
			promptFooter.Proceed.DialogResult = DialogResult.None;
			promptFooter.Cancel.DialogResult = DialogResult.None;
			promptFooter.Proceed.Click += (object s, EventArgs e) =>
			{
				dialog.DialogResult = DialogResult.OK;
				dialog.Close();
			};
			promptFooter.Cancel.Click += (object s, EventArgs e) =>
			{
				dialog.DialogResult = DialogResult.Cancel;
				dialog.Close();
			};
			dialog.Controls.Add(body);
			dialog.Controls.Add(promptFooter.Panel);
			AddSharedTitleBar(dialog);
			ApplyPrimaryContinue(promptFooter.Proceed);
			dialog.Shown += (object s, EventArgs e) => ApplyPrimaryContinue(promptFooter.Proceed);
			dialog.Activated += (object s, EventArgs e) => ApplyPrimaryContinue(promptFooter.Proceed);
			dialog.AcceptButton = promptFooter.Proceed;
			dialog.CancelButton = promptFooter.Cancel;
			IntPtr foregroundWindow = GetForegroundWindow();
			return BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(dialog) == DialogResult.OK;
		}
		finally
		{
			if (dialog != null)
			{
				((IDisposable)dialog).Dispose();
			}
		}
	}

	private static Form CreateDialog()
	{
		try
		{
			Type type = (from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
			Type type2 = type?.GetNestedType("BIMlifyForm", BindingFlags.NonPublic);
			Form form = ((type2 == null) ? null : (Activator.CreateInstance(type2, nonPublic: true) as Form));
			if (form == null)
			{
				return CreateFallbackDialog();
			}
			form.Text = "BIMlify — Review & Clean";
			form.Tag = "review-confirmation";
			form.FormBorderStyle = FormBorderStyle.None;
			form.ShowIcon = false;
			type2.GetProperty("AllowResize", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, false, null);
			type.GetMethod("UseSmoothCorners", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, new object[1] { form });
			return form;
		}
		catch
		{
			return CreateFallbackDialog();
		}
	}

	private static Form CreateFallbackDialog()
	{
		return new Form
		{
			Text = "BIMlify — Review & Clean",
			FormBorderStyle = FormBorderStyle.FixedDialog,
			ShowIcon = false,
			MaximizeBox = false,
			MinimizeBox = false
		};
	}

	private static void AddSharedTitleBar(Form dialog)
	{
		MethodInfo methodInfo = FindActionsType()?.GetMethod("AddWindowTitleBar", BindingFlags.Static | BindingFlags.NonPublic);
		if (methodInfo != null)
		{
			methodInfo.Invoke(null, new object[1] { dialog });
		}
	}

	private static void AddSettingsLogo(Panel body)
	{
		try
		{
			string text = FindActionsType()?.Assembly.Location;
			string text2 = (string.IsNullOrEmpty(text) ? null : Path.Combine(Path.GetDirectoryName(text), "Branding", "LogoMarkOnlyGoldTransparent.png"));
			if (!string.IsNullOrEmpty(text2) && File.Exists(text2))
			{
				PictureBox mark = new PictureBox
				{
					Size = new Size(40, 52),
					SizeMode = PictureBoxSizeMode.Zoom,
					BackColor = body.BackColor,
					Image = Image.FromFile(text2)
				};
				mark.Anchor = AnchorStyles.Top | AnchorStyles.Right;
				Action position = () =>
				{
					mark.SetBounds(Math.Max(0, body.ClientSize.Width - 40 - 16), 16, 40, 52);
					mark.Visible = true;
					mark.BringToFront();
				};
				body.Controls.Add(mark);
				body.Resize += (object s, EventArgs e) =>
				{
					position();
				};
				mark.Disposed += (object s, EventArgs e) =>
				{
					Image image = mark.Image;
					mark.Image = null;
					image?.Dispose();
				};
				position();
			}
		}
		catch
		{
		}
	}

	private static PromptFooter CreateSettingsFooter(Form dialog)
	{
		Panel panel = null;
		Panel panel2 = null;
		Button button = null;
		Button button2 = null;
		try
		{
			Type type = FindActionsType();
			panel = (type?.GetMethod("CreateDialogFooter", BindingFlags.Static | BindingFlags.NonPublic))?.Invoke(null, null) as Panel;
			panel2 = (type?.GetMethod("CreateFooterButtonGroup", BindingFlags.Static | BindingFlags.NonPublic))?.Invoke(null, new object[2] { true, 226 }) as Panel;
			Type type2 = type?.GetNestedType("GlassButton", BindingFlags.NonPublic);
			button = ((type2 == null) ? null : (Activator.CreateInstance(type2, nonPublic: true) as Button));
			button2 = ((type2 == null) ? null : (Activator.CreateInstance(type2, nonPublic: true) as Button));
			MethodInfo methodInfo = type?.GetMethod("PlaceFooterButton", BindingFlags.Static | BindingFlags.NonPublic);
			if (panel != null && panel2 != null && button != null && button2 != null && methodInfo != null)
			{
				button.Text = "Cancel";
				button.Width = 96;
				button.DialogResult = DialogResult.None;
				button2.Text = "Continue";
				button2.Name = "BIMlifyConfirmContinue";
				button2.Width = 96;
				button2.DialogResult = DialogResult.None;
				button2.Enabled = true;
				button2.UseVisualStyleBackColor = false;
				methodInfo.Invoke(null, new object[4] { panel2, button2, 0, 96 });
				methodInfo.Invoke(null, new object[4] { panel2, button, 112, 96 });
				panel.Controls.Add(panel2);
				ApplyPrimaryContinue(button2);
				return new PromptFooter
				{
					Panel = panel,
					Cancel = button,
					Proceed = button2
				};
			}
		}
		catch
		{
		}
		if (panel == null)
		{
			panel = new Panel
			{
				Dock = DockStyle.Bottom,
				Height = 58,
				BackColor = Color.FromArgb(216, 221, 227),
				Padding = new Padding(12, 12, 12, 8)
			};
		}
		button = button ?? CreateFallbackButton("Cancel", DialogResult.None, primary: false);
		button2 = button2 ?? CreateFallbackButton("Continue", DialogResult.None, primary: true);
		button2.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		button2.SetBounds(Math.Max(12, dialog.ClientSize.Width - 234), 14, 96, 30);
		button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		button.SetBounds(Math.Max(12, dialog.ClientSize.Width - 122), 14, 96, 30);
		panel.Controls.Add(button);
		panel.Controls.Add(button2);
		return new PromptFooter
		{
			Panel = panel,
			Cancel = button,
			Proceed = button2
		};
	}

	private static void ApplyPrimaryContinue(Button button)
	{
		if (button == null || button.IsDisposed) return;
		button.Name = "BIMlifyConfirmContinue";
		button.Enabled = true;
		button.UseVisualStyleBackColor = false;
		button.ForeColor = Color.FromArgb(78, 85, 92);
		Type type = button.GetType();
		try { type.GetProperty("Primary", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(button, true, null); } catch { }
		try { type.GetField("primary", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(button, true); } catch { }
		button.Invalidate();
	}

	private static Button CreateFallbackButton(string text, DialogResult result, bool primary)
	{
		Button button = new Button();
		button.Text = text;
		button.DialogResult = result;
		button.Size = new Size(96, 30);
		button.FlatStyle = FlatStyle.Flat;
		button.UseVisualStyleBackColor = false;
		button.Font = new Font("Segoe UI", 8.5f, primary ? FontStyle.Bold : FontStyle.Regular);
		button.BackColor = (primary ? Color.FromArgb(239, 205, 120) : Color.FromArgb(216, 221, 227));
		button.ForeColor = Color.FromArgb(67, 75, 82);
		button.FlatAppearance.BorderColor = (primary ? Color.FromArgb(198, 151, 42) : Color.FromArgb(176, 184, 192));
		return button;
	}

	private static Type FindActionsType()
	{
		return (from a in AppDomain.CurrentDomain.GetAssemblies()
			select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
	}
}

