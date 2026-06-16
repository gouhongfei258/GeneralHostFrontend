namespace GeneralHostFrontend.Core.Hmi;

public sealed class DefaultHmiWidgetCatalog : IHmiWidgetCatalog
{
    private static readonly IReadOnlyList<string> EmptyOptions = Array.Empty<string>();

    private readonly IReadOnlyList<HmiWidgetDescriptor> _descriptors;

    public DefaultHmiWidgetCatalog()
    {
        _descriptors = new[]
        {
            Descriptor(
                HmiWidgetKind.ValueText,
                "Value Text",
                "Data",
                180,
                54,
                Bindings(Binding(HmiBindingNames.Value, "Value Tag")),
                CommonTextProperties(
                    Property(HmiPropertyNames.Unit, "Unit", HmiPropertyEditorKind.Text, "Data", string.Empty),
                    Property(HmiPropertyNames.Format, "Format", HmiPropertyEditorKind.Text, "Data", "0.##"))),
            Descriptor(
                HmiWidgetKind.StateIndicator,
                "State Indicator",
                "Data",
                160,
                64,
                Bindings(Binding(HmiBindingNames.State, "State Tag")),
                Properties(
                    Property(HmiPropertyNames.Text, "Text", HmiPropertyEditorKind.Text, "Appearance", "State"),
                    Property(HmiPropertyNames.OnColor, "On Color", HmiPropertyEditorKind.Color, "Appearance", "#16A34A"),
                    Property(HmiPropertyNames.OffColor, "Off Color", HmiPropertyEditorKind.Color, "Appearance", "#CBD5E1"))),
            Descriptor(
                HmiWidgetKind.CommandButton,
                "Command Button",
                "Input",
                150,
                44,
                Bindings(Binding(HmiBindingNames.Command, "Command Tag")),
                CommonTextProperties()),
            Descriptor(
                HmiWidgetKind.Text,
                "Text",
                "Basic",
                160,
                42,
                Bindings(Binding(HmiBindingNames.Text, "Dynamic Text Tag")),
                CommonTextProperties(
                    Property(HmiPropertyNames.Foreground, "Foreground", HmiPropertyEditorKind.Color, "Appearance", "#1E293B"),
                    Property(HmiPropertyNames.FontSize, "Font Size", HmiPropertyEditorKind.Number, "Appearance", "18"))),
            Descriptor(
                HmiWidgetKind.InputBox,
                "Input Box",
                "Input",
                180,
                42,
                Bindings(Binding(HmiBindingNames.Input, "Input Tag")),
                Properties(
                    Property(HmiPropertyNames.Text, "Text", HmiPropertyEditorKind.Text, "Appearance", string.Empty),
                    Property(HmiPropertyNames.Placeholder, "Placeholder", HmiPropertyEditorKind.Text, "Appearance", "Input value"))),
            Descriptor(
                HmiWidgetKind.SwitchButton,
                "Switch Button",
                "Input",
                150,
                44,
                Bindings(Binding(HmiBindingNames.State, "State Tag")),
                Properties(
                    Property(HmiPropertyNames.Text, "Text", HmiPropertyEditorKind.Text, "Appearance", "Switch"),
                    Property(HmiPropertyNames.OnColor, "On Color", HmiPropertyEditorKind.Color, "Appearance", "#16A34A"),
                    Property(HmiPropertyNames.OffColor, "Off Color", HmiPropertyEditorKind.Color, "Appearance", "#94A3B8"))),
            Descriptor(
                HmiWidgetKind.Image,
                "Image",
                "Media",
                180,
                120,
                Bindings(Binding(HmiBindingNames.Image, "Image Tag")),
                Properties(
                    Property(HmiPropertyNames.Source, "Source", HmiPropertyEditorKind.Resource, "Appearance", string.Empty),
                    Property(HmiPropertyNames.Text, "Alt Text", HmiPropertyEditorKind.Text, "Appearance", "Image"))),
            Descriptor(
                HmiWidgetKind.Rectangle,
                "Rectangle",
                "Shapes",
                160,
                90,
                Bindings(),
                ShapeProperties("#E0F2FE")),
            Descriptor(
                HmiWidgetKind.Ellipse,
                "Ellipse",
                "Shapes",
                120,
                80,
                Bindings(),
                ShapeProperties("#DCFCE7")),
            Descriptor(
                HmiWidgetKind.Line,
                "Line",
                "Shapes",
                180,
                24,
                Bindings(),
                Properties(
                    Property(HmiPropertyNames.Border, "Color", HmiPropertyEditorKind.Color, "Appearance", "#334155"),
                    Property(HmiPropertyNames.StrokeThickness, "Thickness", HmiPropertyEditorKind.Number, "Appearance", "3"),
                    Property(HmiPropertyNames.Orientation, "Orientation", HmiPropertyEditorKind.Enum, "Appearance", "Horizontal", "Horizontal", "Vertical"))),
            Descriptor(
                HmiWidgetKind.Container,
                "Container",
                "Layout",
                260,
                160,
                Bindings(),
                Properties(
                    Property(HmiPropertyNames.Text, "Title", HmiPropertyEditorKind.Text, "Appearance", "Group"),
                    Property(HmiPropertyNames.Background, "Background", HmiPropertyEditorKind.Color, "Appearance", "#F8FAFC"),
                    Property(HmiPropertyNames.Border, "Border", HmiPropertyEditorKind.Color, "Appearance", "#CBD5E1"),
                    Property(HmiPropertyNames.CornerRadius, "Corner Radius", HmiPropertyEditorKind.Number, "Appearance", "6"))),
            Descriptor(
                HmiWidgetKind.ProgressBar,
                "Progress Bar",
                "Data",
                220,
                58,
                Bindings(Binding(HmiBindingNames.Value, "Value Tag")),
                CommonTextProperties(
                    Property(HmiPropertyNames.Minimum, "Minimum", HmiPropertyEditorKind.Number, "Data", "0"),
                    Property(HmiPropertyNames.Maximum, "Maximum", HmiPropertyEditorKind.Number, "Data", "100"),
                    Property(HmiPropertyNames.Unit, "Unit", HmiPropertyEditorKind.Text, "Data", "%"),
                    Property(HmiPropertyNames.Background, "Fill", HmiPropertyEditorKind.Color, "Appearance", "#0F766E"))),
            Descriptor(
                HmiWidgetKind.TrendChart,
                "Trend Chart",
                "Advanced",
                320,
                170,
                Bindings(Binding(HmiBindingNames.Value, "Value Tag")),
                CommonTextProperties(
                    Property(HmiPropertyNames.Minimum, "Minimum", HmiPropertyEditorKind.Number, "Data", "0"),
                    Property(HmiPropertyNames.Maximum, "Maximum", HmiPropertyEditorKind.Number, "Data", "100"),
                    Property(HmiPropertyNames.Window, "Window", HmiPropertyEditorKind.Text, "Data", "5 min"))),
            Descriptor(
                HmiWidgetKind.AlarmList,
                "Alarm List",
                "Advanced",
                320,
                170,
                Bindings(Binding(HmiBindingNames.Text, "Message Tag")),
                Properties(
                    Property(HmiPropertyNames.Text, "Title", HmiPropertyEditorKind.Text, "Appearance", "Alarms"),
                    Property(HmiPropertyNames.Level, "Level", HmiPropertyEditorKind.Enum, "Data", "Warning", "Information", "Warning", "Error", "Critical")))
        };
    }

    public IReadOnlyList<HmiWidgetDescriptor> List()
        => _descriptors;

    public HmiWidgetDescriptor Get(HmiWidgetKind kind)
        => _descriptors.FirstOrDefault(descriptor => descriptor.Kind == kind)
           ?? _descriptors[0];

    private static HmiWidgetDescriptor Descriptor(
        HmiWidgetKind kind,
        string displayName,
        string category,
        double defaultWidth,
        double defaultHeight,
        IReadOnlyList<HmiBindingSlotDescriptor> bindings,
        IReadOnlyList<HmiPropertyDescriptor> properties)
        => new(kind, displayName, category, defaultWidth, defaultHeight, properties, bindings);

    private static IReadOnlyList<HmiPropertyDescriptor> CommonTextProperties(params HmiPropertyDescriptor[] extra)
        => Properties(new[]
        {
            Property(HmiPropertyNames.Text, "Text", HmiPropertyEditorKind.Text, "Appearance", "Text"),
            Property(HmiPropertyNames.Foreground, "Foreground", HmiPropertyEditorKind.Color, "Appearance", "#1E293B")
        }.Concat(extra).ToArray());

    private static IReadOnlyList<HmiPropertyDescriptor> ShapeProperties(string background)
        => Properties(
            Property(HmiPropertyNames.Background, "Background", HmiPropertyEditorKind.Color, "Appearance", background),
            Property(HmiPropertyNames.Border, "Border", HmiPropertyEditorKind.Color, "Appearance", "#64748B"),
            Property(HmiPropertyNames.StrokeThickness, "Stroke", HmiPropertyEditorKind.Number, "Appearance", "1"),
            Property(HmiPropertyNames.CornerRadius, "Corner Radius", HmiPropertyEditorKind.Number, "Appearance", "4"));

    private static HmiPropertyDescriptor Property(
        string name,
        string displayName,
        HmiPropertyEditorKind editor,
        string group,
        string? defaultValue,
        params string[] options)
        => new(name, displayName, editor, group, defaultValue, options);

    private static IReadOnlyList<HmiPropertyDescriptor> Properties(params HmiPropertyDescriptor[] descriptors)
        => descriptors;

    private static HmiBindingSlotDescriptor Binding(string name, string displayName)
        => new(name, displayName, "Data");

    private static IReadOnlyList<HmiBindingSlotDescriptor> Bindings(params HmiBindingSlotDescriptor[] descriptors)
        => descriptors;
}
