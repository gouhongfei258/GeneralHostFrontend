using GeneralHostFrontend.Core.Logic;
using NodifyM.Avalonia.ViewModelBase;

namespace GeneralHostFrontend.ViewModels.Logic;

public sealed class LogicConnectorViewModel : ConnectorViewModelBase
{
    public LogicConnectorViewModel(
        LogicNodeViewModel node,
        LogicConnectorDefinition definition)
    {
        Node = node;
        Id = definition.Id;
        Kind = definition.Kind;
        Direction = definition.Direction;
        ValueType = definition.ValueType;
        Title = definition.Name;
        Flow = definition.Direction is LogicConnectorDirection.Input
            ? ConnectorFlow.Input
            : ConnectorFlow.Output;
    }

    public string Id { get; }

    public LogicConnectorKind Kind { get; }

    public LogicConnectorDirection Direction { get; }

    public LogicValueType ValueType { get; }

    public LogicNodeViewModel Node { get; }

    public string DisplayKind => Kind is LogicConnectorKind.Flow ? "Flow" : ValueType.ToString();
}
