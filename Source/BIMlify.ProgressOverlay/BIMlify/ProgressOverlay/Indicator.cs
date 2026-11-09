using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BIMlify.ProgressOverlay;

public static class Indicator
{
	private static readonly object Sync = new object();

	private static Thread _thread;

	private static Form _form;

	private static Label _message;

	private static Label _description;

	private static ProgressBar _progress;

	public static void Start(string message)
	{
		StartCore(message, 0, "BIMlify — Scanning");
	}

	public static void StartProgress(string message, int total)
	{
		StartCore(message, Math.Max(1, total), "BIMlify — NWC Export");
	}

	private static void StartCore(string message, int total, string title)
	{
		lock (Sync)
		{
			if (_thread != null && _thread.IsAlive)
			{
				return;
			}
			ManualResetEvent ready = new ManualResetEvent(initialState: false);
			_thread = new Thread(() =>
			{
				try
				{
					Form form = new Form
					{
						Text = title,
						FormBorderStyle = FormBorderStyle.FixedSingle,
						StartPosition = FormStartPosition.CenterScreen,
						ClientSize = new Size(460, 128),
						BackColor = Color.FromArgb(238, 241, 245),
						TopMost = true,
						ShowInTaskbar = false,
						ControlBox = true,
						MaximizeBox = false,
						MinimizeBox = false,
						Font = new Font("Segoe UI", 9f)
					};
					Label heading = new Label
					{
						Text = "NWC export is running",
						Left = 14,
						Top = 8,
						Width = 420,
						Height = 21,
						Font = new Font("Segoe UI", 9f, FontStyle.Bold),
						ForeColor = Color.FromArgb(78, 85, 92),
						TextAlign = ContentAlignment.MiddleLeft
					};
					Label description = new Label
					{
						Text = "Revit is creating the Navisworks cache file. This window can be closed without cancelling the export.",
						Left = 14,
						Top = 31,
						Width = 420,
						Height = 19,
						Font = new Font("Segoe UI", 8f),
						ForeColor = Color.FromArgb(102, 109, 118),
						TextAlign = ContentAlignment.MiddleLeft,
						AutoEllipsis = true
					};
					Label label = new Label
					{
						Text = message,
						Left = 14,
						Top = 53,
						Width = 420,
						Height = 18,
						Font = new Font("Segoe UI", 8f),
						ForeColor = Color.FromArgb(102, 109, 118),
						TextAlign = ContentAlignment.MiddleLeft,
						AutoEllipsis = true
					};
					ProgressBar value = new ProgressBar
					{
						Left = 14,
						Top = 91,
						Width = 432,
						Height = 18,
						Style = total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee,
						Minimum = 0,
						Maximum = total > 0 ? total : 100,
						MarqueeAnimationSpeed = 24
					};
					form.Controls.Add(label);
					form.Controls.Add(description);
					form.Controls.Add(heading);
					form.Controls.Add(value);
					_form = form;
					_message = label;
					_description = description;
					_progress = value;
					form.Shown += delegate
					{
						ready.Set();
					};
					form.FormClosed += delegate
					{
						_form = null;
						_message = null;
						_description = null;
					};
					Application.Run(form);
				}
				catch
				{
					ready.Set();
				}
			});
			_thread.IsBackground = true;
			_thread.Name = "BIMlify scan progress";
			_thread.SetApartmentState(ApartmentState.STA);
			_thread.Start();
			ready.WaitOne(2500);
			ready.Dispose();
		}
	}

	public static void SetProgress(int completed, int total, string message)
	{
		Form form = _form;
		Label label = _message;
		ProgressBar progress = _progress;
		if (form == null || label == null || progress == null || form.IsDisposed || !form.IsHandleCreated) return;
		try
		{
			form.BeginInvoke((MethodInvoker)(() =>
			{
				if (form.IsDisposed) return;
				if (!label.IsDisposed) label.Text = message ?? string.Empty;
				if (!progress.IsDisposed)
				{
					int safeTotal = Math.Max(1, total);
					if (progress.Maximum != safeTotal) progress.Maximum = safeTotal;
					progress.Value = Math.Max(progress.Minimum, Math.Min(safeTotal, completed));
				}
			}));
		}
		catch { }
	}

	public static void SetStage(string message)
	{
		Form form = _form;
		Label label = _message;
		if (form == null || label == null || form.IsDisposed || !form.IsHandleCreated)
		{
			return;
		}
		try
		{
			form.BeginInvoke((MethodInvoker)(() =>
			{
				if (!label.IsDisposed)
				{
					label.Text = message;
				}
			}));
		}
		catch
		{
		}
	}

	public static void Stop()
	{
		Form form = _form;
		if (form == null || form.IsDisposed || !form.IsHandleCreated)
		{
			return;
		}
		try
		{
			form.BeginInvoke((MethodInvoker)(() =>
			{
				if (!form.IsDisposed)
				{
					form.Close();
				}
			}));
		}
		catch
		{
		}
	}
}
