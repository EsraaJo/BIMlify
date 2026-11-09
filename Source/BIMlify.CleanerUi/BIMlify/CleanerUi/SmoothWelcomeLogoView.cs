using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace BIMlify.CleanerUi;

internal sealed class SmoothWelcomeLogoView : Control
{
    private const int FadeFrameCount = 49;
    private Image _logo;
    private Bitmap _staticLayer;
    private Bitmap[] _logoFrames = new Bitmap[0];
    private readonly Pen _orbitPen = new Pen(Color.Transparent, 1.15f);
    private readonly Pen _innerOrbitPen = new Pen(Color.Transparent, 1f);
    private double _motion;
    private double _orbitMotion;
    private int _fadeFrame;

    public Image Logo
    {
        get => _logo;
        set { _logo = value; RebuildStaticLayer(); RebuildLogoFrames(); Invalidate(); }
    }

    public double Motion
    {
        get => _motion;
        set { _motion = Clamp(value, 0.0, 1.0); _fadeFrame = (int)Math.Round(_motion * (FadeFrameCount - 1)); }
    }

    public double OrbitMotion
    {
        get => _orbitMotion;
        set { _orbitMotion = Clamp(value, 0.0, 1.38); }
    }

    public SmoothWelcomeLogoView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        _orbitPen.StartCap = LineCap.Round;
        _orbitPen.EndCap = LineCap.Round;
        _innerOrbitPen.StartCap = LineCap.Round;
        _innerOrbitPen.EndCap = LineCap.Round;
    }

    public void WarmUpRenderer()
    {
        if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
        Motion = 1.0;
        OrbitMotion = 1.0;
        using (Bitmap buffer = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb))
            DrawToBitmap(buffer, new Rectangle(Point.Empty, ClientSize));
        Motion = 0.0;
        OrbitMotion = 0.0;
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        RebuildStaticLayer();
    }

    private void RebuildStaticLayer()
    {
        _staticLayer?.Dispose();
        _staticLayer = null;
        if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
        Bitmap layer = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(layer))
        {
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_logo != null && _logo.Width > 0)
            {
                float cx = ClientSize.Width / 2f;
                float cy = ClientSize.Height / 2f;
                float w = 180f;
                float h = w * _logo.Height / _logo.Width;
                using (GraphicsPath glow = new GraphicsPath())
                {
                    glow.AddEllipse(cx - w * 0.48f, cy - h * 0.5f, w * 0.96f, h * 0.98f);
                    using (PathGradientBrush brush = new PathGradientBrush(glow))
                    {
                        brush.CenterColor = Color.FromArgb(16, 214, 171, 61);
                        brush.SurroundColors = new[] { Color.FromArgb(0, 214, 171, 61) };
                        g.FillPath(brush, glow);
                    }
                }
                RectangleF shadow = new RectangleF(cx - 116f, cy + h / 2f - 2f, 232f, 22f);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(shadow);
                    using (PathGradientBrush brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = Color.FromArgb(44, 79, 76, 68);
                        brush.SurroundColors = new[] { Color.FromArgb(0, 79, 76, 68) };
                        g.FillPath(brush, path);
                    }
                }
            }
        }
        _staticLayer = layer;
    }

    private void RebuildLogoFrames()
    {
        foreach (Bitmap frame in _logoFrames) frame?.Dispose();
        _logoFrames = new Bitmap[0];
        if (_logo == null || _logo.Width < 1 || _logo.Height < 1) return;
        int width = 180;
        int height = Math.Max(1, (int)Math.Round(width * (double)_logo.Height / _logo.Width));
        Bitmap[] frames = new Bitmap[FadeFrameCount];
        using (ImageAttributes attributes = new ImageAttributes())
        {
            ColorMatrix matrix = new ColorMatrix();
            for (int i = 0; i < frames.Length; i++)
            {
                matrix.Matrix33 = (float)i / (frames.Length - 1);
                attributes.SetColorMatrix(matrix);
                Bitmap frame = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(_logo, new Rectangle(0, 0, width, height), 0, 0, _logo.Width, _logo.Height, GraphicsUnit.Pixel, attributes);
                }
                frames[i] = frame;
            }
        }
        _logoFrames = frames;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_staticLayer != null) e.Graphics.DrawImageUnscaled(_staticLayer, 0, 0);
        if (_logoFrames.Length != 0)
        {
            Bitmap frame = _logoFrames[Math.Max(0, Math.Min(_fadeFrame, _logoFrames.Length - 1))];
            e.Graphics.DrawImageUnscaled(frame, (ClientSize.Width - frame.Width) / 2, (ClientSize.Height - frame.Height) / 2);
        }
        if (_orbitMotion <= 0.0) return;
        double progress = Math.Min(1.0, _orbitMotion);
        double fade = _orbitMotion <= 1.0 ? 1.0 : Math.Max(0.0, 1.0 - Math.Min(1.0, (_orbitMotion - 1.0) / 0.38));
        int alpha = (int)(105.0 * (0.55 + 0.45 * Math.Sin(progress * Math.PI)) * fade);
        _orbitPen.Color = Color.FromArgb(alpha, 214, 171, 61);
        _innerOrbitPen.Color = Color.FromArgb(Math.Max(0, alpha / 3), 214, 171, 61);
        float size = 316f;
        float x = ClientSize.Width / 2f - size / 2f - 10f;
        float y = ClientSize.Height / 2f - size / 2f;
        float sweep = Math.Min(360f, 360f * (float)progress);
        e.Graphics.DrawArc(_orbitPen, x, y, size, size, -18f, sweep);
        e.Graphics.DrawArc(_innerOrbitPen, x + 8f, y + 5f, size - 16f, size - 10f, -20f, sweep);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _staticLayer?.Dispose();
            foreach (Bitmap frame in _logoFrames) frame?.Dispose();
            _orbitPen.Dispose();
            _innerOrbitPen.Dispose();
        }
        base.Dispose(disposing);
    }

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
