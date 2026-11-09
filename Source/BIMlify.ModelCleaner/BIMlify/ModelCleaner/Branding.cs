using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace BIMlify.ModelCleaner;

public static class Branding
{
	public static Control Create(Color background, string description)
	{
		string path = Path.Combine(Path.GetDirectoryName(typeof(Branding).Assembly.Location), "Branding");
		bool flag = !string.IsNullOrEmpty(description);
		// Tool-window logo lockups use the shared page-header surface. The dashboard
		// lockup includes a description and keeps its own existing background.
		if (!flag)
		{
			background = Color.FromArgb(247, 247, 245);
		}
		string text = Path.Combine(path, flag ? "LogoDashboardOriginalTransparent.png" : "LogoMarkWordmarkGoldTransparent.png");
		Panel panel = new Panel
		{
			Width = (flag ? 610 : 132),
			Height = (flag ? 138 : 116),
			Dock = (flag ? DockStyle.Left : DockStyle.Right),
			BackColor = background
		};
		panel.HandleCreated += delegate
		{
			using GraphicsPath path2 = RoundedPath(new Rectangle(0, 0, panel.Width - 1, panel.Height - 1), 10);
			panel.Region = new Region(path2);
		};
		PictureBox pictureBox = new PictureBox
		{
			Left = (flag ? 20 : 4),
			Top = ((!flag) ? 14 : 0),
			Width = (flag ? 146 : 108),
			Height = (flag ? 138 : 88),
			SizeMode = PictureBoxSizeMode.Zoom,
			BackColor = background,
			Margin = Padding.Empty
		};
		if (File.Exists(text))
		{
			using Image original = Image.FromFile(text);
			pictureBox.Image = new Bitmap(original);
		}
		panel.Controls.Add(pictureBox);
		if (!flag)
		{
			return panel;
		}
		panel.Controls.Add(new Panel
		{
			Left = 192,
			Top = 54,
			Width = 1,
			Height = 44,
			BackColor = Color.FromArgb(224, 225, 223)
		});
		RichTextBox richTextBox = new RichTextBox
		{
			Left = 218,
			Top = 54,
			Width = 382,
			Height = 48,
			BorderStyle = BorderStyle.None,
			ReadOnly = true,
			DetectUrls = false,
			TabStop = false,
			ScrollBars = RichTextBoxScrollBars.None,
			WordWrap = false,
			Font = new Font("Segoe UI", 8.5f),
			ForeColor = Color.FromArgb(78, 85, 92),
			BackColor = background,
			Margin = Padding.Empty,
			ShortcutsEnabled = false
		};
		string[] array = description.Replace("\r", "").Split('\n');
		string text2 = ((array.Length != 0) ? array[0] : description);
		string value = ((array.Length > 1) ? array[1] : string.Empty);
		int num = text2.IndexOf("simplify", StringComparison.CurrentCultureIgnoreCase);
		string text3 = ((num < 0) ? RtfEscape(text2) : (RtfEscape(text2.Substring(0, num)) + "\\cf2\\i " + RtfEscape(text2.Substring(num, "simplify".Length)) + "\\i0\\cf1 " + RtfEscape(text2.Substring(num + "simplify".Length))));
		richTextBox.Rtf = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}}{\\colortbl;\\red78\\green85\\blue92;\\red198\\green151\\blue42;}\\viewkind4\\uc1\\f0\\fs17\\cf1 " + text3 + "\\line\\cf2\\i " + RtfEscape(value) + "\\i0}";
		panel.Controls.Add(richTextBox);
		return panel;
	}

	private static string RtfEscape(string value)
	{
		return (value ?? string.Empty).Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
			.Replace("\n", "\\line ");
	}

	private static GraphicsPath RoundedPath(Rectangle rect, int radius)
	{
		int num = radius * 2;
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(rect.Left, rect.Top, num, num, 180f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Top, num, num, 270f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(rect.Left, rect.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
