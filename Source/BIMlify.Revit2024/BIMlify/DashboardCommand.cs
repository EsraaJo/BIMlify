using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMlify;

[Transaction(TransactionMode.Manual)]
public sealed class DashboardCommand : IExternalCommand
{
	public Result Execute(ExternalCommandData c, ref string m, ElementSet e)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		return BimActions.OpenModelessDashboard(c.Application);
	}
}



