using System.Text.Json.Serialization;

namespace GeneralHostFrontend.Core.Hmi;

public static class HmiPageDefaults
{
    public const string MainPageId = "main";
    public const string MainPageName = "Main HMI";
    public const double Width = 1280;
    public const double Height = 720;
    public const string Background = "#F8FAFC";
}

public sealed record HmiPageDocument(
    int SchemaVersion,
    string Id,
    string Name,
    double Width,
    double Height,
    string Background,
    HmiGridDefinition Grid,
    IReadOnlyList<HmiWidgetDefinition> Widgets)
{
    public const int CurrentSchemaVersion = 1;

    public static HmiPageDocument CreateDefault()
        => CreateDefault(HmiPageDefaults.MainPageId, HmiPageDefaults.MainPageName);

    public static HmiPageDocument CreateDefault(string id, string name)
        => new(
            CurrentSchemaVersion,
            id,
            name,
            HmiPageDefaults.Width,
            HmiPageDefaults.Height,
            HmiPageDefaults.Background,
            HmiGridDefinition.Default,
            new[]
            {
                HmiWidgetDefinition.Create(
                    HmiWidgetKind.ValueText,
                    "Temperature",
                    96,
                    88,
                    180,
                    54,
                    new Dictionary<string, string>
                    {
                        [HmiBindingNames.Value] = "Line.Speed"
                    },
                    new Dictionary<string, string>
                    {
                        [HmiPropertyNames.Text] = "Line Speed",
                        [HmiPropertyNames.Unit] = "pcs/min",
                        [HmiPropertyNames.Format] = "0.0"
                    }),
                HmiWidgetDefinition.Create(
                    HmiWidgetKind.StateIndicator,
                    "Station Ready",
                    96,
                    172,
                    160,
                    64,
                    new Dictionary<string, string>
                    {
                        [HmiBindingNames.State] = "Station.Ready"
                    },
                    new Dictionary<string, string>
                    {
                        [HmiPropertyNames.Text] = "Ready",
                        [HmiPropertyNames.OnColor] = "#16A34A",
                        [HmiPropertyNames.OffColor] = "#CBD5E1"
                    }),
                HmiWidgetDefinition.Create(
                    HmiWidgetKind.CommandButton,
                    "Set Speed",
                    96,
                    266,
                    150,
                    44,
                    new Dictionary<string, string>
                    {
                        [HmiBindingNames.Command] = "Line.Speed"
                    },
                    new Dictionary<string, string>
                    {
                        [HmiPropertyNames.Text] = "Set Speed"
                    }) with
                    {
                        Events = new[]
                        {
                            new HmiEventDefinition(
                                HmiEventNames.Click,
                                new[]
                                {
                                    new HmiActionDefinition(
                                        HmiActionKind.WriteTag,
                                        new Dictionary<string, string>
                                        {
                                            [HmiActionParameterNames.TagName] = "Line.Speed",
                                            [HmiActionParameterNames.Value] = "42"
                                        })
                                })
                        }
                    }
            });
}

public sealed record HmiGridDefinition(
    bool IsVisible,
    bool SnapToGrid,
    double Size)
{
    public static HmiGridDefinition Default { get; } = new(true, true, 20);
}

public sealed record HmiWidgetDefinition(
    string Id,
    HmiWidgetKind Kind,
    string Title,
    double X,
    double Y,
    double Width,
    double Height,
    int ZIndex,
    Dictionary<string, string> Bindings,
    Dictionary<string, string> Properties,
    IReadOnlyList<HmiEventDefinition>? Events = null,
    string? PermissionKey = null)
{
    public static HmiWidgetDefinition Create(
        HmiWidgetKind kind,
        string title,
        double x,
        double y,
        double width,
        double height,
        Dictionary<string, string>? bindings = null,
        Dictionary<string, string>? properties = null)
        => new(
            $"widget-{Guid.NewGuid():N}",
            kind,
            title,
            x,
            y,
            width,
            height,
            0,
            bindings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            properties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Array.Empty<HmiEventDefinition>(),
            null);
}

public sealed record HmiEventDefinition(
    string EventName,
    IReadOnlyList<HmiActionDefinition> Actions);

public sealed record HmiActionDefinition(
    HmiActionKind Kind,
    Dictionary<string, string> Parameters);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HmiActionKind
{
    WriteTag,
    NavigatePage,
    SetVisible,
    SetEnabled,
    SetProperty,
    Delay
}

public static class HmiEventNames
{
    public const string Click = "click";
    public const string Confirm = "confirm";
    public const string Toggle = "toggle";
    public const string ValueChanged = "valueChanged";
}

public static class HmiActionParameterNames
{
    public const string TagName = "tagName";
    public const string Value = "value";
    public const string PageId = "pageId";
    public const string TargetWidgetId = "targetWidgetId";
    public const string PropertyName = "propertyName";
    public const string DelayMilliseconds = "delayMilliseconds";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HmiWidgetKind
{
    ValueText,
    StateIndicator,
    CommandButton,
    Text,
    InputBox,
    SwitchButton,
    Image,
    Rectangle,
    Ellipse,
    Line,
    Container,
    ProgressBar,
    TrendChart,
    AlarmList
}

public static class HmiBindingNames
{
    public const string Value = "value";
    public const string State = "state";
    public const string Command = "command";
    public const string Text = "text";
    public const string Input = "input";
    public const string Image = "image";
}

public static class HmiPropertyNames
{
    public const string Text = "text";
    public const string Unit = "unit";
    public const string Format = "format";
    public const string OnColor = "onColor";
    public const string OffColor = "offColor";
    public const string Foreground = "foreground";
    public const string Background = "background";
    public const string Border = "border";
    public const string FontSize = "fontSize";
    public const string CornerRadius = "cornerRadius";
    public const string StrokeThickness = "strokeThickness";
    public const string Source = "source";
    public const string Placeholder = "placeholder";
    public const string Orientation = "orientation";
    public const string Minimum = "minimum";
    public const string Maximum = "maximum";
    public const string Level = "level";
    public const string Window = "window";
}

public interface IHmiPageStore
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    Task<HmiPageDocument> LoadAsync(string id, CancellationToken cancellationToken = default);

    Task SaveAsync(HmiPageDocument document, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface IHmiWidgetCatalog
{
    IReadOnlyList<HmiWidgetDescriptor> List();

    HmiWidgetDescriptor Get(HmiWidgetKind kind);
}

public interface IHmiResourceStore
{
    Task<IReadOnlyList<HmiResourceDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    string? ResolvePath(string resourceId);
}

public interface IHmiTemplateStore
{
    Task<IReadOnlyList<HmiWidgetTemplateDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    Task<HmiWidgetTemplateDocument> LoadAsync(string id, CancellationToken cancellationToken = default);

    Task SaveAsync(HmiWidgetTemplateDocument template, CancellationToken cancellationToken = default);
}

public interface ITagWriteService
{
    Task<TagWriteResult> WriteAsync(string tagName, object? value, CancellationToken cancellationToken = default);
}

public sealed record TagWriteResult(
    bool Succeeded,
    string Message)
{
    public static TagWriteResult Success(string message)
        => new(true, message);

    public static TagWriteResult Failure(string message)
        => new(false, message);
}

public sealed record HmiResourceDescriptor(
    string Id,
    string DisplayName,
    string Kind,
    string RelativePath);

public sealed record HmiWidgetTemplateDescriptor(
    string Id,
    string Name,
    int WidgetCount);

public sealed record HmiWidgetTemplateDocument(
    string Id,
    string Name,
    IReadOnlyList<HmiWidgetDefinition> Widgets);

public sealed record HmiWidgetDescriptor(
    HmiWidgetKind Kind,
    string DisplayName,
    string Category,
    double DefaultWidth,
    double DefaultHeight,
    IReadOnlyList<HmiPropertyDescriptor> Properties,
    IReadOnlyList<HmiBindingSlotDescriptor> Bindings);

public sealed record HmiPropertyDescriptor(
    string Name,
    string DisplayName,
    HmiPropertyEditorKind Editor,
    string Group,
    string? DefaultValue,
    IReadOnlyList<string> Options);

public sealed record HmiBindingSlotDescriptor(
    string Name,
    string DisplayName,
    string Group);

public enum HmiPropertyEditorKind
{
    Text,
    Number,
    Boolean,
    Color,
    Enum,
    Tag,
    Resource
}
