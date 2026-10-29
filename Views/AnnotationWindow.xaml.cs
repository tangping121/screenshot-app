using ScreenshotApp.Services;

namespace ScreenshotApp.Views;

internal partial class AnnotationWindow : Window
{
    readonly CaptureController _host;
    readonly WriteableBitmap _bitmap;
    readonly Stack<IEdit> _undo = new();
    readonly Stack<IEdit> _redo = new();
    readonly Button[] _toolButtons;
    ToolKind _tool = ToolKind.Pen;
    Color _color = Color.FromRgb(226, 61, 61);
    bool _thick;
    bool _drawing;
    Point _start;
    System.Windows.Shapes.Polyline? _pen;

    public AnnotationWindow(BitmapSource source, CaptureController host)
    {
        _host = host;
        InitializeComponent();
        _bitmap = ToEditable(source);
        Photo.Source = _bitmap;
        Stage.Width = _bitmap.PixelWidth;
        Stage.Height = _bitmap.PixelHeight;
        _toolButtons = new[] { PenButton, ArrowButton, RectButton, TextButton, MosaicButton };
        Highlight(PenButton);
        HighlightWidth(ThinButton);
        Loaded += (_, _) =>
        {
            FitToScreen();
            CopyToClipboard(_bitmap);
        };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && Keyboard.FocusedElement is TextBox)
        {
            Focus();
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBox)
            return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Z)
        {
            Undo();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.S)
        {
            Save();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.C)
        {
            CopyToClipboard(Flatten());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_undo.Count > 0)
        {
            MessageBoxResult answer = MessageBox.Show("这张截图还没有保存。要保存吗？", "标注截图", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
                e.Cancel = true;
            else if (answer == MessageBoxResult.Yes && !Save())
                e.Cancel = true;
        }
        base.OnClosing(e);
    }

    void FitToScreen()
    {
        double maxW = SystemParameters.WorkArea.Width * 0.9;
        double maxH = SystemParameters.WorkArea.Height * 0.86;
        Width = Math.Min(maxW, _bitmap.PixelWidth + 48);
        Height = Math.Min(maxH, _bitmap.PixelHeight + 140);
    }

    void Tool_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        _tool = Enum.Parse<ToolKind>((string)button.Tag);
        Highlight(button);
    }

    void Thin_Click(object sender, RoutedEventArgs e)
    {
        _thick = false;
        HighlightWidth(ThinButton);
    }

    void Thick_Click(object sender, RoutedEventArgs e)
    {
        _thick = true;
        HighlightWidth(ThickButton);
    }

    void Color_Click(object sender, RoutedEventArgs e)
    {
        _color = (Color)ColorConverter.ConvertFromString((string)((Button)sender).Tag);
    }

    void Stage_Down(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            return;
        Point point = Clamp(e.GetPosition(Stage));
        _start = point;
        _drawing = true;
        Stage.CaptureMouse();
        if (_tool == ToolKind.Pen)
        {
            _pen = new System.Windows.Shapes.Polyline
            {
                Stroke = Brush(),
                StrokeThickness = Thickness(),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            _pen.Points.Add(point);
            Ink.Children.Add(_pen);
        }
        else if (_tool == ToolKind.Text)
        {
            var box = new TextBox
            {
                Text = "文字",
                Foreground = Brush(),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = _thick ? 32 : 18,
                MinWidth = 48,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                CaretBrush = Brushes.White
            };
            Canvas.SetLeft(box, point.X);
            Canvas.SetTop(box, point.Y);
            Ink.Children.Add(box);
            Commit(new ElementEdit(Ink, box));
            box.Loaded += (_, _) =>
            {
                box.Focus();
                box.SelectAll();
            };
            _drawing = false;
            Stage.ReleaseMouseCapture();
        }
        e.Handled = true;
    }

    void Stage_Move(object sender, MouseEventArgs e)
    {
        if (!_drawing || e.LeftButton != MouseButtonState.Pressed)
            return;
        Point point = Clamp(e.GetPosition(Stage));
        if (_tool == ToolKind.Pen && _pen != null)
        {
            Point last = _pen.Points[^1];
            if (Math.Abs(last.X - point.X) + Math.Abs(last.Y - point.Y) >= 1)
                _pen.Points.Add(point);
        }
    }

    void Stage_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing)
            return;
        _drawing = false;
        if (Stage.IsMouseCaptured)
            Stage.ReleaseMouseCapture();
        Point point = Clamp(e.GetPosition(Stage));
        switch (_tool)
        {
            case ToolKind.Pen when _pen != null:
                Commit(new ElementEdit(Ink, _pen));
                _pen = null;
                break;
            case ToolKind.Arrow:
                AddShape(CreateArrow(_start, point));
                break;
            case ToolKind.Rect:
                AddShape(CreateRect(_start, point));
                break;
            case ToolKind.Mosaic:
                ApplyMosaic(_start, point);
                break;
        }
    }

    void AddShape(UIElement? element)
    {
        if (element == null)
            return;
        Ink.Children.Add(element);
        Commit(new ElementEdit(Ink, element));
    }

    System.Windows.Shapes.Shape? CreateRect(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(a.X - b.X);
        double h = Math.Abs(a.Y - b.Y);
        if (w < 2 || h < 2)
            return null;
        var rect = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = h,
            Stroke = Brush(),
            StrokeThickness = Thickness(),
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        return rect;
    }

    UIElement? CreateArrow(Point a, Point b)
    {
        Vector delta = b - a;
        if (delta.Length < 4)
            return null;
        delta.Normalize();
        Vector normal = new(-delta.Y, delta.X);
        double head = Thickness() * 4 + 14;
        Point tip = b;
        Point left = b - delta * head + normal * (head * 0.45);
        Point right = b - delta * head - normal * (head * 0.45);
        Point neck = b - delta * (head * 0.55);
        var group = new Canvas();
        group.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = a.X,
            Y1 = a.Y,
            X2 = neck.X,
            Y2 = neck.Y,
            Stroke = Brush(),
            StrokeThickness = Thickness(),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        group.Children.Add(new System.Windows.Shapes.Polygon
        {
            Fill = Brush(),
            Points = new PointCollection { tip, left, right }
        });
        return group;
    }

    void ApplyMosaic(Point a, Point b)
    {
        int x = (int)Math.Min(a.X, b.X);
        int y = (int)Math.Min(a.Y, b.Y);
        int w = (int)Math.Abs(a.X - b.X);
        int h = (int)Math.Abs(a.Y - b.Y);
        if (w < 4 || h < 4)
            return;
        x = Math.Clamp(x, 0, _bitmap.PixelWidth - 1);
        y = Math.Clamp(y, 0, _bitmap.PixelHeight - 1);
        w = Math.Min(w, _bitmap.PixelWidth - x);
        h = Math.Min(h, _bitmap.PixelHeight - y);
        int stride = _bitmap.PixelWidth * 4;
        byte[] pixels = new byte[_bitmap.PixelHeight * stride];
        _bitmap.CopyPixels(pixels, stride, 0);
        byte[] before = CopyRect(pixels, stride, x, y, w, h);
        int block = _thick ? 22 : 12;
        for (int by = y; by < y + h; by += block)
        {
            for (int bx = x; bx < x + w; bx += block)
            {
                int bw = Math.Min(block, x + w - bx);
                int bh = Math.Min(block, y + h - by);
                long blue = 0, green = 0, red = 0, alpha = 0;
                int count = bw * bh;
                for (int yy = 0; yy < bh; yy++)
                {
                    for (int xx = 0; xx < bw; xx++)
                    {
                        int index = ((by + yy) * _bitmap.PixelWidth + (bx + xx)) * 4;
                        blue += pixels[index];
                        green += pixels[index + 1];
                        red += pixels[index + 2];
                        alpha += pixels[index + 3];
                    }
                }

                byte bb = (byte)(blue / count);
                byte gg = (byte)(green / count);
                byte rr = (byte)(red / count);
                byte aa = (byte)(alpha / count);
                for (int yy = 0; yy < bh; yy++)
                {
                    for (int xx = 0; xx < bw; xx++)
                    {
                        int index = ((by + yy) * _bitmap.PixelWidth + (bx + xx)) * 4;
                        pixels[index] = bb;
                        pixels[index + 1] = gg;
                        pixels[index + 2] = rr;
                        pixels[index + 3] = aa;
                    }
                }
            }
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), pixels, stride, 0);
        byte[] after = CopyRect(pixels, stride, x, y, w, h);
        Commit(new MosaicEdit(_bitmap, x, y, w, h, before, after));
    }

    static byte[] CopyRect(byte[] pixels, int stride, int x, int y, int w, int h)
    {
        byte[] rect = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(pixels, (y + row) * stride + x * 4, rect, row * w * 4, w * 4);
        return rect;
    }

    void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    void Undo()
    {
        if (_undo.Count == 0)
            return;
        IEdit edit = _undo.Pop();
        edit.Undo();
        _redo.Push(edit);
    }

    void Redo()
    {
        if (_redo.Count == 0)
            return;
        IEdit edit = _redo.Pop();
        edit.Redo();
        _undo.Push(edit);
    }

    void Commit(IEdit edit)
    {
        _undo.Push(edit);
        _redo.Clear();
        if (_undo.Count > 30)
        {
            var kept = _undo.Take(30).Reverse().ToArray();
            _undo.Clear();
            foreach (IEdit item in kept)
                _undo.Push(item);
        }
    }

    void Copy_Click(object sender, RoutedEventArgs e) => CopyToClipboard(Flatten());

    void Save_Click(object sender, RoutedEventArgs e) => Save();

    bool Save()
    {
        try
        {
            _host.SaveImage(Flatten());
            _undo.Clear();
            StatusText.Text = "已保存，并复制到剪贴板，可以直接粘贴。";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    void CopyToClipboard(BitmapSource image)
    {
        try
        {
            _host.CopyImage(image);
            StatusText.Text = "已复制到剪贴板，可以直接粘贴。";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "复制失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Pin_Click(object sender, RoutedEventArgs e)
    {
        try { _host.PinImage(Flatten()); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "贴到桌面失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    BitmapSource Flatten()
    {
        Focus();
        UpdateLayout();
        int width = _bitmap.PixelWidth;
        int height = _bitmap.PixelHeight;
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(Stage);
        target.Freeze();
        return target;
    }

    SolidColorBrush Brush()
    {
        var brush = new SolidColorBrush(_color);
        brush.Freeze();
        return brush;
    }

    double Thickness() => _thick ? 8 : 3;

    Point Clamp(Point point) => new(
        Math.Clamp(point.X, 0, _bitmap.PixelWidth),
        Math.Clamp(point.Y, 0, _bitmap.PixelHeight));

    void Highlight(Button selected)
    {
        foreach (Button button in _toolButtons)
        {
            button.Background = button == selected ? new SolidColorBrush(Color.FromRgb(219, 234, 254)) : Brushes.White;
            button.BorderBrush = button == selected ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(208, 213, 221));
        }
    }

    void HighlightWidth(Button selected)
    {
        foreach (Button button in new[] { ThinButton, ThickButton })
        {
            button.Background = button == selected ? new SolidColorBrush(Color.FromRgb(219, 234, 254)) : Brushes.White;
            button.BorderBrush = button == selected ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(208, 213, 221));
        }
    }

    static WriteableBitmap ToEditable(BitmapSource source)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        return new WriteableBitmap(bgra);
    }

    interface IEdit
    {
        void Undo();
        void Redo();
    }

    sealed class ElementEdit(Canvas canvas, UIElement element) : IEdit
    {
        public void Undo() => canvas.Children.Remove(element);
        public void Redo()
        {
            if (!canvas.Children.Contains(element))
                canvas.Children.Add(element);
        }
    }

    sealed class MosaicEdit(WriteableBitmap bitmap, int x, int y, int w, int h, byte[] before, byte[] after) : IEdit
    {
        public void Undo() => Write(before);
        public void Redo() => Write(after);

        void Write(byte[] rect)
        {
            int stride = w * 4;
            bitmap.WritePixels(new Int32Rect(x, y, w, h), rect, stride, 0);
        }
    }

    enum ToolKind
    {
        Pen,
        Arrow,
        Rect,
        Text,
        Mosaic
    }
}
