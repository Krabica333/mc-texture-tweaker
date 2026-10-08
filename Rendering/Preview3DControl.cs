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
    public static readonly StyledProperty<Bitmap?> TextureProperty =
        AvaloniaProperty.Register<Preview3DControl, Bitmap?>(nameof(Texture));

    public static readonly StyledProperty<ModelKind> ModelKindProperty =
        AvaloniaProperty.Register<Preview3DControl, ModelKind>(nameof(ModelKind));

    public static readonly StyledProperty<bool> AutoSpinProperty =
        AvaloniaProperty.Register<Preview3DControl, bool>(nameof(AutoSpin), true);

    public Bitmap? Texture
    {
        get => GetValue(TextureProperty);
        set => SetValue(TextureProperty, value);
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
            if (AutoSpin && _dragStart is null)
            {
                _yaw += 0.012f;
                InvalidateVisual();
            }
        };
        _spinTimer.Start();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextureProperty || change.Property == ModelKindProperty)
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

        if (Texture is null) return;

        float cx = (float)bw / 2f;
        float cy = (float)bh / 2f;
        float scale = MathF.Min((float)bw, (float)bh) * 0.55f;

        var quads = ModelKind == ModelKind.Cube ? BuildCube() : BuildCross();
        var projected = new List<(Vector2[] pts, Vector2[] uv, float depth)>(quads.Length);

        float sYaw = MathF.Sin(_yaw),  cYaw = MathF.Cos(_yaw);
        float sPit = MathF.Sin(_pitch), cPit = MathF.Cos(_pitch);

        foreach (var (verts, uv) in quads)
        {
            var p2 = new Vector2[verts.Length];
            float sumZ = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                Vector3 v1 = new(
                    v.X * cYaw + v.Z * sYaw,
                    v.Y,
                    -v.X * sYaw + v.Z * cYaw);
                Vector3 v2 = new(
                    v1.X,
                    v1.Y * cPit - v1.Z * sPit,
                    v1.Y * sPit + v1.Z * cPit);
                p2[i] = new Vector2(cx + v2.X * scale, cy - v2.Y * scale);
                sumZ += v2.Z;
            }
            projected.Add((p2, uv, sumZ / verts.Length));
        }

        projected.Sort((a, b) => a.depth.CompareTo(b.depth));

        float texW = (float)Texture.Size.Width;
        float texH = (float)Texture.Size.Height;

        foreach (var (pts, uv, _) in projected)
        {
            Vector2 p0 = pts[0], p1 = pts[1], p3 = pts[3];
            Vector2 u = p1 - p0;
            Vector2 v = p3 - p0;

            float uSpan = MathF.Max(0.0001f, MathF.Abs(uv[1].X - uv[0].X) * texW);
            float vSpan = MathF.Max(0.0001f, MathF.Abs(uv[3].Y - uv[0].Y) * texH);

            var mat = new Matrix(
                u.X / uSpan, u.Y / uSpan,
                v.X / vSpan, v.Y / vSpan,
                p0.X,        p0.Y);

            using (ctx.PushTransform(mat))
            using (ctx.PushRenderOptions(new RenderOptions
                   {
                       BitmapInterpolationMode = BitmapInterpolationMode.None
                   }))
            {
                ctx.DrawImage(Texture, new Rect(0, 0, texW, texH));
            }
        }
    }

    static (Vector3[] verts, Vector2[] uv)[] BuildCube()
    {
        return new[]
        {
            (new[] { V(0,0,1), V(1,0,1), V(1,1,1), V(0,1,1) }, new[] { UV(0,1), UV(1,1), UV(1,0), UV(0,0) }),
            (new[] { V(1,0,0), V(0,0,0), V(0,1,0), V(1,1,0) }, new[] { UV(0,1), UV(1,1), UV(1,0), UV(0,0) }),
            (new[] { V(0,0,0), V(0,0,1), V(0,1,1), V(0,1,0) }, new[] { UV(0,1), UV(1,1), UV(1,0), UV(0,0) }),
            (new[] { V(1,0,1), V(1,0,0), V(1,1,0), V(1,1,1) }, new[] { UV(0,1), UV(1,1), UV(1,0), UV(0,0) }),
            (new[] { V(0,1,1), V(1,1,1), V(1,1,0), V(0,1,0) }, new[] { UV(0,1), UV(1,1), UV(1,0), UV(0,0) }),
            (new[] { V(0,0,0), V(1,0,0), V(1,0,1), V(0,0,1) }, new[] { UV(0,1), UV(1,1), UV(1,0), UV(0,0) }),
        };
    }

    static (Vector3[] verts, Vector2[] uv)[] BuildCross()
    {
        Vector3 a = new( 0.5f, 0f,  0.5f);
        Vector3 b = new(-0.5f, 0f, -0.5f);
        Vector3 c = new(-0.5f, 0f,  0.5f);
        Vector3 d = new( 0.5f, 0f, -0.5f);

        var q1 = new[] { b, a, a + Vector3.UnitY, b + Vector3.UnitY };
        var q2 = new[] { c, d, d + Vector3.UnitY, c + Vector3.UnitY };

        return new[]
        {
            (q1, new[] { UV(0,1), UV(1,1), UV(1,0), UV(0,0) }),
            (q2, new[] { UV(0,1), UV(1,1), UV(1,0), UV(0,0) }),
        };
    }

    static Vector3 V(float x, float y, float z) => new(x - 0.5f, y - 0.5f, z - 0.5f);
    static Vector2 UV(float u, float v) => new(u, v);
}