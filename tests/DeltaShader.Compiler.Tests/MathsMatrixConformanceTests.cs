using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Delta.Maths;
using Delta.Shader;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Delta.Shader.Compiler.Intrinsics;
using Delta.Shader.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public sealed class MathsMatrixConformanceTests
{
    [Fact]
    public void DeltaMathsContract_ValidatesAllRectangularMatrixMetadata()
    {
        var contract = ShaderContractManifest.LoadEmbedded();
        contract.Validate();

        var expected = new Dictionary<string, (string Glsl, uint Columns, uint Rows, uint Stride, uint Size)>
        {
            ["float2x2"] = ("mat2", 2, 2, 8, 16),
            ["float2x3"] = ("mat2x3", 2, 3, 16, 32),
            ["float2x4"] = ("mat2x4", 2, 4, 16, 32),
            ["float3x2"] = ("mat3x2", 3, 2, 8, 24),
            ["float3x3"] = ("mat3", 3, 3, 16, 48),
            ["float3x4"] = ("mat3x4", 3, 4, 16, 48),
            ["float4x2"] = ("mat4x2", 4, 2, 8, 32),
            ["float4x3"] = ("mat4x3", 4, 3, 16, 64),
            ["float4x4"] = ("mat4", 4, 4, 16, 64)
        };

        foreach (var pair in expected)
        {
            var type = Assert.Single(contract.Types, candidate => candidate.ClrName == pair.Key);
            Assert.Equal(pair.Value.Glsl, type.GlslName);
            Assert.True(type.ColumnMajor == true);
            Assert.Equal(pair.Value.Columns, type.MatrixColumns);
            Assert.Equal(pair.Value.Rows, type.MatrixRows);
            Assert.Equal("float", type.ElementGlslType);
            Assert.Equal(pair.Value.Stride, type.Alignment);
            Assert.Equal(pair.Value.Stride, type.MatrixStride);
            Assert.Equal(pair.Value.Size, type.Size);

            var layout = ShaderStd430Layout.ForGlslType(pair.Value.Glsl);
            Assert.Equal(pair.Value.Stride, layout.Alignment);
            Assert.Equal(pair.Value.Size, layout.Size);
            Assert.Equal(pair.Value.Size, layout.ArrayStride);
            Assert.Equal(pair.Value.Stride, layout.MatrixStride);
        }
    }

    [Fact]
    public void MathsFixtures_CompileVectorAdditionAndMatrixVectorMultiplication()
    {
        const string source = """
            using Delta.Maths;
            using Delta.Shader;

            public readonly struct VectorContext
            {
                [Layout(0, 0)]
                public readonly ReadOnlyStorageBuffer<float2> Left;
                [Layout(0, 1)]
                public readonly ReadOnlyStorageBuffer<float2> Right;
                [Layout(0, 2)]
                public readonly ReadWriteStorageBuffer<float2> Output;
                [PushConstant]
                public readonly uint Count;
            }

            public readonly struct MatrixContext
            {
                [Layout(0, 0)]
                public readonly ReadOnlyStorageBuffer<float4x4> Matrices;
                [Layout(0, 1)]
                public readonly ReadOnlyStorageBuffer<float4> Vectors;
                [Layout(0, 2)]
                public readonly ReadWriteStorageBuffer<float4> Output;
                [PushConstant]
                public readonly uint Count;
            }

            public static class MathsFixtures
            {
                [ComputeShader(localSizeX: 64)]
                public static void Add(in VectorContext context)
                {
                    uint index = ShaderBuiltins.GlobalInvocationId.X;
                    if (index < context.Count)
                    {
                        context.Output[index] = context.Left[index] + context.Right[index];
                    }
                }

                [ComputeShader(localSizeX: 64)]
                public static void Transform(in MatrixContext context)
                {
                    uint index = ShaderBuiltins.GlobalInvocationId.X;
                    if (index < context.Count)
                    {
                        context.Output[index] = context.Matrices[index] * context.Vectors[index];
                    }
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var results = ShaderCompiler.CompileAll(compilation);
        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message))));

        var add = Assert.Single(results, result => result.SourceMethodName == "Add");
        Assert.Contains("vec2", GlslEmitter.EmitFromModule(add.Module!).Source, StringComparison.Ordinal);

        var transform = Assert.Single(results, result => result.SourceMethodName == "Transform");
        var matrixResource = Assert.Single(transform.BuildManifest!.Resources, resource => resource.Binding == 0);
        Assert.Equal("mat4", matrixResource.GlslType);
        Assert.Equal(16u, matrixResource.Alignment);
        Assert.Equal(64u, matrixResource.Size);
        Assert.Equal(64u, matrixResource.ArrayStride);
        Assert.Equal(16u, matrixResource.MatrixStride);
        Assert.Equal(0u, matrixResource.Binding);
        var glsl = GlslEmitter.EmitFromModule(transform.Module!).Source;
        Assert.Contains("mat4", glsl, StringComparison.Ordinal);
        Assert.Contains("*", glsl, StringComparison.Ordinal);
        Assert.DoesNotContain("transpose", glsl, StringComparison.OrdinalIgnoreCase);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trustedAssemblies is not null)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                references.Add(path);
            }
        }

        references.Add(typeof(float2).Assembly.Location);
        references.Add(typeof(ComputeShaderAttribute).Assembly.Location);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        return CSharpCompilation.Create(
            "DeltaShaderMathsMatrixConformance",
            [tree],
            references.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }
}
