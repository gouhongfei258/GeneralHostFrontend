using GeneralHostFrontend.Core.Logic;
using NodifyM.Avalonia.ViewModelBase;

namespace GeneralHostFrontend.ViewModels.Logic;

public sealed class LogicConnectionViewModel : ConnectionViewModelBase
{
    public LogicConnectionViewModel(
        string id,
        NodifyEditorViewModelBase editor,
        LogicConnectorViewModel source,
        LogicConnectorViewModel target)
        : base(editor, source, target, source.Kind is LogicConnectorKind.Flow ? source.Title : source.DisplayKind)
    {
        Id = id;
    }

    public string Id { get; }

    public string Summary
    {
        get
        {
            var source = (LogicConnectorViewModel)Source;
            var target = (LogicConnectorViewModel)Target;
            return $"{source.Node.Title}.{source.Title} -> {target.Node.Title}.{target.Title}";
        }
    }

    public LogicConnectionDefinition ToDefinition()
    {
        var source = (LogicConnectorViewModel)Source;
        var target = (LogicConnectorViewModel)Target;
        return new LogicConnectionDefinition(
            Id,
            source.Node.Id,
            source.Id,
            target.Node.Id,
            target.Id);
    }
}
