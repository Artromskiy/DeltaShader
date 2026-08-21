using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Delta.Maths;
using Delta.Shader.Abstractions;
using Delta.Shader.Compiler.Intrinsics;
using Delta.Shader.Compiler.Syntax;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Delta.Shader.Analyzers;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public class IntrinsicCatalogTests
{
    private struct HostTransformBase
    {
        public float3 Position;
    }

    private struct HostTransformRecord
    {
        public HostTransformBase Base;
        public quaternion Rotation;
        public float4x4 Transform;
    }

    [Fact]
    public async Task DeltaMaths_VectorTypes_AreMappedTo_GlslVectorTypes_BySymbolIdentity()
    {
        var compilation = await LoadMathsCompilationAsync();
        var registry = IntrinsicRegistry.Build(compilation);

        var float2 = compilation.GetTypeByMetadataName("Delta.Maths.float2");
        var int3 = compilation.GetTypeByMetadataName("Delta.Maths.int3");

        Assert.NotNull(float2);
        Assert.NotNull(int3);
        Assert.True(registry.TryMapType(float2!, out var glslFloat2));
        Assert.True(registry.TryMapType(int3!, out var glslInt3));
        Assert.Equal("vec2", glslFloat2);
        Assert.Equal("ivec3", glslInt3);
    }

    [Fact]
    public async Task DeltaMaths_MathsFunctions_AreMatchedByISymbol_AndMapOverloads()
    {
        var compilation = await LoadMathsCompilationAsync();
        var registry = IntrinsicRegistry.Build(compilation);
        var maths = compilation.GetTypeByMetadataName("Delta.Maths.maths")!;

        var sinFloat = maths.GetMembers("sin").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Single);
        var dotFloat3 = maths.GetMembers("dot").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 2 &&
            m.Parameters[0].Type.Name == "float3" &&
            m.Parameters[1].Type.Name == "float3");
        var dotFloat4 = maths.GetMembers("dot").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 2 &&
            m.Parameters[0].Type.Name == "float4" &&
            m.Parameters[1].Type.Name == "float4");

        Assert.NotNull(sinFloat);
        Assert.NotNull(dotFloat3);
        Assert.NotNull(dotFloat4);
        Assert.True(registry.TryGetIntrinsic(sinFloat, out var sinIntrinsic));
        Assert.True(registry.TryGetIntrinsic(dotFloat3, out var dot3Intrinsic));
        Assert.True(registry.TryGetIntrinsic(dotFloat4, out var dot4Intrinsic));
        Assert.Equal("sin", sinIntrinsic.GlslName);
        Assert.Equal("dot", dot3Intrinsic.GlslName);
        Assert.Equal("dot", dot4Intrinsic.GlslName);
        Assert.Equal(dot3Intrinsic.GlslName, dot4Intrinsic.GlslName);
    }

    [Fact]
    public async Task DeltaMaths_VectorConstructors_Operators_Swizzles_AreSymbolMapped()
    {
        var compilation = await LoadMathsCompilationAsync();
        var registry = IntrinsicRegistry.Build(compilation);
        var float4 = compilation.GetTypeByMetadataName("Delta.Maths.float4")!;
        var float3 = compilation.GetTypeByMetadataName("Delta.Maths.float3")!;

        var ctorByScalars = float4.InstanceConstructors.First(c =>
            c.Parameters.Length == 4 &&
            c.Parameters.All(p => p.Type.SpecialType == SpecialType.System_Single));
        var ctorByFloat3AndScalar = float4.InstanceConstructors.First(c =>
            c.Parameters.Length == 2 &&
            c.Parameters[0].Type.Name == "float3" &&
            c.Parameters[1].Type.SpecialType == SpecialType.System_Single);
        var plus = float4.GetMembers().OfType<IMethodSymbol>().First(m =>
            m.MethodKind == MethodKind.UserDefinedOperator &&
            m.Name == "op_Addition" &&
            m.Parameters.Length == 2 &&
            m.Parameters.All(p => p.Type.Name == "float4"));
        var swizzle = float3.GetMembers("xyz").OfType<IPropertySymbol>().FirstOrDefault();

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
                public static class MathsNameCollision
                {
                    public static float sin(float x) => x;
                    public static float dot(float3 a, float3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
                }
            }
            ";

        var compilation = await LoadMathsCompilationAsync(fixtureSource);
        var registry = IntrinsicRegistry.Build(compilation);
        var deltaMaths = compilation.GetTypeByMetadataName("Delta.Maths.maths")!;
        var fakeMaths = compilation.GetTypeByMetadataName("Delta.Shader.Compiler.Tests.Fixtures.MathsNameCollision")!;
        var deltaSin = deltaMaths.GetMembers("sin").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Single);
        var fakeSin = fakeMaths.GetMembers("sin").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Single);
        var fakeDot = fakeMaths.GetMembers("dot").OfType<IMethodSymbol>().Single(m =>
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
        var compilation = await LoadReferenceFixtureCompilationAsync();
        var registry = IntrinsicRegistry.Build(compilation);

        var fixtureType = compilation.GetTypeByMetadataName("Delta.Shader.Compiler.ReferenceFixtures.VectorSymbolFixture");
        var method = fixtureType?.GetMembers("SymbolMapKernel").OfType<IMethodSymbol>().SingleOrDefault();

        Assert.NotNull(fixtureType);
        Assert.NotNull(method);
        Assert.NotNull(method!.DeclaringSyntaxReferences.FirstOrDefault());

        var syntax = (MethodDeclarationSyntax)method!.DeclaringSyntaxReferences[0].GetSyntax();
        var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);

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
        var compilation = await LoadMathsCompilationAsync();
        var contract = ShaderContractManifest.LoadEmbedded();
        var registry = IntrinsicRegistry.Build(compilation, contract);

        var matrix = compilation.GetTypeByMetadataName("Delta.Maths.float4x4")!;
        var quaternion = compilation.GetTypeByMetadataName("Delta.Maths.quaternion")!;
        var matrixType = contract.Types.Single(type => type.ClrName == "float4x4");
        var quaternionType = contract.Types.Single(type => type.ClrName == "quaternion");

        Assert.Equal("mat4", matrixType.GlslName);
        Assert.Equal("vec4", quaternionType.GlslName);
        Assert.Equal("Builtin", matrixType.Mapping);
        Assert.Equal("Builtin", quaternionType.Mapping);

        var matrixMultiply = matrix.GetMembers().OfType<IMethodSymbol>().Single(method =>
            method.MethodKind == MethodKind.UserDefinedOperator &&
            method.Name == "op_Multiply" &&
            method.Parameters.Length == 2 &&
            method.Parameters.All(parameter => parameter.Type.Name == "float4x4"));
        var quaternionMultiply = quaternion.GetMembers().OfType<IMethodSymbol>().Single(method =>
            method.MethodKind == MethodKind.UserDefinedOperator &&
            method.Name == "op_Multiply" &&
            method.Parameters.All(parameter => parameter.Type.Name == "quaternion"));

        Assert.True(registry.TryGetIntrinsic(matrixMultiply, out var matrixBinding));
        Assert.True(registry.TryGetIntrinsic(quaternionMultiply, out var quaternionBinding));
        Assert.Equal("*", matrixBinding.GlslName);
        Assert.Equal("matrix", matrixBinding.RequiredCapability);
        Assert.Equal("delta_quaternionMultiply", quaternionBinding.GlslName);
        Assert.Equal("quaternion", quaternionBinding.RequiredCapability);
    }

    [Fact]
    public async Task IntrinsicRegistry_DoesNotRegisterUnsupportedContractIdentities()
    {
        var compilation = await LoadMathsCompilationAsync();
        var contract = new ShaderContractManifest
        {
            Namespace = "Delta.Maths",
            Types = [new ShaderContractType { ClrName = "float2", GlslName = "vec2", Mapping = "Unsupported" }],
            Functions = [new ShaderContractFunction
            {
                TypeClrName = "maths",
                ClrName = "sin",
                ReturnClrName = "float",
                ParameterClrNames = ["float"],
                GlslName = "sin",
                Mapping = "Unsupported"
            }]
        };

        var registry = IntrinsicRegistry.Build(compilation, contract);
        var float2 = compilation.GetTypeByMetadataName("Delta.Maths.float2");
        var maths = compilation.GetTypeByMetadataName("Delta.Maths.maths");
        var sin = maths!.GetMembers("sin").OfType<IMethodSymbol>().Single(method =>
            method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Single);

        Assert.False(registry.TryMapType(float2!, out _));
        Assert.False(registry.TryGetIntrinsic(sin, out _));
    }

    [Fact]
    public async Task ComputeEntryPoint_ResourcesUseSetBindingAndGlslTypeFromSymbol()
    {
        var source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class StorageBufferEntry
                {
                    [ComputeShader(localSizeX: 8, localSizeY: 2, localSizeZ: 4)]
                    public static void Compute(
                        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float3> input,
                        [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<uint2> output)
                    {
                    }
                }
            }
            ";

        var result = await CompileAndValidateEntryPointAsync(source);
        var module = result.Module!;

        Assert.True(result.Success);
        Assert.Equal("Compute", result.EntryPointName);
        Assert.Equal(8u, module.LocalSizeX);
        Assert.Equal(2u, module.LocalSizeY);
        Assert.Equal(4u, module.LocalSizeZ);
        Assert.Equal(2, module.Resources.Count);

        var input = module.Resources.First(r => r.ParameterName == "input");
        var output = module.Resources.First(r => r.ParameterName == "output");
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
    public async Task ComputeEntryPoint_Rejects_Double_AndFixTypes_WithExplicitDiagnostic()
    {
        var source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class InvalidTypesEntry
                {
                    [ComputeShader]
                    public static void Compute(double doubleValue, fix fixValue) { }
                }
            }
        ";

        var result = await CompileAndValidateEntryPointAsync(source);
        Assert.False(result.Success);
        Assert.True(result.Diagnostics.Count(d => d.Id == ShaderDiagnosticId.DSH002) >= 2);
    }

    [Fact]
    public async Task ComputeEntryPoint_Rejects_OrdinaryParameters()
    {
        var source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class InvalidParamEntry
                {
                    [ComputeShader]
                    public static void Compute(
                        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<uint> input,
                        uint invocationIndex)
                    {
                    }
                }
            }
            ";

        var result = await CompileAndValidateEntryPointAsync(source);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == ShaderDiagnosticId.DSH002);
    }

    [Fact]
    public async Task ComputeEntryPoint_Rejects_InvalidProfilePair()
    {
        var source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class ProfileMismatch
                {
                    [ComputeShader(localSizeX: 1)]
                    public static void Compute(
                        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float> input)
                    {
                    }
                }
            }
            ";

        var result = await CompileAndValidateEntryPointAsync(
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
            using Delta.Shader.Abstractions;

            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public static class DuplicateBindingEntry
                {
                    [ComputeShader]
                    public static void Compute(
                        [ReadOnlyStorageBuffer(1, 0)] ReadOnlyStorageBuffer<float> first,
                        [ReadWriteStorageBuffer(1, 0)] ReadWriteStorageBuffer<float> second)
                    { }
                }
            }
            ";

        var result = await CompileAndValidateEntryPointAsync(source);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == ShaderDiagnosticId.DSH005);
    }

    [Fact]
    public async Task ComputeEntryPoint_BuildsStructuredStd430RecordWithMathsTypes()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;

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
                    [ComputeShader(localSizeX: 8)]
                    public static void Compute(
                        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<TransformRecord> input,
                        [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<TransformRecord> output,
                        [GlobalInvocationId] uint invocation)
                    {
                        if (invocation < input.Length)
                            output.Store(invocation, input.Load(invocation));
                    }
                }
            }
            ";

        var result = await CompileAndValidateEntryPointAsync(source);

        Assert.True(result.Success);
        var input = Assert.Single(result.Module!.Resources, resource => resource.ParameterName == "input");
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
        var cases = new[]
        {
            (Source: @"
                using Delta.Shader.Abstractions;
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
                        [ComputeShader] public static void Compute(
                            [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<ExplicitRecord> input) { }
                    }
                }
            ", ExpectedId: ShaderDiagnosticId.DSH006),
            (Source: @"
                using Delta.Shader.Abstractions;
                namespace Delta.Shader.Compiler.Tests.Fixtures
                {
                    public struct ManagedRecord { public string Name; }
                    public static class ManagedEntry
                    {
                        [ComputeShader] public static void Compute(
                            [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<ManagedRecord> input) { }
                    }
                }
            ", ExpectedId: ShaderDiagnosticId.DSH010),
            (Source: @"
                using Delta.Shader.Abstractions;
                namespace Delta.Shader.Compiler.Tests.Fixtures
                {
                    public struct RecursiveRecord { public RecursiveRecord[] Children; }
                    public static class RecursiveEntry
                    {
                        [ComputeShader] public static void Compute(
                            [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<RecursiveRecord> input) { }
                    }
                }
            ", ExpectedId: ShaderDiagnosticId.DSH010),
            (Source: @"
                using Delta.Shader.Abstractions;
                namespace Delta.Shader.Compiler.Tests.Fixtures
                {
                    public struct ArrayFieldRecord { public float[] Values; }
                    public static class ArrayFieldEntry
                    {
                        [ComputeShader] public static void Compute(
                            [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<ArrayFieldRecord> input) { }
                    }
                }
            ", ExpectedId: ShaderDiagnosticId.DSH010)
        };

        foreach (var testCase in cases)
        {
            var result = await CompileAndValidateEntryPointAsync(testCase.Source);
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
                using Delta.Shader.Abstractions;
                public static class EntryParameter
                {
                    [ComputeShader] public static void Compute(string value) { }
                }
            ",
            @"
                using Delta.Shader.Abstractions;
                public class CpuOnlyHelper { public string Name; }
                public struct StorageRecord { public CpuOnlyHelper Helper; }
                public static class StorageEntry
                {
                    [ComputeShader] public static void Compute(
                        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<StorageRecord> values) { }
                }
            ",
            @"
                using Delta.Shader.Abstractions;
                public struct RecursiveRecord { public RecursiveRecord[] Children; }
                public static class RecursiveEntry
                {
                    [ComputeShader] public static void Compute(
                        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<RecursiveRecord> values) { }
                }
            "
        };

        foreach (var source in cases)
        {
            var result = await CompileAndValidateEntryPointAsync(source);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH010);
        }
    }

    [Fact]
    public async Task ShaderVisibleTypeValidation_RejectsPushConstantReferencesButIgnoresCpuOnlyHelpers()
    {
        const string invalidSource = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;
            public class CpuOnlyHelper { public string Name; }
            public struct Constants { public CpuOnlyHelper Helper; }
            public static class InvalidFragment
            {
                [FragmentShader] public static void Fragment(
                    [PushConstant] Constants constants,
                    [FragmentColor] out float4 color)
                { color = new float4(1f, 0f, 0f, 1f); }
            }";

        var invalidCompilation = await LoadCompilerTestProjectCompilationAsync(invalidSource);
        var invalidResult = Assert.Single(ShaderCompiler.CompileAll(invalidCompilation));
        Assert.False(invalidResult.Success);
        Assert.Contains(invalidResult.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH010);

        const string validSource = @"
            using Delta.Shader.Abstractions;
            public class CpuOnlyHelper { public string Name; }
            public static class ValidCompute
            {
                [ComputeShader] public static void Compute([GlobalInvocationId] uint id) { }
            }";

        var validResult = await CompileAndValidateEntryPointAsync(validSource);
        Assert.DoesNotContain(validResult.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH010);
    }

    [Fact]
    public async Task ShaderVisibleTypeAnalyzer_ReportsDsh010ForGraphicsPushConstantGraph()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;
            public class CpuOnlyHelper { public string Name; }
            public struct Constants { public CpuOnlyHelper Helper; }
            public static class InvalidGraphics
            {
                [FragmentShader] public static void Fragment(
                    [PushConstant] Constants constants,
                    [FragmentColor] out float4 color)
                { color = new float4(1f, 0f, 0f, 1f); }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var analyzerResult = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ComputeEntryPointAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        Assert.Contains(analyzerResult, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH010);
    }

    [Fact]
    public void ShaderStd430Packer_PacksTransformRecordWithExplicitPaddingAndColumnMajorMatrix()
    {
        var record = new HostTransformRecord
        {
            Base = new HostTransformBase { Position = new float3(1.25f, -2.5f, 3.75f) },
            Rotation = new quaternion(4.5f, 5.5f, 6.5f, 7.5f),
            Transform = new float4x4(
                new float4(10f, 11f, 12f, 13f),
                new float4(20f, 21f, 22f, 23f),
                new float4(30f, 31f, 32f, 33f),
                new float4(40f, 41f, 42f, 43f))
        };

        var resource = new ShaderAbiResource
        {
            Name = "records",
            GlslType = "DeltaStruct_HostTransformRecord",
            ArrayStride = 96,
            Size = 96,
            Alignment = 16,
            Packing = new ShaderAbiPackingPlan { Stride = 96 },
            Members = new[]
            {
                new ShaderAbiMember
                {
                    Name = "Base",
                    GlslName = "member_Base",
                    GlslType = "DeltaStruct_HostTransformBase",
                    Offset = 0,
                    Alignment = 16,
                    Size = 16,
                    ArrayStride = 16,
                    Members = new[]
                    {
                        new ShaderAbiMember
                        {
                            Name = "Position",
                            GlslName = "member_Position",
                            GlslType = "vec3",
                            Offset = 0,
                            Alignment = 16,
                            Size = 12,
                            ArrayStride = 16
                        }
                    }
                },
                new ShaderAbiMember
                {
                    Name = "Rotation",
                    GlslName = "member_Rotation",
                    GlslType = "vec4",
                    Offset = 16,
                    Alignment = 16,
                    Size = 16,
                    ArrayStride = 16
                },
                new ShaderAbiMember
                {
                    Name = "Transform",
                    GlslName = "member_Transform",
                    GlslType = "mat4",
                    Offset = 32,
                    Alignment = 16,
                    Size = 64,
                    ArrayStride = 64,
                    MatrixStride = 16
                }
            }
        };

        var bytes = ShaderStd430Packer.Pack(new[] { record }, resource);

        Assert.Equal(96, bytes.Length);
        Assert.Equal("uint32", resource.Packing.BoolRepresentation);
        Assert.Equal(1.25f, ReadFloat(bytes, 0));
        Assert.Equal(-2.5f, ReadFloat(bytes, 4));
        Assert.Equal(3.75f, ReadFloat(bytes, 8));
        Assert.Equal(0u, ReadUInt32(bytes, 12));
        Assert.Equal(4.5f, ReadFloat(bytes, 16));
        Assert.Equal(7.5f, ReadFloat(bytes, 28));
        for (var column = 0; column < 4; column++)
        {
            for (var row = 0; row < 4; row++)
            {
                Assert.Equal((column + 1) * 10f + row, ReadFloat(bytes, 32 + column * 16 + row * 4));
            }
        }

        Assert.Equal(0x4f48bf5cu, Fnv1a(bytes));
    }

    private static float ReadFloat(byte[] bytes, int offset)
        => BitConverter.ToSingle(bytes, offset);

    private static uint ReadUInt32(byte[] bytes, int offset)
        => BitConverter.ToUInt32(bytes, offset);

    private static uint Fnv1a(byte[] bytes)
    {
        var hash = 2166136261u;
        foreach (var value in bytes)
        {
            hash = (hash ^ value) * 16777619u;
        }

        return hash;
    }

    [Fact]
    public async Task GraphicsEntryPoints_BuildVertexAndFragmentModulesWithStageAbi()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;
            namespace Delta.Shader.Compiler.Tests.Fixtures
            {
                public struct Constants { public float2 Resolution; public float Time; }
                public static class Graphics
                {
                    [VertexShader(""FullscreenVertex"")] public static void Vertex([VertexIndex] uint index, [Position] out float4 position, [ShaderVarying(0)] out float2 uv)
                    { position = new float4(-1f, -1f, 0f, 1f); uv = new float2(0f, 0f); }
                    [FragmentShader(""FullscreenFragment"")] public static void Fragment([FragmentCoord] float2 coord, [PushConstant] Constants constants, [ShaderVarying(0)] float2 uv, [FragmentColor] out float4 color)
                    { var normalized = float2.Normalize(uv); var edge = ShaderIntrinsics.fwidth(coord.x); color = new float4(edge, constants.Time, normalized.x, 1f); }
                }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var results = ShaderCompiler.CompileAll(compilation);
        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message))));
        var vertex = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Vertex);
        Assert.Equal("FullscreenVertex", vertex.Module!.SourceEntryPointName);
        var fragment = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Fragment);
        Assert.Equal("FullscreenFragment", fragment.Module!.SourceEntryPointName);
        Assert.Equal("main", fragment.AbiManifest!.EntryPointName);
        Assert.Single(fragment.AbiManifest.PushConstants);
    }

    [Fact]
    public async Task GraphicsEntryPoints_TransformConformancePreservesColumnMajorCpuGpuContract()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;
            public struct TransformConstants
            {
                public float4x4 Model;
                public float4x4 View;
                public float4x4 Projection;
            }
            public static class TransformConformance
            {
                [VertexShader(""CubeVertex"")]
                public static void Vertex(
                    [PushConstant] TransformConstants constants,
                    [Position] out float4 position)
                {
                    var vertex = new float3(1f, 2f, 3f);
                    position = constants.Projection * constants.View * constants.Model * new float4(vertex, 1f);
                }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var module = result.Module!;
        var push = Assert.Single(result.AbiManifest!.PushConstants);
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
        Assert.Contains("layout(push_constant, std430) uniform DeltaPushConstants", glsl);
        Assert.Contains("layout(offset = 0) mat4 member_Model", glsl);
        Assert.Contains("layout(offset = 64) mat4 member_View", glsl);
        Assert.Contains("layout(offset = 128) mat4 member_Projection", glsl);
        Assert.Contains("gl_Position", glsl);
        var projectionIndex = glsl.IndexOf("pushConstants.member_Projection", StringComparison.Ordinal);
        var viewIndex = glsl.IndexOf("pushConstants.member_View", StringComparison.Ordinal);
        var modelIndex = glsl.IndexOf("pushConstants.member_Model", StringComparison.Ordinal);
        Assert.True(projectionIndex >= 0 && projectionIndex < viewIndex && viewIndex < modelIndex);
        Assert.DoesNotContain("transpose", glsl, StringComparison.OrdinalIgnoreCase);

        var model = float4x4.CreateTRS(new float3(4f, -1f, 2f), quaternion.CreateFromAxisAngle(new float3(0f, 1f, 0f), 0.35f), new float3(2f, 3f, 4f));
        var view = float4x4.CreateLookTo(new float3(0f, 1f, -8f), new float3(0f, 0f, 1f), new float3(0f, 1f, 0f));
        var projection = float4x4.CreatePerspectiveFieldOfViewLeftHanded(global::Delta.Maths.Maths.Radians(60f), 16f / 9f, 0.1f, 100f);
        var vertex = new float4(1f, 2f, 3f, 1f);
        var cpu = projection * view * model * vertex;
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
            using Delta.Shader.Abstractions;

            public struct SceneParameters
            {
                public float4x4 Model;
                public float4x4 View;
                public float4x4 Projection;
                public float3 LightDirection;
                public float _Padding0;
                public float4 LightColor;
            }

            public static class EditorViewportCube
            {
                [VertexShader(""EditorViewportCubeVertex"")]
                public static void Vertex(
                    [VertexInput(0)] float3 position,
                    [VertexInput(1)] float3 normal,
                    [VertexInput(2)] float2 uv,
                    [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<SceneParameters> scene,
                    [Position] out float4 clipPosition,
                    [ShaderVarying(0)] out float3 worldNormal,
                    [ShaderVarying(1)] out float2 texCoord)
                {
                    var modelPosition = scene[0].Model * new float4(position, 1f);
                    clipPosition = scene[0].Projection * scene[0].View * modelPosition;
                    worldNormal = maths.normalize((scene[0].Model * new float4(normal, 0f)).xyz);
                    texCoord = uv;
                }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var module = result.Module!;
        Assert.Equal(3, module.VertexInputs.Count);
        Assert.Equal((0u, "vec3", "VK_FORMAT_R32G32B32_SFLOAT"), (module.VertexInputs[0].Location, module.VertexInputs[0].GlslType, module.VertexInputs[0].FormatHint));
        Assert.Equal((1u, "vec3", "VK_FORMAT_R32G32B32_SFLOAT"), (module.VertexInputs[1].Location, module.VertexInputs[1].GlslType, module.VertexInputs[1].FormatHint));
        Assert.Equal((2u, "vec2", "VK_FORMAT_R32G32_SFLOAT"), (module.VertexInputs[2].Location, module.VertexInputs[2].GlslType, module.VertexInputs[2].FormatHint));

        var resource = Assert.Single(module.Resources);
        Assert.Equal("storage-buffer", resource.Category);
        Assert.Equal(ShaderResourceAccess.ReadOnly, resource.Access);
        Assert.Equal(0u, resource.Set);
        Assert.Equal(0u, resource.Binding);
        Assert.Equal(224u, resource.Std430Layout!.Size);
        Assert.Equal(16u, resource.Std430Layout.Alignment);

        var glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(module).Source;
        Assert.Contains("#version 460", glsl);
        Assert.Contains("layout(location = 0) in vec3 position;", glsl);
        Assert.Contains("layout(location = 1) in vec3 normal;", glsl);
        Assert.Contains("layout(location = 2) in vec2 uv;", glsl);
        Assert.Contains("member_Projection", glsl);
        Assert.Contains("member_View", glsl);
        Assert.Contains("member_Model", glsl);
        Assert.DoesNotContain("transpose", glsl, StringComparison.OrdinalIgnoreCase);

        var model = float4x4.CreateTRS(new float3(1f, 2f, 3f), quaternion.CreateFromAxisAngle(new float3(0f, 1f, 0f), 0.5f), new float3(2f, 2f, 2f));
        var view = float4x4.CreateLookTo(new float3(0f, 0f, -5f), new float3(0f, 0f, 1f), new float3(0f, 1f, 0f));
        var projection = float4x4.CreatePerspectiveFieldOfViewLeftHanded(global::Delta.Maths.Maths.Radians(60f), 1f, 0.1f, 100f);
        var vertex = new float4(1f, 0f, 0f, 1f);
        var cpuOrder = projection * view * model * vertex;
        var gpuOrder = projection * view * model * vertex;
        Assert.Equal(cpuOrder, gpuOrder);
    }

    [Fact]
    public async Task GraphicsEntryPoints_RejectsBadVertexInputLocationsStagesAndManagedTypes()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;

            public sealed class ManagedData
            {
                public float Value;
            }

            public static class InvalidViewport
            {
                [FragmentShader(""Fragment"")]
                public static void Fragment([VertexInput(0)] float3 position, [FragmentColor] out float4 color)
                {
                    color = new float4(position, 1f);
                }

                [VertexShader(""Vertex"")]
                public static void Vertex(
                    [VertexInput(0)] float3 first,
                    [VertexInput(0)] float2 duplicate,
                    [VertexInput(1)] ManagedData managed,
                    [Position] out float4 position)
                {
                    position = new float4(first, 1f);
                }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var results = ShaderCompiler.CompileAll(compilation);
        Assert.Equal(2, results.Count);

        var vertex = Assert.Single(results, result => result.Module?.Stage == ShaderStage.Vertex);
        Assert.False(vertex.Success);
        Assert.Contains(vertex.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH013);
        Assert.Contains(vertex.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH010);

        var fragment = Assert.Single(results, result => result.Module?.Stage == ShaderStage.Fragment);
        Assert.False(fragment.Success);
        Assert.Contains(fragment.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH013);
    }

    [Fact]
    public async Task GraphicsEntryPoints_RejectFragmentBuiltinInVertexStage()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;
            public static class InvalidGraphics
            {
                [VertexShader] public static void Vertex([FragmentCoord] float2 coord, [Position] out float4 position)
                { position = new float4(coord.x, 0f, 0f, 1f); }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var result = Assert.Single(ShaderCompiler.CompileAll(compilation));
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH011);
    }

    [Fact]
    public async Task GraphicsEntryPoints_LowerDefaultLiteralToTypedGlslZero()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;
            public static class FullscreenUi
            {
                [VertexShader] public static void Vertex(
                    [VertexIndex] uint index,
                    [Position] out float4 position)
                { position = default; }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("gl_Position", result.Module!.Body);
        Assert.Contains("vec4(0.0)", result.Module.Body);
        Assert.DoesNotContain("default", result.Module.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SampledTexture_CompilesForVertexAndFragment_WithOpaqueAbi()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;
            public static class TextureStages
            {
                public struct TextParameters
                {
                    public float4 TextColor;
                    public float4 OutlineColor;
                    public float OutlineWidth;
                }

                [VertexShader(""sdf-text"")]
                public static void Vertex(
                    [VertexIndex] uint index,
                    [SampledTexture2D(0, 1, ShaderStageMask.Vertex)] SampledTexture2D atlas,
                    [Position] out float4 position,
                    [ShaderVarying(0)] out float2 uv)
                {
                    var sampled = ShaderIntrinsics.SampleVertex<float2, float4>(atlas, new float2(0.5f, 0.5f));
                    position = sampled;
                    uv = new float2(0.5f, 0.5f);
                }

                [FragmentShader(""sdf-text"")]
                public static void Fragment(
                    [SampledTexture2D(0, 2)] SampledTexture2D atlas,
                    [ShaderVarying(0)] float2 uv,
                    [PushConstant] TextParameters parameters,
                    [FragmentColor] out float4 color)
                {
                    var texel = ShaderIntrinsics.SampleFragment<float2, float4>(atlas, uv);
                    var median = maths.max(maths.min(texel.x, texel.y), maths.min(maths.max(texel.x, texel.y), texel.z));
                    var edge = ShaderIntrinsics.fwidth(median - 0.5f);
                    var coverage = 1f - maths.smoothStep(-edge, edge, median - 0.5f);
                    color = parameters.TextColor * coverage + parameters.OutlineColor * (1f - coverage) * parameters.OutlineWidth;
                }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var results = ShaderCompiler.CompileAll(compilation);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message))));

        var vertex = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Vertex);
        var vertexResource = Assert.Single(vertex.AbiManifest!.Resources);
        Assert.Equal("sdf-text", vertex.EntryPointName);
        Assert.Equal("sampled-texture", vertexResource.Category);
        Assert.Equal(ShaderStage.Vertex, vertexResource.Stage);
        Assert.Equal("sampler2D", vertexResource.GlslType);
        Assert.Equal("opaque", vertexResource.Layout);
        Assert.Equal("none", vertexResource.Packing.Scheme);
        Assert.Equal(0u, vertexResource.Packing.Stride);
        var vertexGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(vertex.Module!).Source;
        Assert.Contains("layout(set = 0, binding = 1) uniform sampler2D", vertexGlsl);
        Assert.Contains("texture(", vertexGlsl);
        Assert.Contains("varying_0", vertexGlsl);
        Assert.DoesNotContain("std430) readonly buffer", vertexGlsl);

        var fragment = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Fragment);
        var fragmentResource = Assert.Single(fragment.AbiManifest!.Resources);
        Assert.Equal("sdf-text", fragment.EntryPointName);
        Assert.Equal(ShaderStage.Fragment, fragmentResource.Stage);
        Assert.Equal(2u, fragmentResource.Binding);
        Assert.Equal(0u, fragmentResource.Offset);
        Assert.Equal(0u, fragmentResource.ArrayStride);
        Assert.Equal("main", fragment.AbiManifest.EntryPointName);
        var fragmentGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(fragment.Module!).Source;
        Assert.Contains("layout(set = 0, binding = 2) uniform sampler2D", fragmentGlsl);
        Assert.Contains("fwidth", fragmentGlsl);
        Assert.Contains("smoothstep", fragmentGlsl);
    }

    [Fact]
    public async Task SampledTexture_RejectsStageMaskThatExcludesFragment()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;
            public static class InvalidTextureStage
            {
                [FragmentShader]
                public static void Fragment(
                    [SampledTexture2D(0, 0, ShaderStageMask.Vertex)] SampledTexture2D atlas,
                    [FragmentColor] out float4 color)
                {
                    color = new float4(1f, 1f, 1f, 1f);
                }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH011);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("not enabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GraphicsText_GlyphInstances_AreReflected_AsStd430Ssbo_WithInstanceIndex()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;
            public static class TextScene
            {
                public struct GlyphInstance
                {
                    public float2 PixelMin;
                    public float2 PixelMax;
                    public float4 UvRect;
                    public float4 Color;
                }

                public struct TextParameters
                {
                    public float2 Resolution;
                    public float4 TextColor;
                    public float4 OutlineColor;
                    public float OutlineWidth;
                }

                [VertexShader(""sdf-text"")]
                public static void Vertex(
                    [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<GlyphInstance> glyphs,
                    [InstanceIndex] uint instanceIndex,
                    [VertexIndex] uint vertexIndex,
                    [Position] out float4 position,
                    [ShaderVarying(0)] out float2 uv,
                    [ShaderVarying(1)] out float4 glyphColor,
                    [PushConstant] TextParameters parameters)
                {
                    var glyph = glyphs[instanceIndex];
                    var min = glyph.PixelMin;
                    var max = glyph.PixelMax;
                    var uvMin = new float2(glyph.UvRect.x, glyph.UvRect.y);
                    var uvMax = new float2(glyph.UvRect.z, glyph.UvRect.w);
                    position = new float4(0f, 0f, 0f, 1f);
                    uv = uvMin;
                    glyphColor = glyph.Color;
                    if (vertexIndex == 0u)
                    {
                        position = new float4((min.x / parameters.Resolution.x) * 2f - 1f, (min.y / parameters.Resolution.y) * 2f - 1f, 0f, 1f);
                        uv = uvMin;
                    }
                    else if (vertexIndex == 1u)
                    {
                        position = new float4((max.x / parameters.Resolution.x) * 2f - 1f, (min.y / parameters.Resolution.y) * 2f - 1f, 0f, 1f);
                        uv = new float2(uvMax.x, uvMin.y);
                    }
                    else if (vertexIndex == 2u)
                    {
                        position = new float4((min.x / parameters.Resolution.x) * 2f - 1f, (max.y / parameters.Resolution.y) * 2f - 1f, 0f, 1f);
                        uv = new float2(uvMin.x, uvMax.y);
                    }
                    else if (vertexIndex == 3u)
                    {
                        position = new float4((min.x / parameters.Resolution.x) * 2f - 1f, (max.y / parameters.Resolution.y) * 2f - 1f, 0f, 1f);
                        uv = new float2(uvMin.x, uvMax.y);
                    }
                    else if (vertexIndex == 4u)
                    {
                        position = new float4((max.x / parameters.Resolution.x) * 2f - 1f, (min.y / parameters.Resolution.y) * 2f - 1f, 0f, 1f);
                        uv = new float2(uvMax.x, uvMin.y);
                    }
                    else
                    {
                        position = new float4((max.x / parameters.Resolution.x) * 2f - 1f, (max.y / parameters.Resolution.y) * 2f - 1f, 0f, 1f);
                        uv = uvMax;
                    }
                }

                [FragmentShader(""sdf-text"")]
                public static void Fragment(
                    [SampledTexture2D(0, 3)] SampledTexture2D atlas,
                    [ShaderVarying(0)] float2 uv,
                    [ShaderVarying(1)] float4 glyphColor,
                    [PushConstant] TextParameters parameters,
                    [FragmentColor] out float4 color)
                {
                    var texel = ShaderIntrinsics.SampleFragment<float2, float4>(atlas, uv);
                    var distance = texel.x - 0.5f;
                    var edge = ShaderIntrinsics.fwidth(distance);
                    var coverage = 1f - maths.smoothStep(-edge, edge, distance);
                    color = parameters.TextColor * glyphColor * coverage;
                }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var results = ShaderCompiler.CompileAll(compilation);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message))));

        var vertex = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Vertex);
        var glyphResource = Assert.Single(vertex.AbiManifest!.Resources);
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
        Assert.Equal("InstanceIndex", Assert.Single(vertex.AbiManifest.Inputs, input => input.Builtin == "InstanceIndex").Builtin);
        var vertexGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(vertex.Module!).Source;
        Assert.Contains("gl_InstanceIndex", vertexGlsl);
        Assert.Contains("buffer", vertexGlsl);
        Assert.Contains(".data[", vertexGlsl);

        var fragment = Assert.Single(results, result => result.Module!.Stage == ShaderStage.Fragment);
        Assert.Equal("sampled-texture", Assert.Single(fragment.AbiManifest!.Resources).Category);
        var fragmentGlsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(fragment.Module!).Source;
        Assert.Contains("layout(set = 0, binding = 3) uniform sampler2D", fragmentGlsl);
        Assert.Contains("fwidth", fragmentGlsl);
    }

    [Fact]
    public async Task InstanceIndex_RejectsFragmentStage()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;
            public static class InvalidInstanceIndex
            {
                [FragmentShader]
                public static void Fragment([InstanceIndex] uint instanceIndex, [FragmentColor] out float4 color)
                {
                    color = new float4(instanceIndex, instanceIndex, instanceIndex, 1f);
                }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var result = Assert.Single(ShaderCompiler.CompileAll(compilation));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH011);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("vertex shader", StringComparison.Ordinal));
    }

    private static async Task<ShaderCompilationResult> CompileAndValidateEntryPointAsync(
        string source,
        ShaderCompilationOptions? options = null)
    {
        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var context = new ModuleCompilationContext(compilation);
        var frontend = new RoslynFrontend(compilation);
        return ComputeEntryPoints.ValidateAndBuild(context, frontend, options);
    }

    [Fact]
    public async Task CompileTimeTypedKernel_LowersIndexedResourcesAndMathsThroughTheExistingPipeline()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;

            public static class CompileTimeValid
            {
                [ComputeShader(localSizeX: 64)]
                public static void Compute(
                    [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float> input,
                    [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<float> output,
                    [GlobalInvocationId] uint invocation)
                {
                    output[invocation] = maths.sin(input[invocation]);
                }
            }";

        var result = await CompileAndValidateEntryPointAsync(source);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("output.data[invocation]", result.Module!.Body);
        Assert.Contains("sin(input.data[invocation])", result.Module.Body);
    }

    [Fact]
    public async Task NonGenericUIntBuffers_LowerThroughValidationIrAndGlsl()
    {
        const string source = @"
            using Delta.Shader.Abstractions;

            public static class SimpleCompute
            {
                [DeltaCompute(localSizeX: 64)]
                public static void Compute(
                    [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer input,
                    [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer output,
                    [GlobalInvocationId] uint id)
                {
                    if (id < input.Length) output[id] = input[id] * 2u + 1u;
                }
            }";

        var result = await CompileAndValidateEntryPointAsync(source);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var resources = result.Module!.Resources;
        Assert.Equal("uint", Assert.Single(resources, resource => resource.ParameterName == "input").GlslType);
        Assert.Equal("uint", Assert.Single(resources, resource => resource.ParameterName == "output").GlslType);
        Assert.Contains("output.data[id] = input.data[id] * 2u + 1u", result.Module.Body);

        var glsl = Delta.Shader.Backend.Glsl.GlslEmitter.EmitFromModule(result.Module).Source;
        Assert.Contains("#version 460", glsl);
        Assert.Contains("layout(set = 0, binding = 0, std430) readonly buffer", glsl);
        Assert.Contains("uint data[];", glsl);
    }

    [Fact]
    public async Task DeltaComputeGenerator_EmitsGlslManifestAndArtifactWrapper()
    {
        const string source = @"
            using Delta.Maths;
            using Delta.Shader.Abstractions;

            public static class GeneratedKernel
            {
                [DeltaCompute(localSizeX: 64)]
                public static void Compute(
                    [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float> input,
                    [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<float> output,
                    [GlobalInvocationId] uint invocation)
                {
                    output[invocation] = maths.sin(input[invocation]);
                }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var parseOptions = compilation.SyntaxTrees.First().Options as CSharpParseOptions;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new DeltaComputeGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources));
        Assert.Contains("public const string Glsl", generated.SourceText.ToString());
        Assert.Contains("#version 460", generated.SourceText.ToString());
        Assert.Contains("ManifestJson", generated.SourceText.ToString());
        Assert.Contains("CreateArtifact", generated.SourceText.ToString());
    }

    [Fact]
    public async Task CompileTimeShaderAnalyzer_RejectsManagedStateReflectionVirtualCallsAndReferenceLocals()
    {
        const string source = @"
            using System.Reflection;
            using Delta.Shader.Abstractions;

            public sealed class VirtualWorker
            {
                public virtual uint Next(uint value) => value;
            }

            public static class CompileTimeInvalid
            {
                public static uint MutableState;

                [ComputeShader(localSizeX: 64)]
                public static void Compute(
                    [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<uint> input,
                    [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<uint> output,
                    [GlobalInvocationId] uint invocation)
                {
                    string managed = ""not a shader value"";
                    var reflected = Assembly.GetExecutingAssembly().GetName();
                    output.Store(invocation, new VirtualWorker().Next(input.Load(invocation)) + MutableState);
                }
            }";

        var compilation = await LoadCompilerTestProjectCompilationAsync(source);
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ComputeEntryPointAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        var unsupported = diagnostics.Where(diagnostic => diagnostic.Id == ShaderDiagnosticId.DSH014).ToArray();
        Assert.True(unsupported.Length >= 4, string.Join(Environment.NewLine, unsupported.Select(diagnostic => diagnostic.GetMessage())));
        Assert.Contains(unsupported, diagnostic => diagnostic.GetMessage().Contains("Reference local", StringComparison.Ordinal));
        Assert.Contains(unsupported, diagnostic => diagnostic.GetMessage().Contains("Reflection", StringComparison.Ordinal));
        Assert.Contains(unsupported, diagnostic => diagnostic.GetMessage().Contains("Virtual", StringComparison.Ordinal));
        Assert.Contains(unsupported, diagnostic => diagnostic.GetMessage().Contains("mutable state", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Compilation> LoadMathsCompilationAsync(string? extraSource = null)
    {
        var root = Path.Combine(FindRepositoryRoot(), "Maths", "Delta.Maths.csproj");
        using var workspace = CreateWorkspace();
        var project = await workspace.OpenProjectAsync(root);
        var baseCompilation = await project.GetCompilationAsync();
        Assert.NotNull(baseCompilation);

        if (string.IsNullOrWhiteSpace(extraSource))
        {
            return baseCompilation!;
        }

        var parseOptions = baseCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
            ?? CSharpParseOptions.Default;
        var parsedTree = CSharpSyntaxTree.ParseText(extraSource, parseOptions);
        return baseCompilation.AddSyntaxTrees(parsedTree);
    }

    private static async Task<Compilation> LoadReferenceFixtureCompilationAsync()
    {
        var root = ResolveProjectPath("tests", "Delta.Shader.Compiler.ReferenceFixtures", "Delta.Shader.Compiler.ReferenceFixtures.csproj");
        using var workspace = CreateWorkspace();
        var project = await workspace.OpenProjectAsync(root);
        var baseCompilation = await project.GetCompilationAsync();
        Assert.NotNull(baseCompilation);

        return baseCompilation!;
    }

    private static async Task<Compilation> LoadCompilerTestProjectCompilationAsync(string extraSource)
    {
        var root = ResolveProjectPath("tests", "Delta.Shader.Compiler.Tests", "Delta.Shader.Compiler.Tests.csproj");
        using var workspace = CreateWorkspace();
        var project = await workspace.OpenProjectAsync(root);
        var baseCompilation = await project.GetCompilationAsync();
        Assert.NotNull(baseCompilation);

        if (string.IsNullOrWhiteSpace(extraSource))
        {
            return baseCompilation!;
        }

        var parseOptions = baseCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
            ?? CSharpParseOptions.Default;
        var parsedTree = CSharpSyntaxTree.ParseText(extraSource, parseOptions);
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
            var marker = Path.Combine(current.FullName, "Maths");
            if (Directory.Exists(marker))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
