using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMlify;

[Transaction(TransactionMode.Manual)]
public sealed class AuditPatternsCommand : IExternalCommand
{
	public Result Execute(ExternalCommandData c, ref string m, ElementSet e)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		return BimActions.Run(c, ref m, e, "patterns");
	}
}



