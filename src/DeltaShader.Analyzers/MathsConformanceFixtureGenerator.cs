using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Delta.Shader.Analyzers;

public sealed class MathsConformanceFixtureGenerator(string source) : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(
            context.CompilationProvider,
            (sourceContext, _) => sourceContext.AddSource(
                "MathsConformanceFixtures.g.cs",
                SourceText.From(source, Encoding.UTF8)));
    }
}
