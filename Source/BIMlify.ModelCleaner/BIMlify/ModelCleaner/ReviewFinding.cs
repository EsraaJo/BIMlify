using Autodesk.Revit.DB;

namespace BIMlify.ModelCleaner;

internal sealed class ReviewFinding
{
	public ElementId Id;

	public string Group;

	public string Kind;

	public string Name;

	public string Reason;
}
