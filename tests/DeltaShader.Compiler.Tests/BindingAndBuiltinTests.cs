using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Delta.Shader.Compiler;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public sealed class BindingAndBuiltinTests
{
    [Fact]
    public async Task ComputeContext_UsesUnifiedDescriptorBindingAndStaticBuiltin()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct ComputeContext
            {
                [Layout(0, 0)]
                public readonly ReadOnlyStorageBuffer<uint> Input;

                [Layout(0, 1)]
                public readonly ReadWriteStorageBuffer<uint> Output;

                [PushConstant]
                public readonly uint Count;
            }

            public static class ComputeEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext ctx)
                {
                    if (ShaderBuiltins.GlobalInvocationId.X < ctx.Count)
                        ctx.Output[ShaderBuiltins.GlobalInvocationId.X] = ctx.Input[ShaderBuiltins.GlobalInvocationId.X] * 2u + 1u;
                }
            }";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = ComputeEntryPoints.ValidateAndBuild(
            new ModuleCompilationContext(compilation),
            new RoslynFrontend(compilation),
            ShaderCompilationOptions.Default);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal((0u, 0u), (Assert.Single(result.BuildManifest!.Resources, resource => resource.ParameterName == "ctx.Input").Set,
            Assert.Single(result.BuildManifest.Resources, resource => resource.ParameterName == "ctx.Input").Binding));
        Assert.Equal((0u, 1u), (Assert.Single(result.BuildManifest.Resources, resource => resource.ParameterName == "ctx.Output").Set,
            Assert.Single(result.BuildManifest.Resources, resource => resource.ParameterName == "ctx.Output").Binding));

        string glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(result.Module!).Source;
        Assert.Contains("gl_GlobalInvocationID.x", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VertexInput_UsesSingleArgumentBindingAndComputesSequentialOffsets()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            public static class VertexEntry
            {
                [VertexShader]
                public static void Execute(
                    [Layout(0)] float3 position,
                    [Layout(1)] float2 uv,
                    [Position] out float4 clipPosition)
                {
                    clipPosition = new float4(position, ShaderBuiltins.VertexIndex);
                }
            }";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = GraphicsEntryPoints.ValidateAndBuild(
            new ModuleCompilationContext(compilation),
            new RoslynFrontend(compilation),
            ShaderStage.Vertex);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal((0u, 0u), (result.Module!.VertexInputs[0].Location, result.Module.VertexInputs[0].ByteOffset));
        Assert.Equal((1u, 12u), (result.Module.VertexInputs[1].Location, result.Module.VertexInputs[1].ByteOffset));
        Assert.Contains("gl_VertexIndex", Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(result.Module).Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindingLocation_IsRejectedInComputeContext()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct InvalidContext
            {
                [Layout(0)]
                public readonly ReadOnlyStorageBuffer<uint> Values;
            }

            public static class InvalidCompute
            {
                [ComputeShader]
                public static void Execute(in InvalidContext ctx)
                {
                }
            }";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = ComputeEntryPoints.ValidateAndBuild(
            new ModuleCompilationContext(compilation),
            new RoslynFrontend(compilation),
            ShaderCompilationOptions.Default);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Vertex-input [Layout(location)] is not valid in compute", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VertexBuiltin_IsRejectedInFragmentBody()
    {
        const string source = @"
            using Delta.Shader;

            public static class FragmentEntry
            {
                [FragmentShader]
                public static void Execute([FragmentColor] out float4 color)
                {
                    color = new float4(ShaderBuiltins.VertexIndex);
                }
            }";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = GraphicsEntryPoints.ValidateAndBuild(
            new ModuleCompilationContext(compilation),
            new RoslynFrontend(compilation),
            ShaderStage.Fragment);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Shader builtin 'VertexIndex' is not valid in Fragment stage", StringComparison.Ordinal));
    }

    private static async Task<Compilation> LoadCompilationAsync(string source)
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        string projectPath = Path.Combine(
            FindRepositoryRoot(),
            "DeltaShader",
            "tests",
            "DeltaShader.Compiler.Tests",
            "DeltaShader.Compiler.Tests.csproj");
        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        Project project = await workspace.OpenProjectAsync(projectPath).ConfigureAwait(true);
        Compilation? compilation = await project.GetCompilationAsync().ConfigureAwait(true);
        Assert.NotNull(compilation);
        CSharpParseOptions parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
            ?? CSharpParseOptions.Default;
        return compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, parseOptions));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "DeltaMaths")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate TheFurnace repository root.");
    }
}
