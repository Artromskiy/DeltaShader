using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Delta.Maths;
using Delta.Shader;
using Delta.Shader.Analyzers;
using Delta.Shader.Compiler.Intrinsics;
using Delta.Shader.Compiler.IR;
using Delta.Shader.Compiler.Syntax;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public class IntrinsicCatalogTests
{
    private struct HostTransformBase
    {
        public float3 Position = default;

        public HostTransformBase()
        {
        }
    }

    private struct HostTransformRecord
    {
        public HostTransformBase Base = new();
        public quaternion Rotation = default;
        public float4x4 Transform = default;

        public HostTransformRecord()
        {
        }
    }

    [Fact]
    public void ClosedMetadata_UsesTypedKinds_AndPreservesWireNames()
    {
        var manifest = new ShaderCompilationManifest
        {
            Resources =
            [
                new ShaderCompilationResource
                {
                    Name = "values",
                    Category = "storage-buffer"
                }
            ]
        };

        var json = JsonSerializer.Serialize(manifest);
        Assert.Contains("\"Category\":\"storage-buffer\"", json, StringComparison.Ordinal);

        var roundTrip = JsonSerializer.Deserialize<ShaderCompilationManifest>(json);
        Assert.NotNull(roundTrip);
        Assert.Equal(ShaderResourceKind.StorageBuffer, ShaderResourceKindExtensions.ParseMetadataName(Assert.Single(roundTrip.Resources).Category));

        var unknownResourceJson = json.Replace("storage-buffer", "future-resource", StringComparison.Ordinal);
        var unknownResourceManifest = JsonSerializer.Deserialize<ShaderCompilationManifest>(unknownResourceJson);
        Assert.NotNull(unknownResourceManifest);
        Assert.Equal(ShaderResourceKind.Unknown, ShaderResourceKindExtensions.ParseMetadataName(Assert.Single(unknownResourceManifest.Resources).Category));

        var unknownMapping = JsonSerializer.Deserialize<ShaderContractType>("{\"mapping\":\"future-mapping\"}");
        Assert.NotNull(unknownMapping);
        Assert.Equal(ShaderContractMapping.Unknown, unknownMapping.Mapping);
    }

    [Fact]
    public async Task DeltaMaths_VectorTypes_AreMappedTo_GlslVectorTypes_BySymbolIdentity()
    {
        Compilation compilation = await LoadDeltaMathsCompilationAsync().ConfigureAwait(true);
        var registry = IntrinsicRegistry.Build(compilation);

        INamedTypeSymbol? float2 = compilation.GetTypeByMetadataName("Delta.Maths.float2");
        INamedTypeSymbol? int3 = compilation.GetTypeByMetadataName("Delta.Maths.int3");

        Assert.NotNull(float2);
        Assert.NotNull(int3);
        Assert.True(registry.TryMapType(float2!, out var glslFloat2));
        Assert.True(registry.TryMapType(int3!, out var glslInt3));
        Assert.Equal("vec2", glslFloat2);
        Assert.Equal("ivec3", glslInt3);
    }

    [Fact]
    public async Task DeltaMaths_DeltaMathsFunctions_AreMatchedByISymbol_AndMapOverloads()
    {
        Compilation compilation = await LoadDeltaMathsCompilationAsync().ConfigureAwait(true);
        var registry = IntrinsicRegistry.Build(compilation);
        INamedTypeSymbol maths = compilation.GetTypeByMetadataName("Delta.Maths.maths")!;

        IMethodSymbol sinFloat = maths.GetMembers("sin").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Single);
        IMethodSymbol dotFloat3 = maths.GetMembers("dot").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 2 &&
            m.Parameters[0].Type.Name == "float3" &&
            m.Parameters[1].Type.Name == "float3");
        IMethodSymbol dotFloat4 = maths.GetMembers("dot").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 2 &&
            m.Parameters[0].Type.Name == "float4" &&
            m.Parameters[1].Type.Name == "float4");

        Assert.NotNull(sinFloat);
        Assert.NotNull(dotFloat3);
        Assert.NotNull(dotFloat4);
        Assert.True(registry.TryGetIntrinsic(sinFloat, out IntrinsicBinding? sinIntrinsic));
        Assert.True(registry.TryGetIntrinsic(dotFloat3, out IntrinsicBinding? dot3Intrinsic));
        Assert.True(registry.TryGetIntrinsic(dotFloat4, out IntrinsicBinding? dot4Intrinsic));
        Assert.Equal("sin", sinIntrinsic.GlslName);
        Assert.Equal("dot", dot3Intrinsic.GlslName);
        Assert.Equal("dot", dot4Intrinsic.GlslName);
        Assert.Equal(dot3Intrinsic.GlslName, dot4Intrinsic.GlslName);
    }

    [Fact]
    public async Task DeltaMaths_VectorConstructors_Operators_Swizzles_AreSymbolMapped()
    {
        Compilation compilation = await LoadDeltaMathsCompilationAsync().ConfigureAwait(true);
        var registry = IntrinsicRegistry.Build(compilation);
        INamedTypeSymbol float4 = compilation.GetTypeByMetadataName("Delta.Maths.float4")!;
        INamedTypeSymbol float3 = compilation.GetTypeByMetadataName("Delta.Maths.float3")!;

        IMethodSymbol ctorByScalars = float4.InstanceConstructors.First(c =>
            c.Parameters.Length == 4 &&
            c.Parameters.All(p => p.Type.SpecialType == SpecialType.System_Single));
        IMethodSymbol ctorByFloat3AndScalar = float4.InstanceConstructors.First(c =>
            c.Parameters.Length == 2 &&
            c.Parameters[0].Type.Name == "float3" &&
            c.Parameters[1].Type.SpecialType == SpecialType.System_Single);
        IMethodSymbol plus = float4.GetMembers().OfType<IMethodSymbol>().First(m =>
            m.MethodKind == MethodKind.UserDefinedOperator &&
            m.Name == "op_Addition" &&
            m.Parameters.Length == 2 &&
            m.Parameters.All(p => p.Type.Name == "float4"));
        IPropertySymbol? swizzle = float3.GetMembers("xyz").OfType<IPropertySymbol>().FirstOrDefault();

        Assert.NotNull(float4);
        Assert.NotNull(float3);
        Assert.NotNull(swizzle);
        Assert.True(registry.TryGetIntrinsic(ctorByScalars, out _));
        Assert.True(registry.TryGetIntrinsic(ctorByFloat3AndScalar, out _));
        Assert.True(registry.TryGetIntrinsic(plus, out _));
        Assert.True(registry.TryGetIntrinsic(swizzle!, out _));
    }

    [Fact]
    public async Task DeltaMaths_IdentityContract_IgnoresNameCollisionWithoutISymbolMatch()
    {
        var fixtureSource = @"
            using Delta.Maths;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class DeltaMathsNameCollision
                {
                    public static float sin(float x) => x;
                    public static float dot(float3 a, float3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
                }
            }
            ";

        Compilation compilation = await LoadDeltaMathsCompilationAsync(fixtureSource).ConfigureAwait(true);
        var registry = IntrinsicRegistry.Build(compilation);
        INamedTypeSymbol deltaDeltaMaths = compilation.GetTypeByMetadataName("Delta.Maths.maths")!;
        INamedTypeSymbol fakeDeltaMaths = compilation.GetTypeByMetadataName("Delta.Shader.Compiler.Tests.Fixtures.DeltaMathsNameCollision")!;
        IMethodSymbol deltaSin = deltaDeltaMaths.GetMembers("sin").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Single);
        IMethodSymbol fakeSin = fakeDeltaMaths.GetMembers("sin").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Single);
        IMethodSymbol fakeDot = fakeDeltaMaths.GetMembers("dot").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 2 &&
            m.Parameters[0].Type.Name == "float3" &&
            m.Parameters[1].Type.Name == "float3");

        Assert.True(registry.TryGetIntrinsic(deltaSin, out _));
        Assert.False(registry.TryGetIntrinsic(fakeSin, out _));
        Assert.False(registry.TryGetIntrinsic(fakeDot, out _));
    }

    [Fact]
    public async Task DeltaMaths_IntrinsicRegistry_MapsReferenceProjectSymbolsBySymbolIdentity()
    {
        Compilation compilation = await LoadReferenceFixtureCompilationAsync().ConfigureAwait(true);
        var registry = IntrinsicRegistry.Build(compilation);

        INamedTypeSymbol? fixtureType = compilation.GetTypeByMetadataName("Delta.Shader.Compiler.ReferenceFixtures.VectorSymbolFixture");
        IMethodSymbol? method = fixtureType?.GetMembers("SymbolMapKernel").OfType<IMethodSymbol>().SingleOrDefault();

        Assert.NotNull(fixtureType);
        var resolvedMethod = method ?? throw new InvalidOperationException("SymbolMapKernel fixture method was not found.");
        var syntaxReference = resolvedMethod.DeclaringSyntaxReferences.FirstOrDefault()
            ?? throw new InvalidOperationException("SymbolMapKernel fixture syntax was not found.");

        var syntax = (MethodDeclarationSyntax)await syntaxReference.GetSyntaxAsync().ConfigureAwait(true);
        SemanticModel semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);

        var constructors = syntax
            .DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Select(e => semanticModel.GetSymbolInfo(e).Symbol as IMethodSymbol)
            .Where(m => m?.ContainingType is not null)
            .Where(m => m!.ContainingType.ContainingNamespace?.ToDisplayString() == "Delta.Maths")
            .ToList();

        var operators = syntax
            .DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Select(e => semanticModel.GetSymbolInfo(e).Symbol as IMethodSymbol)
            .Where(m => m is not null)
            .Where(m => m!.Name == "op_Addition")
            .ToList();

        var swizzles = syntax
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Select(e => semanticModel.GetSymbolInfo(e).Symbol as IPropertySymbol)
            .Where(p => p is not null)
            .Where(p => p!.ContainingType?.ContainingNamespace?.ToDisplayString() == "Delta.Maths")
            .ToList();

        var mathsCalls = syntax
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(e => semanticModel.GetSymbolInfo(e).Symbol as IMethodSymbol)
            .Where(m => m is not null)
            .Where(m => m!.ContainingType is not null && m.ContainingType.Name == "maths")
            .ToList();

        Assert.Equal(2, constructors.Count(c => c?.ContainingType.Name is "float3" or "float2"));
        Assert.All(constructors, constructor => Assert.True(registry.TryGetIntrinsic(constructor!, out _)));

        Assert.NotEmpty(operators);
        Assert.All(operators, op => Assert.True(registry.TryGetIntrinsic(op!, out _)));

        Assert.Contains(swizzles, swizzle => swizzle!.Name == "xy");
        Assert.All(swizzles, swizzle => Assert.True(registry.TryGetIntrinsic(swizzle!, out _)));

        Assert.Contains(mathsCalls, call => call!.Name == "dot");
        Assert.Contains(mathsCalls, call => call!.Name == "normalize");
        Assert.All(mathsCalls, call => Assert.True(registry.TryGetIntrinsic(call!, out _)));
    }

    [Fact]
    public async Task DeltaMaths_ShaderContract_MapsMatrixQuaternionOverloadsByFullSignature()
    {
        Compilation compilation = await LoadDeltaMathsCompilationAsync().ConfigureAwait(true);
        var contract = ShaderContractManifest.LoadEmbedded();
        var registry = IntrinsicRegistry.Build(compilation, contract);

        INamedTypeSymbol matrix = compilation.GetTypeByMetadataName("Delta.Maths.float4x4")!;
        INamedTypeSymbol quaternion = compilation.GetTypeByMetadataName("Delta.Maths.quaternion")!;
        ShaderContractType matrixType = contract.Types.Single(type => type.ClrName == "float4x4");
        ShaderContractType quaternionType = contract.Types.Single(type => type.ClrName == "quaternion");

        Assert.Equal("mat4", matrixType.GlslName);
        Assert.Equal("vec4", quaternionType.GlslName);
        Assert.Equal(ShaderContractMapping.Builtin, matrixType.Mapping);
        Assert.Equal(ShaderContractMapping.Builtin, quaternionType.Mapping);

        IMethodSymbol matrixMultiply = matrix.GetMembers().OfType<IMethodSymbol>().Single(method =>
            method.MethodKind == MethodKind.UserDefinedOperator &&
            method.Name == "op_Multiply" &&
            method.Parameters.Length == 2 &&
            method.Parameters.All(parameter => parameter.Type.Name == "float4x4"));
        IMethodSymbol quaternionMultiply = quaternion.GetMembers().OfType<IMethodSymbol>().Single(method =>
            method.MethodKind == MethodKind.UserDefinedOperator &&
            method.Name == "op_Multiply" &&
            method.Parameters.All(parameter => parameter.Type.Name == "quaternion"));

        Assert.True(registry.TryGetIntrinsic(matrixMultiply, out IntrinsicBinding? matrixBinding));
        Assert.True(registry.TryGetIntrinsic(quaternionMultiply, out IntrinsicBinding? quaternionBinding));
        Assert.Equal("*", matrixBinding.GlslName);
        Assert.Equal("matrix", matrixBinding.RequiredCapability);
        Assert.Equal("delta_quaternionMultiply", quaternionBinding.GlslName);
        Assert.Equal("quaternion", quaternionBinding.RequiredCapability);
    }

    [Fact]
    public async Task IntrinsicRegistry_DoesNotRegisterUnsupportedContractIdentities()
    {
        Compilation compilation = await LoadDeltaMathsCompilationAsync().ConfigureAwait(true);
        var contract = new ShaderContractManifest
        {
            Namespace = "Delta.Maths",
            Types = [new ShaderContractType { ClrName = "float2", GlslName = "vec2", Mapping = ShaderContractMapping.Unsupported }],
            Functions = [new ShaderContractFunction
            {
                TypeClrName = "maths",
                ClrName = "sin",
                ReturnClrName = "float",
                ParameterClrNames = ["float"],
                GlslName = "sin",
                Mapping = ShaderContractMapping.Unsupported
            }]
        };

        var registry = IntrinsicRegistry.Build(compilation, contract);
        INamedTypeSymbol? float2 = compilation.GetTypeByMetadataName("Delta.Maths.float2");
        INamedTypeSymbol? maths = compilation.GetTypeByMetadataName("Delta.Maths.maths");
        IMethodSymbol sin = maths!.GetMembers("sin").OfType<IMethodSymbol>().Single(method =>
            method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Single);

        Assert.False(registry.TryMapType(float2!, out _));
        Assert.False(registry.TryGetIntrinsic(sin, out _));
    }

    [Fact]
    public async Task IntrinsicRegistry_MapsListedScalarOverload()
    {
        Compilation compilation = await LoadDeltaMathsCompilationAsync().ConfigureAwait(true);
        var registry = IntrinsicRegistry.Build(compilation, ShaderContractManifest.LoadEmbedded());
        var maths = compilation.GetTypeByMetadataName("Delta.Maths.maths")
            ?? throw new InvalidOperationException("Delta.Maths.maths was not found in the test compilation.");
        var scalarAbs = maths.GetMembers("abs").OfType<IMethodSymbol>().Single(method =>
            method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Single);

        Assert.True(registry.TryGetIntrinsic(scalarAbs, out IntrinsicBinding? binding));
        Assert.Equal("abs", binding.GlslName);
        Assert.Equal(["float"], binding.ParameterGlslTypes);
        Assert.Equal("float", binding.ReturnGlslType);
    }

    [Fact]
    public async Task IntrinsicRegistry_MapsDeltaMathsVectorFacadeByFullSignature()
    {
        Compilation compilation = await LoadDeltaMathsCompilationAsync().ConfigureAwait(true);
        var registry = IntrinsicRegistry.Build(compilation, ShaderContractManifest.LoadEmbedded());
        var maths = compilation.GetTypeByMetadataName("Delta.Maths.maths")
            ?? throw new InvalidOperationException("Delta.Maths.maths was not found in the test compilation.");
        var vectorAbs = maths.GetMembers("abs").OfType<IMethodSymbol>().Single(method =>
            method.Parameters.Length == 1 && method.Parameters[0].Type.Name == "float2");

        Assert.True(registry.TryGetIntrinsic(vectorAbs, out IntrinsicBinding? binding));
        Assert.Equal("abs", binding.GlslName);
        Assert.Equal(["vec2"], binding.ParameterGlslTypes);
        Assert.Equal("vec2", binding.ReturnGlslType);
    }

    [Fact]
    public async Task ComputeEntryPoint_ResourcesUseSetBindingAndGlslTypeFromSymbol()
    {
        var source = @"
            using Delta.Maths;
            using Delta.Shader;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class StorageBufferEntry
                {
                    public readonly struct ComputeContext
                    {
                        [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<float3> input;
                        [Layout(0, 1)] public readonly ReadWriteStorageBuffer<uint2> output;
                    }

                    [ComputeShader(localSizeX: 8, localSizeY: 2, localSizeZ: 4)]
                    public static void Compute(in ComputeContext context)
                    {
                    }
                }
            }
            ";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);
        ShaderIrModule module = result.Module!;

        Assert.True(result.Success);
        Assert.Equal("Compute", result.EntryPointName);
        Assert.Equal(8u, module.LocalSizeX);
        Assert.Equal(2u, module.LocalSizeY);
        Assert.Equal(4u, module.LocalSizeZ);
        Assert.Equal(2, module.Resources.Count);

        ShaderIrResource input = module.Resources.First(r => r.ParameterName == "context.input");
        ShaderIrResource output = module.Resources.First(r => r.ParameterName == "context.output");
        Assert.Equal(0u, input.Set);
        Assert.Equal(0u, input.Binding);
        Assert.True(input.ReadOnly);
        Assert.Equal("vec3", input.GlslType);
        Assert.Equal(0u, output.Set);
        Assert.Equal(1u, output.Binding);
        Assert.False(output.ReadOnly);
        Assert.Equal("uvec2", output.GlslType);
    }

    [Fact]
    public async Task ComputeEntryPoint_Rejects_ManagedType_WithExplicitDiagnostic()
    {
        var source = @"
            using Delta.Maths;
            using Delta.Shader;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class InvalidTypesEntry
                {
                    public readonly struct ComputeContext
                    {
                        [PushConstant] public readonly string ManagedValue;
                    }

                    [ComputeShader]
                    public static void Compute(in ComputeContext context) { }
                }
            }
        ";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == ShaderDiagnosticId.DSH010 &&
            diagnostic.Message.Contains("string", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ComputeEntryPoint_Rejects_OrdinaryParameters()
    {
        var source = @"
            using Delta.Maths;
            using Delta.Shader;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class InvalidParamEntry
                {
                    [ComputeShader]
                    public static void Compute(
                        [Layout(0, 0)] ReadOnlyStorageBuffer<uint> input,
                        uint invocationIndex)
                    {
                    }
                }
            }
            ";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == ShaderDiagnosticId.DSH002);
    }

    [Fact]
    public async Task ComputeEntryPoint_Rejects_InvalidProfilePair()
    {
        var source = @"
            using Delta.Maths;
            using Delta.Shader;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class ProfileMismatch
                {
                    [ComputeShader(localSizeX: 1)]
                    public static void Compute(
                        [Layout(0, 0)] ReadOnlyStorageBuffer<float> input)
                    {
                    }
                }
            }
            ";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(
            source,
            new ShaderCompilationOptions { Profile = "vulkan1.2", Spirv = "1.6" });

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == ShaderDiagnosticId.DSH007);
    }

    [Fact]
    public async Task ComputeEntryPoint_RejectsDuplicateBinding()
    {
        var source = @"
            using Delta.Maths;
            using Delta.Shader;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class DuplicateBindingEntry
                {
                    public readonly struct ComputeContext
                    {
                        [Layout(1, 0)] public readonly ReadOnlyStorageBuffer<float> First;
                        [Layout(1, 0)] public readonly ReadWriteStorageBuffer<float> Second;
                    }

                    [ComputeShader]
                    public static void Compute(in ComputeContext context)
                    { }
                }
            }
            ";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == ShaderDiagnosticId.DSH005);
    }

    [Fact]
    public async Task ComputeEntryPoint_BuildsStructuredStd430RecordWithDeltaMathsTypes()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public struct TransformRecord
                {
                    public TransformBase Base;
                    public quaternion Rotation;
                    public float4x4 Transform;
                }

                public struct TransformBase
                {
                    public float3 Position;
                }

                public static class StructuredEntry
                {
                    public readonly struct ComputeContext
                    {
                        [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<TransformRecord> input;
                        [Layout(0, 1)] public readonly ReadWriteStorageBuffer<TransformRecord> output;
                    }

                    [ComputeShader(localSizeX: 8)]
                    public static void Compute(in ComputeContext context)
                    {
                        uint invocation = ShaderBuiltins.GlobalInvocationId.X;
                        if (invocation < context.input.Length)
                            context.output[invocation] = context.input[invocation];
                    }
                }
            }
            ";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);

        Assert.True(result.Success);
        ShaderIrResource input = Assert.Single(result.Module!.Resources, resource => resource.ParameterName == "context.input");
        Assert.Equal("DeltaStruct_Delta_Shader_Compiler_Tests_Fixtures_TransformRecord", input.GlslType);
        Assert.Equal(16u, input.Std430Layout!.Alignment);
        Assert.Equal(96u, input.Std430Layout.Size);
        Assert.Equal(96u, input.Std430Layout.ArrayStride);
        Assert.Equal(3, input.Members.Count);
        Assert.Equal(("Base", "DeltaStruct_Delta_Shader_Compiler_Tests_Fixtures_TransformBase", 0u, 16u, 16u), (input.Members[0].Name, input.Members[0].GlslType, input.Members[0].Offset, input.Members[0].Alignment, input.Members[0].Size));
        Assert.Single(input.Members[0].Members);
        Assert.Equal(("Position", "vec3", 0u, 16u, 12u), (input.Members[0].Members[0].Name, input.Members[0].Members[0].GlslType, input.Members[0].Members[0].Offset, input.Members[0].Members[0].Alignment, input.Members[0].Members[0].Size));
        Assert.Equal(("Rotation", "vec4", 16u, 16u, 16u), (input.Members[1].Name, input.Members[1].GlslType, input.Members[1].Offset, input.Members[1].Alignment, input.Members[1].Size));
        Assert.Equal(("Transform", "mat4", 32u, 16u, 64u), (input.Members[2].Name, input.Members[2].GlslType, input.Members[2].Offset, input.Members[2].Alignment, input.Members[2].Size));
        Assert.Equal(16u, input.Members[2].MatrixStride);
    }

    [Fact]
    public async Task ComputeEntryPoint_RejectsUnsupportedStructLayoutsAndFields()
    {
        (string Source, string ExpectedId)[] cases = new[]
        {
            (Source: @"
                using Delta.Shader;
                using System.Runtime.InteropServices;
                namespace Delta.Shader.Compiler.Tests.Fixtures
                {
                    [StructLayout(LayoutKind.Explicit)]
                    public struct ExplicitRecord
                    {
                        [FieldOffset(0)] public float Value;
                    }
                    public static class ExplicitEntry
                    {
                        public struct Context { [Layout(0, 0)] public ReadOnlyStorageBuffer<ExplicitRecord> Input; }
                        [ComputeShader] public static void Compute(in Context context) { }
                    }
                }
            ", ExpectedId: ShaderDiagnosticId.DSH002),
            (Source: @"
                using Delta.Shader;
                namespace Delta.Shader.Compiler.Tests.Fixtures
                {
                    public struct ManagedRecord { public string Name; }
                    public static class ManagedEntry
                    {
                        public struct Context { [Layout(0, 0)] public ReadOnlyStorageBuffer<ManagedRecord> Input; }
                        [ComputeShader] public static void Compute(in Context context) { }
                    }
                }
            ", ExpectedId: ShaderDiagnosticId.DSH010),
            (Source: @"
                using Delta.Shader;
                namespace Delta.Shader.Compiler.Tests.Fixtures
                {
                    public struct RecursiveRecord { public RecursiveRecord[] Children; }
                    public static class RecursiveEntry
                    {
                        public struct Context { [Layout(0, 0)] public ReadOnlyStorageBuffer<RecursiveRecord> Input; }
                        [ComputeShader] public static void Compute(in Context context) { }
                    }
                }
            ", ExpectedId: ShaderDiagnosticId.DSH010),
            (Source: @"
                using Delta.Shader;
                namespace Delta.Shader.Compiler.Tests.Fixtures
                {
                    public struct ArrayFieldRecord { public float[] Values; }
                    public static class ArrayFieldEntry
                    {
                        public struct Context { [Layout(0, 0)] public ReadOnlyStorageBuffer<ArrayFieldRecord> Input; }
                        [ComputeShader] public static void Compute(in Context context) { }
                    }
                }
            ", ExpectedId: ShaderDiagnosticId.DSH010)
        };

        foreach ((string Source, string ExpectedId) testCase in cases)
        {
            ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(testCase.Source).ConfigureAwait(true);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == testCase.ExpectedId);
        }
    }

    [Fact]
    public async Task ShaderVisibleTypeValidation_RejectsEntryParameterAndStorageReferenceGraphs()
    {
        var cases = new[]
        {
            @"
                using Delta.Shader;
                public static class EntryParameter
                {
                    public struct Context { [PushConstant] public string Value; }
                    [ComputeShader] public static void Compute(in Context context) { }
                }
            ",
            @"
                using Delta.Shader;
                public class CpuOnlyHelper { public string Name; }
                public struct StorageRecord { public CpuOnlyHelper Helper; }
                public static class StorageEntry
                {
                    public struct Context { [Layout(0, 0)] public ReadOnlyStorageBuffer<StorageRecord> Values; }
                    [ComputeShader] public static void Compute(in Context context) { }
                }
            ",
            @"
                using Delta.Shader;
                public struct RecursiveRecord { public RecursiveRecord[] Children; }
                public static class RecursiveEntry
                {
                    public struct Context { [Layout(0, 0)] public ReadOnlyStorageBuffer<RecursiveRecord> Values; }
                    [ComputeShader] public static void Compute(in Context context) { }
                }
            "
        };

        foreach (var source in cases)
        {
            ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH010);
        }
    }

    [Fact]
    public async Task ShaderVisibleTypeValidation_RejectsPushConstantReferencesButIgnoresCpuOnlyHelpers()
    {
        const string invalidSource = @"
            using Delta.Maths;
            using Delta.Shader;
            public class CpuOnlyHelper { public string Name; }
            public struct Constants { public CpuOnlyHelper Helper; }
            [Interstage]
            public struct FragmentPayload {  public Position Position; }
            public struct FragmentContext
            {
                [Interstage] public FragmentPayload Fragment;
                [PushConstant] public Constants Constants;
            }
            public static class InvalidFragment
            {
                [FragmentShader] public static float4 Fragment(in FragmentContext context)
                    => new float4(1f, 0f, 0f, 1f);
            }";

        Compilation invalidCompilation = await LoadCompilerTestProjectCompilationAsync(invalidSource).ConfigureAwait(true);
        ShaderCompilationResult invalidResult = Assert.Single(ShaderCompiler.CompileAll(invalidCompilation));
        Assert.False(invalidResult.Success);
        Assert.Contains(invalidResult.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH006);

        const string validSource = @"
            using Delta.Shader;
            public class CpuOnlyHelper { public string Name; }
            public static class ValidCompute
            {
                public readonly struct Context
                {
                    [PushConstant] public readonly uint Count;
                }

                [ComputeShader] public static void Compute(in Context context) { }
            }";

        ShaderCompilationResult validResult = await CompileAndValidateEntryPointAsync(validSource).ConfigureAwait(true);
        Assert.DoesNotContain(validResult.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH010);
    }

    [Fact]
    public async Task ShaderVisibleTypeAnalyzer_ReportsDsh010ForGraphicsPushConstantGraph()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            public class CpuOnlyHelper { public string Name; }
            public struct Constants { public CpuOnlyHelper Helper; }
            [Interstage]
            public struct FragmentPayload {  public Position Position; }
            public struct FragmentContext
            {
                [Interstage] public FragmentPayload Fragment;
                [PushConstant] public Constants Constants;
            }
            public static class InvalidGraphics
            {
                [FragmentShader] public static float4 Fragment(in FragmentContext context)
                    => new float4(1f, 0f, 0f, 1f);
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ImmutableArray<Diagnostic> analyzerResult = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ComputeEntryPointAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        Assert.Contains(analyzerResult, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH010);
    }

    [Fact]
    public async Task GraphicsEntryPointAnalyzer_AllowsFragmentOnlyShader()
    {
        const string source = """
            using Delta.Maths;
            using Delta.Shader;

            public struct FragmentPayload
            {
                public Position Position;
            }

            public struct FragmentContext
            {
                [Interstage]
                public FragmentPayload Fragment;
            }

            public static class FragmentOnlyShader
            {
                [FragmentShader]
                public static float4 Fragment(in FragmentContext context) =>
                    new float4(1f, 0f, 0f, 1f);
            }
            """;

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ComputeEntryPointAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH017);
    }

    [Fact]
    public async Task GraphicsEntryPoints_BuildVertexAndFragmentModulesWithStageAbi()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public struct Constants { public float2 Resolution; public float Time; }
                [Interstage]
                public struct GraphicsPayload
                {
                     public Position Position;
                    public Uv0 Uv;
                }
                public struct VertexContext { [Interstage] public GraphicsPayload Vertex; }
                public struct FragmentContext
                {
                    [Interstage] public GraphicsPayload Fragment;
                    [PushConstant] public Constants Constants;
                }
                public static class Graphics
                {
                    [VertexShader(""FullscreenVertex"")] public static GraphicsPayload Vertex(in VertexContext context)
                        => new GraphicsPayload { Position = new float4(-1f, -1f, 0f, 1f), Uv = new float2(ShaderBuiltins.VertexIndex, 0f) };
                    [FragmentShader(""FullscreenFragment"")] public static float4 Fragment(in FragmentContext context)
                        => new float4(intrinsics.fwidth(ShaderBuiltins.FragmentCoord.X), context.Constants.Time, float2.Normalize(context.Fragment.Uv).x, 1f);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message))));
        ShaderCompilationResult vertex = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Vertex);
        Assert.Equal("FullscreenVertex", vertex.Module!.SourceEntryPointName);
        ShaderCompilationResult fragment = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Fragment);
        Assert.Equal("FullscreenFragment", fragment.Module!.SourceEntryPointName);
        Assert.Equal("main", fragment.BuildManifest!.EntryPointName);
        Assert.Single(fragment.BuildManifest.PushConstants);
    }

    [Fact]
    public async Task GraphicsEntryPoints_TransformConformancePreservesColumnMajorCpuGpuContract()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            public struct TransformConstants
            {
                public float4x4 Model;
                public float4x4 View;
                public float4x4 Projection;
            }
            [Interstage]
            public struct VertexPayload {  public Position Position; }
            public struct VertexContext
            {
                [Interstage] public VertexPayload Vertex;
                [PushConstant] public TransformConstants Constants;
            }
            public static class TransformConformance
            {
                [VertexShader(""CubeVertex"")]
                public static VertexPayload Vertex(in VertexContext context) => new VertexPayload
                {
                    Position = context.Constants.Projection * context.Constants.View * context.Constants.Model * new float4(1f, 2f, 3f, 1f)
                };
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        ShaderIrModule module = result.Module!;
        ShaderCompilationPushConstant push = Assert.Single(result.BuildManifest!.PushConstants);
        Assert.Equal("mat4", Assert.Single(push.Members, member => member.Name == "Model").GlslType);
        Assert.Equal(0u, Assert.Single(push.Members, member => member.Name == "Model").Offset);
        Assert.Equal(64u, Assert.Single(push.Members, member => member.Name == "View").Offset);
        Assert.Equal(128u, Assert.Single(push.Members, member => member.Name == "Projection").Offset);
        Assert.All(push.Members, member =>
        {
            Assert.Equal(16u, member.Alignment);
            Assert.Equal(64u, member.Size);
            Assert.Equal(64u, member.ArrayStride);
            Assert.Equal(16u, member.MatrixStride);
        });
        Assert.Equal(16u, push.Alignment);
        Assert.Equal(192u, push.Size);
        Assert.Equal(192u, push.ArrayStride);

        var glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(module).Source;
        Assert.Contains("layout(push_constant, std430) uniform DeltaPushConstants", glsl, StringComparison.Ordinal);
        Assert.Contains("layout(offset = 0) mat4 member_Model", glsl, StringComparison.Ordinal);
        Assert.Contains("layout(offset = 64) mat4 member_View", glsl, StringComparison.Ordinal);
        Assert.Contains("layout(offset = 128) mat4 member_Projection", glsl, StringComparison.Ordinal);
        Assert.Contains("gl_Position", glsl, StringComparison.Ordinal);
        var projectionIndex = glsl.IndexOf("pushConstants.member_Projection", StringComparison.Ordinal);
        var viewIndex = glsl.IndexOf("pushConstants.member_View", StringComparison.Ordinal);
        var modelIndex = glsl.IndexOf("pushConstants.member_Model", StringComparison.Ordinal);
        Assert.True(projectionIndex >= 0 && projectionIndex < viewIndex && viewIndex < modelIndex);
        Assert.DoesNotContain("transpose", glsl, StringComparison.OrdinalIgnoreCase);

        var model = float4x4.CreateTRS(new float3(4f, -1f, 2f), quaternion.CreateFromAxisAngle(new float3(0f, 1f, 0f), 0.35f), new float3(2f, 3f, 4f));
        var view = float4x4.CreateLookTo(new float3(0f, 1f, -8f), new float3(0f, 0f, 1f), new float3(0f, 1f, 0f));
        var projection = float4x4.CreatePerspectiveFieldOfViewLeftHanded(global::Delta.Maths.DeltaMaths.Radians(60f), 16f / 9f, 0.1f, 100f);
        var vertex = new float4(1f, 2f, 3f, 1f);
        float4 cpu = projection * view * model * vertex;
        Assert.Equal(1f, vertex.w);
        Assert.True(cpu.w > 0f);
        Assert.Equal(0f, projection.M44);
        Assert.Equal(1f, view.M44);
        Assert.Equal(1f, model.M44);
    }

    [Fact]
    public async Task GraphicsEntryPoints_ViewportCube_EmitsVertexInputs_ReadonlyTransformsAndStableMatrixOrder()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            public struct SceneParameters
            {
                public float4x4 Model;
                public float4x4 View;
                public float4x4 Projection;
                public float3 LightDirection;
                public float _Padding0;
                public float4 LightColor;
            }

            [Interstage]
            public struct CubePayload
            {
                 [Layout(0)] public Position Position;
                [Layout(1)] public WorldNormal Normal;
                [Layout(2)] public Uv0 Uv;
            }

            public struct VertexContext
            {
                [Interstage] public CubePayload Vertex;
                [Layout(0, 0)] public ReadOnlyStorageBuffer<SceneParameters> Scene;
            }

            public static class EditorViewportCube
            {
                [VertexShader(""EditorViewportCubeVertex"")]
                public static CubePayload Vertex(in VertexContext context) => new CubePayload
                {
                    Position = context.Scene[0].Projection * context.Scene[0].View * context.Scene[0].Model * context.Vertex.Position,
                    Normal = maths.normalize((context.Scene[0].Model * new float4(context.Vertex.Normal, 0f)).xyz),
                    Uv = context.Vertex.Uv
                };
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        ShaderIrModule module = result.Module!;
        Assert.Equal(3, module.VertexInputs.Count);
        Assert.Equal((0u, "vec4", "VK_FORMAT_R32G32B32A32_SFLOAT"), (module.VertexInputs[0].Location, module.VertexInputs[0].GlslType, module.VertexInputs[0].FormatHint));
        Assert.Equal((1u, "vec3", "VK_FORMAT_R32G32B32_SFLOAT"), (module.VertexInputs[1].Location, module.VertexInputs[1].GlslType, module.VertexInputs[1].FormatHint));
        Assert.Equal((2u, "vec2", "VK_FORMAT_R32G32_SFLOAT"), (module.VertexInputs[2].Location, module.VertexInputs[2].GlslType, module.VertexInputs[2].FormatHint));
        Assert.Single(module.VertexBuffers);
        Assert.Equal(0u, module.VertexBuffers[0].Binding);
        Assert.Equal(36u, module.VertexBuffers[0].Stride);
        Assert.Equal(3, module.VertexBuffers[0].Attributes.Count);
        Assert.Equal(0u, module.VertexBuffers[0].Attributes[0].ByteOffset);
        Assert.Equal(16u, module.VertexBuffers[0].Attributes[1].ByteOffset);
        Assert.Equal(28u, module.VertexBuffers[0].Attributes[2].ByteOffset);

        ShaderIrResource resource = Assert.Single(module.Resources);
        Assert.Equal(ShaderResourceKind.StorageBuffer, resource.Category);
        Assert.Equal(ShaderResourceAccess.ReadOnly, resource.Access);
        Assert.Equal(0u, resource.Set);
        Assert.Equal(0u, resource.Binding);
        Assert.Equal(224u, resource.Std430Layout!.Size);
        Assert.Equal(16u, resource.Std430Layout.Alignment);

        var glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(module).Source;
        Assert.Contains("#version 460", glsl, StringComparison.Ordinal);
        Assert.Contains("layout(location = 0) in vec4 vertex_Position;", glsl, StringComparison.Ordinal);
        Assert.Contains("layout(location = 1) in vec3 vertex_Normal;", glsl, StringComparison.Ordinal);
        Assert.Contains("layout(location = 2) in vec2 vertex_Uv;", glsl, StringComparison.Ordinal);
        Assert.Contains("member_Projection", glsl, StringComparison.Ordinal);
        Assert.Contains("member_View", glsl, StringComparison.Ordinal);
        Assert.Contains("member_Model", glsl, StringComparison.Ordinal);
        Assert.DoesNotContain("transpose", glsl, StringComparison.OrdinalIgnoreCase);

        var model = float4x4.CreateTRS(new float3(1f, 2f, 3f), quaternion.CreateFromAxisAngle(new float3(0f, 1f, 0f), 0.5f), new float3(2f, 2f, 2f));
        var view = float4x4.CreateLookTo(new float3(0f, 0f, -5f), new float3(0f, 0f, 1f), new float3(0f, 1f, 0f));
        var projection = float4x4.CreatePerspectiveFieldOfViewLeftHanded(global::Delta.Maths.DeltaMaths.Radians(60f), 1f, 0.1f, 100f);
        var vertex = new float4(1f, 0f, 0f, 1f);
        float4 cpuOrder = projection * view * model * vertex;
        Assert.Equal(cpuOrder, projection * view * model * vertex);
    }

    [Fact]
    public async Task GraphicsEntryPoints_RejectsBadVertexInputLocationsStagesAndManagedTypes()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            public sealed class ManagedData
            {
                public float Value;
            }

            [Interstage]
            public struct FragmentPayload {  public Position Position; }
            [Interstage]
            public struct VertexPayload
            {
                 [Layout(0)] public Position First;
                [Layout(0)] public float2 Duplicate;
                [Layout(1)] public ManagedData Managed;
            }
            public struct FragmentContext
            {
                [Interstage] public FragmentPayload Fragment;
                [Layout(0, 0)] public SampledTexture2D Texture;
            }
            public struct VertexContext
            {
                [Interstage] public VertexPayload Vertex;
            }
            public static class InvalidViewport
            {
                [FragmentShader(""Fragment"")]
                public static float4 Fragment(in FragmentContext context)
                    => new float4(context.Fragment.Position.xyz, 1f);

                [VertexShader(""Vertex"")]
                public static VertexPayload Vertex(in VertexContext context) => context.Vertex;
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        Assert.Equal(2, results.Count);

        ShaderCompilationResult vertex = Assert.Single(results, result => result.Module?.Stage == ShaderStage.Vertex);
        Assert.False(vertex.Success);
        Assert.Contains(vertex.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH013);

        ShaderCompilationResult fragment = Assert.Single(results, result => result.Module?.Stage == ShaderStage.Fragment);
        Assert.True(fragment.Success, string.Join(Environment.NewLine, fragment.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Fact]
    public async Task GraphicsEntryPoints_RejectFragmentBuiltinInVertexStage()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            [Interstage]
            public struct VertexPayload {  public Position Position; }
            public struct VertexContext { [Interstage] public VertexPayload Vertex; }
            public static class InvalidGraphics
            {
                [VertexShader] public static VertexPayload Vertex(in VertexContext context)
                    => new VertexPayload { Position = new float4(ShaderBuiltins.FragmentCoord.X, 0f, 0f, 1f) };
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH008);
    }

    [Fact]
    public async Task GraphicsEntryPoints_LowerDefaultLiteralToTypedGlslZero()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            [Interstage]
            public struct VertexPayload {  public Position Position; }
            public struct VertexContext { [Interstage] public VertexPayload Vertex; }
            public static class FullscreenUi
            {
                [VertexShader] public static VertexPayload Vertex(in VertexContext context)
                    => new VertexPayload { Position = default };
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("gl_Position", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("vec4(0.0)", result.Module.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("default", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphicsEntryPoints_PreserveEarlyReturnsForFullscreenVertexBranches()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            [Interstage]
            public struct VertexPayload {  public Position Position; }
            public struct VertexContext { [Interstage] public VertexPayload Vertex; }
            public static class FullscreenVertex
            {
                [VertexShader] public static VertexPayload Vertex(in VertexContext context)
                {
                    if (ShaderBuiltins.VertexIndex == 0u)
                    {
                        return new VertexPayload { Position = new float4(-1f, -1f, 0f, 1f) };
                    }

                    if (ShaderBuiltins.VertexIndex == 1u)
                    {
                        return new VertexPayload { Position = new float4(3f, -1f, 0f, 1f) };
                    }

                    return new VertexPayload { Position = new float4(-1f, 3f, 0f, 1f) };
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var body = result.Module!.Body ?? throw new InvalidOperationException("Vertex compilation did not produce a shader body.");
        Assert.Equal(3, body.Split("gl_Position =", StringSplitOptions.None).Length - 1);
        Assert.Equal(3, body.Split("return;", StringSplitOptions.None).Length - 1);
        Assert.Contains("vec4(-1, -1, 0, 1)", body, StringComparison.Ordinal);
        Assert.Contains("vec4(3, -1, 0, 1)", body, StringComparison.Ordinal);
        Assert.Contains("vec4(-1, 3, 0, 1)", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SampledTexture_CompilesForVertexAndFragment_WithOpaqueAbi()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            public static class TextureStages
            {
                public struct TextParameters
                {
                    public float4 TextColor;
                    public float4 OutlineColor;
                    public float OutlineWidth;
                    public float DistanceRange;
                }
                [Interstage]
                public struct TexturePayload
                {
                     public Position Position;
                    public Uv0 Uv;
                }
                public struct VertexContext
                {
                    [Interstage] public TexturePayload Vertex;
                    [Layout(0, 1)] public SampledTexture2D Atlas;
                }
                public struct FragmentContext
                {
                    [Interstage] public TexturePayload Fragment;
                    [Layout(0, 2)] public SampledTexture2D Atlas;
                    [PushConstant] public TextParameters Parameters;
                }

                [VertexShader(""sdf-text"")]
                public static TexturePayload Vertex(in VertexContext context)
                {
                    var sampled = context.Atlas.Sample<float2, float4>(new float2(0.5f, 0.5f));
                    return new TexturePayload { Position = sampled, Uv = new float2(0.5f, 0.5f) };
                }

                [FragmentShader(""sdf-text"")]
                public static float4 Fragment(in FragmentContext context)
                {
                    var texel = context.Atlas.Sample<float2, float4>(context.Fragment.Uv);
                    var median = maths.max(maths.min(texel.x, texel.y), maths.min(maths.max(texel.x, texel.y), texel.z));
                    var signedDistance = (median - 0.5f) * context.Parameters.DistanceRange;
                    var edge = intrinsics.fwidth(signedDistance);
                    var fillCoverage = maths.smoothstep(-edge, edge, signedDistance);
                    var outlineWidth = maths.max(context.Parameters.OutlineWidth, 0f);
                    var outerCoverage = maths.smoothstep(-outlineWidth - edge, -outlineWidth + edge, signedDistance);
                    var outlineContribution = maths.max(outerCoverage - fillCoverage, 0f);
                    return context.Parameters.TextColor * fillCoverage + context.Parameters.OutlineColor * outlineContribution;
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message))));

        ShaderCompilationResult vertex = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Vertex);
        ShaderCompilationResource vertexResource = Assert.Single(vertex.BuildManifest!.Resources);
        Assert.Equal("sdf-text", vertex.EntryPointName);
        Assert.Equal("sampled-texture", vertexResource.Category);
        Assert.Equal(ShaderStage.Vertex, vertexResource.Stage);
        Assert.Equal("sampler2D", vertexResource.GlslType);
        Assert.Equal("opaque", vertexResource.Layout);
        Assert.Equal("none", vertexResource.Packing.Scheme);
        Assert.Equal(0u, vertexResource.Packing.Stride);
        var vertexGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(vertex.Module!).Source;
        Assert.Contains("layout(set = 0, binding = 1) uniform sampler2D", vertexGlsl, StringComparison.Ordinal);
        Assert.Contains("texture(", vertexGlsl, StringComparison.Ordinal);
        Assert.Contains("layout(location = 0) out vec2 Uv;", vertexGlsl, StringComparison.Ordinal);
        Assert.DoesNotContain("std430) readonly buffer", vertexGlsl, StringComparison.Ordinal);

        ShaderCompilationResult fragment = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Fragment);
        ShaderCompilationResource fragmentResource = Assert.Single(fragment.BuildManifest!.Resources);
        Assert.Equal("sdf-text", fragment.EntryPointName);
        Assert.Equal(ShaderStage.Fragment, fragmentResource.Stage);
        Assert.Equal(2u, fragmentResource.Binding);
        Assert.Equal(0u, fragmentResource.Offset);
        Assert.Equal(0u, fragmentResource.ArrayStride);
        Assert.Equal("main", fragment.BuildManifest.EntryPointName);
        var fragmentGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(fragment.Module!).Source;
        Assert.Contains("layout(set = 0, binding = 2) uniform sampler2D", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("fwidth", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("smoothstep", fragmentGlsl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SampledTexture_RejectsVertexBindingFormInFragment()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            [Interstage]
            public struct FragmentPayload {  public Position Position; }
            public struct FragmentContext
            {
                [Interstage] public FragmentPayload Fragment;
                [Layout(0)] public SampledTexture2D Atlas;
            }
            public static class InvalidTextureStage
            {
                [FragmentShader]
                public static float4 Fragment(in FragmentContext context)
                    => new float4(1f, 1f, 1f, 1f);
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH002);
    }

    [Fact]
    public async Task Intrinsics_DerivativesLowerForFragmentAndRejectOtherStages()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            [Interstage]
            public struct FragmentPayload {  public Position Position; }
            [Interstage]
            public struct VertexPayload {  public Position Position; }
            public struct FragmentContext { [Interstage] public FragmentPayload Fragment; }
            public struct VertexContext { [Interstage] public VertexPayload Vertex; }
            public static class DerivativeStages
            {
                [FragmentShader]
                public static float4 Fragment(in FragmentContext context)
                {
                    var coord = new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y);
                    var dx = intrinsics.ddx(coord.x);
                    var dy = intrinsics.ddy(coord.y);
                    return new float4(dx, dy, 0f, 1f);
                }

                [VertexShader]
                public static VertexPayload Vertex(in VertexContext context)
                    => new VertexPayload { Position = new float4(intrinsics.ddx(1f), 0f, 0f, 1f) };
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);

        ShaderCompilationResult fragment = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Fragment);
        Assert.True(fragment.Success, string.Join(Environment.NewLine, fragment.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var fragmentGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(fragment.Module!).Source;
        Assert.Contains("dFdx", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("dFdy", fragmentGlsl, StringComparison.Ordinal);

        ShaderCompilationResult vertex = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Vertex);
        Assert.False(vertex.Success);
        Assert.Contains(vertex.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH008);
        Assert.Contains(vertex.Diagnostics, diagnostic => diagnostic.Message.Contains("ddx", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GraphicsText_GlyphInstances_AreReflected_AsStd430Ssbo_WithInstanceIndex()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            public static class TextScene
            {
                public struct GlyphInstance
                {
                    public float2 PixelMin;
                    public float2 PixelMax;
                    public float4 UvRect;
                    public Color Color;
                }

                public struct TextParameters
                {
                    public float2 Resolution;
                    public float4 TextColor;
                    public float4 OutlineColor;
                    public float OutlineWidth;
                    public float DistanceRange;
                }

                [Interstage]
                public struct TextPayload
                {
                     public Position Position;
                    public Uv0 Uv;
                    public VertexColor GlyphColor;
                }

                public struct VertexContext
                {
                    [Interstage] public TextPayload Vertex;
                    [Layout(0, 0)] public ReadOnlyStorageBuffer<GlyphInstance> Glyphs;
                    [PushConstant] public TextParameters Parameters;
                }

                public struct FragmentContext
                {
                    [Interstage] public TextPayload Fragment;
                    [Layout(0, 3)] public SampledTexture2D Atlas;
                    [PushConstant] public TextParameters Parameters;
                }

                [VertexShader(""sdf-text"")]
                public static TextPayload Vertex(in VertexContext context) => new TextPayload
                {
                    Position = new float4(0f, 0f, 0f, 1f),
                    Uv = context.Glyphs[ShaderBuiltins.InstanceIndex].UvRect.xy,
                    GlyphColor = context.Glyphs[ShaderBuiltins.InstanceIndex].Color
                };

                [FragmentShader(""sdf-text"")]
                public static float4 Fragment(in FragmentContext context)
                {
                    var texel = context.Atlas.Sample<float2, float4>(context.Fragment.Uv);
                    var signedDistance = (texel.x - 0.5f) * context.Parameters.DistanceRange;
                    var edge = intrinsics.fwidth(signedDistance);
                    var fillCoverage = maths.smoothstep(-edge, edge, signedDistance);
                    var outlineWidth = maths.max(context.Parameters.OutlineWidth, 0f);
                    var outerCoverage = maths.smoothstep(-outlineWidth - edge, -outlineWidth + edge, signedDistance);
                    var outlineContribution = maths.max(outerCoverage - fillCoverage, 0f);
                    return context.Parameters.TextColor * context.Fragment.GlyphColor * fillCoverage + context.Parameters.OutlineColor * context.Fragment.GlyphColor * outlineContribution;
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message))));

        ShaderCompilationResult vertex = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Vertex);
        ShaderCompilationResource glyphResource = Assert.Single(vertex.BuildManifest!.Resources);
        Assert.Equal("storage-buffer", glyphResource.Category);
        Assert.Equal(ShaderStage.Vertex, glyphResource.Stage);
        Assert.Equal(ShaderResourceAccess.ReadOnly, glyphResource.Access);
        Assert.Equal("std430", glyphResource.Layout);
        Assert.Equal(0u, glyphResource.Set);
        Assert.Equal(0u, glyphResource.Binding);
        Assert.Equal(48u, glyphResource.ArrayStride);
        Assert.Equal(0u, glyphResource.Members[0].Offset);
        Assert.Equal(8u, glyphResource.Members[1].Offset);
        Assert.Equal(16u, glyphResource.Members[2].Offset);
        Assert.Equal(32u, glyphResource.Members[3].Offset);
        Assert.Equal(48u, glyphResource.Size);
        Assert.Equal("InstanceIndex", Assert.Single(vertex.BuildManifest.Inputs, input => input.Builtin == "InstanceIndex").Builtin);
        var vertexGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(vertex.Module!).Source;
        Assert.Contains("gl_InstanceIndex", vertexGlsl, StringComparison.Ordinal);
        Assert.Contains("buffer", vertexGlsl, StringComparison.Ordinal);
        Assert.Contains(".data[", vertexGlsl, StringComparison.Ordinal);

        ShaderCompilationResult fragment = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Fragment);
        Assert.Equal("sampled-texture", Assert.Single(fragment.BuildManifest!.Resources).Category);
        ShaderCompilationPushConstant textParameters = Assert.Single(fragment.BuildManifest.PushConstants);
        Assert.Equal(64u, textParameters.Size);
        Assert.Equal(52u, Assert.Single(textParameters.Members, member => member.Name == "DistanceRange").Offset);
        var fragmentGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(fragment.Module!).Source;
        Assert.Contains("layout(set = 0, binding = 3) uniform sampler2D", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("fwidth", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("smoothstep", fragmentGlsl, StringComparison.Ordinal);
        Assert.DoesNotContain("1 - smoothstep", fragmentGlsl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphicsStructFieldLowering_PreservesLocalWithMatchingFieldName()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            public static class StructFieldSymbols
            {
                public struct Payload
                {
                    public Color Color;
                }

                [Interstage]
                public struct VertexPayload
                {
                     public Position Position;
                }

                public struct VertexContext
                {
                    [Interstage] public VertexPayload Vertex;
                    [Layout(0, 0)] public ReadOnlyStorageBuffer<Payload> Payloads;
                }

                [VertexShader]
                public static VertexPayload Vertex(in VertexContext context)
                {
                    uint index = ShaderBuiltins.VertexIndex;
                    var payload = context.Payloads[index];
                    var copiedColor = payload.Color;
                    var Color = new float4(0.25f, 0.5f, 0.75f, 1f);
                    return new VertexPayload { Position = copiedColor + Color };
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("member_Color", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("+ Color", result.Module.Body, StringComparison.Ordinal);
        Assert.Contains("copiedColor = payload.member_Color", result.Module.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("member_member_Color", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstanceIndex_RejectsFragmentStage()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            [Interstage]
            public struct FragmentPayload {  public Position Position; }
            public struct FragmentContext { [Interstage] public FragmentPayload Fragment; }
            public static class InvalidInstanceIndex
            {
                [FragmentShader]
                public static float4 Fragment(in FragmentContext context)
                    => new float4(ShaderBuiltins.InstanceIndex, ShaderBuiltins.InstanceIndex, ShaderBuiltins.InstanceIndex, 1f);
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH008);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("Fragment stage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GraphicsEntryPoint_LowersStaticHelperCallGraphInDependencyOrder()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            [Interstage]
            public struct FragmentPayload {  public Position Position; }
            public struct FragmentContext { [Interstage] public FragmentPayload Fragment; }
            public static class HelperShader
            {
                [FragmentShader]
                public static float4 Fragment(in FragmentContext context)
                {
                    var value = Wave(0.5f);
                    return new float4(value, value, value, 1f);
                }

                private static float Wave(float value)
                {
                    return maths.sin(value);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var module = result.Module ?? throw new InvalidOperationException("Successful helper compilation did not produce an IR module.");
        Assert.Single(module.HelperFunctions);
        var glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(module).Source;
        var helperIndex = glsl.IndexOf("delta_helper_", StringComparison.Ordinal);
        Assert.True(helperIndex >= 0);
        Assert.True(helperIndex < glsl.IndexOf("void main()", StringComparison.Ordinal));
        Assert.Contains("return sin(arg_value)", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphicsEntryPoint_LowersExpressionBodiedStaticHelper()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            [Interstage]
            public struct FragmentPayload {  public Position Position; }
            public struct FragmentContext { [Interstage] public FragmentPayload Fragment; }
            public static class ExpressionHelperShader
            {
                private static float Wave(float value) => maths.sin(value);

                [FragmentShader]
                public static float4 Fragment(in FragmentContext context)
                {
                    var value = Wave(0.5f);
                    return new float4(value);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var module = result.Module ?? throw new InvalidOperationException("Successful expression-bodied helper compilation did not produce an IR module.");
        Assert.Single(module.HelperFunctions);
        var glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(module).Source;
        Assert.Contains("return sin(arg_value)", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphicsEntryPoint_RejectsRecursiveStaticHelperCallGraph()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            [Interstage]
            public struct FragmentPayload {  public Position Position; }
            public struct FragmentContext { [Interstage] public FragmentPayload Fragment; }
            public static class RecursiveHelperShader
            {
                [FragmentShader]
                public static float4 Fragment(in FragmentContext context)
                {
                    var value = First(0.5f);
                    return new float4(value, value, value, 1f);
                }

                private static float First(float value)
                {
                    return Second(value);
                }

                private static float Second(float value)
                {
                    return First(value);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("Recursive shader helper call graph", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GraphicsEntryPoint_RejectsManagedStaticHelperCapture()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            [Interstage]
            public struct FragmentPayload {  public Position Position; }
            public struct FragmentContext { [Interstage] public FragmentPayload Fragment; }
            public static class CapturedHelperShader
            {
                private static readonly float Scale = 2f;

                [FragmentShader]
                public static float4 Fragment(in FragmentContext context)
                {
                    var value = ScaleValue(0.5f);
                    return new float4(value, value, value, 1f);
                }

                private static float ScaleValue(float value)
                {
                    return value * Scale;
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("captures managed field 'Scale'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ComputeContext_LowersUserDefinedResourcesBuiltinsAndPushConstants()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            public readonly struct UserDefinedComputeContext
            {
                [Layout(0, 0)]
                public readonly ReadOnlyStorageBuffer<uint> Input;

                [Layout(0, 1)]
                public readonly ReadWriteStorageBuffer<uint> Output;

                [PushConstant]
                public readonly uint Count;

            }

            public static class ContextCompute
            {
                [ComputeShader(localSizeX: 64)]
                public static void Compute(in UserDefinedComputeContext ctx)
                {
                    if (ShaderBuiltins.GlobalInvocationId.X < ctx.Count)
                        ctx.Output[ShaderBuiltins.GlobalInvocationId.X] = ctx.Input[ShaderBuiltins.GlobalInvocationId.X] * 2u + 1u;
                }
            }";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        ShaderCompilationManifest buildManifest = Assert.IsType<ShaderCompilationManifest>(result.BuildManifest);
        ShaderIrModule module = Assert.IsType<ShaderIrModule>(result.Module);
        Assert.Equal("ctx.Input", Assert.Single(buildManifest.Resources, resource => resource.ParameterName == "ctx.Input").ParameterName);
        Assert.Equal("ctx.Output", Assert.Single(buildManifest.Resources, resource => resource.ParameterName == "ctx.Output").ParameterName);
        ShaderCompilationPushConstant push = Assert.Single(buildManifest.PushConstants);
        Assert.Equal("Count", Assert.Single(push.Members).Name);

        var glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(module).Source;
        Assert.Contains("gl_GlobalInvocationID.x", glsl, StringComparison.Ordinal);
        Assert.Contains("pushConstants.member_Count", glsl, StringComparison.Ordinal);
        Assert.Contains("std430", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeContext_CanContainOnlyUserDefinedPushConstants()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct ParametersContext
            {
                [PushConstant]
                public readonly uint Count;
            }

            public static class ParametersCompute
            {
                [ComputeShader]
                public static void Compute(in ParametersContext ctx)
                {
                }
            }";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        ShaderCompilationManifest buildManifest = Assert.IsType<ShaderCompilationManifest>(result.BuildManifest);
        Assert.Empty(buildManifest.Resources);
        Assert.Single(buildManifest.PushConstants);
    }

    [Fact]
    public async Task ComputeContext_RejectsReferenceFieldsAndUnannotatedState()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct InvalidContext
            {
                [PushConstant]
                public readonly string Label;

                public readonly uint HiddenState;
            }

            public static class InvalidContextCompute
            {
                [ComputeShader]
                public static void Compute(in InvalidContext ctx)
                {
                }
            }";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == ShaderDiagnosticId.DSH010 &&
            diagnostic.Message.Contains("reference", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == ShaderDiagnosticId.DSH010 &&
            diagnostic.Message.Contains("role", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ComputeContext_ParameterFormRequiresContext()
    {
        const string source = @"
            using Delta.Shader;

            public readonly struct ValidContext
            {
                [PushConstant]
                public readonly uint Count;
            }

            public static class MigrationDiagnostics
            {
                [ComputeShader]
                public static void Context(in ValidContext ctx) { }

                [ComputeShader]
                public static void Legacy() { }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ComputeEntryPointAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DSH002" && diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("exactly one", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DSH002" && diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Context", StringComparison.Ordinal));
    }

    private static async Task<ShaderCompilationResult> CompileAndValidateEntryPointAsync(
        string source,
        ShaderCompilationOptions? options = null)
    {
        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        var context = new ModuleCompilationContext(compilation);
        var frontend = new RoslynFrontend(compilation);
        return ComputeEntryPoints.ValidateAndBuild(context, frontend, options);
    }

    [Fact]
    public async Task CompileTimeTypedKernel_LowersIndexedResourcesAndDeltaMathsThroughTheExistingPipeline()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            public static class CompileTimeValid
            {
                public readonly struct ComputeContext
                {
                    [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<float> Input;
                    [Layout(0, 1)] public readonly ReadWriteStorageBuffer<float> Output;
                }

                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint invocation = ShaderBuiltins.GlobalInvocationId.X;
                    context.Output[invocation] = maths.sin(context.Input[invocation]);
                }
            }";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("Output.data[local_invocation]", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("sin(Input.data[local_invocation])", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonGenericUIntBuffers_LowerThroughValidationIrAndGlsl()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            public static class SimpleCompute
            {
                public readonly struct ComputeContext
                {
                    [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<uint> Input;
                    [Layout(0, 1)] public readonly ReadWriteStorageBuffer<uint> Output;
                }

                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    if (id < context.Input.Length) context.Output[id] = context.Input[id] * 2u + 1u;
                }
            }";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        IReadOnlyList<ShaderIrResource> resources = result.Module!.Resources;
        Assert.Equal("uint", Assert.Single(resources, resource => resource.ParameterName == "context.Input").GlslType);
        Assert.Equal("uint", Assert.Single(resources, resource => resource.ParameterName == "context.Output").GlslType);
        Assert.Contains("Output.data[local_id] = Input.data[local_id]* 2u + 1u", result.Module.Body, StringComparison.Ordinal);

        var glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(result.Module).Source;
        Assert.Contains("#version 460", glsl, StringComparison.Ordinal);
        Assert.Contains("layout(set = 0, binding = 0, std430) readonly buffer", glsl, StringComparison.Ordinal);
        Assert.Contains("uint data[];", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeSampledTexture_LowersWithComputeStageAbi()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            public static class ComputeTexture
            {
                public readonly struct ComputeContext
                {
                    [Layout(0, 2)] public readonly SampledTexture2D Atlas;
                    [Layout(0, 1)] public readonly ReadWriteStorageBuffer<float4> Output;
                }

                [ComputeShader(localSizeX: 8)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    context.Output[id] = context.Atlas.Sample<float2, float4>(new float2(0.5f, 0.5f));
                }
            }";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        ShaderIrModule module = Assert.IsType<Delta.Shader.Compiler.IR.ShaderIrModule>(result.Module);
        ShaderIrResource texture = Assert.Single(module.Resources, resource => resource.Category == ShaderResourceKind.SampledTexture2D);
        Assert.Equal(ShaderStage.Compute, texture.Stage);
        Assert.Equal(0u, texture.Set);
        Assert.Equal(2u, texture.Binding);
        Assert.Equal(ShaderResourceAccess.ReadOnly, texture.Access);
        Assert.Contains("texture(Atlas, vec2(0.5, 0.5))", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeSampledTexture_RejectsParameterStyleEntryPoint()
    {
        const string source = @"
            using Delta.Shader;

            public static class InvalidComputeTexture
            {
                [ComputeShader]
                public static void Compute([Layout(0, 0)] SampledTexture2D atlas)
                {
                }
            }";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH002);
    }

    [Fact]
    public async Task ComputeStorageBufferIndexer_LowersTypedPayload()
    {
        const string source = @"
            using Delta.Shader;

            public static class IndexedPayloadCompute
            {
                public readonly struct ComputeContext
                {
                    [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<uint> Input;
                    [Layout(0, 1)] public readonly ReadWriteStorageBuffer<uint> Output;
                }

                [ComputeShader(localSizeX: 8)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    if (id < context.Input.Length) context.Output[id] = context.Input[id] * 2u + 1u;
                }
            }";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        ShaderIrModule module = Assert.IsType<Delta.Shader.Compiler.IR.ShaderIrModule>(result.Module);
        Assert.Contains("Output.data[local_id] = Input.data[local_id]* 2u + 1u", module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeStorageBuffer_RejectsManagedReferencePayload()
    {
        const string source = @"
            using Delta.Shader;

            public struct ManagedPayload
            {
                public string Label;
                public uint Value;
            }

            public static class ManagedPayloadCompute
            {
                public struct Context
                {
                    [Layout(0, 0)] public ReadOnlyStorageBuffer<ManagedPayload> Input;
                }

                [ComputeShader]
                public static void Compute(in Context context)
                {
                }
            }";

        ShaderCompilationResult result = await CompileAndValidateEntryPointAsync(source).ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == ShaderDiagnosticId.DSH010 &&
            diagnostic.Message.Contains("reference", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeltaComputeGenerator_EmitsGlslManifestAndArtifactWrapper()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            public static class GeneratedKernel
            {
                public readonly struct ComputeContext
                {
                    [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<float> Input;
                    [Layout(0, 1)] public readonly ReadWriteStorageBuffer<float> Output;
                }

                [ComputeShader(localSizeX: 64)]
                internal static void Compute(in ComputeContext context)
                {
                    uint invocation = ShaderBuiltins.GlobalInvocationId.X;
                    context.Output[invocation] = maths.sin(context.Input[invocation]);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        var parseOptions = compilation.SyntaxTrees.First().Options as CSharpParseOptions;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new DeltaComputeGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        GeneratedSourceResult generated = Assert.Single(driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources));
        Assert.Contains("CreateAbi", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("public static Delta.Shader.Contract.ShaderAbi Abi", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("public static partial class Shaders", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("public static partial class Abi", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("public static partial class GeneratedKernel", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("public static ShaderAbi Compute()", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("CreateArtifact", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("PackComputeInputElement", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("PackComputeInputElements", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("PackComputeOutputElement", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("UnpackComputeInputElement", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("UnpackComputeInputElements", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("UnpackComputeOutputElement", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("Delta.Shader.Contract", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Deserialize", generated.SourceText.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeltaComputeGenerator_EmitsUnpackForRequiredInitProperties()
    {
        const string source = @"
            using Delta.Shader;

            public struct Parameters
            {
                public required uint Count { get; init; }
            }

            public static class InitOnlyKernel
            {
                public readonly struct ComputeContext
                {
                    [PushConstant] public readonly Parameters Parameters;
                }

                [ComputeShader]
                public static void Compute(in ComputeContext context)
                {
                    uint value = context.Parameters.Count;
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        var parseOptions = compilation.SyntaxTrees.First().Options as CSharpParseOptions;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new DeltaComputeGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Compilation generatedCompilation = updatedCompilation;
        Assert.DoesNotContain(generatedCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        GeneratedSourceResult generated = Assert.Single(driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources));
        var generatedText = generated.SourceText.ToString();
        Assert.Contains("PackComputeParameters", generatedText, StringComparison.Ordinal);
        Assert.Contains("UnpackComputePushConstants", generatedText, StringComparison.Ordinal);
        Assert.Contains("new global::Parameters { Count = reader.ReadUInt(0u) }", generatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeltaComputeGenerator_UsesInternalAccessibilityForNonPublicShaderTypes()
    {
        const string source = """
            using Delta.Maths;
            using Delta.Shader;

            internal static class GeneratedKernel
            {
                public readonly struct ComputeContext
                {
                    [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<float> Input;
                    [Layout(0, 1)] public readonly ReadWriteStorageBuffer<float> Output;
                    [PushConstant] public readonly uint Count;
                }

                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint invocation = ShaderBuiltins.GlobalInvocationId.X;
                    context.Output[invocation] = context.Input[invocation];
                }
            }
            """;

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        var parseOptions = compilation.SyntaxTrees.First().Options as CSharpParseOptions;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new DeltaComputeGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(updatedCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        GeneratedSourceResult generated = Assert.Single(driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources));
        var generatedText = generated.SourceText.ToString();
        Assert.Contains("internal static int PackComputeContext", generatedText, StringComparison.Ordinal);
        Assert.Contains("internal static byte[] PackComputeContext", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("public static int PackComputeContext", generatedText, StringComparison.Ordinal);
        Assert.Contains("public static int PackComputeInputElement", generatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeltaGraphicsGenerator_EmitsResolvedVertexBufferPackers()
    {
        const string source = """
            using Delta.Maths;
            using Delta.Shader;

            public static class MeshShaders
            {
                [Interstage]
                public struct MeshPayload
                {

                    [Layout(0)]
                    public Position Position;
                    [Layout(1)]
                    public WorldNormal Normal;
                    [Layout(2)]
                    public Uv0 Uv;
                }

                public struct VertexContext
                {
                    [Interstage]
                    public MeshPayload Vertex;
                }

                public struct FragmentContext
                {
                    [Interstage]
                    public MeshPayload Fragment;
                }

                [VertexShader("MeshVertex")]
                public static MeshPayload Transform(in VertexContext context) => context.Vertex;

                [FragmentShader("MeshFragment")]
                public static float4 Fragment(in FragmentContext context) =>
                    new float4(context.Fragment.Uv, 0.0f, 1.0f);
            }
            """;

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        var parseOptions = compilation.SyntaxTrees.First().Options as CSharpParseOptions;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new DeltaGraphicsGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(updatedCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        GeneratedSourceResult generated = Assert.Single(driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources));
        var generatedText = generated.SourceText.ToString();
        Assert.Contains("PackTransformVertexElement", generatedText, StringComparison.Ordinal);
        Assert.Contains("PackTransformVertexElements", generatedText, StringComparison.Ordinal);
        Assert.Contains("UnpackTransformVertexElement", generatedText, StringComparison.Ordinal);
        Assert.Contains("GetArrayByteLength(values.Length, 36u)", generatedText, StringComparison.Ordinal);
        Assert.Contains("writer.WriteFloat(0u, value.Position.Value.x)", generatedText, StringComparison.Ordinal);
        Assert.Contains("writer.WriteFloat(16u, value.Normal.Value.x)", generatedText, StringComparison.Ordinal);
        Assert.Contains("writer.WriteFloat(28u, value.Uv.Value.x)", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("MeshPayload.x", generatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeltaComputeGenerator_EmitsTypedGraphicsProgramForVertexFragmentPair()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            public static class GeneratedGraphics
            {
                [Interstage]
                public struct Payload
                {
                     public Position Position;
                }
                public struct VertexContext { [Interstage] public Payload Vertex; }
                public struct FragmentContext { [Interstage] public Payload Fragment; }

                [VertexShader(""CubeVertex"")]
                public static Payload Vertex(in VertexContext context) => new Payload
                {
                    Position = new float4((float)ShaderBuiltins.VertexIndex, 0.0f, 0.0f, 1.0f)
                };

                [FragmentShader(""CubeFragment"")]
                public static float4 Fragment(in FragmentContext context) =>
                    new float4(1.0f, 0.0f, 1.0f, 1.0f);
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        var parseOptions = compilation.SyntaxTrees.First().Options as CSharpParseOptions;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new DeltaComputeGenerator().AsSourceGenerator(), new DeltaGraphicsGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        GeneratedSourceResult generated = Assert.Single(driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources));
        var generatedText = generated.SourceText.ToString();
        Assert.Contains("GraphicsShaderProgram", generatedText, StringComparison.Ordinal);
        Assert.Contains("CreateProgram", generatedText, StringComparison.Ordinal);
        Assert.Contains("public static Delta.Shader.Contract.ShaderAbi VertexAbi", generatedText, StringComparison.Ordinal);
        Assert.Contains("public static Delta.Shader.Contract.ShaderAbi FragmentAbi", generatedText, StringComparison.Ordinal);
        Assert.Contains("Delta.Shader.Contract", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Deserialize", generatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeltaGraphicsGenerator_AllowsFragmentOnlyShader()
    {
        const string source = """
            using Delta.Maths;
            using Delta.Shader;

            public struct FragmentPayload
            {
                public Position Position;
            }

            public struct FragmentContext
            {
                [Interstage]
                public FragmentPayload Fragment;
            }

            public static class FragmentOnlyShader
            {
                [FragmentShader]
                public static float4 Fragment(in FragmentContext context) =>
                    new float4(1f, 0f, 0f, 1f);
            }
            """;

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new DeltaGraphicsGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources));
    }

    [Fact]
    public async Task DeltaGraphicsGenerator_MatchesSameNamedMethodsBySymbolIdentity()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;

            public static class FirstGraphics
            {
                [Interstage]
                public struct Payload
                {
                     public Position Position;
                }

                public struct VertexContext { [Interstage] public Payload Vertex; }
                public struct FragmentContext { [Interstage] public Payload Fragment; }

                [VertexShader(""first"")]
                public static Payload Vertex(in VertexContext context) => new Payload
                {
                    Position = new float4((float)ShaderBuiltins.VertexIndex, 0.0f, 0.0f, 1.0f)
                };

                [FragmentShader(""first"")]
                public static float4 Fragment(in FragmentContext context) =>
                    new float4(1.0f, 0.0f, 0.0f, 1.0f);
            }

            public static class SecondGraphics
            {
                [Interstage]
                public struct Payload
                {
                     public Position Position;
                }

                public struct VertexContext { [Interstage] public Payload Vertex; }
                public struct FragmentContext { [Interstage] public Payload Fragment; }

                [VertexShader(""second"")]
                public static Payload Vertex(in VertexContext context) => new Payload
                {
                    Position = new float4((float)ShaderBuiltins.VertexIndex, 0.0f, 0.0f, 1.0f)
                };

                [FragmentShader(""second"")]
                public static float4 Fragment(in FragmentContext context) =>
                    new float4(0.0f, 0.0f, 1.0f, 1.0f);
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        var parseOptions = compilation.SyntaxTrees.First().Options as CSharpParseOptions;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new DeltaGraphicsGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).ToArray();
        Assert.Equal(2, generated.Length);
        Assert.Contains(generated, result => result.HintName == "FirstGraphicsShaderProgram.g.cs");
        Assert.Contains(generated, result => result.HintName == "SecondGraphicsShaderProgram.g.cs");
    }

    [Fact]
    public async Task CompileTimeShaderAnalyzer_RejectsManagedStateReflectionVirtualCallsAndReferenceLocals()
    {
        const string source = @"
            using System.Reflection;
            using Delta.Shader;

            public sealed class VirtualWorker
            {
                public virtual uint Next(uint value) => value;
            }

            public static class CompileTimeInvalid
            {
                public static uint MutableState;

                public readonly struct ComputeContext
                {
                    [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<uint> Input;
                    [Layout(0, 1)] public readonly ReadWriteStorageBuffer<uint> Output;
                }

                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    string managed = ""not a shader value"";
                    var reflected = Assembly.GetExecutingAssembly().GetName();
                    context.Output[ShaderBuiltins.GlobalInvocationId.X] = new VirtualWorker().Next(context.Input[ShaderBuiltins.GlobalInvocationId.X]) + MutableState;
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ComputeEntryPointAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        Diagnostic[] unsupported = diagnostics.Where(diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH014).ToArray();
        Assert.True(unsupported.Length >= 4, string.Join(Environment.NewLine, unsupported.Select(diagnostic => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Contains(unsupported, diagnostic => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Reference local", StringComparison.Ordinal));
        Assert.Contains(unsupported, diagnostic => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Reflection", StringComparison.Ordinal));
        Assert.Contains(unsupported, diagnostic => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Virtual", StringComparison.Ordinal));
        Assert.Contains(unsupported, diagnostic => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("mutable state", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Compute_MapsSupportedEnumConstantsToGlslScalars()
    {
        const string source = @"
            using Delta.Shader;
            public enum Operation : byte { Add = 3 }
            public struct ComputeContext
            {
                [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
            }
            public static class EnumShader
            {
                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    Operation operation = Operation.Add;
                    context.Output[id] = context.Input[id] + (uint)operation;
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("uint", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("3u", result.Module.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Operation", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compute_Rejects64BitEnumStorageAbi()
    {
        const string source = @"
            using Delta.Shader;
            public enum WideOperation : long { Add = 3 }
            public struct ComputeContext
            {
                [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
                [PushConstant] public WideOperation Operation;
            }
            public static class EnumShader
            {
                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    context.Output[id] = context.Input[id];
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("enum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Compute_LowersInstanceMethodOnValueStructWithReceiver()
    {
        const string source = @"
            using Delta.Shader;
            public struct Calculator
            {
                public uint Bias;
                public uint Add(uint value) => value + Bias;
            }
            public struct ComputeContext
            {
                [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
            }
            public static class InstanceShader
            {
                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    Calculator calculator = new Calculator { Bias = 3u };
                    context.Output[id] = calculator.Add(context.Input[id]);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string emitted = string.Join(Environment.NewLine, result.Module!.HelperFunctions);
        Assert.Contains("self", emitted, StringComparison.Ordinal);
        Assert.Contains("member_Bias", emitted, StringComparison.Ordinal);
        Assert.Contains("delta_helper_", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compute_RejectsInstanceMethodOnReferenceType()
    {
        const string source = @"
            using Delta.Shader;
            public sealed class Calculator
            {
                public uint Add(uint value) => value + 1u;
            }
            public struct ComputeContext
            {
                [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
            }
            public static class InstanceShader
            {
                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    Calculator calculator = new Calculator();
                    context.Output[id] = calculator.Add(context.Input[id]);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("instance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Compute_SpecializesGenericValueStructInstanceMethod()
    {
        const string source = @"
            using Delta.Shader;
            public interface IAdder
            {
                uint Add(uint value);
            }
            public struct Adder : IAdder
            {
                public uint Bias;
                public uint Add(uint value) => value + Bias;
            }
            public struct Box<T> where T : unmanaged, IAdder
            {
                public T Worker;
                public uint Apply(uint value) => Worker.Add(value);
            }
            public struct ComputeContext
            {
                [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
            }
            public static class GenericShader
            {
                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    Box<Adder> box = new Box<Adder> { Worker = new Adder { Bias = 3u } };
                    context.Output[id] = box.Apply(context.Input[id]);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string emitted = string.Join(Environment.NewLine, result.Module!.HelperFunctions);
        Assert.Contains("member_Bias", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("IAdder", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("Box<T>", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compute_SpecializesClosedGenericMethodWithInterfaceConstraint()
    {
        const string source = @"
            using Delta.Shader;
            public interface IAdder
            {
                uint Add(uint value);
            }
            public struct Adder : IAdder
            {
                public uint Bias;
                public uint Add(uint value) => value + Bias;
            }
            public static class GenericShader
            {
                public static uint Apply<T>(T worker, uint value) where T : unmanaged, IAdder
                    => worker.Add(value);

                public struct ComputeContext
                {
                    [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                    [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
                }

                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    context.Output[id] = Apply<Adder>(new Adder { Bias = 3u }, context.Input[id]);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string emitted = string.Join(Environment.NewLine, result.Module!.HelperFunctions);
        Assert.Contains("member_Bias", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("IAdder", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("<T>", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compute_LowersStatelessValueStructInstanceMethodWithoutAbiReceiver()
    {
        const string source = @"
            using Delta.Shader;
            public readonly struct StatelessAdder
            {
                public uint Add(uint value) => value + 1u;
            }
            public struct ComputeContext
            {
                [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
            }
            public static class StatelessShader
            {
                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    StatelessAdder adder = default;
                    context.Output[id] = adder.Add(context.Input[id]);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string emitted = string.Join(Environment.NewLine, result.Module!.HelperFunctions);
        Assert.Contains("delta_helper_", emitted, StringComparison.Ordinal);
        Assert.Contains("uint arg_value", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("StatelessAdder self", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compute_LowersReadableInstanceAutoPropertyAsStructMember()
    {
        const string source = @"
            using Delta.Shader;
            public struct Calculator
            {
                public uint Bias { get; set; }
                public uint Add(uint value) => value + Bias;
            }
            public struct ComputeContext
            {
                [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
            }
            public static class PropertyShader
            {
                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    Calculator calculator = new Calculator { Bias = 3u };
                    context.Output[id] = calculator.Add(context.Input[id]);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string emitted = string.Join(Environment.NewLine, result.Module!.HelperFunctions);
        Assert.Contains("member_Bias", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compute_LowersStaticExpressionBodiedProperty()
    {
        const string source = @"
            using Delta.Shader;
            public struct ComputeContext
            {
                [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
            }
            public static class PropertyShader
            {
                private static uint Scale => 3u;

                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    context.Output[id] = context.Input[id] * Scale;
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("* 3u", result.Module!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compute_LowersInstanceExpressionBodiedAndStaticAutoProperties()
    {
        const string source = @"
            using Delta.Shader;
            public struct Calculator
            {
                public uint Bias;
                public uint Doubled => Bias * 2u;
            }
            public struct ComputeContext
            {
                [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
            }
            public static class PropertyShader
            {
                private static uint Scale { get; } = 3u;

                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    Calculator calculator = new Calculator { Bias = context.Input[id] };
                    context.Output[id] = calculator.Doubled * Scale;
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("calculator.member_Bias", result.Module!.Body, StringComparison.Ordinal);
        Assert.Contains("* 3u", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compute_RejectsPropertyMutationInInstanceHelper()
    {
        const string source = @"
            using Delta.Shader;
            public struct Calculator
            {
                public uint Bias { get; set; }
                public uint Add(uint value)
                {
                    Bias = 2u;
                    return value + Bias;
                }
            }
            public struct ComputeContext
            {
                [Layout(0, 0)] public ReadOnlyStorageBuffer<uint> Input;
                [Layout(0, 1)] public ReadWriteStorageBuffer<uint> Output;
            }
            public static class PropertyShader
            {
                [ComputeShader(localSizeX: 64)]
                public static void Compute(in ComputeContext context)
                {
                    uint id = ShaderBuiltins.GlobalInvocationId.X;
                    Calculator calculator = new Calculator { Bias = 3u };
                    context.Output[id] = calculator.Add(context.Input[id]);
                }
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("mutate a property", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Graphics_SpecializesClosedGenericHelperWithValueStructInterface()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader;
            public interface ITransform
            {
                float4 Apply(float4 value);
            }
            public struct Offset : ITransform
            {
                public float4 Delta;
                public float4 Apply(float4 value) => value + Delta;
            }
            [Interstage]
            public struct VertexPayload
            {
                 public Position Position;
                public Color Color;
            }
            public struct VertexContext
            {
                [Interstage] public VertexPayload Vertex;
            }
            public static class GenericGraphicsShader
            {
                public static float4 Apply<T>(T operation, float4 value) where T : unmanaged, ITransform
                    => operation.Apply(value);

                [VertexShader]
                public static VertexPayload Vertex(in VertexContext context)
                    => new VertexPayload
                    {
                        Position = Apply<Offset>(new Offset { Delta = new float4(1f, 0f, 0f, 0f) }, context.Vertex.Position),
                        Color = context.Vertex.Color
                    };
            }";

        Compilation compilation = await LoadCompilerTestProjectCompilationAsync(source).ConfigureAwait(true);
        ShaderCompilationResult result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string emitted = string.Join(Environment.NewLine, result.Module!.HelperFunctions);
        Assert.Contains("member_Delta", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("ITransform", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("<T>", emitted, StringComparison.Ordinal);
    }

    private static async Task<Compilation> LoadDeltaMathsCompilationAsync(string? extraSource = null)
    {
        var root = Path.Combine(FindRepositoryRoot(), "DeltaMaths", "src", "DeltaMaths", "DeltaMaths.csproj");
        using MSBuildWorkspace workspace = CreateWorkspace();
        Project project = await workspace.OpenProjectAsync(root).ConfigureAwait(true);
        Compilation? baseCompilation = await project.GetCompilationAsync().ConfigureAwait(true);
        Assert.NotNull(baseCompilation);

        if (string.IsNullOrWhiteSpace(extraSource))
        {
            return baseCompilation!;
        }

        CSharpParseOptions parseOptions = baseCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
            ?? CSharpParseOptions.Default;
        SyntaxTree parsedTree = CSharpSyntaxTree.ParseText(extraSource, parseOptions);
        return baseCompilation.AddSyntaxTrees(parsedTree);
    }

    private static async Task<Compilation> LoadReferenceFixtureCompilationAsync()
    {
        var root = ResolveProjectPath("tests", "DeltaShader.Compiler.ReferenceFixtures", "DeltaShader.Compiler.ReferenceFixtures.csproj");
        using MSBuildWorkspace workspace = CreateWorkspace();
        Project project = await workspace.OpenProjectAsync(root).ConfigureAwait(true);
        Compilation? baseCompilation = await project.GetCompilationAsync().ConfigureAwait(true);
        Assert.NotNull(baseCompilation);

        return baseCompilation!;
    }

    private static async Task<Compilation> LoadCompilerTestProjectCompilationAsync(string extraSource)
    {
        var root = ResolveProjectPath("tests", "DeltaShader.Compiler.Tests", "DeltaShader.Compiler.Tests.csproj");
        using MSBuildWorkspace workspace = CreateWorkspace();
        Project project = await workspace.OpenProjectAsync(root).ConfigureAwait(true);
        Compilation? baseCompilation = await project.GetCompilationAsync().ConfigureAwait(true);
        Assert.NotNull(baseCompilation);

        if (string.IsNullOrWhiteSpace(extraSource))
        {
            return baseCompilation!;
        }

        CSharpParseOptions parseOptions = baseCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
            ?? CSharpParseOptions.Default;
        SyntaxTree parsedTree = CSharpSyntaxTree.ParseText(extraSource, parseOptions);
        return baseCompilation.AddSyntaxTrees(parsedTree);
    }

    private static string ResolveProjectPath(params string[] relativeSegments)
    {
        var root = FindRepositoryRoot();
        var shadersRoot = Path.Combine(root, "DeltaShader");
        return Path.GetFullPath(Path.Combine(shadersRoot, Path.Combine(relativeSegments)));
    }

    private static MSBuildWorkspace CreateWorkspace()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        return MSBuildWorkspace.Create();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "DeltaMaths");
            if (Directory.Exists(marker))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
