using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TextureTinter.Models;

namespace TextureTinter.Rendering;

public sealed class Preview3DControl : Control
{
    public static readonly StyledProperty<PreviewTextureSet?> TextureSetProperty =
        AvaloniaProperty.Register<Preview3DControl, PreviewTextureSet?>(nameof(TextureSet));

    public static readonly StyledProperty<ModelKind> ModelKindProperty =
        AvaloniaProperty.Register<Preview3DControl, ModelKind>(nameof(ModelKind));

    public static readonly StyledProperty<bool> AutoSpinProperty =
        AvaloniaProperty.Register<Preview3DControl, bool>(nameof(AutoSpin), true);

    public PreviewTextureSet? TextureSet
    {
        get => GetValue(TextureSetProperty);
        set => SetValue(TextureSetProperty, value);
    }
    public ModelKind ModelKind
    {
        get => GetValue(ModelKindProperty);
        set => SetValue(ModelKindProperty, value);
    }
    public bool AutoSpin
    {
        get => GetValue(AutoSpinProperty);
        set => SetValue(AutoSpinProperty, value);
    }

    float _yaw = 0.6f;
    float _pitch = 0.5f;

    float _zoom = 1.0f;
    float _zoomTarget = 1.0f;
    const float ZoomMin = 0.35f;
    const float ZoomMax = 3.0f;

    readonly DispatcherTimer _spinTimer;
    Point? _dragStart;
    float _dragYaw, _dragPitch;

    public Preview3DControl()
    {
        ClipToBounds = true;
        Focusable = true;

        _spinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _spinTimer.Tick += (_, _) =>
        {
            bool needsRedraw = false;

            if (AutoSpin && _dragStart is null)
            {
                _yaw += 0.012f;
                needsRedraw = true;
            }

            // Smooth zoom: approach target at ~20% per frame, snap when close.
            if (MathF.Abs(_zoom - _zoomTarget) > 0.0005f)
            {
                _zoom += (_zoomTarget - _zoom) * 0.22f;
                needsRedraw = true;
            }
            else if (_zoom != _zoomTarget)
            {
                _zoom = _zoomTarget;
                needsRedraw = true;
            }

            if (needsRedraw) InvalidateVisual();
        };
        _spinTimer.Start();

        // Intro animation: appear slightly zoomed-out and animate into place.
        _zoom = 0.72f;
        _zoomTarget = 1.0f;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _zoom = 0.72f;
        _zoomTarget = 1.0f;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextureSetProperty || change.Property == ModelKindProperty)
            InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragStart = e.GetPosition(this);
        _dragYaw = _yaw;
        _dragPitch = _pitch;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is null) return;
        var p = e.GetPosition(this);
        float dx = (float)(p.X - _dragStart.Value.X);
        float dy = (float)(p.Y - _dragStart.Value.Y);
        _yaw = _dragYaw + dx * 0.012f;
        _pitch = Math.Clamp(_dragPitch + dy * 0.012f, -1.55f, 1.55f);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragStart = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragStart = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        float delta = (float)e.Delta.Y;
        if (MathF.Abs(delta) < 0.0001f) return;

        // Multiplicative so zoom steps feel uniform at every level.
        float factor = MathF.Pow(1.12f, delta);
        _zoomTarget = Math.Clamp(_zoomTarget * factor, ZoomMin, ZoomMax);
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        double bw = Bounds.Width;
        double bh = Bounds.Height;

        var bg = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x22, 0x27, 0x2C), 0));
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x12, 0x14), 1));
        ctx.FillRectangle(bg, new Rect(0, 0, bw, bh));

        var set = TextureSet;
        if (set is null) return;

        float cx = (float)bw / 2f;
        float cy = (float)bh / 2f;
        float scale = MathF.Min((float)bw, (float)bh) * 0.55f * _zoom;

        if (ModelKind == ModelKind.Cross)
        {
            var bmp = set.PickCross();
            if (bmp is null) return;
            DrawCross(ctx, bmp, cx, cy, scale);
            return;
        }

        DrawCube(ctx, set, cx, cy, scale);
    }

    void DrawCube(DrawingContext ctx, PreviewTextureSet set, float cx, float cy, float scale)
    {
        var faces = new (Vector3[] verts, Vector2[] uv, Bitmap? bmp)[]
        {
            // front  (+Z)
            (new[] { V(0,0,1), V(1,0,1), V(1,1,1), V(0,1,1) }, QuadUVs(), set.PickFront()),
            // back   (-Z)
            (new[] { V(1,0,0), V(0,0,0), V(0,1,0), V(1,1,0) }, QuadUVs(), set.PickBack()),
            // left   (-X)
            (new[] { V(0,0,0), V(0,0,1), V(0,1,1), V(0,1,0) }, QuadUVs(), set.PickLeft()),
            // right  (+X)
            (new[] { V(1,0,1), V(1,0,0), V(1,1,0), V(1,1,1) }, QuadUVs(), set.PickRight()),
            // top    (+Y)
            (new[] { V(0,1,1), V(1,1,1), V(1,1,0), V(0,1,0) }, QuadUVs(), set.PickTop()),
            // bottom (-Y)
            (new[] { V(0,0,0), V(1,0,0), V(1,0,1), V(0,0,1) }, QuadUVs(), set.PickBottom()),
        };

        ProjectAndDraw(ctx, faces, cx, cy, scale);
    }

    void DrawCross(DrawingContext ctx, Bitmap bmp, float cx, float cy, float scale)
    {
        Vector3 a = new( 0.5f, 0f,  0.5f);
        Vector3 b = new(-0.5f, 0f, -0.5f);
        Vector3 c = new(-0.5f, 0f,  0.5f);
        Vector3 d = new( 0.5f, 0f, -0.5f);

        var q1 = new[] { b, a, a + Vector3.UnitY, b + Vector3.UnitY };
        var q2 = new[] { c, d, d + Vector3.UnitY, c + Vector3.UnitY };

        var faces = new (Vector3[] verts, Vector2[] uv, Bitmap? bmp)[]
        {
            (q1, QuadUVs(), bmp),
            (q2, QuadUVs(), bmp),
        };

        ProjectAndDraw(ctx, faces, cx, cy, scale);
    }

    void ProjectAndDraw(DrawingContext ctx,
                        (Vector3[] verts, Vector2[] uv, Bitmap? bmp)[] faces,
                        float cx, float cy, float scale)
    {
        float sYaw = MathF.Sin(_yaw), cYaw = MathF.Cos(_yaw);
        float sPit = MathF.Sin(_pitch), cPit = MathF.Cos(_pitch);

        var projected = new List<(Vector2[] pts, Vector2[] uv, Bitmap bmp, float depth)>(faces.Length);

        foreach (var (verts, uv, bmp) in faces)
        {
            if (bmp is null) continue;
            var p2 = new Vector2[verts.Length];
            float sumZ = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                var v1 = new Vector3(v.X * cYaw + v.Z * sYaw, v.Y, -v.X * sYaw + v.Z * cYaw);
                var v2 = new Vector3(v1.X, v1.Y * cPit - v1.Z * sPit, v1.Y * sPit + v1.Z * cPit);
                p2[i] = new Vector2(cx + v2.X * scale, cy - v2.Y * scale);
                sumZ += v2.Z;
            }
            projected.Add((p2, uv, bmp, sumZ / verts.Length));
        }

        projected.Sort((a, b) => a.depth.CompareTo(b.depth));

        foreach (var (pts, uv, bmp, _) in projected)
        {
            float texW = (float)bmp.Size.Width;
            float texH = (float)bmp.Size.Height;

            // Locate the vertex with UV (0,0) [image top-left], (1,0) [image top-right],
            // and (0,1) [image bottom-left]. Build the affine from those three.
            int iTL = 0, iTR = 0, iBL = 0;
            float dTL = float.MaxValue, dTR = float.MaxValue, dBL = float.MaxValue;
            for (int i = 0; i < uv.Length; i++)
            {
                float a0 = uv[i].X * uv[i].X + uv[i].Y * uv[i].Y;
                float b0 = (uv[i].X - 1) * (uv[i].X - 1) + uv[i].Y * uv[i].Y;
                float c0 = uv[i].X * uv[i].X + (uv[i].Y - 1) * (uv[i].Y - 1);
                if (a0 < dTL) { dTL = a0; iTL = i; }
                if (b0 < dTR) { dTR = b0; iTR = i; }
                if (c0 < dBL) { dBL = c0; iBL = i; }
            }

            Vector2 pTL = pts[iTL];
            Vector2 pTR = pts[iTR];
            Vector2 pBL = pts[iBL];

            // Screen delta per image pixel along each axis.
            Vector2 u = (pTR - pTL) / texW;
            Vector2 v = (pBL - pTL) / texH;

            var mat = new Matrix(u.X, u.Y, v.X, v.Y, pTL.X, pTL.Y);

            using (ctx.PushTransform(mat))
            using (ctx.PushRenderOptions(new RenderOptions
                   { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            {
                ctx.DrawImage(bmp, new Rect(0, 0, texW, texH));
            }
        }
    }

    static Vector3 V(float x, float y, float z) => new(x - 0.5f, y - 0.5f, z - 0.5f);

    // Quad UVs in vertex order: BL, BR, TR, TL (bottom-left, bottom-right, top-right, top-left)
    static Vector2[] QuadUVs() => new[]
    {
        new Vector2(0, 1),  // BL
        new Vector2(1, 1),  // BR
        new Vector2(1, 0),  // TR
        new Vector2(0, 0),  // TL
    };
}