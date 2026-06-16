using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GeneralHostFrontend.Core.Hmi;

namespace GeneralHostFrontend.ViewModels.Hmi;

public sealed partial class HmiWidgetViewModel : ViewModelBase
{
    private static Func<string, string?>? s_resourceResolver;
    private readonly Dictionary<string, string> _bindings;
    private readonly Dictionary<string, string> _properties;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    [ObservableProperty]
    private int _zIndex;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isPrimarySelected;

    [ObservableProperty]
    private string _displayValue = "-";

    [ObservableProperty]
    private bool _state;

    public HmiWidgetViewModel(HmiWidgetDefinition definition)
    {
        Id = definition.Id;
        Kind = definition.Kind;
        Title = definition.Title;
        X = definition.X;
        Y = definition.Y;
        Width = definition.Width;
        Height = definition.Height;
        ZIndex = definition.ZIndex;
        _bindings = new Dictionary<string, string>(definition.Bindings ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        _properties = new Dictionary<string, string>(definition.Properties ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        Events = definition.Events ?? Array.Empty<HmiEventDefinition>();
        PermissionKey = definition.PermissionKey;
    }

    public string Id { get; }

    public HmiWidgetKind Kind { get; }

    public string KindText => Kind.ToString();

    public bool IsValueText => Kind is HmiWidgetKind.ValueText;

    public bool IsStateIndicator => Kind is HmiWidgetKind.StateIndicator;

    public bool IsCommandButton => Kind is HmiWidgetKind.CommandButton;

    public bool IsText => Kind is HmiWidgetKind.Text;

    public bool IsInputBox => Kind is HmiWidgetKind.InputBox;

    public bool IsSwitchButton => Kind is HmiWidgetKind.SwitchButton;

    public bool IsImage => Kind is HmiWidgetKind.Image;

    public bool IsRectangle => Kind is HmiWidgetKind.Rectangle;

    public bool IsEllipse => Kind is HmiWidgetKind.Ellipse;

    public bool IsLine => Kind is HmiWidgetKind.Line;

    public bool IsContainer => Kind is HmiWidgetKind.Container;

    public bool IsProgressBar => Kind is HmiWidgetKind.ProgressBar;

    public bool IsTrendChart => Kind is HmiWidgetKind.TrendChart;

    public bool IsAlarmList => Kind is HmiWidgetKind.AlarmList;

    public string Title { get; set; }

    public string Text
    {
        get => GetProperty(HmiPropertyNames.Text, Title);
        set
        {
            SetPropertyValue(HmiPropertyNames.Text, value);
            OnPropertyChanged();
        }
    }

    public string Unit
    {
        get => GetProperty(HmiPropertyNames.Unit, string.Empty);
        set
        {
            SetPropertyValue(HmiPropertyNames.Unit, value);
            OnPropertyChanged();
        }
    }

    public string Format
    {
        get => GetProperty(HmiPropertyNames.Format, string.Empty);
        set
        {
            SetPropertyValue(HmiPropertyNames.Format, value);
            OnPropertyChanged();
        }
    }

    public string ValueTag
    {
        get => GetBinding(HmiBindingNames.Value);
        set
        {
            SetBindingValue(HmiBindingNames.Value, value);
            OnPropertyChanged();
        }
    }

    public string StateTag
    {
        get => GetBinding(HmiBindingNames.State);
        set
        {
            SetBindingValue(HmiBindingNames.State, value);
            OnPropertyChanged();
        }
    }

    public string CommandTag
    {
        get => GetBinding(HmiBindingNames.Command);
        set
        {
            SetBindingValue(HmiBindingNames.Command, value);
            OnPropertyChanged();
        }
    }

    public string DynamicTextTag
    {
        get => GetBinding(HmiBindingNames.Text);
        set
        {
            SetBindingValue(HmiBindingNames.Text, value);
            OnPropertyChanged();
        }
    }

    public string InputTag
    {
        get => GetBinding(HmiBindingNames.Input);
        set
        {
            SetBindingValue(HmiBindingNames.Input, value);
            OnPropertyChanged();
        }
    }

    public string PrimaryTag => Kind switch
    {
        HmiWidgetKind.StateIndicator => StateTag,
        HmiWidgetKind.CommandButton => CommandTag,
        _ => ValueTag
    };

    public IBrush StateBrush
    {
        get
        {
            var color = State
                ? GetProperty(HmiPropertyNames.OnColor, "#16A34A")
                : GetProperty(HmiPropertyNames.OffColor, "#CBD5E1");

            return Brush.Parse(color);
        }
    }

    public string Foreground => GetProperty(HmiPropertyNames.Foreground, "#1E293B");

    public string Background => GetProperty(HmiPropertyNames.Background, "#E0F2FE");

    public string Border => GetProperty(HmiPropertyNames.Border, "#64748B");

    public string Placeholder => GetProperty(HmiPropertyNames.Placeholder, string.Empty);

    public string ImageSource => GetProperty(HmiPropertyNames.Source, string.Empty);

    public string? ResolvedImageSource => ResolveImageSource(ImageSource);

    public string Orientation => GetProperty(HmiPropertyNames.Orientation, "Horizontal");

    public bool IsHorizontalLine => string.Equals(Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase);

    public bool IsVerticalLine => string.Equals(Orientation, "Vertical", StringComparison.OrdinalIgnoreCase);

    public double FontSize => GetDoubleProperty(HmiPropertyNames.FontSize, 18);

    public double CornerRadius => GetDoubleProperty(HmiPropertyNames.CornerRadius, 4);

    public double StrokeThickness => GetDoubleProperty(HmiPropertyNames.StrokeThickness, 1);

    public double Minimum => GetDoubleProperty(HmiPropertyNames.Minimum, 0);

    public double Maximum => GetDoubleProperty(HmiPropertyNames.Maximum, 100);

    public string Level => GetProperty(HmiPropertyNames.Level, "Warning");

    public string Window => GetProperty(HmiPropertyNames.Window, "5 min");

    public double ProgressValue
        => double.TryParse(DisplayValue, out var value) ? Math.Clamp(value, Minimum, Maximum) : Minimum;

    public double ProgressPercent
    {
        get
        {
            var range = Maximum - Minimum;
            if (Math.Abs(range) < double.Epsilon)
            {
                return 0;
            }

            return Math.Clamp((ProgressValue - Minimum) / range * 100, 0, 100);
        }
    }

    public static void SetResourceResolver(Func<string, string?>? resolver)
    {
        s_resourceResolver = resolver;
    }

    public HmiWidgetDefinition ToDefinition()
        => new(
            Id,
            Kind,
            Title,
            X,
            Y,
            Width,
            Height,
            ZIndex,
            new Dictionary<string, string>(_bindings, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(_properties, StringComparer.OrdinalIgnoreCase),
            Events,
            PermissionKey);

    public IReadOnlyList<HmiEventDefinition> Events { get; private set; }

    public string? PermissionKey { get; private set; }

    public bool IsBoundTo(string tagName)
        => _bindings.Values.Any(value => string.Equals(value, tagName, StringComparison.OrdinalIgnoreCase));

    public void ApplyValue(string tagName, string displayValue, object? rawValue)
    {
        if (string.Equals(ValueTag, tagName, StringComparison.OrdinalIgnoreCase))
        {
            DisplayValue = displayValue;
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(ProgressPercent));
        }

        if (string.Equals(StateTag, tagName, StringComparison.OrdinalIgnoreCase))
        {
            State = ConvertToBoolean(rawValue, displayValue);
            OnPropertyChanged(nameof(StateBrush));
        }

        if (string.Equals(DynamicTextTag, tagName, StringComparison.OrdinalIgnoreCase))
        {
            SetPropertyValue(HmiPropertyNames.Text, displayValue);
        }
    }

    partial void OnStateChanged(bool value)
    {
        OnPropertyChanged(nameof(StateBrush));
    }

    public string GetBindingValue(string key)
        => GetBinding(key);

    public void SetBindingValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _bindings.Remove(key);
            return;
        }

        _bindings[key] = value.Trim();
    }

    public string GetPropertyValue(string key, string fallback)
        => GetProperty(key, fallback);

    public void SetPropertyValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _properties.Remove(key);
        }
        else
        {
            _properties[key] = value.Trim();
        }

        NotifyVisualPropertyChanged(key);
    }

    private string GetBinding(string key)
        => _bindings.TryGetValue(key, out var value) ? value : string.Empty;

    private string GetProperty(string key, string fallback)
        => _properties.TryGetValue(key, out var value) ? value : fallback;

    private double GetDoubleProperty(string key, double fallback)
    {
        if (!_properties.TryGetValue(key, out var value) || !double.TryParse(value, out var parsed))
        {
            return fallback;
        }

        return parsed;
    }

    private void NotifyVisualPropertyChanged(string key)
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Unit));
        OnPropertyChanged(nameof(Format));
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(Foreground));
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(Border));
        OnPropertyChanged(nameof(Placeholder));
        OnPropertyChanged(nameof(ImageSource));
        OnPropertyChanged(nameof(ResolvedImageSource));
        OnPropertyChanged(nameof(Orientation));
        OnPropertyChanged(nameof(IsHorizontalLine));
        OnPropertyChanged(nameof(IsVerticalLine));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(CornerRadius));
        OnPropertyChanged(nameof(StrokeThickness));
        OnPropertyChanged(nameof(Minimum));
        OnPropertyChanged(nameof(Maximum));
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(Window));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressPercent));
    }

    partial void OnDisplayValueChanged(string value)
    {
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressPercent));
    }

    private static string? ResolveImageSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        return s_resourceResolver?.Invoke(source) ?? source;
    }

    private static bool ConvertToBoolean(object? rawValue, string displayValue)
    {
        if (rawValue is bool boolean)
        {
            return boolean;
        }

        if (rawValue is IConvertible convertible)
        {
            try
            {
                return Math.Abs(convertible.ToDouble(null)) > double.Epsilon;
            }
            catch (FormatException)
            {
            }
            catch (InvalidCastException)
            {
            }
        }

        return bool.TryParse(displayValue, out var parsedBoolean) && parsedBoolean
            || double.TryParse(displayValue, out var parsedNumber) && Math.Abs(parsedNumber) > double.Epsilon
            || string.Equals(displayValue, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayValue, "running", StringComparison.OrdinalIgnoreCase);
    }
}
