using Autodesk.Revit.UI;
using BIMlify.ReviewRunGate;

namespace BIMlify.ModelCleaner;

public static class Entry
{
	public static void Run(UIDocument ui)
	{
		if (ui != null && ui.Document != null)
		{
			bool flag = BIMlify.ReviewRunGate.Entry.ConfirmSmartCleaner();
			CleanerWindow cleanerWindow = new CleanerWindow(ui);
			if (flag)
			{
				cleanerWindow.ScanModel();
			}
			cleanerWindow.Show();
		}
	}

	public static void RunCategoryReview(UIDocument ui)
	{
		if (ui == null || ui.Document == null)
		{
			return;
		}
		using CleanerWindow cleanerWindow = new CleanerWindow(ui, openCategoryReview: true);
		BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(cleanerWindow);
	}
}

