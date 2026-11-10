using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Form = System.Windows.Forms.Form;
using Panel = System.Windows.Forms.Panel;
using View = Autodesk.Revit.DB.View;
using Color = System.Drawing.Color;
using Control = System.Windows.Forms.Control;
using TextBox = System.Windows.Forms.TextBox;
using ComboBox = System.Windows.Forms.ComboBox;
using Point = System.Drawing.Point;

namespace BIMlify.CategorySelectSupport;

public static partial class Entry
{
	private sealed class RowData
	{
		internal string Key;

		internal string Name;

		internal string Type;

		internal string Category;

		internal string FilterName;

		internal int InView;

		internal int InProject;

		internal bool IsFamilyType;

		internal bool IsLinked;

		internal long TypeId;

		internal long LinkInstanceId;
	}

	private sealed class RevitWindow : IWin32Window
	{
		public IntPtr Handle { get; private set; }

		internal RevitWindow(IntPtr h)
		{
			Handle = h;
		}
	}

	private sealed class PickerHandler : IExternalEventHandler
	{
		private readonly Form form;

		private readonly Document originalDocument;

		private readonly View originalView;

		private readonly ExternalEvent externalEvent;

		private Func<List<RowData>> rowsProvider;

		private Func<bool> familyMode;

		private Func<bool> includeLinks;

		private Label status;

		private Button selectButton;

		private Button isolateButton;

		private Func<bool> excludeAnnotations;

		private Func<bool> excludeLinks;

		private Button visibleSelectButton;

		private List<RowData> pending = new List<RowData>();

		private string operation;
        private Action<UIDocument> advancedOperation;

		private bool disposed;

		internal PickerHandler(Form f, Document doc, View view)
		{
			form = f;
			originalDocument = doc;
			originalView = view;
			externalEvent = ExternalEvent.Create((IExternalEventHandler)(object)this);
		}

		internal void SetSelectionProvider(Func<List<RowData>> rows, Func<bool> family, Func<bool> links, Label label, Button select, Button isolate)
		{
			rowsProvider = rows;
			familyMode = family;
			includeLinks = links;
			status = label;
			selectButton = select;
			isolateButton = isolate;
		}

		internal void SetVisibleSelectionProvider(Func<bool> excludeAnnotationItems, Func<bool> excludeLinkedItems, Button selectVisible)
		{
			excludeAnnotations = excludeAnnotationItems;
			excludeLinks = excludeLinkedItems;
			visibleSelectButton = selectVisible;
		}

		internal void Queue(string op, Action<UIDocument> advanced = null)
		{
			//IL_0083: Unknown result type (might be due to invalid IL or missing references)
			//IL_0088: Unknown result type (might be due to invalid IL or missing references)
			//IL_0089: Unknown result type (might be due to invalid IL or missing references)
			//IL_008b: Invalid comparison between Unknown and I4
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_008f: Invalid comparison between Unknown and I4
			if (disposed)
			{
				return;
			}
            if (externalEvent.IsPending)
            {
                SetStatus("Wait for the current Revit selection operation to finish.");
                return;
            }
            operation = op;
            advancedOperation = advanced;
			// Reading rows may touch the Revit document.  Do that only inside the
			// ExternalEvent for operations that actually need the grid selection.
			// "visible" and "reset" must not access Revit from this modeless UI.
			pending = new List<RowData>();
			if (advanced == null && op != "reset" && op != "visible")
			{
				pending = ((rowsProvider == null) ? new List<RowData>() : rowsProvider());
			}
			if (advanced == null && op != "reset" && op != "visible" && pending.Count == 0)
			{
				SetStatus("Select at least one item first.");
				return;
			}
			SetButtons(enabled: false);
			SetStatus("Working in Revit…");
			try
			{
				ExternalEventRequest val = externalEvent.Raise();
				if ((int)val == 2 || (int)val == 3)
				{
					SetButtons(enabled: true);
					SetStatus("Revit did not accept the request. Try again.");
				}
			}
			catch (Exception ex)
			{
				SetButtons(enabled: true);
				SetStatus("Could not queue the Revit operation: " + ex.Message);
			}
		}

		public string GetName()
		{
			return "BIMlify Category Select";
		}

		public void Execute(UIApplication app)
		{
			//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c8: Expected Obj, but got Unknown
			//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00db: Unknown result type (might be due to invalid IL or missing references)
			//IL_0361: Unknown result type (might be due to invalid IL or missing references)
			//IL_0368: Expected Obj, but got Unknown
			//IL_036b: Unknown result type (might be due to invalid IL or missing references)
			//IL_037c: Unknown result type (might be due to invalid IL or missing references)
			try
			{
				UIDocument activeUIDocument = app.ActiveUIDocument;
				if (activeUIDocument == null || activeUIDocument.Document == null)
				{
					throw new InvalidOperationException("Open the source project in Revit before using Category Select.");
				}
				Document doc = activeUIDocument.Document;
				View activeView = activeUIDocument.ActiveView;

                bool viewScope = CanReadView(doc, activeView);
                if ((operation == "isolate" || operation == "reset") &&
                    (!viewScope || !activeView.CanUseTemporaryVisibilityModes()))
                {
                    SetStatus("This view does not support temporary isolation. Switch views or use Select.");
                    return;
                }
				if (!doc.Equals(originalDocument))
				{
					throw new InvalidOperationException("Return to the project where Category Select was opened, then try again.");
				}
                if (advancedOperation != null)
                {
                    Action<UIDocument> action = advancedOperation;
                    advancedOperation = null;
                    if (!doc.Equals(originalDocument))
                        throw new InvalidOperationException("Return to the project where Category Select was opened.");
                    action(activeUIDocument);
                    return;
                }
				if (operation == "reset")
				{
					Transaction val = new Transaction(doc, "BIMlify — Reset temporary isolate");
					try
					{
						val.Start();
						activeView.DisableTemporaryViewMode((TemporaryViewMode)2);
						val.Commit();
					}
					finally
					{
						((IDisposable)val)?.Dispose();
					}
					SetStatus("Temporary isolation reset. Continue editing in Revit.");
					return;
				}
				if (operation == "visible")
				{
                    if (!viewScope)
                    {
                        SetStatus("Revit cannot list visible elements in this view. Switch views or use project selection.");
                        return;
                    }
					bool omitAnnotations = excludeAnnotations != null && excludeAnnotations();
					bool omitLinks = excludeLinks != null && excludeLinks();
					List<ElementId> visibleIds = new FilteredElementCollector(doc, activeView.Id)
						.WhereElementIsNotElementType()
						.Where(e => e != null && (!omitLinks || !(e is RevitLinkInstance)) &&
							(!omitAnnotations || e.Category == null || e.Category.CategoryType != CategoryType.Annotation))
						.Select(e => e.Id)
						.Distinct()
						.ToList();
					activeUIDocument.Selection.SetElementIds((ICollection<ElementId>)visibleIds);
					SetStatus(visibleIds.Count + " visible model element(s) selected in the active view.");
					try { SetForegroundWindow(activeUIDocument.Application.MainWindowHandle); } catch { }
					return;
				}
				List<RowData> list = pending ?? new List<RowData>();
				bool flag = familyMode != null && familyMode();
				bool include = includeLinks != null && includeLinks();
				List<Reference> list2 = new List<Reference>();
				List<ElementId> list3 = new List<ElementId>();
				if (flag)
				{
					CollectFamilyTargets(doc, activeView, list, include, list2, list3);
				}
				else
				{
					List<string> categoryKeys = (from r in list
						select r.Key into k
						where !string.IsNullOrEmpty(k)
						select k).ToList();
					List<Category> hiddenCategories = viewScope ? GetHiddenHostCategories(doc, activeView, categoryKeys) : new List<Category>();
					if (hiddenCategories.Count > 0)
					{
						TaskDialogResult revealChoice = TaskDialog.Show(
							"Category Select",
						"One or more selected categories are hidden in Visibility/Graphics (VV/VG) for this view.\n\nShow them in this view and continue? Choose No to leave VV/VG unchanged and adjust it manually.",
							TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
						if (revealChoice != TaskDialogResult.Yes)
						{
						SetStatus("Canceled. The selected categories are still hidden; show them in VV/VG and try again.");
						return;
						}
						using (Transaction revealTransaction = new Transaction(doc, "BIMlify — Show selected categories"))
						{
							revealTransaction.Start();
							foreach (Category category in hiddenCategories)
							{
								activeView.SetCategoryHidden(category.Id, false);
							}
							revealTransaction.Commit();
						}
					}
					CollectCategoryTargets(doc, activeView, categoryKeys, include, list2, list3);
				}
				if (operation == "select")
				{
					if (list2.Count != 0 || list3.Count != 0)
					{
						try
						{
							if (list2.Count > 0)
							{
								activeUIDocument.Selection.SetReferences((IList<Reference>)list2);
							}
							else
							{
								activeUIDocument.Selection.SetElementIds((ICollection<ElementId>)list3.Distinct().ToList());
							}
							SetStatus(list2.Count + (viewScope ? " matching element reference(s) selected in Revit." : " matching element(s) selected across the project."));
						}
						catch (Exception e)
						{
							List<ElementId> list4 = list3.Where((ElementId id) => !(doc.GetElement(id) is RevitLinkInstance)).Distinct().ToList();
							if (list4.Count == 0)
							{
								throw;
							}
							activeUIDocument.Selection.SetElementIds((ICollection<ElementId>)list4);
							SetStatus("Selected " + list4.Count + " host element(s). Some linked elements could not be selected: " + Unwrap(e).Message);
						}
						try
						{
							SetForegroundWindow(activeUIDocument.Application.MainWindowHandle);
							return;
						}
						catch
						{
							return;
						}
					}
					SetStatus(viewScope ? "No matching elements found in the active view or enabled links." : "No matching host elements found in the project.");
				}
				else
				{
					if (!(operation == "isolate"))
					{
						return;
					}
					list3 = list3.Distinct().ToList();
					if (list3.Count == 0)
					{
						SetStatus("No matching elements found to isolate in the active view or enabled links.");
						return;
					}
					Transaction val2 = new Transaction(doc, "BIMlify — Temporarily isolate categories");
					try
					{
						val2.Start();
						activeView.IsolateElementsTemporary((ICollection<ElementId>)list3);
						val2.Commit();
					}
					finally
					{
						((IDisposable)val2)?.Dispose();
					}
					SetStatus(list3.Count + " element or link instance(s) isolated. Continue editing in Revit.");
				}
			}
			catch (Exception e2)
			{
				SetStatus("Category Select could not complete: " + Unwrap(e2).Message + " Adjust the selection and try again.");
			}
			finally
			{
				SetButtons(enabled: true);
			}
		}

		private static void CollectCategoryTargets(Document doc, View view, List<string> keys, bool include, List<Reference> refs, List<ElementId> ids)
		{
			//IL_0065: Unknown result type (might be due to invalid IL or missing references)
			//IL_0132: Unknown result type (might be due to invalid IL or missing references)
			//IL_0099: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a3: Expected Obj, but got Unknown
			//IL_00db: Unknown result type (might be due to invalid IL or missing references)
			//IL_0168: Unknown result type (might be due to invalid IL or missing references)
			HashSet<string> hashSet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
			MethodInfo method = FindActionsType()?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => m.Name == "CategoryKey");
			Func<Element, string> func = (Element e) =>
			{
				try
				{
					return method?.Invoke(null, new object[1] { e.Category }) as string;
				}
				catch
				{
					return (string)null;
				}
			};
			foreach (Element item in SelectionCollector(doc, view).WhereElementIsNotElementType())
			{
				if (hashSet.Contains(func(item)))
				{
					refs.Add(new Reference(item));
					ids.Add(item.Id);
				}
			}
			if (!include)
			{
				return;
			}
			foreach (RevitLinkInstance item2 in ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance))).Cast<RevitLinkInstance>())
			{
				bool flag = false;
				try
				{
					if (item2.GetLinkDocument() == null)
					{
						continue;
					}
					foreach (Element item3 in new FilteredElementCollector(doc, ((Element)view).Id, ((Element)item2).Id).WhereElementIsNotElementType())
					{
						if (hashSet.Contains(func(item3)))
						{
							refs.Add(new Reference(item3).CreateLinkReference(item2));
							flag = true;
						}
					}
					if (flag)
					{
						ids.Add(((Element)item2).Id);
					}
				}
				catch
				{
				}
			}
		}

		private static List<Category> GetHiddenHostCategories(Document doc, View view, IList<string> keys)
		{
			List<Category> hidden = new List<Category>();
			HashSet<string> selectedKeys = new HashSet<string>(keys ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			MethodInfo categoryKeyMethod = FindActionsType()?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => m.Name == "CategoryKey");
			if (categoryKeyMethod == null)
			{
				return hidden;
			}
			try
			{
				foreach (Category category in (CategoryNameMap)doc.Settings.Categories)
				{
					if (category == null || category.Parent != null)
					{
						continue;
					}
					string key = categoryKeyMethod.Invoke(null, new object[1] { category }) as string;
					if (key != null && selectedKeys.Contains(key) && view.GetCategoryHidden(category.Id))
					{
						hidden.Add(category);
					}
				}
			}
			catch
			{
			}
			return hidden;
		}

		private static void CollectFamilyTargets(Document doc, View view, List<RowData> rows, bool include, List<Reference> refs, List<ElementId> ids)
		{
			//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b2: Expected Obj, but got Unknown
			//IL_016e: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fc: Expected Obj, but got Unknown
			//IL_006b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0131: Unknown result type (might be due to invalid IL or missing references)
			foreach (RowData item in rows.Where((RowData r) => r.IsFamilyType))
			{
				if (item.IsLinked && !include)
				{
					continue;
				}
				if (!item.IsLinked)
				{
					foreach (Element item2 in SelectionCollector(doc, view).WhereElementIsNotElementType())
					{
						try
						{
							if (item2.GetTypeId().Value == item.TypeId)
							{
								refs.Add(new Reference(item2));
								ids.Add(item2.Id);
							}
						}
						catch
						{
						}
					}
					continue;
				}
				Element element = doc.GetElement(new ElementId(item.LinkInstanceId));
				RevitLinkInstance val = (RevitLinkInstance)(object)((element is RevitLinkInstance) ? element : null);
				if (val == null || val.GetLinkDocument() == null)
				{
					continue;
				}
				bool flag = false;
				foreach (Element item3 in new FilteredElementCollector(doc, ((Element)view).Id, ((Element)val).Id).WhereElementIsNotElementType())
				{
					try
					{
						if (item3.GetTypeId().Value == item.TypeId)
						{
							refs.Add(new Reference(item3).CreateLinkReference(val));
							flag = true;
						}
					}
					catch
					{
					}
				}
				if (flag)
				{
					ids.Add(((Element)val).Id);
				}
			}
		}

		internal void SetStatus(string text)
		{
			if (form == null || form.IsDisposed || status == null || status.IsDisposed)
			{
				return;
			}
			if (form.InvokeRequired)
			{
				form.BeginInvoke((MethodInvoker)(() =>
				{
					SetStatus(text);
				}));
			}
			else
			{
				status.Text = text;
			}
		}

		private void SetButtons(bool enabled)
		{
			if (form == null || form.IsDisposed)
			{
				return;
			}
			if (form.InvokeRequired)
			{
				form.BeginInvoke((MethodInvoker)(() =>
				{
					SetButtons(enabled);
				}));
				return;
			}
			bool flag = rowsProvider != null && rowsProvider().Count > 0;
			if (selectButton != null)
			{
				selectButton.Enabled = enabled & flag;
			}
			if (isolateButton != null)
			{
				isolateButton.Enabled = enabled & flag;
			}
			if (visibleSelectButton != null)
			{
				visibleSelectButton.Enabled = enabled;
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			try
			{
				externalEvent.Dispose();
			}
			catch
			{
			}
		}
	}

	private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	private static Form current;
    private static Document currentProject;

	private static readonly Color Back = Color.FromArgb(238, 241, 245);

	private static readonly Color Footer = Color.FromArgb(216, 221, 227);

	private static readonly Color Ink = Color.FromArgb(78, 85, 92);

	private static readonly Color Muted = Color.FromArgb(102, 109, 118);

	private static readonly Color Gold = Color.FromArgb(198, 151, 42);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	public static void Run(UIDocument ui, string requestedAction)
	{
		//IL_0301: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		if (ui == null || ui.Document == null)
		{
			return;
		}

        if (current != null && !current.IsDisposed)
        {
            if (ui.Document.Equals(currentProject))
            {
                current.Show();
                current.Activate();
                return;
            }
            current.Close();
        }
		try
		{
			List<RowData> categoryRows = ReadCategories(ui.Document, ui.ActiveView);
			List<RowData> familyRows = ReadFamilyTypes(ui.Document, ui.ActiveView);
			Type type = FindActionsType();
			Type type2 = type?.GetNestedType("BIMlifyForm", BindingFlags.NonPublic);
			Form form = ((type2 == null) ? new Form() : (Activator.CreateInstance(type2, nonPublic: true) as Form));
			if (form == null)
			{
				form = new Form();
			}
			form.Text = "BIMlify — Category Select";
			form.Width = 1080;
			form.Height = 720;
			form.MinimumSize = new Size(980, 640);
			Form dashboardOwner = null;
			List<Form> dashboardWindowsToRestore = new List<Form>();
            EventHandler keepDashboardHidden = (sender, args) =>
            {
                if (sender is Form dashboard && !dashboard.IsDisposed && dashboard.Visible &&
                    ReferenceEquals(current, form) && !form.IsDisposed)
                    dashboard.Hide();
            };
			try
			{
				Type enhancements = Type.GetType("BIMlify.CleanerUi.DashboardEnhancements, BIMlify.CleanerUi", throwOnError: false);
				dashboardOwner = enhancements?.GetProperty("DashboardWindow", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null, null) as Form;
				if (dashboardOwner != null && dashboardOwner.IsDisposed) dashboardOwner = null;
			}
			catch { dashboardOwner = null; }
			// Fall back to the Revit add-in's main dashboard field so Select / Isolate
			// always hides the main interface and its Home action restores that window.
			if (dashboardOwner == null)
			{
				try
				{
					dashboardOwner = type?.GetField("dashboardWindow", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as Form;
					if (dashboardOwner != null && dashboardOwner.IsDisposed) dashboardOwner = null;
				}
				catch { dashboardOwner = null; }
			}
			form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterScreen;
            if (dashboardOwner != null && dashboardOwner.WindowState == FormWindowState.Normal)
            {
                System.Drawing.Rectangle area = Screen.FromControl(dashboardOwner).WorkingArea;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(
                    Math.Max(area.Left, Math.Min(dashboardOwner.Left, area.Right - form.Width)),
                    Math.Max(area.Top, Math.Min(dashboardOwner.Top, area.Bottom - form.Height)));
            }
			form.ShowIcon = false;
			form.MaximizeBox = true;
			form.MinimizeBox = true;
			form.AutoScaleMode = AutoScaleMode.Dpi;
			form.BackColor = Back;
			form.ForeColor = Ink;
			form.Font = new Font("Segoe UI", 9f);
			try
			{
				type.GetMethod("UseSmoothCorners", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, new object[1] { form });
			}
			catch
			{
			}
			PickerHandler handler = new PickerHandler(form, ui.Document, ui.ActiveView);
			DataGridView dataGridView = BuildWindow(form, categoryRows, familyRows, requestedAction, handler);
			try
			{
				type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo x) => x.Name == "AddWindowSidebar")?.Invoke(null, new object[3] { form, false, null });
				Control sidebar = form.Controls.Cast<Control>().FirstOrDefault((Control c) => string.Equals(c.GetType().Name, "BIMlifySidebar", StringComparison.Ordinal));
				if (sidebar != null)
				{
					Panel dividerCover = new Panel
					{
						Left = Math.Max(0, sidebar.Right - 1),
						Top = 0,
						Width = 1,
						Height = form.ClientSize.Height,
						Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
						BackColor = Color.FromArgb(246, 240, 222),
						TabStop = false,
						Enabled = false
					};
					form.Controls.Add(dividerCover);
					dividerCover.BringToFront();
				}
			}
			catch
			{
			}
            var sourceProject = ui.Document;
            var application = ui.Application;
            EventHandler<Autodesk.Revit.UI.Events.ViewActivatedEventArgs> onViewActivated = (sender, args) =>
            {
                if (form.IsDisposed || args.CurrentActiveView == null ||
                    args.CurrentActiveView.Document.Equals(sourceProject)) return;
                if (form.IsHandleCreated) form.BeginInvoke(new Action(() =>
                {
                    if (!form.IsDisposed && !sourceProject.Equals(application.ActiveUIDocument?.Document))
                        form.Close();
                }));
            };
			form.FormClosed += (object s, FormClosedEventArgs e2) =>
			{
                // FormClosed is raised by WinForms, not Revit.  Removing a
                // Revit event subscription here is an API call outside the
                // valid API context and triggers Revit's fatal exception.
                // The listener is deliberately kept passive; it exits
                // immediately for a disposed form, while all operations
                // already validate the original project in Execute().
				handler.Dispose();
				if (current == form)
				{
					current = null;
                    currentProject = null;
				}
				// Restore the dashboard windows hidden while Select / Isolate was open.
				try
				{
					foreach (Form hiddenDashboard in dashboardWindowsToRestore.ToArray())
					{
						if (hiddenDashboard == null || hiddenDashboard.IsDisposed) continue;
                        hiddenDashboard.VisibleChanged -= keepDashboardHidden;
						if (hiddenDashboard.WindowState == FormWindowState.Minimized) hiddenDashboard.WindowState = FormWindowState.Normal;
						if (!hiddenDashboard.Visible) hiddenDashboard.Show();
					}
					if (dashboardOwner != null && !dashboardOwner.IsDisposed) dashboardOwner.Activate();
				}
				catch { }
			};
            dashboardWindowsToRestore.AddRange(Application.OpenForms.Cast<Form>().Where(candidate =>
                candidate != form && !candidate.IsDisposed && candidate.Visible &&
                (string.Equals(candidate.Text, "BIMlify", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.Tag as string, "dashboard_modeless", StringComparison.OrdinalIgnoreCase))).ToArray());
            if (dashboardOwner != null && !dashboardWindowsToRestore.Contains(dashboardOwner))
                dashboardWindowsToRestore.Add(dashboardOwner);

            try
            {
                IntPtr revitHandle = ui.Application.MainWindowHandle;
                if (revitHandle != IntPtr.Zero) form.Show(new RevitWindow(revitHandle));
                else form.Show();
                if (form.IsDisposed) return;
                current = form;
                currentProject = sourceProject;
                // Do not subscribe to ViewActivated from this modeless form.
                // Revit requires the matching unsubscribe to occur in an API
                // context, whereas FormClosed occurs on the WinForms thread.
                foreach (Form dashboard in dashboardWindowsToRestore)
                {
                    if (dashboard.IsDisposed) continue;
                    dashboard.VisibleChanged += keepDashboardHidden;
                    if (dashboard.Visible) dashboard.Hide();
                }
                form.Activate();
            }
            catch
            {
                if (!form.IsDisposed)
                {
                    if (form.Visible) form.Close();
                    else { handler.Dispose(); form.Dispose(); }
                }
                throw;
            }
		}
		catch (Exception e)
		{
			TaskDialog.Show("Category Select", "Could not open Category Select.\n\n" + Unwrap(e).Message);
		}
	}

	private static DataGridView BuildWindow(Form form, List<RowData> categoryRows, List<RowData> familyRows, string requestedAction, PickerHandler handler)
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 128,
			BackColor = Color.FromArgb(247, 247, 245),
			Padding = new Padding(18, 10, 18, 6)
		};
		Label title = new Label
		{
			Left = 18,
			Top = 10,
			Width = 520,
			Height = 25,
			Text = "Select or Isolate Categories",
			Font = new Font("Segoe UI", 10f, FontStyle.Bold),
			ForeColor = Ink,
			TextAlign = ContentAlignment.MiddleLeft
		};
		Label description = new Label
		{
			Left = 18,
			Top = 38,
			Width = 760,
			Height = 24,
			Text = "Select elements or isolate matching instances. You can continue working in Revit while this window stays open.",
			Font = new Font("Segoe UI", 8f),
			ForeColor = Muted,
			TextAlign = ContentAlignment.MiddleLeft
		};
		panel.Controls.Add(title);
		panel.Controls.Add(description);
		panel.Resize += (object s, EventArgs e) =>
		{
			description.Width = Math.Max(360, Math.Min(760, panel.ClientSize.Width - 190));
		};
		AddLogo(panel);
		MethodInfo headerStyle = FindActionsType()?.GetMethod("AddYellowPanelAccent", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if (headerStyle != null) headerStyle.Invoke(null, new object[3] { panel, Color.FromArgb(214, 171, 61), false });
		Panel filters = new Panel
		{
			Dock = DockStyle.Top,
			Height = 44,
			BackColor = Back,
			Padding = new Padding(14, 6, 14, 5)
		};
		Label searchLabel = new Label
		{
			Text = "Search",
			AutoSize = false,
			Height = 30,
			Font = new Font("Segoe UI", 8.5f),
			ForeColor = Ink,
			TextAlign = ContentAlignment.MiddleLeft
		};
		TextBox search = new TextBox
		{
			Height = 26,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = Color.White,
			ForeColor = Ink,
			Font = new Font("Segoe UI", 8.5f)
		};
		BIMlify.SearchControls.SearchGlyph.Attach(search);
            Label modeLabel = new Label
		{
			Text = "Filter by",
			AutoSize = false,
			Height = 30,
			Font = new Font("Segoe UI", 8.5f),
			ForeColor = Ink,
			TextAlign = ContentAlignment.MiddleLeft
		};
		ComboBox mode = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.White,
			ForeColor = Ink,
			Font = new Font("Segoe UI", 8.5f)
		};
		mode.Items.AddRange(new object[2] { "Category", "Family / Type" });
		mode.SelectedIndex = 0;
		Label typeLabel = new Label
		{
			Text = "Category type",
			AutoSize = false,
			Height = 30,
			Font = new Font("Segoe UI", 8.5f),
			ForeColor = Ink,
			TextAlign = ContentAlignment.MiddleLeft
		};
		ComboBox categoryType = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.White,
			ForeColor = Ink,
			Font = new Font("Segoe UI", 8.5f)
		};
		categoryType.Items.AddRange(new object[3] { "All types", "Model", "Annotation" });
		categoryType.SelectedIndex = 0;
		Label itemLabel = new Label
		{
			Text = "Category",
			AutoSize = false,
			Height = 30,
			Font = new Font("Segoe UI", 8.5f),
			ForeColor = Ink,
			TextAlign = ContentAlignment.MiddleLeft
		};
		ComboBox itemFilter = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.White,
			ForeColor = Ink,
			Font = new Font("Segoe UI", 8.5f)
		};
		CheckBox includeLinks = new CheckBox
		{
			Text = "Include loaded links",
			AutoSize = false,
			Height = 30,
			Width = 165,
			ForeColor = Ink,
			Font = new Font("Segoe UI", 8.5f),
			TextAlign = ContentAlignment.MiddleLeft,
			Checked = false
		};
		filters.Controls.AddRange(new Control[9] { searchLabel, search, modeLabel, mode, typeLabel, categoryType, itemLabel, itemFilter, includeLinks });
		Action layoutFilters = () =>
		{
			const int gap = 8;
			const int filterHeight = 78;
			int left = filters.Padding.Left;
			if (filters.Height != filterHeight) filters.Height = filterHeight;
			int right = filters.ClientSize.Width - filters.Padding.Right;
			int row1 = 6;
			int row2 = 44;
			int includeWidth = 184;
			int includeLeft = Math.Max(left, right - includeWidth);
			includeLinks.SetBounds(includeLeft, row1, includeWidth, 30);

			int modeWidth = 132;
			int modeLabelWidth = 62;
			int modeLeft = Math.Max(left + 52 + gap + 100 + gap + modeLabelWidth + gap, includeLeft - gap - modeWidth);
			mode.SetBounds(modeLeft, row1 + 2, Math.Max(100, Math.Min(modeWidth, includeLeft - gap - modeLeft)), 25);
			modeLabel.SetBounds(Math.Max(left + 52 + gap + 100 + gap, modeLeft - gap - modeLabelWidth), row1, modeLeft - gap - Math.Max(left + 52 + gap + 100 + gap, modeLeft - gap - modeLabelWidth), 30);

			int searchLeft = left + 52 + gap;
			int searchRight = modeLabel.Left - gap;
			searchLabel.SetBounds(left, row1, 52, 30);
			search.SetBounds(searchLeft, row1 + 2, Math.Max(100, searchRight - searchLeft), 25);

			const int filterLabelWidth = 100;
			const int pairGap = 20;
			int comboWidth = Math.Max(120, (right - left - filterLabelWidth * 2 - gap * 2 - pairGap) / 2);
			typeLabel.SetBounds(left, row2, filterLabelWidth, 30);
			int typeComboLeft = left + filterLabelWidth + gap;
			categoryType.SetBounds(typeComboLeft, row2 + 2, comboWidth, 25);
			int itemLabelLeft = typeComboLeft + comboWidth + pairGap;
			const int categoryLabelNudge = 12;
			itemLabel.SetBounds(itemLabelLeft + categoryLabelNudge, row2, filterLabelWidth - categoryLabelNudge, 30);
			int itemLeft = itemLabelLeft + filterLabelWidth + gap;
			itemFilter.SetBounds(itemLeft, row2 + 2, comboWidth, 25);
		};
		filters.Resize += (object s, EventArgs e) =>
		{
			layoutFilters();
		};
		layoutFilters();
		Panel gridHost = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Back,
			Padding = new Padding(14, 10, 14, 10)
		};
		DataGridView grid = new DataGridView
		{
			Dock = DockStyle.Fill,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			AllowUserToResizeRows = false,
			RowHeadersVisible = false,
			AutoGenerateColumns = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			MultiSelect = true,
			EditMode = DataGridViewEditMode.EditOnEnter,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
			BackgroundColor = Color.White,
			BorderStyle = BorderStyle.None,
			GridColor = Color.FromArgb(220, 224, 229),
			EnableHeadersVisualStyles = false,
			Font = new Font("Segoe UI", 8.5f),
			RowTemplate = 
			{
				Height = 34
			}
		};
		grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 245);
		grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
		grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
		grid.ColumnHeadersHeight = 34;
		grid.DefaultCellStyle.BackColor = Color.White;
		grid.DefaultCellStyle.ForeColor = Ink;
		grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(252, 243, 218);
		grid.DefaultCellStyle.SelectionForeColor = Ink;
		grid.DefaultCellStyle.Padding = new Padding(4);
		grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 252);
		grid.AlternatingRowsDefaultCellStyle.ForeColor = Ink;
		grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
		grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
		gridHost.Controls.Add(grid);
		DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn = new DataGridViewCheckBoxColumn();
		dataGridViewCheckBoxColumn.HeaderText = "Use";
		dataGridViewCheckBoxColumn.Width = 58;
		dataGridViewCheckBoxColumn.FillWeight = 8f;
		dataGridViewCheckBoxColumn.FlatStyle = FlatStyle.Standard;
		dataGridViewCheckBoxColumn.ThreeState = false;
		dataGridViewCheckBoxColumn.ValueType = typeof(bool);
		dataGridViewCheckBoxColumn.TrueValue = true;
		dataGridViewCheckBoxColumn.FalseValue = false;
		DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn2 = dataGridViewCheckBoxColumn;
		dataGridViewCheckBoxColumn2.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
		grid.Columns.Add(dataGridViewCheckBoxColumn2);
		grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = "Category",
			ReadOnly = true,
			FillWeight = 40f
		});
		grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = "Type",
			ReadOnly = true,
			FillWeight = 18f
		});
		grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = "In view",
			ReadOnly = true,
			FillWeight = 12f
		});
		grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = "In project",
			ReadOnly = true,
			FillWeight = 14f
		});
        TabControl tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(130, 32),
            Padding = new Point(12, 4),
            Font = new Font("Segoe UI", 8.3f),
            BackColor = Back
        };
        tabs.DrawItem += (sender, args) =>
        {
            if (args.Index < 0 || args.Index >= tabs.TabPages.Count) return;
            System.Drawing.Rectangle bounds = args.Bounds;
            bool selected = tabs.SelectedIndex == args.Index;
            using (var fill = new SolidBrush(selected ? Color.White : Color.FromArgb(222, 226, 231)))
                args.Graphics.FillRectangle(fill, bounds);
            using (var outline = new Pen(Color.FromArgb(161, 169, 178)))
                args.Graphics.DrawRectangle(outline, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
            using (var font = new Font(tabs.Font, selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(args.Graphics, tabs.TabPages[args.Index].Text, font, System.Drawing.Rectangle.Inflate(bounds, -6, -2),
                    Color.FromArgb(67, 76, 86), TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            if (selected)
                using (var accent = new Pen(Color.FromArgb(220, 177, 82), 2f))
                    args.Graphics.DrawLine(accent, bounds.Left + 1, bounds.Bottom - 2, bounds.Right - 2, bounds.Bottom - 2);
        };
        tabs.SelectedIndexChanged += (sender, args) => tabs.Invalidate();
		TabPage categoriesPage = new TabPage("Smart Selection") { BackColor = Back, Padding = new Padding(0) };
		TabPage smartSelectionPage = new TabPage("Select All Visible") { BackColor = Back, Padding = new Padding(0) };
		// Keep the existing category filters/table together as the first tab.
		categoriesPage.Controls.Add(gridHost);
		categoriesPage.Controls.Add(filters);
		Panel smartSelectionContent = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Back,
			Padding = new Padding(8)
		};
		Panel smartSelectionCard = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.White,
			Padding = new Padding(20)
		};
		smartSelectionCard.Paint += (object s, PaintEventArgs e) =>
		{
			using (Pen outline = new Pen(Color.FromArgb(220, 224, 229), 1f))
			{
				e.Graphics.DrawRectangle(outline, 0, 0, Math.Max(0, smartSelectionCard.Width - 1), Math.Max(0, smartSelectionCard.Height - 1));
			}
		};
		Label smartSelectionTitle = new Label
		{
			Left = 20, Top = 18, Width = 520, Height = 24,
			Text = "Select All Visible",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Ink,
			TextAlign = ContentAlignment.MiddleLeft
		};
		Label smartSelectionDescription = new Label
		{
			Left = 20, Top = 47, Width = 720, Height = 42,
			Text = "Selects model elements visible in the active Revit view. Hidden elements are not included.",
			Font = new Font("Segoe UI", 8.5f), ForeColor = Muted,
			TextAlign = ContentAlignment.TopLeft
		};
		CheckBox excludeAnnotations = new CheckBox
		{
			Left = 20, Top = 104, Width = 300, Height = 28,
			Text = "Exclude annotations", Checked = true,
			Font = new Font("Segoe UI", 8.5f), ForeColor = Ink,
			TextAlign = ContentAlignment.MiddleLeft
		};
		CheckBox excludeLinks = new CheckBox
		{
			Left = 20, Top = 138, Width = 330, Height = 28,
			Text = "Exclude linked model instances", Checked = true,
			Font = new Font("Segoe UI", 8.5f), ForeColor = Ink,
			TextAlign = ContentAlignment.MiddleLeft
		};
		Label smartSelectionNote = new Label
		{
			Left = 20, Top = 180, Width = 720, Height = 40,
			Text = "The options above only affect this selection; they do not change Visibility/Graphics or the model.",
			Font = new Font("Segoe UI", 8.5f), ForeColor = Muted,
			TextAlign = ContentAlignment.TopLeft
		};
		Button selectVisible = MakeButton("Select All Visible", 150, primary: true);
		selectVisible.Left = 20;
		selectVisible.Top = smartSelectionNote.Bottom + 16;
		selectVisible.Anchor = AnchorStyles.Left | AnchorStyles.Top;
		selectVisible.Click += (object s, EventArgs e) => handler.Queue("visible");
		smartSelectionContent.Controls.Add(smartSelectionCard);
		smartSelectionCard.Controls.Add(smartSelectionTitle);
		smartSelectionCard.Controls.Add(smartSelectionDescription);
		smartSelectionCard.Controls.Add(excludeAnnotations);
		smartSelectionCard.Controls.Add(excludeLinks);
		smartSelectionCard.Controls.Add(smartSelectionNote);
		smartSelectionCard.Controls.Add(selectVisible);
        selectVisible.BringToFront();
		smartSelectionPage.Controls.Add(smartSelectionContent);
		tabs.TabPages.Add(categoriesPage);
		tabs.TabPages.Add(smartSelectionPage);
        AddAdvancedSelectionTabs(tabs, handler);
        void FitSelectionTabs()
        {
            if (tabs.IsDisposed || tabs.TabCount == 0 || tabs.ClientSize.Width < 1) return;
            // Keep every workflow visible without the horizontal tab scroller.
            int width = Math.Max(1, Math.Min(130, (tabs.ClientSize.Width - 18) / tabs.TabCount));
            if (tabs.ItemSize.Width != width) tabs.ItemSize = new Size(width, 32);
        }
        tabs.SizeChanged += (sender, args) => FitSelectionTabs();
        form.Shown += (sender, args) => form.BeginInvoke(new Action(() =>
        {
            FitSelectionTabs();

            foreach (var picker in new[] { mode, categoryType, itemFilter })
                picker.BackColor = Color.FromArgb(255, 245, 215);
        }));
		grid.CurrentCellDirtyStateChanged += (object s, EventArgs e) =>
		{
			if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
			{
				grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
				grid.EndEdit();
			}
		};
		grid.CellMouseUp += (object s, DataGridViewCellMouseEventArgs e) =>
		{
			if (e.RowIndex >= 0 && e.ColumnIndex == 0)
			{
				try
				{
					grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
					grid.EndEdit();
				}
				catch
				{
				}
			}
		};
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 64,
			BackColor = Footer,
			Padding = new Padding(14, 8, 14, 8)
		};
		panel2.Paint += (object s, PaintEventArgs e) =>
		{
			using (Pen accent = new Pen(Color.FromArgb(214, 171, 61), 1.35f))
			{
				e.Graphics.DrawLine(accent, 0, panel2.Height - 2, panel2.Width, panel2.Height - 2);
			}
		};
		Label count = new Label
		{
			Left = 14,
			Top = 13,
			Width = 205,
			Height = 18,
			Text = "0 selected | 0 in view",
			ForeColor = Ink,
			BackColor = Footer,
			Font = new Font("Segoe UI", 8.2f),
			TextAlign = ContentAlignment.MiddleLeft,
			RightToLeft = RightToLeft.No,
			AutoSize = false,
			AutoEllipsis = false,
			UseMnemonic = false
		};
		Action fitCount = () =>
		{
			try
			{
				MethodInfo fit = FindActionsType()?.GetMethod("FitLabelTextToOneLine", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				fit?.Invoke(null, new object[3] { count, count.Text, 7.2f });
			}
			catch { }
		};
		count.TextChanged += (object s, EventArgs e) => fitCount();
		count.SizeChanged += (object s, EventArgs e) => fitCount();
		Label label = new Label
		{
			Left = 14,
			Top = 31,
			Width = 590,
			Height = 18,
			Text = "Choose categories or family types, then select or isolate.",
			ForeColor = Muted,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			RightToLeft = RightToLeft.No
		};
		Button button = MakeButton("Select All", 96, primary: false);
        Button button2 = MakeButton("Clear Selection", 118, primary: false);
		Button select = MakeButton("Select", 96, primary: false);
		Button isolate = MakeButton("Isolate", 96, primary: true);
		Button button3 = MakeButton("Reset Isolate", 112, primary: false);
		Button button4 = MakeButton("Close", 96, primary: true);
		FlowLayoutPanel actionGroup = new FlowLayoutPanel
		{
			Left = 0,
			Top = 0,
			Width = 664,
			Height = 34,
			WrapContents = false,
			FlowDirection = FlowDirection.LeftToRight,
			BackColor = Footer,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		actionGroup.Controls.Add(button);
		actionGroup.Controls.Add(button2);
		actionGroup.Controls.Add(button3);
		actionGroup.Controls.Add(isolate);
		actionGroup.Controls.Add(select);
		actionGroup.Controls.Add(button4);
		if (requestedAction == "select")
		{
			isolate.Visible = false;
		}
		else if (requestedAction == "isolate")
		{
			select.Visible = false;
		}
		Action layoutActionGroup = () =>
		{
			int visibleWidth = actionGroup.Controls.Cast<Control>().Where((Control c) => c.Visible).Sum((Control c) => c.Width + c.Margin.Horizontal);
			actionGroup.Width = visibleWidth;
			actionGroup.Left = Math.Max(0, panel2.ClientSize.Width - visibleWidth - panel2.Padding.Right);
			actionGroup.Top = Math.Max(0, (panel2.ClientSize.Height - actionGroup.Height) / 2);
			label.Width = Math.Max(0, actionGroup.Left - label.Left - 12);
		};
        var tabActions = new Dictionary<TabPage, Button> { { smartSelectionPage, selectVisible } };
        foreach (TabPage page in tabs.TabPages)
        {
            var action = page.Controls.OfType<Panel>().SelectMany(card => card.Controls.OfType<Button>())
                .FirstOrDefault(candidate => candidate.Text == "Select Similar" || candidate.Text == "Select Matches");
            if (action != null) tabActions.Add(page, action);
        }
        foreach (var action in tabActions.Values)
        {
            actionGroup.Controls.Add(action);
            actionGroup.Controls.SetChildIndex(action, 0);
            action.Visible = false;
        }
        tabs.SelectedIndexChanged += (sender, args) =>
        {
            button.Visible = tabs.SelectedTab == categoriesPage;
            foreach (var pair in tabActions) pair.Value.Visible = tabs.SelectedTab == pair.Key;
            layoutActionGroup();
        };
		panel2.Resize += (object s, EventArgs e) => layoutActionGroup();
		form.Shown += (object s, EventArgs e) => layoutActionGroup();
		layoutActionGroup();
		button.Click += (object s, EventArgs e) =>
		{
			foreach (DataGridViewRow item2 in (IEnumerable)grid.Rows)
			{
				if (item2.Visible)
				{
					item2.Cells[0].Value = true;
				}
			}
		};
		button2.Click += (object s, EventArgs e) =>
		{
			foreach (DataGridViewRow item3 in (IEnumerable)grid.Rows)
			{
				item3.Cells[0].Value = false;
			}
		};
		button4.Click += (object s, EventArgs e) =>
		{
			form.Close();
		};
		panel2.Controls.Add(count);
		panel2.Controls.Add(label);
		panel2.Controls.Add(actionGroup);
		Panel tabHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Back,
            Padding = new Padding(18, 8, 18, 10)
        };
        tabHost.Controls.Add(tabs);
        form.Controls.Add(tabHost);
		form.Controls.Add(panel2);
		form.Controls.Add(panel);
		List<RowData> currentRows = new List<RowData>();
		search.TextChanged += (object s, EventArgs e) =>
		{
			ApplyFilter();
		};
		categoryType.SelectedIndexChanged += (object s, EventArgs e) =>
		{
			ApplyFilter();
		};
		itemFilter.SelectedIndexChanged += (object s, EventArgs e) =>
		{
			ApplyFilter();
		};
		mode.SelectedIndexChanged += (object s, EventArgs e) =>
		{
			ReloadRows();
		};
		includeLinks.CheckedChanged += (object s, EventArgs e) =>
		{
			ReloadRows();
		};
		grid.CellValueChanged += (object s, DataGridViewCellEventArgs e) =>
		{
			UpdateCount();
		};
		grid.RowsAdded += (object s, DataGridViewRowsAddedEventArgs e) =>
		{
			UpdateCount();
		};
		grid.RowsRemoved += (object s, DataGridViewRowsRemovedEventArgs e) =>
		{
			UpdateCount();
		};
		handler.SetSelectionProvider(() => CheckedRows(), () => mode.SelectedIndex == 1, () => includeLinks.Checked, label, select, isolate);
		handler.SetVisibleSelectionProvider(() => excludeAnnotations.Checked, () => excludeLinks.Checked, selectVisible);
		select.Click += (object s, EventArgs e) =>
		{
			handler.Queue("select");
		};
		isolate.Click += (object s, EventArgs e) =>
		{
			handler.Queue("isolate");
		};
		button3.Click += (object s, EventArgs e) =>
		{
			handler.Queue("reset");
		};
		ReloadRows();
		return grid;
		void ApplyFilter()
		{
			string text = (search.Text ?? "").Trim();
			string text2 = ((categoryType.SelectedIndex <= 0) ? null : categoryType.SelectedItem.ToString());
			string text3 = ((itemFilter.SelectedIndex <= 0) ? null : itemFilter.SelectedItem.ToString());
			bool flag = mode.SelectedIndex == 1;
			foreach (DataGridViewRow item4 in (IEnumerable)grid.Rows)
			{
				RowData rowData = (RowData)item4.Tag;
				bool flag2 = flag || text2 == null || rowData.Type == text2;
				bool flag3 = text3 == null || rowData.FilterName == text3;
				bool flag4 = text.Length == 0 || rowData.Name.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0 || rowData.Type.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0 || rowData.Category.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;
				item4.Visible = flag2 & flag3 & flag4;
			}
		}
		List<RowData> CheckedRows()
		{
			return (from DataGridViewRow r in grid.Rows
				where r.Cells[0].Value != null && Convert.ToBoolean(r.Cells[0].Value)
				select (RowData)r.Tag).ToList();
		}
		void ReloadRows()
		{
			bool flag = mode.SelectedIndex == 1;
			List<RowData> source = (flag ? familyRows : categoryRows);
			bool links = includeLinks.Checked;
			currentRows = source.Where((RowData r) => links || !r.IsLinked).ToList();
			categoryType.Enabled = !flag;
			typeLabel.Enabled = !flag;
			itemLabel.Text = (flag ? "Family / Type" : "Category");
			itemFilter.BeginUpdate();
			itemFilter.Items.Clear();
			itemFilter.Items.Add(flag ? "All family types" : "All categories");
			foreach (string item5 in (from r in currentRows
				select r.FilterName into x
				where !string.IsNullOrEmpty(x)
				select x).Distinct().OrderBy((string x) => x, StringComparer.CurrentCultureIgnoreCase))
			{
				itemFilter.Items.Add(item5);
			}
			itemFilter.SelectedIndex = 0;
			itemFilter.EndUpdate();
			grid.Rows.Clear();
			foreach (RowData item6 in currentRows)
			{
				int index = grid.Rows.Add(false, item6.Name, item6.Type, item6.InView, item6.InProject);
				grid.Rows[index].Tag = item6;
			}
			ApplyFilter();
			UpdateCount();
		}
		void UpdateCount()
		{
			List<RowData> list = CheckedRows();
			int num = list.Sum((RowData r) => r.InView);
			count.Text = list.Count + " selected | " + num + " in view";
			select.Enabled = list.Count > 0;
			isolate.Enabled = list.Count > 0;
		}
	}

	private static Button MakeButton(string text, int width, bool primary)
	{
		bool closeAction = string.Equals((text ?? string.Empty).Trim(), "Close", StringComparison.OrdinalIgnoreCase);
		Button button = null;
		try
		{
			Type glassButton = FindActionsType()?.GetNestedType("GlassButton", BindingFlags.NonPublic);
			if (glassButton != null) button = Activator.CreateInstance(glassButton, nonPublic: true) as Button;
		}
		catch { }
		if (button == null)
		{
			button = new Button
			{
				FlatStyle = FlatStyle.Flat,
				Font = new Font("Segoe UI", 8.5f),
				BackColor = Color.FromArgb(216, 221, 227),
				ForeColor = Ink,
				UseVisualStyleBackColor = false
			};
			button.FlatAppearance.BorderColor = Color.FromArgb(132, 140, 147);
			button.FlatAppearance.BorderSize = 1;
			button.FlatAppearance.MouseOverBackColor = Color.FromArgb(247, 249, 251);
		}
		button.Text = text;
		button.Width = Math.Max(width, button.MinimumSize.Width);
		button.Height = 30;
		button.Margin = new Padding(4, 0, 4, 0);
		button.UseVisualStyleBackColor = false;
		PropertyInfo primaryProperty = button.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (primaryProperty != null && primaryProperty.CanWrite) primaryProperty.SetValue(button, primary || closeAction, null);
		if (primary && !closeAction) button.Name = "BIMlifyConfirmContinue";
		if (closeAction) button.DialogResult = DialogResult.Cancel;
		return button;
	}

	private static void AddLogo(Panel header)
	{
		try
		{
			MethodInfo methodInfo = FindActionsType()?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo x) => x.Name == "CreateBrandLockup" && x.GetParameters().Length == 2);
			if (!(methodInfo == null) && methodInfo.Invoke(null, new object[2] { header.BackColor, null }) is Control control)
			{
				control.Dock = DockStyle.Right;
				control.Width = 150;
				header.Controls.Add(control);
				control.BringToFront();
			}
		}
		catch
		{
		}
	}

	private static Type FindActionsType()
	{
		return (from a in AppDomain.CurrentDomain.GetAssemblies()
			select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
	}

	private static List<RowData> ReadCategories(Document doc, View view)
	{
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		Type type = FindActionsType();
		MethodInfo methodInfo = type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo x) => x.Name == "BuildCategoryEntries");
		if (methodInfo == null)
		{
			throw new InvalidOperationException("Category data is unavailable in this BIMlify build.");
		}
		IEnumerable enumerable = methodInfo.Invoke(null, new object[2] { doc, view }) as IEnumerable;
		List<RowData> list = new List<RowData>();
		if (enumerable == null)
		{
			return list;
		}
		MethodInfo methodInfo2 = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo x) => x.Name == "CategoryKey");
		// Determine host categories from the document's category collection, not
		// from elements visible in the active view. A host category can be hidden
		// in VV/VG (or simply have no visible instances) and must still remain in
		// the picker when Include loaded links is unchecked.
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			foreach (Category category in (CategoryNameMap)doc.Settings.Categories)
			{
				if (methodInfo2?.Invoke(null, new object[1] { category }) is string item)
				{
					hashSet.Add(item);
				}
			}
		}
		catch
		{
		}
		foreach (object item3 in enumerable)
		{
			Type type2 = item3.GetType();
			string text = Read<string>(type2, item3, "Key");
			string text2 = Read<string>(type2, item3, "Name");
			list.Add(new RowData
			{
				Key = text,
				Name = text2,
				Category = text2,
				Type = Read<string>(type2, item3, "Type"),
				InView = Read<int>(type2, item3, "InView"),
				InProject = Read<int>(type2, item3, "InProject"),
				FilterName = text2,
				IsFamilyType = false,
				IsLinked = !hashSet.Contains(text)
			});
		}
		return list;
	}

	private static List<RowData> ReadFamilyTypes(Document doc, View view)
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		Dictionary<string, RowData> map = new Dictionary<string, RowData>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, int> projectCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		Action<Document, string> action = (Document source, string prefix) =>
		{
			//IL_0004: Unknown result type (might be due to invalid IL or missing references)
			try
			{
				foreach (Element item in new FilteredElementCollector(source).WhereElementIsNotElementType())
				{
					try
					{
						ElementId typeId = item.GetTypeId();
						if (!(typeId == (ElementId)null) && !(typeId == ElementId.InvalidElementId) && source.GetElement(typeId) is FamilySymbol)
						{
							string key = prefix + "|" + typeId.Value;
							projectCounts[key] = ((!projectCounts.TryGetValue(key, out var value)) ? 1 : (value + 1));
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		};
		action(doc, "H");
		Action<Element, Document, long, string, bool> action2 = (Element e, Document source, long linkId, string linkName, bool linked) =>
		{
			try
			{
				ElementId typeId = e.GetTypeId();
				if (!(typeId == (ElementId)null) && !(typeId == ElementId.InvalidElementId))
				{
					Element element = source.GetElement(typeId);
					FamilySymbol val2 = (FamilySymbol)(object)((element is FamilySymbol) ? element : null);
					if (val2 != null)
					{
						string text = ((val2.Family == null) ? "Family" : ((Element)val2.Family).Name);
						string category = ((((Element)val2).Category == null) ? "" : ((Element)val2).Category.Name);
						string text2 = (linked ? ("L" + linkId) : "H");
						string key = text2 + "|" + typeId.Value;
						projectCounts.TryGetValue(key, out var value);
						if (!map.TryGetValue(key, out var value2))
						{
							value2 = new RowData
							{
								Key = key,
								Name = text + " | " + ((Element)val2).Name + (linked ? (" — " + linkName) : ""),
								Type = "Family / Type",
								Category = category,
								FilterName = text + " | " + ((Element)val2).Name + (linked ? (" — " + linkName) : ""),
								TypeId = typeId.Value,
								LinkInstanceId = linkId,
								IsLinked = linked,
								IsFamilyType = true,
								InProject = value
							};
							map.Add(key, value2);
						}
						if (CanReadView(doc, view)) value2.InView++;
					}
				}
			}
			catch
			{
			}
		};
		foreach (Element item2 in SelectionCollector(doc, view).WhereElementIsNotElementType())
		{
			action2(item2, doc, 0L, "", arg5: false);
		}
		foreach (RevitLinkInstance item3 in ((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance))).Cast<RevitLinkInstance>())
		{
			try
			{
				Document linkDocument = item3.GetLinkDocument();
				if (linkDocument == null)
				{
					continue;
				}
				string arg2 = "L" + ((Element)item3).Id.Value;
				action(linkDocument, arg2);
				foreach (Element item4 in new FilteredElementCollector(doc, ((Element)view).Id, ((Element)item3).Id).WhereElementIsNotElementType())
				{
					action2(item4, linkDocument, ((Element)item3).Id.Value, ((Element)item3).Name, arg5: true);
				}
			}
			catch
			{
			}
		}
		return map.Values.OrderBy((RowData x) => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	private static T Read<T>(Type t, object o, string name)
	{
		FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		return (field != null && field.GetValue(o) is T val) ? val : default(T);
	}

    private static bool CanReadView(Document doc, View view)
        => view != null && !view.IsTemplate && FilteredElementCollector.IsViewValidForElementIteration(doc, view.Id);

    private static FilteredElementCollector SelectionCollector(Document doc, View view)
        => CanReadView(doc, view) ? new FilteredElementCollector(doc, view.Id) : new FilteredElementCollector(doc);

	private static Exception Unwrap(Exception e)
	{
		return (e is TargetInvocationException && e.InnerException != null) ? e.InnerException : e;
	}
}










