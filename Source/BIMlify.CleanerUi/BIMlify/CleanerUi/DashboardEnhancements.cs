using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ComboBox = System.Windows.Forms.ComboBox;
using Color = System.Drawing.Color;
using Control = System.Windows.Forms.Control;
using Form = System.Windows.Forms.Form;
using Panel = System.Windows.Forms.Panel;
using TextBox = System.Windows.Forms.TextBox;
using Rectangle = System.Drawing.Rectangle;

namespace BIMlify.CleanerUi;

public static class DashboardEnhancements
{
    public static void InitializeQuickBar(Form bar, Control tools, IntPtr revitHandle, bool autoHide)
    {
        if (bar == null || bar.IsDisposed) return;
        Timer timer = null;
        try
        {
            Func<Rectangle> area = () => (revitHandle == IntPtr.Zero ? Screen.FromControl(bar) : Screen.FromHandle(revitHandle)).WorkingArea;
            Func<int> width = () =>
            {
                QuickBarRect rect;
                int ownerWidth = revitHandle != IntPtr.Zero && GetQuickBarWindowRect(revitHandle, out rect) ? rect.Right - rect.Left : 0;
                int available = area().Width;
                int minimum = Math.Min(900, Math.Max(1, available - 24));
                return Math.Max(minimum, Math.Min(1100, Math.Min(ownerWidth >= 900 ? ownerWidth : available, available - 24)));
            };
            Func<System.Drawing.Point> dock = () =>
            {
                QuickBarRect rect;
                if (revitHandle != IntPtr.Zero && GetQuickBarWindowRect(revitHandle, out rect))
                    return new System.Drawing.Point(rect.Left + Math.Max(0, (rect.Right - rect.Left - bar.Width) / 2),
                        Math.Max(rect.Top, rect.Bottom - bar.Height - 38));
                Rectangle bounds = area();
                return new System.Drawing.Point(bounds.Left + Math.Max(0, (bounds.Width - bar.Width) / 2),
                    Math.Max(bounds.Top, bounds.Bottom - bar.Height - 8));
            };
            bar.MinimumSize = new Size(Math.Min(900, width()), 76);
            bar.MaximumSize = new Size(1100, 76);
            bar.Size = new Size(width(), 76);
            bar.Location = dock();
            bool docked = true;
            bool moving = false;
            Action clamp = () =>
            {
                Rectangle screen = Screen.FromRectangle(bar.Bounds).WorkingArea;
                var point = new System.Drawing.Point(
                    Math.Max(screen.Left, Math.Min(screen.Right - bar.Width, bar.Left)),
                    Math.Max(screen.Top, Math.Min(screen.Bottom - bar.Height, bar.Top)));
                if (bar.Location != point) bar.Location = point;
            };
            EventHandler moved = (sender, args) =>
            {
                if (moving || bar.IsDisposed) return;
                docked = false;
                moving = true;
                try { clamp(); }
                catch (Exception error) { LogQuickBarError(error); }
                finally { moving = false; }
            };
            tools?.PerformLayout();
            bar.Invalidate(true);
            timer = new Timer { Interval = 60 };
            var shownAt = DateTime.UtcNow;
            timer.Tick += (sender, args) =>
            {
                if (bar.IsDisposed || moving) return;
                moving = true;
                try
                {
                    int nextWidth = width();
                    if (bar.Width != nextWidth) bar.Width = nextWidth;
                    var target = dock();
                    int horizontal = Math.Max(360, bar.Width / 2);
                    int vertical = Math.Max(200, bar.Height * 3);
                    if (docked || (Math.Abs(bar.Left - target.X) <= horizontal && Math.Abs(bar.Top - target.Y) <= vertical))
                    {
                        if (bar.Location != target) bar.Location = target;
                        docked = true;
                    }
                    clamp();
                    if (!autoHide) return;
                    Rectangle hoverBounds = bar.Bounds;
                    hoverBounds.Inflate(18, 18);
                    double targetOpacity = hoverBounds.Contains(Cursor.Position) || (DateTime.UtcNow - shownAt).TotalMilliseconds < 1100 ? 1.0 : 0.82;
                    double delta = targetOpacity - bar.Opacity;
                    if (Math.Abs(delta) > 0.005) bar.Opacity += Math.Sign(delta) * Math.Min(0.06, Math.Abs(delta));
                }
                catch (Exception error) { timer.Stop(); LogQuickBarError(error); }
                finally { moving = false; }
            };
            bar.LocationChanged += moved;
            bar.FormClosed += (sender, args) =>
            {
                bar.LocationChanged -= moved;
                timer.Dispose();
            };
            timer.Start();
        }
        catch (Exception error)
        {
            timer?.Dispose();
            LogQuickBarError(error);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct QuickBarRect { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    private static extern bool GetQuickBarWindowRect(IntPtr handle, out QuickBarRect rect);

    private static void LogQuickBarError(Exception error)
    {
        try
        {
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMlify");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "QuickBar.log"), DateTime.Now.ToString("O") + " " + error + Environment.NewLine);
        }
        catch { }
    }

    private const string DefaultSupabaseProjectUrl = "https://kragfkqugjicvozoehxy.supabase.co";
    private const string DefaultSupabasePublishableKey = "sb_publishable_1fjuWypoSRpN7FdQS4ToKw_97FdvT3T";
    private static UIApplication _application;
    private static Form _dashboard;
    private static Form _quickBarRestoreHook;
    private static readonly List<Form> _quickBarMinimizedTools = new List<Form>();
    private static Label _status;
    private static DateTime _statusChanged = DateTime.MinValue;
    private static string _pendingTool = "";
    private static Label _greeting;
    private static Label _greetingNameLabel;
    private static string _greetingName = "Architect";
    private static Timer _statusTimer;
    private static Timer _quickBarToolsTimer;
    private static AuthSession _activeSession;
    private static readonly object AuthSessionSync = new object();
    private static readonly MethodInfo RaiseClick = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Color Muted = Color.FromArgb(148, 151, 154);
    private static readonly Color Green = Color.FromArgb(93, 137, 100);
    private static readonly Color Red = Color.FromArgb(171, 105, 99);
    private static readonly Color Gold = Color.FromArgb(214, 171, 61);

    private sealed class SearchToolItem
    {
        internal string Name;
        internal string Action;
        internal bool IsQa;
        public override string ToString() => Name;
    }

    /// <summary>Adds the common tool-search control to an existing BIMlify navigation rail.</summary>
    public static void AttachToolSearchButton(Form owner, int top)
    {
        if (owner == null || owner.IsDisposed) return;
        Control rail = Descendants(owner).FirstOrDefault(x => x is Panel && x.Dock == DockStyle.Left && x.Width >= 50 && x.Width <= 72);
        if (rail != null) AttachToolSearchButton(rail, owner, top);
    }

    public static void AttachToolSearchButton(Control rail, Form owner, int top)
    {
        AttachToolSearchButton(rail, owner, top, false);
    }

    public static void AttachToolSearchButton(Control rail, Form owner, int top, bool fromQualitySuite)
    {
        if (rail == null || rail.IsDisposed || owner == null || owner.IsDisposed) return;
        bool dashboard = IsDashboardSidebar(rail);
        HideRedundantSidebarAction(rail);
        if (rail.Controls.ContainsKey("BIMlifyToolSearch")) return;
        // Match the 6 px gap used between the lower sidebar buttons.
        // On the dashboard Search takes Home's former first slot.
        int searchTop = dashboard ? 10 : 58;
        SearchRailButton button = new SearchRailButton { Name = "BIMlifyToolSearch", Left = 8, Top = searchTop, Width = 42, Height = 42, Anchor = AnchorStyles.Top | AnchorStyles.Left, TabIndex = 2, TabStop = true, AccessibleName = "Search BIMlify tools", AccessibleRole = AccessibleRole.PushButton };
        ToolTip tip = new ToolTip { InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 4500, ShowAlways = true };
        tip.SetToolTip(button, "Search BIMlify tools");
        button.Click += (s, e) => ShowToolSearch(owner, fromQualitySuite);
        rail.Controls.Add(button);
        button.BringToFront();
        button.Disposed += (s, e) => tip.Dispose();
    }

    private static void HideRedundantSidebarAction(Control rail)
    {
        if (rail == null || rail.IsDisposed) return;
        Control sidebar = String.Equals(rail.GetType().Name, "BIMlifySidebar", StringComparison.Ordinal)
            ? rail
            : rail.Controls.Cast<Control>().FirstOrDefault(c => String.Equals(c.GetType().Name, "BIMlifySidebar", StringComparison.Ordinal));
        if (sidebar == null) return;
        bool dashboard = IsDashboardSidebar(rail);

        // Sidebar glyphs are rendered by BIMlifySidebar itself. Do not add a
        // second Home overlay: on secondary windows the native Home now occupies
        // Back's former slot, and Search occupies the following slot.
    }

    private static bool IsDashboardSidebar(Control rail)
    {
        Control sidebar = rail == null ? null : (String.Equals(rail.GetType().Name, "BIMlifySidebar", StringComparison.Ordinal)
            ? rail
            : rail.Controls.Cast<Control>().FirstOrDefault(c => String.Equals(c.GetType().Name, "BIMlifySidebar", StringComparison.Ordinal)));
        if (sidebar == null) return false;
        try
        {
            FieldInfo flag = sidebar.GetType().GetField("dashboard", BindingFlags.Instance | BindingFlags.NonPublic);
            return flag != null && flag.FieldType == typeof(bool) && (bool)flag.GetValue(sidebar);
        }
        catch { return false; }
    }

    public static void ShowToolSearch(Form owner)
    {
        ShowToolSearch(owner, false);
    }

    /// <summary>Shows the BIMlify account dialog and connects email/password auth to Supabase.</summary>
    public static void ShowAccount(Form owner)
    {
        if (owner == null || owner.IsDisposed) return;
        using (Form dialog = CreateSettingsStyleDialog())
        {
            pendingAvatar = null;
            removeAvatarPending = false;
            Color surface = Color.FromArgb(238, 241, 245);
            Color ink = Color.FromArgb(55, 67, 79);
            dialog.Text = "BIMlify — Account & Subscription";
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.Width = 600; dialog.Height = 430; dialog.MinimumSize = new Size(560, 410);
            dialog.MaximizeBox = true; dialog.MinimizeBox = true; dialog.ShowIcon = false; dialog.ShowInTaskbar = false;
            // This compact dialog uses fixed, measured row geometry. Letting WinForms
            // rescale those bounds again at high DPI pushed the second row under footer.
            dialog.AutoScaleMode = AutoScaleMode.None; dialog.BackColor = surface; dialog.Font = new Font("Segoe UI", 9f);
            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(24, 14, 82, 12) };
            Label heading = new Label { Text = "BIMlify Account", Left = 24, Top = 18, Width = 390, Height = 24, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            Label displayNameLabel = new Label { Text = "Display name (shown in your greeting)", Left = 124, Top = 52, Width = 290, Height = 18, ForeColor = Color.FromArgb(78, 85, 92), BackColor = surface, Font = new Font("Segoe UI", 8f) };
            TextBox displayName = new TextBox { Left = 124, Top = 72, Width = 332, Height = 26, Font = new Font("Segoe UI", 9f), MaxLength = 48 };
            PictureBox avatar = new PictureBox { Left = 28, Top = 54, Width = 84, Height = 84, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            SetAvatarImage(avatar, CreateDefaultAvatar());
            Button choosePhoto = CreateSharedStyleButton("Choose photo", false); choosePhoto.SetBounds(124, 103, 104, 28); choosePhoto.Enabled = false;
            Button removePhoto = CreateSharedStyleButton("Remove photo", false); removePhoto.SetBounds(236, 103, 104, 28); removePhoto.Enabled = false;
            Button saveProfile = new Button
            {
                Text = "Save changes",
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = Color.FromArgb(250, 230, 168),
                ForeColor = ink,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Enabled = false
            };
            saveProfile.FlatAppearance.BorderSize = 1;
            saveProfile.FlatAppearance.BorderColor = Color.FromArgb(198, 151, 42);
            saveProfile.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 239, 190);
            saveProfile.FlatAppearance.MouseDownBackColor = Color.FromArgb(250, 230, 168);
            saveProfile.SetBounds(348, 103, 108, 28);
            Label avatarHint = new Label { Text = "Optional photo", Left = 20, Top = 141, Width = 100, Height = 18, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(105, 117, 129), BackColor = surface, Font = new Font("Segoe UI", 7f) };
            Label emailLabel = new Label { Text = "Email", Left = 24, Top = 148, Width = 100, Height = 18, ForeColor = Color.FromArgb(78, 85, 92), BackColor = surface, Font = new Font("Segoe UI", 8f) };
            TextBox email = new TextBox { Left = 24, Top = 168, Width = 390, Height = 27, Font = new Font("Segoe UI", 9f) };
            Label passwordLabel = new Label { Text = "Password", Left = 24, Top = 202, Width = 100, Height = 18, ForeColor = Color.FromArgb(78, 85, 92), BackColor = surface, Font = new Font("Segoe UI", 8f) };
            TextBox password = new TextBox { Left = 24, Top = 222, Width = 390, Height = 27, Font = new Font("Segoe UI", 9f), UseSystemPasswordChar = true };
            Label status = new Label { Text = "Sign in, or create an account with a display name. You can add a photo after signing in.", Left = 24, Top = 260, Width = 430, Height = 54, ForeColor = Color.FromArgb(90, 96, 103), BackColor = surface, Font = new Font("Segoe UI", 8f) };
            LinkLabel modeToggle = new LinkLabel { Text = "New to BIMlify? Create an account", Left = 24, Top = 318, Width = 300, Height = 20, LinkColor = Gold, ActiveLinkColor = Gold, VisitedLinkColor = Gold, BackColor = surface, Font = new Font("Segoe UI", 8f) };
            Button signIn = CreateSettingsStyleButton("Sign in", DialogResult.None);
            Button signUp = CreateSettingsStyleButton("Create account", DialogResult.None);
            signIn.Width = signUp.Width = 112; signIn.Left = signUp.Left = 0; signIn.Height = signUp.Height = 30; signIn.Top = signUp.Top = 3; signUp.Visible = false;
            // Keep the outer button margins visually balanced: the auth actions start
            // 28 px from the left, matching Close's 28 px right margin.
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(28, 12, 16, 8) };
            Panel actions = new Panel { Dock = DockStyle.Right, Width = 108, Height = 38, BackColor = footer.BackColor };
            Panel authActions = new Panel { Dock = DockStyle.Left, Width = 136, Height = 38, BackColor = footer.BackColor };
            Button close = CreateSettingsStyleButton("Close", DialogResult.Cancel); close.Left = 0; close.Top = 3; close.Width = 96; close.Height = 30;
            authActions.Controls.Add(signIn); authActions.Controls.Add(signUp);
            actions.Controls.Add(close); footer.Controls.Add(actions); footer.Controls.Add(authActions);
            page.Controls.Add(heading); page.Controls.Add(avatar); page.Controls.Add(avatarHint);
            page.Controls.Add(displayNameLabel); page.Controls.Add(displayName); page.Controls.Add(choosePhoto); page.Controls.Add(removePhoto); page.Controls.Add(saveProfile);
            page.Controls.Add(emailLabel); page.Controls.Add(email); page.Controls.Add(passwordLabel); page.Controls.Add(password); page.Controls.Add(status); page.Controls.Add(modeToggle);
            AddSettingsStyleMark(page);
            dialog.Controls.Add(page); dialog.Controls.Add(footer);
            AddSettingsStyleTitleBar(dialog); ApplySettingsStyleWindowCorners(dialog);
            dialog.AcceptButton = signIn; dialog.CancelButton = close;

            bool createMode = false;
            bool profileSaveInProgress = false;
            Action<bool> setMode = create =>
            {
                createMode = create;
                bool signedIn = _activeSession != null;
                avatar.Visible = avatarHint.Visible = choosePhoto.Visible = removePhoto.Visible = saveProfile.Visible = signedIn;
                choosePhoto.Enabled = removePhoto.Enabled = saveProfile.Enabled = signedIn;
                displayNameLabel.Visible = displayName.Visible = signedIn || create;
                emailLabel.Visible = email.Visible = passwordLabel.Visible = password.Visible = !signedIn;
                modeToggle.Visible = !signedIn;
                modeToggle.Text = create ? "Already have an account? Sign in" : "New to BIMlify? Create an account";
                dialog.AcceptButton = signedIn ? null : (create ? (IButtonControl)signUp : signIn);
                if (signedIn)
                {
                    // Keep profile editing available while a restored session is checked;
                    // emphasize Save changes and leave photo actions in the neutral style.
                    choosePhoto.Enabled = removePhoto.Enabled = saveProfile.Enabled = true;
                    ApplyPrimaryAccountButtonStyle(saveProfile);
                    displayNameLabel.SetBounds(130, 63, 340, 18); displayName.SetBounds(130, 88, 340, 27);
                    avatar.SetBounds(28, 62, 84, 84); avatarHint.SetBounds(20, 149, 100, 18);
                    choosePhoto.SetBounds(130, 128, 104, 28); removePhoto.SetBounds(246, 128, 104, 28); saveProfile.SetBounds(362, 128, 108, 28);
                    status.SetBounds(24, 190, 430, 48);
                }
                else
                {
                    displayNameLabel.SetBounds(24, 62, 290, 18); displayName.SetBounds(24, 86, 390, 27);
                    emailLabel.Top = create ? 129 : 87; email.Top = create ? 152 : 110;
                    passwordLabel.Top = create ? 192 : 153; password.Top = create ? 215 : 176;
                    status.Top = create ? 258 : 219; status.Height = 38;
                    modeToggle.Top = status.Bottom + 10; signIn.Visible = !create; signUp.Visible = create;
                    signUp.Text = "Create account";
                    ApplySecondaryAccountButtonStyle(signUp);
                }
            };
            Action<AuthSession> showSignedIn = session =>
            {
                _activeSession = session;
                email.Text = session.Email ?? "";
                displayName.Text = session.DisplayName ?? "";
                signIn.Visible = false; signUp.Visible = true; signIn.Enabled = signUp.Enabled = true; signUp.Text = "Sign out";
                ApplyPrimaryAccountButtonStyle(signUp);
                choosePhoto.Enabled = removePhoto.Enabled = saveProfile.Enabled = true;
                setMode(false);
                status.ForeColor = Color.FromArgb(93, 137, 100);
                status.Text = "Signed in as " + (session.Email ?? "") + ".";
                modeToggle.Visible = false;
                SetGreetingName(session.DisplayName);
                byte[] savedAvatar = LoadProfileAvatar(session.UserId);
                if (savedAvatar != null) SetAvatarImage(avatar, savedAvatar);
            };
            int restoreGeneration = 0;
            bool restorePending = false;
            AuthSession savedSession = _activeSession ?? LoadAuthSession();
            if (savedSession != null)
            {
                email.Text = savedSession.Email ?? "";
                displayName.Text = savedSession.DisplayName == "Architect" ? "" : (savedSession.DisplayName ?? "");
                if (!String.IsNullOrWhiteSpace(savedSession.DisplayName)) SetGreetingName(savedSession.DisplayName);
                byte[] cachedAvatar = LoadProfileAvatar(savedSession.UserId);
                if (cachedAvatar != null) SetAvatarImage(avatar, cachedAvatar);
                // Restore the previously authenticated local account immediately so Sign out
                // remains available while Supabase validates/rotates its refresh token.
                _activeSession = savedSession;
                showSignedIn(savedSession);
                restorePending = true;
                int thisRestore = ++restoreGeneration;
                status.ForeColor = Color.FromArgb(90, 96, 103); status.Text = "Restoring your saved sign-in…";
                Task<AuthSession> restoreTask = Task.Run(() => RefreshAuthSession(savedSession, () => ReferenceEquals(_activeSession, savedSession)));
                Action<Task<AuthSession>> completeRestore = task =>
                {
                    if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
                    try { dialog.BeginInvoke((Action)(() =>
                    {
                        if (dialog.IsDisposed) return;
                        if (thisRestore != restoreGeneration || !restorePending || !ReferenceEquals(_activeSession, savedSession)) return;
                        restorePending = false;
                        AuthSession session = !task.IsFaulted && !task.IsCanceled ? task.Result : null;
                        if (session != null && !string.IsNullOrEmpty(session.AccessToken))
                        {
                            showSignedIn(session);
                        }
                        else if (System.IO.File.Exists(AuthSessionPath()))
                        {
                            // A connection/API failure does not erase a previously signed-in
                            // user's local account or photo. Keep Sign out available for them.
                            showSignedIn(savedSession);
                            status.ForeColor = Color.FromArgb(171, 137, 81);
                            status.Text = "Could not verify the saved sign-in. Check your connection; Sign out is available.";
                        }
                        else
                        {
                            _activeSession = null;
                            status.ForeColor = Color.FromArgb(171, 105, 99);
                            status.Text = "Your saved sign-in has expired. Please sign in again.";
                            signIn.Enabled = signUp.Enabled = true; signUp.Text = "Create account"; setMode(false);
                        }
                    })); } catch { }
                };
                restoreTask.ContinueWith(completeRestore);
                Task.Delay(8000).ContinueWith(_ =>
                {
                    if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
                    try { dialog.BeginInvoke((Action)(() =>
                    {
                        if (dialog.IsDisposed || thisRestore != restoreGeneration || !restorePending || !ReferenceEquals(_activeSession, savedSession)) return;
                        status.ForeColor = Color.FromArgb(171, 137, 81);
                        status.Text = "Sign-in check is taking longer. Your saved account and Sign out remain available.";
                    })); } catch { }
                });
            }
            Action<bool> authenticate = createAccount =>
            {
                // A manual auth attempt supersedes any background restore result.
                restorePending = false;
                restoreGeneration++;
                string address = (email.Text ?? string.Empty).Trim();
                string secret = password.Text ?? string.Empty;
                string name = (displayName.Text ?? string.Empty).Trim();
                if (address.Length == 0 || address.IndexOf('@') < 1 || secret.Length < 6)
                {
                    status.ForeColor = Color.FromArgb(171, 105, 99);
                    status.Text = "Enter a valid email and a password with at least 6 characters.";
                    return;
                }
                if (createAccount && name.Length < 2)
                {
                    status.ForeColor = Color.FromArgb(171, 105, 99);
                    status.Text = "Enter the display name you want shown in the dashboard greeting.";
                    displayName.Focus();
                    return;
                }
                signIn.Enabled = signUp.Enabled = false;
                status.ForeColor = Color.FromArgb(90, 96, 103); status.Text = createAccount ? "Creating your account…" : "Signing in…";
                Task.Run(() => SendAuthRequest(createAccount, address, secret, name)).ContinueWith(task =>
                {
                    if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
                    try { dialog.BeginInvoke((Action)(() =>
                    {
                        if (dialog.IsDisposed) return;
                        signIn.Enabled = signUp.Enabled = true;
                        if (task.IsFaulted || task.Result == null)
                        {
                            status.ForeColor = Color.FromArgb(171, 105, 99);
                            status.Text = "Could not reach Supabase. Check your connection and try again.";
                            return;
                        }
                        AuthResult result = task.Result;
                        status.ForeColor = result.Success ? Color.FromArgb(93, 137, 100) : Color.FromArgb(171, 105, 99);
                        status.Text = result.Message;
                        if (!string.IsNullOrEmpty(result.AccessToken))
                        {
                            AuthSession session = CreateAuthSession(address, result);
                            _activeSession = session;
                            bool remembered = !string.IsNullOrEmpty(result.RefreshToken) && SaveAuthSession(session);
                            showSignedIn(session);
                            if (!remembered) status.Text = "Signed in, but this session could not be saved for next time.";
                        }
                        else
                        {
                            signIn.Enabled = signUp.Enabled = true;
                            if (createAccount && result.Success) { setMode(false); password.Clear(); }
                        }
                    })); } catch { }
                });
            };
            modeToggle.LinkClicked += (s, e) =>
            {
                setMode(!createMode);
                status.ForeColor = Color.FromArgb(90, 96, 103);
                status.Text = createMode ? "Create an account with your name, email and password." : "Sign in to your existing BIMlify account.";
                if (createMode) displayName.Focus(); else email.Focus();
            };
            signIn.Click += (s, e) => authenticate(false);
            signUp.Click += (s, e) =>
            {
                AuthSession session = _activeSession;
                if (session != null)
                {
                    string token = session.AccessToken;
                    _activeSession = null;
                    ClearAuthSession();
                    SetGreetingName("Architect");
                    signIn.Enabled = true; signUp.Text = "Create account"; setMode(false);
                    choosePhoto.Enabled = removePhoto.Enabled = saveProfile.Enabled = false;
                    displayName.Text = ""; password.Text = ""; SetAvatarImage(avatar, CreateDefaultAvatar());
                    if (!String.IsNullOrEmpty(token)) Task.Run(() => SendSignOut(token));
                    status.ForeColor = Color.FromArgb(90, 96, 103); status.Text = "Signed out.";
                    return;
                }
                authenticate(true);
            };
            saveProfile.Click += (s, e) =>
            {
                if (profileSaveInProgress) return;
                string name = (displayName.Text ?? "").Trim();
                AuthSession session = _activeSession;
                if (session == null || name.Length < 2)
                {
                    status.ForeColor = Color.FromArgb(171, 105, 99); status.Text = "Enter a display name with at least 2 characters."; return;
                }
                byte[] stagedPhoto = pendingAvatar;
                bool shouldRemovePhoto = removeAvatarPending;
                string previousName = session.DisplayName;
                profileSaveInProgress = true;
                try
                {
                    session.DisplayName = name;
                    if (shouldRemovePhoto && !RemoveProfileAvatar(session.UserId)) throw new System.IO.IOException("Photo could not be removed.");
                    else if (stagedPhoto != null) SaveProfileAvatar(session.UserId, stagedPhoto);
                    if (!SaveAuthSession(session)) throw new InvalidOperationException("Local session could not be saved.");
                }
                catch
                {
                    profileSaveInProgress = false;
                    session.DisplayName = previousName;
                    status.ForeColor = Color.FromArgb(171, 105, 99); status.Text = "Could not save the profile on this Windows account.";
                    return;
                }
                _activeSession = session; pendingAvatar = null; removeAvatarPending = false; SetGreetingName(name);
                // Keep Save changes enabled so WinForms does not replace its gold
                // fill with the system disabled-button color while syncing.
                choosePhoto.Enabled = removePhoto.Enabled = false;
                saveProfile.Enabled = true;
                ApplyPrimaryAccountButtonStyle(saveProfile);
                status.ForeColor = Color.FromArgb(90, 96, 103); status.Text = "Saved on this device. Syncing account name…";
                Task.Run(() => String.Equals(previousName ?? "", name, StringComparison.Ordinal) || UpdateProfileName(session, name)).ContinueWith(task =>
                {
                    if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
                    try { dialog.BeginInvoke((Action)(() =>
                    {
                        profileSaveInProgress = false;
                        saveProfile.Enabled = choosePhoto.Enabled = removePhoto.Enabled = true;
                        ApplyPrimaryAccountButtonStyle(saveProfile);
                        if (!task.IsFaulted && task.Result)
                        {
                            status.ForeColor = Color.FromArgb(93, 137, 100); status.Text = "Profile saved and account name synced.";
                        }
                        else { status.ForeColor = Color.FromArgb(171, 137, 81); status.Text = "Saved on this device; account sync will retry next sign-in."; }
                    })); } catch { }
                });
            };
            choosePhoto.Click += (s, e) =>
            {
                AuthSession session = _activeSession;
                if (session == null) return;
                using (OpenFileDialog picker = new OpenFileDialog { Title = "Choose a profile picture", Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*", CheckFileExists = true })
                {
                    if (picker.ShowDialog(dialog) != DialogResult.OK) return;
                    try
                    {
                        byte[] imageData = NormalizeAvatar(picker.FileName);
                        pendingAvatar = imageData; removeAvatarPending = false;
                        SetAvatarImage(avatar, imageData);
                        status.ForeColor = Color.FromArgb(90, 96, 103);
                        status.Text = "Photo selected. Select Save changes to keep it.";
                    }
                    catch { status.ForeColor = Color.FromArgb(171, 105, 99); status.Text = "Could not load that picture. Choose a PNG, JPG or BMP image."; }
                }
            };
            removePhoto.Click += (s, e) =>
            {
                if (_activeSession == null) return;
                pendingAvatar = null; removeAvatarPending = true; SetAvatarImage(avatar, CreateDefaultAvatar());
                status.ForeColor = Color.FromArgb(90, 96, 103); status.Text = "Photo removal selected. Select Save changes to confirm.";
            };
            dialog.FormClosed += (s, e) => { pendingAvatar = null; removeAvatarPending = false; Image old = avatar.Image; avatar.Image = null; old?.Dispose(); };
            setMode(false);
            dialog.ShowDialog(owner);
        }
    }

    private sealed class AuthResult { internal bool Success; internal string Message; internal string AccessToken; internal string RefreshToken; internal string UserId; internal string DisplayName; internal int ExpiresIn; }
    private sealed class AuthSession { internal string Email; internal string RefreshToken; internal string AccessToken; internal string UserId; internal string DisplayName; internal DateTime ExpiresAt; }
    private static byte[] pendingAvatar;
    private static bool removeAvatarPending;

    private static readonly byte[] AuthSessionEntropy = Encoding.UTF8.GetBytes("BIMlify.Auth.Session.v1");

    private static string AuthSessionPath()
    {
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMlify", "auth.session");
    }

    private static bool SaveAuthSession(AuthSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.Email) || string.IsNullOrWhiteSpace(session.RefreshToken)) return false;
        try
        {
            string path = AuthSessionPath();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string[] fields = { session.Email, session.RefreshToken, session.UserId ?? "", session.DisplayName ?? "" };
            byte[] clear = Encoding.UTF8.GetBytes(String.Join("\n", fields.Select(x => Convert.ToBase64String(Encoding.UTF8.GetBytes(x)))));
            byte[] protectedData = ProtectedData.Protect(clear, AuthSessionEntropy, DataProtectionScope.CurrentUser);
            System.IO.File.WriteAllBytes(path, protectedData);
            Array.Clear(clear, 0, clear.Length);
            return true;
        }
        catch { return false; }
    }

    private static AuthSession LoadAuthSession()
    {
        try
        {
            string path = AuthSessionPath();
            if (!System.IO.File.Exists(path)) return null;
            byte[] protectedData = System.IO.File.ReadAllBytes(path);
            byte[] clear = ProtectedData.Unprotect(protectedData, AuthSessionEntropy, DataProtectionScope.CurrentUser);
            string value = Encoding.UTF8.GetString(clear);
            Array.Clear(clear, 0, clear.Length);
            string[] fields = value.Split('\n');
            if (fields.Length < 2) return null;
            string Decode(int i) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(fields[i])); } catch { return fields[i]; } }
            return new AuthSession { Email = Decode(0).Trim(), RefreshToken = Decode(1).Trim(), UserId = fields.Length > 2 ? Decode(2) : "", DisplayName = fields.Length > 3 ? Decode(3) : "" };
        }
        catch { return null; }
    }

    private static void ClearAuthSession()
    {
        try { string path = AuthSessionPath(); if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
    }

    private static string SupabaseConfig(string name)
    {
        string fallback = string.Equals(name, "ProjectUrl", StringComparison.OrdinalIgnoreCase) ? DefaultSupabaseProjectUrl :
            string.Equals(name, "PublishableKey", StringComparison.OrdinalIgnoreCase) ? DefaultSupabasePublishableKey : null;
        try
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(DashboardEnhancements).Assembly.Location), "BIMlify.Supabase.config");
            if (!System.IO.File.Exists(path)) return fallback;
            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                int split = line.IndexOf('=');
                if (split > 0 && string.Equals(line.Substring(0, split).Trim(), name, StringComparison.OrdinalIgnoreCase)) return line.Substring(split + 1).Trim();
            }
        }
        catch { }
        return fallback;
    }

    private static AuthResult SendAuthRequest(bool createAccount, string email, string password, string displayName)
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        string projectUrl = SupabaseConfig("ProjectUrl"), publishableKey = SupabaseConfig("PublishableKey");
        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(publishableKey))
            return new AuthResult { Success = false, Message = "Supabase settings are missing. Please contact BIMlify support." };
        string endpoint = projectUrl.TrimEnd('/') + (createAccount ? "/auth/v1/signup" : "/auth/v1/token?grant_type=password");
        string json = "{\"email\":\"" + EscapeJson(email) + "\",\"password\":\"" + EscapeJson(password) + "\"" +
            (createAccount ? ",\"data\":{\"display_name\":\"" + EscapeJson(displayName) + "\"}" : "") + "}";
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
        request.Method = "POST"; request.ContentType = "application/json"; request.Accept = "application/json";
        request.Headers["apikey"] = publishableKey; request.Timeout = 20000; request.ReadWriteTimeout = 20000;
        byte[] data = Encoding.UTF8.GetBytes(json); request.ContentLength = data.Length;
        using (System.IO.Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
        string responseText; HttpStatusCode code;
        try
        {
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream())) { responseText = reader.ReadToEnd(); code = response.StatusCode; }
        }
        catch (WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response == null) throw;
            using (response) using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream()))
                return new AuthResult { Success = false, Message = AuthError(reader.ReadToEnd(), response.StatusCode) };
        }
        AuthResult parsed = ParseAuthResult(responseText);
        if (!string.IsNullOrEmpty(parsed.AccessToken)) { parsed.Success = true; parsed.Message = "Signed in."; return parsed; }
        if (createAccount && (int)code >= 200 && (int)code < 300)
            return new AuthResult { Success = true, DisplayName = displayName, Message = "Account created. Check your email to confirm your address, then sign in." };
        return new AuthResult { Success = false, Message = "Supabase returned an unexpected response. Please try again." };
    }

    private static AuthResult SendRefreshRequest(string refreshToken)
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        string projectUrl = SupabaseConfig("ProjectUrl"), publishableKey = SupabaseConfig("PublishableKey");
        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(publishableKey) || string.IsNullOrWhiteSpace(refreshToken))
            return new AuthResult { Success = false, Message = "Saved sign-in data is unavailable." };
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(projectUrl.TrimEnd('/') + "/auth/v1/token?grant_type=refresh_token");
        request.Method = "POST"; request.ContentType = "application/json"; request.Accept = "application/json";
        request.Headers["apikey"] = publishableKey; request.Timeout = 20000; request.ReadWriteTimeout = 20000;
        byte[] data = Encoding.UTF8.GetBytes("{\"refresh_token\":\"" + EscapeJson(refreshToken) + "\"}");
        request.ContentLength = data.Length;
        using (System.IO.Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
        string responseText;
        try
        {
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream())) responseText = reader.ReadToEnd();
        }
        catch (WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response == null) throw;
            using (response) using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream()))
                return new AuthResult { Success = false, Message = AuthError(reader.ReadToEnd(), response.StatusCode) };
        }
        AuthResult parsed = ParseAuthResult(responseText);
        return !string.IsNullOrEmpty(parsed.AccessToken) && !string.IsNullOrEmpty(parsed.RefreshToken)
            ? CompleteAuthResult(parsed)
            : new AuthResult { Success = false, Message = "Supabase returned an unexpected response. Please sign in again." };
    }

    private static AuthResult ParseAuthResult(string json)
    {
        string access = JsonString(json, "access_token"), refresh = JsonString(json, "refresh_token");
        string userId = JsonString(json, "id");
        string displayName = JsonString(json, "display_name");
        int expires = 3600;
        int.TryParse(System.Text.RegularExpressions.Regex.Match(json ?? "", "\\\"expires_in\\\"\\s*:\\s*(\\d+)").Groups[1].Value, out expires);
        return new AuthResult { AccessToken = access, RefreshToken = refresh, UserId = userId, DisplayName = displayName, ExpiresIn = expires };
    }

    private static AuthResult CompleteAuthResult(AuthResult result) { result.Success = true; result.Message = "Signed in."; return result; }

    private static string JsonString(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return "";
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(json,
            "\\\"" + System.Text.RegularExpressions.Regex.Escape(key) + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value) : "";
    }

    private static AuthSession CreateAuthSession(string email, AuthResult result)
    {
        return new AuthSession { Email = email, RefreshToken = result.RefreshToken, AccessToken = result.AccessToken,
            UserId = result.UserId, DisplayName = String.IsNullOrWhiteSpace(result.DisplayName) ? "Architect" : result.DisplayName,
            ExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(60, result.ExpiresIn - 60)) };
    }

    private static AuthSession RefreshAuthSession(AuthSession saved, Func<bool> mayCommit = null)
    {
        if (saved == null || String.IsNullOrEmpty(saved.RefreshToken)) return null;
        lock (AuthSessionSync)
        {
            if (saved.ExpiresAt > DateTime.UtcNow.AddMinutes(1) && !String.IsNullOrEmpty(saved.AccessToken)) return saved;
            AuthResult result = SendRefreshRequest(saved.RefreshToken);
            if (result == null || !result.Success)
            {
                string error = result == null ? "" : (result.Message ?? "");
                if (error.IndexOf("refresh token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    error.IndexOf("invalid grant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    error.IndexOf("token not found", StringComparison.OrdinalIgnoreCase) >= 0)
                    ClearAuthSession();
                return null;
            }
            AuthSession fresh = CreateAuthSession(saved.Email, result);
            if (String.IsNullOrEmpty(fresh.UserId)) fresh.UserId = saved.UserId;
            if (String.IsNullOrEmpty(fresh.DisplayName) || fresh.DisplayName == "Architect") fresh.DisplayName = saved.DisplayName;
            if (mayCommit != null && !mayCommit()) return null;
            saved.AccessToken = fresh.AccessToken;
            saved.RefreshToken = fresh.RefreshToken;
            saved.UserId = fresh.UserId;
            saved.DisplayName = fresh.DisplayName;
            saved.ExpiresAt = fresh.ExpiresAt;
            SaveAuthSession(saved);
            return saved;
        }
    }

    private static bool UpdateProfileName(AuthSession session, string name)
    {
        try
        {
            session = RefreshAuthSession(session);
            if (session == null || String.IsNullOrEmpty(session.AccessToken)) return false;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(SupabaseConfig("ProjectUrl").TrimEnd('/') + "/auth/v1/user");
            request.Method = "PUT"; request.ContentType = "application/json"; request.Headers["apikey"] = SupabaseConfig("PublishableKey");
            request.Headers["Authorization"] = "Bearer " + session.AccessToken; request.Timeout = 20000;
            byte[] data = Encoding.UTF8.GetBytes("{\"data\":{\"display_name\":\"" + EscapeJson(name) + "\"}}"); request.ContentLength = data.Length;
            using (System.IO.Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                bool ok = (int)response.StatusCode >= 200 && (int)response.StatusCode < 300;
                if (ok) { session.DisplayName = name; SaveAuthSession(session); }
                return ok;
            }
        }
        catch { return false; }
    }

    private static byte[] CreateDefaultAvatar()
    {
        using (Bitmap bitmap = new Bitmap(128, 128))
        using (Graphics g = Graphics.FromImage(bitmap))
        using (SolidBrush bg = new SolidBrush(Color.FromArgb(250, 247, 238)))
        using (SolidBrush silhouette = new SolidBrush(Color.FromArgb(174, 181, 187)))
        using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            g.FillEllipse(bg, 3, 3, 122, 122);
            g.FillEllipse(silhouette, 45, 28, 38, 38); g.FillEllipse(silhouette, 25, 70, 78, 52);
            using (Bitmap rendered = RenderCircularAvatar(bitmap)) { rendered.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }
    }

    private static byte[] NormalizeAvatar(string path)
    {
        using (Image source = Image.FromFile(path))
        using (Bitmap result = RenderCircularAvatar(source))
        using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
        {
            result.Save(ms, ImageFormat.Png); return ms.ToArray();
        }
    }

    // Render the circular crop at 4x resolution, then downsample with a high-quality
    // filter. A larger output keeps the gold circle smooth when shown at 84 px.
    private static Bitmap RenderCircularAvatar(Image source)
    {
        const int outputSize = 512;
        const int renderSize = 2048;
        float side = Math.Min(source.Width, source.Height);
        float cropX = (source.Width - side) / 2f;
        float cropY = (source.Height - side) / 2f;
        using (Bitmap large = new Bitmap(renderSize, renderSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(large))
            using (System.Drawing.Drawing2D.GraphicsPath circle = new System.Drawing.Drawing2D.GraphicsPath())
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                circle.AddEllipse(0, 0, renderSize - 1, renderSize - 1);
                g.SetClip(circle);
                g.DrawImage(source, new Rectangle(0, 0, renderSize, renderSize), cropX, cropY, side, side, GraphicsUnit.Pixel);
            }

            Bitmap result = new Bitmap(outputSize, outputSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(large, new Rectangle(0, 0, outputSize, outputSize), 0, 0, renderSize, renderSize, GraphicsUnit.Pixel);
                // Match the default avatar's gold ring (3 px at 128 px, scaled to 512 px).
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen frame = new Pen(Color.FromArgb(214, 171, 61), 12f))
                {
                    frame.Alignment = System.Drawing.Drawing2D.PenAlignment.Center;
                    g.DrawEllipse(frame, 12f, 12f, 488f, 488f);
                }
            }
            return result;
        }
    }

    private static void SetAvatarImage(PictureBox box, byte[] bytes)
    {
        if (box == null || box.IsDisposed || bytes == null || bytes.Length == 0) return;
        using (System.IO.MemoryStream ms = new System.IO.MemoryStream(bytes))
        using (Image temp = Image.FromStream(ms))
        {
            Image replacement = new Bitmap(temp); Image old = box.Image; box.Image = replacement; old?.Dispose();
        }
    }

    private static string AvatarPath(string userId)
    {
        string safe = new string((userId ?? "default").Where(c => Char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMlify", "Profiles", safe + ".avatar");
    }

    private static void SaveProfileAvatar(string userId, byte[] image)
    {
        string path = AvatarPath(userId); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        byte[] protectedImage = ProtectedData.Protect(image, Encoding.UTF8.GetBytes("BIMlify.Avatar." + userId), DataProtectionScope.CurrentUser);
        System.IO.File.WriteAllBytes(path, protectedImage);
    }

    private static bool RemoveProfileAvatar(string userId)
    {
        try { string path = AvatarPath(userId); if (System.IO.File.Exists(path)) System.IO.File.Delete(path); return !System.IO.File.Exists(path); } catch { return false; }
    }

    private static byte[] LoadProfileAvatar(string userId)
    {
        try
        {
            string path = AvatarPath(userId); if (!System.IO.File.Exists(path)) return null;
            return ProtectedData.Unprotect(System.IO.File.ReadAllBytes(path), Encoding.UTF8.GetBytes("BIMlify.Avatar." + userId), DataProtectionScope.CurrentUser);
        }
        catch { return null; }
    }

    private static void SendSignOut(string accessToken)
    {
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string url = SupabaseConfig("ProjectUrl"), key = SupabaseConfig("PublishableKey");
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url.TrimEnd('/') + "/auth/v1/logout");
            request.Method = "POST"; request.Headers["apikey"] = key; request.Headers["Authorization"] = "Bearer " + accessToken; request.Timeout = 15000;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) { }
        }
        catch { }
    }

    private static string AuthError(string json, HttpStatusCode status)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(json ?? string.Empty, "\\\"(?:msg|message|error_description)\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return "Sign-in failed (" + (int)status + "). Check your email and password.";
        string message = System.Text.RegularExpressions.Regex.Unescape(match.Groups[1].Value);
        if (message.IndexOf("invalid login credentials", StringComparison.OrdinalIgnoreCase) >= 0) return "Email or password is incorrect.";
        if (message.IndexOf("already registered", StringComparison.OrdinalIgnoreCase) >= 0) return "This email already has an account. Sign in instead.";
        if (message.IndexOf("email not confirmed", StringComparison.OrdinalIgnoreCase) >= 0) return "Confirm your email first, then sign in.";
        return message.Length > 180 ? message.Substring(0, 180) : message;
    }

    private static string EscapeJson(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

    public static void ShowToolSearchFromQualitySuite()
    {
        Form dashboard = _dashboard;
        if (dashboard != null && !dashboard.IsDisposed) ShowToolSearch(dashboard, true);
    }

    /// <summary>Invokes an existing BIMlify sidebar action for an embedded tool window.</summary>
    public static void InvokeBimlifyWindowAction(string methodName, Form owner)
    {
        if (String.IsNullOrWhiteSpace(methodName) || owner == null || owner.IsDisposed) return;
        if (String.Equals(methodName, "ShowContactOptions", StringComparison.Ordinal))
        {
            ShowContactOptionsStyled(owner);
            return;
        }
        try
        {
            Type actions = BimActionsType();
            MethodInfo method = actions?.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || method.GetParameters().Length != 1 || !method.GetParameters()[0].ParameterType.IsInstanceOfType(owner)) return;
            method.Invoke(null, new object[] { owner });
        }
        catch { }
    }

    private static void ShowContactOptionsStyled(Form owner)
    {
        if (owner == null || owner.IsDisposed) return;
        using (Form dialog = CreateSettingsStyleDialog())
        {
            Color surface = Color.FromArgb(238, 241, 245);
            Color ink = Color.FromArgb(55, 67, 79);
            dialog.Text = "BIMlify — Contact";
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.Width = 600; dialog.Height = 430; dialog.MinimumSize = new Size(560, 410);
            dialog.MaximizeBox = true; dialog.MinimizeBox = true; dialog.ShowIcon = false; dialog.ShowInTaskbar = false;
            // Keep the dialog's explicit card geometry stable at Revit's DPI scaling.
            dialog.AutoScaleMode = AutoScaleMode.None; dialog.BackColor = surface; dialog.Font = new Font("Segoe UI", 9f);

            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(24, 14, 82, 12) };
            Label title = new Label { Text = "Contact BIMlify", Left = 24, Top = 18, Width = 390, Height = 24, ForeColor = ink, BackColor = surface, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            Panel choices = new Panel { Left = 24, Top = 62, Width = 430, Height = 270, BackColor = surface };
            Panel email = new Panel { Height = 68, BackColor = Color.White, Cursor = Cursors.Hand, BorderStyle = BorderStyle.None };
            Panel linkedin = new Panel { Height = 68, BackColor = Color.White, Cursor = Cursors.Hand, BorderStyle = BorderStyle.None };
            Action<Panel> paintContactCard = card => card.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, card.ClientSize.Width - 1, card.ClientSize.Height - 1);
                int radius = 10;
                using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
                using (Pen border = new Pen(Color.FromArgb(218, 223, 229), 1f))
                {
                    path.AddArc(r.Left, r.Top, radius * 2, radius * 2, 180, 90);
                    path.AddArc(r.Right - radius * 2, r.Top, radius * 2, radius * 2, 270, 90);
                    path.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                    path.AddArc(r.Left, r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                    path.CloseFigure();
                    e.Graphics.DrawPath(border, path);
                }
            };
            paintContactCard(email); paintContactCard(linkedin);
            Action<Panel> applyContactCardRegion = card =>
            {
                if (card.ClientSize.Width < 2 || card.ClientSize.Height < 2) return;
                using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    Rectangle r = new Rectangle(0, 0, card.ClientSize.Width, card.ClientSize.Height);
                    int radius = Math.Min(10, Math.Min(r.Width, r.Height) / 2);
                    path.AddArc(r.Left, r.Top, radius * 2, radius * 2, 180, 90);
                    path.AddArc(r.Right - radius * 2, r.Top, radius * 2, radius * 2, 270, 90);
                    path.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                    path.AddArc(r.Left, r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                    path.CloseFigure();
                    Region previous = card.Region;
                    card.Region = new Region(path);
                    if (previous != null) previous.Dispose();
                }
            };
            Label emailIcon = new Label { Text = "✉", Width = 38, ForeColor = Gold, Font = new Font("Segoe UI Symbol", 15f), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Cursor = Cursors.Hand };
            Label emailTitle = new Label { Text = "Email", ForeColor = ink, Font = new Font("Segoe UI", 9f, FontStyle.Bold), BackColor = Color.Transparent, Cursor = Cursors.Hand };
            Label emailDetail = new Label { Text = "Send a message to BIMlify support", ForeColor = Color.FromArgb(105, 117, 129), Font = new Font("Segoe UI", 8f), BackColor = Color.Transparent, Cursor = Cursors.Hand };
            Label linkedinIcon = new Label { Text = "in", Width = 38, ForeColor = Gold, Font = new Font("Segoe UI", 13f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Cursor = Cursors.Hand };
            Label linkedinTitle = new Label { Text = "LinkedIn", ForeColor = ink, Font = new Font("Segoe UI", 9f, FontStyle.Bold), BackColor = Color.Transparent, Cursor = Cursors.Hand };
            Label linkedinDetail = new Label { Text = "Visit the BIMlify LinkedIn profile", ForeColor = Color.FromArgb(105, 117, 129), Font = new Font("Segoe UI", 8f), BackColor = Color.Transparent, Cursor = Cursors.Hand };
            Label emailArrow = new Label { Text = "›", Width = 24, ForeColor = Color.FromArgb(132, 140, 147), Font = new Font("Segoe UI", 16f), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Cursor = Cursors.Hand };
            Label linkedinArrow = new Label { Text = "›", Width = 24, ForeColor = Color.FromArgb(132, 140, 147), Font = new Font("Segoe UI", 16f), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Cursor = Cursors.Hand };
            Label status = new Label { Left = 12, Height = 24, ForeColor = Color.FromArgb(105, 117, 129), Font = new Font("Segoe UI", 8f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            Action layoutChoices = () =>
            {
                int contentWidth = Math.Max(280, page.ClientSize.Width - 48);
                int contentHeight = Math.Max(190, page.ClientSize.Height - choices.Top - 12);
                choices.SetBounds(24, 62, contentWidth, contentHeight);
                int rowWidth = Math.Min(520, Math.Max(250, choices.ClientSize.Width - 24));
                int left = Math.Max(12, (choices.ClientSize.Width - rowWidth) / 2);
                int top = Math.Max(12, (choices.ClientSize.Height - (68 * 2 + 12)) / 2);
                email.SetBounds(left, top, rowWidth, 68);
                linkedin.SetBounds(left, top + 80, rowWidth, 68);
                applyContactCardRegion(email); applyContactCardRegion(linkedin);
                emailIcon.SetBounds(10, 7, 40, 52); emailTitle.SetBounds(58, 12, rowWidth - 100, 21); emailDetail.SetBounds(58, 34, rowWidth - 100, 20); emailArrow.SetBounds(rowWidth - 36, 8, 24, 50);
                linkedinIcon.SetBounds(10, 7, 40, 52); linkedinTitle.SetBounds(58, 12, rowWidth - 100, 21); linkedinDetail.SetBounds(58, 34, rowWidth - 100, 20); linkedinArrow.SetBounds(rowWidth - 36, 8, 24, 50);
                status.SetBounds(left + 4, choices.ClientSize.Height - 30, rowWidth - 8, 22);
            };
            email.Controls.AddRange(new Control[] { emailIcon, emailTitle, emailDetail, emailArrow });
            linkedin.Controls.AddRange(new Control[] { linkedinIcon, linkedinTitle, linkedinDetail, linkedinArrow });
            choices.Controls.AddRange(new Control[] { email, linkedin, status });
            choices.Resize += (s, e) => layoutChoices();
            page.Resize += (s, e) => layoutChoices(); layoutChoices();
            Action<Control, EventHandler> wireClick = null;
            wireClick = (control, handler) => { control.Click += handler; foreach (Control child in control.Controls) wireClick(child, handler); };
            Action<string> open = url =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { status.Text = "Could not open the link: " + ex.Message; }
            };
            wireClick(email, (s, e) => open("mailto:Ealjohari@yahoo.com"));
            wireClick(linkedin, (s, e) => open("https://www.linkedin.com/in/esra-a-e-al-johari-msc-434056a5/"));

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(16, 12, 16, 8) };
            Button close = CreateSettingsStyleButton("Close", DialogResult.Cancel); close.Width = 96; close.Height = 30; close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.Top = 3; close.Left = Math.Max(0, footer.ClientSize.Width - close.Width - CompactFooterInset);
            footer.Resize += (s, e) => close.Left = Math.Max(0, footer.ClientSize.Width - close.Width - CompactFooterInset);
            footer.Controls.Add(close);

            page.Controls.Add(choices); page.Controls.Add(title); title.BringToFront(); AddSettingsStyleMark(page);
            dialog.Controls.Add(page); dialog.Controls.Add(footer);
            AddSettingsStyleTitleBar(dialog); ApplySettingsStyleWindowCorners(dialog);
            dialog.CancelButton = close;
            dialog.ShowDialog(owner);
        }
    }

    private static void ShowToolSearch(Form owner, bool fromQualitySuite)
    {
        if (owner == null || owner.IsDisposed) return;
        using (Form dialog = CreateSettingsStyleDialog())
        {
            Color surface = Color.FromArgb(238, 241, 245);
            Color ink = Color.FromArgb(55, 67, 79);
            dialog.Text = "BIMlify — Search Tools";
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.Width = 600; dialog.Height = 430; dialog.MinimumSize = new Size(560, 410);
            dialog.MaximizeBox = true; dialog.MinimizeBox = true; dialog.ShowIcon = false; dialog.ShowInTaskbar = false;
            dialog.AutoScaleMode = AutoScaleMode.Dpi; dialog.BackColor = surface; dialog.Font = new Font("Segoe UI", 9f);

            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(24, 14, 82, 12) };
            Label title = new Label { Text = "SEARCH TOOLS", Left = 24, Top = 18, Width = 390, Height = 22, Font = new Font("Segoe UI", 9.2f, FontStyle.Bold), ForeColor = ink, BackColor = surface };
            TextBox query = new TextBox { Left = 24, Top = 50, Width = 420, Height = 27, Font = new Font("Segoe UI", 9f) };
            BIMlify.SearchControls.SearchGlyph.Attach(query, "Search BIMlify tools");
            ListBox results = new ListBox { Left = 24, Top = 88, Width = 420, Height = 258, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = ink, Font = new Font("Segoe UI", 8.5f), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 25 };
            results.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush fill = new SolidBrush(selected ? Color.FromArgb(255, 247, 226) : Color.White)) e.Graphics.FillRectangle(fill, e.Bounds);
                if (selected) using (Pen edge = new Pen(Gold, 2f)) e.Graphics.DrawLine(edge, e.Bounds.Left + 1, e.Bounds.Top + 2, e.Bounds.Left + 1, e.Bounds.Bottom - 2);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(results.Items[e.Index]), results.Font, Rectangle.Inflate(e.Bounds, -9, 0), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(12, 12, 16, 8) };
            footer.Paint += (s, e) => { using (Pen accent = new Pen(Gold, 1.35f)) e.Graphics.DrawLine(accent, 0, footer.Height - 2, footer.Width, footer.Height - 2); };
            Panel actions = new Panel { Dock = DockStyle.Right, Width = 208, Height = 38, BackColor = footer.BackColor };
            Button open = CreateSettingsStyleButton("Open", DialogResult.OK);
            Button cancel = CreateSettingsStyleButton("Close", DialogResult.Cancel);
            ApplyRoundedButtonCorners(open, 6);
            ApplyRoundedButtonCorners(cancel, 6);
            open.Left = 0; cancel.Left = 112;
            open.Top = cancel.Top = 3; open.Width = cancel.Width = 96; open.Height = cancel.Height = 30;
            actions.Controls.Add(open); actions.Controls.Add(cancel); footer.Controls.Add(actions);
            page.Controls.Add(results); page.Controls.Add(query); page.Controls.Add(title);
            Action layoutSearch = () =>
            {
                int safeWidth = Math.Max(240, page.ClientSize.Width - 120);
                title.Width = safeWidth;
                query.Width = safeWidth;
                results.Width = Math.Max(280, page.ClientSize.Width - 48);
                results.Height = Math.Max(120, page.ClientSize.Height - 100);
            };
            page.SizeChanged += (s, e) => layoutSearch();
            layoutSearch();
            AddSettingsStyleMark(page);
            dialog.Controls.Add(page); dialog.Controls.Add(footer);
            AddSettingsStyleTitleBar(dialog);
            ApplySettingsStyleWindowCorners(dialog);
            dialog.AcceptButton = open; dialog.CancelButton = cancel;
            SearchToolItem[] all = BuildSearchItems();
            Action filter = () =>
            {
                string text = (query.Text ?? "").Trim();
                results.BeginUpdate(); results.Items.Clear();
                foreach (SearchToolItem item in all)
                    if (text.Length == 0 || item.Name.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        results.Items.Add(item);
                if (results.Items.Count > 0) results.SelectedIndex = 0;
                open.Enabled = results.Items.Count > 0;
                results.EndUpdate();
            };
            query.TextChanged += (s, e) => filter();
            query.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down && results.Items.Count > 0) { results.Focus(); results.SelectedIndex = 0; e.Handled = true; } };
            results.DoubleClick += (s, e) => { if (results.SelectedItem != null) { dialog.DialogResult = DialogResult.OK; dialog.Close(); } };
            filter(); query.Focus();
            if (dialog.ShowDialog(owner) != DialogResult.OK || results.SelectedItem == null) return;
            LaunchSearchItem(owner, results.SelectedItem as SearchToolItem, fromQualitySuite);
        }
    }

    private static Type BimActionsType()
        => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("BIMlify.BimActions", false)).FirstOrDefault(t => t != null);

    internal static Form CreateSettingsStyleDialog()
    {
        try
        {
            Type actions = BimActionsType();
            Type formType = actions?.GetNestedType("BIMlifyForm", BindingFlags.NonPublic);
            if (formType != null)
            {
                Form styled = Activator.CreateInstance(formType, true) as Form ?? new Form();
                styled.StartPosition = FormStartPosition.CenterScreen;
                return styled;
            }
        }
        catch { }
        return new Form { FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen };
    }

    private const int CompactFooterInset = 28;

    public static void AttachPatternOutline(Form form)
    {
        Label heading = Descendants(form).OfType<Label>().FirstOrDefault(label => label.Text == "Preview");
        Panel canvas = heading?.Parent?.Controls.OfType<Panel>().Where(panel => panel.Height > 100)
            .OrderByDescending(panel => panel.Height).FirstOrDefault();
        if (canvas == null) return;
        canvas.Paint += (sender, args) =>
        {
            if (canvas.ClientSize.Width < 2 || canvas.ClientSize.Height < 2) return;
            var state = args.Graphics.Save();
            try
            {
                args.Graphics.ResetTransform();
                using (var border = new Pen(Color.FromArgb(210, 215, 220)))
                    args.Graphics.DrawRectangle(border, 0, 0, canvas.ClientSize.Width - 1, canvas.ClientSize.Height - 1);
            }
            finally { args.Graphics.Restore(state); }
        };
        canvas.Resize += (sender, args) => canvas.Invalidate();
    }

    public static void AttachTemplateSearch(Form form)
    {
        AttachCompatibleViewFilter(form);
        SeparateSelectedTemplateName(form);
        var heading = Descendants(form).OfType<Label>().FirstOrDefault(label => label.Text == "1. CHOOSE TEMPLATE");
        var parent = heading?.Parent;
        var tabs = parent?.Controls.OfType<TabControl>().FirstOrDefault();
        if (tabs == null) return;
        var lists = tabs.TabPages.Cast<TabPage>().SelectMany(page => page.Controls.OfType<ListBox>()).ToArray();
        if (lists.Length == 0) return;
        var items = lists.ToDictionary(list => list, list => list.Items.Cast<object>().ToList());
        Type actions = BimActionsType();
        var searchType = actions?.GetNestedType("AdvancedSearchTextBox", BindingFlags.NonPublic);
        var search = searchType == null ? new TextBox() : (TextBox)Activator.CreateInstance(searchType, nonPublic: true);
        search.Dock = DockStyle.Top;
        search.Height = 30;
        search.Font = lists[0].Font;
        search.AccessibleName = "Search template names";
        actions?.GetMethod("StyleSearchBox", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null,
            new object[] { search, Color.White, lists[0].ForeColor, "Search template names" });
        var types = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Standard, BackColor = Color.White, Font = new Font("Segoe UI", 8f), AccessibleName = "Template view type" };
        types.Items.AddRange(tabs.TabPages.Cast<TabPage>().Select(page => (object)page.Text).ToArray());
        types.SelectedIndex = tabs.SelectedIndex;
        types.SelectionChangeCommitted += (sender, args) => tabs.SelectedIndex = types.SelectedIndex;
        tabs.SelectedIndexChanged += (sender, args) => types.SelectedIndex = tabs.SelectedIndex;
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        int tabsIndex = parent.Controls.GetChildIndex(tabs);
        parent.Controls.Add(listHost);
        parent.Controls.SetChildIndex(listHost, tabsIndex);
        listHost.Controls.Add(tabs);
        tabs.Dock = DockStyle.None;
        tabs.TabStop = false;
        void LayoutTemplateList()
        {
            int headerHeight = tabs.DisplayRectangle.Top;
            tabs.SetBounds(0, -headerHeight, listHost.ClientSize.Width, listHost.ClientSize.Height + headerHeight);
        }
        listHost.Resize += (sender, args) => LayoutTemplateList();
        tabs.HandleCreated += (sender, args) => LayoutTemplateList();
        form.Shown += (sender, args) => LayoutTemplateList();
        LayoutTemplateList();

        var filters = new TableLayoutPanel { Dock = DockStyle.Top, Height = 94, ColumnCount = 1, RowCount = 3 };
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var help = new Label { Text = "Choose a view type, then find a template by name.",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0),
            Font = new Font("Segoe UI", 8f), ForeColor = Color.FromArgb(102, 109, 118) };
        types.Dock = search.Dock = DockStyle.Fill;
        types.Margin = new Padding(0, 2, 0, 4);
        search.Margin = new Padding(0, 3, 0, 3);
        filters.Controls.Add(help, 0, 0);
        filters.Controls.Add(types, 0, 1);
        filters.Controls.Add(search, 0, 2);
        parent.Controls.Add(filters);
        parent.Controls.SetChildIndex(filters, parent.Controls.GetChildIndex(heading));
        Action filter = () =>
        {
            string query = search.Text.Trim();
            foreach (ListBox list in lists)
            {
                foreach (object item in list.Items)
                    if (!items[list].Contains(item)) items[list].Add(item);
                object selected = list.SelectedItem;
                list.BeginUpdate();
                try
                {
                    list.Items.Clear();
                    list.Items.AddRange(items[list].Where(item =>
                        list.GetItemText(item).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToArray());
                    if (selected != null && list.Items.Contains(selected)) list.SelectedItem = selected;
                }
                finally { list.EndUpdate(); }
            }
        };
        search.TextChanged += (sender, args) => filter();
        AttachViewTemplateAudit(form, heading.Parent?.Parent as TableLayoutPanel);
        var import = Descendants(form).OfType<Button>().FirstOrDefault(button => button.Text.StartsWith("Import from", StringComparison.OrdinalIgnoreCase));
        if (import != null) import.Click += (sender, args) => { if (form.IsHandleCreated) form.BeginInvoke(filter); };
    }

    private static void AttachViewTemplateAudit(Form form, TableLayoutPanel body)
    {
        var doc = _application?.ActiveUIDocument?.Document;
        if (body?.Parent == null || doc == null) return;
        var parent = body.Parent;
        int index = parent.Controls.GetChildIndex(body);
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f) };
        var assignment = new TabPage("Apply Templates") { BackColor = Color.White, Padding = new Padding(8) };
        var audit = new TabPage("Template Audit") { BackColor = Color.White, Padding = new Padding(16, 12, 16, 12) };
        tabs.TabPages.Add(assignment);
        tabs.TabPages.Add(audit);
        parent.Controls.Remove(body);
        assignment.Controls.Add(body);
        var tabHost = new Panel
        {
            Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 8),
            BackColor = Color.FromArgb(238, 241, 245)
        };
        tabHost.Controls.Add(tabs);
        parent.Controls.Add(tabHost);
        parent.Controls.SetChildIndex(tabHost, index);
        var assignmentActions = Descendants(form).OfType<Button>().Where(button =>
            button.Text == "Select All" || button.Text == "Clear" || button.Text == "Apply Template"
            || button.Text.StartsWith("Import from", StringComparison.OrdinalIgnoreCase)).ToArray();
        tabs.SelectedIndexChanged += (sender, args) =>
        {
            foreach (var button in assignmentActions) button.Visible = tabs.SelectedTab == assignment;
        };

        var templates = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, DisplayMember = "Name" };
        templates.Items.Add("Find all unassigned views");
        var allViews = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.View)).Cast<Autodesk.Revit.DB.View>().ToList();
        templates.Items.AddRange(allViews.Where(view => view.IsTemplate).OrderBy(view => view.Name).Cast<object>().ToArray());
        var refresh = CreateSettingsStyleButton("Refresh Audit", DialogResult.None);
        refresh.Width = 116;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false };
        refresh.Name = "BIMlifyConfirmContinue";
        var export = CreateSettingsStyleButton("Export CSV", DialogResult.None);
        bar.Controls.AddRange(new Control[] { new Label { Text = "Expected template", AutoSize = true, Margin = new Padding(0, 9, 8, 0) }, templates });
        var close = Descendants(form).OfType<Button>().FirstOrDefault(button => button.Text == "Close");
        var actions = new[] { refresh, export };
        var status = new Label { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8f), ForeColor = Color.FromArgb(105, 117, 129) };
        if (close?.Parent != null)
        {
            var footer = close.Parent;
            var footerStatus = footer.Parent?.Controls.OfType<Label>().FirstOrDefault(label => label.Dock == DockStyle.Left);
            if (footerStatus != null)
            {
                footerStatus.Font = new Font("Segoe UI", 8f);
                footerStatus.ForeColor = Color.FromArgb(105, 117, 129);
                footerStatus.TextAlign = ContentAlignment.MiddleLeft;
                status.Dock = DockStyle.Left;
                status.Width = footerStatus.Width;
                status.AutoEllipsis = true;
                footerStatus.Parent.Controls.Add(status);
            }
            foreach (var button in actions)
            {
                button.Visible = false;
                button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                footer.Controls.Add(button);
                button.BringToFront();
            }
            var applyActions = assignmentActions.Where(button => button.Parent == footer)
                .OrderByDescending(button => button.Left).ToArray();
            void LayoutTemplateFooter()
            {
                bool auditing = tabs.SelectedTab == audit;
                if (footerStatus != null)
                {
                    var padding = footerStatus.Parent.Padding;
                    var centered = new Padding(0, Math.Max(0, padding.Bottom - padding.Top),
                        0, Math.Max(0, padding.Top - padding.Bottom));
                    footerStatus.Padding = status.Padding = centered;
                    footerStatus.Visible = !auditing;
                    status.Visible = auditing;
                }
                foreach (var button in actions) button.Visible = auditing;

                int right = footer.ClientSize.Width;
                if (footer.Parent != null)
                    right = Math.Min(right, footer.Parent.ClientSize.Width - footer.Left - FooterCloseOrder.RightInset);
                close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                close.Left = Math.Max(close.Left, right - close.Width);
                right = close.Left;
                foreach (var button in auditing ? actions : applyActions)
                {
                    button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    button.SetBounds(right - button.Width - FooterCloseOrder.ButtonGap, close.Top, button.Width, close.Height);
                    right = button.Left;
                }
            }
            tabs.SelectedIndexChanged += (sender, args) => LayoutTemplateFooter();
            footer.Resize += (sender, args) => LayoutTemplateFooter();
            form.Shown += (sender, args) =>
            {
                LayoutTemplateFooter();
                form.BeginInvoke(new Action(LayoutTemplateFooter));
            };
        }
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            RowHeadersVisible = false, BackgroundColor = Color.White, EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        ReportGridStyle.Apply(grid);
        grid.BorderStyle = BorderStyle.None;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        foreach (string name in new[] { "View", "Type", "Current template", "Finding" }) grid.Columns.Add(name, name);
        audit.Controls.Add(grid);
        if (status.Parent == null) audit.Controls.Add(status);
        audit.Controls.Add(bar);

        void Reload()
        {
            var expected = templates.SelectedItem as Autodesk.Revit.DB.View;
            grid.Rows.Clear();
            foreach (var view in new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.View)).Cast<Autodesk.Revit.DB.View>()
                .Where(view => !view.IsTemplate && !(view is ViewSheet) && view.CanBePrinted).OrderBy(view => view.Name))
            {
                if (expected != null && (!view.IsValidViewTemplate(expected.Id) || view.ViewTemplateId == expected.Id)) continue;
                bool missing = view.ViewTemplateId == ElementId.InvalidElementId;
                if (expected == null && !missing) continue;
                var row = grid.Rows[grid.Rows.Add(view.Name, view.ViewType.ToString(), doc.GetElement(view.ViewTemplateId)?.Name ?? "None",
                    missing ? "No template" : "Different template")];
                row.Tag = view.Id;
            }
            status.Text = grid.Rows.Count + " views match this audit.";
        }
        templates.SelectedIndexChanged += (sender, args) => Reload();
        refresh.Click += (sender, args) => Reload();
        export.Click += (sender, args) =>
        {
            using (var picker = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "Template Audit.csv" })
            {
                if (picker.ShowDialog(form) != DialogResult.OK) return;
                string Quote(object value) => "\"" + Convert.ToString(value).Replace("\"", "\"\"") + "\"";
                try
                {
                    var headers = grid.Columns.Cast<DataGridViewColumn>().Select(column => Quote(column.HeaderText));
                    var rows = grid.Rows.Cast<DataGridViewRow>().Select(row =>
                        String.Join(",", row.Cells.Cast<DataGridViewCell>().Select(cell => Quote(cell.Value))));
                    System.IO.File.WriteAllLines(picker.FileName, new[] { String.Join(",", headers) }.Concat(rows), new System.Text.UTF8Encoding(true));
                    status.Text = "Exported " + grid.Rows.Count + " views.";
                }
                catch (Exception error) { status.Text = error.Message; }
            }
        };
        templates.SelectedIndex = 0;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, ComboBox> compatibleViewFilters =
        new System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, ComboBox>();

    private static void SeparateSelectedTemplateName(Form form)
    {
        const string titleText = "2. SELECT VIEWS";
        const string prefix = titleText + " · ";
        var heading = Descendants(form).OfType<Label>().FirstOrDefault(label =>
            label.Text == titleText || label.Text.StartsWith(prefix, StringComparison.Ordinal));
        var card = heading?.Parent;
        if (card == null) return;

        var row = new Panel { Dock = DockStyle.Top, Height = heading.Height };
        var title = new Label { Text = titleText, Dock = DockStyle.Left, Font = heading.Font,
            ForeColor = heading.ForeColor, TextAlign = heading.TextAlign, UseMnemonic = false };
        title.Width = title.PreferredWidth + 6;
        var name = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false,
            Font = new Font(heading.Font.FontFamily, 8f), ForeColor = Color.FromArgb(102, 109, 118),
            TextAlign = ContentAlignment.MiddleLeft };
        var tooltip = new ToolTip();
        void UpdateName()
        {
            name.Text = heading.Text.StartsWith(prefix, StringComparison.Ordinal)
                ? heading.Text.Substring(prefix.Length) : string.Empty;
            tooltip.SetToolTip(name, name.Text);
        }

        int index = card.Controls.GetChildIndex(heading);
        heading.Hide();
        row.Controls.Add(name);
        row.Controls.Add(title);
        card.Controls.Add(row);
        card.Controls.SetChildIndex(row, index);
        heading.TextChanged += (sender, args) => UpdateName();
        row.Disposed += (sender, args) => tooltip.Dispose();
        UpdateName();
    }

    private static void AttachCompatibleViewFilter(Form form)
    {
        var viewList = Descendants(form).OfType<CheckedListBox>().FirstOrDefault();
        var layout = viewList?.Parent as TableLayoutPanel;
        var search = layout?.Controls.OfType<TextBox>().FirstOrDefault();
        if (search != null)
        {
            var types = new ComboBox
            {
                AccessibleName = "Filter compatible views by type",
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Standard,
                BackColor = Color.White,
                ForeColor = viewList.ForeColor,
                Font = new Font("Segoe UI", 8f)
            };
            compatibleViewFilters.Add(search, types);
            layout.SuspendLayout();
            layout.RowCount = 4;
            layout.SetRow(viewList, 3);
            layout.SetRow(search, 2);
            layout.RowStyles.Insert(1, new RowStyle(SizeType.Absolute, 34f));
            types.Margin = new Padding(0, 2, 0, 4);
            search.Margin = new Padding(0, 3, 0, 3);
            layout.Controls.Add(types, 0, 1);
            layout.ResumeLayout();
            var notifySearch = typeof(TextBox).GetMethod("OnTextChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            types.SelectionChangeCommitted += (sender, args) => notifySearch.Invoke(search, new object[] { EventArgs.Empty });
            form.Shown += (sender, args) => notifySearch.Invoke(search, new object[] { EventArgs.Empty });
        }

        var buttons = Descendants(form).OfType<Button>().ToArray();
        var close = buttons.FirstOrDefault(button => button.Text == "Close");
        var apply = buttons.FirstOrDefault(button => button.Text == "Apply Template");
        if (close != null && apply != null && close.Parent == apply.Parent && close.Left < apply.Left)
        {
            int right = apply.Right;
            apply.Left = close.Left;
            close.Left = right - close.Width;
            int closeTabIndex = close.TabIndex;
            close.TabIndex = apply.TabIndex;
            apply.TabIndex = closeTabIndex;
        }
    }

    public static void RefreshCompatibleViewTypes(TextBox search, Autodesk.Revit.DB.View template,
        IEnumerable<Autodesk.Revit.DB.View> views)
    {
        if (!compatibleViewFilters.TryGetValue(search, out var types)) return;
        const string allTypes = "All compatible view types";
        var names = template == null ? Array.Empty<string>() : views
            .Where(view => view.ViewType == template.ViewType)
            .GroupBy(view => view.GetTypeId())
            .Select(group => CompatibleViewTypeName(group.First()))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (types.Items.Cast<string>().Skip(1).SequenceEqual(names) && types.Items.Count > 0) return;

        string selected = types.SelectedItem as string;
        types.BeginUpdate();
        try
        {
            types.Items.Clear();
            types.Items.Add(allTypes);
            types.Items.AddRange(names);
            int selectedIndex = selected == null ? -1 : types.Items.IndexOf(selected);
            types.SelectedIndex = Math.Max(0, selectedIndex);
            types.Enabled = names.Length > 1;
        }
        finally { types.EndUpdate(); }
    }

    public static bool MatchesCompatibleViewType(bool matchesSearch, Autodesk.Revit.DB.View view, TextBox search)
    {
        return matchesSearch && (!compatibleViewFilters.TryGetValue(search, out var types)
            || types.SelectedIndex <= 0
            || string.Equals((string)types.SelectedItem, CompatibleViewTypeName(view), StringComparison.CurrentCultureIgnoreCase));
    }

    private static string CompatibleViewTypeName(Autodesk.Revit.DB.View view)
    {
        return view.Document.GetElement(view.GetTypeId())?.Name ?? view.ViewType.ToString();
    }

    private static FormWindowState workspaceWindowState = FormWindowState.Normal;

    private static readonly Font DropdownFont = new Font("Segoe UI", 8f);
    private static readonly Color DropdownText = Color.FromArgb(96, 106, 116);
    private static readonly Color DropdownFill = Color.FromArgb(255, 245, 215);

    public static void ApplyLightGoldDropdowns(Control root)
    {
        foreach (Button button in Descendants(root).OfType<Button>())
            SelectionButtonColors.ApplyFlatButton(button);
        foreach (ComboBox picker in Descendants(root).OfType<ComboBox>())
        {
            if (picker.DropDownStyle == ComboBoxStyle.Simple) continue;
            picker.Font = DropdownFont;
            picker.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : DropdownText;
            picker.BackColor = SystemInformation.HighContrast ? SystemColors.Window : DropdownFill;
            if (picker.FlatStyle == FlatStyle.Standard) picker.FlatStyle = FlatStyle.Flat;
            if (picker.DropDownStyle != ComboBoxStyle.DropDownList || picker.DrawMode != DrawMode.Normal) continue;
            picker.DrawMode = DrawMode.OwnerDrawFixed;
            picker.DrawItem += DrawDropdownField;
        }
    }

    private static void DrawDropdownField(object sender, DrawItemEventArgs e)
    {
        var picker = (ComboBox)sender;
        bool field = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool goldField = field && picker.Enabled && !SystemInformation.HighContrast;
        Color background = goldField ? DropdownFill : selected ? SystemColors.Highlight : SystemColors.Window;
        Color foreground = !picker.Enabled ? SystemColors.GrayText :
            goldField ? picker.ForeColor : selected ? SystemColors.HighlightText : picker.ForeColor;
        using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
        string value = e.Index >= 0 && e.Index < picker.Items.Count
            ? picker.GetItemText(picker.Items[e.Index]) : picker.Text;
        TextRenderer.DrawText(e.Graphics, value, picker.Font, e.Bounds, foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }

    public static void TrackWorkspaceWindowState(Form form)
    {
        form.Shown += (sender, args) => form.BeginInvoke(new Action(() => ApplyLightGoldDropdowns(form)));
        bool initialized = false;
        form.Shown += (sender, args) =>
        {
            if (!form.MaximizeBox || form.Width < 850 || form.Height < 550) return;
            form.WindowState = workspaceWindowState;
            initialized = true;
        };
        form.Resize += (sender, args) =>
        {
            if (initialized && form.Visible && form.WindowState != FormWindowState.Minimized)
                workspaceWindowState = form.WindowState;
        };
        form.VisibleChanged += (sender, args) =>
        {
            if (initialized && form.Visible && form.WindowState != FormWindowState.Minimized)
                form.WindowState = workspaceWindowState;
        };
    }

    public static void AlignCompactFooter(Form dialog)
    {
        if (dialog == null || dialog.Width != 600 || dialog.Height != 430) return;
        Panel footer = dialog.Controls.OfType<Panel>().FirstOrDefault(panel => panel.Dock == DockStyle.Bottom);
        Panel actions = footer?.Controls.OfType<Panel>().FirstOrDefault(panel => panel.Dock == DockStyle.Right);
        if (actions == null) return;
        Button rightmost = actions.Controls.OfType<Button>().OrderByDescending(button => button.Right).FirstOrDefault();
        if (rightmost == null) return;
        int paddingRight = CompactFooterInset - (actions.Width - rightmost.Right);
        if (paddingRight < 0)
        {
            actions.Width = rightmost.Right + CompactFooterInset;
            paddingRight = 0;
        }
        Padding padding = footer.Padding;
        Panel leftActions = footer.Controls.OfType<Panel>().FirstOrDefault(panel => panel.Dock == DockStyle.Left);
        Button leftmost = leftActions?.Controls.OfType<Button>().OrderBy(button => button.Left).FirstOrDefault();
        int paddingLeft = leftmost == null ? padding.Left : Math.Max(0, CompactFooterInset - leftmost.Left);
        footer.Padding = new Padding(paddingLeft, padding.Top, paddingRight, padding.Bottom);
    }

    internal static void AddSettingsStyleTitleBar(Form dialog)
    {
        dialog.Shown += (sender, args) => dialog.BeginInvoke(new Action(() => ApplyLightGoldDropdowns(dialog)));
        AlignCompactFooter(dialog);
        try
        {
            Type actions = BimActionsType();
            MethodInfo addTitleBar = actions?.GetMethod("AddWindowTitleBar", BindingFlags.Static | BindingFlags.NonPublic);
            if (addTitleBar != null && addTitleBar.GetParameters().Length == 1 && addTitleBar.GetParameters()[0].ParameterType.IsInstanceOfType(dialog))
                addTitleBar.Invoke(null, new object[] { dialog });
        }
        catch { }
    }

    internal static void AddSettingsStyleMark(Control page)
    {
        try
        {
            Type actions = BimActionsType();
            MethodInfo createMark = actions?.GetMethod("CreateSmallMark", BindingFlags.Static | BindingFlags.NonPublic);
            if (createMark == null) return;
            Control mark = createMark.Invoke(null, new object[] { page }) as Control;
            if (mark != null) { page.Controls.Add(mark); mark.BringToFront(); }
        }
        catch { }
    }

    internal static Button CreateSettingsStyleButton(string text, DialogResult result)
    {
        Button button = null;
        try
        {
            Type actions = BimActionsType();
            Type buttonType = actions?.GetNestedType("GlassButton", BindingFlags.NonPublic);
            if (buttonType != null) button = Activator.CreateInstance(buttonType, true) as Button;
        }
        catch { }
        if (button == null)
        {
            button = new Button { FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(216, 221, 227), ForeColor = Color.FromArgb(78, 85, 92), Font = new Font("Segoe UI", 8f) };
            button.FlatAppearance.BorderSize = 0;
        }
        button.Text = text; button.DialogResult = result; button.Width = 96; button.Height = 30;
        bool closeAction = string.Equals((text ?? string.Empty).Trim(), "Close", StringComparison.OrdinalIgnoreCase);
        if (closeAction)
        {
            try { button.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(button, true, null); } catch { }
            if (button.GetType().Name != "GlassButton")
            {
                button.FlatStyle = FlatStyle.Flat; button.UseVisualStyleBackColor = false;
                button.BackColor = Color.FromArgb(250, 230, 168); button.ForeColor = Color.FromArgb(55, 67, 79);
                button.FlatAppearance.BorderSize = 1; button.FlatAppearance.BorderColor = Color.FromArgb(198, 151, 42);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 239, 190);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(250, 230, 168);
            }
        }
        if (button.Text == "Clear Selection") SelectionButtonColors.ApplyFlatButton(button);
        return button;
    }

    internal static Button CreateSharedStyleButton(string text, bool primary)
    {
        Button button = CreateSettingsStyleButton(text, DialogResult.None);
        bool closeAction = string.Equals((text ?? string.Empty).Trim(), "Close", StringComparison.OrdinalIgnoreCase);
        try { button.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(button, primary || closeAction, null); } catch { }
        return button;
    }

    private static void ApplyPrimaryAccountButtonStyle(Button button)
    {
        if (button == null) return;
        try
        {
            PropertyInfo primary = button.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (primary != null && primary.CanWrite) primary.SetValue(button, true, null);
        }
        catch { }
        // Keep one flat WinForms button and reapply the gold fill after account/session state updates.
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = Color.FromArgb(250, 230, 168);
        button.ForeColor = Color.FromArgb(55, 67, 79);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(198, 151, 42);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 239, 190);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(250, 230, 168);
        button.Invalidate();
    }

    private static void ApplySecondaryAccountButtonStyle(Button button)
    {
        if (button == null) return;
        try
        {
            PropertyInfo primary = button.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (primary != null && primary.CanWrite) primary.SetValue(button, false, null);
        }
        catch { }
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = Color.FromArgb(216, 221, 227);
        button.ForeColor = Color.FromArgb(78, 85, 92);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(176, 184, 192);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 233, 237);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(216, 221, 227);
        button.Invalidate();
    }

    internal static void ApplySettingsStyleWindowCorners(Form dialog)
    {
        try
        {
            Type actions = BimActionsType();
            MethodInfo round = actions?.GetMethod("UseSmoothCorners", BindingFlags.Static | BindingFlags.NonPublic);
            if (round != null && round.GetParameters().Length == 1 && round.GetParameters()[0].ParameterType.IsInstanceOfType(dialog))
                round.Invoke(null, new object[] { dialog });
        }
        catch { }
    }

    internal static DialogResult ShowSettingsStyleMessage(IWin32Window owner, string title, string message)
    {
        using (Form dialog = CreateSettingsStyleDialog())
        {
            Color surface = Color.FromArgb(238, 241, 245);
            dialog.Text = title ?? "BIMlify";
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.Size = new Size(600, 430);
            dialog.MinimumSize = new Size(560, 410);
            dialog.MaximizeBox = true; dialog.MinimizeBox = true;
            dialog.ShowIcon = false; dialog.ShowInTaskbar = false;
            dialog.BackColor = surface; dialog.Font = new Font("Segoe UI", 9f);

            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(24, 18, 82, 12) };
            Label body = new Label { Text = message ?? "", Left = 24, Top = 24, Width = 450, Height = 90, ForeColor = Color.FromArgb(55, 67, 79), BackColor = surface, Font = new Font("Segoe UI", 9f), AutoEllipsis = true };
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(12, 12, 16, 8) };
            Panel actions = new Panel { Dock = DockStyle.Right, Width = 96, BackColor = footer.BackColor };
            Button close = CreateSettingsStyleButton("Close", DialogResult.OK);
            close.SetBounds(0, 3, 96, 30); actions.Controls.Add(close); footer.Controls.Add(actions);
            page.Controls.Add(body); AddSettingsStyleMark(page);
            dialog.Controls.Add(page); dialog.Controls.Add(footer);
            AddSettingsStyleTitleBar(dialog); ApplySettingsStyleWindowCorners(dialog);
            dialog.AcceptButton = close; dialog.CancelButton = close;
            return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        }
    }

    internal static DialogResult ShowCompactSettingsStyleMessage(IWin32Window owner, string title, string message)
    {
        using (Form dialog = CreateSettingsStyleDialog())
        {
            Color surface = Color.FromArgb(238, 241, 245);
            dialog.Text = title ?? "BIMlify";
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.Size = new Size(430, 176);
            dialog.MinimumSize = new Size(430, 176);
            dialog.MaximizeBox = false; dialog.MinimizeBox = false;
            dialog.ShowIcon = false; dialog.ShowInTaskbar = false;
            dialog.BackColor = surface; dialog.Font = new Font("Segoe UI", 9f);
            dialog.AutoScaleMode = AutoScaleMode.None;

            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(0) };
            Label body = new Label { Text = message ?? "", Left = 20, Top = 18, Width = 330, Height = 54, ForeColor = Color.FromArgb(55, 67, 79), BackColor = surface, Font = new Font("Segoe UI", 9f), AutoEllipsis = true };
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.FromArgb(216, 221, 227), Padding = new Padding(10, 8, 12, 8) };
            Panel actions = new Panel { Dock = DockStyle.Right, Width = 84, BackColor = footer.BackColor };
            Button close = CreateSettingsStyleButton("Close", DialogResult.OK);
            close.SetBounds(0, 2, 84, 28); actions.Controls.Add(close); footer.Controls.Add(actions);
            page.Controls.Add(body); AddSettingsStyleMark(page);
            dialog.Controls.Add(page); dialog.Controls.Add(footer);
            AddSettingsStyleTitleBar(dialog); ApplySettingsStyleWindowCorners(dialog);
            dialog.AcceptButton = close; dialog.CancelButton = close;
            return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        }
    }

    private static void ApplyRoundedButtonCorners(Button button, int radius)
    {
        if (button == null) return;
        Action update = () =>
        {
            if (button.IsDisposed || button.Width < 4 || button.Height < 4) return;
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            Rectangle bounds = new Rectangle(0, 0, button.Width, button.Height);
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            Region previous = button.Region;
            button.Region = new Region(path);
            path.Dispose();
            if (previous != null) previous.Dispose();
        };
        button.SizeChanged += (s, e) => update();
        button.HandleCreated += (s, e) => update();
        update();
    }

    private static SearchToolItem[] BuildSearchItems()
    {
        return new[] {
            Item("Clash Detection", "clashdetection"), Item("Review & Clean", "cleanup"), Item("Smart Cleaner", "smartcleaner"), Item("Smart Openings", "smartopenings"),
            Item("3D Room", "wet3d"), Item("BIM Library", "bimlibrary"), Item("Level Bubbles", "bubbles"), Item("Pattern Maker", "patternmaker"), Item("Plan View Range", "scaletags"), Item("Smart Sheets", "sheetorganization"), Item("Smart Templates", "viewtemplates"), Item("Select / Isolate", "categorytools"), Item("Select By ID", "selectbyid"),
            Qa("Smart QA/QC"), Qa("Workset Checker"), Qa("Sheet Audit"), Qa("Revision Manager"), Qa("Parameter Audit"), Qa("Naming Convention Checker"), Qa("Model Health Score"), Qa("Room Data Checker"), Qa("Door & Window Audit"), Qa("Finishes Checker"), Qa("Level & Grid Audit"), Qa("Opening Checker"), Qa("LOD & Deliverables Checker"),
            Item("Smart Export", "smart-tool:PDF Batch Export"), Item("Smart Log", "smart-tool:Model Change Log"), Item("Smart Map", "smart-tool:Parameter Mapping"), Item("Excel ↔ Revit Data Checker", "smart-tool:Excel ↔ Revit Data Checker"), Item("Smart Planner", "smart-tool:BIM Task Planner"), Item("Smart Links", "smart-tool:Smart Links")
        };
    }
    private static SearchToolItem Item(string name, string action) => new SearchToolItem { Name = name, Action = action };
    private static SearchToolItem Qa(string name) => new SearchToolItem { Name = name, IsQa = true };

    private static void LaunchSearchItem(Form owner, SearchToolItem item, bool fromQualitySuite)
    {
        if (item == null) return;
        Form dashboard = _dashboard;
        if (!String.IsNullOrEmpty(item.Action) && item.Action.StartsWith("smart-tool:", StringComparison.Ordinal))
        {
            BimQualitySuite.OpenStandaloneTool(item.Action.Substring("smart-tool:".Length));
            return;
        }
        if (item.IsQa)
        {
            if (dashboard == null || dashboard.IsDisposed) return;
            BimQualitySuite.OpenFromSearch(item.Name == "Smart QA/QC" ? null : item.Name);
            return;
        }
        if (dashboard == null || dashboard.IsDisposed) return;
        try
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("BIMlify.BimActions", false)).FirstOrDefault(t => t != null);
            FieldInfo handlerField = type?.GetField("dashboardActionHandler", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo eventField = type?.GetField("dashboardExternalEvent", BindingFlags.Static | BindingFlags.NonPublic);
            object handler = handlerField?.GetValue(null); ExternalEvent externalEvent = eventField?.GetValue(null) as ExternalEvent;
            MethodInfo queue = handler?.GetType().GetMethod("Queue", BindingFlags.Instance | BindingFlags.NonPublic);
            if (handler == null || externalEvent == null || queue == null) return;
            if (fromQualitySuite) BimQualitySuite.NavigateHome();
            else if (!ReferenceEquals(owner, dashboard)) { try { owner.Close(); } catch { } }
            dashboard.Hide();
            queue.Invoke(handler, new object[] { item.Action });
            ExternalEventRequest request = externalEvent.Raise();
            if ((int)request != 0 && (int)request != 1) { dashboard.Show(); dashboard.Activate(); MessageBox.Show(dashboard, "Revit is busy. Please try this tool again in a moment.", "BIMlify", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
        catch { if (!dashboard.IsDisposed) { dashboard.Show(); dashboard.Activate(); } }
    }

    private sealed class RelocatedHomeButton : Control
    {
        private bool _hover;
        internal RelocatedHomeButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(246, 240, 222);
            Cursor = Cursors.Hand;
            AccessibleName = "Home — return to BIMlify tools";
            AccessibleRole = AccessibleRole.PushButton;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(246, 240, 222));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle r = ClientRectangle;
            r.Width -= 1; r.Height -= 1;
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int d = 16;
                path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
                using (SolidBrush b = new SolidBrush(_hover ? Color.FromArgb(255, 249, 231) : Color.FromArgb(255, 252, 242))) e.Graphics.FillPath(b, path);
                using (Pen border = new Pen(Color.FromArgb(214, 171, 61), _hover ? 1.15f : 0.6f)) e.Graphics.DrawPath(border, path);
            }
            System.Drawing.Drawing2D.GraphicsState state = e.Graphics.Save();
            float scale = _hover ? 1.14f : 1f;
            e.Graphics.TranslateTransform(Width / 2f, Height / 2f);
            e.Graphics.ScaleTransform(scale, scale);
            e.Graphics.TranslateTransform(-Width / 2f, -Height / 2f);
            using (Pen p = new Pen(Color.FromArgb(214, 171, 61), 2f))
            {
                p.StartCap = p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                float x = Width / 2f, y = Height / 2f;
                e.Graphics.DrawLines(p, new PointF[] { new PointF(x - 10, y - 1), new PointF(x, y - 10), new PointF(x + 10, y - 1) });
                e.Graphics.DrawLine(p, x - 7, y - 2, x - 7, y + 9); e.Graphics.DrawLine(p, x + 7, y - 2, x + 7, y + 9);
                e.Graphics.DrawLine(p, x - 7, y + 9, x - 2, y + 9); e.Graphics.DrawLine(p, x + 2, y + 9, x + 7, y + 9);
                e.Graphics.DrawLine(p, x - 2, y + 9, x - 2, y + 3); e.Graphics.DrawLine(p, x + 2, y + 9, x + 2, y + 3); e.Graphics.DrawLine(p, x - 2, y + 3, x + 2, y + 3);
            }
            e.Graphics.Restore(state);
        }
    }

    private sealed class SearchRailButton : Control
    {
        private bool _hover;
        internal SearchRailButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; BackColor = Color.FromArgb(246, 240, 222); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(246, 240, 222)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            bool emphasized = _hover || Focused;
            using (System.Drawing.Drawing2D.GraphicsPath path = RoundedSearchPath(r, 8))
            using (SolidBrush b = new SolidBrush(_hover ? Color.FromArgb(255, 249, 231) : Color.FromArgb(255, 252, 242)))
            using (Pen border = new Pen(Color.FromArgb(214, 171, 61), emphasized ? 1.15f : 0.6f)) { e.Graphics.FillPath(b, path); e.Graphics.DrawPath(border, path); }
            System.Drawing.Drawing2D.GraphicsState state = e.Graphics.Save();
            float scale = _hover ? 1.14f : 1f;
            e.Graphics.TranslateTransform(21f, 21f);
            e.Graphics.ScaleTransform(scale, scale);
            e.Graphics.TranslateTransform(-21f, -21f);
            using (Pen icon = new Pen(Color.FromArgb(214, 171, 61), 2f)) { icon.StartCap = System.Drawing.Drawing2D.LineCap.Round; icon.EndCap = System.Drawing.Drawing2D.LineCap.Round; e.Graphics.DrawEllipse(icon, 13, 12, 12, 12); e.Graphics.DrawLine(icon, 23, 23, 29, 29); }
            e.Graphics.Restore(state);
        }
        private static System.Drawing.Drawing2D.GraphicsPath RoundedSearchPath(Rectangle r, int radius)
        {
            int d = radius * 2; System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
    }

    internal static Form DashboardWindow => _dashboard;

    internal static void MinimizeToQuickBar(Form window)
    {
        if (window == null || window.IsDisposed) return;
        try
        {
            Type actions = BimActionsType();
            if (actions == null) throw new InvalidOperationException("BIMlify dashboard is unavailable.");
            Form dashboard = _dashboard;
            if (dashboard == null || dashboard.IsDisposed)
            {
                FieldInfo dashboardField = actions.GetField("dashboardWindow", BindingFlags.Static | BindingFlags.NonPublic);
                dashboard = dashboardField?.GetValue(null) as Form;
            }
            if (ReferenceEquals(window, dashboard))
            {
                InvokeDashboardQuickBar(actions, window);
                return;
            }

            window.WindowState = FormWindowState.Minimized;
            if (!_quickBarMinimizedTools.Contains(window)) _quickBarMinimizedTools.Add(window);
            if (dashboard == null || dashboard.IsDisposed) return;
            InvokeDashboardQuickBar(actions, dashboard);
            HookQuickBarRestore(actions);
        }
        catch { }
        if (window != null && !window.IsDisposed) window.WindowState = FormWindowState.Minimized;
    }

    private static void InvokeDashboardQuickBar(Type actions, Form dashboard)
    {
        MethodInfo minimize = actions.GetMethod("MinimizeDashboardToQuickBar", BindingFlags.Static | BindingFlags.NonPublic);
        if (minimize == null || minimize.GetParameters().Length != 1 || !minimize.GetParameters()[0].ParameterType.IsInstanceOfType(dashboard))
            throw new MissingMethodException("The BIMlify Quick Bar entry point was not found.");
        minimize.Invoke(null, new object[] { dashboard });
    }

    private static void HookQuickBarRestore(Type actions)
    {
        FieldInfo barField = actions.GetField("dashboardQuickBarWindow", BindingFlags.Static | BindingFlags.NonPublic);
        Form bar = barField?.GetValue(null) as Form;
        if (bar == null || bar.IsDisposed || ReferenceEquals(bar, _quickBarRestoreHook)) return;
        _quickBarRestoreHook = bar;
        bar.FormClosed += (s, e) =>
        {
            _quickBarRestoreHook = null;
            Form[] restore = _quickBarMinimizedTools.ToArray();
            _quickBarMinimizedTools.Clear();
            if (String.Equals(bar.Tag as string, "shutdown", StringComparison.Ordinal))
            {
                foreach (var tool in restore)
                    if (tool != null && !tool.IsDisposed) tool.Close();
                return;
            }
            foreach (Form tool in restore)
            {
                if (tool == null || tool.IsDisposed) continue;
                tool.WindowState = FormWindowState.Normal;
                try { tool.Show(); tool.BringToFront(); tool.Activate(); } catch { }
            }
        };
    }

    public static void SetGreetingName(string name)
    {
        _greetingName = String.IsNullOrWhiteSpace(name) ? "Architect" : name.Trim();
        UpdateGreeting();
    }

    private static void UpdateGreeting()
    {
        Label label = _greeting;
        Label nameLabel = _greetingNameLabel;
        if (label == null || label.IsDisposed || nameLabel == null || nameLabel.IsDisposed) return;
        string period = DateTime.Now.Hour < 12 ? "Good morning," : (DateTime.Now.Hour < 17 ? "Good afternoon," : "Good evening,");
        if (label.InvokeRequired) { try { label.BeginInvoke(new Action(UpdateGreeting)); } catch { } return; }
        label.Text = period;
        nameLabel.Text = _greetingName + "!";
        int measured = TextRenderer.MeasureText(label.Text + " ", label.Font, new Size(440, 28), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        nameLabel.Left = label.Left + Math.Max(0, measured - 7);
    }
    public static void SetApplication(UIApplication application)
    {
        _application = application;
        InstallGridEditEscape();
    }

    public static void ApplyDashboard(Form form)
    {
        if (form == null || form.IsDisposed) return;
        _dashboard = form;
        form.KeyPreview = true;
        Panel footer = FindFooter(form);
        TableLayoutPanel groups = Descendants(form).OfType<TableLayoutPanel>().FirstOrDefault(x => x.ColumnCount == 3 && x.RowCount == 1);
        if (groups != null)
        {
            Padding p = groups.Padding;
            groups.Padding = new Padding(p.Left, p.Top + 36, p.Right, Math.Max(24, p.Bottom + 4));
            Label greeting = new Label
            {
                Name = "BIMlifyGreeting",
                Left = groups.Left + 26,
                Top = groups.Top + Math.Max(0, (groups.Padding.Top - 28) / 2) + 3,
                Width = 440,
                Height = 28,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(85, 91, 98),
                BackColor = form.BackColor
            };
            Label greetingName = new Label
            {
                Name = "BIMlifyGreetingName",
                Left = greeting.Left + 130,
                Top = greeting.Top,
                Width = 230,
                Height = greeting.Height,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold | FontStyle.Italic),
                ForeColor = Gold,
                BackColor = form.BackColor
            };
            _greeting = greeting;
            _greetingNameLabel = greetingName;
            form.Controls.Add(greeting);
            form.Controls.Add(greetingName);
            greeting.BringToFront();
            greetingName.BringToFront();
            UpdateGreeting();
            RestoreDashboardIdentity(form);
        }
        if (footer != null)
        {
            Color footerColor = Color.FromArgb(216, 221, 227);
            footer.Height = 42;
            footer.BackColor = footerColor;
            footer.Padding = new Padding(0, 0, 18, 0);
            Label[] copyright = footer.Controls.OfType<Label>().ToArray();
            foreach (Label item in copyright)
            {
                item.Top = 9;
                item.Height = 24;
                item.BackColor = footerColor;
            }
            int copyrightLeft = copyright.Length == 0 ? footer.ClientSize.Width - 220 : copyright.Min(x => x.Left);
            Label projectInfo = new Label
            {
                Name = "BIMlifyProjectStatus",
                Width = Math.Max(120, copyrightLeft - 36),
                Height = 17,
                Top = 0,
                Left = 18,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Muted,
                BackColor = footerColor,
                Font = new Font("Segoe UI", 7f, FontStyle.Regular),
                AutoEllipsis = true
            };
            UpdateProjectText(projectInfo);
            footer.Controls.Add(projectInfo);
            Label status = new Label
            {
                Name = "BIMlifyDashboardStatus",
                Left = 18,
                Top = 0,
                Width = Math.Max(120, copyrightLeft - 36),
                Height = 17,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Muted,
                BackColor = footerColor,
                Font = new Font("Segoe UI", 7f, FontStyle.Regular),
                AutoEllipsis = true,
                Text = "Ready"
            };
            footer.Controls.Add(status);
            _status = status;
            var contacts = new FlowLayoutPanel { Size = new Size(180, 26), WrapContents = false, BackColor = Color.Transparent };
            var contactTips = new ToolTip { OwnerDraw = true, InitialDelay = 220, ReshowDelay = 80, AutoPopDelay = 3600 };
            var contactTipFont = new Font("Segoe UI", 8f);
            contactTips.Popup += (sender, args) =>
            {
                var size = TextRenderer.MeasureText(contactTips.GetToolTip(args.AssociatedControl), contactTipFont);
                args.ToolTipSize = new Size(size.Width + 12, size.Height + 6);
            };
            contactTips.Draw += (sender, args) =>
            {
                using var background = new SolidBrush(Color.FromArgb(255, 241, 196));
                args.Graphics.FillRectangle(background, args.Bounds);
                TextRenderer.DrawText(args.Graphics, args.ToolTipText, contactTipFont, args.Bounds,
                    Color.FromArgb(78, 85, 92), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            foreach (var contact in new[]
            {
                ("in", "LinkedIn", "https://www.linkedin.com/in/esra-a-e-al-johari-msc-434056a5"),
                ("Bē", "Behance", "https://www.behance.net/eealjoharid6dd"),
                ("R", "ResearchGate", "https://www.researchgate.net/profile/Esraa-Al-Johari-2"),
                ("✉", "Email", "mailto:Ealjohari@yahoo.com"),
                ("◎", "Instagram", "https://www.instagram.com/ej.architect")
            })
            {
                var link = new Button { Text = contact.Item1, AccessibleName = contact.Item2,
                    Size = new Size(30, 26), Margin = new Padding(3, 0, 3, 0),
                    FlatStyle = FlatStyle.Flat, BackColor = footerColor, Cursor = Cursors.Hand };
                link.FlatAppearance.BorderSize = 0;
                link.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 235, 239);
                link.Paint += (sender, args) =>
                {
                    var graphics = args.Graphics;
                    graphics.Clear(footerColor);
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    const int diameter = 10;
                    int right = link.Width - 1, bottom = link.Height - 1;
                    using var outline = new System.Drawing.Drawing2D.GraphicsPath();
                    outline.AddArc(1, 1, diameter, diameter, 180, 90);
                    outline.AddArc(right - diameter, 1, diameter, diameter, 270, 90);
                    outline.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
                    outline.AddArc(1, bottom - diameter, diameter, diameter, 90, 90);
                    outline.CloseFigure();
                    bool hovered = link.ClientRectangle.Contains(link.PointToClient(Cursor.Position));
                    using var face = new SolidBrush(hovered ? link.FlatAppearance.MouseOverBackColor : footerColor);
                    using var edge = new Pen(Color.FromArgb(122, 134, 146), link.Focused ? 1.2f : 0.85f);
                    graphics.FillPath(face, outline);
                    graphics.DrawPath(edge, outline);
                    var ink = Color.FromArgb(86, 98, 110);
                    if (contact.Item2 == "Email")
                    {
                        using var envelope = new Pen(ink, 1.15f);
                        int x = (link.Width - 16) / 2, y = (link.Height - 10) / 2;
                        graphics.DrawRectangle(envelope, x, y, 16, 10);
                        graphics.DrawLines(envelope, new[] { new System.Drawing.Point(x, y),
                            new System.Drawing.Point(x + 8, y + 6), new System.Drawing.Point(x + 16, y) });
                    }
                    else
                    {
                        using var symbol = new System.Drawing.Drawing2D.GraphicsPath();
                        symbol.AddString(link.Text, link.Font.FontFamily, (int)link.Font.Style,
                            link.Font.SizeInPoints * graphics.DpiY / 72f, System.Drawing.PointF.Empty,
                            StringFormat.GenericTypographic);
                        var bounds = symbol.GetBounds();
                        using var offset = new System.Drawing.Drawing2D.Matrix();
                        offset.Translate((1 + right - bounds.Width) / 2f - bounds.Left,
                            (1 + bottom - bounds.Height) / 2f - bounds.Top);
                        symbol.Transform(offset);
                        using var symbolBrush = new SolidBrush(ink);
                        graphics.FillPath(symbolBrush, symbol);
                    }
                };
                link.MouseEnter += (sender, args) => link.Invalidate();
                link.MouseLeave += (sender, args) => link.Invalidate();
                contactTips.SetToolTip(link, contact.Item2);
                link.Click += (sender, args) =>
                {
                    try { Process.Start(new ProcessStartInfo(contact.Item3) { UseShellExecute = true }); }
                    catch (Exception error) { status.Text = error.Message; }
                };
                contacts.Controls.Add(link);
            }
            footer.Controls.Add(contacts);
            contacts.BringToFront();
            contacts.Disposed += (sender, args) => { contactTips.Dispose(); contactTipFont.Dispose(); };
            void LayoutContacts()
            {
                int right = copyright.Select(label => label.Left).DefaultIfEmpty(footer.ClientSize.Width - 220).Min();
                contacts.Left = Math.Min((footer.ClientSize.Width - contacts.Width) / 2, right - contacts.Width - 12);
                contacts.Top = (footer.ClientSize.Height - contacts.Height) / 2;
                contacts.Visible = contacts.Left >= 150;
                if (contacts.Visible)
                    status.Width = projectInfo.Width = contacts.Left - status.Left - 12;
            }
            footer.Resize += (s, e) =>
            {
                if (status.IsDisposed) return;
                int right = footer.Controls.OfType<Label>().Where(x => x.Name != "BIMlifyDashboardStatus" && x.Name != "BIMlifyProjectStatus").Select(x => x.Left).DefaultIfEmpty(footer.ClientSize.Width - 220).Min();
                status.Width = Math.Max(120, right - status.Left - 12);
                projectInfo.Width = status.Width;
                int stackTop = Math.Max(0, (footer.ClientSize.Height - projectInfo.Height - status.Height) / 2);
                projectInfo.Top = stackTop;
                status.Top = projectInfo.Bottom;
                foreach (Label item in copyright)
                    item.Top = Math.Max(0, (footer.ClientSize.Height - item.Height) / 2);
                LayoutContacts();
            };
            form.Shown += (sender, args) => LayoutContacts();
            int initialStackTop = Math.Max(0, (footer.ClientSize.Height - projectInfo.Height - status.Height) / 2);
            projectInfo.Top = initialStackTop;
            status.Top = projectInfo.Bottom;
            foreach (Label item in copyright)
                item.Top = Math.Max(0, (footer.ClientSize.Height - item.Height) / 2);
            _statusTimer?.Stop();
            _statusTimer?.Dispose();
            _statusTimer = new Timer { Interval = 500 };
            _statusTimer.Tick += (s, e) =>
            {
                if (_status == null || _status.IsDisposed || _pendingTool.Length == 0) return;
                if ((DateTime.UtcNow - _statusChanged).TotalSeconds >= 12)
                    SetState("Revit is busy. Please try again shortly.", Red);
            };
            _statusTimer.Start();
            form.FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_dashboard, form))
                {
                    _dashboard = null;
                    _status = null;
                    _greeting = null;
                    _greetingNameLabel = null;
                    _pendingTool = "";
                    _statusTimer?.Stop();
                    _statusTimer?.Dispose();
                    _statusTimer = null;
                    _quickBarToolsTimer?.Stop();
                    _quickBarToolsTimer?.Dispose();
                    _quickBarToolsTimer = null;
                }
            };
        }
        InstallGridEditEscape();
        BimQualitySuite.AttachDashboard(form);
        AttachToolSearchButton(form, 60);
        AddKeyboardAccess(form);
        StartQuickBarToolSync(form);
    }

    private static void StartQuickBarToolSync(Form dashboard)
    {
        _quickBarToolsTimer?.Stop();
        _quickBarToolsTimer?.Dispose();
        _quickBarToolsTimer = new Timer { Interval = 300 };
        _quickBarToolsTimer.Tick += (s, e) =>
        {
            if (dashboard == null || dashboard.IsDisposed || !ReferenceEquals(_dashboard, dashboard)) return;
            try { SyncQuickBarTools(); } catch { }
        };
        _quickBarToolsTimer.Start();
    }

    private static void SyncQuickBarTools()
    {
        Type actions = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("BIMlify.BimActions", false)).FirstOrDefault(t => t != null);
        if (actions == null) return;
        FieldInfo barField = actions.GetField("dashboardQuickBarWindow", BindingFlags.Static | BindingFlags.NonPublic);
        Form bar = barField?.GetValue(null) as Form;
        if (bar == null || bar.IsDisposed || !bar.Visible) return;
        TableLayoutPanel tools = Descendants(bar).OfType<TableLayoutPanel>().FirstOrDefault(x => x.RowCount == 1 && x.Controls.Count >= 10);
        if (tools == null || tools.Controls.ContainsKey("BIMlifyExtraQuickToolClash")) return;

        Type buttonType = actions.GetNestedType("QuickToolButton", BindingFlags.NonPublic);
        ConstructorInfo constructor = buttonType?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string), typeof(string) }, null);
        if (constructor == null) return;
        string icons = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(actions.Assembly.Location), "Icons");
        var additions = new[] {
            new { Name = "Clash Detection", Action = "clashdetection", Icon = "clash.png", Key = "Clash", Description = "Find overlapping model elements and review coordination clashes." },
            new { Name = "Smart Openings", Action = "smartopenings", Icon = "smartopenings.png", Key = "SmartOpenings", Description = "Place and manage MEP and structural openings in the model." },
            new { Name = "Smart Sheets", Action = "sheetorganization", Icon = "sheetorganization.png", Key = "SheetOrganization", Description = "Group, sequence, and renumber project sheets using your naming rules." },
            new { Name = "Smart QA/QC", Action = "qa", Icon = "smartqaqc.png", Key = "SmartQaQc", Description = "Review model quality, coordination, and delivery requirements." }
        };
        tools.SuspendLayout();
        try
        {
            int existing = tools.Controls.Count;
            tools.ColumnCount = existing + additions.Length;
            tools.ColumnStyles.Clear();
            for (int i = 0; i < tools.ColumnCount; i++) tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / tools.ColumnCount));
            ToolTip tips = new ToolTip { InitialDelay = 220, ReshowDelay = 80, AutoPopDelay = 3600, ShowAlways = true };
            bar.Disposed += (s, e) => tips.Dispose();
            for (int i = 0; i < additions.Length; i++)
            {
                var item = additions[i];
                Control button = constructor.Invoke(new object[] { item.Name, System.IO.Path.Combine(icons, item.Icon), null }) as Control;
                if (button == null) continue;
                button.Name = "BIMlifyExtraQuickTool" + item.Key;
                button.AccessibleName = item.Name;
                button.Dock = DockStyle.Fill; button.Height = 40; button.Margin = Padding.Empty; button.TabStop = true;
                tips.SetToolTip(button, item.Description);
                string action = item.Action;
                button.Click += (s, e) =>
                {
                    if (bar.IsDisposed) return;
                    if (action == "qa")
                    {
                        bar.Tag = "home";
                        bar.Close();
                        Form owner = _dashboard;
                        if (owner != null && !owner.IsDisposed)
                            owner.BeginInvoke(new Action(() => BimQualitySuite.OpenFromSearch(null)));
                    }
                    else
                    {
                        bar.Tag = action;
                        bar.Close();
                    }
                };
                tools.Controls.Add(button, existing + i, 0);
            }
        }
        finally { tools.ResumeLayout(true); }
    }

    public static void SetExecutionResult(int result)
    {
        if (result == 0)
        {
            string tool = _pendingTool;
            _pendingTool = "";
            SetState(string.IsNullOrEmpty(tool) ? "Ready" : tool + " opened.", Green);
        }
        else if (result == 1)
        {
            _pendingTool = "";
            SetState("Ready", Muted);
        }
        else
        {
            _pendingTool = "";
            SetState("Could not complete. See Revit's message for details.", Red);
        }
    }

    [ThreadStatic]
    private static bool _gridEditEscapeInstalled;

    internal static void InstallGridEditEscape()
    {
        if (_gridEditEscapeInstalled) return;
        System.Windows.Forms.Application.AddMessageFilter(new GridEditEscapeFilter());
        _gridEditEscapeInstalled = true;
    }

    [ThreadStatic]
    private static DataGridView _cancellingGrid;

    internal static bool IsCancellingGridEdit(DataGridView grid) => ReferenceEquals(_cancellingGrid, grid);

    internal static void AttachGridEditEscape(DataGridView grid)
    {
        InstallGridEditEscape();
        void Preview(object sender, PreviewKeyDownEventArgs args)
        {
            if (args.KeyCode == Keys.Escape && grid.IsCurrentCellInEditMode) args.IsInputKey = true;
        }
        void Cancel(object sender, KeyEventArgs args)
        {
            if (args.KeyCode != Keys.Escape || !grid.IsCurrentCellInEditMode) return;
            args.Handled = args.SuppressKeyPress = true;
            CancelGridEdit(grid);
        }

        grid.EditingControlShowing += (sender, args) =>
        {
            args.Control.PreviewKeyDown -= Preview;
            args.Control.KeyDown -= Cancel;
            args.Control.PreviewKeyDown += Preview;
            args.Control.KeyDown += Cancel;
        };
    }

    private static void CancelGridEdit(DataGridView grid)
    {
        var previous = _cancellingGrid;
        var cell = grid.CurrentCell;
        var editMode = grid.EditMode;
        _cancellingGrid = grid;
        try
        {
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            grid.CancelEdit();
            if (grid.IsCurrentCellInEditMode)
            {
                grid.CurrentCell = null;
                grid.CurrentCell = cell;
            }
        }
        finally
        {
            grid.EditMode = editMode;
            _cancellingGrid = previous;
        }
    }

    private sealed class GridEditEscapeFilter : IMessageFilter
    {
        private bool _cancelledGridEdit;

        public bool PreFilterMessage(ref Message message)
        {
            const int keyDown = 0x0100;
            const long previousKeyState = 1L << 30;
            if (message.Msg != keyDown || message.WParam.ToInt64() != (int)Keys.Escape) return false;

            bool repeat = (message.LParam.ToInt64() & previousKeyState) != 0;
            if (!repeat) _cancelledGridEdit = false;

            Control control = Control.FromChildHandle(message.HWnd);
            while (control != null && !(control is DataGridView))
                control = control.Parent;
            if (!(control is DataGridView grid)) return false;

            Form form = grid.FindForm();
            if (form == null || (!form.Text.StartsWith("BIMlify", StringComparison.OrdinalIgnoreCase)
                && !(form.GetType().Assembly.GetName().Name ?? "").StartsWith("BIMlify.", StringComparison.OrdinalIgnoreCase)))
                return false;

            if (repeat && _cancelledGridEdit) return true;
            if (!grid.IsCurrentCellInEditMode) return false;

            CancelGridEdit(grid);
            _cancelledGridEdit = true;
            return true;
        }
    }

    private static void AddKeyboardAccess(Form form)
    {
        int index = 0;
        foreach (Panel tile in Descendants(form).OfType<Panel>().Where(IsToolTile))
        {
            Label label = tile.Controls.OfType<Label>().FirstOrDefault();
            if (label == null) continue;
            tile.TabStop = true;
            tile.TabIndex = index++;
            tile.AccessibleRole = AccessibleRole.PushButton;
            tile.AccessibleName = label.Text.Replace(Environment.NewLine, " ").Trim();
            foreach (Control child in tile.Controls) child.TabStop = false;
            string toolName = tile.AccessibleName;
            EventHandler announce = (s, e) => SetOpening(toolName);
            tile.Click += announce;
            foreach (Control child in tile.Controls) child.Click += announce;
            tile.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space) return;
                RaiseClick?.Invoke(tile, new object[] { EventArgs.Empty });
                e.Handled = true;
                e.SuppressKeyPress = true;
            };
            tile.GotFocus += (s, e) => tile.Invalidate();
            tile.LostFocus += (s, e) => tile.Invalidate();
            tile.Paint += (s, e) =>
            {
                if (!tile.Focused) return;
                Rectangle bounds = tile.ClientRectangle;
                bounds.Inflate(-2, -4);
                using (Pen pen = new Pen(Color.FromArgb(214, 171, 61), 1f))
                {
                    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                    e.Graphics.DrawRectangle(pen, bounds);
                }
            };
        }
    }

    private static bool IsToolTile(Panel panel)
    {
        if (!(panel.Parent is FlowLayoutPanel)) return false;
        return panel.Controls.OfType<PictureBox>().Any() && panel.Controls.OfType<Label>().Any(x => !string.IsNullOrWhiteSpace(x.Text));
    }

    private static void RestoreDashboardIdentity(Form form)
    {
        AuthSession saved = _activeSession ?? LoadAuthSession();
        if (saved == null) return;
        if (!String.IsNullOrWhiteSpace(saved.DisplayName) && !String.Equals(saved.DisplayName, "Architect", StringComparison.OrdinalIgnoreCase))
            SetGreetingName(saved.DisplayName);
        Task.Run(() => RefreshAuthSession(saved)).ContinueWith(task =>
        {
            AuthSession session = task.IsFaulted ? null : task.Result;
            if (session == null || String.IsNullOrWhiteSpace(session.DisplayName) || session.DisplayName == "Architect") return;
            if (form.IsDisposed || !form.IsHandleCreated) return;
            try { form.BeginInvoke((Action)(() => { if (!form.IsDisposed) SetGreetingName(session.DisplayName); })); } catch { }
        });
    }

    private static void UpdateProjectText(Label label)
    {
        string title = "Current project";
        int links = 0;
        try
        {
            Document doc = _application?.ActiveUIDocument?.Document;
            if (doc != null)
            {
                string path = doc.PathName;
                title = !string.IsNullOrWhiteSpace(path) ? System.IO.Path.GetFileName(path) : doc.Title;
                links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount();
            }
        }
        catch { }
        label.Text = title + "  ·  " + links + (links == 1 ? " link" : " links");
        label.AccessibleName = "Project status: " + label.Text;
    }

    private static void SetState(string text, Color color)
    {
        Label label = _status;
        if (label == null || label.IsDisposed) return;
        if (label.InvokeRequired)
        {
            try { label.BeginInvoke(new Action(() => SetState(text, color))); } catch { }
            return;
        }
        label.Text = text;
        label.ForeColor = color;
        _statusChanged = DateTime.UtcNow;
    }

    private static void SetOpening(string tool)
    {
        _pendingTool = tool ?? "Tool";
        _statusChanged = DateTime.UtcNow;
        SetState("Opening " + _pendingTool + "…", Gold);
    }

    private static Panel FindFooter(Control root)
    {
        return Descendants(root).OfType<Panel>().FirstOrDefault(p => p.Dock == DockStyle.Bottom && p.Controls.OfType<Label>().Any(x => x.Text.StartsWith("©", StringComparison.Ordinal)));
    }

    internal static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }
}









