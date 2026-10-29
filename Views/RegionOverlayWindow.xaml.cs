using System.Windows.Interop;
using ScreenshotApp.Models;
using ScreenshotApp.Services;

namespace ScreenshotApp.Views;

public partial class RegionOverlayWindow : Window
{
    readonly MonitorInfo _monitor;
    readonly bool _recording;
    readonly TaskCompletionSource<OverlayPick?> _done = new();
    readonly System.Windows.Shapes.Ellipse[] _handles;
    PixelRect? _selection;
    PixelRect _dragRect;
    PixelPoint _anchor;
    DragKind _drag = DragKind.None;
    bool _completed;

    public RegionOverlayWindow(MonitorInfo monitor, bool recording, AppSettings settings)
    {
        _monitor = monitor;
        _recording = recording;
        InitializeComponent();
        _handles = Enumerable.Range(0, 8).Select(_ => new System.Windows.Shapes.Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        }).ToArray();
        foreach (var handle in _handles)
            Surface.Children.Add(handle);

        Options.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        if (recording)
        {
            Options.Load(settings);
            Options.ChoiceChanged += (_, _) => Redraw();
            OkButton.Content = "开始录像";
            HintText.Text = "拖出区域后可调整边缘。Enter 开始，Esc 取消。";
        }
        else
        {
            HintText.Text = "拖出区域后可调整边缘。Enter 完成，Esc 取消。";
        }

        Left = monitor.Bounds.X;
        Top = monitor.Bounds.Y;
        Width = Math.Max(100, monitor.Bounds.Width);
        Height = Math.Max(100, monitor.Bounds.Height);
        SourceInitialized += (_, _) => Fit();
        DpiChanged += (_, _) => Fit();
        SizeChanged += (_, _) => Redraw();
        Closed += (_, _) =>
        {
            if (!_completed)
                _done.TrySetResult(null);
        };
    }

    public Task<OverlayPick?> PickAsync()
    {
        Show();
        Activate();
        Focus();
        return _done.Task;
    }

    void Fit()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        NativeMethods.MakeToolWindow(hwnd);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            _monitor.Bounds.X,
            _monitor.Bounds.Y,
            _monitor.Bounds.Width,
            _monitor.Bounds.Height,
            NativeMethods.SwpShowWindow);

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return;
        Matrix matrix = source.CompositionTarget.TransformFromDevice;
        Point topLeft = matrix.Transform(new Point(_monitor.Bounds.X, _monitor.Bounds.Y));
        Point bottomRight = matrix.Transform(new Point(_monitor.Bounds.Right, _monitor.Bounds.Bottom));
        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(1, bottomRight.X - topLeft.X);
        Height = Math.Max(1, bottomRight.Y - topLeft.Y);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (InsideToolbar(e.OriginalSource as DependencyObject))
            return;
        PixelPoint point = ToPixel(e.GetPosition(this));
        _anchor = point;
        _drag = HitTest(point);
        _dragRect = _selection ?? new PixelRect(point.X, point.Y, 0, 0);
        if (_drag == DragKind.Create)
            _selection = new PixelRect(point.X, point.Y, 0, 0);
        CaptureMouse();
        Redraw();
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        PixelPoint point = ToPixel(e.GetPosition(this));
        if (!IsMouseCaptured)
        {
            Cursor = CursorFor(HitTest(point));
            return;
        }

        PixelRect bounds = _monitor.Bounds;
        switch (_drag)
        {
            case DragKind.Create:
                _selection = PixelRect.FromCorners(_anchor.X, _anchor.Y, point.X, point.Y).Intersect(bounds);
                break;
            case DragKind.Move:
                int dx = point.X - _anchor.X;
                int dy = point.Y - _anchor.Y;
                int x = Math.Clamp(_dragRect.X + dx, bounds.X, Math.Max(bounds.X, bounds.Right - _dragRect.Width));
                int y = Math.Clamp(_dragRect.Y + dy, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - _dragRect.Height));
                _selection = new PixelRect(x, y, _dragRect.Width, _dragRect.Height);
                break;
            default:
                int left = _dragRect.X, top = _dragRect.Y, right = _dragRect.Right, bottom = _dragRect.Bottom;
                if (_drag is DragKind.West or DragKind.NorthWest or DragKind.SouthWest)
                    left = point.X;
                if (_drag is DragKind.East or DragKind.NorthEast or DragKind.SouthEast)
                    right = point.X;
                if (_drag is DragKind.North or DragKind.NorthWest or DragKind.NorthEast)
                    top = point.Y;
                if (_drag is DragKind.South or DragKind.SouthWest or DragKind.SouthEast)
                    bottom = point.Y;
                _selection = PixelRect.FromCorners(left, top, right, bottom).Intersect(bounds);
                break;
        }

        Redraw();
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!IsMouseCaptured)
            return;
        ReleaseMouseCapture();
        if (_selection is PixelRect rect && (rect.Width < 4 || rect.Height < 4))
            _selection = null;
        _drag = DragKind.None;
        Redraw();
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
            return;
        }

        if (_selection is not PixelRect current)
            return;
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        int dx = e.Key switch
        {
            Key.Left => -step,
            Key.Right => step,
            _ => 0
        };
        int dy = e.Key switch
        {
            Key.Up => -step,
            Key.Down => step,
            _ => 0
        };
        if (dx == 0 && dy == 0)
            return;
        PixelRect bounds = _monitor.Bounds;
        int x = Math.Clamp(current.X + dx, bounds.X, Math.Max(bounds.X, bounds.Right - current.Width));
        int y = Math.Clamp(current.Y + dy, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - current.Height));
        _selection = new PixelRect(x, y, current.Width, current.Height);
        Redraw();
        e.Handled = true;
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (!InsideToolbar(e.OriginalSource as DependencyObject))
            Confirm();
    }

    void Ok_Click(object sender, RoutedEventArgs e) => Confirm();
    void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();

    void Confirm()
    {
        if (_selection is not PixelRect rect || rect.Width < 8 || rect.Height < 8)
            return;
        _completed = true;
        _done.TrySetResult(new OverlayPick
        {
            Region = rect,
            Recording = _recording ? Options.Choice : null
        });
        Close();
    }

    void Cancel()
    {
        _completed = true;
        _done.TrySetResult(null);
        Close();
    }

    void Redraw()
    {
        double width = Surface.ActualWidth;
        double height = Surface.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        if (_selection is not PixelRect rect || rect.Width <= 0 || rect.Height <= 0)
        {
            Place(DimTop, 0, 0, width, height);
            Place(DimLeft, 0, 0, 0, 0);
            Place(DimRight, 0, 0, 0, 0);
            Place(DimBottom, 0, 0, 0, 0);
            SelectionBox.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            foreach (var handle in _handles)
                handle.Visibility = Visibility.Collapsed;
            return;
        }

        Rect dip = ToDip(rect);
        Place(DimTop, 0, 0, width, Math.Max(0, dip.Top));
        Place(DimLeft, 0, dip.Top, Math.Max(0, dip.Left), dip.Height);
        Place(DimRight, dip.Right, dip.Top, Math.Max(0, width - dip.Right), dip.Height);
        Place(DimBottom, 0, dip.Bottom, width, Math.Max(0, height - dip.Bottom));
        SelectionBox.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBox, dip.Left);
        Canvas.SetTop(SelectionBox, dip.Top);
        SelectionBox.Width = Math.Max(0, dip.Width);
        SelectionBox.Height = Math.Max(0, dip.Height);

        Point[] points =
        {
            dip.TopLeft, dip.TopRight, dip.BottomRight, dip.BottomLeft,
            new Point(dip.Left + dip.Width / 2, dip.Top),
            new Point(dip.Right, dip.Top + dip.Height / 2),
            new Point(dip.Left + dip.Width / 2, dip.Bottom),
            new Point(dip.Left, dip.Top + dip.Height / 2)
        };
        for (int i = 0; i < _handles.Length; i++)
        {
            _handles[i].Visibility = Visibility.Visible;
            Canvas.SetLeft(_handles[i], points[i].X - 5);
            Canvas.SetTop(_handles[i], points[i].Y - 5);
        }

        string text = $"{rect.Width} × {rect.Height}";
        if (_recording)
        {
            (int outW, int outH) = VideoSizing.Fit(rect.Width, rect.Height, Options.Choice.Cap);
            if (outW != rect.Width || outH != rect.Height)
                text += $"  →  {outW} × {outH}";
        }

        SizeLabel.Text = text;
        SizeLabel.Visibility = Visibility.Visible;
        SizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double labelTop = dip.Bottom + 8;
        if (labelTop + SizeLabel.DesiredSize.Height > height - 8)
            labelTop = Math.Max(0, dip.Top - SizeLabel.DesiredSize.Height - 8);
        Canvas.SetLeft(SizeLabel, Math.Max(0, dip.Left));
        Canvas.SetTop(SizeLabel, labelTop);
    }

    PixelPoint ToPixel(Point dip)
    {
        var source = PresentationSource.FromVisual(this);
        Point device = source?.CompositionTarget == null
            ? dip
            : source.CompositionTarget.TransformToDevice.Transform(dip);
        int x = _monitor.Bounds.X + (int)Math.Round(device.X);
        int y = _monitor.Bounds.Y + (int)Math.Round(device.Y);
        x = Math.Clamp(x, _monitor.Bounds.X, _monitor.Bounds.Right);
        y = Math.Clamp(y, _monitor.Bounds.Y, _monitor.Bounds.Bottom);
        return new PixelPoint(x, y);
    }

    Rect ToDip(PixelRect rect)
    {
        var source = PresentationSource.FromVisual(this);
        Point origin = new(rect.X - _monitor.Bounds.X, rect.Y - _monitor.Bounds.Y);
        Point far = new(rect.Right - _monitor.Bounds.X, rect.Bottom - _monitor.Bounds.Y);
        if (source?.CompositionTarget != null)
        {
            Matrix matrix = source.CompositionTarget.TransformFromDevice;
            origin = matrix.Transform(origin);
            far = matrix.Transform(far);
        }

        return new Rect(origin, far);
    }

    DragKind HitTest(PixelPoint point)
    {
        if (_selection is not PixelRect rect || rect.Width < 4 || rect.Height < 4)
            return DragKind.Create;
        int slop = Math.Max(8, (int)Math.Round(12 * DeviceScale()));
        if (Near(point, rect.X, rect.Y, slop)) return DragKind.NorthWest;
        if (Near(point, rect.Right, rect.Y, slop)) return DragKind.NorthEast;
        if (Near(point, rect.X, rect.Bottom, slop)) return DragKind.SouthWest;
        if (Near(point, rect.Right, rect.Bottom, slop)) return DragKind.SouthEast;
        if (Math.Abs(point.Y - rect.Y) <= slop && point.X >= rect.X && point.X <= rect.Right) return DragKind.North;
        if (Math.Abs(point.Y - rect.Bottom) <= slop && point.X >= rect.X && point.X <= rect.Right) return DragKind.South;
        if (Math.Abs(point.X - rect.X) <= slop && point.Y >= rect.Y && point.Y <= rect.Bottom) return DragKind.West;
        if (Math.Abs(point.X - rect.Right) <= slop && point.Y >= rect.Y && point.Y <= rect.Bottom) return DragKind.East;
        if (rect.Contains(point.X, point.Y)) return DragKind.Move;
        return DragKind.Create;
    }

    double DeviceScale()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
    }

    static bool Near(PixelPoint point, int x, int y, int slop) =>
        Math.Abs(point.X - x) <= slop && Math.Abs(point.Y - y) <= slop;

    static Cursor CursorFor(DragKind kind) => kind switch
    {
        DragKind.Move => Cursors.SizeAll,
        DragKind.North or DragKind.South => Cursors.SizeNS,
        DragKind.East or DragKind.West => Cursors.SizeWE,
        DragKind.NorthEast or DragKind.SouthWest => Cursors.SizeNESW,
        DragKind.NorthWest or DragKind.SouthEast => Cursors.SizeNWSE,
        _ => Cursors.Cross
    };

    static bool InsideToolbar(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement { Name: "Toolbar" })
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    static void Place(System.Windows.Shapes.Rectangle rectangle, double x, double y, double width, double height)
    {
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
    }

    enum DragKind
    {
        None,
        Create,
        Move,
        North,
        South,
        East,
        West,
        NorthEast,
        NorthWest,
        SouthEast,
        SouthWest
    }
}
