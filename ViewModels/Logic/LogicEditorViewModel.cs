using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeneralHostFrontend.Core.Logic;
using GeneralHostFrontend.Infrastructure.Logic;
using NodifyM.Avalonia.ViewModelBase;

namespace GeneralHostFrontend.ViewModels.Logic;

public sealed partial class LogicEditorViewModel : NodifyEditorViewModelBase
{
    private readonly ILogicGraphStore _store;
    private readonly ILogicCodeGenerator _codeGenerator;
    private readonly ILogicCompiler _compiler;
    private LogicGraphDocument _document = new("Main Logic", Array.Empty<LogicNodeDefinition>(), Array.Empty<LogicConnectionDefinition>());

    [ObservableProperty]
    private LogicNodeViewModel? _selectedNode;

    [ObservableProperty]
    private LogicNodeKind _selectedNodeKind = LogicNodeKind.ReadTag;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _generatedCode = string.Empty;

    [ObservableProperty]
    private string _diagnostics = string.Empty;

    [ObservableProperty]
    private LogicConnectorViewModel? _selectedSourceConnector;

    [ObservableProperty]
    private LogicConnectorViewModel? _selectedTargetConnector;

    public LogicEditorViewModel()
    {
        _store = null!;
        _codeGenerator = null!;
        _compiler = null!;
        PendingConnection = new PendingConnectionViewModelBase(this);
        AttachSelectionSync();
    }

    public LogicEditorViewModel(
        ILogicGraphStore store,
        ILogicCodeGenerator codeGenerator,
        ILogicCompiler compiler)
    {
        _store = store;
        _codeGenerator = codeGenerator;
        _compiler = compiler;
        PendingConnection = new PendingConnectionViewModelBase(this);
        AttachSelectionSync();
        _ = LoadAsync();
    }

    public ObservableCollection<LogicNodeViewModel> LogicNodes { get; } = new();

    public ObservableCollection<LogicConnectionViewModel> LogicConnections { get; } = new();

    public IReadOnlyList<LogicNodeKind> NodeKinds { get; } = Enum.GetValues<LogicNodeKind>();

    public IReadOnlyList<LogicConnectorViewModel> OutputConnectors
        => LogicNodes.SelectMany(node => node.Output.OfType<LogicConnectorViewModel>()).ToArray();

    public IReadOnlyList<LogicConnectorViewModel> InputConnectors
        => LogicNodes.SelectMany(node => node.Input.OfType<LogicConnectorViewModel>()).ToArray();

    [RelayCommand]
    private void AddNode()
    {
        var nextIndex = LogicNodes.Count + 1;
        var definition = LogicNodeTemplate.Create(SelectedNodeKind, $"node-{Guid.NewGuid():N}", 80 + nextIndex * 24, 80 + nextIndex * 24);
        var node = new LogicNodeViewModel(definition);
        AddNode(node);
        SelectNode(node);
        RefreshConnectorLists();
        StatusMessage = $"{definition.Title} node added.";
    }

    [RelayCommand]
    private void DeleteSelectedNode()
    {
        var node = ResolveSelectedNode() ?? SelectedNode;
        if (node is null)
        {
            return;
        }

        for (var index = LogicConnections.Count - 1; index >= 0; index--)
        {
            var connection = LogicConnections[index];
            var source = (LogicConnectorViewModel)connection.Source;
            var target = (LogicConnectorViewModel)connection.Target;
            if (source.Node == node || target.Node == node)
            {
                RemoveConnectionAt(index);
            }
        }

        RemoveNode(node);
        SelectNode(LogicNodes.FirstOrDefault());
        RefreshConnectorLists();
        StatusMessage = "Selected node deleted.";
    }

    [RelayCommand]
    private void ConnectSelected()
    {
        if (SelectedSourceConnector is null || SelectedTargetConnector is null)
        {
            StatusMessage = "Select an output connector and an input connector first.";
            return;
        }

        AddConnectionFromConnectors(SelectedSourceConnector, SelectedTargetConnector);
    }

    public override void Connect(ConnectorViewModelBase source, ConnectorViewModelBase target)
    {
        if (source is not LogicConnectorViewModel sourceConnector
            || target is not LogicConnectorViewModel targetConnector)
        {
            StatusMessage = "Unsupported connector type.";
            return;
        }

        if (sourceConnector.Direction is LogicConnectorDirection.Input)
        {
            (sourceConnector, targetConnector) = (targetConnector, sourceConnector);
        }

        AddConnectionFromConnectors(sourceConnector, targetConnector);
    }

    public override void DisconnectConnector(ConnectorViewModelBase connector)
    {
        base.DisconnectConnector(connector);
        SyncLogicConnections();
        StatusMessage = "Connector disconnected.";
    }

    private void AddConnectionFromConnectors(LogicConnectorViewModel source, LogicConnectorViewModel target)
    {
        if (source.Direction is not LogicConnectorDirection.Output || target.Direction is not LogicConnectorDirection.Input)
        {
            StatusMessage = "Connections must go from an output connector to an input connector.";
            return;
        }

        if (source.Node == target.Node)
        {
            StatusMessage = "A node cannot connect to itself.";
            return;
        }

        if (source.Kind != target.Kind)
        {
            StatusMessage = "Connector kinds must match.";
            return;
        }

        if (source.Kind is LogicConnectorKind.Value && !CanConnectValueTypes(source.ValueType, target.ValueType))
        {
            StatusMessage = $"Cannot connect {source.ValueType} to {target.ValueType}.";
            return;
        }

        if (LogicConnections.Any(connection =>
            ReferenceEquals(connection.Source, source)
            && ReferenceEquals(connection.Target, target)))
        {
            StatusMessage = "Connection already exists.";
            return;
        }

        AddConnection(new LogicConnectionViewModel($"conn-{Guid.NewGuid():N}", this, source, target));
        StatusMessage = "Connection added.";
    }

    private static bool CanConnectValueTypes(LogicValueType source, LogicValueType target)
        => source == target
           || source is LogicValueType.Any
           || target is LogicValueType.Any
           || target is LogicValueType.Object;

    [RelayCommand]
    private void DeleteLastConnection()
    {
        if (LogicConnections.Count == 0)
        {
            return;
        }

        RemoveConnectionAt(LogicConnections.Count - 1);
        StatusMessage = "Last connection deleted.";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_store is null)
        {
            return;
        }

        _document = await _store.LoadAsync();
        LoadFromDocument(_document);
        GenerateCode();
        StatusMessage = "Logic graph loaded.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_store is null)
        {
            return;
        }

        _document = ToDocument();
        await _store.SaveAsync(_document);
        SyncLogicConnections();
        GenerateCode();
        StatusMessage = "Logic graph saved.";
    }

    [RelayCommand]
    private void GenerateCode()
    {
        if (_codeGenerator is null)
        {
            return;
        }

        SyncLogicConnections();
        var result = _codeGenerator.Generate(ToDocument());
        GeneratedCode = result.Code;
        Diagnostics = result.Diagnostics.Count == 0
            ? "No diagnostics."
            : string.Join(Environment.NewLine, result.Diagnostics);
        StatusMessage = result.IsValid
            ? "C# logic generated."
            : "C# logic generated with diagnostics.";
    }

    [RelayCommand]
    private async Task CompileAsync()
    {
        if (_compiler is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(GeneratedCode))
        {
            GenerateCode();
        }

        var result = await _compiler.CompileAsync(GeneratedCode);
        Diagnostics = result.Diagnostics.Count == 0
            ? "Compile succeeded."
            : string.Join(Environment.NewLine, result.Diagnostics);
        StatusMessage = result.IsValid ? "Generated C# compiled." : "Generated C# compile failed.";
    }

    private void LoadFromDocument(LogicGraphDocument document)
    {
        LogicNodes.Clear();
        LogicConnections.Clear();
        Nodes.Clear();
        Connections.Clear();

        foreach (var node in document.Nodes)
        {
            AddNode(new LogicNodeViewModel(node));
        }

        foreach (var connection in document.Connections)
        {
            var source = FindConnector(connection.SourceNodeId, connection.SourceConnectorId, output: true);
            var target = FindConnector(connection.TargetNodeId, connection.TargetConnectorId, output: false);
            if (source is not null && target is not null)
            {
                AddConnection(new LogicConnectionViewModel(connection.Id, this, source, target));
            }
        }

        SelectNode(LogicNodes.FirstOrDefault());
        RefreshConnectorLists();
    }

    private LogicGraphDocument ToDocument()
    {
        return new LogicGraphDocument(
            string.IsNullOrWhiteSpace(_document.Name) ? "Main Logic" : _document.Name,
            LogicNodes.Select(node => node.ToDefinition()).ToArray(),
            Connections
                .OfType<ConnectionViewModelBase>()
                .Select(ToDefinition)
                .ToArray(),
            _document.PlcStructs is { Count: > 0 }
                ? _document.PlcStructs
                : LogicNodeTemplate.CreateDefaultPlcStructs());
    }

    private static LogicConnectionDefinition ToDefinition(ConnectionViewModelBase connection)
    {
        var source = (LogicConnectorViewModel)connection.Source;
        var target = (LogicConnectorViewModel)connection.Target;
        return connection is LogicConnectionViewModel typed
            ? typed.ToDefinition()
            : new LogicConnectionDefinition(
                $"conn-{Guid.NewGuid():N}",
                source.Node.Id,
                source.Id,
                target.Node.Id,
                target.Id);
    }

    private LogicConnectorViewModel? FindConnector(string nodeId, string connectorId, bool output)
    {
        var node = LogicNodes.FirstOrDefault(item => string.Equals(item.Id, nodeId, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            return null;
        }

        var connectors = output ? node.Output : node.Input;
        return connectors
            .OfType<LogicConnectorViewModel>()
            .FirstOrDefault(item => string.Equals(item.Id, connectorId, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshConnectorLists()
    {
        OnPropertyChanged(nameof(OutputConnectors));
        OnPropertyChanged(nameof(InputConnectors));
        SelectedSourceConnector ??= OutputConnectors.FirstOrDefault();
        SelectedTargetConnector ??= InputConnectors.FirstOrDefault();
    }

    private void AttachSelectionSync()
    {
        SelectedNodes.CollectionChanged += OnSelectedNodesChanged;
    }

    private void OnSelectedNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SelectedNode = ResolveSelectedNode();
    }

    private LogicNodeViewModel? ResolveSelectedNode()
        => SelectedNodes.OfType<LogicNodeViewModel>().LastOrDefault();

    private void SelectNode(LogicNodeViewModel? node)
    {
        SelectedNode = node;

        if (SelectedNodes.Count == 1 && ReferenceEquals(SelectedNodes[0], node))
        {
            return;
        }

        SelectedNodes.Clear();
        if (node is not null)
        {
            SelectedNodes.Add(node);
        }
    }

    private void AddNode(LogicNodeViewModel node)
    {
        LogicNodes.Add(node);
        Nodes.Add(node);
    }

    private void RemoveNode(LogicNodeViewModel node)
    {
        LogicNodes.Remove(node);
        Nodes.Remove(node);
    }

    private void AddConnection(LogicConnectionViewModel connection)
    {
        LogicConnections.Add(connection);
        Connections.Add(connection);
        connection.Source.IsConnected = true;
        connection.Target.IsConnected = true;
    }

    private void SyncLogicConnections()
    {
        LogicConnections.Clear();
        foreach (var connection in Connections.OfType<ConnectionViewModelBase>())
        {
            if (connection is LogicConnectionViewModel typed)
            {
                LogicConnections.Add(typed);
                continue;
            }

            if (connection.Source is LogicConnectorViewModel source
                && connection.Target is LogicConnectorViewModel target)
            {
                LogicConnections.Add(new LogicConnectionViewModel($"conn-{Guid.NewGuid():N}", this, source, target));
            }
        }
    }

    private void RemoveConnectionAt(int index)
    {
        var connection = LogicConnections[index];
        LogicConnections.RemoveAt(index);
        Connections.Remove(connection);
        RefreshConnectionState(connection.Source);
        RefreshConnectionState(connection.Target);
    }

    private void RefreshConnectionState(ConnectorViewModelBase connector)
    {
        connector.IsConnected = Connections.Any(connection =>
            ReferenceEquals(connection.Source, connector)
            || ReferenceEquals(connection.Target, connector));
    }
}
