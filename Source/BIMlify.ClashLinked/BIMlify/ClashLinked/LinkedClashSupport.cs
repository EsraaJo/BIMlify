using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Control = System.Windows.Forms.Control;
using ComboBox = System.Windows.Forms.ComboBox;
using Color = System.Drawing.Color;
using Form = System.Windows.Forms.Form;
using Panel = System.Windows.Forms.Panel;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using View = Autodesk.Revit.DB.View;

namespace BIMlify.ClashLinked;

public static class LinkedClashSupport
{
	private sealed class Candidate
	{
		public Element Element;

		public RevitLinkInstance Link;

		public BoundingBoxXYZ Box;

		public Reference Reference;

		public string Source;

		public string Discipline;

		public Transform Transform;

		public List<Solid> Solids;

		public bool SolidsLoaded;
	}

	private sealed class Clash
	{
		public Reference A;

		public Reference B;

		public ElementId AId;

		public ElementId BId;

		public ElementId LinkA;

		public ElementId LinkB;

		public BoundingBoxXYZ BoxA;

		public BoundingBoxXYZ BoxB;

		public string SourceA;

		public string SourceB;

		public string Text;

		public string Description;
	}

	private sealed class ModelessDispatcher : IExternalEventHandler, IDisposable
	{
		private readonly object gate = new object();

		private readonly ExternalEvent externalEvent;

		private readonly List<Action> pending = new List<Action>();

		internal ModelessDispatcher()
		{
			externalEvent = ExternalEvent.Create((IExternalEventHandler)(object)this);
		}

		internal bool Post(Action action)
		{
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0095: Unknown result type (might be due to invalid IL or missing references)
			//IL_0098: Unknown result type (might be due to invalid IL or missing references)
			//IL_009a: Invalid comparison between Unknown and I4
			lock (gate)
			{
				pending.Add(action);
			}
			ExternalEventRequest val;
			try
			{
				val = externalEvent.Raise();
			}
			catch
			{
				lock (gate)
				{
					if (pending.Count > 0)
					{
						pending.RemoveAt(pending.Count - 1);
					}
				}
				return false;
			}
			if ((int)val != 0 && (int)val != 1)
			{
				lock (gate)
				{
					if (pending.Count > 0)
					{
						pending.RemoveAt(pending.Count - 1);
					}
				}
				return false;
			}
			return true;
		}

		public void Execute(UIApplication application)
		{
			Action[] array;
			lock (gate)
			{
				array = pending.ToArray();
				pending.Clear();
			}
			Action[] array2 = array;
			foreach (Action action in array2)
			{
				if (action != null)
				{
					try
					{
						action();
					}
					catch
					{
					}
				}
			}
		}

		public string GetName()
		{
			return "BIMlify Clash Detection Actions";
		}

		public void Dispose()
		{
			lock (gate)
			{
				pending.Clear();
			}
			try
			{
				externalEvent.Dispose();
			}
			catch
			{
			}
		}
	}

	private sealed class ModelessSession
	{
		internal ModelessDispatcher Dispatcher;

		internal Button Close;
	}

	private sealed class HostBoxSnapshot
	{
		internal int Id;

		internal double MinX;

		internal double MinY;

		internal double MinZ;

		internal double MaxX;

		internal double MaxY;

		internal double MaxZ;

		internal string Name;

		internal string Category;

		internal string Discipline;
	}

	private sealed class HostPairCandidate
	{
		internal HostBoxSnapshot A;

		internal HostBoxSnapshot B;
	}

	private sealed class HostClashSnapshot
	{
		internal int A;

		internal int B;

		internal string Row;

		internal string DisciplineA;

		internal string Reason;
	}

	private sealed class HostScanState
	{
		internal bool Running;

		internal bool Pending;

		internal Form Form;

		internal Label Status;

		internal ModelessDispatcher Dispatcher;

		internal Document Document;

		internal View View;

		internal List<ElementId> ElementIds;

		internal int NextElement;

		internal string ChoiceA;

		internal string ChoiceB;

		internal List<HostBoxSnapshot> Boxes;

		internal List<HostPairCandidate> Candidates;

		internal int NextCandidate;

		internal int ScannedCount;

		internal List<HostClashSnapshot> Confirmed;
	}

	private static readonly Dictionary<Form, ModelessSession> modelessSessions = new Dictionary<Form, ModelessSession>();

	private static readonly Dictionary<object, HostScanState> hostScans = new Dictionary<object, HostScanState>();

	private static readonly Dictionary<Form, Action<List<string>>> hostRowSynchronizers = new Dictionary<Form, Action<List<string>>>();

	private static readonly Dictionary<Form, Func<bool>> hostOnlyScanEnabled = new Dictionary<Form, Func<bool>>();

	public static void StartHostScanAsync(object closure)
	{
		try
		{
			ListBox results = ClosureField(closure, "results") as ListBox;
			Form form = ((results == null) ? null : modelessSessions.Keys.FirstOrDefault((Form f) => !f.IsDisposed && All(f).Contains(results)));
			if (form == null || !modelessSessions.TryGetValue(form, out var value) || value.Dispatcher == null)
			{
				return;
			}
			HostScanState state;
			lock (hostScans)
			{
				if (!hostScans.TryGetValue(closure, out state))
				{
					state = new HostScanState
					{
						Form = form,
						Status = (ClosureField(closure, "status") as Label),
						Dispatcher = value.Dispatcher
					};
					hostScans.Add(closure, state);
				}
			}
			QueueHostApiAction(state, () =>
			{
				CollectHostScanInputs(closure, state);
			});
		}
		catch
		{
		}
	}

	private static void CollectHostScanInputs(object closure, HostScanState state)
	{
		//IL_01f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01eb: Unknown result type (might be due to invalid IL or missing references)
		if (state == null || state.Form == null || state.Form.IsDisposed)
		{
			return;
		}
		lock (hostScans)
		{
			if (state.Running)
			{
				state.Pending = true;
				return;
			}
			state.Running = true;
		}
		Label label = ClosureField(closure, "status") as Label;
		object obj = ClosureField(closure, "doc");
		Document val = (Document)((obj is Document) ? obj : null);
		ComboBox comboBox = ClosureField(closure, "scope") as ComboBox;
		ComboBox comboBox2 = ClosureField(closure, "disciplineA") as ComboBox;
		ComboBox comboBox3 = ClosureField(closure, "disciplineB") as ComboBox;
		if (val == null || comboBox == null || comboBox2 == null || comboBox3 == null)
		{
			FinishHostScan(closure, state, "Clash scan could not read the current scan options.");
			return;
		}
		Func<bool> value = null;
		lock (hostOnlyScanEnabled)
		{
			hostOnlyScanEnabled.TryGetValue(state.Form, out value);
		}
		if (value != null && !value())
		{
			FinishHostScan(closure, state, null);
			return;
		}
		try
		{
			if (label != null)
			{
				label.Text = "Scanning model elements… Results remain available while the scan updates.";
			}
			string choiceA = ChoiceCode(comboBox2.SelectedItem?.ToString() ?? "");
			string choiceB = ChoiceCode(comboBox3.SelectedItem?.ToString() ?? "");
			bool flag = comboBox.SelectedIndex == 1;
			state.Document = val;
			state.View = (flag ? null : val.ActiveView);
			state.ChoiceA = choiceA;
			state.ChoiceB = choiceB;
			IEnumerable<Element> source = (IEnumerable<Element>)(flag ? new FilteredElementCollector(val).WhereElementIsNotElementType() : new FilteredElementCollector(val, ((Element)state.View).Id).WhereElementIsNotElementType());
			state.ElementIds = (from x in source
				orderby (!(x is Wall)) ? 1 : 0
				select x.Id).ToList();
			state.NextElement = 0;
			state.Boxes = new List<HostBoxSnapshot>(1500);
			ProcessHostScanChunk(closure, state);
		}
		catch (Exception ex)
		{
			FinishHostScan(closure, state, "Clash scan failed: " + ex.Message);
		}
	}

	private static void ProcessHostScanChunk(object closure, HostScanState state)
	{
		try
		{
			Label label = ClosureField(closure, "status") as Label;
			int num = Math.Min(state.NextElement + 64, state.ElementIds.Count);
			while (state.NextElement < num && state.Boxes.Count < 1500)
			{
				try
				{
					Element element = state.Document.GetElement(state.ElementIds[state.NextElement]);
					if (element != null && element.Category != null)
					{
						string category = element.Category.Name ?? "";
						string text = Discipline(element);
						if (!(text == "OTHER"))
						{
							BoundingBoxXYZ val = element.get_BoundingBox(state.View);
							if (val != null)
							{
								double num2 = val.Max.X - val.Min.X;
								double num3 = val.Max.Y - val.Min.Y;
								double num4 = val.Max.Z - val.Min.Z;
								if (!(Math.Sqrt(num2 * num2 + num3 * num3 + num4 * num4) < 0.001))
								{
									state.Boxes.Add(new HostBoxSnapshot
									{
										Id = element.Id.IntegerValue,
										MinX = val.Min.X,
										MinY = val.Min.Y,
										MinZ = val.Min.Z,
										MaxX = val.Max.X,
										MaxY = val.Max.Y,
										MaxZ = val.Max.Z,
										Name = (element.Name ?? ((object)element).GetType().Name),
										Category = category,
										Discipline = text
									});
								}
							}
						}
					}
				}
				catch
				{
				}
				state.NextElement++;
			}
			if (state.NextElement < state.ElementIds.Count && state.Boxes.Count < 1500)
			{
				if (label != null)
				{
					label.Text = "Scanning model elements… " + state.NextElement + " checked. Results remain selectable.";
				}
				QueueHostApiAction(state, () =>
				{
					ProcessHostScanChunk(closure, state);
				});
				return;
			}
			HostBoxSnapshot[] captured = state.Boxes.ToArray();
			Task.Run(() => CompareHostBoxes(captured, state.ChoiceA, state.ChoiceB)).ContinueWith((Task<List<HostPairCandidate>> task) =>
			{
				if (state.Form.IsDisposed)
				{
					return;
				}
				try
				{
					state.Form.BeginInvoke((MethodInvoker)(() =>
					{
						if (!state.Form.IsDisposed)
						{
							if (!task.IsFaulted)
							{
								try
								{
									state.Candidates = task.Result;
									state.NextCandidate = 0;
									state.ScannedCount = captured.Length;
									state.Confirmed = new List<HostClashSnapshot>();
									QueueHostApiAction(state, () =>
									{
										ConfirmHostScanCandidates(closure, state);
									});
									return;
								}
								catch (Exception ex2)
								{
									FinishHostScan(closure, state, "Could not update clash results: " + ex2.Message);
									return;
								}
							}
							FinishHostScan(closure, state, "Clash comparison failed: " + task.Exception.GetBaseException().Message);
						}
					}));
				}
				catch
				{
					FinishHostScan(closure, state, null);
				}
			}, TaskScheduler.Default);
		}
		catch (Exception ex)
		{
			FinishHostScan(closure, state, "Clash scan failed: " + ex.Message);
		}
	}

	private static List<HostPairCandidate> CompareHostBoxes(HostBoxSnapshot[] boxes, string a, string b)
	{
		List<HostPairCandidate> list = new List<HostPairCandidate>();
		for (int i = 0; i < boxes.Length; i++)
		{
			for (int j = i + 1; j < boxes.Length; j++)
			{
				HostBoxSnapshot hostBoxSnapshot = boxes[i];
				HostBoxSnapshot hostBoxSnapshot2 = boxes[j];
				if (MatchesChoice(a, hostBoxSnapshot.Discipline, hostBoxSnapshot2.Discipline) && MatchesChoice(b, hostBoxSnapshot.Discipline, hostBoxSnapshot2.Discipline) && (!(a != "ANY") || !(b != "ANY") || (a == hostBoxSnapshot.Discipline && b == hostBoxSnapshot2.Discipline) || (a == hostBoxSnapshot2.Discipline && b == hostBoxSnapshot.Discipline)) && !(hostBoxSnapshot.MaxX <= hostBoxSnapshot2.MinX) && !(hostBoxSnapshot2.MaxX <= hostBoxSnapshot.MinX) && !(hostBoxSnapshot.MaxY <= hostBoxSnapshot2.MinY) && !(hostBoxSnapshot2.MaxY <= hostBoxSnapshot.MinY) && !(hostBoxSnapshot.MaxZ <= hostBoxSnapshot2.MinZ) && !(hostBoxSnapshot2.MaxZ <= hostBoxSnapshot.MinZ))
				{
					list.Add(new HostPairCandidate
					{
						A = hostBoxSnapshot,
						B = hostBoxSnapshot2
					});
				}
			}
		}
		return list;
	}

	private static void ConfirmHostScanCandidates(object closure, HostScanState state)
	{
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Expected Obj, but got Unknown
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Expected Obj, but got Unknown
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_0387: Unknown result type (might be due to invalid IL or missing references)
		//IL_0393: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b2: Expected Obj, but got Unknown
		//IL_03b2: Expected Obj, but got Unknown
		if (state == null || state.Form == null || state.Form.IsDisposed)
		{
			return;
		}
		try
		{
			List<HostPairCandidate> list = state.Candidates ?? new List<HostPairCandidate>();
			List<HostClashSnapshot> list2 = state.Confirmed ?? (state.Confirmed = new List<HostClashSnapshot>());
			int num = Math.Min(state.NextCandidate + 32, list.Count);
			while (state.NextCandidate < num && list2.Count < 500)
			{
				HostPairCandidate hostPairCandidate = list[state.NextCandidate];
				try
				{
					Element element = state.Document.GetElement(new ElementId(hostPairCandidate.A.Id));
					Element element2 = state.Document.GetElement(new ElementId(hostPairCandidate.B.Id));
					if (element != null && element2 != null && ((ElementFilter)new ElementIntersectsElementFilter(element)).PassesFilter(state.Document, element2.Id))
					{
						int num2 = list2.Count + 1;
						string reason = "Actual 3D geometry intersects between " + hostPairCandidate.A.Category + " and " + hostPairCandidate.B.Category + ". Review clearance, routing, or offsets.";
						string row = "Clash " + num2 + "  •  [" + hostPairCandidate.A.Discipline + "] " + hostPairCandidate.A.Category + " / [" + hostPairCandidate.B.Discipline + "] " + hostPairCandidate.B.Category + "  —  " + hostPairCandidate.A.Id + " / " + hostPairCandidate.B.Id;
						list2.Add(new HostClashSnapshot
						{
							A = hostPairCandidate.A.Id,
							B = hostPairCandidate.B.Id,
							Row = row,
							DisciplineA = hostPairCandidate.A.Discipline,
							Reason = reason
						});
					}
				}
				catch
				{
				}
				state.NextCandidate++;
			}
			if (state.NextCandidate < list.Count && list2.Count < 500)
			{
				QueueHostApiAction(state, () =>
				{
					ConfirmHostScanCandidates(closure, state);
				});
				return;
			}
			if (!(ClosureField(closure, "clashRows") is IList list3))
			{
				FinishHostScan(closure, state, "Clash results could not be updated.");
				return;
			}
			list3.Clear();
			foreach (HostClashSnapshot item in list2)
			{
				list3.Add(Tuple.Create<ElementId, ElementId, string, string, string>(new ElementId(item.A), new ElementId(item.B), item.Row, item.DisciplineA, item.Reason));
			}
			Action<List<string>> value = null;
			lock (hostRowSynchronizers)
			{
				hostRowSynchronizers.TryGetValue(state.Form, out value);
			}
			if (value != null)
			{
				value(list2.Select((HostClashSnapshot x) => x.Row).ToList());
			}
			else if (ClosureField(closure, "refreshResults") is Action action)
			{
				action();
			}
			if (state.Status != null)
			{
				state.Status.Text = list2.Count + " potential clashes found · " + state.ScannedCount + " elements scanned.";
			}
			FinishHostScan(closure, state, null);
		}
		catch (Exception ex)
		{
			FinishHostScan(closure, state, "Could not verify clash geometry: " + ex.Message);
		}
	}

	private static bool MatchesChoice(string choice, string a, string b)
	{
		return choice == "ANY" || choice == a || choice == b;
	}

	private static string ChoiceCode(string choice)
	{
		return choice switch
		{
			"Electrical / Plumbing" => "ELEC", 
			"Mechanical / HVAC" => "MEP", 
			"Structure" => "STR", 
			"Architecture" => "ARCH", 
			_ => "ANY", 
		};
	}

	private static void FinishHostScan(object closure, HostScanState state, string message)
	{
		if (!string.IsNullOrEmpty(message))
		{
			SetHostScanStatus(closure, state, message);
		}
		bool flag = false;
		lock (hostScans)
		{
			if (state != null)
			{
				state.Running = false;
				flag = state.Pending;
				state.Pending = false;
			}
		}
		if (flag && state != null && state.Dispatcher != null && !state.Form.IsDisposed)
		{
			QueueHostApiAction(state, () =>
			{
				CollectHostScanInputs(closure, state);
			});
		}
	}

	private static void QueueHostApiAction(HostScanState state, Action action)
	{
		try
		{
			if (state == null || state.Form == null || state.Form.IsDisposed || !state.Form.IsHandleCreated)
			{
				return;
			}
			state.Form.BeginInvoke((MethodInvoker)(() =>
			{
				if (!state.Form.IsDisposed && !state.Dispatcher.Post(action))
				{
					FinishHostScan(null, state, "Revit is busy. Please try the clash scan again.");
				}
			}));
		}
		catch
		{
			FinishHostScan(null, state, "The clash scan could not be scheduled.");
		}
	}

	private static void SetHostScanStatus(object closure, HostScanState state, string message)
	{
		try
		{
			Label label = (closure as Label) ?? ((state == null) ? (ClosureField(closure, "status") as Label) : state.Status);
			if (label == null || label.IsDisposed)
			{
				return;
			}
			if (label.InvokeRequired)
			{
				label.BeginInvoke((MethodInvoker)(() =>
				{
					if (!label.IsDisposed)
					{
						label.Text = message;
					}
				}));
			}
			else
			{
				label.Text = message;
			}
		}
		catch
		{
		}
	}

	private static object ClosureField(object root, string name)
	{
		Queue<object> queue = new Queue<object>();
		HashSet<object> hashSet = new HashSet<object>();
		if (root != null)
		{
			queue.Enqueue(root);
		}
		while (queue.Count > 0)
		{
			object obj = queue.Dequeue();
			if (obj == null || !hashSet.Add(obj))
			{
				continue;
			}
			Type type = obj.GetType();
			FieldInfo[] fields = type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				object obj2 = null;
				try
				{
					obj2 = fieldInfo.GetValue(obj);
				}
				catch
				{
				}
				if (fieldInfo.Name == name)
				{
					return obj2;
				}
				if (obj2 != null && fieldInfo.FieldType.FullName != null && (fieldInfo.FieldType.FullName.StartsWith("BIMlify.BimActions/<>c__DisplayClass", StringComparison.Ordinal) || fieldInfo.FieldType.FullName.StartsWith("BIMlify.BimActions+<>c__DisplayClass", StringComparison.Ordinal)))
				{
					queue.Enqueue(obj2);
				}
			}
		}
		return null;
	}

	public static void Attach(Form form, UIDocument ui)
	{
		//IL_12d8: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			List<ComboBox> source = All(form).OfType<ComboBox>().ToList();
			ComboBox scope = source.FirstOrDefault((ComboBox c) => c.Items.Cast<object>().Any((object x) => (x?.ToString() ?? "").StartsWith("Scan ", StringComparison.OrdinalIgnoreCase)));
			List<ComboBox> source2 = (from c in source
				where c.Items.Cast<object>().Any((object x) => (x?.ToString() ?? "") == "All disciplines")
				orderby AbsoluteLeft(c)
				select c).ToList();
			ComboBox disciplineA = source2.ElementAtOrDefault(0);
			ComboBox disciplineB = source2.ElementAtOrDefault(1);
			Label scopeLabel = All(form).OfType<Label>().FirstOrDefault((Label l) => l.Text == "SCAN SCOPE");
			ListBox results = All(form).OfType<ListBox>().FirstOrDefault();
			Control resultsPanel = ((results == null) ? null : results.Parent);
			Label resultsHeader = ((resultsPanel == null) ? null : resultsPanel.Controls.OfType<Label>().FirstOrDefault((Label l) => l.Text == "Detected clashes"));
			SplitContainer clashSplit = All(form).OfType<SplitContainer>().FirstOrDefault((SplitContainer x) => All(x.Panel1).Contains(results));
			if (scope == null || disciplineA == null || disciplineB == null || results == null)
			{
				return;
			}
			Control cardA = ReplaceRoundedCard(disciplineA.Parent);
			Control cardB = ReplaceRoundedCard(disciplineB.Parent);
			Control pair = cardA.Parent;
			Control rules = pair.Parent;
			Control scanPanel = scope.Parent;
			Button run = All(form).OfType<Button>().FirstOrDefault((Button b) => b.Text == "Run Clash Test");
			Button review = All(form).OfType<Button>().FirstOrDefault((Button b) => b.Text == "Review in Revit");
			Button copyA = All(form).OfType<Button>().FirstOrDefault((Button b) => b.Text == "Copy A ID");
			Button copyB = All(form).OfType<Button>().FirstOrDefault((Button b) => b.Text == "Copy B ID");
			Button button = All(form).OfType<Button>().FirstOrDefault((Button b) => b.Text == "Find IDs" || b.Text == "Refresh");
			FlowLayoutPanel flowLayoutPanel = ((copyA == null) ? null : (copyA.Parent as FlowLayoutPanel));
			if (button != null)
			{
				DetachHandler(button, "click");
				button.Text = "Refresh";
				button.Width = 84;
				button.Height = 28;
				button.Enabled = true;
				button.Visible = true;
				button.DialogResult = DialogResult.None;
				button.AccessibleName = "Refresh clash results by running the current clash scan again";
			}
			Label status = All(form).OfType<Label>().FirstOrDefault((Label l) => l.Text.StartsWith("Ready to scan", StringComparison.OrdinalIgnoreCase));
			Label detail = All(form).OfType<Label>().FirstOrDefault((Label l) => l.Text.StartsWith("Select a clash to see the reason", StringComparison.OrdinalIgnoreCase));
			Panel footer = form.Controls.OfType<Panel>().FirstOrDefault((Panel p) => p.Dock == DockStyle.Bottom && p.Height >= 50 && p.Controls.OfType<Panel>().Any((Panel child) => child.Dock == DockStyle.Right && child.Controls.OfType<Button>().Any((Button b) => b.Text == "Close")));
			Label previewTitle = All(form).OfType<Label>().FirstOrDefault((Label l) => l.Text.StartsWith("Clash details", StringComparison.OrdinalIgnoreCase));
			Label previewText = All(form).OfType<Label>().FirstOrDefault((Label l) => l.Text.StartsWith("Select a clash from the left panel", StringComparison.OrdinalIgnoreCase));
			Control previewCard = ((previewTitle == null) ? null : previewTitle.Parent);
			Color backColor = Color.FromArgb(255, 248, 226);
			if (detail != null)
			{
				detail.AutoSize = false;
				detail.Font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
				detail.TextAlign = ContentAlignment.MiddleLeft;
				detail.UseCompatibleTextRendering = false;
				detail.AutoEllipsis = true;
				detail.Padding = new Padding(2, 0, 2, 0);
				detail.Height = 20;
			}
			if (status != null)
			{
				status.AutoSize = false;
				status.Font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
				status.TextAlign = ContentAlignment.MiddleLeft;
				status.UseCompatibleTextRendering = false;
				status.AutoEllipsis = true;
				status.Padding = Padding.Empty;
				status.Height = 20;
			}
			bool formattingFooterText = false;
			if (detail != null)
			{
				detail.TextChanged += (object s, EventArgs e) =>
				{
					if (!formattingFooterText)
					{
						string text = ShortenClashFooterDetail(detail.Text);
						if (text != detail.Text)
						{
							formattingFooterText = true;
							detail.Text = text;
							formattingFooterText = false;
						}
					}
				};
			}
			if (status != null)
			{
				status.TextChanged += (object s, EventArgs e) =>
				{
					if (!formattingFooterText)
					{
						string text = ShortenClashFooterStatus(status.Text);
						if (text != status.Text)
						{
							formattingFooterText = true;
							status.Text = text;
							formattingFooterText = false;
						}
					}
					if (Regex.IsMatch(status.Text ?? string.Empty, "^\\s*\\d+\\s+(?:potential clashes found|linked clashes? found)\\b", RegexOptions.IgnoreCase))
					{
						status.TextAlign = ContentAlignment.MiddleLeft;
					}
				};
			}
			if (footer != null && (detail != null || status != null))
			{
				footer.Height = 64;
				footer.BackColor = Color.FromArgb(216, 221, 227);
				if (detail != null)
				{
					Control parent = detail.Parent;
					parent?.Controls.Remove(detail);
					footer.Controls.Add(detail);
					detail.Dock = DockStyle.None;
					detail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
					detail.BackColor = footer.BackColor;
					parent?.PerformLayout();
				}
				if (status != null)
				{
					Control parent2 = status.Parent;
					parent2?.Controls.Remove(status);
					footer.Controls.Add(status);
					status.Dock = DockStyle.None;
					status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
					status.BackColor = footer.BackColor;
					parent2?.PerformLayout();
				}
				Panel footerActions = footer.Controls.OfType<Panel>().FirstOrDefault((Panel p) => p.Dock == DockStyle.Right);
				Action layoutFooterConclusion = () =>
				{
					int num2 = ((footerActions == null) ? (footer.ClientSize.Width - 12) : (footerActions.Left - 12));
					int width = Math.Max(80, num2 - 18);
					if (detail != null)
					{
						detail.SetBounds(16, 8, width, 20);
					}
					if (status != null)
					{
						status.SetBounds(16, 30, width, 20);
					}
					if (detail != null)
					{
						detail.BringToFront();
					}
					if (status != null)
					{
						status.BringToFront();
					}
					if (footerActions != null)
					{
						footerActions.BringToFront();
					}
				};
				footer.Resize += (object s, EventArgs e) =>
				{
					layoutFooterConclusion();
				};
				form.Resize += (object s, EventArgs e) =>
				{
					if (!form.IsDisposed)
					{
						if (form.IsHandleCreated)
						{
							try
							{
								form.BeginInvoke(layoutFooterConclusion);
								return;
							}
							catch
							{
								return;
							}
						}
						layoutFooterConclusion();
					}
				};
				if (detail != null)
				{
					detail.TextChanged += (object s, EventArgs e) =>
					{
						layoutFooterConclusion();
					};
				}
				if (status != null)
				{
					status.TextChanged += (object s, EventArgs e) =>
					{
						layoutFooterConclusion();
					};
				}
				form.Shown += (object s, EventArgs e) =>
				{
					if (form.IsHandleCreated)
					{
						form.BeginInvoke(layoutFooterConclusion);
					}
					else
					{
						layoutFooterConclusion();
					}
				};
				layoutFooterConclusion();
				clashSplit?.Parent?.PerformLayout();
			}
			ComboBox categoryFilterA = ((resultsPanel == null) ? null : new ComboBox
			{
				Height = 25,
				DropDownStyle = ComboBoxStyle.DropDownList,
				FlatStyle = FlatStyle.Flat,
				Font = new Font("Segoe UI", 8.5f),
				BackColor = Color.FromArgb(255, 248, 226),
				ForeColor = Color.FromArgb(67, 75, 82),
				MaxDropDownItems = 18,
				IntegralHeight = false,
				DropDownHeight = 360,
				Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right)
			});
			ComboBox categoryFilterB = ((resultsPanel == null) ? null : new ComboBox
			{
				Height = 25,
				DropDownStyle = ComboBoxStyle.DropDownList,
				FlatStyle = FlatStyle.Flat,
				Font = new Font("Segoe UI", 8.5f),
				BackColor = Color.FromArgb(255, 248, 226),
				ForeColor = Color.FromArgb(67, 75, 82),
				MaxDropDownItems = 18,
				IntegralHeight = false,
				DropDownHeight = 360,
				Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right)
			});
			Label categoryCaptionA = ((resultsPanel == null) ? null : new Label
			{
				Text = "D1 Category",
				Height = 25,
				Font = new Font("Segoe UI", 7.5f),
				ForeColor = Color.FromArgb(78, 85, 92),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoSize = false
			});
			Label categoryCaptionB = ((resultsPanel == null) ? null : new Label
			{
				Text = "D2 Category",
				Height = 25,
				Font = new Font("Segoe UI", 7.5f),
				ForeColor = Color.FromArgb(78, 85, 92),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoSize = false
			});
			Label categoryVs = ((resultsPanel == null) ? null : new Label
			{
				Text = "VS",
				Height = 25,
				Font = new Font("Segoe UI", 8f, FontStyle.Bold),
				ForeColor = Color.FromArgb(198, 151, 42),
				TextAlign = ContentAlignment.MiddleCenter,
				AutoSize = false
			});
			ToolTip categoryTips = new ToolTip
			{
				AutoPopDelay = 10000,
				InitialDelay = 350,
				ReshowDelay = 100
			};
			if (categoryFilterA != null)
			{
				categoryFilterA.Items.Add("All categories");
				categoryFilterA.SelectedIndex = 0;
				categoryFilterA.Visible = true;
				categoryFilterA.AccessibleName = "Filter clashes by Discipline 01 category";
				categoryTips.SetToolTip(categoryCaptionA, "Category of Discipline 01");
				resultsPanel.Controls.Add(categoryFilterA);
				resultsPanel.Controls.Add(categoryCaptionA);
			}
			if (categoryFilterB != null)
			{
				categoryFilterB.Items.Add("All categories");
				categoryFilterB.SelectedIndex = 0;
				categoryFilterB.Visible = true;
				categoryFilterB.AccessibleName = "Filter clashes by Discipline 02 category";
				categoryTips.SetToolTip(categoryCaptionB, "Category of Discipline 02");
				resultsPanel.Controls.Add(categoryFilterB);
				resultsPanel.Controls.Add(categoryCaptionB);
			}
			if (categoryVs != null)
			{
				resultsPanel.Controls.Add(categoryVs);
			}
			if (resultsPanel != null)
			{
				resultsPanel.Dock = DockStyle.Fill;
				resultsPanel.BackColor = Color.White;
				ClearPaintHandlers(resultsPanel);
				if (resultsPanel is Panel panel)
				{
					panel.BorderStyle = BorderStyle.None;
				}
			}
			if (previewCard != null)
			{
				ClearPaintHandlers(previewCard);
				if (previewCard is Panel panel2)
				{
					panel2.BorderStyle = BorderStyle.None;
				}
				previewCard.Dock = DockStyle.Fill;
				previewCard.Padding = new Padding(8);

			}
			if (clashSplit != null)
			{
				ClearPaintHandlers(clashSplit.Panel1);
				clashSplit.Dock = DockStyle.None;
				clashSplit.Visible = true;
				clashSplit.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
				clashSplit.BorderStyle = BorderStyle.None;
				clashSplit.SplitterWidth = 12;
				clashSplit.BackColor = Color.White;
				clashSplit.Panel1.Padding = Padding.Empty;
				clashSplit.Panel2.Padding = Padding.Empty;
				Control parent3 = clashSplit.Parent;
				if (parent3 != null)
				{
					parent3.Paint += (object s, PaintEventArgs e) =>
					{
						if (clashSplit.IsDisposed || clashSplit.Width < 4 || clashSplit.Height < 4)
						{
							return;
						}
						Rectangle rectangle = new Rectangle(clashSplit.Left - 2, clashSplit.Top - 2, clashSplit.Width + 3, clashSplit.Height + 4);
						e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
						using GraphicsPath path = RoundedRectangle(rectangle, 10);
						using Pen pen = new Pen(Color.FromArgb(190, 197, 204), 1f);
						e.Graphics.DrawPath(pen, path);
					};
				}
			}
			if (previewTitle != null)
			{
				previewTitle.TextAlign = ContentAlignment.MiddleLeft;
				previewTitle.Padding = Padding.Empty;
				previewTitle.Dock = DockStyle.None;
				previewTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			}
			if (resultsHeader != null)
			{
				resultsHeader.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
				resultsHeader.TextAlign = ContentAlignment.MiddleLeft;
				resultsHeader.Padding = new Padding(16, 0, 0, 0);
			}
			Font previewFitFont = null;
			if (previewText != null)
			{
				previewText.Dock = DockStyle.None;
				previewText.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
				previewFitFont = new Font("Segoe UI", 8f, FontStyle.Regular);
				previewText.Font = previewFitFont;
				previewText.BackColor = backColor;
				previewText.ForeColor = Color.FromArgb(82, 78, 67);
				previewText.TextAlign = ContentAlignment.TopLeft;
				previewText.Padding = new Padding(0, 2, 0, 0);
				previewText.AutoEllipsis = false;
				previewText.AutoSize = false;
				previewText.UseCompatibleTextRendering = false;
				if (previewText.Parent != null)
				{
					previewText.Parent.BackColor = backColor;
					foreach (FlowLayoutPanel item in previewText.Parent.Controls.OfType<FlowLayoutPanel>())
					{
						item.BackColor = backColor;
					}
				}
			}
			if (previewTitle != null)
			{
				previewTitle.BackColor = backColor;
				previewTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
				previewTitle.ForeColor = Color.FromArgb(78, 70, 51);
			}
			Action layoutPreviewTitle = () =>
			{
				if (previewCard == null)
				{
					return;
				}
				if (previewTitle != null)
				{
					previewTitle.SetBounds(16, 2, Math.Max(80, previewCard.ClientSize.Width - 24), 25);
				}
				if (previewText != null)
				{
					int num2 = (from x in previewCard.Controls.OfType<FlowLayoutPanel>()
						select x.Height).DefaultIfEmpty(36).First();
					previewText.SetBounds(8, 30, Math.Max(80, previewCard.ClientSize.Width - 16), Math.Max(28, previewCard.ClientSize.Height - 30 - num2 - 8));
				}
				if (previewText != null && previewText.Width > 0 && previewText.Height > 0)
				{
					int width = Math.Max(40, previewText.ClientSize.Width - previewText.Padding.Horizontal);
					int num3 = Math.Max(12, previewText.ClientSize.Height - previewText.Padding.Vertical);
					float num4 = 6f;
					for (float num5 = 8f; num5 >= 6f; num5 -= 0.25f)
					{
						using Font font = new Font("Segoe UI", num5, FontStyle.Regular);
						if (TextRenderer.MeasureText(previewText.Text ?? string.Empty, font, new Size(width, int.MaxValue), TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height <= num3)
						{
							num4 = num5;
							break;
						}
					}
					if (previewFitFont == null || Math.Abs(previewFitFont.Size - num4) > 0.01f)
					{
						Font font2 = previewFitFont;
						previewFitFont = new Font("Segoe UI", num4, FontStyle.Regular);
						previewText.Font = previewFitFont;
						font2?.Dispose();
					}
				}
				previewTitle?.BringToFront();
				foreach (FlowLayoutPanel item2 in previewCard.Controls.OfType<FlowLayoutPanel>())
				{
					item2.BringToFront();
				}
			};
			if (previewText != null)
			{
				previewText.TextChanged += (object s, EventArgs e) =>
				{
					layoutPreviewTitle();
				};
			}
			if (previewCard != null)
			{
				previewCard.Resize += (object s, EventArgs e) =>
				{
					layoutPreviewTitle();
				};
				layoutPreviewTitle();
			}
			Action<Button> makeGold = (Button button4) =>
			{
				if (button4 != null)
				{
					PropertyInfo property = button4.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
					{
						property.SetValue(button4, true, null);
					}
					button4.ForeColor = Color.FromArgb(110, 76, 8);
					button4.Invalidate();
				}
			};
			Action<Button, Color> action = (Button button4, Color surface) =>
			{
				if (button4 != null)
				{
					PropertyInfo property = button4.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
					{
						property.SetValue(button4, false, null);
					}
					if (button4.Parent is FlowLayoutPanel flowLayoutPanel2)
					{
						int width = button4.Width;
						int height = button4.Height;
						Padding margin = button4.Margin;
						flowLayoutPanel2.Controls.Remove(button4);
						Panel panel3 = new Panel
						{
							Width = width,
							Height = height,
							BackColor = flowLayoutPanel2.BackColor,
							Margin = margin,
							Padding = Padding.Empty
						};
						button4.Margin = Padding.Empty;
						button4.Dock = DockStyle.Fill;
						panel3.Controls.Add(button4);
						flowLayoutPanel2.Controls.Add(panel3);
						button4.BringToFront();
					}
					else
					{
						button4.BackColor = surface;
					}
					button4.Name = button4 == review ? "BIMlifyClashReviewAction" : "BIMlifyClashDetailAction";
					button4.ForeColor = Color.FromArgb(67, 75, 82);
					button4.Invalidate();
				}
			};
			makeGold(run);
			makeGold(review);
			ToolTip linkTips = new ToolTip
			{
				AutoPopDelay = 15000,
				InitialDelay = 300,
				ReshowDelay = 100
			};
			List<RevitLinkInstance> links = (from RevitLinkInstance l in (IEnumerable)new FilteredElementCollector(ui.Document).OfClass(typeof(RevitLinkInstance))
				where l.GetLinkDocument() != null
				orderby ((Element)l).Name
				select l).ToList();
			CheckBox include = new CheckBox
			{
				Text = "Include linked elements",
				AutoSize = false,
				Height = 24,
				Width = 270,
				Top = 13,
				ForeColor = Color.FromArgb(67, 75, 82),
				Font = new Font("Segoe UI", 8f)
			};
			scanPanel.Controls.Add(include);
			Action layoutScanOptions = () =>
			{
				int width = scanPanel.ClientSize.Width;
				int num2 = Math.Min(190, Math.Max(150, width / 4));
				int num3 = Math.Max(14, width - num2 - 14);
				include.SetBounds(num3, 13, num2, 24);
				int x = 14;
				int num4 = 126;
				int width2 = Math.Max(120, Math.Min(260, num3 - num4 - 16));
				if (scopeLabel != null)
				{
					scopeLabel.AutoSize = false;
					scopeLabel.TextAlign = ContentAlignment.MiddleLeft;
					scopeLabel.SetBounds(x, 18, 104, 22);
				}
				scope.SetBounds(num4, 10, width2, 32);
			};
			layoutScanOptions();
			scanPanel.SizeChanged += (object s, EventArgs e) =>
			{
				layoutScanOptions();
			};
			ComboBox sourceA = SourcePicker(cardA);
			ComboBox sourceB = SourcePicker(cardB);
			lock (hostOnlyScanEnabled)
			{
				hostOnlyScanEnabled[form] = () => !include.Checked || (sourceA.SelectedIndex == 0 && sourceB.SelectedIndex == 0);
			}
			ComboBox[] array = new ComboBox[2] { sourceA, sourceB };
			foreach (ComboBox picker in array)
			{
				picker.BackColor = disciplineA.BackColor;
				picker.ForeColor = disciplineA.ForeColor;
				picker.Font = disciplineA.Font;
				picker.FlatStyle = disciplineA.FlatStyle;
				picker.Height = disciplineA.Height;
				picker.ItemHeight = disciplineA.ItemHeight;
				picker.IntegralHeight = false;
				picker.MaxDropDownItems = 12;
				picker.DropDownHeight = Math.Max(picker.ItemHeight + 12, Math.Min(300, picker.Items.Count * Math.Max(20, picker.ItemHeight + 2)));
				picker.DropDown += (object s, EventArgs e) =>
				{
					picker.DropDownHeight = Math.Max(picker.ItemHeight + 12, Math.Min(300, picker.Items.Count * Math.Max(20, picker.ItemHeight + 2)));
				};
			}
			Panel sourceDividerA = new Panel
			{
				Height = 1,
				BackColor = Color.FromArgb(218, 222, 226)
			};
			Panel sourceDividerB = new Panel
			{
				Height = 1,
				BackColor = Color.FromArgb(218, 222, 226)
			};
			cardA.Controls.Add(sourceDividerA);
			cardB.Controls.Add(sourceDividerB);
			Button button2 = cardA.Controls.OfType<Button>().FirstOrDefault((Button b) => (b.Text ?? "").IndexOf("DISCIPLINE", StringComparison.OrdinalIgnoreCase) >= 0);
			Button button3 = cardB.Controls.OfType<Button>().FirstOrDefault((Button b) => (b.Text ?? "").IndexOf("DISCIPLINE", StringComparison.OrdinalIgnoreCase) >= 0);
			if (button2 != null)
			{
				button2.Text = "DISCIPLINE A"; button2.BackColor = Color.FromArgb(225, 242, 224);
			}
			if (button3 != null)
			{
				button3.Text = "DISCIPLINE B"; button3.BackColor = Color.FromArgb(250, 230, 168);
			}
			pair.Dock = DockStyle.None;
			pair.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			if (clashSplit != null)
			{
				clashSplit.FixedPanel = FixedPanel.None;
				clashSplit.IsSplitterFixed = false;
				Action equalizeClashPanels = () =>
				{
					int num2 = clashSplit.ClientSize.Width - clashSplit.SplitterWidth;
					if (num2 < 440)
					{
						return;
					}
					try
					{
						clashSplit.Panel1MinSize = 0;
						clashSplit.Panel2MinSize = 0;
						int num3 = num2 / 2;
						if (Math.Abs(clashSplit.SplitterDistance - num3) > 2)
						{
							clashSplit.SplitterDistance = num3;
						}
						clashSplit.Panel1MinSize = 220;
						clashSplit.Panel2MinSize = 220;
					}
					catch (ArgumentException)
					{
					}
				};
				clashSplit.SizeChanged += (object s, EventArgs e) =>
				{
					equalizeClashPanels();
				};
				form.Shown += (object s, EventArgs e) =>
				{
					if (form.IsHandleCreated)
					{
						form.BeginInvoke(equalizeClashPanels);
					}
					else
					{
						equalizeClashPanels();
					}
				};
				equalizeClashPanels();
			}
			if (flowLayoutPanel != null)
			{
				Padding padding = flowLayoutPanel.Padding;
				flowLayoutPanel.Padding = new Padding(padding.Left + 8, padding.Top, padding.Right, padding.Bottom);
			}
			if (review != null && flowLayoutPanel != null)
			{
				if (review.Parent != null)
				{
					review.Parent.Controls.Remove(review);
				}
				flowLayoutPanel.Controls.Add(review);
				review.Width = 122;
				review.Height = 28;
				review.Text = "Review in Revit";
			}
			if (run != null)
			{
				if (run.Parent != null)
				{
					run.Parent.Controls.Remove(run);
				}
				rules.Controls.Add(run);
				run.Width = 126;
				run.Height = 28;
				run.Text = "Run Clash Test";
				run.Visible = true;
				run.Enabled = true;
				run.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			}
			makeGold(run);
			makeGold(review);
			makeGold(button);
			action(review, Color.FromArgb(255, 232, 232));
			action(copyA, Color.White);
			action(copyB, Color.White);
			action(button, Color.White);
			Action layoutCards = () =>
			{
				int width = Math.Max(400, rules.ClientSize.Width - 20);
				int disciplineHeight = include.Checked ? 126 : 94;
                pair.SetBounds(10, 32, width, disciplineHeight);
				int num2 = Math.Max(400, pair.ClientSize.Width);
				int num3 = Math.Min(260, Math.Max(100, num2 / 4));
				int num4 = Math.Max(220, (num2 - num3) / 2);
				cardA.SetBounds(0, 4, num4, disciplineHeight - 8);
				cardB.SetBounds(num2 - num4, 4, num4, disciplineHeight - 8);
				foreach (Button item3 in cardA.Controls.OfType<Button>())
				{
					item3.SetBounds(12, 7, Math.Max(180, num4 - 24), 26);
				}
				foreach (Button item4 in cardB.Controls.OfType<Button>())
				{
					item4.SetBounds(12, 7, Math.Max(180, num4 - 24), 26);
				}
				int width2 = Math.Max(180, num4 - 32);
				disciplineA.SetBounds(16, 39, width2, 34);
				disciplineB.SetBounds(16, 39, width2, 34);
				sourceDividerA.SetBounds(16, 76, width2, 1);
				sourceDividerB.SetBounds(16, 76, width2, 1);
				sourceA.SetBounds(16, 81, width2, 34);
				sourceB.SetBounds(16, 81, width2, 34);
				ComboBox[] array2 = new ComboBox[2] { sourceA, sourceB };
				foreach (ComboBox picker2 in array2)
				{
					int val = (from object x in picker2.Items
						select TextRenderer.MeasureText(x.ToString(), picker2.Font).Width + 40).DefaultIfEmpty(180).Max();
					picker2.DropDownWidth = Math.Min(Math.Max(picker2.Width, val), Math.Max(picker2.Width, form.ClientSize.Width - 48));
				}
				if (run != null)
				{
					run.SetBounds(Math.Max(0, rules.ClientSize.Width - 138), pair.Bottom + 4, 126, 28);
					run.Visible = true;
					run.Enabled = true;
					makeGold(run);
					run.BringToFront();
				}
			};
			Action ensureRunRow = () =>
			{
				layoutCards();
				if (run != null)
				{
                    int requiredHeight = pair.Bottom + 4 + run.Height + 12;
                    if (rules.Height != requiredHeight) rules.Height = requiredHeight;
					run.SetBounds(Math.Max(0, rules.ClientSize.Width - 138), pair.Bottom + 4, 126, 28);
					run.Visible = true;
					run.Enabled = true;
					run.BringToFront();
					rules.Parent?.PerformLayout();
				}
			};
			layoutCards();
			rules.Resize += (object s, EventArgs e) =>
			{
				layoutCards();
			};
			form.Shown += (object s, EventArgs e) =>
			{
				if (form.IsHandleCreated)
				{
					form.BeginInvoke(ensureRunRow);
				}
				else
				{
					ensureRunRow();
				}
			};
			form.Resize += (object s, EventArgs e) =>
			{
				if (form.IsHandleCreated)
				{
					form.BeginInvoke(ensureRunRow);
				}
			};
			Dictionary<string, Clash> clashes = new Dictionary<string, Clash>(StringComparer.Ordinal);
			List<string> allRows = (from object x in results.Items
				select x as string into x
				where x != null
				select x).ToList();
			HashSet<string> solvedRows = new HashSet<string>(StringComparer.Ordinal);
			Func<string, string> clashIdentity = (string row) =>
			{
				if (string.IsNullOrEmpty(row))
				{
					return string.Empty;
				}
				int num2 = row.LastIndexOf(" — ", StringComparison.Ordinal);
				if (num2 < 0)
				{
					return row;
				}
				string text = row.Substring(num2 + 3).Trim();
				return row.StartsWith("LINK CLASH · ", StringComparison.Ordinal) ? ("LINK:" + row.Substring(0, num2) + "|" + text) : ("HOST:" + text);
			};

            Dictionary<string, int> rowPositions = new Dictionary<string, int>(StringComparer.Ordinal);
            int nextRowPosition = 0;
            foreach (string initialRow in allRows)
            {
                string identity = clashIdentity(initialRow);
                if (!rowPositions.ContainsKey(identity)) rowPositions[identity] = nextRowPosition++;
            }
			bool hostScanBaselineReady = false;
			bool linkedScanBaselineReady = false;
			Action<int> showSolvedMessage = (int count) =>
			{
				if (count > 0 && !form.IsDisposed)
				{
					string text = ((count == 1) ? "Clash solved successfully." : (count + " clashes solved successfully."));
					MessageBox.Show(form, text, "Clash Detection", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
			};
			results.DrawMode = DrawMode.OwnerDrawFixed;
			results.ItemHeight = Math.Max(20, results.Font.Height + 4);
			results.DrawItem += (object s, DrawItemEventArgs e) =>
			{
				if (e.Index >= 0 && e.Index < results.Items.Count)
				{
					string row = (results.Items[e.Index] as string) ?? string.Empty;
					bool flag = solvedRows.Any((string saved) => clashIdentity(saved) == clashIdentity(row));
					bool flag2 = (e.State & DrawItemState.Selected) != 0;
					Color color = ((!flag) ? (flag2 ? Color.FromArgb(255, 211, 211) : Color.FromArgb(255, 239, 239)) : (flag2 ? Color.FromArgb(199, 232, 205) : Color.FromArgb(230, 246, 233)));
					using (SolidBrush brush = new SolidBrush(color))
					{
						e.Graphics.FillRectangle(brush, e.Bounds);
					}
					string text = row;
					if (row.StartsWith("LINK CLASH · ", StringComparison.Ordinal))
					{
						int num2 = 0;
						for (int num3 = 0; num3 <= e.Index; num3++)
						{
							if (((results.Items[num3] as string) ?? string.Empty).StartsWith("LINK CLASH · ", StringComparison.Ordinal))
							{
								num2++;
							}
						}
						text = "Clash " + num2 + " · " + row;
					}
					string text2 = (flag ? (text + "  ·  Solved") : text);
					TextRenderer.DrawText(e.Graphics, text2, results.Font, e.Bounds, Color.FromArgb(67, 75, 82), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
					if ((e.State & DrawItemState.Focus) != 0)
					{
						ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds);
					}
				}
			};
			Func<string, string[]> rowCategorySides = (string row) =>
			{
				List<string> list = new List<string>();
				foreach (Match item5 in Regex.Matches(row, "\\[(?<discipline>[^\\]]+)\\]\\s*(?<category>[^/—]+?)\\s*(?=/\\s*\\[|—)"))
				{
					list.Add(item5.Groups["category"].Value.Trim());
				}
				if (list.Count >= 2)
				{
					return list.Take(2).ToArray();
				}
				if (row.StartsWith("LINK CLASH", StringComparison.OrdinalIgnoreCase))
				{
					int num2 = row.LastIndexOf(" · ", StringComparison.Ordinal);
					if (num2 >= 0)
					{
						string text = row.Substring(num2 + 3);
						int num3 = text.IndexOf(" — ", StringComparison.Ordinal);
						if (num3 >= 0)
						{
							text = text.Substring(0, num3);
						}
						string[] array2 = (from x in text.Split(new string[1] { " / " }, StringSplitOptions.RemoveEmptyEntries)
							select x.Trim() into x
							where x.Length > 0
							select x).Take(2).ToArray();
						if (array2.Length == 2)
						{
							return array2;
						}
					}
				}
				return list.ToArray();
			};
			List<string> documentCategories = (from n in (from Category c in (IEnumerable)ui.Document.Settings.Categories
					select c.Name).Concat(links.SelectMany((RevitLinkInstance l) => from Category c in (IEnumerable)l.GetLinkDocument().Settings.Categories
					select c.Name))
				where !string.IsNullOrWhiteSpace(n)
				select n).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			Action refreshCategoryOptions = () =>
			{
				if (categoryFilterA != null && categoryFilterB != null)
				{
					string text = categoryFilterA.SelectedItem as string;
					string text2 = categoryFilterB.SelectedItem as string;
					List<string> list = new string[1] { "All categories" }.Concat((from x in documentCategories.Concat(allRows.SelectMany(rowCategorySides))
						where !string.IsNullOrWhiteSpace(x)
						select x).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy((string x) => x, StringComparer.OrdinalIgnoreCase)).ToList();
					ComboBox[] array2 = new ComboBox[2] { categoryFilterA, categoryFilterB };
					foreach (ComboBox comboBox in array2)
					{
						string text3 = ((comboBox == categoryFilterA) ? text : text2);
						comboBox.BeginUpdate();
						comboBox.Items.Clear();
						foreach (string item6 in list)
						{
							comboBox.Items.Add(item6);
						}
						int num3 = ((text3 == null) ? (-1) : comboBox.Items.IndexOf(text3));
						comboBox.SelectedIndex = ((num3 >= 0) ? num3 : 0);
						comboBox.EndUpdate();
					}
				}
			};
			Action renderRows = () =>
			{
				string text = results.SelectedItem as string;
				foreach (string row in allRows)
				{
					string identity = clashIdentity(row);
					if (!rowPositions.ContainsKey(identity)) rowPositions[identity] = nextRowPosition++;
				}
				string categoryA = ((categoryFilterA == null) ? "All categories" : (categoryFilterA.SelectedItem as string));
				string categoryB = ((categoryFilterB == null) ? "All categories" : (categoryFilterB.SelectedItem as string));
				results.BeginUpdate();
				results.Items.Clear();
				foreach (string item7 in allRows.Where((string x) =>
				{
					string[] array2 = rowCategorySides(x);
					bool flag = string.IsNullOrEmpty(categoryA) || categoryA == "All categories" || (array2.Length != 0 && string.Equals(array2[0], categoryA, StringComparison.OrdinalIgnoreCase));
					bool flag2 = string.IsNullOrEmpty(categoryB) || categoryB == "All categories" || (array2.Length > 1 && string.Equals(array2[1], categoryB, StringComparison.OrdinalIgnoreCase));
					return flag & flag2;
				}).OrderBy((string row) => rowPositions[clashIdentity(row)]))
				{
					results.Items.Add(item7);
				}
				if (text != null && results.Items.Contains(text))
				{
					results.SelectedItem = text;
				}
				results.EndUpdate();
			};
			lock (hostRowSynchronizers)
			{
				hostRowSynchronizers[form] = (List<string> rows) =>
				{
					List<string> list = allRows.Where((string row) => row.StartsWith("Clash ", StringComparison.OrdinalIgnoreCase) && !solvedRows.Contains(row)).ToList();
					HashSet<string> current2 = new HashSet<string>(rows.Select(clashIdentity), StringComparer.Ordinal);
					bool compareHostScan = hostScanBaselineReady;
					int num2 = compareHostScan ? list.Count((string row) => !current2.Contains(clashIdentity(row))) : 0;
					foreach (string item8 in list)
					{
						if (compareHostScan && !current2.Contains(clashIdentity(item8)))
						{
							solvedRows.Add(item8);
						}
					}
					hostScanBaselineReady = true;
					solvedRows.RemoveWhere((string row) => row.StartsWith("Clash ", StringComparison.OrdinalIgnoreCase) && current2.Contains(clashIdentity(row)));
					allRows.RemoveAll((string row) => row.StartsWith("Clash ", StringComparison.OrdinalIgnoreCase));
					allRows.AddRange(rows);
					foreach (string item9 in solvedRows.Where((string x) => x.StartsWith("Clash ", StringComparison.OrdinalIgnoreCase)))
					{
						if (!current2.Contains(clashIdentity(item9)) && !allRows.Contains(item9))
						{
							allRows.Add(item9);
						}
					}
					refreshCategoryOptions();
					renderRows();
					results.Invalidate();
					if (num2 > 0)
					{
						showSolvedMessage(num2);
					}
				};
			}
			form.FormClosed += (object s, FormClosedEventArgs e) =>
			{
				lock (hostRowSynchronizers)
				{
					hostRowSynchronizers.Remove(form);
				}
				lock (hostOnlyScanEnabled)
				{
					hostOnlyScanEnabled.Remove(form);
				}
			};
			refreshCategoryOptions();
			if (categoryFilterA != null)
			{
				categoryFilterA.SelectedIndexChanged += (object s, EventArgs e) =>
				{
					categoryTips.SetToolTip(categoryFilterA, "Category of Discipline 01: " + categoryFilterA.SelectedItem);
					renderRows();
				};
			}
			if (categoryFilterB != null)
			{
				categoryFilterB.SelectedIndexChanged += (object s, EventArgs e) =>
				{
					categoryTips.SetToolTip(categoryFilterB, "Category of Discipline 02: " + categoryFilterB.SelectedItem);
					renderRows();
				};
			}
			ToolTip toolTip = new ToolTip
			{
				AutoPopDelay = 5000,
				InitialDelay = 350,
				ReshowDelay = 100
			};
			Button previousClash = new Button
			{
				Text = "‹",
				Width = 24,
				Height = 22,
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(255, 239, 190),
				ForeColor = Color.FromArgb(110, 76, 8),
				Font = new Font("Segoe UI", 10f, FontStyle.Bold),
				Cursor = Cursors.Hand,
				TabStop = false
			};
			Button nextClash = new Button
			{
				Text = "›",
				Width = 24,
				Height = 22,
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(255, 239, 190),
				ForeColor = Color.FromArgb(110, 76, 8),
				Font = new Font("Segoe UI", 10f, FontStyle.Bold),
				Cursor = Cursors.Hand,
				TabStop = false
			};
			previousClash.FlatAppearance.BorderColor = Color.FromArgb(204, 156, 74);
			nextClash.FlatAppearance.BorderColor = Color.FromArgb(204, 156, 74);
			toolTip.SetToolTip(previousClash, "Select previous clash");
			toolTip.SetToolTip(nextClash, "Select next clash");
			Action layoutCategoryFilter = () =>
			{
			};
			if (resultsPanel != null)
			{
				resultsPanel.Controls.Add(previousClash);
				resultsPanel.Controls.Add(nextClash);
				layoutCategoryFilter = () =>
				{
					int width = resultsPanel.ClientSize.Width;
					int height = resultsPanel.ClientSize.Height;
					results.Dock = DockStyle.None;
					results.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
					previousClash.SetBounds(Math.Max(0, width - 58), 8, 24, 22);
					nextClash.SetBounds(Math.Max(0, width - 30), 8, 24, 22);
					if (resultsHeader != null)
					{
						resultsHeader.SetBounds(56, 8, 182, 25);
					}
					int num2 = 12;
					int num3 = 22;
					int num4 = 28;
					int num5 = 4;
					int num6 = width - 12;
					int num7 = Math.Max(220, num6 - num2);
					int num8 = Math.Max(80, (num7 - num3 * 2 - num4 - num5 * 4) / 2);
					int num9 = num2;
					if (categoryCaptionA != null)
					{
						categoryCaptionA.Text = "D1";
						categoryCaptionA.SetBounds(num9, 36, num3, 25);
						categoryCaptionA.AutoEllipsis = false;
						categoryCaptionA.TextAlign = ContentAlignment.MiddleLeft;
					}
					num9 += num3 + num5;
					if (categoryFilterA != null)
					{
						categoryFilterA.SetBounds(num9, 36, num8, 25);
						categoryFilterA.DropDownWidth = Math.Min(width - 24, Math.Max(num8, (from object v in categoryFilterA.Items
							select TextRenderer.MeasureText(v?.ToString() ?? "", categoryFilterA.Font).Width + 32).DefaultIfEmpty(num8).Max()));
					}
					num9 += num8 + num5;
					if (categoryVs != null)
					{
						categoryVs.SetBounds(num9, 36, num4, 25);
					}
					num9 += num4 + num5;
					if (categoryCaptionB != null)
					{
						categoryCaptionB.Text = "D2";
						categoryCaptionB.SetBounds(num9, 36, num3, 25);
						categoryCaptionB.AutoEllipsis = false;
						categoryCaptionB.TextAlign = ContentAlignment.MiddleLeft;
					}
					num9 += num3 + num5;
					if (categoryFilterB != null)
					{
						categoryFilterB.SetBounds(num9, 36, Math.Max(80, num6 - num9), 25);
						categoryFilterB.DropDownWidth = Math.Min(width - 24, Math.Max(categoryFilterB.Width, (from object v in categoryFilterB.Items
							select TextRenderer.MeasureText(v?.ToString() ?? "", categoryFilterB.Font).Width + 32).DefaultIfEmpty(categoryFilterB.Width).Max()));
					}
					results.SetBounds(4, 66, Math.Max(80, width - 8), Math.Max(28, height - 70));
					if (resultsHeader != null)
					{
						resultsHeader.BringToFront();
					}
					if (categoryCaptionA != null)
					{
						categoryCaptionA.BringToFront();
					}
					if (categoryCaptionB != null)
					{
						categoryCaptionB.BringToFront();
					}
					if (categoryFilterA != null)
					{
						categoryFilterA.BringToFront();
					}
					if (categoryFilterB != null)
					{
						categoryFilterB.BringToFront();
					}
					if (categoryVs != null)
					{
						categoryVs.BringToFront();
					}
					previousClash.BringToFront();
					nextClash.BringToFront();
				};
				layoutCategoryFilter();
				resultsPanel.Resize += (object s, EventArgs e) =>
				{
					layoutCategoryFilter();
				};
			}
			Action fitClashCards = () =>
			{
				if (clashSplit != null && !clashSplit.IsDisposed)
				{
					Control host = clashSplit.Parent;
					if (host != null)
					{
						int num2 = Math.Max(BottomInParent(rules, host), BottomInParent(scanPanel, host)) + 8;
						int height = Math.Max(120, host.ClientSize.Height - num2 - 4);
						clashSplit.SetBounds(2, num2, Math.Max(80, host.ClientSize.Width - 4), height);
						clashSplit.Visible = true;
						clashSplit.PerformLayout();
						resultsPanel?.PerformLayout();
						previewCard?.PerformLayout();
						layoutCategoryFilter?.Invoke();
						host.Invalidate();
						if (host.IsHandleCreated)
						{
							host.BeginInvoke((Action)(() =>
							{
								if (!clashSplit.IsDisposed)
								{
									int num3 = Math.Max(BottomInParent(rules, host), BottomInParent(scanPanel, host)) + 8;
									clashSplit.SetBounds(2, num3, Math.Max(80, host.ClientSize.Width - 4), Math.Max(120, host.ClientSize.Height - num3 - 4));
									clashSplit.PerformLayout();
									resultsPanel?.PerformLayout();
									previewCard?.PerformLayout();
									layoutCategoryFilter?.Invoke();
									host.Invalidate();
								}
							}));
						}
					}
				}
			};
			form.Shown += (object s, EventArgs e) =>
			{
				if (form.IsHandleCreated)
				{
					form.BeginInvoke(fitClashCards);
				}
				else
				{
					fitClashCards();
				}
			};
			form.Resize += (object s, EventArgs e) =>
			{
				if (form.IsHandleCreated)
				{
					form.BeginInvoke(fitClashCards);
				}
			};
			string clashPendingVerification = null;
			Action<int> moveClashSelection = (int direction) =>
			{
				if (results.Items.Count != 0)
				{
					int selectedIndex = results.SelectedIndex;
					if (direction > 0 && selectedIndex >= 0)
					{
						clashPendingVerification = results.Items[selectedIndex] as string;
					}
					int num2 = selectedIndex;
					if (num2 < 0)
					{
						num2 = ((direction > 0) ? (-1) : 0);
					}
					num2 = (num2 + direction + results.Items.Count) % results.Items.Count;
					results.SelectedIndex = num2;
					results.Focus();
				}
			};
			previousClash.Click += (object s, EventArgs e) =>
			{
				moveClashSelection(-1);
			};
			nextClash.Click += (object s, EventArgs e) =>
			{
				moveClashSelection(1);
			};
			Action clearLinked = () =>
			{
				allRows.RemoveAll((string row) => row.StartsWith("LINK CLASH · ", StringComparison.Ordinal) && !solvedRows.Contains(row));
				clashes.Clear();
				renderRows();
			};
			Func<ComboBox, Candidate[]> collect = (ComboBox comboBox) =>
			{
				//IL_018b: Unknown result type (might be due to invalid IL or missing references)
				//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
				//IL_0091: Unknown result type (might be due to invalid IL or missing references)
				//IL_00bb: Expected Obj, but got Unknown
				int selectedIndex = comboBox.SelectedIndex;
				if (selectedIndex <= 0)
				{
					View active = ui.Document.ActiveView;
					bool activeOnly = (scope.SelectedItem?.ToString() ?? "").IndexOf("Active View", StringComparison.OrdinalIgnoreCase) >= 0;
					FilteredElementCollector val = (activeOnly ? new FilteredElementCollector(ui.Document, ((Element)active).Id) : new FilteredElementCollector(ui.Document));
					return (from e in (IEnumerable<Element>)val.WhereElementIsNotElementType()
						where !(e is RevitLinkInstance)
						orderby (!(e is Wall)) ? 1 : 0
						select Make(e, null, activeOnly ? active : null, Transform.Identity, "Host model") into x
						where x != null
						select x).Take(1500).ToArray();
				}
				RevitLinkInstance link = links[selectedIndex - 1];
				Document linkDocument = link.GetLinkDocument();
				return (linkDocument == null) ? new Candidate[0] : (from e in (IEnumerable<Element>)new FilteredElementCollector(linkDocument).WhereElementIsNotElementType()
					orderby (!(e is Wall)) ? 1 : 0
					select Make(e, link, null, ((Instance)link).GetTotalTransform(), ((Element)link).Name) into x
					where x != null
					select x).Take(1500).ToArray();
			};
			Action scan = () =>
			{
				List<string> list = allRows.Where((string row) => row.StartsWith("LINK CLASH · ", StringComparison.Ordinal) && !solvedRows.Contains(row)).ToList();
				clearLinked();
				if (!include.Checked || (sourceA.SelectedIndex == 0 && sourceB.SelectedIndex == 0))
				{
					return;
				}
				try
				{
					Candidate[] array2 = collect(sourceA);
					Candidate[] array3 = collect(sourceB);
					string x = Code(disciplineA.SelectedItem?.ToString() ?? "");
					string y = Code(disciplineB.SelectedItem?.ToString() ?? "");
					int num2 = 0;
					List<string> list2 = new List<string>();
					bool flag = sourceA.SelectedIndex == sourceB.SelectedIndex;
					for (int num3 = 0; num3 < array2.Length; num3++)
					{
						if (num2 >= 300)
						{
							break;
						}
						for (int num4 = (flag ? (num3 + 1) : 0); num4 < array3.Length; num4++)
						{
							if (num2 >= 300)
							{
								break;
							}
							Candidate candidate = array2[num3];
							Candidate candidate2 = array3[num4];
							if (!((candidate.Element.Id == candidate2.Element.Id) & flag))
							{
								string discipline = candidate.Discipline;
								string discipline2 = candidate2.Discipline;
								if (Allowed(discipline, discipline2, x, y) && Overlap(candidate.Box, candidate2.Box) && GeometryIntersects(candidate, candidate2))
								{
									string text = "LINK CLASH · " + candidate.Source + " / " + candidate2.Source + " · " + candidate.Element.Category.Name + " / " + candidate2.Element.Category.Name + " — " + candidate.Element.Id.Value + " / " + candidate2.Element.Id.Value;
									string description = candidate.Element.Category.Name + " and " + candidate2.Element.Category.Name + " overlap. Check clearance, routing, or offsets.";
									clashes[text] = new Clash
									{
										A = candidate.Reference,
										B = candidate2.Reference,
										AId = candidate.Element.Id,
										BId = candidate2.Element.Id,
										LinkA = ((candidate.Link == null) ? null : ((Element)candidate.Link).Id),
										LinkB = ((candidate2.Link == null) ? null : ((Element)candidate2.Link).Id),
										BoxA = candidate.Box,
										BoxB = candidate2.Box,
										SourceA = candidate.Source,
										SourceB = candidate2.Source,
										Text = text,
										Description = description
									};
									if (!allRows.Contains(text))
									{
										allRows.Add(text);
									}
									list2.Add(text);
									num2++;
								}
							}
						}
					}
					HashSet<string> currentLinked = new HashSet<string>(list2.Select(clashIdentity), StringComparer.Ordinal);
					bool compareLinkedScan = linkedScanBaselineReady;
					int num5 = compareLinkedScan ? list.Count((string row) => !currentLinked.Contains(clashIdentity(row))) : 0;
					foreach (string item10 in list)
					{
						if (compareLinkedScan && !currentLinked.Contains(clashIdentity(item10)))
						{
							solvedRows.Add(item10);
						}
					}
					linkedScanBaselineReady = true;
					solvedRows.RemoveWhere((string row) => row.StartsWith("LINK CLASH · ", StringComparison.Ordinal) && currentLinked.Contains(clashIdentity(row)));
					foreach (string item11 in solvedRows.Where((string text2) => text2.StartsWith("LINK CLASH · ", StringComparison.Ordinal)))
					{
						if (!currentLinked.Contains(clashIdentity(item11)) && !allRows.Contains(item11))
						{
							allRows.Add(item11);
						}
					}
					refreshCategoryOptions();
					renderRows();
					results.Invalidate();
					if (status != null)
					{
						status.Text = num2 + " linked clash(es) found for the selected sources.";
					}
					if (num5 > 0)
					{
						showSolvedMessage(num5);
					}
				}
				catch (Exception ex)
				{
					if (status != null)
					{
						status.Text = "Linked-model scan failed: " + ex.Message;
					}
				}
			};
			include.CheckedChanged += (object s, EventArgs e) =>
			{
				ComboBox comboBox = sourceA;
				bool enabled = (sourceB.Enabled = include.Checked);
				comboBox.Enabled = enabled;
				ComboBox comboBox2 = sourceA;
				enabled = (sourceB.Visible = include.Checked);
                comboBox2.Visible = enabled;
                ensureRunRow();
                fitClashCards();
				if (!include.Checked)
				{
					clearLinked();
					if (status != null)
					{
						status.Text = "Linked-element scan disabled.";
					}
				}
				else if (status != null)
				{
					status.Text = "Choose a source above Discipline A and B, then run the test.";
				}
			};
			bool resultsActivated = false;
			Action showResultsArea = () =>
			{
				results.Visible = resultsActivated;
				if (categoryFilterA != null)
				{
					categoryFilterA.Visible = true;
				}
				if (categoryFilterB != null)
				{
					categoryFilterB.Visible = true;
				}
				if (categoryCaptionA != null)
				{
					categoryCaptionA.Visible = true;
				}
				if (categoryCaptionB != null)
				{
					categoryCaptionB.Visible = true;
				}
				if (categoryVs != null)
				{
					categoryVs.Visible = true;
				}
				previousClash.Visible = resultsActivated;
				nextClash.Visible = resultsActivated;
			};
			resultsActivated = false;
			showResultsArea();
			EventHandler value = (object s, EventArgs e) =>
			{
				resultsActivated = false;
				showResultsArea();
				if (previewText != null)
				{
					previewText.Text = "Choose the scan scope and disciplines, then click Run Clash Test.";
				}
			};
			disciplineA.SelectedIndexChanged += value;
			disciplineB.SelectedIndexChanged += value;
			EventHandler value2 = (object s, EventArgs e) =>
			{
				form.DialogResult = DialogResult.None;
				resultsActivated = true;
				showResultsArea();
				allRows.Clear();
				allRows.AddRange(from object x in results.Items
					select x as string into x
					where x != null
					select x);
				foreach (string solved in solvedRows.ToList())
				{
					allRows.RemoveAll((string row) => clashIdentity(row) == clashIdentity(solved));
				}
				solvedRows.Clear();
				if (include.Checked && (sourceA.SelectedIndex != 0 || sourceB.SelectedIndex != 0))
				{
					allRows.RemoveAll((string row) => row.StartsWith("Clash ", StringComparison.OrdinalIgnoreCase) && !row.StartsWith("LINK CLASH · ", StringComparison.OrdinalIgnoreCase));
				}
				refreshCategoryOptions();
				scan();
				if (clashPendingVerification != null)
				{
					bool flag = allRows.Contains(clashPendingVerification);
					if (status != null)
					{
						status.Text = (flag ? "Clash remains in the latest scan." : "(Solved)");
					}
					clashPendingVerification = null;
				}
			};
			if (run != null)
			{
				run.DialogResult = DialogResult.None;
				run.Click += value2;
			}
			EventHandler value3 = (object s, EventArgs e) =>
			{
				string row = results.SelectedItem as string;
				if (row != null && previewTitle != null)
				{
					previewTitle.Text = "Clash details";
				}
				Clash value4;
				if (row != null && solvedRows.Any((string saved) => clashIdentity(saved) == clashIdentity(row)))
				{
					if (review != null)
					{
						review.Enabled = false;
					}
					if (copyA != null)
					{
						copyA.Enabled = false;
					}
					if (copyB != null)
					{
						copyB.Enabled = false;
					}
					if (detail != null)
					{
						detail.Text = "This clash was not found in the latest scan. It is marked solved.";
					}
					if (previewText != null)
					{
						previewText.Text = "Description: This clash was not found in the latest scan; it is marked solved.";
					}
				}
				else if (row != null && clashes.TryGetValue(row, out value4))
				{
					if (review != null)
					{
						review.Enabled = true;
					}
					if (copyA != null)
					{
						copyA.Enabled = true;
					}
					if (copyB != null)
					{
						copyB.Enabled = true;
					}
					if (detail != null)
					{
						detail.Text = "Elements " + value4.A.LinkedElementId.Value + " and " + value4.B.LinkedElementId.Value + " overlap. Review both in Revit.";
					}
					if (previewText != null)
					{
						previewText.Text = "Element A: " + value4.AId.Value + " — Source: " + (value4.SourceA ?? "Host model") + Environment.NewLine + "Element B: " + value4.BId.Value + " — Source: " + (value4.SourceB ?? "Host model") + Environment.NewLine + "Description: " + (value4.Description ?? "Elements overlap. Check clearance, routing, or offsets.") + Environment.NewLine + "Classification: Hard clash";
					}
				}
				else if (row != null && row.StartsWith("Clash ", StringComparison.OrdinalIgnoreCase) && previewText != null)
				{
					Match match = Regex.Match(row, "\\]\\s*(?<a>.*?)\\s*/\\s*\\[[^\\]]+\\]\\s*(?<b>.*?)\\s+—");
					int num2 = row.LastIndexOf(" — ", StringComparison.Ordinal);
					string text = ((num2 >= 0) ? row.Substring(num2 + 3).Trim() : string.Empty);
					string[] array2 = text.Split(new string[1] { " / " }, StringSplitOptions.None);
					string text2 = (match.Success ? (match.Groups["a"].Value.Trim() + " and " + match.Groups["b"].Value.Trim() + " overlap. Check clearance, routing, or offsets.") : "Elements overlap. Check clearance, routing, or offsets.");
					previewText.Text = ((array2.Length == 2) ? ("Element A: " + array2[0] + " — Source: Host model" + Environment.NewLine + "Element B: " + array2[1] + " — Source: Host model" + Environment.NewLine) : string.Empty) + "Description: " + text2 + Environment.NewLine + "Classification: Hard clash";
				}
			};
			results.SelectedIndexChanged += value3;
			if (copyA != null)
			{
				copyA.Click += (object s, EventArgs e) =>
				{
					Copy(first: true);
				};
			}
			if (copyB != null)
			{
				copyB.Click += (object s, EventArgs e) =>
				{
					Copy(first: false);
				};
			}
			if (review != null)
			{
				review.DialogResult = DialogResult.None;
				review.Click += (object s, EventArgs e) =>
				{
					//IL_013f: Unknown result type (might be due to invalid IL or missing references)
					//IL_0146: Expected Obj, but got Unknown
					//IL_01b5: Unknown result type (might be due to invalid IL or missing references)
					//IL_01bc: Expected Obj, but got Unknown
					//IL_022c: Unknown result type (might be due to invalid IL or missing references)
					//IL_024f: Unknown result type (might be due to invalid IL or missing references)
					//IL_0259: Expected Obj, but got Unknown
					//IL_0259: Expected Obj, but got Unknown
					if (!(results.SelectedItem is string key) || !clashes.TryGetValue(key, out var value4))
					{
						return;
					}
					try
					{
						ui.Selection.SetReferences((IList<Reference>)new List<Reference> { value4.A, value4.B });
						UIView val = ui.GetOpenUIViews().FirstOrDefault((UIView v) => v.ViewId == ((Element)ui.Document.ActiveView).Id);
						if (val != null && value4.BoxA != null && value4.BoxB != null)
						{
							XYZ val2 = new XYZ(Math.Min(value4.BoxA.Min.X, value4.BoxB.Min.X), Math.Min(value4.BoxA.Min.Y, value4.BoxB.Min.Y), Math.Min(value4.BoxA.Min.Z, value4.BoxB.Min.Z));
							XYZ val3 = new XYZ(Math.Max(value4.BoxA.Max.X, value4.BoxB.Max.X), Math.Max(value4.BoxA.Max.Y, value4.BoxB.Max.Y), Math.Max(value4.BoxA.Max.Z, value4.BoxB.Max.Z));
							double num2 = Math.Max(1.0, Math.Max(val3.X - val2.X, Math.Max(val3.Y - val2.Y, val3.Z - val2.Z)) * 0.15);
							val.ZoomAndCenterRectangle(new XYZ(val2.X - num2, val2.Y - num2, val2.Z - num2), new XYZ(val3.X + num2, val3.Y + num2, val3.Z + num2));
						}
						else
						{
							List<ElementId> list = new List<ElementId>();
							if (value4.LinkA != (ElementId)null)
							{
								list.Add(value4.LinkA);
							}
							else
							{
								list.Add(value4.AId);
							}
							if (value4.LinkB != (ElementId)null && !list.Contains(value4.LinkB))
							{
								list.Add(value4.LinkB);
							}
							else if (value4.LinkB == (ElementId)null)
							{
								list.Add(value4.BId);
							}
							ui.ShowElements((ICollection<ElementId>)list);
						}
						if (status != null)
						{
							status.Text = "Linked clash elements selected and framed in the active view.";
						}
					}
					catch (Exception ex)
					{
						try
						{
							List<ElementId> list2 = new List<ElementId>();
							if (value4.LinkA != (ElementId)null)
							{
								list2.Add(value4.LinkA);
							}
							if (value4.LinkB != (ElementId)null && !list2.Contains(value4.LinkB))
							{
								list2.Add(value4.LinkB);
							}
							if (list2.Count > 0)
							{
								ui.Selection.SetElementIds((ICollection<ElementId>)list2);
							}
						}
						catch
						{
						}
						if (status != null)
						{
							status.Text = "Could not select or frame both linked clash elements: " + ex.Message;
						}
					}
				};
			}
			EnableModeless(form, run, review, button, ui, status);
			void Copy(bool first)
			{
				if (results.SelectedItem is string key && clashes.TryGetValue(key, out var value4))
				{
					try
					{
						ElementId val = (first ? value4.AId : value4.BId);
						if (val == (ElementId)null)
						{
							throw new InvalidOperationException("The selected element ID is unavailable.");
						}
						Clipboard.SetText(val.Value.ToString());
						if (status != null)
						{
							status.Text = "Element " + (first ? "A" : "B") + " ID copied: " + val.Value;
						}
					}
					catch (Exception ex)
					{
						if (status != null)
						{
							status.Text = "Could not copy Element " + (first ? "A" : "B") + " ID: " + ex.Message;
						}
					}
				}
			}
			ComboBox SourcePicker(Control card)
			{
				ComboBox picker2 = new ComboBox
				{
					DropDownStyle = ComboBoxStyle.DropDownList,
					Font = new Font("Segoe UI", 8f),
					BackColor = Color.FromArgb(255, 239, 190),
					FlatStyle = FlatStyle.Flat,
					Enabled = false,
					Visible = false,
					IntegralHeight = false,
					MaxDropDownItems = 12,
					DropDownHeight = 176
				};
				picker2.Items.Add("Host model");
				foreach (RevitLinkInstance item12 in links)
				{
					picker2.Items.Add(((Element)item12).Name + "  [" + ((Element)item12).Id.Value + "]");
				}
				picker2.SelectedIndex = 0;
				picker2.DropDownHeight = Math.Max(40, Math.Min(300, picker2.Items.Count * Math.Max(20, picker2.ItemHeight + 2)));
				int val = (from object x in picker2.Items
					select TextRenderer.MeasureText(x.ToString(), picker2.Font).Width + 40).DefaultIfEmpty(180).Max();
				picker2.DropDownWidth = Math.Min(Math.Max(picker2.Width, val), Math.Max(picker2.Width, form.ClientSize.Width - 48));
				linkTips.SetToolTip(picker2, picker2.SelectedItem?.ToString() ?? "");
				picker2.SelectedIndexChanged += (object s, EventArgs e) =>
				{
					linkTips.SetToolTip(picker2, picker2.SelectedItem?.ToString() ?? "");
				};
				card.Controls.Add(picker2);
				return picker2;
			}
		}
		catch
		{
		}
	}

	private static string ShortenClashFooterDetail(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		if (text.StartsWith("Select a clash to see the reason", StringComparison.OrdinalIgnoreCase))
		{
			return "Select a clash to view its reason and suggested action.";
		}
		string text2 = text.Trim();
		if (text2.StartsWith("Reason: ", StringComparison.OrdinalIgnoreCase))
		{
			text2 = text2.Substring("Reason: ".Length);
		}
		if (text2.StartsWith("Overlap: ", StringComparison.OrdinalIgnoreCase))
		{
			text2 = text2.Substring("Overlap: ".Length);
		}
		string[] array = new string[5] { " Suggested action:", " Action:", ". Review clearance, routing, or offsets.", ". Check clearance, routing, or offsets.", ". Review clearance/routing/offsets." };
		foreach (string value in array)
		{
			int num = text2.IndexOf(value, StringComparison.OrdinalIgnoreCase);
			if (num >= 0)
			{
				text2 = text2.Substring(0, num);
			}
		}
		if (text2.StartsWith("Bounding boxes overlap between ", StringComparison.OrdinalIgnoreCase))
		{
			text2 = text2.Substring("Bounding boxes overlap between ".Length);
		}
		text2 = text2.Trim().TrimEnd('.').Trim();
		if (text2.Length == 0)
		{
			return text;
		}
		return text2 + " overlap. Inspect both; review clearance, routing, and offsets in the responsible discipline.";
	}

	private static string ShortenClashFooterStatus(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		Match match = Regex.Match(text, "^(?<count>\\d+) linked clash\\(es\\) found for the selected sources\\.?$", RegexOptions.IgnoreCase);
		if (match.Success)
		{
			int num = int.Parse(match.Groups["count"].Value);
			return num + ((num == 1) ? " linked clash found." : " linked clashes found.");
		}
		Match match2 = Regex.Match(text, "^(?<clashes>\\d+) potential clash\\(es\\) found in (?<elements>\\d+) model element\\(s\\)\\.?$", RegexOptions.IgnoreCase);
		if (match2.Success)
		{
			return match2.Groups["clashes"].Value + " clashes found · " + match2.Groups["elements"].Value + " elements scanned.";
		}
		return text;
	}

	private static void EnableModeless(Form form, Button run, Button review, Button refresh, UIDocument ui, Label status)
	{
		if (form == null || modelessSessions.ContainsKey(form))
		{
			return;
		}
		ModelessDispatcher dispatcher = new ModelessDispatcher();
		ModelessSession modelessSession = new ModelessSession
		{
			Dispatcher = dispatcher
		};
		modelessSessions.Add(form, modelessSession);
		Action<string> report = (string text) =>
		{
			if (status != null && !status.IsDisposed)
			{
				status.Text = text;
			}
		};
		EventHandler runAction = null;
		Action<Button> action = (Button button2) =>
		{
			if (button2 != null)
			{
				EventHandler original = DetachHandler(button2, "click");
				if (original == null)
				{
					button2.Enabled = false;
					report("This Revit action could not be connected to the modeless window.");
				}
				else
				{
					if (button2 == run)
					{
						runAction = original;
					}
					button2.Click += (object s, EventArgs e) =>
					{
						form.DialogResult = DialogResult.None;
						if (!dispatcher.Post(() =>
						{
							original(button2, EventArgs.Empty);
						}))
						{
							report("Revit is busy. Please try again in a moment.");
						}
					};
				}
			}
		};
		action(run);
		action(review);
		if (refresh != null)
		{
			if (runAction == null)
			{
				refresh.Enabled = false;
				report("Refresh is unavailable until the scan action is connected.");
			}
			else
			{
				refresh.Click += (object s, EventArgs e) =>
				{
					form.DialogResult = DialogResult.None;
					if (!dispatcher.Post(() =>
					{
						runAction(run, EventArgs.Empty);
					}))
					{
						report("Revit is busy. Please try again in a moment.");
					}
				};
			}
		}
		Button button = All(form).OfType<Button>().FirstOrDefault((Button b) => string.Equals(b.Text, "Close", StringComparison.OrdinalIgnoreCase));
		if (button != null)
		{
			modelessSession.Close = button;
			button.DialogResult = DialogResult.None;
			button.Click += (object s, EventArgs e) =>
			{
				form.Close();
			};
		}
		form.CancelButton = null;
		EventHandler originalShown = DetachHandler(form, "shown");
		form.Shown += (object s, EventArgs e) =>
		{
			if (originalShown != null && !dispatcher.Post(() =>
			{
				originalShown(form, e);
			}))
			{
				report("Revit is busy; the initial clash scan could not be queued.");
			}
			if (form.IsDisposed || !form.IsHandleCreated)
			{
				return;
			}
			try
			{
				form.BeginInvoke((MethodInvoker)(() =>
				{
					HideDashboardAndActivate(form);
				}));
			}
			catch
			{
			}
		};
		form.FormClosed += (object s, FormClosedEventArgs e) =>
		{
			try
			{
				modelessSessions.Remove(form);
			}
			catch
			{
			}
			dispatcher.Dispose();
			ShowDashboardAfterClose(form);
		};
	}

	public static void MinimizeCaptionWindow(object value)
	{
		if (value is Form { IsDisposed: false } form)
		{
			try
			{
				Type dashboard = AppDomain.CurrentDomain.GetAssemblies()
					.Select(a => a.GetType("BIMlify.CleanerUi.DashboardEnhancements", false))
					.FirstOrDefault(t => t != null);
				MethodInfo minimize = dashboard?.GetMethod("MinimizeToQuickBar", BindingFlags.Static | BindingFlags.NonPublic);
				if (minimize != null && minimize.GetParameters().Length == 1 && minimize.GetParameters()[0].ParameterType.IsInstanceOfType(form))
				{
					minimize.Invoke(null, new object[1] { form });
					return;
				}
			}
			catch
			{
			}
			form.WindowState = FormWindowState.Minimized;
		}
	}

	public static void CloseDashboardIfOwner(object value)
	{
		try
		{
			Type type = ((!(value is Form form)) ? null : form.GetType().Assembly.GetType("BIMlify.BimActions", throwOnError: false));
			object obj = ((type == null) ? null : type.GetField("dashboardWindow", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null));
			if (value == obj)
			{
				type.GetMethod("CloseModelessDashboard", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
			}
		}
		catch
		{
		}
	}

	public static void HandleCaptionClick(object captionButton)
	{
		if (captionButton == null)
		{
			return;
		}
		try
		{
			Type type = captionButton.GetType();
			FieldInfo field = type.GetField("kind", BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo field2 = type.GetField("owner", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field == null || field2 == null)
			{
				return;
			}
			int num = (int)field.GetValue(captionButton);
			Form form = field2.GetValue(captionButton) as Form;
			if (form == null || form.IsDisposed)
			{
				return;
			}
            Type actions = form.GetType().Assembly.GetType("BIMlify.BimActions", false);
            Form quickBar = actions?.GetField("dashboardQuickBarWindow", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as Form;
            if (ReferenceEquals(form, quickBar))
            {
                if (num == 2)
                {
                    actions.GetMethod("CloseModelessDashboard", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, null);
                    return;
                }
                if (num == 3)
                {
                    var dashboard = actions.GetField("dashboardWindow", BindingFlags.Static | BindingFlags.NonPublic)
                        ?.GetValue(null) as Form;
                    if (dashboard == null || dashboard.IsDisposed) return;
                    SetMaximizedBoundsToWorkArea(dashboard);
                    dashboard.WindowState = FormWindowState.Maximized;
                    form.Tag = "home";
                    form.Close();
                    if (dashboard.IsHandleCreated && !dashboard.IsDisposed)
                        dashboard.BeginInvoke((MethodInvoker)(() => { if (!dashboard.IsDisposed) dashboard.Activate(); }));
                    return;
                }
            }
			switch (num)
			{
			case 0:
				MinimizeCaptionWindow(form);
				break;
			case 1:
				EnsureWindowOnScreen(form);
				SetMaximizedBoundsToWorkArea(form);
				form.WindowState = ((form.WindowState != FormWindowState.Maximized) ? FormWindowState.Maximized : FormWindowState.Normal);
				if (form.IsDisposed)
				{
					break;
				}
				form.Show();
				form.BringToFront();
				form.Activate();
				try
				{
					form.BeginInvoke((MethodInvoker)(() =>
					{
						if (!form.IsDisposed)
						{
							SetMaximizedBoundsToWorkArea(form);
							EnsureWindowOnScreen(form);
							form.Show();
							form.BringToFront();
							form.Activate();
						}
					}));
					break;
				}
				catch
				{
					break;
				}
			case 3:
				form.Tag = "restore";
				form.DialogResult = DialogResult.OK;
				form.Close();
				break;
			default:
				CloseDashboardIfOwner(form);
				form.Close();
				break;
			}
		}
		catch
		{
		}
	}

	public static void AttachSecondaryWindowBehavior(object value)
	{
		Form form = value as Form;
		if (form == null || form.IsDisposed)
		{
			return;
		}
		Action updateMaximizedBounds = () =>
		{
			SetMaximizedBoundsToWorkArea(form);
		};
		form.LocationChanged += (object s, EventArgs e) =>
		{
			updateMaximizedBounds();
		};
		form.SizeChanged += (object s, EventArgs e) =>
		{
			updateMaximizedBounds();
		};
		FormWindowState lastWindowState = form.WindowState;
		form.Resize += (object s, EventArgs e) =>
		{
			updateMaximizedBounds();
			FormWindowState windowState = form.WindowState;
			bool flag = lastWindowState == FormWindowState.Minimized && windowState != FormWindowState.Minimized;
			bool flag2 = lastWindowState != FormWindowState.Maximized && windowState == FormWindowState.Maximized;
			lastWindowState = windowState;
			if (flag2)
			{
				KeepOtherBimlifyWindowsMaximized(form);
			}
			if (flag && !form.IsDisposed)
			{
				Action restore = () =>
				{
					if (!form.IsDisposed && form.WindowState != FormWindowState.Minimized)
					{
						EnsureWindowOnScreen(form);
						form.Show();
						form.BringToFront();
						form.Activate();
					}
				};
				if (form.IsHandleCreated)
				{
					try
					{
						form.BeginInvoke((MethodInvoker)(() =>
						{
							restore();
						}));
						return;
					}
					catch
					{
						return;
					}
				}
				restore();
			}
		};
		form.Shown += (object s, EventArgs e) =>
		{
			EnsureWindowOnScreen(form);
		};
		EventHandler onLoad = null;
		onLoad = (object s, EventArgs e) =>
		{
			try
			{
				updateMaximizedBounds();
				Type type = form.GetType().Assembly.GetType("BIMlify.BimActions", throwOnError: false);
				Form form2 = ((type == null) ? null : (type.GetField("dashboardWindow", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as Form));
				if (form.Width >= 700 && form.Height >= 500 && form2 != null && !form2.IsDisposed && form != form2 && form2.WindowState == FormWindowState.Maximized)
				{
					form.StartPosition = FormStartPosition.Manual;
					form.Location = form2.Location;
					form.WindowState = FormWindowState.Maximized;
				}
			}
			catch
			{
			}
			finally
			{
				form.Load -= onLoad;
			}
		};
		form.Load += onLoad;
	}

	private static void KeepOtherBimlifyWindowsMaximized(Form source)
	{
		try
		{
			foreach (Form openForm in Application.OpenForms)
			{
				if (openForm != null && openForm != source && !openForm.IsDisposed && openForm.WindowState != FormWindowState.Minimized && openForm.Text.StartsWith("BIMlify", StringComparison.OrdinalIgnoreCase) && openForm.Width >= 700 && openForm.Height >= 500 && openForm.WindowState != FormWindowState.Maximized)
				{
					SetMaximizedBoundsToWorkArea(openForm);
					EnsureWindowOnScreen(openForm);
					openForm.WindowState = FormWindowState.Maximized;
				}
			}
		}
		catch
		{
		}
	}

	private static void SetMaximizedBoundsToWorkArea(Form form)
	{
		if (form == null || form.IsDisposed)
		{
			return;
		}
		try
		{
			PropertyInfo property = typeof(Form).GetProperty("MaximizedBounds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.CanWrite)
			{
				property.SetValue(form, Screen.FromControl(form).WorkingArea, null);
			}
		}
		catch
		{
		}
	}

	private static void EnsureWindowOnScreen(Form form)
	{
		if (form == null || form.IsDisposed || form.WindowState == FormWindowState.Minimized)
		{
			return;
		}
		try
		{
			bool flag = form.WindowState == FormWindowState.Maximized;
			Rectangle bounds = form.Bounds;
			Screen[] allScreens = Screen.AllScreens;
			if (allScreens.Any((Screen screen) => Rectangle.Intersect(bounds, screen.WorkingArea).Width >= Math.Min(100, Math.Max(1, bounds.Width)) && Rectangle.Intersect(bounds, screen.WorkingArea).Height >= Math.Min(48, Math.Max(1, bounds.Height))))
			{
				return;
			}
			Screen primaryScreen = Screen.PrimaryScreen;
			if (primaryScreen == null)
			{
				return;
			}
			Rectangle workingArea = primaryScreen.WorkingArea;
			if (flag)
			{
				form.WindowState = FormWindowState.Normal;
			}
			int num = Math.Min(Math.Max(form.MinimumSize.Width, bounds.Width), workingArea.Width);
			int num2 = Math.Min(Math.Max(form.MinimumSize.Height, bounds.Height), workingArea.Height);
			int x = Math.Max(workingArea.Left, Math.Min(bounds.Left, workingArea.Right - num));
			int y = Math.Max(workingArea.Top, Math.Min(bounds.Top, workingArea.Bottom - num2));
			form.StartPosition = FormStartPosition.Manual;
			form.SetBounds(x, y, num, num2);
			if (!flag)
			{
				return;
			}
			PropertyInfo property = typeof(Form).GetProperty("MaximizedBounds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.CanWrite)
			{
				property.SetValue(form, workingArea, null);
			}
			form.WindowState = FormWindowState.Maximized;
			try
			{
				form.BeginInvoke((MethodInvoker)(() =>
				{
					if (!form.IsDisposed)
					{
						form.Show();
						form.BringToFront();
						form.Activate();
					}
				}));
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private static EventHandler DetachHandler(Control control, string eventName)
	{
		PropertyInfo property = typeof(Control).GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic);
		EventHandlerList eventHandlerList = ((property == null) ? null : (property.GetValue(control, null) as EventHandlerList));
		if (eventHandlerList == null)
		{
			return null;
		}
		Type type = control.GetType();
		while (type != null)
		{
			FieldInfo[] fields = type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				string text = fieldInfo.Name ?? "";
				if (fieldInfo.FieldType == typeof(object) && text.IndexOf(eventName, StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("double", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) < 0 && eventHandlerList[fieldInfo.GetValue(null)] is EventHandler eventHandler)
				{
					eventHandlerList.RemoveHandler(fieldInfo.GetValue(null), eventHandler);
					return eventHandler;
				}
			}
			type = type.BaseType;
		}
		return null;
	}

	private static Control ReplaceRoundedCard(Control oldCard)
	{
		if (oldCard == null || oldCard.Parent == null)
		{
			return oldCard;
		}
		Control parent = oldCard.Parent;
		int childIndex = parent.Controls.GetChildIndex(oldCard);
		Panel panel = new Panel
		{
			BackColor = Color.White,
			Bounds = oldCard.Bounds,
			Anchor = oldCard.Anchor,
			Dock = oldCard.Dock,
			Margin = oldCard.Margin,
			Padding = oldCard.Padding
		};
		parent.Controls.Remove(oldCard);
		while (oldCard.Controls.Count > 0)
		{
			panel.Controls.Add(oldCard.Controls[0]);
		}
		parent.Controls.Add(panel);
		parent.Controls.SetChildIndex(panel, childIndex);
		oldCard.Dispose();
		return panel;
	}

	private static Panel WrapRoundedClashGroup(Control content)
	{
		if (content == null || content.Parent == null)
		{
			return null;
		}
		Control parent = content.Parent;
		int childIndex = parent.Controls.GetChildIndex(content);
		Panel frame = new Panel
		{
			BackColor = Color.White,
			Bounds = content.Bounds,
			Anchor = content.Anchor,
			Dock = content.Dock,
			Margin = content.Margin,
			Padding = new Padding(2),
			TabStop = false
		};
		Action updateRegion = () =>
		{
			if (frame.Width >= 4 && frame.Height >= 4)
			{
				using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, frame.Width - 1, frame.Height - 1), 10))
				{
					Region region = frame.Region;
					frame.Region = new Region(path);
					region?.Dispose();
				}
				frame.Invalidate();
			}
		};
		frame.Paint += (object s, PaintEventArgs e) =>
		{
			if (frame.Width < 4 || frame.Height < 4)
			{
				return;
			}
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, frame.Width - 1, frame.Height - 1), 10);
			using Pen pen = new Pen(Color.FromArgb(190, 197, 204), 1f);
			e.Graphics.DrawPath(pen, path);
		};
		frame.SizeChanged += (object s, EventArgs e) =>
		{
			updateRegion();
		};
		parent.Controls.Remove(content);
		parent.Controls.Add(frame);
		parent.Controls.SetChildIndex(frame, childIndex);
		frame.Controls.Add(content);
		content.Dock = DockStyle.Fill;
		content.Margin = Padding.Empty;
		updateRegion();
		return frame;
	}

	private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		if (rectangle.Width < 2 || rectangle.Height < 2)
		{
			return graphicsPath;
		}
		int num = Math.Max(2, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
		graphicsPath.AddArc(rectangle.X, rectangle.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(rectangle.Right - num, rectangle.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(rectangle.Right - num, rectangle.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(rectangle.X, rectangle.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private static void ClearPaintHandlers(Control control)
	{
		if (control == null)
		{
			return;
		}
		try
		{
			PropertyInfo property = typeof(Control).GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic);
			EventHandlerList eventHandlerList = ((property == null) ? null : (property.GetValue(control, null) as EventHandlerList));
			if (eventHandlerList == null)
			{
				return;
			}
			Type type = control.GetType();
			while (type != null)
			{
				FieldInfo[] fields = type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo in fields)
				{
					if (!(fieldInfo.FieldType != typeof(object)) && (fieldInfo.Name ?? "").IndexOf("paint", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						object value = fieldInfo.GetValue(null);
						Delegate obj = eventHandlerList[value];
						if ((object)obj != null)
						{
							eventHandlerList.RemoveHandler(value, obj);
						}
					}
				}
				type = type.BaseType;
			}
		}
		catch
		{
		}
	}

	private static void HideDashboardAndActivate(Form clash)
	{
		foreach (Form openForm in Application.OpenForms)
		{
			if (openForm != clash && !openForm.IsDisposed && string.Equals(openForm.Text, "BIMlify", StringComparison.OrdinalIgnoreCase))
			{
				openForm.Hide();
			}
		}
		if (!clash.IsDisposed)
		{
			clash.ShowInTaskbar = true;
			clash.Activate();
		}
	}

	private static void ShowDashboardAfterClose(Form clash)
	{
		foreach (Form openForm in Application.OpenForms)
		{
			if (openForm != clash && !openForm.IsDisposed && string.Equals(openForm.Text, "BIMlify", StringComparison.OrdinalIgnoreCase))
			{
				openForm.Show();
				openForm.Activate();
				break;
			}
		}
	}

	private static int AbsoluteLeft(Control c)
	{
		int num = 0;
		while (c != null)
		{
			num += c.Left;
			c = c.Parent;
		}
		return num;
	}

	private static int BottomInParent(Control control, Control parent)
	{
		if (control == null || parent == null)
		{
			return 0;
		}
		try
		{
			return parent.PointToClient(control.Parent.PointToScreen(new Point(control.Left, control.Bottom))).Y;
		}
		catch
		{
			return control.Bottom;
		}
	}

	private static Candidate Make(Element e, RevitLinkInstance link, View view, Transform t, string source)
	{
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (e.Category == null)
			{
				return null;
			}
			BoundingBoxXYZ val = e.get_BoundingBox(view);
			if (val == null)
			{
				return null;
			}
			BoundingBoxXYZ box = TransformBox(val, t);
			return new Candidate
			{
				Element = e,
				Link = link,
				Box = box,
				Transform = (t ?? Transform.Identity),
				Reference = ((link != null) ? new Reference(e).CreateLinkReference(link) : new Reference(e)),
				Source = source,
				Discipline = Discipline(e)
			};
		}
		catch
		{
			return null;
		}
	}

	private static bool GeometryIntersects(Candidate a, Candidate b)
	{
		try
		{
			List<Solid> candidateSolids = GetCandidateSolids(a);
			List<Solid> candidateSolids2 = GetCandidateSolids(b);
			foreach (Solid item in candidateSolids)
			{
				foreach (Solid item2 in candidateSolids2)
				{
					try
					{
						Solid val = BooleanOperationsUtils.ExecuteBooleanOperation(item, item2, (BooleanOperationsType)2);
						if ((GeometryObject)(object)val != (GeometryObject)null && val.Volume > 1E-09)
						{
							return true;
						}
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static List<Solid> GetCandidateSolids(Candidate candidate)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected Obj, but got Unknown
		if (candidate.SolidsLoaded)
		{
			return candidate.Solids ?? new List<Solid>();
		}
		candidate.SolidsLoaded = true;
		candidate.Solids = new List<Solid>();
		try
		{
			Options val = new Options
			{
				DetailLevel = (ViewDetailLevel)3,
				IncludeNonVisibleObjects = false
			};
			GeometryElement geometry = candidate.Element.get_Geometry(val);
			CollectSolids(geometry, candidate.Transform ?? Transform.Identity, candidate.Solids);
		}
		catch
		{
		}
		return candidate.Solids;
	}

	private static void CollectSolids(GeometryElement geometry, Transform transform, List<Solid> solids)
	{
		if ((GeometryObject)(object)geometry == (GeometryObject)null)
		{
			return;
		}
		foreach (GeometryObject item in geometry)
		{
			Solid val = (Solid)(object)((item is Solid) ? item : null);
			if ((GeometryObject)(object)val != (GeometryObject)null && val.Faces.Size > 0 && val.Volume > 1E-09)
			{
				try
				{
					solids.Add((transform == null || transform.IsIdentity) ? val : SolidUtils.CreateTransformed(val, transform));
				}
				catch
				{
				}
				continue;
			}
			GeometryInstance val2 = (GeometryInstance)(object)((item is GeometryInstance) ? item : null);
			if ((GeometryObject)(object)val2 != (GeometryObject)null)
			{
				try
				{
					CollectSolids(val2.GetInstanceGeometry(), transform, solids);
				}
				catch
				{
				}
			}
		}
	}

	private static BoundingBoxXYZ TransformBox(BoundingBoxXYZ b, Transform t)
	{
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Expected Obj, but got Unknown
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected Obj, but got Unknown
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		XYZ val = null;
		XYZ val2 = null;
		for (int i = 0; i < 8; i++)
		{
			XYZ val3 = new XYZ(((i & 1) == 0) ? b.Min.X : b.Max.X, ((i & 2) == 0) ? b.Min.Y : b.Max.Y, ((i & 4) == 0) ? b.Min.Z : b.Max.Z);
			val3 = t.OfPoint(b.Transform.OfPoint(val3));
			val = ((val == null) ? val3 : new XYZ(Math.Min(val.X, val3.X), Math.Min(val.Y, val3.Y), Math.Min(val.Z, val3.Z)));
			val2 = ((val2 == null) ? val3 : new XYZ(Math.Max(val2.X, val3.X), Math.Max(val2.Y, val3.Y), Math.Max(val2.Z, val3.Z)));
		}
		return new BoundingBoxXYZ
		{
			Min = val,
			Max = val2
		};
	}

	private static bool Overlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
	{
		return a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y && a.Min.Z < b.Max.Z && b.Min.Z < a.Max.Z;
	}

	private static string Code(string s)
	{
		return s switch
		{
			"Electrical / Plumbing" => "ELEC", 
			"Mechanical / HVAC" => "MEP", 
			"Structure" => "STR", 
			"Architecture" => "ARCH", 
			_ => "ANY", 
		};
	}

	private static string Discipline(Element element)
	{
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Invalid comparison between Unknown and I4
		if (element == null || element.Category == null)
		{
			return "OTHER";
		}
		string text = (element.Category.Name ?? "").ToLowerInvariant();
		try
		{
			Wall val = (Wall)(object)((element is Wall) ? element : null);
			if (val != null)
			{
				Parameter val2 = ((Element)val).get_Parameter((BuiltInParameter)(-1001596));
				if ((val2 != null && val2.AsInteger() != 0) || (int)val.StructuralUsage > 0)
				{
					return "STR";
				}
				return "ARCH";
			}
			if (element is Floor)
			{
				Parameter val3 = element.get_Parameter((BuiltInParameter)(-1001954));
				return (val3 != null && val3.AsInteger() != 0) ? "STR" : "ARCH";
			}
		}
		catch
		{
		}
		if (text.Contains("structural") || text.Contains("column") || text.Contains("beam") || text.Contains("foundation") || text.Contains("pile") || text.Contains("truss"))
		{
			return "STR";
		}
		if (text.Contains("duct") || text.Contains("mechanical") || text.Contains("air terminal") || text.Contains("pipe") || text.Contains("plumbing") || text.Contains("fire protection"))
		{
			return "MEP";
		}
		if (text.Contains("electrical") || text.Contains("lighting") || text.Contains("cable") || text.Contains("device"))
		{
			return "ELEC";
		}
		if (text.Contains("door") || text.Contains("window") || text.Contains("room") || text.Contains("ceiling") || text.Contains("casework") || text.Contains("roof") || text.Contains("curtain"))
		{
			return "ARCH";
		}
		return "OTHER";
	}

	private static bool Allowed(string a, string b, string x, string y)
	{
		return (x == "ANY" || x == a || x == b) && (y == "ANY" || y == a || y == b) && (x == "ANY" || y == "ANY" || (x == a && y == b) || (x == b && y == a));
	}

	private static IEnumerable<Control> All(Control root)
	{
		foreach (Control c in root.Controls)
		{
			yield return c;
			foreach (Control item in All(c))
			{
				yield return item;
			}
		}
	}
}
