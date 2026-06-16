using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using GeneralHostFrontend.ViewModels.Hmi;
using System.ComponentModel;

namespace GeneralHostFrontend.Views.Hmi;

public static class DesignerCanvasBehavior
{
    public static readonly AttachedProperty<bool> IsDragEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsDragEnabled", typeof(DesignerCanvasBehavior));

    public static readonly AttachedProperty<bool> IsResizeEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsResizeEnabled", typeof(DesignerCanvasBehavior));

    public static readonly AttachedProperty<bool> IsResizeHandleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsResizeHandle", typeof(DesignerCanvasBehavior));

    public static readonly AttachedProperty<bool> IsCanvasItemEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsCanvasItemEnabled", typeof(DesignerCanvasBehavior));

    public static readonly AttachedProperty<bool> IsSelectionSurfaceEnabledProperty =
        AvaloniaProperty.RegisterAttached<Canvas, bool>("IsSelectionSurfaceEnabled", typeof(DesignerCanvasBehavior));

    public static readonly AttachedProperty<bool> IsGridLineEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsGridLineEnabled", typeof(DesignerCanvasBehavior));

    public static readonly AttachedProperty<bool> IsWidgetContainerEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsWidgetContainerEnabled", typeof(DesignerCanvasBehavior));

    public static readonly AttachedProperty<bool> IsGridLineContainerEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsGridLineContainerEnabled", typeof(DesignerCanvasBehavior));

    private static readonly AttachedProperty<DragState?> DragStateProperty =
        AvaloniaProperty.RegisterAttached<Control, DragState?>("DragState", typeof(DesignerCanvasBehavior));

    private static readonly AttachedProperty<ResizeState?> ResizeStateProperty =
        AvaloniaProperty.RegisterAttached<Control, ResizeState?>("ResizeState", typeof(DesignerCanvasBehavior));

    private static readonly AttachedProperty<SelectionState?> SelectionStateProperty =
        AvaloniaProperty.RegisterAttached<Canvas, SelectionState?>("SelectionState", typeof(DesignerCanvasBehavior));

    private static readonly AttachedProperty<CanvasItemState?> CanvasItemStateProperty =
        AvaloniaProperty.RegisterAttached<Control, CanvasItemState?>("CanvasItemState", typeof(DesignerCanvasBehavior));

    static DesignerCanvasBehavior()
    {
        IsDragEnabledProperty.Changed.AddClassHandler<Control>(OnIsDragEnabledChanged);
        IsResizeEnabledProperty.Changed.AddClassHandler<Control>(OnIsResizeEnabledChanged);
        IsCanvasItemEnabledProperty.Changed.AddClassHandler<Control>(OnIsCanvasItemEnabledChanged);
        IsSelectionSurfaceEnabledProperty.Changed.AddClassHandler<Canvas>(OnIsSelectionSurfaceEnabledChanged);
        IsGridLineEnabledProperty.Changed.AddClassHandler<Control>(OnIsGridLineEnabledChanged);
        IsWidgetContainerEnabledProperty.Changed.AddClassHandler<Control>(OnIsWidgetContainerEnabledChanged);
        IsGridLineContainerEnabledProperty.Changed.AddClassHandler<Control>(OnIsGridLineContainerEnabledChanged);
    }

    public static bool GetIsDragEnabled(Control control)
        => control.GetValue(IsDragEnabledProperty);

    public static void SetIsDragEnabled(Control control, bool value)
        => control.SetValue(IsDragEnabledProperty, value);

    public static bool GetIsResizeEnabled(Control control)
        => control.GetValue(IsResizeEnabledProperty);

    public static void SetIsResizeEnabled(Control control, bool value)
        => control.SetValue(IsResizeEnabledProperty, value);

    public static bool GetIsResizeHandle(Control control)
        => control.GetValue(IsResizeHandleProperty);

    public static void SetIsResizeHandle(Control control, bool value)
        => control.SetValue(IsResizeHandleProperty, value);

    public static bool GetIsCanvasItemEnabled(Control control)
        => control.GetValue(IsCanvasItemEnabledProperty);

    public static void SetIsCanvasItemEnabled(Control control, bool value)
        => control.SetValue(IsCanvasItemEnabledProperty, value);

    public static bool GetIsSelectionSurfaceEnabled(Canvas canvas)
        => canvas.GetValue(IsSelectionSurfaceEnabledProperty);

    public static void SetIsSelectionSurfaceEnabled(Canvas canvas, bool value)
        => canvas.SetValue(IsSelectionSurfaceEnabledProperty, value);

    public static bool GetIsGridLineEnabled(Control control)
        => control.GetValue(IsGridLineEnabledProperty);

    public static void SetIsGridLineEnabled(Control control, bool value)
        => control.SetValue(IsGridLineEnabledProperty, value);

    public static bool GetIsWidgetContainerEnabled(Control control)
        => control.GetValue(IsWidgetContainerEnabledProperty);

    public static void SetIsWidgetContainerEnabled(Control control, bool value)
        => control.SetValue(IsWidgetContainerEnabledProperty, value);

    public static bool GetIsGridLineContainerEnabled(Control control)
        => control.GetValue(IsGridLineContainerEnabledProperty);

    public static void SetIsGridLineContainerEnabled(Control control, bool value)
        => control.SetValue(IsGridLineContainerEnabledProperty, value);

    private static void OnIsDragEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
            control.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
            control.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
        }
        else
        {
            control.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            control.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            control.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        }
    }

    private static void OnIsResizeEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            control.AddHandler(InputElement.PointerPressedEvent, OnResizePointerPressed, handledEventsToo: true);
            control.AddHandler(InputElement.PointerMovedEvent, OnResizePointerMoved, handledEventsToo: true);
            control.AddHandler(InputElement.PointerReleasedEvent, OnResizePointerReleased, handledEventsToo: true);
        }
        else
        {
            control.RemoveHandler(InputElement.PointerPressedEvent, OnResizePointerPressed);
            control.RemoveHandler(InputElement.PointerMovedEvent, OnResizePointerMoved);
            control.RemoveHandler(InputElement.PointerReleasedEvent, OnResizePointerReleased);
        }
    }

    private static void OnIsCanvasItemEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            control.AttachedToVisualTree += OnCanvasItemAttached;
            control.DetachedFromVisualTree += OnCanvasItemDetached;
            control.DataContextChanged += OnCanvasItemDataContextChanged;
            AttachCanvasItem(control);
        }
        else
        {
            control.AttachedToVisualTree -= OnCanvasItemAttached;
            control.DetachedFromVisualTree -= OnCanvasItemDetached;
            control.DataContextChanged -= OnCanvasItemDataContextChanged;
            DetachCanvasItem(control);
        }
    }

    private static void OnIsSelectionSurfaceEnabledChanged(Canvas canvas, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            canvas.Focusable = true;
            canvas.PointerPressed += OnSurfacePointerPressed;
            canvas.PointerMoved += OnSurfacePointerMoved;
            canvas.PointerReleased += OnSurfacePointerReleased;
            canvas.KeyDown += OnSurfaceKeyDown;
        }
        else
        {
            canvas.PointerPressed -= OnSurfacePointerPressed;
            canvas.PointerMoved -= OnSurfacePointerMoved;
            canvas.PointerReleased -= OnSurfacePointerReleased;
            canvas.KeyDown -= OnSurfaceKeyDown;
            ClearSelectionState(canvas);
        }
    }

    private static void OnIsGridLineEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            control.AttachedToVisualTree += OnGridLineAttached;
            control.DataContextChanged += OnGridLineDataContextChanged;
            ApplyGridLineLayout(control);
        }
        else
        {
            control.AttachedToVisualTree -= OnGridLineAttached;
            control.DataContextChanged -= OnGridLineDataContextChanged;
        }
    }

    private static void OnIsWidgetContainerEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            control.AttachedToVisualTree += OnWidgetContainerAttached;
            control.DetachedFromVisualTree += OnWidgetContainerDetached;
            control.DataContextChanged += OnWidgetContainerDataContextChanged;
            AttachCanvasItem(control);
        }
        else
        {
            control.AttachedToVisualTree -= OnWidgetContainerAttached;
            control.DetachedFromVisualTree -= OnWidgetContainerDetached;
            control.DataContextChanged -= OnWidgetContainerDataContextChanged;
            DetachCanvasItem(control);
        }
    }

    private static void OnIsGridLineContainerEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            control.AttachedToVisualTree += OnGridLineContainerAttached;
            control.DataContextChanged += OnGridLineContainerDataContextChanged;
            ApplyGridLineContainerLayout(control);
        }
        else
        {
            control.AttachedToVisualTree -= OnGridLineContainerAttached;
            control.DataContextChanged -= OnGridLineContainerDataContextChanged;
        }
    }

    private static void OnWidgetContainerAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control control)
        {
            AttachCanvasItem(control);
        }
    }

    private static void OnWidgetContainerDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control control)
        {
            DetachCanvasItem(control);
        }
    }

    private static void OnWidgetContainerDataContextChanged(object? sender, EventArgs args)
    {
        if (sender is Control control)
        {
            DetachCanvasItem(control);
            AttachCanvasItem(control);
        }
    }

    private static void OnGridLineContainerAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control control)
        {
            ApplyGridLineContainerLayout(control);
        }
    }

    private static void OnGridLineContainerDataContextChanged(object? sender, EventArgs args)
    {
        if (sender is Control control)
        {
            ApplyGridLineContainerLayout(control);
        }
    }

    private static void ApplyGridLineContainerLayout(Control control)
    {
        if (control.DataContext is not HmiGridLineViewModel gridLine)
        {
            return;
        }

        Canvas.SetLeft(control, gridLine.X);
        Canvas.SetTop(control, gridLine.Y);
        control.Width = gridLine.Width;
        control.Height = gridLine.Height;
    }

    private static void OnGridLineAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control control)
        {
            ApplyGridLineLayout(control);
        }
    }

    private static void OnGridLineDataContextChanged(object? sender, EventArgs args)
    {
        if (sender is Control control)
        {
            ApplyGridLineLayout(control);
        }
    }

    private static void ApplyGridLineLayout(Control control)
    {
        if (control.DataContext is not HmiGridLineViewModel gridLine
            || FindCanvasChild(control) is not { } target)
        {
            return;
        }

        Canvas.SetLeft(target, gridLine.X);
        Canvas.SetTop(target, gridLine.Y);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Control control
            || control.DataContext is not HmiWidgetViewModel widget
            || args.GetCurrentPoint(control).Properties.IsLeftButtonPressed is false)
        {
            return;
        }

        if (IsFromResizeHandle(args.Source as Visual))
        {
            return;
        }

        var root = FindCanvas(control);
        if (root is null)
        {
            return;
        }

        var editor = FindEditor(control);
        if (editor?.IsRunMode is true)
        {
            return;
        }

        if (editor is not null)
        {
            if (args.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                editor.ToggleWidgetSelection(widget);
            }
            else if (!widget.IsSelected)
            {
                editor.SelectWidget(widget);
            }
        }

        var starts = editor is null
            ? new Dictionary<HmiWidgetViewModel, Point> { [widget] = new(widget.X, widget.Y) }
            : editor.SelectedWidgets.ToDictionary(selected => selected, selected => new Point(selected.X, selected.Y));

        if (!starts.ContainsKey(widget))
        {
            starts[widget] = new Point(widget.X, widget.Y);
        }

        control.SetValue(DragStateProperty, new DragState(args.GetPosition(root), starts));
        args.Pointer.Capture(control);
        root.Focus();
        args.Handled = true;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not Control control
            || control.DataContext is not HmiWidgetViewModel widget
            || control.GetValue(DragStateProperty) is not { } state
            || FindCanvas(control) is not { } root)
        {
            return;
        }

        var current = args.GetPosition(root);
        var deltaX = current.X - state.StartPointer.X;
        var deltaY = current.Y - state.StartPointer.Y;
        var editor = FindEditor(control);
        foreach (var (target, start) in state.StartPositions)
        {
            editor?.SetWidgetPosition(target, start.X + deltaX, start.Y + deltaY);
        }

        args.Handled = true;
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not Control control)
        {
            return;
        }

        control.ClearValue(DragStateProperty);
        args.Pointer.Capture(null);
        args.Handled = true;
    }

    private static void OnResizePointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Control control
            || control.DataContext is not HmiWidgetViewModel widget
            || args.GetCurrentPoint(control).Properties.IsLeftButtonPressed is false)
        {
            return;
        }

        var root = FindCanvas(control);
        if (root is null)
        {
            return;
        }

        var editor = FindEditor(control);
        if (editor?.IsRunMode is true)
        {
            return;
        }

        editor?.SelectWidget(widget);
        control.SetValue(ResizeStateProperty, new ResizeState(args.GetPosition(root), widget.Width, widget.Height));
        args.Pointer.Capture(control);
        root.Focus();
        args.Handled = true;
    }

    private static void OnResizePointerMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not Control control
            || control.DataContext is not HmiWidgetViewModel widget
            || control.GetValue(ResizeStateProperty) is not { } state
            || FindCanvas(control) is not { } root)
        {
            return;
        }

        var current = args.GetPosition(root);
        FindEditor(control)?.ResizeWidget(
            widget,
            state.StartWidth + current.X - state.StartPointer.X,
            state.StartHeight + current.Y - state.StartPointer.Y);
        args.Handled = true;
    }

    private static void OnResizePointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not Control control)
        {
            return;
        }

        control.ClearValue(ResizeStateProperty);
        args.Pointer.Capture(null);
        args.Handled = true;
    }

    private static void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Canvas canvas
            || args.Source is Control { DataContext: HmiWidgetViewModel }
            || args.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed is false)
        {
            return;
        }

        var editor = FindEditor(canvas);
        if (editor is null || editor.IsRunMode)
        {
            return;
        }

        canvas.Focus();
        var start = args.GetPosition(canvas);
        var rectangle = CreateSelectionRectangle(start);
        canvas.Children.Add(rectangle);
        canvas.SetValue(SelectionStateProperty, new SelectionState(start, rectangle));
        args.Pointer.Capture(canvas);
        args.Handled = true;
    }

    private static void OnSurfacePointerMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not Canvas canvas
            || canvas.GetValue(SelectionStateProperty) is not { } state)
        {
            return;
        }

        UpdateSelectionRectangle(state.Rectangle, state.Start, args.GetPosition(canvas));
        args.Handled = true;
    }

    private static void OnSurfacePointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not Canvas canvas
            || canvas.GetValue(SelectionStateProperty) is not { } state)
        {
            return;
        }

        var end = args.GetPosition(canvas);
        FindEditor(canvas)?.SelectWidgetsInRect(state.Start.X, state.Start.Y, end.X, end.Y);
        ClearSelectionState(canvas);
        args.Pointer.Capture(null);
        args.Handled = true;
    }

    private static void OnSurfaceKeyDown(object? sender, KeyEventArgs args)
    {
        if (sender is not Canvas canvas || FindEditor(canvas) is not { } editor)
        {
            return;
        }

        switch (args.Key)
        {
            case Key.Delete:
                editor.DeleteSelectedWidgetCommand.Execute(null);
                args.Handled = true;
                break;
            case Key.C when args.KeyModifiers.HasFlag(KeyModifiers.Control):
                editor.CopySelectedWidgetsCommand.Execute(null);
                args.Handled = true;
                break;
            case Key.V when args.KeyModifiers.HasFlag(KeyModifiers.Control):
                editor.PasteWidgetsCommand.Execute(null);
                args.Handled = true;
                break;
            case Key.Left:
                editor.MoveSelectionCommand.Execute(args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "LargeLeft" : "Left");
                args.Handled = true;
                break;
            case Key.Right:
                editor.MoveSelectionCommand.Execute(args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "LargeRight" : "Right");
                args.Handled = true;
                break;
            case Key.Up:
                editor.MoveSelectionCommand.Execute(args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "LargeUp" : "Up");
                args.Handled = true;
                break;
            case Key.Down:
                editor.MoveSelectionCommand.Execute(args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "LargeDown" : "Down");
                args.Handled = true;
                break;
        }
    }

    private static Border CreateSelectionRectangle(Point start)
    {
        var rectangle = new Border
        {
            Width = 0,
            Height = 0,
            BorderBrush = Brush.Parse("#0EA5E9"),
            BorderThickness = new Thickness(1),
            Background = Brush.Parse("#220EA5E9"),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(rectangle, start.X);
        Canvas.SetTop(rectangle, start.Y);
        rectangle.SetValue(Panel.ZIndexProperty, int.MaxValue);
        return rectangle;
    }

    private static void UpdateSelectionRectangle(Border rectangle, Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        rectangle.Width = Math.Abs(end.X - start.X);
        rectangle.Height = Math.Abs(end.Y - start.Y);
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, top);
    }

    private static void ClearSelectionState(Canvas canvas)
    {
        if (canvas.GetValue(SelectionStateProperty) is not { } state)
        {
            return;
        }

        canvas.Children.Remove(state.Rectangle);
        canvas.ClearValue(SelectionStateProperty);
    }

    private sealed record DragState(Point StartPointer, IReadOnlyDictionary<HmiWidgetViewModel, Point> StartPositions);

    private sealed record ResizeState(Point StartPointer, double StartWidth, double StartHeight);

    private sealed record SelectionState(Point Start, Border Rectangle);

    private sealed record CanvasItemState(Control Target, HmiWidgetViewModel Widget, PropertyChangedEventHandler Handler);

    private static void OnCanvasItemAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control control)
        {
            AttachCanvasItem(control);
        }
    }

    private static void OnCanvasItemDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control control)
        {
            DetachCanvasItem(control);
        }
    }

    private static void OnCanvasItemDataContextChanged(object? sender, EventArgs args)
    {
        if (sender is Control control)
        {
            DetachCanvasItem(control);
            AttachCanvasItem(control);
        }
    }

    private static void AttachCanvasItem(Control control)
    {
        if (control.GetValue(CanvasItemStateProperty) is not null
            || control.DataContext is not HmiWidgetViewModel widget
            || FindCanvasChild(control) is not { } target)
        {
            return;
        }

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is nameof(HmiWidgetViewModel.X)
                or nameof(HmiWidgetViewModel.Y)
                or nameof(HmiWidgetViewModel.Width)
                or nameof(HmiWidgetViewModel.Height)
                or nameof(HmiWidgetViewModel.ZIndex))
            {
                ApplyCanvasLayout(target, widget);
            }
        };

        widget.PropertyChanged += handler;
        control.SetValue(CanvasItemStateProperty, new CanvasItemState(target, widget, handler));
        ApplyCanvasLayout(target, widget);
    }

    private static void DetachCanvasItem(Control control)
    {
        if (control.GetValue(CanvasItemStateProperty) is not { } state)
        {
            return;
        }

        state.Widget.PropertyChanged -= state.Handler;
        control.ClearValue(CanvasItemStateProperty);
    }

    private static void ApplyCanvasLayout(Control target, HmiWidgetViewModel widget)
    {
        Canvas.SetLeft(target, widget.X);
        Canvas.SetTop(target, widget.Y);
        target.SetValue(Panel.ZIndexProperty, widget.ZIndex);
        target.Width = widget.Width;
        target.Height = widget.Height;
    }

    private static Control? FindCanvasChild(Control control)
    {
        if (control.GetVisualParent() is Canvas)
        {
            return control;
        }

        var current = control.GetVisualParent();
        while (current is not null)
        {
            if (current.GetVisualParent() is Canvas && current is Control target)
            {
                return target;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private static bool IsFromResizeHandle(Visual? visual)
    {
        var current = visual;
        while (current is not null)
        {
            if (current is Control control && GetIsResizeHandle(control))
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private static Canvas? FindCanvas(Control control)
    {
        var current = control.GetVisualParent();
        while (current is not null)
        {
            if (current is Canvas canvas)
            {
                return canvas;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private static HmiEditorViewModel? FindEditor(StyledElement element)
    {
        var current = element.Parent;
        while (current is not null)
        {
            if (current.DataContext is HmiEditorViewModel editor)
            {
                return editor;
            }

            current = current.Parent;
        }

        return null;
    }
}
