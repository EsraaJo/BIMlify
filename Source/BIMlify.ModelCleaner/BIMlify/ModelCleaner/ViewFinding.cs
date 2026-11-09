using Autodesk.Revit.DB;

namespace BIMlify.ModelCleaner;

internal sealed class ViewFinding
{
	public ElementId Id;

	public string Name;

	public string ViewType;

	public string Template;

	public string Status;

	public string Reason;
}
