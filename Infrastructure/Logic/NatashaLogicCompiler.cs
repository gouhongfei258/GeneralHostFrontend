using GeneralHostFrontend.Core.Logic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GeneralHostFrontend.Infrastructure.Logic;

public sealed class NatashaLogicCompiler : ILogicCompiler
{
    public Task<LogicBuildResult> CompileAsync(string code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var syntaxTree = CSharpSyntaxTree.ParseText(code, cancellationToken: cancellationToken);
        var references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "GeneralHostFrontend.GeneratedLogic",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: cancellationToken);
        var diagnostics = emit.Diagnostics
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        return Task.FromResult(new LogicBuildResult(code, emit.Success, diagnostics));
    }
}
