using System.Linq.Expressions;
using Delta.Maths;
using Delta.Shader.Abstractions;
using Delta.Shader.Compiler;
using Delta.Shader.Runtime;
using Xunit;

namespace Delta.Shader.Runtime.Tests;

public sealed class ExpressionComputeShaderTests
{
    [Fact]
    public void ValidExpressionLambda_CompilesToValidatedArtifact()
    {
        var result = ExpressionComputeShaderCompiler.Compile(CreateKernel(), CreateOptions());

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)) + "\n" + result.GeneratedSource);
        Assert.NotNull(result.Artifact);
        Assert.Equal(ShaderStage.Compute, result.Artifact!.Stage);
        Assert.Equal("main", result.Artifact.EntryPoint);
        Assert.NotEmpty(result.Artifact.Spirv);
        Assert.Contains("?", result.GeneratedSource);
    }

    [Fact]
    public void StatementBody_IsRejectedWithDsh014()
    {
        var input = Expression.Parameter(typeof(ReadOnlyStorageBuffer<uint>), "input");
        var output = Expression.Parameter(typeof(ReadWriteStorageBuffer<uint>), "output");
        var invocation = Expression.Parameter(typeof(uint), "invocation");
        var store = Expression.Call(
            output,
            typeof(ReadWriteStorageBuffer<uint>).GetMethod(nameof(ReadWriteStorageBuffer<uint>.Store))!,
            invocation,
            Expression.Call(input, typeof(ReadOnlyStorageBuffer<uint>).GetMethod(nameof(ReadOnlyStorageBuffer<uint>.Load))!, invocation));
        var expression = Expression.Lambda<Action<ReadOnlyStorageBuffer<uint>, ReadWriteStorageBuffer<uint>, uint>>(
            Expression.Block(store, Expression.Empty()), input, output, invocation);

        var result = ExpressionComputeShaderCompiler.Compile(expression, CreateOptions());

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH014);
    }

    [Fact]
    public void ClosureConstant_IsRejectedWithoutFallback()
    {
        var scale = 2u;
        Expression<Action<ReadOnlyStorageBuffer<uint>, ReadWriteStorageBuffer<uint>, uint>> expression =
            (input, output, invocation) => output.Store(invocation, input.Load(invocation) * scale);

        var result = ExpressionComputeShaderCompiler.Compile(expression, CreateOptions());

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH014);
    }

    [Fact]
    public void DeltaMathsCall_IsAcceptedBySymbolBasedCompiler()
    {
        Expression<Action<ReadOnlyStorageBuffer<float>, ReadWriteStorageBuffer<float>, uint>> expression =
            (input, output, invocation) => output.Store(invocation, maths.sin(input.Load(invocation)));

        var result = ExpressionComputeShaderCompiler.Compile(expression, CreateOptions());

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Fact]
    public void SameExpressionAndProfile_UsesStructuralCache()
    {
        ExpressionComputeShaderCompiler.ClearCache();
        var expression = CreateKernel();

        var first = ExpressionComputeShaderCompiler.Compile(expression, CreateOptions());
        var second = ExpressionComputeShaderCompiler.Compile(expression, CreateOptions());

        Assert.True(first.Success, string.Join("\n", first.Diagnostics.Select(diagnostic => diagnostic.Message)) + "\n" + first.GeneratedSource);
        Assert.True(second.Success, string.Join("\n", second.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.False(first.CacheHit);
        Assert.True(second.CacheHit);
        Assert.Equal(first.CacheKey, second.CacheKey);
    }

    private static Expression<Action<ReadOnlyStorageBuffer<uint>, ReadWriteStorageBuffer<uint>, uint>> CreateKernel()
        => (input, output, invocation) => output.Store(
            invocation,
            invocation < input.Length
                ? input.Load(invocation) * 2u + 1u
                : 0u);

    private static ComputeExpressionOptions CreateOptions()
        => new()
        {
            InvocationParameterIndex = 2,
            Bindings =
            [
                new ComputeExpressionBinding(0, 0, 0, ShaderResourceAccess.ReadOnly),
                new ComputeExpressionBinding(1, 0, 1, ShaderResourceAccess.ReadWrite)
            ]
        };
}
