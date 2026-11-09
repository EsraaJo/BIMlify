using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Autodesk.Revit.UI;

namespace BIMlify.CleanerUi
{
    public sealed class SheetWorkspace : IExternalEventHandler
    {
        private static readonly ConditionalWeakTable<Form, SheetWorkspace> Sessions =
            new ConditionalWeakTable<Form, SheetWorkspace>();
        private readonly Form _form;
        private readonly UIDocument _document;
        private readonly ExternalEvent _event;
        private Action _pending;
        private Form _dashboard;
        private EventHandler _keepDashboardHidden;

        private SheetWorkspace(Form form, UIDocument document)
        {
            _form = form;
            _document = document;
            _event = ExternalEvent.Create(this);
            form.Disposed += (sender, args) =>
            {
                _pending = null;
                RestoreDashboard();
                _event.Dispose();
            };
        }

        internal static void Attach(Form form, UIDocument document)
            => Sessions.Add(form, new SheetWorkspace(form, document));

        internal static void Run(Control control, Action action)
        {
            var form = control.FindForm();
            if (form == null || !Sessions.TryGetValue(form, out var session)) return;
            if (session._pending != null)
            {
                DashboardEnhancements.ShowSettingsStyleMessage(form, "Smart Sheets", "A sheet action is already queued.");
                return;
            }
            session._pending = action;
            try
            {
                var request = session._event.Raise();
                if (request == ExternalEventRequest.Accepted || request == ExternalEventRequest.Pending) return;
                throw new InvalidOperationException("Revit is busy. Try the sheet action again.");
            }
            catch (Exception error)
            {
                session._pending = null;
                DashboardEnhancements.ShowSettingsStyleMessage(form, "Smart Sheets", error.Message);
            }
        }

        public string GetName() => "BIMlify Smart Sheets";

        public void Execute(UIApplication app)
        {
            var action = _pending;
            _pending = null;
            if (action == null || _form.IsDisposed) return;
            try
            {
                if (!_document.Document.IsValidObject || app.ActiveUIDocument == null ||
                    !app.ActiveUIDocument.Document.Equals(_document.Document))
                    throw new InvalidOperationException("Activate the project where Smart Sheets was opened.");
                action();
            }
            catch (Exception error)
            {
                DashboardEnhancements.ShowSettingsStyleMessage(_form, "Smart Sheets", error.Message);
            }
        }

        public static DialogResult Show(Form form)
        {
            if (!Sessions.TryGetValue(form, out var session))
                return BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedDialog(form);
            session.HideDashboard();
            try
            {
                BIMlify.PlanRangeUi.PlanRangeInteraction.ShowOwnedModelessDialog(form, session._document.Application.MainWindowHandle);
            }
            catch
            {
                session.RestoreDashboard();
                throw;
            }
            return DialogResult.None;
        }

        private void HideDashboard()
        {
            var dashboard = DashboardEnhancements.DashboardWindow;
            if (dashboard == null || dashboard.IsDisposed || ReferenceEquals(dashboard, _form)) return;
            _dashboard = dashboard;
            _keepDashboardHidden = (sender, args) =>
            {
                if (_dashboard != null && !_dashboard.IsDisposed && _dashboard.Visible && !_form.IsDisposed)
                    _dashboard.Hide();
            };
            _dashboard.VisibleChanged += _keepDashboardHidden;
            if (_dashboard.Visible) _dashboard.Hide();
        }

        private void RestoreDashboard()
        {
            if (_dashboard == null || _dashboard.IsDisposed) return;
            if (_keepDashboardHidden != null) _dashboard.VisibleChanged -= _keepDashboardHidden;
            if (_dashboard.WindowState == FormWindowState.Minimized) _dashboard.WindowState = FormWindowState.Normal;
            if (!_dashboard.Visible) _dashboard.Show();
            _dashboard.Activate();
            _dashboard = null;
            _keepDashboardHidden = null;
        }

        public static void DisposeAfterDialog(IDisposable value)
        {
            if (value is Control control && control.FindForm() is Form form &&
                form.Visible && Sessions.TryGetValue(form, out _)) return;
            value.Dispose();
        }
    }
}
