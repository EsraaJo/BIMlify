using Autodesk.Revit.DB;

namespace BIMlify.SmartOpenings;

internal sealed class OpeningCandidate
{
	internal ElementId HostId;

	internal XYZ Center;

	internal XYZ Axis;

	internal double Width;

	internal double Height;

	internal bool Circle;

	internal bool Wall;

	internal string PreviewTab;

	internal ElementId CreatedOpeningId;

	internal string Summary;
}
