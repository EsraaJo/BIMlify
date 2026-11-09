using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Form = System.Windows.Forms.Form;
using Rectangle = System.Drawing.Rectangle;
using Color = System.Drawing.Color;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMlify.SmartOpenings;

[Transaction(TransactionMode.Manual)]
public sealed class SmartOpeningsCommand : IExternalCommand
{
	private static Form current;

	private static Form dashboardOwner;

	public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		UIDocument activeUIDocument = commandData.Application.ActiveUIDocument;
		if (activeUIDocument == null)
		{
			TaskDialog.Show("Smart Openings", "Open a project before using Smart Openings.");
			return (Result)1;
		}
		Launch(activeUIDocument);
		return (Result)0;
	}

	public static void Launch(UIDocument ui)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		if (ui == null)
		{
			TaskDialog.Show("Smart Openings", "Open a project before using Smart Openings.");
			return;
		}
		Form form = FindDashboard();
		bool flag = form != null && string.Equals(form.Tag as string, "dashboard_modeless", StringComparison.Ordinal);
		if (current != null && !current.IsDisposed)
		{
			if (flag)
			{
				ShowInDashboardSequence(form, current);
				return;
			}
			current.Show();
			current.Activate();
			return;
		}
		SmartOpeningsForm page = new SmartOpeningsForm(ui);
		current = CreateSharedWindow(page);
		current.ShowInTaskbar = true;
		dashboardOwner = (flag ? form : null);
		current.FormClosed += (object s, FormClosedEventArgs e) =>
		{
			Form owner = dashboardOwner;
			dashboardOwner = null;
			current = null;
			if (owner != null && !owner.IsDisposed)
			{
				try
				{
					owner.BeginInvoke((MethodInvoker)(() =>
					{
						if (!owner.IsDisposed)
						{
							if (owner.WindowState == FormWindowState.Minimized)
							{
								owner.WindowState = FormWindowState.Normal;
							}
							owner.Show();
							owner.Activate();
						}
					}));
				}
				catch
				{
				}
			}
		};
		current.Show();
		if (flag)
		{
			ShowInDashboardSequence(form, current);
		}
	}

	private static Form FindDashboard()
	{
		try
		{
			return (from a in AppDomain.CurrentDomain.GetAssemblies()
				select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null)?.GetField("dashboardWindow", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as Form;
		}
		catch
		{
			return null;
		}
	}

	private static void ShowInDashboardSequence(Form dashboard, Form tool)
	{
		try
		{
			dashboard.BeginInvoke((MethodInvoker)(() =>
			{
				if (!dashboard.IsDisposed && !tool.IsDisposed)
				{
					dashboard.BeginInvoke((MethodInvoker)(() =>
					{
						if (!dashboard.IsDisposed && !tool.IsDisposed)
						{
							dashboard.Hide();
							if (tool.WindowState == FormWindowState.Minimized)
							{
								tool.WindowState = FormWindowState.Normal;
							}
							tool.ShowInTaskbar = true;
							tool.Show();
							tool.Activate();
						}
					}));
				}
			}));
		}
		catch
		{
		}
	}

	private static Form CreateSharedWindow(SmartOpeningsForm page)
	{
		Type type = (from a in AppDomain.CurrentDomain.GetAssemblies()
			select a.GetType("BIMlify.BimActions", throwOnError: false)).FirstOrDefault((Type t) => t != null);
		Type type2 = type?.GetNestedType("BIMlifyForm", BindingFlags.NonPublic);
		Form shell = ((type2 == null) ? null : (Activator.CreateInstance(type2, nonPublic: true) as Form));
		if (shell == null)
		{
			shell = new Form();
		}
		shell.Text = page.Text;
		shell.Width = 1080;
		shell.Height = 720;
		shell.MinimumSize = new Size(980, 640);
		shell.StartPosition = FormStartPosition.CenterScreen;
		shell.ShowIcon = false;
		shell.MaximizeBox = true;
		shell.MinimizeBox = true;
		shell.AutoScaleMode = AutoScaleMode.Dpi;
		shell.BackColor = Color.FromArgb(238, 241, 245);
		shell.Font = new Font("Segoe UI", 9f);
		(type?.GetMethod("UseSmoothCorners", BindingFlags.Static | BindingFlags.NonPublic))?.Invoke(null, new object[1] { shell });
		page.Dock = DockStyle.None;
		shell.Controls.Add(page);
		MethodInfo method = type.GetMethod("AddWindowSidebar", BindingFlags.Static | BindingFlags.NonPublic);
		if (method != null)
		{
			method.Invoke(null, new object[3] { shell, false, null });
		}
		page.Dock = DockStyle.None;
		page.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
		page.Bounds = new Rectangle(58, 34, Math.Max(200, shell.ClientSize.Width - 58), Math.Max(200, shell.ClientSize.Height - 34));
		shell.MinimumSize = new Size(980, 640);
		shell.Size = new Size(1080, 720);
		shell.StartPosition = FormStartPosition.CenterScreen;
		shell.Resize += (object s, EventArgs e) =>
		{
			if (!page.IsDisposed)
			{
				page.Bounds = new Rectangle(58, 34, Math.Max(200, shell.ClientSize.Width - 58), Math.Max(200, shell.ClientSize.Height - 34));
			}
		};
		page.SendToBack();
		shell.FormClosed += (object s, FormClosedEventArgs e) =>
		{
			if (!page.IsDisposed)
			{
				page.Dispose();
			}
		};
		page.Show();
		return shell;
	}
}

