using System;
using System.Linq;
using Delta.Maths;
using Delta.Shader;
using Delta.Shader.Analyzers;
using Delta.Shader.Contract;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public sealed class GeneratedBufferPackingTests
{
    [Fact]
    public void GraphicsGenerator_EmitsStorageRangesAndVertexBindingPacker()
    {
        const string source = """
            using Delta.Maths;
            using Delta.Shader;

            public struct Payload
            {
                [Layout(0)]
                public Position Position;

                [Layout(1)]
                public Color Color;
            }

            public readonly struct VertexContext
            {
                [Interstage]
                public readonly Payload Vertex;

                [Layout(0, 0)]
                public readonly ReadOnlyStorageBuffer<uint> First;

                [Layout(0, 1)]
                public readonly ReadOnlyStorageBuffer<float> Second;
            }

            public readonly struct FragmentContext
            {
                [Interstage]
                public readonly Payload Fragment;
            }

            public static class BufferPackingShader
            {
                [VertexShader("buffer-packing")]
                public static Payload Vertex(in VertexContext context) => default;

                [FragmentShader("buffer-packing")]
                public static float4 Fragment(in FragmentContext context) => default;
            }
            """;

        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(
            [
                typeof(ComputeShaderAttribute).Assembly.Location,
                typeof(ShaderArtifact).Assembly.Location,
                typeof(float4).Assembly.Location,
                typeof(DeltaGraphicsGenerator).Assembly.Location
            ])
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "GeneratedBufferPackingFixture",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new DeltaGraphicsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSource = string.Join(
            Environment.NewLine,
            driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).Select(sourceResult => sourceResult.SourceText.ToString()));

        Assert.Contains("StorageBufferCount = 2", generatedSource, StringComparison.Ordinal);
        Assert.Contains("GetVertexStorageBufferRanges", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackVertexVertexElement", generatedSource, StringComparison.Ordinal);
        Assert.Contains("VertexVertexBufferCount = 1", generatedSource, StringComparison.Ordinal);
        Assert.Contains("GetVertexVertexBufferRanges", generatedSource, StringComparison.Ordinal);
    }
}
