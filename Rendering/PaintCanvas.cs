using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using McTextureTweaker.Models;

namespace McTextureTweaker.Rendering;

public sealed class PaintCanvas : Control
{
    const int N = PaintLayer.Size;

    public static readonly StyledProperty<byte[]?> BasePixelsProperty =
        AvaloniaProperty.Register<PaintCanvas, byte[]?>(nameof(BasePixels));

    public static readonly StyledProperty<IList<PaintLayer>?> LayersProperty =
        AvaloniaProperty.Register<PaintCanvas, IList<PaintLayer>?>(nameof(Layers));

    public static readonly StyledProperty<int> ActiveLayerIndexProperty =
        AvaloniaProperty.Register<PaintCanvas, int>(nameof(ActiveLayerIndex));

    public static readonly StyledProperty<int> ZoomProperty =
        AvaloniaProperty.Register<PaintCanvas, int>(nameof(Zoom), 28);

    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<PaintCanvas, bool>(nameof(ShowGrid), true);

    public static readonly StyledProperty<Color> CurrentColorProperty =
        AvaloniaProperty.Register<PaintCanvas, Color>(nameof(CurrentColor), Colors.White);

    public static readonly StyledProperty<PaintTool> ToolProperty =
        AvaloniaProperty.Register<PaintCanvas, PaintTool>(nameof(Tool), PaintTool.Pencil);

    public byte[]? BasePixels { get => GetValue(BasePixelsProperty); set => SetValue(BasePixelsProperty, value); }
    public IList<PaintLayer>? Layers { get => GetValue(LayersProperty); set => SetValue(LayersProperty, value); }
    public int ActiveLayerIndex { get => GetValue(ActiveLayerIndexProperty); set => SetValue(ActiveLayerIndexProperty, value); }
    public int Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public bool ShowGrid { get => GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public Color CurrentColor { get => GetValue(CurrentColorProperty); set => SetValue(CurrentColorProperty, value); }
    public PaintTool Tool { get => GetValue(ToolProperty); set => SetValue(ToolProperty, value); }

    public event EventHandler? StrokeBegin;
    public event EventHandler<Color>? ColorPicked;

    static readonly IBrush CheckerA = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
    static readonly IBrush CheckerB = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
    static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
    static readonly IPen OutlinePen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xBB, 0x77)), 1.5);
    static readonly IPen HoverPen = new Pen(Brushes.White, 1.5);
    static readonly IPen ShapeOutlinePen = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), 1);

    IList<PaintLayer>? _boundLayers;
    int? _hoverX, _hoverY;
    bool _drawing;
    int _lastX, _lastY;
    PaintTool _effectiveTool = PaintTool.Pencil;

    // Shape drag preview
    int? _shapeAnchorX, _shapeAnchorY;
    int? _shapeCurrentX, _shapeCurrentY;
    bool _shapeActive;

    public PaintCanvas()
    {
        ClipToBounds = true;
        Focusable = true;

        this.GetObservable(LayersProperty).Subscribe(RebindLayers);
        this.GetObservable(BasePixelsProperty).Subscribe(_ => InvalidateVisual());
        this.GetObservable(ActiveLayerIndexProperty).Subscribe(_ => InvalidateVisual());
        this.GetObservable(ZoomProperty).Subscribe(_ => InvalidateVisual());
        this.GetObservable(ShowGridProperty).Subscribe(_ => InvalidateVisual());
    }

    void RebindLayers(IList<PaintLayer>? layers)
    {
        if (_boundLayers is not null)
        {
            foreach (var l in _boundLayers) l.PropertyChanged -= OnLayerChanged;
            if (_boundLayers is INotifyCollectionChanged nc) nc.CollectionChanged -= OnCollectionChanged;
        }
        _boundLayers = layers;
        if (_boundLayers is not null)
        {
            foreach (var l in _boundLayers) l.PropertyChanged += OnLayerChanged;
            if (_boundLayers is INotifyCollectionChanged nc) nc.CollectionChanged += OnCollectionChanged;
        }
        InvalidateVisual();
    }

    void OnCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        RebindLayers(_boundLayers);
        InvalidateVisual();
    }

    void OnLayerChanged(object? s, PropertyChangedEventArgs e) => InvalidateVisual();

    // ── Coordinate helpers ─────────────────────────────────
    (float ox, float oy) Offset()
    {
        float boardW = N * Zoom;
        float boardH = N * Zoom;
        return ((float)(Bounds.Width - boardW) / 2f, (float)(Bounds.Height - boardH) / 2f);
    }

    bool TryPosToPixel(Point p, out int px, out int py)
    {
        var (ox, oy) = Offset();
        int x = (int)Math.Floor((p.X - ox) / Zoom);
        int y = (int)Math.Floor((p.Y - oy) / Zoom);
        px = x; py = y;
        return x >= 0 && x < N && y >= 0 && y < N;
    }

    PaintLayer? ActiveLayer =>
        Layers is not null && ActiveLayerIndex >= 0 && ActiveLayerIndex < Layers.Count
            ? Layers[ActiveLayerIndex] : null;

    static bool IsShapeTool(PaintTool t) =>
        t is PaintTool.Line or PaintTool.Rect or PaintTool.RectFill
          or PaintTool.Ellipse or PaintTool.EllipseFill;

    // ── Rendering ──────────────────────────────────────────
    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                          new Rect(0, 0, Bounds.Width, Bounds.Height));

        var (ox, oy) = Offset();
        int cell = Zoom;
        float size = N * cell;

        // Checkerboard
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var b = ((x + y) & 1) == 0 ? CheckerA : CheckerB;
                ctx.FillRectangle(b, new Rect(ox + x * cell, oy + y * cell, cell, cell));
            }

        // Base texture
        var bp = BasePixels;
        if (bp is not null && bp.Length >= N * N * 4)
        {
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = (y * N + x) * 4;
                    byte a = bp[i + 3];
                    if (a == 0) continue;
                    var col = Color.FromArgb(a, bp[i], bp[i + 1], bp[i + 2]);
                    ctx.FillRectangle(new SolidColorBrush(col),
                        new Rect(ox + x * cell, oy + y * cell, cell, cell));
                }
        }

        // Layers
        if (Layers is not null)
        {
            foreach (var layer in Layers)
            {
                if (!layer.Visible) continue;
                var px = layer.PixelData;
                float op = layer.Opacity;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        int i = (y * N + x) * 4;
                        byte a0 = px[i + 3];
                        if (a0 == 0) continue;
                        byte a = (byte)(a0 * op);
                        if (a == 0) continue;
                        var col = Color.FromArgb(a, px[i], px[i + 1], px[i + 2]);
                        ctx.FillRectangle(new SolidColorBrush(col),
                            new Rect(ox + x * cell, oy + y * cell, cell, cell));
                    }
            }
        }

        // Shape preview (semi-transparent overlay while dragging)
        if (_shapeActive && _shapeAnchorX is int ax && _shapeAnchorY is int ay
                         && _shapeCurrentX is int cx2 && _shapeCurrentY is int cy2)
        {
            var shapeTemp = new byte[PaintLayer.ByteCount];
            if (!IsShapeTool(Tool)) { /* skip */ }
            else
            {
                switch (Tool)
                {
                    case PaintTool.Line:      RasterLine(shapeTemp, ax, ay, cx2, cy2); break;
                    case PaintTool.Rect:      RasterRect(shapeTemp, ax, ay, cx2, cy2, false); break;
                    case PaintTool.RectFill:  RasterRect(shapeTemp, ax, ay, cx2, cy2, true); break;
                    case PaintTool.Ellipse:   RasterEllipse(shapeTemp, ax, ay, cx2, cy2, false); break;
                    case PaintTool.EllipseFill:RasterEllipse(shapeTemp, ax, ay, cx2, cy2, true); break;
                }
                var previewColor = Color.FromArgb(180, CurrentColor.R, CurrentColor.G, CurrentColor.B);
                var previewBrush = new SolidColorBrush(previewColor);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        int i = (y * N + x) * 4;
                        if (shapeTemp[i + 3] == 0) continue;
                        ctx.FillRectangle(previewBrush,
                            new Rect(ox + x * cell, oy + y * cell, cell, cell));
                    }
            }
        }

        // Grid
        if (ShowGrid)
        {
            for (int x = 0; x <= N; x++)
                ctx.DrawLine(GridPen,
                    new Point(ox + x * cell, oy),
                    new Point(ox + x * cell, oy + size));
            for (int y = 0; y <= N; y++)
                ctx.DrawLine(GridPen,
                    new Point(ox, oy + y * cell),
                    new Point(ox + size, oy + y * cell));
        }

        // Board outline
        ctx.DrawRectangle(null, OutlinePen, new Rect(ox, oy, size, size));

        // Hover cell
        if (_hoverX is int hx && _hoverY is int hy && !_shapeActive)
            ctx.DrawRectangle(null, HoverPen,
                new Rect(ox + hx * cell, oy + hy * cell, cell, cell));
    }

    // ── Input ──────────────────────────────────────────────
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var pt = e.GetCurrentPoint(this);
        var props = pt.Properties;

        if (!props.IsLeftButtonPressed &&
            !props.IsRightButtonPressed &&
            !props.IsMiddleButtonPressed)
            return;

        if (!TryPosToPixel(pt.Position, out int px, out int py)) return;
        var layer = ActiveLayer;
        if (layer is null) return;

        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        PaintTool effective = Tool;
        if (alt || props.IsMiddleButtonPressed) effective = PaintTool.Eyedropper;
        else if (props.IsRightButtonPressed)    effective = PaintTool.Eraser;

        switch (effective)
        {
            case PaintTool.Eyedropper:
                PickColor(px, py);
                e.Handled = true;
                return;

            case PaintTool.Fill:
                StrokeBegin?.Invoke(this, EventArgs.Empty);
                FloodFill(layer, px, py, CurrentColor);
                InvalidateVisual();
                e.Handled = true;
                return;

            case PaintTool.Pencil:
            case PaintTool.Eraser:
                StrokeBegin?.Invoke(this, EventArgs.Empty);
                _drawing = true;
                _effectiveTool = effective;
                _lastX = px;
                _lastY = py;
                PaintPixel(layer, px, py);
                e.Pointer.Capture(this);
                InvalidateVisual();
                e.Handled = true;
                return;

            case PaintTool.Line:
            case PaintTool.Rect:
            case PaintTool.RectFill:
            case PaintTool.Ellipse:
            case PaintTool.EllipseFill:
                _shapeAnchorX = px;
                _shapeAnchorY = py;
                _shapeCurrentX = px;
                _shapeCurrentY = py;
                _shapeActive = true;
                e.Pointer.Capture(this);
                InvalidateVisual();
                e.Handled = true;
                return;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (TryPosToPixel(e.GetPosition(this), out int px, out int py))
        {
            if (_hoverX != px || _hoverY != py)
            {
                _hoverX = px; _hoverY = py;
                InvalidateVisual();
            }
        }
        else if (_hoverX is not null)
        {
            _hoverX = null; _hoverY = null;
            InvalidateVisual();
        }

        if (_shapeActive)
        {
            if (TryPosToPixel(e.GetPosition(this), out px, out py))
            {
                _shapeCurrentX = px;
                _shapeCurrentY = py;
                InvalidateVisual();
            }
            return;
        }

        if (!_drawing) return;
        var layer = ActiveLayer;
        if (layer is null) return;
        if (!TryPosToPixel(e.GetPosition(this), out px, out py)) return;
        if (px == _lastX && py == _lastY) return;

        Line(layer, _lastX, _lastY, px, py);
        _lastX = px;
        _lastY = py;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_shapeActive && _shapeAnchorX is int ax && _shapeAnchorY is int ay
                         && _shapeCurrentX is int cx2 && _shapeCurrentY is int cy2)
        {
            var layer = ActiveLayer;
            if (layer is not null)
            {
                StrokeBegin?.Invoke(this, EventArgs.Empty);
                var data = layer.PixelData;
                var temp = new byte[PaintLayer.ByteCount];
                switch (Tool)
                {
                    case PaintTool.Line:      RasterLine(temp, ax, ay, cx2, cy2); break;
                    case PaintTool.Rect:      RasterRect(temp, ax, ay, cx2, cy2, false); break;
                    case PaintTool.RectFill:  RasterRect(temp, ax, ay, cx2, cy2, true); break;
                    case PaintTool.Ellipse:   RasterEllipse(temp, ax, ay, cx2, cy2, false); break;
                    case PaintTool.EllipseFill:RasterEllipse(temp, ax, ay, cx2, cy2, true); break;
                }
                var c = CurrentColor;
                for (int i = 0; i < PaintLayer.Size * PaintLayer.Size; i++)
                {
                    int b = i * 4;
                    if (temp[b + 3] == 0) continue;
                    data[b + 0] = c.R;
                    data[b + 1] = c.G;
                    data[b + 2] = c.B;
                    data[b + 3] = 255;
                }
                layer.SetPixels(data);
            }
        }

        _shapeActive = false;
        _shapeAnchorX = _shapeAnchorY = _shapeCurrentX = _shapeCurrentY = null;
        _drawing = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _drawing = false;
        _shapeActive = false;
        _shapeAnchorX = _shapeAnchorY = _shapeCurrentX = _shapeCurrentY = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            int newZoom = Zoom + (int)(e.Delta.Y * 2);
            Zoom = Math.Clamp(newZoom, 8, 64);
            e.Handled = true;
        }
    }

    // ── Drawing primitives ─────────────────────────────────
    void PaintPixel(PaintLayer layer, int x, int y)
    {
        var data = layer.PixelData;
        int i = (y * N + x) * 4;
        var c = CurrentColor;
        if (_effectiveTool == PaintTool.Eraser)
        {
            data[i + 0] = data[i + 1] = data[i + 2] = data[i + 3] = 0;
        }
        else
        {
            data[i + 0] = c.R;
            data[i + 1] = c.G;
            data[i + 2] = c.B;
            data[i + 3] = 255;
        }
        layer.SetPixels(data);
    }

    void Line(PaintLayer layer, int x0, int y0, int x1, int y1)
    {
        var temp = new byte[PaintLayer.ByteCount];
        RasterLine(temp, x0, y0, x1, y1);
        ApplyMask(layer, temp);
    }

    // Applies the mask to the layer: paints CurrentColor, or erases, depending
    // on the effective tool for the current stroke.
    void ApplyMask(PaintLayer layer, byte[] mask)
    {
        var data = layer.PixelData;
        bool erasing = _effectiveTool == PaintTool.Eraser;
        var c = CurrentColor;

        for (int i = 0; i < N * N; i++)
        {
            int b = i * 4;
            if (mask[b + 3] == 0) continue;   // untouched
            if (erasing)
            {
                data[b + 0] = data[b + 1] = data[b + 2] = data[b + 3] = 0;
            }
            else
            {
                data[b + 0] = c.R;
                data[b + 1] = c.G;
                data[b + 2] = c.B;
                data[b + 3] = 255;
            }
        }
        layer.SetPixels(data);
    }

    // Marks a pixel as "touched" in the raster mask. Always opaque white so the
    // apply step (ApplyMask) can distinguish touched-vs-untouched. The apply step
    // then writes the real colour (or erases) based on the current tool.
    void WritePixel(byte[] buf, int x, int y)
    {
        if (x < 0 || x >= N || y < 0 || y >= N) return;
        int i = (y * N + x) * 4;
        buf[i + 0] = 255;
        buf[i + 1] = 255;
        buf[i + 2] = 255;
        buf[i + 3] = 255;
    }

    void RasterLine(byte[] buf, int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            WritePixel(buf, x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int e2 = err * 2;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    void RasterRect(byte[] buf, int x0, int y0, int x1, int y1, bool fill)
    {
        int xmin = Math.Min(x0, x1), xmax = Math.Max(x0, x1);
        int ymin = Math.Min(y0, y1), ymax = Math.Max(y0, y1);
        if (fill)
        {
            for (int y = ymin; y <= ymax; y++)
                for (int x = xmin; x <= xmax; x++)
                    WritePixel(buf, x, y);
        }
        else
        {
            for (int x = xmin; x <= xmax; x++) { WritePixel(buf, x, ymin); WritePixel(buf, x, ymax); }
            for (int y = ymin; y <= ymax; y++) { WritePixel(buf, xmin, y); WritePixel(buf, xmax, y); }
        }
    }

    void RasterEllipse(byte[] buf, int x0, int y0, int x1, int y1, bool fill)
    {
        int xmin = Math.Min(x0, x1), xmax = Math.Max(x0, x1);
        int ymin = Math.Min(y0, y1), ymax = Math.Max(y0, y1);
        double cx = (xmin + xmax) / 2.0;
        double cy = (ymin + ymax) / 2.0;
        double rx = Math.Max(0.5, (xmax - xmin) / 2.0);
        double ry = Math.Max(0.5, (ymax - ymin) / 2.0);

        for (int y = ymin; y <= ymax; y++)
        {
            for (int x = xmin; x <= xmax; x++)
            {
                double dx = (x - cx) / rx;
                double dy = (y - cy) / ry;
                double d = dx * dx + dy * dy;
                if (fill)
                {
                    if (d <= 1.0) WritePixel(buf, x, y);
                }
                else
                {
                    // thin outline: inside but near edge
                    if (d <= 1.0 && d >= 0.55) WritePixel(buf, x, y);
                }
            }
        }
    }

    void FloodFill(PaintLayer layer, int sx, int sy, Color color)
    {
        var data = layer.PixelData;
        int target = (sy * N + sx) * 4;
        byte tr = data[target], tg = data[target + 1], tb = data[target + 2], ta = data[target + 3];
        if (tr == color.R && tg == color.G && tb == color.B && ta == 255) return;

        var visited = new bool[N * N];
        var stack = new Stack<(int, int)>();
        stack.Push((sx, sy));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || x >= N || y < 0 || y >= N) continue;
            int idx = y * N + x;
            if (visited[idx]) continue;
            int i = idx * 4;
            if (data[i] != tr || data[i + 1] != tg || data[i + 2] != tb || data[i + 3] != ta) continue;
            visited[idx] = true;
            data[i + 0] = color.R;
            data[i + 1] = color.G;
            data[i + 2] = color.B;
            data[i + 3] = 255;
            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }
        layer.SetPixels(data);
    }

    void PickColor(int x, int y)
    {
        int i = (y * N + x) * 4;
        double r = 0, g = 0, b = 0, a = 0;

        if (BasePixels is { Length: >= N * N * 4 } bp)
        {
            r = bp[i] / 255.0 * (bp[i + 3] / 255.0);
            g = bp[i + 1] / 255.0 * (bp[i + 3] / 255.0);
            b = bp[i + 2] / 255.0 * (bp[i + 3] / 255.0);
            a = bp[i + 3] / 255.0;
        }

        if (Layers is not null)
        {
            foreach (var layer in Layers)
            {
                if (!layer.Visible) continue;
                var px = layer.PixelData;
                double la = px[i + 3] / 255.0 * layer.Opacity;
                if (la <= 0) continue;
                r = px[i] / 255.0 * la + r * (1 - la);
                g = px[i + 1] / 255.0 * la + g * (1 - la);
                b = px[i + 2] / 255.0 * la + b * (1 - la);
                a = la + a * (1 - la);
            }
        }

        if (a <= 0) return;
        byte R = (byte)Math.Clamp(r / a * 255, 0, 255);
        byte G = (byte)Math.Clamp(g / a * 255, 0, 255);
        byte B = (byte)Math.Clamp(b / a * 255, 0, 255);
        ColorPicked?.Invoke(this, Color.FromRgb(R, G, B));
    }
}

internal static class ObservableExtensions
{
    public static IDisposable Subscribe<T>(this IObservable<T> obs, Action<T> onNext)
        => obs.Subscribe(new DelegateObserver<T>(onNext));

    sealed class DelegateObserver<T> : IObserver<T>
    {
        readonly Action<T> _onNext;
        public DelegateObserver(Action<T> onNext) => _onNext = onNext;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => _onNext(value);
    }
}