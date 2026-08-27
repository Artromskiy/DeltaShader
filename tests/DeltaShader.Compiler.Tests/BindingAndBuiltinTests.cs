using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public void ContextRoleAttributes_AreFieldOnly()
    {
        Assert.Equal(AttributeTargets.Field, typeof(LayoutAttribute).GetCustomAttribute<AttributeUsageAttribute>()!.ValidOn);
        Assert.Equal(AttributeTargets.Field, typeof(PushConstantAttribute).GetCustomAttribute<AttributeUsageAttribute>()!.ValidOn);
        Assert.Equal(AttributeTargets.Field, typeof(PositionAttribute).GetCustomAttribute<AttributeUsageAttribute>()!.ValidOn);
    }

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
    public async Task ComputeBodySupportsLocalsAndNestedBoundsCheck()
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
                public static void Execute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    if (id < context.Count)
                    {
                        uint value = context.Input[id] * 2u;
                        context.Output[id] = value + 1u;
                    }
                }
            }";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("uint id", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("uint value", result.Module.Body, StringComparison.Ordinal);
        Assert.Contains("if (id< pushConstants.member_Count)", result.Module.Body, StringComparison.Ordinal);
        Assert.Contains("Output.data[id] = value+ 1u;", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeBodySupportsStaticHelperCallGraph()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct ComputeContext
            {
                [Layout(0, 0)]
                public readonly ReadOnlyStorageBuffer<int> Input;

                [Layout(0, 1)]
                public readonly ReadWriteStorageBuffer<int> Output;
            }

            public static class SomeClass
            {
                public static int GetValue() => 100 / 3;
            }

            public static class ComputeEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext context)
                {
                    var k = SomeClass.GetValue();
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    if (id < context.Input.Length)
                    {
                        context.Output[id] = context.Input[id] + k;
                    }
                }
            }
        ";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var module = result.Module ?? throw new InvalidOperationException("Successful helper compilation did not produce an IR module.");
        Assert.Single(module.HelperFunctions);
        Assert.Contains("int k = delta_helper_", module.Body, StringComparison.Ordinal);

        var glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(module).Source;
        var helperIndex = glsl.IndexOf("delta_helper_", StringComparison.Ordinal);
        Assert.True(helperIndex >= 0);
        Assert.True(helperIndex < glsl.IndexOf("void main()", StringComparison.Ordinal));
        Assert.Contains("return 100 / 3;", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeBodySupportsEarlyReturn()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct ComputeContext
            {
                [Layout(0, 0)]
                public readonly ReadWriteStorageBuffer<uint> Output;
            }

            public static class ComputeEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    if (id == 0u)
                        return;
                    context.Output[id] = 1u;
                }
            }
        ";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("if (id", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("return;", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeBodySupportsLocalValueStructInitializer()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct ComputeContext
            {
                [Layout(0, 0)]
                public readonly ReadWriteStorageBuffer<float> Output;

                [PushConstant]
                public readonly float DeltaTime;
            }

            public struct SubContext
            {
                public float DeltaTime { get; init; }
            }

            public static class ComputeEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext context)
                {
                    var sbctx = new SubContext
                    {
                        DeltaTime = context.DeltaTime
                    };
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    if (id < context.Output.Length)
                        context.Output[id] = sbctx.DeltaTime;
                }
            }
        ";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains(result.Module!.Structs, structure => structure.GlslName.Contains("SubContext", StringComparison.Ordinal));
        Assert.Contains("DeltaStruct_SubContext sbctx", result.Module.Body, StringComparison.Ordinal);
        Assert.Contains("sbctx.member_DeltaTime", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeBodySupportsForLoopWithLocalCounter()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct ComputeContext
            {
                [PushConstant]
                public readonly uint Seed;
            }

            public static class ComputeEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext context)
                {
                    for (int i = 10; i < 100; i++)
                    {
                    }
                }
            }
        ";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("for (int i = 10;", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("i< 100", result.Module.Body, StringComparison.Ordinal);
        Assert.Contains("i++", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeBodySupportsOutHelperAndDiscardedResult()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct ComputeContext
            {
                [PushConstant]
                public readonly uint Seed;
            }

            public static class ComputeEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext context)
                {
                    for (int i = 10; i < 100; ++i)
                    {
                        _ = GetSome(out var some);
                    }
                }

                private static bool GetSome(out int some)
                {
                    some = 11;
                    return true;
                }
            }
        ";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains(result.Module!.HelperFunctions, helper => helper.Contains("out int", StringComparison.Ordinal));
        Assert.Contains(result.Module.HelperFunctions, helper => helper.Contains("arg_some = 11;", StringComparison.Ordinal));
        Assert.Contains("delta_helper_", result.Module.Body, StringComparison.Ordinal);
        Assert.Contains("some", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeBodyRejectsInstanceIndex()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct ComputeContext
            {
                [Layout(0, 0)]
                public readonly ReadWriteStorageBuffer<uint> Output;
            }

            public static class ComputeEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext context)
                {
                    context.Output[ShaderBuiltins.InstanceIndex] = 1u;
                }
            }
        ";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Shader builtin 'InstanceIndex' is not valid in Compute stage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ComputeBodySupportsTargetTypedMathsConstructor()
    {
        const string source = @"
            using Delta.Shader;
            using Delta.Maths;

            public readonly struct ComputeContext
            {
                [Layout(0, 0)]
                public readonly ReadOnlyStorageBuffer<uint> Input;

                [Layout(0, 1)]
                public readonly ReadWriteStorageBuffer<uint> Output;
            }

            public static class ComputeEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext context)
                {
                    float4 color = new(1f, 1f, 1f, 1f);
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    if (id < context.Input.Length)
                    {
                        context.Output[id] = context.Input[id] * 2u + 1u;
                    }
                }
            }";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("vec4 color = vec4(1, 1, 1, 1);", result.Module!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VertexInput_UsesSingleArgumentBindingAndComputesSequentialOffsets()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            [Interstage]
            public struct VertexPayload
            {
                [Position]
                [Layout(0)]
                public float4 Position;
                [Layout(1)]
                public float2 Uv;
            }

            public readonly struct VertexContext
            {
                [Interstage]
                public readonly VertexPayload Vertex;
            }

            public static class VertexEntry
            {
                [VertexShader]
                public static VertexPayload Execute(in VertexContext context)
                {
                    return new VertexPayload
                    {
                        Position = new float4(context.Vertex.Position.xyz, 1f),
                        Uv = context.Vertex.Uv
                    };
                }
            }";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal((0u, 0u), (result.Module!.VertexInputs[0].Location, result.Module.VertexInputs[0].ByteOffset));
        Assert.Equal((1u, 16u), (result.Module.VertexInputs[1].Location, result.Module.VertexInputs[1].ByteOffset));
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
            diagnostic.Message.Contains("Vertex-input [Layout(location)] is not valid in a compute", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VertexBuiltin_IsRejectedInFragmentBody()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            [Interstage]
            public struct FragmentPayload
            {
                [Position]
                public float4 Position;
            }

            public readonly struct FragmentContext
            {
                [Interstage]
                public readonly FragmentPayload Fragment;
            }

            public static class FragmentEntry
            {
                [FragmentShader]
                public static float4 Execute(in FragmentContext context) =>
                    new float4(ShaderBuiltins.VertexIndex);
            }";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

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
