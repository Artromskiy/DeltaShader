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
        Assert.Contains("uint local_id", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("uint local_value", result.Module.Body, StringComparison.Ordinal);
        Assert.Contains("if (local_id< pushConstants.member_Count)", result.Module.Body, StringComparison.Ordinal);
        Assert.Contains("Output.data[local_id] = local_value+ 1u;", result.Module.Body, StringComparison.Ordinal);
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
        Assert.Contains("int local_k = delta_helper_", module.Body, StringComparison.Ordinal);

        var glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(module).Source;
        var helperIndex = glsl.IndexOf("delta_helper_", StringComparison.Ordinal);
        Assert.True(helperIndex >= 0);
        Assert.True(helperIndex < glsl.IndexOf("void main()", StringComparison.Ordinal));
        Assert.Contains("return 100 / 3;", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeBodyLowersIntegerRemainderWithCSharpSemantics()
    {
        const string source = """
            using Delta.Maths;
            using Delta.Shader;

            public readonly struct ComputeContext
            {
                [Layout(0, 0)]
                public readonly ReadOnlyStorageBuffer<int> Input;

                [Layout(0, 1)]
                public readonly ReadWriteStorageBuffer<int> Output;
            }

            public static class ComputeEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    if (id < context.Input.Length)
                    {
                        int divisor = -3;
                        context.Output[id] = context.Input[id] % divisor;
                    }
                }
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains(
            "Input.data[local_id] - (Input.data[local_id] / local_divisor) * local_divisor",
            result.Module!.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("%", result.Module.Body, StringComparison.Ordinal);
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
        Assert.Contains("if (local_id", result.Module!.Body, StringComparison.Ordinal);
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
        Assert.Contains("DeltaStruct_SubContext local_sbctx", result.Module.Body, StringComparison.Ordinal);
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
        Assert.Contains("for (int local_i = 10;", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("local_i< 100", result.Module.Body, StringComparison.Ordinal);
        Assert.Contains("local_i++", result.Module.Body, StringComparison.Ordinal);
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
        Assert.Contains("vec4 local_color = vec4(1, 1, 1, 1);", result.Module!.Body, StringComparison.Ordinal);
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

                [Layout(0)]
                public Position Position;
                [Layout(1)]
                public Uv0 Uv;
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
    public async Task NestedInterstagePayload_FlattensSemanticLeavesAndMatchesStages()
    {
        const string source = """
            using Delta.Maths;
            using Delta.Shader;

            [Interstage]
            public struct SurfacePayload
            {
                public Position Position;
                public SurfaceData Surface;
            }

            public struct SurfaceData
            {
                public Uv0 Uv;
                public VertexColor Color;
            }

            public struct VertexContext
            {
                [Interstage]
                public SurfacePayload Vertex;
            }

            public struct FragmentContext
            {
                [Interstage]
                public SurfacePayload Fragment;
            }

            public static class NestedGraphics
            {
                [VertexShader]
                public static SurfacePayload Vertex(in VertexContext context) => new SurfacePayload
                {
                    Position = new float4(0f, 0f, 0f, 1f),
                    Surface = new SurfaceData
                    {
                        Uv = new float2(0.5f, 0.5f),
                        Color = new float4(1f, 0f, 0f, 1f)
                    }
                };

                [FragmentShader]
                public static float4 Fragment(in FragmentContext context) => context.Fragment.Surface.Color.Value;
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        ShaderCompilationResult vertex = Assert.Single(results, result => result.Module?.Stage == ShaderStage.Vertex);
        ShaderCompilationResult fragment = Assert.Single(results, result => result.Module?.Stage == ShaderStage.Fragment);

        Assert.True(vertex.Success, string.Join(Environment.NewLine, vertex.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(fragment.Success, string.Join(Environment.NewLine, fragment.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(
            new[] { "Surface_Uv", "Surface_Color" },
            vertex.Module!.Outputs.Where(output => output.Builtin is null).Select(output => output.GlslName).ToArray());
        Assert.Equal(
            new[] { "Surface_Uv", "Surface_Color" },
            fragment.Module!.Inputs.Where(input => input.Builtin is null).Select(input => input.GlslName).ToArray());
        Assert.Equal((0u, 1u), (fragment.Module.Inputs[1].Location, fragment.Module.Inputs[2].Location));
        Assert.Contains("Surface_Uv = vec2", vertex.Module.Body, StringComparison.Ordinal);
        Assert.Contains("Surface_Color = vec4", vertex.Module.Body, StringComparison.Ordinal);
        Assert.Contains("fragColor = Surface_Color", fragment.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NestedInterstagePayload_RejectsRepeatedLeafSymbols()
    {
        const string source = """
            using Delta.Maths;
            using Delta.Shader;

            public struct SharedSurface
            {
                public Uv0 Uv;
            }

            [Interstage]
            public struct SurfacePayload
            {
                public Position Position;
                public SharedSurface First;
                public SharedSurface Second;
            }

            public struct VertexContext
            {
                [Interstage]
                public SurfacePayload Vertex;
            }

            public static class RepeatedNestedGraphics
            {
                [VertexShader]
                public static SurfacePayload Vertex(in VertexContext context) => default;
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == ShaderDiagnosticId.DSH013 &&
            diagnostic.Message.Contains("present more than once", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NestedInterstagePayload_RejectsUnwrappedMappedTypes()
    {
        const string source = """
            using Delta.Maths;
            using Delta.Shader;

            public struct InvalidSurface
            {
                public float2 Uv;
            }

            [Interstage]
            public struct SurfacePayload
            {
                public Position Position;
                public InvalidSurface Surface;
            }

            public struct VertexContext
            {
                [Interstage]
                public SurfacePayload Vertex;
            }

            public static class InvalidNestedGraphics
            {
                [VertexShader]
                public static SurfacePayload Vertex(in VertexContext context) => default;
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == ShaderDiagnosticId.DSH013 &&
            diagnostic.Message.Contains("must use a Delta.Shader semantic type", StringComparison.Ordinal));
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

                public Position Position;
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

    [Fact]
    public async Task GraphicsHelper_AllowsDeltaMathsFieldSwizzles()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            [Interstage]
            public struct FragmentPayload
            {

                public Position Position;
            }

            public readonly struct FragmentContext
            {
                [Interstage]
                public readonly FragmentPayload Fragment;
            }

            public static class SwizzleHelperShader
            {
                [FragmentShader]
                public static float4 Fragment(in FragmentContext context)
                {
                    return ReadCandidate(new float4(1f, 2f, 3f, 4f));
                }

                private static float4 ReadCandidate(float4 candidate)
                {
                    return new float4(candidate.z, candidate.y, candidate.x, candidate.w);
                }
            }";

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains(result.Module!.HelperFunctions, helper => helper.Contains(".z", StringComparison.Ordinal));
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
