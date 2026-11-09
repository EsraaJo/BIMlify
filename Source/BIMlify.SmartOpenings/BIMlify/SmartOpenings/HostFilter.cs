using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace BIMlify.SmartOpenings;

internal sealed class HostFilter : ISelectionFilter
{
	public bool AllowElement(Element e)
	{
		return e != null && e.Category != null && (e is Wall || e is Floor || e.Category.Id.Value == -2000035 || e.Category.Id.Value == -2000038);
	}

	public bool AllowReference(Reference r, XYZ p)
	{
		return false;
	}
}
