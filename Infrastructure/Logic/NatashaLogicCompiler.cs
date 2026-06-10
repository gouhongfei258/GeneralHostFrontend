using GeneralHostFrontend.Core.Logic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using System.Runtime.Loader;

namespace GeneralHostFrontend.Infrastructure.Logic;

public sealed class NatashaLogicCompiler : ILogicCompiler
{
    private static readonly HashSet<string> AllowedUsings = new(StringComparer.Ordinal)
    {
        "System",
        "System.Collections.Generic",
        "System.Threading",
        "System.Threading.Tasks",
        "GeneralHostFrontend.Core.Logic",
        "GeneralHostFrontend.Core.Tags"
    };

    private static readonly string[] BlockedIdentifiers =
    {
        "Activator",
        "AppContext",
        "AppDomain",
        "Assembly",
        "AssemblyLoadContext",
        "Console",
        "Directory",
        "Environment",
        "File",
        "FileInfo",
        "FileStream",
        "GC",
        "GCHandle",
        "HttpClient",
        "Marshal",
        "Path",
        "Process",
        "Registry",
        "Socket",
        "TcpClient",
        "Thread",
        "ThreadPool",
        "Type"
    };

    private static readonly HashSet<string> AllowedReferenceAssemblyNames = new(StringComparer.Ordinal)
    {
        "GeneralHostFrontend",
        "System.Collections",
        "System.Linq",
        "System.Private.CoreLib",
        "System.Runtime",
        "System.Threading",
        "System.Threading.Tasks",
        "netstandard"
    };

    public Task<LogicBuildResult> CompileAsync(string code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var syntaxTree = CSharpSyntaxTree.ParseText(code, cancellationToken: cancellationToken);
        var safetyDiagnostics = ValidateSafety(syntaxTree, cancellationToken);
        if (safetyDiagnostics.Count > 0)
        {
            return Task.FromResult(new LogicBuildResult(code, false, safetyDiagnostics));
        }

        var references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly =>
                !assembly.IsDynamic
                && !string.IsNullOrWhiteSpace(assembly.Location)
                && AllowedReferenceAssemblyNames.Contains(assembly.GetName().Name ?? string.Empty))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToArray();

        var assemblyName = $"GeneralHostFrontend.GeneratedLogic.{Guid.NewGuid():N}";
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assemblyStream = new MemoryStream();
        using var symbolsStream = new MemoryStream();
        var emit = compilation.Emit(
            assemblyStream,
            symbolsStream,
            cancellationToken: cancellationToken);
        var diagnostics = emit.Diagnostics
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        if (!emit.Success)
        {
            return Task.FromResult(new LogicBuildResult(code, false, diagnostics));
        }

        assemblyStream.Position = 0;
        symbolsStream.Position = 0;

        var loadContext = new CollectibleLogicLoadContext(assemblyName);
        try
        {
            var assembly = loadContext.LoadFromStream(assemblyStream, symbolsStream);
            var logicType = assembly
                .GetTypes()
                .FirstOrDefault(type =>
                    typeof(IGeneratedHostLogic).IsAssignableFrom(type)
                    && !type.IsAbstract
                    && type.GetConstructor(Type.EmptyTypes) is not null);

            if (logicType is null)
            {
                loadContext.Unload();
                return Task.FromResult(new LogicBuildResult(
                    code,
                    false,
                    new[] { "Compiled assembly does not contain a public parameterless IGeneratedHostLogic implementation." }));
            }

            var instance = (IGeneratedHostLogic)Activator.CreateInstance(logicType)!;
            return Task.FromResult(new LogicBuildResult(
                code,
                true,
                diagnostics,
                new CompiledHostLogic(assemblyName, instance, loadContext)));
        }
        catch (Exception ex)
        {
            loadContext.Unload();
            return Task.FromResult(new LogicBuildResult(code, false, new[] { ex.Message }));
        }
    }

    private static IReadOnlyList<string> ValidateSafety(SyntaxTree syntaxTree, CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var root = syntaxTree.GetRoot(cancellationToken);

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var namespaceName = usingDirective.Name?.ToString();
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                continue;
            }

            if (!AllowedUsings.Contains(namespaceName))
            {
                diagnostics.Add($"Using namespace '{namespaceName}' is not allowed in generated host logic.");
            }
        }

        foreach (var member in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var expression = member.Expression.ToString();
            var name = member.Name.Identifier.ValueText;
            if (IsBlockedExpression(expression) || IsBlockedIdentifier(name) || IsBlockedTaskMember(expression, name))
            {
                diagnostics.Add($"Access to '{member}' is not allowed in generated host logic.");
            }
        }

        foreach (var objectCreation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var typeName = objectCreation.Type.ToString();
            if (IsBlockedExpression(typeName))
            {
                diagnostics.Add($"Creating '{typeName}' is not allowed in generated host logic.");
            }
        }

        foreach (var identifier in root.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken)))
        {
            if (IsBlockedIdentifier(identifier.ValueText))
            {
                diagnostics.Add($"Identifier '{identifier.ValueText}' is not allowed in generated host logic.");
            }
        }

        return diagnostics.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsBlockedExpression(string expression)
        => expression.StartsWith("System.IO", StringComparison.Ordinal)
           || expression.StartsWith("System.Net", StringComparison.Ordinal)
           || expression.StartsWith("System.Reflection", StringComparison.Ordinal)
           || expression.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal)
           || expression.StartsWith("System.Diagnostics", StringComparison.Ordinal)
           || expression.StartsWith("Microsoft.Win32", StringComparison.Ordinal)
           || BlockedIdentifiers.Any(identifier => expression.Equals(identifier, StringComparison.Ordinal));

    private static bool IsBlockedIdentifier(string identifier)
        => BlockedIdentifiers.Contains(identifier, StringComparer.Ordinal);

    private static bool IsBlockedTaskMember(string expression, string name)
        => (expression.Equals("Task", StringComparison.Ordinal) && !name.Equals("Delay", StringComparison.Ordinal))
           || expression.StartsWith("Task.", StringComparison.Ordinal);

    private sealed class CollectibleLogicLoadContext : AssemblyLoadContext
    {
        public CollectibleLogicLoadContext(string name)
            : base(name, isCollectible: true)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var sharedAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
            return sharedAssembly;
        }
    }

    private sealed class CompiledHostLogic : ICompiledHostLogic
    {
        private readonly AssemblyLoadContext _loadContext;
        private IGeneratedHostLogic? _instance;
        private bool _disposed;

        public CompiledHostLogic(string assemblyName, IGeneratedHostLogic instance, AssemblyLoadContext loadContext)
        {
            AssemblyName = assemblyName;
            _instance = instance;
            _loadContext = loadContext;
        }

        public string AssemblyName { get; }

        public IGeneratedHostLogic Instance
            => _instance ?? throw new ObjectDisposedException(nameof(CompiledHostLogic));

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _instance = null;
            _loadContext.Unload();
            return ValueTask.CompletedTask;
        }
    }
}
