using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Form = System.Windows.Forms.Form;
using View = Autodesk.Revit.DB.View;
using InvalidOperationException = System.InvalidOperationException;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BIMlify.SmartOpenings;

internal sealed class OpeningsEvent : IExternalEventHandler
{
	private readonly SmartOpeningsForm form;

	private readonly ExternalEvent externalEvent;

	private string operation;
    private ElementId locateId;
    private Document candidateProject;

	private bool busy;

	private readonly List<OpeningCandidate> candidates = new List<OpeningCandidate>();

	internal OpeningsEvent(SmartOpeningsForm f)
	{
		form = f;
		externalEvent = ExternalEvent.Create((IExternalEventHandler)(object)this);
	}

	internal void Request(string op, ElementId id = null)
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Invalid comparison between Unknown and I4
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Invalid comparison between Unknown and I4
		if (busy)
		{
			form.ShowAfterAction("Smart Openings is still working on the current action.");
			return;
		}
		operation = op;
        locateId = id;
		busy = true;
		try
		{
			ExternalEventRequest val = externalEvent.Raise();
			if ((int)val == 2 || (int)val == 3)
			{
				busy = false;
				form.ShowAfterAction("Revit could not start this action. Wait for Revit to finish, then try again.");
			}
		}
		catch (Exception ex)
		{
			busy = false;
			form.ShowAfterAction("Revit could not start this action: " + ex.Message);
		}
	}

	internal void Dispose()
	{
		try
		{
			externalEvent.Dispose();
		}
		catch
		{
		}
	}

	public string GetName()
	{
		return "BIMlify Smart Openings";
	}

	public void Execute(UIApplication app)
	{
		UIDocument activeUIDocument = app.ActiveUIDocument;
		if (activeUIDocument == null)
		{
			this.form.ShowAfterAction("No active Revit project is available.");
			busy = false;
			return;
		}
		Form window = this.form.FindForm();
		bool picking = operation == "mep" || operation == "structural";
		if (picking) window?.Hide();
		try
		{
			if (operation == "locate" || operation == "locate-host")
            {
                bool pending = operation == "locate-host";
                if (!activeUIDocument.Document.Equals(candidateProject))
                    throw new InvalidOperationException("Return to the Revit project that produced this result.");
                Element element = activeUIDocument.Document.GetElement(locateId);
                if (element == null || (!pending && !(element is Opening)))
                    throw new InvalidOperationException("The selected element is unavailable in the active project.");
                var ids = new List<ElementId> { locateId };
                activeUIDocument.Selection.SetElementIds(ids);
                activeUIDocument.ShowElements(ids);
                this.form.ShowAfterAction(pending
                    ? "Located host " + locateId.Value + ". Choose Create openings to cut the pending opening."
                    : "Located Revit opening " + locateId.Value + ".");
            }
            else if (operation == "mep")
			{
				candidateProject = activeUIDocument.Document;
                CollectMep(activeUIDocument);
			}
			else if (operation == "auto")
			{
				candidateProject = activeUIDocument.Document;
                AutoCreateMep(activeUIDocument, structuralMode: false, this.form.SelectedSourceLinkId);
			}
			else if (operation == "auto-structural")
			{
				candidateProject = activeUIDocument.Document;
                AutoCreateMep(activeUIDocument, structuralMode: true, this.form.SelectedSourceLinkId);
			}
			else if (operation == "structural")
			{
				candidateProject = activeUIDocument.Document;
                CollectStructural(activeUIDocument);
			}
			else if (operation == "create")
			{
				if (!activeUIDocument.Document.Equals(candidateProject))
                    throw new InvalidOperationException("Return to the project where the opening preview was generated.");
                Create(activeUIDocument);
			}
		}
		catch (System.OperationCanceledException)
		{
			this.form.ShowAfterAction("Selection cancelled.");
		}
		catch (Exception ex2)
		{
			this.form.ShowAfterAction("Smart Openings could not complete: " + ex2.Message);
		}
		finally
		{
			busy = false;
			if (picking && window != null && !window.IsDisposed && !window.Visible) window.Show();
		}
	}

	private void CollectMep(UIDocument ui)
	{
		candidates.Clear();
		form.StartWorkflow("MEP");
		form.ShowMessage("Select the wall/floor/roof hosts to check, then click Finish.");
		List<Element> list = (from r in ui.Selection.PickObjects((ObjectType)1, (ISelectionFilter)(object)new HostFilter(), "Select opening hosts")
			select ui.Document.GetElement(r.ElementId) into e
			where e != null
			select e).ToList();
		form.ShowMessage("Select the MEP services that should pass through those hosts, then click Finish.");
		List<Element> list2 = (from r in ui.Selection.PickObjects((ObjectType)1, (ISelectionFilter)(object)new MepFilter(), "Select pipes, ducts, trays, or conduits")
			select ui.Document.GetElement(r.ElementId) into e
			where e != null
			select e).ToList();
		candidates.Clear();
		double clearance = UnitUtils.ConvertToInternalUnits(form.ClearanceMm, UnitTypeId.Millimeters);
		string shape = form.Shape;
		foreach (Element item in list)
		{
			foreach (Element item2 in list2)
			{
				BoundingBoxXYZ val = item.get_BoundingBox((View)null);
				BoundingBoxXYZ val2 = item2.get_BoundingBox((View)null);
				if (val != null && val2 != null && Overlaps(val, val2))
				{
					bool circle = shape == "Circular" || (shape == "Auto by service" && IsRoundService(item2));
					OpeningCandidate openingCandidate = ForMep(item, item2, val, val2, clearance, circle);
					if (openingCandidate != null)
					{
						openingCandidate.PreviewTab = "MEP";
						candidates.Add(openingCandidate);
					}
				}
			}
		}
		form.SetCandidates(candidates, "MEP preview");
	}

	private void AutoCreateMep(UIDocument ui, bool structuralMode, long selectedSourceLinkId = -1L)
	{
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Expected Obj, but got Unknown
		//IL_03fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0402: Unknown result type (might be due to invalid IL or missing references)
		//IL_040c: Expected Obj, but got Unknown
		//IL_033c: Unknown result type (might be due to invalid IL or missing references)
		candidates.Clear();
		form.StartWorkflow(structuralMode ? "Structural" : "MEP");
		Document document = ui.Document;
		if (selectedSourceLinkId == 0)
		{
			form.ShowAfterAction("Choose a source model before scanning for openings.");
			return;
		}
		double clearance = UnitUtils.ConvertToInternalUnits(form.ClearanceMm, UnitTypeId.Millimeters);
		string shape = form.Shape;
		List<Opening> openings = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(Opening))).Cast<Opening>().ToList();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		if (selectedSourceLinkId < 0)
		{
			List<Element> list = ((IEnumerable<Element>)new FilteredElementCollector(document).WhereElementIsNotElementType()).Where((Element e) => new HostFilter().AllowElement(e)).ToList();
			foreach (Element item3 in list)
			{
				BoundingBoxXYZ val = item3.get_BoundingBox((View)null);
				if (val == null)
				{
					continue;
				}
				IList<Element> list2;
				try
				{
					list2 = new FilteredElementCollector(document).WherePasses((ElementFilter)new ElementIntersectsElementFilter(item3)).WhereElementIsNotElementType().ToElements();
				}
				catch
				{
					continue;
				}
				foreach (Element item4 in list2)
				{
					if (!new MepFilter().AllowElement(item4))
					{
						continue;
					}
					string item = item3.Id.Value + ":" + item4.Id.Value;
					if (!hashSet.Add(item))
					{
						continue;
					}
					BoundingBoxXYZ val2 = item4.get_BoundingBox((View)null);
					if (val2 != null && Overlaps(val, val2))
					{
						bool circle = shape == "Circular" || (shape == "Auto by service" && IsRoundService(item4));
						OpeningCandidate openingCandidate = ForMep(item3, item4, val, val2, clearance, circle);
						if (openingCandidate != null && !HasExistingOpening(openings, openingCandidate))
						{
							openingCandidate.PreviewTab = (structuralMode ? "Structural" : "MEP");
							candidates.Add(openingCandidate);
						}
					}
				}
			}
		}
		else
		{
			foreach (RevitLinkInstance item5 in from RevitLinkInstance x in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance))
				where ((Element)x).Id.Value == selectedSourceLinkId
				select x)
			{
				Document linkDocument = item5.GetLinkDocument();
				if (linkDocument == null)
				{
					continue;
				}
				Transform totalTransform;
				try
				{
					totalTransform = ((Instance)item5).GetTotalTransform();
				}
				catch
				{
					continue;
				}
				List<Element> list3 = ((IEnumerable<Element>)new FilteredElementCollector(linkDocument).WhereElementIsNotElementType()).Where((Element e) => new MepFilter().AllowElement(e)).ToList();
				foreach (Element item6 in list3)
				{
					BoundingBoxXYZ val3;
					try
					{
						val3 = TransformBounds(item6.get_BoundingBox((View)null), totalTransform);
					}
					catch
					{
						continue;
					}
					if (val3 == null)
					{
						continue;
					}
					List<Solid> list4 = TransformedSolids(item6, totalTransform).ToList();
					if (list4.Count == 0)
					{
						continue;
					}
					HashSet<ElementId> hashSet2 = new HashSet<ElementId>();
					foreach (Solid item7 in list4)
					{
						try
						{
							foreach (Element item8 in new FilteredElementCollector(document).WherePasses((ElementFilter)new ElementIntersectsSolidFilter(item7)).WhereElementIsNotElementType())
							{
								if (new HostFilter().AllowElement(item8))
								{
									hashSet2.Add(item8.Id);
								}
							}
						}
						catch
						{
						}
					}
					foreach (ElementId item9 in hashSet2)
					{
						Element element = document.GetElement(item9);
						if (element == null)
						{
							continue;
						}
						string item2 = ((Element)item5).Id.Value + ":" + element.Id.Value + ":" + item6.Id.Value;
						if (!hashSet.Add(item2))
						{
							continue;
						}
						BoundingBoxXYZ val4 = element.get_BoundingBox((View)null);
						if (val4 != null && Overlaps(val4, val3))
						{
							bool circle2 = shape == "Circular" || (shape == "Auto by service" && IsRoundService(item6));
							OpeningCandidate openingCandidate2 = ForMep(element, item6, val4, val3, clearance, circle2);
							if (openingCandidate2 != null && !HasExistingOpening(openings, openingCandidate2))
							{
								openingCandidate2.Summary = "Linked model: " + ((Element)item5).Name + " — " + openingCandidate2.Summary;
								openingCandidate2.PreviewTab = (structuralMode ? "Structural" : "MEP");
								candidates.Add(openingCandidate2);
							}
						}
					}
				}
			}
		}
		if (candidates.Count == 0)
		{
			form.ShowAfterAction("No new MEP openings were found. Existing openings are left unchanged.");
		}
		else
		{
			Create(ui);
		}
	}

	private static BoundingBoxXYZ TransformBounds(BoundingBoxXYZ box, Transform transform)
	{
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Expected Obj, but got Unknown
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0158: Expected Obj, but got Unknown
		//IL_015b: Expected Obj, but got Unknown
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Expected Obj, but got Unknown
		if (box == null || transform == null)
		{
			return null;
		}
		double num = double.MaxValue;
		double num2 = double.MaxValue;
		double num3 = double.MaxValue;
		double num4 = double.MinValue;
		double num5 = double.MinValue;
		double num6 = double.MinValue;
		for (int i = 0; i < 8; i++)
		{
			XYZ val = transform.OfPoint(new XYZ(((i & 1) == 0) ? box.Min.X : box.Max.X, ((i & 2) == 0) ? box.Min.Y : box.Max.Y, ((i & 4) == 0) ? box.Min.Z : box.Max.Z));
			num = Math.Min(num, val.X);
			num2 = Math.Min(num2, val.Y);
			num3 = Math.Min(num3, val.Z);
			num4 = Math.Max(num4, val.X);
			num5 = Math.Max(num5, val.Y);
			num6 = Math.Max(num6, val.Z);
		}
		return new BoundingBoxXYZ
		{
			Min = new XYZ(num, num2, num3),
			Max = new XYZ(num4, num5, num6)
		};
	}

	private static IEnumerable<Solid> TransformedSolids(Element element, Transform linkTransform)
	{
		Options options = new Options
		{
			DetailLevel = (ViewDetailLevel)3,
			IncludeNonVisibleObjects = false
		};
		GeometryElement geometry = element.get_Geometry(options);
		if ((GeometryObject)(object)geometry == (GeometryObject)null)
		{
			yield break;
		}
		foreach (Solid item in TransformedSolids(geometry, linkTransform))
		{
			yield return item;
		}
	}

	private static IEnumerable<Solid> TransformedSolids(GeometryElement geometry, Transform linkTransform)
	{
		foreach (GeometryObject obj in geometry)
		{
			Solid solid = (Solid)(object)((obj is Solid) ? obj : null);
			if (solid != null && solid.Volume > 1E-09)
			{
				Solid transformed = null;
				try
				{
					transformed = SolidUtils.CreateTransformed(solid, linkTransform);
				}
				catch
				{
				}
				if ((GeometryObject)(object)transformed != (GeometryObject)null)
				{
					yield return transformed;
				}
				continue;
			}
			GeometryInstance instance = (GeometryInstance)(object)((obj is GeometryInstance) ? obj : null);
			if (instance == null)
			{
				continue;
			}
			GeometryElement nested = null;
			try
			{
				nested = instance.GetInstanceGeometry();
			}
			catch
			{
			}
			if (!((GeometryObject)(object)nested != (GeometryObject)null))
			{
				continue;
			}
			foreach (Solid item in TransformedSolids(nested, linkTransform))
			{
				yield return item;
			}
		}
	}

	private static bool HasExistingOpening(IEnumerable<Opening> openings, OpeningCandidate c)
	{
		XYZ val = (c.Wall ? c.Axis : XYZ.BasisX);
		XYZ val2 = c.Center - val * (c.Width / 2.0) - XYZ.BasisZ * (c.Height / 2.0);
		XYZ val3 = c.Center + val * (c.Width / 2.0) + XYZ.BasisZ * (c.Height / 2.0);
		double num = Math.Min(val2.X, val3.X);
		double num2 = Math.Max(val2.X, val3.X);
		double num3 = Math.Min(val2.Y, val3.Y);
		double num4 = Math.Max(val2.Y, val3.Y);
		double num5 = Math.Min(val2.Z, val3.Z);
		double num6 = Math.Max(val2.Z, val3.Z);
		foreach (Opening opening in openings)
		{
			try
			{
				if (opening.Host != null && !(opening.Host.Id != c.HostId))
				{
					BoundingBoxXYZ val4 = ((Element)opening).get_BoundingBox((View)null);
					if (val4 != null && num <= val4.Max.X && val4.Min.X <= num2 && num3 <= val4.Max.Y && val4.Min.Y <= num4 && num5 <= val4.Max.Z && val4.Min.Z <= num6)
					{
						return true;
					}
				}
			}
			catch
			{
			}
		}
		return false;
	}

	private void CollectStructural(UIDocument ui)
	{
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		candidates.Clear();
		form.StartWorkflow("Structural");
		Element element = ui.Document.GetElement(ui.Selection.PickObject((ObjectType)1, (ISelectionFilter)(object)new HostFilter(), "Select a wall, floor, roof, or ceiling"));
		XYZ val = ui.Selection.PickPoint("Pick the centre of the structural opening");
		BoundingBoxXYZ val2 = element.get_BoundingBox((View)null);
		if (val2 == null)
		{
			throw new InvalidOperationException("The selected host has no usable model bounds.");
		}
		bool flag = element is Wall;
		bool flag2 = form.Shape == "Circular" && !flag;
		if ((form.Shape == "Circular") & flag)
		{
			form.ShowMessage("Revit's native wall opening API creates rectangular openings. This wall will use a rectangular opening.");
		}
		double width = UnitUtils.ConvertToInternalUnits(form.WidthMm + 2.0 * form.ClearanceMm, UnitTypeId.Millimeters);
		double height = UnitUtils.ConvertToInternalUnits(form.HeightMm + 2.0 * form.ClearanceMm, UnitTypeId.Millimeters);
		candidates.Clear();
		XYZ axis = WallAxis(element);
		XYZ center = (flag ? ProjectToWall(element, val) : new XYZ(val.X, val.Y, val2.Max.Z));
		List<OpeningCandidate> list = candidates;
		OpeningCandidate openingCandidate = new OpeningCandidate
		{
			HostId = element.Id,
			Center = center,
			Axis = axis,
			Width = width,
			Height = height,
			Circle = flag2,
			Wall = flag,
			PreviewTab = "Structural"
		};
		string[] array = new string[10]
		{
			element.Category.Name,
			" #",
			element.Id.Value.ToString(),
			" · ",
			flag2 ? "Circular" : "Rectangular",
			" · ",
			null,
			null,
			null,
			null
		};
		double widthMm = form.WidthMm;
		array[6] = widthMm.ToString();
		array[7] = " × ";
		widthMm = form.HeightMm;
		array[8] = widthMm.ToString();
		array[9] = " mm";
		openingCandidate.Summary = string.Concat(array);
		list.Add(openingCandidate);
		form.SetCandidates(candidates, "Structural preview");
	}

	private void Create(UIDocument ui)
	{
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Expected Obj, but got Unknown
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_050a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0500: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Expected Obj, but got Unknown
		//IL_0399: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a0: Expected Obj, but got Unknown
		//IL_03c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c7: Expected Obj, but got Unknown
		//IL_03e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ee: Expected Obj, but got Unknown
		//IL_040e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0415: Expected Obj, but got Unknown
		//IL_021b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0222: Expected Obj, but got Unknown
		List<OpeningCandidate> list = candidates.Where((OpeningCandidate c) => c.CreatedOpeningId == (ElementId)null).ToList();
		if (list.Count == 0)
		{
			form.ShowAfterAction((candidates.Count == 0) ? "Select hosts or prepare a preview first." : "These openings were already created. Their numbered list remains available for review.");
			return;
		}
		int num = 0;
		int num2 = 0;
        string firstFailure = null;
		List<OpeningCandidate> list2 = new List<OpeningCandidate>();
		List<ElementId> list3 = new List<ElementId>();
		Transaction val = new Transaction(ui.Document, "BIMlify Smart Openings");
		try
		{
			val.Start();
			foreach (OpeningCandidate item in list)
			{
				try
				{
					Element element = ui.Document.GetElement(item.HostId);
					if (element == null)
					{
						throw new InvalidOperationException("Host no longer exists.");
					}
					if (item.Wall)
					{
						Wall val2 = (Wall)(object)((element is Wall) ? element : null);
						if (val2 != null)
						{
							XYZ axis = item.Axis;
							XYZ val3 = item.Center - axis * (item.Width / 2.0) - XYZ.BasisZ * (item.Height / 2.0);
							XYZ val4 = item.Center + axis * (item.Width / 2.0) + XYZ.BasisZ * (item.Height / 2.0);
							item.CreatedOpeningId = ((Element)ui.Document.Create.NewOpening(val2, val3, val4)).Id;
							goto IL_047c;
						}
					}
					double z = item.Center.Z;
					CurveArray val5 = new CurveArray();
					if (item.Circle)
					{
						double num3 = Math.Min(item.Width, item.Height) / 2.0;
						XYZ val6 = new XYZ(item.Center.X, item.Center.Y, z);
						XYZ val7 = val6 + XYZ.BasisX * num3;
						XYZ val8 = val6 + XYZ.BasisY * num3;
						XYZ val9 = val6 - XYZ.BasisX * num3;
						XYZ val10 = val6 - XYZ.BasisY * num3;
						val5.Append((Curve)(object)Arc.Create(val7, val8, val6 + (XYZ.BasisX + XYZ.BasisY).Normalize() * num3));
						val5.Append((Curve)(object)Arc.Create(val8, val9, val6 + (-XYZ.BasisX + XYZ.BasisY).Normalize() * num3));
						val5.Append((Curve)(object)Arc.Create(val9, val10, val6 + (-XYZ.BasisX - XYZ.BasisY).Normalize() * num3));
						val5.Append((Curve)(object)Arc.Create(val10, val7, val6 + (XYZ.BasisX - XYZ.BasisY).Normalize() * num3));
					}
					else
					{
						double num4 = item.Width / 2.0;
						double num5 = item.Height / 2.0;
						XYZ val11 = new XYZ(item.Center.X - num4, item.Center.Y - num5, z);
						XYZ val12 = new XYZ(item.Center.X + num4, item.Center.Y - num5, z);
						XYZ val13 = new XYZ(item.Center.X + num4, item.Center.Y + num5, z);
						XYZ val14 = new XYZ(item.Center.X - num4, item.Center.Y + num5, z);
						val5.Append((Curve)(object)Line.CreateBound(val11, val12));
						val5.Append((Curve)(object)Line.CreateBound(val12, val13));
						val5.Append((Curve)(object)Line.CreateBound(val13, val14));
						val5.Append((Curve)(object)Line.CreateBound(val14, val11));
					}
					item.CreatedOpeningId = ((Element)ui.Document.Create.NewOpening(element, val5, false)).Id;
					goto IL_047c;
					IL_047c:

					list2.Add(item);
					list3.Add(item.CreatedOpeningId);
					num++;
				}
                catch (Exception error)
                {
                    firstFailure = firstFailure ?? ("Host " + item.HostId.Value + ": " + error.Message);
                    num2++;
                }
			}
			if (num > 0)
			{
				if (val.Commit() != TransactionStatus.Committed)
                {
                    foreach (OpeningCandidate candidate in list2) candidate.CreatedOpeningId = null;
                    throw new InvalidOperationException("Revit did not commit the opening cuts.");
                }
			}
			else
			{
				val.RollBack();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
        list3.Clear();
        foreach (OpeningCandidate candidate in list2)
        {
            if (ui.Document.GetElement(candidate.CreatedOpeningId) is Opening)
            {
                candidate.Summary = "Opening #" + candidate.CreatedOpeningId.Value + " — " + candidate.Summary;
                list3.Add(candidate.CreatedOpeningId);
            }
            else
            {
                candidate.CreatedOpeningId = null;
                num2++;
                firstFailure = firstFailure ?? "Revit did not retain one of the opening elements.";
            }
        }
        num = list3.Count;
        string failureDetail = firstFailure == null ? "" : " " + firstFailure;
		if (num > 0)
		{
			try
			{
				ui.Selection.SetElementIds((ICollection<ElementId>)list3);
			}
			catch
			{
			}
			form.SetCandidates(candidates, "Created openings");
			form.ShowAfterAction(num + " Revit opening(s) created; " + num2 + " pending. Use Locate Elements." + failureDetail);
		}
		else
		{
			form.SetCandidates(candidates, candidates.FirstOrDefault()?.PreviewTab ?? "Preview");
            form.ShowAfterAction("No openings were created; " + num2 + " pending." + failureDetail);
		}
	}

	private static OpeningCandidate ForMep(Element host, Element service, BoundingBoxXYZ hb, BoundingBoxXYZ sb, double clearance, bool circle)
	{
		//IL_028f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0296: Expected Obj, but got Unknown
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected Obj, but got Unknown
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Expected Obj, but got Unknown
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		//IL_017a: Expected Obj, but got Unknown
		bool flag = host is Wall;
		XYZ val = WallAxis(host);
		XYZ val5;
		double num3;
		double num4;
		if (flag)
		{
			Location location = host.Location;
			Location obj = ((location is LocationCurve) ? location : null);
			Curve val2 = ((obj != null) ? ((LocationCurve)obj).Curve : null);
			if ((GeometryObject)(object)val2 == (GeometryObject)null)
			{
				return null;
			}
			XYZ val3 = new XYZ((sb.Min.X + sb.Max.X) / 2.0, (sb.Min.Y + sb.Max.Y) / 2.0, (sb.Min.Z + sb.Max.Z) / 2.0);
			IntersectionResult val4 = val2.Project(val3);
			if (val4 == null)
			{
				return null;
			}
			val5 = new XYZ(val4.XYZPoint.X, val4.XYZPoint.Y, val3.Z);
			double num = double.MaxValue;
			double num2 = double.MinValue;
			for (int i = 0; i < 8; i++)
			{
				XYZ val6 = new XYZ(((i & 1) == 0) ? sb.Min.X : sb.Max.X, ((i & 2) == 0) ? sb.Min.Y : sb.Max.Y, ((i & 4) == 0) ? sb.Min.Z : sb.Max.Z);
				double val7 = (val6 - val5).DotProduct(val);
				num = Math.Min(num, val7);
				num2 = Math.Max(num2, val7);
			}
			num3 = num2 - num + 2.0 * clearance;
			num4 = sb.Max.Z - sb.Min.Z + 2.0 * clearance;
			circle = false;
		}
		else
		{
			num3 = sb.Max.X - sb.Min.X + 2.0 * clearance;
			num4 = sb.Max.Y - sb.Min.Y + 2.0 * clearance;
			val5 = new XYZ((sb.Min.X + sb.Max.X) / 2.0, (sb.Min.Y + sb.Max.Y) / 2.0, hb.Max.Z);
		}
		if (num3 <= 0.0 || num4 <= 0.0)
		{
			return null;
		}
		OpeningCandidate openingCandidate = new OpeningCandidate();
		openingCandidate.HostId = host.Id;
		openingCandidate.Center = val5;
		openingCandidate.Axis = val;
		openingCandidate.Width = num3;
		openingCandidate.Height = num4;
		openingCandidate.Circle = circle;
		openingCandidate.Wall = flag;
		openingCandidate.Summary = host.Category.Name + " #" + host.Id.Value + " ← " + service.Category.Name + " #" + service.Id.Value + " · " + (circle ? "Circular" : "Rectangular") + " · " + Math.Round(UnitUtils.ConvertFromInternalUnits(num3, UnitTypeId.Millimeters)) + " × " + Math.Round(UnitUtils.ConvertFromInternalUnits(num4, UnitTypeId.Millimeters)) + " mm";
		return openingCandidate;
	}

	private static bool IsRoundService(Element e)
	{
		return e.Category != null && (e.Category.Id.Value == -2008044 || e.Category.Id.Value == -2008050);
	}

	private static XYZ WallAxis(Element host)
	{
		Location location = host.Location;
		Location obj = ((location is LocationCurve) ? location : null);
		Curve val = ((obj != null) ? ((LocationCurve)obj).Curve : null);
		return ((GeometryObject)(object)val == (GeometryObject)null) ? XYZ.BasisX : val.GetEndPoint(1).Subtract(val.GetEndPoint(0)).Normalize();
	}

	private static XYZ ProjectToWall(Element host, XYZ point)
	{
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		Location location = host.Location;
		Location obj = ((location is LocationCurve) ? location : null);
		Curve val = ((obj != null) ? ((LocationCurve)obj).Curve : null);
		XYZ result;
		if (!((GeometryObject)(object)val == (GeometryObject)null))
		{
			IntersectionResult val2 = val.Project(point);
			double num = ((val2 != null) ? val2.XYZPoint.X : point.X);
			IntersectionResult val3 = val.Project(point);
			result = new XYZ(num, (val3 != null) ? val3.XYZPoint.Y : point.Y, point.Z);
		}
		else
		{
			result = point;
		}
		return result;
	}

	private static bool Overlaps(BoundingBoxXYZ a, BoundingBoxXYZ b)
	{
		return a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y && a.Min.Z < b.Max.Z && b.Min.Z < a.Max.Z;
	}
}

