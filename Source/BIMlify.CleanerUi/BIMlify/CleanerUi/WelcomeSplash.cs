using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace BIMlify.CleanerUi;

public static class WelcomeSplash
{
    private static readonly object Gate = new object();
    private static Form _current;
    private static bool _running;
    private static readonly ManualResetEvent SplashCompleted = new ManualResetEvent(false);

    private static void ApplyRoundedRegion(Form form, int radius)
    {
        if (form == null || form.IsDisposed || form.ClientSize.Width <= 0 || form.ClientSize.Height <= 0) return;
        int d = Math.Max(2, radius * 2);
        using (GraphicsPath path = new GraphicsPath())
        {
            Rectangle r = new Rectangle(0, 0, form.ClientSize.Width, form.ClientSize.Height);
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            Region old = form.Region;
            form.Region = new Region(path);
            old?.Dispose();
        }
    }

    public static void Show()
    {
        lock (Gate)
        {
            if (_running) return;
            _running = true;
            SplashCompleted.Reset();
        }

        
        StartupDiagnostics.RecordSplash("requested");

        Thread thread = new Thread(ShowOnUiThread)
        {
            IsBackground = true,
            Name = "BIMlify welcome screen"
        };
        thread.SetApartmentState(ApartmentState.STA);
        try
        {
            thread.Start();
            SplashCompleted.WaitOne();
        }
        catch (Exception exception)
        {
            lock (Gate) _running = false;
            StartupDiagnostics.RecordSplashError(exception);
            SplashCompleted.Set();
        }
    }

    private static void ShowOnUiThread()
    {
        Form splash = null;
        Timer timer = null;
        Image image = null;
        try
        {
        splash = new Form
        {
            Text = "BIMlify",
            FormBorderStyle = FormBorderStyle.None,
            ClientSize = new Size(1080, 720),
            StartPosition = FormStartPosition.CenterScreen,
            ShowInTaskbar = false,
            ShowIcon = false,
            TopMost = true,
            BackColor = Color.White,
            Opacity = 0.98
        };
        lock (Gate) _current = splash;

        SmoothWelcomeLogoView logo = new SmoothWelcomeLogoView { Size = new Size(500, 425) };

        string logoPath = Path.Combine(Path.GetDirectoryName(typeof(WelcomeSplash).Assembly.Location), "Branding", "LogoWelcome.png");
        try { if (File.Exists(logoPath)) using (Image original = Image.FromFile(logoPath)) image = new Bitmap(original); }
        catch { }
        logo.Logo = image;

        Label message = new Label
        {
            Text = "Opening your BIMlify workspace",
            Width = 360,
            Height = 24,
            ForeColor = Color.FromArgb(102, 109, 118),
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.White
        };
        Font creatorFont = new Font("Segoe UI", 6.8f);
        Font creatorNameFont = new Font(creatorFont, FontStyle.Italic);
        Panel creatorCredit = new Panel
        {
            Width = 360,
            Height = 24,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            BackColor = Color.White,
            AccessibleRole = AccessibleRole.StaticText,
            AccessibleName = "BIMlify Developed by Esra'a E. Al-Johari"
        };
        creatorCredit.Paint += (s, e) =>
        {
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using StringFormat format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
            string brandBim = "BIM";
            string brandLify = "lify";
            string prefix = "Developed by";
            string name = "Esra'a E. Al-Johari";
            const float brandGap = 3f;
            const float nameGap = 4f;
            SizeF brandBimSize = e.Graphics.MeasureString(brandBim, creatorFont, int.MaxValue, format);
            SizeF brandLifySize = e.Graphics.MeasureString(brandLify, creatorFont, int.MaxValue, format);
            SizeF prefixSize = e.Graphics.MeasureString(prefix, creatorFont, int.MaxValue, format);
            SizeF nameSize = e.Graphics.MeasureString(name, creatorFont, int.MaxValue, format);
            float totalWidth = brandBimSize.Width + brandLifySize.Width + brandGap + prefixSize.Width + nameGap + nameSize.Width;
            float left = (creatorCredit.ClientSize.Width - totalWidth) / 2f;
            float top = (creatorCredit.ClientSize.Height - creatorFont.GetHeight(e.Graphics)) / 2f;
            using SolidBrush prefixBrush = new SolidBrush(Color.FromArgb(148, 151, 154));
            using SolidBrush nameBrush = new SolidBrush(Color.FromArgb(214, 171, 61));
            using SolidBrush brandBimBrush = new SolidBrush(Color.FromArgb(148, 151, 154));
            using SolidBrush brandLifyBrush = new SolidBrush(Color.FromArgb(198, 151, 42));
            e.Graphics.DrawString(brandBim, creatorFont, brandBimBrush, left, top, format);
            e.Graphics.DrawString(brandLify, creatorFont, brandLifyBrush, left + brandBimSize.Width, top, format);
            e.Graphics.DrawString(prefix, creatorFont, prefixBrush, left + brandBimSize.Width + brandLifySize.Width + brandGap, top, format);
            e.Graphics.DrawString(name, creatorNameFont, nameBrush, left + brandBimSize.Width + brandLifySize.Width + brandGap + prefixSize.Width + nameGap, top, format);
        };
        creatorCredit.Disposed += (s, e) => { creatorFont.Dispose(); creatorNameFont.Dispose(); };
        Panel track = new Panel { Width = 280, Height = 3, BackColor = Color.FromArgb(235, 232, 224) };
        Panel fill = new Panel { Dock = DockStyle.Left, Width = 0, BackColor = Color.FromArgb(214, 171, 61) };
        track.Controls.Add(fill);
        splash.Controls.Add(logo);
        splash.Controls.Add(message);
        splash.Controls.Add(creatorCredit);
        splash.Controls.Add(track);
        Action layout = () =>
        {
            logo.Left = (splash.ClientSize.Width - logo.Width) / 2;
            logo.Top = 120;
            message.Left = (splash.ClientSize.Width - message.Width) / 2;
            message.Top = 555;
            track.Left = (splash.ClientSize.Width - track.Width) / 2;
            track.Top = 594;
            creatorCredit.Left = (splash.ClientSize.Width - creatorCredit.Width) / 2;
            int messageToTrackGap = Math.Max(0, track.Top - message.Bottom);
            creatorCredit.Top = Math.Min(splash.ClientSize.Height - creatorCredit.Height - 8, track.Bottom + messageToTrackGap) - 2;
        };
        splash.SizeChanged += (s, e) => { ApplyRoundedRegion(splash, 18); layout(); };
        splash.Shown += (s, e) => ApplyRoundedRegion(splash, 18);
        layout();

        splash.CreateControl();
        logo.CreateControl();
        logo.WarmUpRenderer();

        timer = new Timer { Interval = 16 };
        Stopwatch started = null;
        timer.Tick += (s, e) =>
        {
            double elapsed = started.Elapsed.TotalMilliseconds;
            logo.Motion = Math.Min(1.0, elapsed / 750.0);
            logo.OrbitMotion = Math.Min(1.38, Math.Max(0.0, elapsed / 1950.0));
            logo.Invalidate();
            if (elapsed > 2100.0) splash.Opacity = Math.Max(0.0, 0.98 - (elapsed - 2100.0) / 220.0);
            fill.Width = (int)(track.Width * Math.Min(1.0, elapsed / 1400.0));
            if (elapsed >= 2400.0) splash.Close();
        };
        splash.Shown += (s, e) =>
        {
            started = Stopwatch.StartNew();
            StartupDiagnostics.RecordSplash("shown");
            timer.Start();
        };
        splash.FormClosed += (s, e) =>
        {
            long duration = started == null ? -1 : started.ElapsedMilliseconds;
            timer.Stop();
            timer.Dispose();
            timer = null;
            image?.Dispose();
            image = null;
            StartupDiagnostics.RecordSplash("closed", duration);
            SplashCompleted.Set();
            lock (Gate)
            {
                if (ReferenceEquals(_current, splash)) _current = null;
                _running = false;
            }
        };
        Application.Run(splash);
        }
        catch (Exception exception)
        {
            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
            }
            image?.Dispose();
            if (splash != null && !splash.IsDisposed) splash.Dispose();
            lock (Gate)
            {
                _current = null;
                _running = false;
            }
            StartupDiagnostics.RecordSplashError(exception);
            SplashCompleted.Set();
        }
    }
}
