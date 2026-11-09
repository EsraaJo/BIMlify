using Autodesk.Revit.DB;

namespace BIMlify.ModelCleaner;

internal sealed class PurgeFinding
{
	public ElementId Id;

	public string Kind;

	public string Category;

	public string Name;

	public string Reason;

	public override string ToString()
	{
		return Name;
	}
}
