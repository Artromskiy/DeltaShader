using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Delta.Shader.Compiler;
using Delta.Shader.Compiler.Frontend;
using Delta.Shader.Compiler.Intrinsics;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public sealed class BindingAndBuiltinTests
{
    private static readonly string[] SurfacePayloadOutputs = ["Surface_Uv", "Surface_Color"];

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
            using Delta.Graphics.Semantics;

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
    public async Task ContractHelperBinding_LowersByAbiNameAndEmitsHelperImplementation()
    {
        const string source = """
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

            public readonly struct ComputeContext
            {
                [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<double> Input;
                [Layout(0, 1)] public readonly ReadWriteStorageBuffer<double> Output;
            }

            public static class DoubleHelperEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    context.Output[id] = maths.sin(context.Input[id]) + maths.atan2(context.Input[id], context.Input[id]);
                }
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderContractManifest contract = CreateMathsContract(
            new ShaderContractFunction
            {
                TypeClrName = "maths",
                ClrName = "sin",
                ReturnClrName = "double",
                ReturnGlslType = "double",
                ParameterClrNames = ["double"],
                ParameterGlslTypes = ["double"],
                GlslName = "delta_d_sin",
                Mapping = ShaderContractMapping.Helper,
                RequiredCapability = "float64",
                Stages = ["compute"]
            },
            new ShaderContractFunction
            {
                TypeClrName = "maths",
                ClrName = "atan2",
                ReturnClrName = "double",
                ReturnGlslType = "double",
                ParameterClrNames = ["double", "double"],
                ParameterGlslTypes = ["double", "double"],
                GlslName = "delta_d_atan2",
                Mapping = ShaderContractMapping.Helper,
                RequiredCapability = "float64",
                Stages = ["compute"]
            });
        IntrinsicRegistry registry = IntrinsicRegistry.Build(compilation, contract);
        IMethodSymbol sin = compilation.GetTypeByMetadataName("Delta.maths")!
            .GetMembers("sin").OfType<IMethodSymbol>().Single(method =>
                method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Double);

        Assert.True(registry.TryGetIntrinsic(sin, out IntrinsicBinding? binding));
        Assert.Equal(ShaderContractMapping.Helper, binding.Mapping);
        Assert.Equal("delta_d_sin", binding.GlslName);

        ShaderCompilationResult result = ComputeEntryPoints.ValidateAndBuild(
            new ModuleCompilationContext(compilation, registry),
            new RoslynFrontend(compilation),
            ShaderCompilationOptions.Default);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("delta_d_sin(Input.data[local_id])", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("delta_d_atan2(Input.data[local_id], Input.data[local_id])", result.Module.Body, StringComparison.Ordinal);
        Assert.Contains(result.Module.HelperFunctions, helper => helper.Contains("double delta_d_sin(double x)", StringComparison.Ordinal));
        Assert.Contains(result.Module.HelperFunctions, helper => helper.Contains("double delta_d_atan2(double y, double x)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContractBuiltinBinding_EmitsNativeCallWithoutHelperLowering()
    {
        const string source = """
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

            public readonly struct ComputeContext
            {
                [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<float> Input;
                [Layout(0, 1)] public readonly ReadWriteStorageBuffer<float> Output;
            }

            public static class FloatBuiltinEntry
            {
                [ComputeShader(64)]
                public static void Execute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    context.Output[id] = maths.sin(context.Input[id]);
                }
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        ShaderContractManifest contract = CreateMathsContract(
            new ShaderContractFunction
            {
                TypeClrName = "maths",
                ClrName = "sin",
                ReturnClrName = "float",
                ReturnGlslType = "float",
                ParameterClrNames = ["float"],
                ParameterGlslTypes = ["float"],
                GlslName = "sin",
                Mapping = ShaderContractMapping.Builtin,
                RequiredCapability = "scalar",
                Stages = ["compute"]
            });
        ShaderCompilationResult result = ComputeEntryPoints.ValidateAndBuild(
            new ModuleCompilationContext(compilation, IntrinsicRegistry.Build(compilation, contract)),
            new RoslynFrontend(compilation),
            ShaderCompilationOptions.Default);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("sin(Input.data[local_id])", result.Module!.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Module.HelperFunctions, helper => helper.Contains("delta_d_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ComputeBodySupportsLocalsAndNestedBoundsCheck()
    {
        const string source = @"
            using Delta.Shader;
            using Delta.Graphics.Semantics;

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
            using Delta.Graphics.Semantics;

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
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

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
            using Delta.Graphics.Semantics;

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
            using Delta.Graphics.Semantics;

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
            using Delta.Graphics.Semantics;

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
            using Delta.Graphics.Semantics;

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
            using Delta.Graphics.Semantics;

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
            using Delta.Graphics.Semantics;
            using Delta;

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
        Assert.Contains("vec4 local_color = vec4(1.0, 1.0, 1.0, 1.0);", result.Module!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VertexInput_UsesSingleArgumentBindingAndComputesSequentialOffsets()
    {
        const string source = @"
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

            [Interstage]
            public struct VertexPayload
            {

                [Layout(0)]
                public Position Position;
                [Layout(1)]
                public Uv0 Uv;
            }

            public readonly struct VertexContext
            {}

            public static class VertexEntry
            {
                [VertexShader]
                public static VertexPayload Execute(in VertexContext context, in VertexPayload input)
                {
                    return new VertexPayload
                    {
                        Position = new float4(input.Position.xyz, 1f),
                        Uv = input.Uv
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
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

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
            {}

            public struct FragmentContext
            {}

            public static class NestedGraphics
            {
                [VertexShader]
                public static SurfacePayload Vertex(in VertexContext context, in SurfacePayload input) => new SurfacePayload
                {
                    Position = new float4(0f, 0f, 0f, 1f),
                    Surface = new SurfaceData
                    {
                        Uv = new float2(0.5f, 0.5f),
                        Color = new float4(1f, 0f, 0f, 1f)
                    }
                };

                [FragmentShader]
                public static float4 Fragment(in FragmentContext context, in SurfacePayload input) => input.Surface.Color.Value;
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        ShaderCompilationResult vertex = Assert.Single(results, result => result.Module?.Stage == ShaderStage.Vertex);
        ShaderCompilationResult fragment = Assert.Single(results, result => result.Module?.Stage == ShaderStage.Fragment);

        Assert.True(vertex.Success, string.Join(Environment.NewLine, vertex.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(fragment.Success, string.Join(Environment.NewLine, fragment.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(
            SurfacePayloadOutputs,
            vertex.Module!.Outputs
                .Where(output => output.Builtin is null && output.GlslName is not ("Position" or "gl_Position"))
                .Select(output => output.GlslName)
                .ToArray());
        Assert.Equal(
            SurfacePayloadOutputs,
            fragment.Module!.Inputs
                .Where(input => input.Builtin is null && input.GlslName is not ("Position" or "gl_Position"))
                .Select(input => input.GlslName)
                .ToArray());
        Assert.Equal((0u, 1u), (fragment.Module.Inputs[1].Location, fragment.Module.Inputs[2].Location));
        Assert.Contains("Surface_Uv = vec2", vertex.Module.Body, StringComparison.Ordinal);
        Assert.Contains("Surface_Color = vec4", vertex.Module.Body, StringComparison.Ordinal);
        Assert.Contains("fragColor = Surface_Color", fragment.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NestedInterstagePayload_RejectsRepeatedLeafSymbols()
    {
        const string source = """
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

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
            {}

            public static class RepeatedNestedGraphics
            {
                [VertexShader]
                public static SurfacePayload Vertex(in VertexContext context, in SurfacePayload input) => default;
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
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

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
            {}

            public static class InvalidNestedGraphics
            {
                [VertexShader]
                public static SurfacePayload Vertex(in VertexContext context, in SurfacePayload input) => default;
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
    public async Task DomainOwnedInterstageValueWrappers_AreStructuralSemantics()
    {
        const string source = """
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

            namespace Consumer.Domain;

            public readonly struct DomainUv
            {
                public readonly float2 Value;
                public DomainUv(float2 value) => Value = value;
            }

            public readonly struct DomainTint
            {
                public float4 Value { get; init; }
                public DomainTint(float4 value) => Value = value;
            }

            [Interstage]
            public struct Surface
            {
                public Position Position;
                public DomainUv Uv;
                public DomainTint Tint;
            }

            public struct VertexContext {}
            public struct FragmentContext {}

            public static class DomainOwnedGraphics
            {
                [VertexShader("domain-owned")]
                public static Surface Vertex(in VertexContext context, in Surface input) =>
                    new Surface
                    {
                        Position = new Position(new float4(input.Uv.Value, 0f, 1f)),
                        Uv = input.Uv,
                        Tint = input.Tint
                    };

                [FragmentShader("domain-owned")]
                public static float4 Fragment(in FragmentContext context, in Surface input) =>
                    new float4(input.Uv.Value, 0f, 1f) * input.Tint.Value;
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);

        Assert.Equal(2, results.Count);
        Assert.All(results, result =>
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message))));
    }

    [Fact]
    public async Task UiCompositeBuildPlan_PreparesOrderedLayersAndGeneratedProgram()
    {
        const string source = """
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

            namespace Consumer.Composite;

            [Interstage]
            public struct Surface
            {
                public Position Position;
                public Uv0 Uv;
                public VertexColor Tint;
            }

            public struct Frame
            {
                public float Time;
            }

            public struct VertexContext {}
            public struct FragmentContext
            {
                [PushConstant]
                public Frame Frame;
            }

            public static class Layers
            {
                [VertexShader("geometry")]
                public static Surface Geometry(in VertexContext context, in Surface input) => new Surface
                {
                    Position = default,
                    Uv = default,
                    Tint = default
                };

                [FragmentShader("shade")]
                public static float4 Shade(in FragmentContext context, in Surface input) =>
                    input.Tint.Value * context.Frame.Time;

                [FragmentShader("pulse")]
                public static float4 Pulse(in FragmentContext context, in Surface input) =>
                    input.Tint.Value * (0.5f + context.Frame.Time);
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        var vertex = Assert.Single(results, result => result.Module?.Stage == ShaderStage.Vertex);
        var fragments = results.Where(result => result.Module?.Stage == ShaderStage.Fragment).ToArray();
        Assert.Equal(2, fragments.Length);
        var json = $$"""
            {
              "schema": 1,
              "composites": [
                {
                  "name": "PreparedUi",
                  "key": {
                    "target": "Visual",
                    "primitive": "Rounded",
                    "material": "FlatColor",
                    "textRepresentation": "None",
                    "effects": "Stroke",
                    "quality": "Analytic"
                  },
                  "vertexLayers": [{{System.Text.Json.JsonSerializer.Serialize(vertex.SourceMethodIdentity)}}],
                  "fragmentLayers": [
                    {{System.Text.Json.JsonSerializer.Serialize(fragments[0].SourceMethodIdentity)}},
                    {{System.Text.Json.JsonSerializer.Serialize(fragments[1].SourceMethodIdentity)}}
                  ]
                }
              ]
            }
            """;

        Assert.True(UiShaderCompositeBuildPlan.TryParse(json, out var plan, out var parseDiagnostics),
            string.Join(Environment.NewLine, parseDiagnostics.Select(diagnostic => diagnostic.Message)));
        var numericEffects = json.Replace("\"effects\": \"Stroke\"", "\"effects\": \"2\"", StringComparison.Ordinal);
        Assert.False(UiShaderCompositeBuildPlan.TryParse(
            numericEffects,
            out _,
            out var numericDiagnostics));
        Assert.Contains(numericDiagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH019);
        var entry = Assert.Single(plan!.Composites);
        UiShaderCompositeBuildPreparation preparation = UiShaderCompositeBuildPlanner.Prepare(entry, results);
        Assert.True(preparation.Success,
            string.Join(Environment.NewLine, preparation.Diagnostics.Select(diagnostic => diagnostic.Message)));

        INamedTypeSymbol layersType = compilation.GetTypeByMetadataName("Consumer.Composite.Layers")!;
        var vertexMethod = Assert.Single(layersType.GetMembers("Geometry").OfType<IMethodSymbol>());
        var fragmentMethods = entry.FragmentLayers
            .Select(identity => Assert.Single(layersType.GetMembers().OfType<IMethodSymbol>(), method =>
                results.Any(result => result.SourceMethodIdentity == identity && result.SourceMethodName == method.Name)))
            .ToArray();
        Assert.True(Delta.Shader.Analyzers.ShaderCompositeSourceGenerator.TryGenerateBuildUiVariant(
            entry,
            [vertexMethod],
            [vertex.BuildManifest!],
            fragmentMethods,
            preparation.FragmentLayers.Select(result => result.BuildManifest!).ToArray(),
            preparation.Variant!.Composition!,
            preparation.Variant.VariantIdentity,
            out var generated,
            out var generationReason), generationReason);
        Assert.Contains("class PreparedUiGraphicsShaderProgram", generated, StringComparison.Ordinal);
        Assert.Contains("PreparedUi.vert.spv", generated, StringComparison.Ordinal);
        Assert.Contains("PreparedUi.frag.spv", generated, StringComparison.Ordinal);
        Assert.Contains("PackPreparedUiFragmentLayer0", generated, StringComparison.Ordinal);
        Assert.Contains("PackPreparedUiFragmentLayer1", generated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindingLocation_IsRejectedInComputeContext()
    {
        const string source = @"
            using Delta.Shader;
            using Delta.Graphics.Semantics;

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
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

            [Interstage]
            public struct FragmentPayload
            {

                public Position Position;
            }

            public readonly struct FragmentContext
            {}

            public static class FragmentEntry
            {
                [FragmentShader]
                public static float4 Execute(in FragmentContext context, in FragmentPayload input) =>
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
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

            [Interstage]
            public struct FragmentPayload
            {

                public Position Position;
            }

            public readonly struct FragmentContext
            {}

            public static class SwizzleHelperShader
            {
                [FragmentShader]
                public static float4 Fragment(in FragmentContext context, in FragmentPayload input)
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

    [Fact]
    public async Task CompositeContextResolver_MergesSemanticFieldsWithoutFieldNames()
    {
        const string source = """
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

            [Interstage]
            public struct VertexPayload
            {
                public Position Position;
                public Uv0 VertexUv;
                public VertexColor Tint;
            }

            [Interstage]
            public struct FragmentPayload
            {
                public Position ScreenPosition;
                public Uv0 FragmentUv;
                public VertexColor ColorInput;
            }

            public struct Frame
            {
                public float4 Color;
            }

            public struct VertexContext
            {
                [Layout(0, 0)]
                public ReadOnlyStorageBuffer<float4> InstanceData;
                [PushConstant]
                public Frame Constants;
            }

            public struct FragmentContext
            {
                [Layout(0, 1)]
                public SampledTexture2D FragmentTexture;
                [PushConstant]
                public Frame Constants;
            }

            public static class GrassLayers
            {
                [VertexShader("grass-vertex")]
                public static VertexPayload Vertex(in VertexContext context, in VertexPayload input) => new VertexPayload
                {
                    Position = new float4(0f, 0f, 0f, 1f),
                    VertexUv = new float2(0f, 0f),
                    Tint = new float4(1f, 1f, 1f, 1f)
                };

                [FragmentShader("grass-fragment")]
                public static float4 Fragment(in FragmentContext context, in FragmentPayload input) =>
                    input.ColorInput.Value * context.Constants.Color;
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        Assert.All(results, result =>
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message))));

        ShaderCompositeContextResolution resolution = ShaderCompiler.ResolveCompositeContext(results);

        Assert.True(resolution.Success, string.Join(Environment.NewLine, resolution.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(6, resolution.Fields.Count);
        ShaderCompositeContextField position = Assert.Single(resolution.Fields,
            field => field.TypeIdentity.EndsWith("Delta.Shader.Position", StringComparison.Ordinal));
        Assert.False(position.HostProvided);
        ShaderCompositeContextField vertexResource = Assert.Single(resolution.Fields,
            field => field.Kind == ShaderCompositeContextFieldKind.Resource && field.SourcePath == "InstanceData");
        Assert.True(vertexResource.HostProvided);
        Assert.Equal((uint?)0, vertexResource.Set);
        Assert.Equal((uint?)0, vertexResource.Binding);
        ShaderCompositeContextField fragmentResource = Assert.Single(resolution.Fields,
            field => field.Kind == ShaderCompositeContextFieldKind.Resource && field.SourcePath == "FragmentTexture");
        Assert.True(fragmentResource.HostProvided);
        Assert.Equal((uint?)0, fragmentResource.Set);
        Assert.Equal((uint?)1, fragmentResource.Binding);
        ShaderCompositeContextField push = Assert.Single(resolution.Fields,
            field => field.Kind == ShaderCompositeContextFieldKind.PushConstant);
        Assert.Equal(2, push.Stages.Count);

        ShaderCompositeCompilationResult composite = ShaderCompiler.ComposeGraphics(
            results.Where(result => result.Module?.Stage == ShaderStage.Vertex).ToArray(),
            results.Where(result => result.Module?.Stage == ShaderStage.Fragment).ToArray());

        Assert.True(composite.Success, string.Join(Environment.NewLine, composite.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.StartsWith("ui-composite:", composite.VariantIdentity, StringComparison.Ordinal);
        Assert.NotNull(composite.Vertex);
        Assert.NotNull(composite.Fragment);
        Assert.Contains("composite_field_1", composite.Vertex!.Body, StringComparison.Ordinal);
        Assert.Contains("composite_field_1", composite.Fragment!.Body, StringComparison.Ordinal);
        string vertexGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(composite.Vertex).Source;
        string fragmentGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(composite.Fragment).Source;
        Assert.Contains("layout(location = 1) out vec4 composite_field_1;", vertexGlsl, StringComparison.Ordinal);
        Assert.Contains("layout(location = 1) in vec4 composite_field_1;", fragmentGlsl, StringComparison.Ordinal);

        ShaderCompilationManifest vertexManifest = composite.GetBuildManifest(ShaderStage.Vertex);
        ShaderCompilationManifest fragmentManifest = composite.GetBuildManifest(ShaderStage.Fragment);
        Assert.Contains(vertexManifest.Resources, resource => resource.ParameterName == "InstanceData");
        Assert.DoesNotContain(vertexManifest.Resources, resource => resource.ParameterName == "FragmentTexture");
        Assert.Contains(fragmentManifest.Resources, resource => resource.ParameterName == "FragmentTexture");
        Assert.DoesNotContain(fragmentManifest.Resources, resource => resource.ParameterName == "InstanceData");
        Assert.Single(vertexManifest.PushConstants);
        Assert.Single(fragmentManifest.PushConstants);
    }

    [Fact]
    public async Task CompositeContextResolver_RejectsFragmentFieldWithoutVertexProducer()
    {
        const string source = """
            using Delta;
            using Delta.Shader;
            using Delta.Graphics.Semantics;

            [Interstage]
            public struct VertexPayload
            {
                public Position Position;
            }

            [Interstage]
            public struct FragmentPayload
            {
                public Position Position;
                public Uv0 MissingUv;
            }

            public struct VertexContext
            {}

            public struct FragmentContext
            {}

            public static class InvalidComposite
            {
                [VertexShader("producer")]
                public static VertexPayload Vertex(in VertexContext context, in VertexPayload input) => default;

                [FragmentShader("consumer")]
                public static float4 Fragment(in FragmentContext context, in FragmentPayload input) =>
                    new float4(input.MissingUv.Value, 0f, 1f);
            }
            """;

        Compilation compilation = await LoadCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        ShaderCompositeContextResolution resolution = ShaderCompiler.ResolveCompositeContext(results);

        Assert.False(resolution.Success);
        Assert.Contains(resolution.Diagnostics, diagnostic =>
            diagnostic.Id == ShaderDiagnosticId.DSH013 &&
            diagnostic.Message.Contains("has no vertex producer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GrassCompositeSample_ComposesEveryFragmentVariant()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        string projectPath = Path.Combine(
            FindRepositoryRoot(),
            "DeltaShader",
            "samples",
            "DeltaShader.GrassComposite",
            "DeltaShader.GrassComposite.csproj");
        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        Project project = await workspace.OpenProjectAsync(projectPath).ConfigureAwait(true);
        Compilation? compilation = await project.GetCompilationAsync().ConfigureAwait(true);
        Assert.NotNull(compilation);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation!);
        ShaderCompilationResult vertex = Assert.Single(results,
            result => result.Module?.Stage == ShaderStage.Vertex);
        ShaderCompilationResult[] fragments = results
            .Where(result => result.Module?.Stage == ShaderStage.Fragment)
            .ToArray();

        Assert.Equal(7, fragments.Length);
        Assert.All(fragments, fragment =>
        {
            ShaderCompositeCompilationResult composite = ShaderCompiler.ComposeGraphics(
                [vertex],
                [fragment]);
            Assert.True(composite.Success,
                string.Join(Environment.NewLine, composite.Diagnostics.Select(diagnostic => diagnostic.Message)));
            Assert.NotNull(composite.Vertex);
            Assert.NotNull(composite.Fragment);
            Assert.True(Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(composite.Vertex!).Success);
            Assert.True(Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(composite.Fragment!).Success);

            Assert.Equal("main", composite.GetBuildManifest(ShaderStage.Vertex).EntryPointName);
            Assert.Equal("main", composite.GetBuildManifest(ShaderStage.Fragment).EntryPointName);
        });

        ShaderCompilationResult fragmentLayer = fragments[0];
        ShaderCompositeCompilationResult selectedComposite = ShaderCompiler.ComposeGraphics(
            [vertex],
            [fragmentLayer]);
        INamedTypeSymbol layerType = compilation!.GetTypeByMetadataName(
            "Delta.Shader.GrassComposite.GrassCompositeLayers")!;
        IMethodSymbol vertexMethod = Assert.Single(layerType.GetMembers("TransformAndInstance").OfType<IMethodSymbol>());
        IMethodSymbol fragmentMethod = Assert.Single(layerType.GetMembers("TexturedLambert").OfType<IMethodSymbol>());
        Assert.True(Delta.Shader.Analyzers.ShaderCompositeSourceGenerator.TryGenerate(
            "GrassSelectedComposite",
            vertexMethod,
            fragmentMethod,
            selectedComposite,
            out string generatedSource,
            out string? generationReason,
            "visual/rounded/flatcolor/none/10/analytic|" + selectedComposite.VariantIdentity), generationReason);
        Assert.Contains("class GrassSelectedComposite", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackGrassSelectedCompositeVertex", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackGrassSelectedCompositeFragment", generatedSource, StringComparison.Ordinal);
        Assert.Contains("VariantIdentity", generatedSource, StringComparison.Ordinal);
        Assert.Contains("visual/rounded/flatcolor/none/10/analytic|", generatedSource, StringComparison.Ordinal);
        Assert.Contains("public static Delta.Shader.Contract.ShaderAbi VertexAbi", generatedSource, StringComparison.Ordinal);
        Assert.Contains("public static Delta.Shader.Contract.ShaderAbi FragmentAbi", generatedSource, StringComparison.Ordinal);

        Assert.True(Delta.Shader.Analyzers.ShaderCompositeSourceGenerator.TryGenerateUiVariant(
            "GrassPreparedUiComposite",
            vertexMethod,
            fragmentMethod,
            selectedComposite,
            "visual/rounded/flatcolor/none/10/analytic|" + selectedComposite.VariantIdentity,
            out string uiGeneratedSource,
            out string? uiGenerationReason), uiGenerationReason);
        Assert.DoesNotContain("Directory.GetFiles", uiGeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetVertexSpirv", uiGeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateProgram()", uiGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("CreateProgram(", uiGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("ReadOnlySpan<byte> vertexSpirv", uiGeneratedSource, StringComparison.Ordinal);

        CSharpParseOptions parseOptions = compilation.SyntaxTrees.First().Options as CSharpParseOptions
            ?? CSharpParseOptions.Default;
        Compilation generatedCompilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(generatedSource, parseOptions));
        Assert.Empty(generatedCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static ShaderContractManifest CreateMathsContract(params ShaderContractFunction[] functions)
        => new()
        {
            Namespace = "Delta",
            Functions = functions
        };

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
