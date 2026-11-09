using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace BIMlify.SmartOpenings;

internal sealed class MepFilter : ISelectionFilter
{
	public bool AllowElement(Element e)
	{
		if (e == null || e.Category == null)
		{
			return false;
		}
		long value = e.Category.Id.Value;
		return value == -2008044 || value == -2008000 || value == -2008130 || value == -2008132 || value == -2008050 || value == -2008020;
	}

	public bool AllowReference(Reference r, XYZ p)
	{
		return false;
	}
}
