using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using DVG.Shaders.Compiler.Intrinsics;
using DVG.Shaders.Compiler.Syntax;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;

namespace DVG.Shaders.Compiler.Tests;

public class IntrinsicCatalogTests
{
    [Fact]
    public async Task DvgMaths_VectorTypes_AreMappedTo_GlslVectorTypes_BySymbolIdentity()
    {
        var compilation = await LoadMathsCompilationAsync();
        var registry = IntrinsicRegistry.Build(compilation);

        var float2 = compilation.GetTypeByMetadataName("DVG.Maths.float2");
        var int3 = compilation.GetTypeByMetadataName("DVG.Maths.int3");

        Assert.NotNull(float2);
        Assert.NotNull(int3);
        Assert.True(registry.TryMapType(float2!, out var glslFloat2));
        Assert.True(registry.TryMapType(int3!, out var glslInt3));
        Assert.Equal("vec2", glslFloat2);
        Assert.Equal("ivec3", glslInt3);
    }

    [Fact]
    public async Task DvgMaths_MathsFunctions_AreMatchedByISymbol_AndMapOverloads()
    {
        var compilation = await LoadMathsCompilationAsync();
        var registry = IntrinsicRegistry.Build(compilation);
        var maths = compilation.GetTypeByMetadataName("DVG.Maths.maths")!;

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
    public async Task DvgMaths_VectorConstructors_Operators_Swizzles_AreSymbolMapped()
    {
        var compilation = await LoadMathsCompilationAsync();
        var registry = IntrinsicRegistry.Build(compilation);
        var float4 = compilation.GetTypeByMetadataName("DVG.Maths.float4")!;
        var float3 = compilation.GetTypeByMetadataName("DVG.Maths.float3")!;

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
    public async Task DvgMaths_IdentityContract_IgnoresNameCollisionWithoutISymbolMatch()
    {
        var fixtureSource = @"
            using DVG.Maths;

            namespace DVG.Shaders.Compiler.Tests.Fixtures
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
        var dvgMaths = compilation.GetTypeByMetadataName("DVG.Maths.maths")!;
        var fakeMaths = compilation.GetTypeByMetadataName("DVG.Shaders.Compiler.Tests.Fixtures.MathsNameCollision")!;
        var dvgSin = dvgMaths.GetMembers("sin").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Single);
        var fakeSin = fakeMaths.GetMembers("sin").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Single);
        var fakeDot = fakeMaths.GetMembers("dot").OfType<IMethodSymbol>().Single(m =>
            m.Parameters.Length == 2 &&
            m.Parameters[0].Type.Name == "float3" &&
            m.Parameters[1].Type.Name == "float3");

        Assert.True(registry.TryGetIntrinsic(dvgSin, out _));
        Assert.False(registry.TryGetIntrinsic(fakeSin, out _));
        Assert.False(registry.TryGetIntrinsic(fakeDot, out _));
    }

    [Fact]
    public async Task DvgMaths_IntrinsicRegistry_MapsReferenceProjectSymbolsBySymbolIdentity()
    {
        var compilation = await LoadReferenceFixtureCompilationAsync();
        var registry = IntrinsicRegistry.Build(compilation);

        var fixtureType = compilation.GetTypeByMetadataName("DVG.Shaders.Compiler.ReferenceFixtures.VectorSymbolFixture");
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
            .Where(m => m!.ContainingType.ContainingNamespace?.ToDisplayString() == "DVG.Maths")
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
            .Where(p => p!.ContainingType?.ContainingNamespace?.ToDisplayString() == "DVG.Maths")
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
    public async Task ComputeEntryPoint_ResourcesUseSetBindingAndGlslTypeFromSymbol()
    {
        var source = @"
            using DVG.Maths;
            using DVG.Shaders.Abstractions;

            namespace DVG.Shaders.Compiler.Tests.Fixtures
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
            using DVG.Maths;
            using DVG.Shaders.Abstractions;

            namespace DVG.Shaders.Compiler.Tests.Fixtures
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
        Assert.True(result.Diagnostics.Count(d => d.Id == GlshDiagnosticId.GLSH002) >= 2);
    }

    [Fact]
    public async Task ComputeEntryPoint_Rejects_OrdinaryParameters()
    {
        var source = @"
            using DVG.Maths;
            using DVG.Shaders.Abstractions;

            namespace DVG.Shaders.Compiler.Tests.Fixtures
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
        Assert.Contains(result.Diagnostics, d => d.Id == GlshDiagnosticId.GLSH002);
    }

    [Fact]
    public async Task ComputeEntryPoint_Rejects_InvalidProfilePair()
    {
        var source = @"
            using DVG.Maths;
            using DVG.Shaders.Abstractions;

            namespace DVG.Shaders.Compiler.Tests.Fixtures
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
        Assert.Contains(result.Diagnostics, d => d.Id == GlshDiagnosticId.GLSH007);
    }

    [Fact]
    public async Task ComputeEntryPoint_RejectsDuplicateBinding()
    {
        var source = @"
            using DVG.Maths;
            using DVG.Shaders.Abstractions;

            namespace DVG.Shaders.Compiler.Tests.Fixtures
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
        Assert.Contains(result.Diagnostics, d => d.Id == GlshDiagnosticId.GLSH005);
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

    private static async Task<Compilation> LoadMathsCompilationAsync(string? extraSource = null)
    {
        var root = Path.Combine(FindRepositoryRoot(), "Maths", "KibiHex.Maths.csproj");
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
        var root = ResolveProjectPath("tests", "GLSH.Compiler.ReferenceFixtures", "GLSH.Compiler.ReferenceFixtures.csproj");
        using var workspace = CreateWorkspace();
        var project = await workspace.OpenProjectAsync(root);
        var baseCompilation = await project.GetCompilationAsync();
        Assert.NotNull(baseCompilation);

        return baseCompilation!;
    }

    private static async Task<Compilation> LoadCompilerTestProjectCompilationAsync(string extraSource)
    {
        var root = ResolveProjectPath("tests", "GLSH.Compiler.Tests", "GLSH.Compiler.Tests.csproj");
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
        var glshRoot = Path.Combine(root, "GLSH");
        return Path.GetFullPath(Path.Combine(glshRoot, Path.Combine(relativeSegments)));
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
