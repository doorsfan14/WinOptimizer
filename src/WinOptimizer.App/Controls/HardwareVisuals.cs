using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinOptimizer.Models;
using WinOptimizer.Services;

namespace WinOptimizer.Controls;

public sealed class CpuGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double?), typeof(CpuGauge), new FrameworkPropertyMetadata(null, OnValueChanged));
    private static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.Register(
        "AnimatedValue", typeof(double), typeof(CpuGauge), new FrameworkPropertyMetadata(-1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public static double IndicatorAngle(double value) => 135 + Math.Clamp(double.IsFinite(value) ? value : 0, 0, 100) * 2.7;

    public CpuGauge()
    {
        MinHeight = 104;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Focusable = false;
    }

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (CpuGauge)sender;
        if (e.NewValue is not double raw || !double.IsFinite(raw))
        {
            gauge.BeginAnimation(AnimatedValueProperty, null);
            gauge.SetValue(AnimatedValueProperty, -1d);
            return;
        }
        var target = Math.Clamp(raw, 0d, 100d);
        if (!SystemParameters.ClientAreaAnimation)
        {
            gauge.BeginAnimation(AnimatedValueProperty, null);
            gauge.SetValue(AnimatedValueProperty, target);
            return;
        }
        var current = (double)gauge.GetValue(AnimatedValueProperty);
        if (current < 0) current = target;
        gauge.BeginAnimation(AnimatedValueProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 20 || height < 20) return;
        var center = new Point(width / 2, height * .78);
        var radius = Math.Min(width * .42, height * .66);
        if (radius < 15) return;
        var muted = Brush("TextMuted", "#94A8B0");
        var grid = Brush("ChartGrid", "#293941");
        var accent = Brush("Accent", "#77D8CA");
        var warning = Brush("Warning", "#EABF78");
        var danger = Brush("Danger", "#EF8F8F");
        var current = (double)GetValue(AnimatedValueProperty);

        for (var tick = 0; tick <= 100; tick += 4)
        {
            var angle = Degrees(IndicatorAngle(tick));
            var major = tick % 20 == 0;
            var outer = PointAt(center, radius, angle);
            var inner = PointAt(center, radius - (major ? 11 : 5), angle);
            var active = current >= 0 && tick <= current;
            var tickBrush = !active ? grid : current >= 85 ? danger : current >= 65 ? warning : accent;
            dc.DrawLine(new Pen(tickBrush, major ? 2 : 1.15), inner, outer);
        }

        if (current >= 0)
        {
            var needleTip = PointAt(center, radius * .78, Degrees(IndicatorAngle(current)));
            dc.DrawLine(new Pen(Brush("TextPrimary", "#F0F6F5"), 2), center, needleTip);
            dc.DrawEllipse(accent, null, center, 4.5, 4.5);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => ThemeService.ThemeChanged += InvalidateVisual;
    private void OnUnloaded(object sender, RoutedEventArgs e) => ThemeService.ThemeChanged -= InvalidateVisual;
    private Brush Brush(string key, string fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
    private static double Degrees(double value) => value * Math.PI / 180;
    private static Point PointAt(Point center, double radius, double radians) => new(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
}

public sealed class FanVisual : FrameworkElement
{
    public static readonly DependencyProperty RpmProperty = DependencyProperty.Register(
        nameof(Rpm), typeof(double?), typeof(FanVisual), new FrameworkPropertyMetadata(null, OnSpeedChanged));
    public static readonly DependencyProperty FanStateProperty = DependencyProperty.Register(
        nameof(FanState), typeof(WinOptimizer.Models.FanState), typeof(FanVisual), new FrameworkPropertyMetadata(WinOptimizer.Models.FanState.NotInitialized, FrameworkPropertyMetadataOptions.AffectsRender, OnSpeedChanged));

    private readonly DispatcherTimer _timer;
    private double _angle;
    private DateTime _lastFrame = DateTime.UtcNow;
    public double? Rpm { get => (double?)GetValue(RpmProperty); set => SetValue(RpmProperty, value); }
    public WinOptimizer.Models.FanState FanState { get => (WinOptimizer.Models.FanState)GetValue(FanStateProperty); set => SetValue(FanStateProperty, value); }
    public static double AngularSpeedDegreesPerSecond(double rpm) =>
        !double.IsFinite(rpm) || rpm <= 0 ? 0 : Math.Min(rpm * .18, 720);
    public static bool ShouldAnimate(double? rpm, WinOptimizer.Models.FanState state, bool animationsEnabled) =>
        animationsEnabled && state == WinOptimizer.Models.FanState.Available && rpm is > 0 && double.IsFinite(rpm.Value);

    public FanVisual()
    {
        MinWidth = 96; MinHeight = 96;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        Loaded += OnLoaded; Unloaded += OnUnloaded;
    }

    private static void OnSpeedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var fan = (FanVisual)sender;
        fan.InvalidateVisual();
        fan.UpdateAnimation();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged += InvalidateVisual;
        UpdateAnimation();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged -= InvalidateVisual;
        _timer.Stop();
    }

    private void UpdateAnimation()
    {
        if (IsLoaded && ShouldAnimate(Rpm, FanState, SystemParameters.ClientAreaAnimation))
        {
            _lastFrame = DateTime.UtcNow;
            _timer.Start();
        }
        else _timer.Stop();
    }

    private void Tick()
    {
        if (Rpm is not { } rpm || rpm <= 0 || !IsLoaded) { _timer.Stop(); return; }
        var now = DateTime.UtcNow;
        var elapsed = Math.Clamp((now - _lastFrame).TotalSeconds, 0, .1);
        _lastFrame = now;
        // Real RPM changes the direction's angular speed. Motion is intentionally slowed for visibility.
        _angle = (_angle + AngularSpeedDegreesPerSecond(rpm) * elapsed) % 360;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 20) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size * .43;
        var accent = Brush("Accent", "#77D8CA");
        var line = Brush("Line", "#293941");
        var surface = Brush("SurfaceRaised", "#19262D");
        var blade = Brush("AccentSoft", "#213D3D");
        dc.DrawEllipse(surface, new Pen(line, 1.5), center, radius, radius);
        dc.DrawEllipse(null, new Pen(line, 1), center, radius * .83, radius * .83);
        for (var index = 0; index < 24; index++)
        {
            var angle = index * Math.PI / 12;
            var start = radius * .88;
            var end = index % 3 == 0 ? radius * .97 : radius * .93;
            dc.DrawLine(new Pen(index % 3 == 0 ? accent : line, index % 3 == 0 ? 1.5 : 1),
                new Point(center.X + Math.Cos(angle) * start, center.Y + Math.Sin(angle) * start),
                new Point(center.X + Math.Cos(angle) * end, center.Y + Math.Sin(angle) * end));
        }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, -9), true, true);
            context.BezierTo(new Point(3, -25), new Point(20, -44), new Point(32, -39), true, false);
            context.BezierTo(new Point(42, -32), new Point(31, -13), new Point(9, -7), true, false);
            context.LineTo(new Point(0, -9), true, false);
        }
        geometry.Freeze();
        dc.PushTransform(new TranslateTransform(center.X, center.Y));
        dc.PushTransform(new ScaleTransform(radius / 45, radius / 45));
        dc.PushTransform(new RotateTransform(_angle));
        for (var bladeIndex = 0; bladeIndex < 5; bladeIndex++)
        {
            dc.PushTransform(new RotateTransform(bladeIndex * 72));
            dc.DrawGeometry(blade, new Pen(accent, 1.2), geometry);
            dc.Pop();
        }
        dc.Pop(); dc.Pop(); dc.Pop();
        dc.DrawEllipse(accent, new Pen(surface, 2), center, radius * .18, radius * .18);
    }

    private Brush Brush(string key, string fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
}

public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(ObservableCollection<double>), typeof(HistoryChart), new FrameworkPropertyMetadata(null, OnValuesChanged));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(HistoryChart), new FrameworkPropertyMetadata(Brushes.MediumTurquoise, FrameworkPropertyMetadataOptions.AffectsRender));
    private ObservableCollection<double>? _subscribed;
    private int _selectedIndex = -1;
    public ObservableCollection<double>? Values { get => (ObservableCollection<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    public HistoryChart()
    {
        MinHeight = 64; Focusable = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static double SampleX(int index, int count, double width, double padding = 2)
    {
        var plotWidth = Math.Max(0, width - padding * 2);
        return count <= 1 ? width / 2 : padding + Math.Clamp(index, 0, count - 1) * plotWidth / (count - 1);
    }

    private static void OnValuesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var chart = (HistoryChart)sender;
        if (chart._subscribed is not null) chart._subscribed.CollectionChanged -= chart.OnCollectionChanged;
        chart._subscribed = e.NewValue as ObservableCollection<double>;
        if (chart.IsLoaded && chart._subscribed is not null) chart._subscribed.CollectionChanged += chart.OnCollectionChanged;
        chart._selectedIndex = -1;
        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged += InvalidateVisual;
        if (_subscribed is not null) _subscribed.CollectionChanged += OnCollectionChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged -= InvalidateVisual;
        if (_subscribed is not null) _subscribed.CollectionChanged -= OnCollectionChanged;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth; var height = ActualHeight;
        if (width < 24 || height < 20) return;
        var left = 2d; var top = 4d; var plotWidth = width - 4; var plotHeight = height - 8;
        var grid = TryFindResource("ChartGrid") as Brush ?? new SolidColorBrush(Color.FromRgb(41, 57, 65));
        for (var row = 0; row <= 2; row++)
        {
            var y = top + plotHeight * row / 2;
            dc.DrawLine(new Pen(grid, .8), new Point(left, y), new Point(left + plotWidth, y));
        }
        var points = Values?.ToArray() ?? [];
        if (points.Length == 0) return;
        Point Map(int index, double value) => new(SampleX(index, points.Length, width, left),
            top + plotHeight * (1 - Math.Clamp(value, 0, 100) / 100d));
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(Map(0, points[0]), false, false);
            for (var i = 1; i < points.Length; i++) ctx.LineTo(Map(i, points[i]), true, false);
        }
        line.Freeze();
        var pen = new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, line);
        if (_selectedIndex >= 0 && _selectedIndex < points.Length)
        {
            var point = Map(_selectedIndex, points[_selectedIndex]);
            dc.DrawLine(new Pen(grid, 1), new Point(point.X, top), new Point(point.X, top + plotHeight));
            dc.DrawEllipse(Stroke, new Pen(Brushes.White, 1), point, 3.5, 3.5);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var values = Values;
        if (values is null || values.Count == 0) return;
        var index = values.Count == 1 ? 0 : (int)Math.Round(Math.Clamp(e.GetPosition(this).X / Math.Max(ActualWidth, 1), 0, 1) * (values.Count - 1));
        SetSelection(index);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsKeyboardFocused) SetSelection(-1);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (Values is { Count: > 0 } values) SetSelection(values.Count - 1);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Values is not { Count: > 0 } values) return;
        if (e.Key == Key.Left) { SetSelection(Math.Max(0, _selectedIndex - 1)); e.Handled = true; }
        else if (e.Key == Key.Right) { SetSelection(Math.Min(values.Count - 1, _selectedIndex + 1)); e.Handled = true; }
    }

    private void SetSelection(int index)
    {
        if (_selectedIndex == index) return;
        _selectedIndex = index;
        ToolTip = index >= 0 && Values is { } values && index < values.Count
            ? $"{values[index]:0.#}% · sample {index + 1} of {values.Count}"
            : null;
        InvalidateVisual();
    }
}
