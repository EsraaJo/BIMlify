using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace BIMlify.SmartOpenings;

public sealed class SmartOpeningsApp : IExternalApplication
{
	public Result OnStartup(UIControlledApplication app)
	{
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected Obj, but got Unknown
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			RibbonPanel val = app.CreateRibbonPanel("BIMlify Smart Tools");
			PushButtonData val2 = new PushButtonData("BIMlifySmartOpenings", "Smart\nOpenings", Assembly.GetExecutingAssembly().Location, typeof(SmartOpeningsCommand).FullName);
			((RibbonItemData)val2).ToolTip = "Preview and create MEP and structural openings in walls, floors, roofs, and ceilings.";
			((ButtonData)val2).LargeImage = IconImage();
			val.AddItem((RibbonItemData)(object)val2);
		}
		catch (Exception ex)
		{
			TaskDialog.Show("BIMlify Smart Openings", "Could not add the Smart Openings button.\n\n" + ex.Message);
		}
		return (Result)0;
	}

	public Result OnShutdown(UIControlledApplication app)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		return (Result)0;
	}

	private static BitmapSource IconImage()
	{
		using Bitmap bitmap = new Bitmap(32, 32);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.Clear(Color.Transparent);
		using (Pen pen = new Pen(Color.FromArgb(198, 151, 42), 3f))
		{
			using SolidBrush brush = new SolidBrush(Color.FromArgb(238, 241, 245));
			graphics.FillRectangle(brush, 4, 4, 24, 24);
			graphics.DrawRectangle(pen, 4, 4, 24, 24);
			graphics.DrawLine(pen, 8, 16, 24, 16);
			graphics.DrawLine(pen, 16, 8, 16, 24);
		}
		IntPtr hbitmap = bitmap.GetHbitmap();
		try
		{
			return Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
		}
		finally
		{
			DeleteObject(hbitmap);
		}
	}

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr hObject);
}
