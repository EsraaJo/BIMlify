using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace BIMlify;

public sealed class App : IExternalApplication
{
	public Result OnStartup(UIControlledApplication app)
	{
		BIMlify.PlanRangeUi.PlanRangeInteraction.SetMainWindowHandle(app.MainWindowHandle);
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
			Application.ThreadException += (object sender, ThreadExceptionEventArgs args) =>
			{
				try
				{
					File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BIMlify_UI_Error.log"), DateTime.Now.ToString("u") + Environment.NewLine + args.Exception.ToString() + Environment.NewLine + Environment.NewLine);
				}
				catch
				{
				}
			};
		}
		catch
		{
		}
		app.CreateRibbonTab("BIMlify");
		RibbonPanel val = app.CreateRibbonPanel("BIMlify", "BIMlify Tools");
		string location = typeof(App).Assembly.Location;
		val.AddItem((RibbonItemData)(object)Button(location, "Dashboard", "BIMlify", typeof(DashboardCommand), "bimlify.png", "Open the BIMlify tool center"));
		return (Result)0;
	}

	private static PushButtonData Button(string assembly, string name, string label, Type command, string icon, string tip)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected Obj, but got Unknown
		string text = Path.Combine(Path.GetDirectoryName(assembly), "Icons", icon);
		PushButtonData val = new PushButtonData("BIMlify_" + name, label, assembly, command.FullName);
		((RibbonItemData)val).ToolTip = tip;
		if (File.Exists(text))
		{
			BitmapImage largeImage = (BitmapImage)(((ButtonData)val).Image = new BitmapImage(new Uri(text, UriKind.Absolute)));
			if (name == "Dashboard")
			{
				((ButtonData)val).LargeImage = largeImage;
			}
		}
		return val;
	}

	public Result OnShutdown(UIControlledApplication app)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		BimActions.CloseModelessDashboard();
		return (Result)0;
	}
}


