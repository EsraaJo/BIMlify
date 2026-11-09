using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics;

[assembly: AssemblyVersion("0.0.0.0")]
namespace BIMlify.PlanRangeUi
{
    public static class PlanRangeInteraction
    {
        private static IntPtr _mainWindowHandle;
        private static readonly HashSet<Form> DeferredForms = new HashSet<Form>();
        private static readonly HashSet<Form> ModelessForms = new HashSet<Form>();

        public static void SetMainWindowHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Revit did not provide a main-window handle.");
            _mainWindowHandle = handle;
        }

        public static DialogResult ShowOwnedDialog(Form form)
        {
            return ShowOwnedDialog(form, _mainWindowHandle);
        }
        private sealed class WindowOwner : IWin32Window
        {
            private readonly IntPtr _handle;
            public WindowOwner(IntPtr handle) { _handle = handle; }
            public IntPtr Handle { get { return _handle; } }
        }

        public static DialogResult ShowOwnedDialog(Form form, IntPtr ownerHandle)
        {
            if (form == null || form.IsDisposed) throw new ArgumentNullException("form");
            if (ownerHandle == IntPtr.Zero) throw new InvalidOperationException("Revit did not provide a valid main-window handle for the Plan View Range dialog.");
            return form.ShowDialog(new WindowOwner(ownerHandle));
        }

        public static void ShowOwnedDialogDeferred(Form form, IntPtr ownerHandle)
        {
            if (form == null || form.IsDisposed) throw new ArgumentNullException("form");
            if (ownerHandle == IntPtr.Zero) throw new InvalidOperationException("Revit did not provide a valid main-window handle for the Plan View Range dialog.");
            IntPtr createdHandle = form.Handle;
            lock (DeferredForms) DeferredForms.Add(form);
            form.BeginInvoke((MethodInvoker)delegate
            {
                try
                {
                    if (!form.IsDisposed) form.ShowDialog(new WindowOwner(ownerHandle));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Plan View Range could not display.\r\n\r\n" + ex.Message, "BIMlify — Plan View Range", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    lock (DeferredForms) DeferredForms.Remove(form);
                    if (!form.IsDisposed) form.Dispose();
                }
            });
        }
        public static void ShowOwnedModelessDialog(Form form, IntPtr ownerHandle)
        {
            if (form == null || form.IsDisposed) throw new ArgumentNullException("form");
            if (ownerHandle == IntPtr.Zero) throw new InvalidOperationException("Revit did not provide a valid main-window handle for the Plan View Range window.");
            IntPtr createdHandle = form.Handle;
            lock (ModelessForms) ModelessForms.Add(form);
            form.FormClosed += delegate { lock (ModelessForms) ModelessForms.Remove(form); };
            try
            {
                form.Show(new WindowOwner(ownerHandle));
            }
            catch
            {
                lock (ModelessForms) ModelessForms.Remove(form);
                throw;
            }
        }
        public static void Attach(Form form)
        {
            try
            {
                if (form == null || form.IsDisposed) return;
                Control preview = All(form).FirstOrDefault(delegate(Control c) { return c.GetType().Name == "PlanRangePreview"; });
                if (preview == null || preview.Parent == null || !(preview.Parent is Panel)) return;
                Panel panel = (Panel)preview.Parent;
                if (panel.Controls.OfType<Panel>().Any(delegate(Panel p) { return p.Name == "PlanRangeSchematicViewport"; })) return;
                Panel footer = panel.Controls.OfType<Panel>().FirstOrDefault(delegate(Panel p) { return p.Dock == DockStyle.Bottom && p.Height >= 38; });
                if (footer == null) return;
                Label caption = footer.Controls.OfType<Label>().FirstOrDefault();
                foreach (Button button in footer.Controls.OfType<Button>().ToArray())
                {
                    footer.Controls.Remove(button);
                    button.Dispose();
                }
                if (caption != null) caption.Width = Math.Max(80, footer.ClientSize.Width - caption.Left - 8);
                Panel viewport = new Panel();
                viewport.Name = "PlanRangeSchematicViewport";
                viewport.Dock = DockStyle.Fill;
                viewport.BackColor = Color.White;
                viewport.Margin = Padding.Empty;
                viewport.Padding = Padding.Empty;
                panel.Controls.Remove(preview);
                preview.Dock = DockStyle.None;
                preview.Location = Point.Empty;
                preview.TabStop = true;
                preview.Cursor = Cursors.Hand;
                preview.Size = viewport.ClientSize;
                viewport.Controls.Add(preview);
                panel.Controls.Add(viewport);
                viewport.SendToBack();
                footer.BringToFront();
                foreach (Label label in panel.Controls.OfType<Label>()) label.BringToFront();
                FieldInfo zoom = preview.GetType().GetField("Zoom", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                viewport.Resize += delegate { if (!preview.IsDisposed) preview.Size = viewport.ClientSize; };
                bool dragging = false;
                Point originMouse = Point.Empty;
                Point originPreview = Point.Empty;
                MouseEventHandler down = delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Left) return;
                    dragging = true;
                    originMouse = Control.MousePosition;
                    originPreview = preview.Location;
                    preview.Capture = true;
                    preview.Cursor = Cursors.Hand;
                };
                MouseEventHandler move = delegate(object sender, MouseEventArgs e)
                {
                    if (!dragging) return;
                    Point now = Control.MousePosition;
                    preview.Location = new Point(originPreview.X + now.X - originMouse.X, originPreview.Y + now.Y - originMouse.Y);
                };
                MouseEventHandler up = delegate(object sender, MouseEventArgs e)
                {
                    if (!dragging) return;
                    dragging = false;
                    preview.Capture = false;
                };
                preview.MouseDown += down; viewport.MouseDown += down;
                preview.MouseMove += move; viewport.MouseMove += move;
                preview.MouseUp += up; viewport.MouseUp += up;
                MouseEventHandler wheel = delegate(object sender, MouseEventArgs e)
                {
                    if (e.Delta == 0 || zoom == null || preview.IsDisposed) return;
                    try
                    {
                        float current = Convert.ToSingle(zoom.GetValue(preview));
                        float factor = e.Delta > 0 ? 1.15f : 0.86956525f;
                        zoom.SetValue(preview, Math.Max(0.35f, Math.Min(3f, current * factor)));
                        preview.Invalidate();
                    }
                    catch { }
                };
                preview.MouseWheel += wheel; viewport.MouseWheel += wheel;
                viewport.MouseEnter += delegate { if (!preview.IsDisposed) preview.Focus(); };
                form.Shown += delegate
                {
                    try
                    {
                        preview.Location = Point.Empty;
                        preview.Size = viewport.ClientSize;
                        if (zoom != null) zoom.SetValue(preview, 0.82f);
                        preview.Invalidate();
                    }
                    catch { }
                };
            }
            catch { }
        }

        private static IEnumerable<Control> All(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control descendant in All(child)) yield return descendant;
            }
        }
    }
}









