using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BIMlify.ModelCleaner;

internal sealed class WarningFinding
{
	public string Severity;

	public string Description;

	public string Categories;

	public List<ElementId> Ids = new List<ElementId>();
}
