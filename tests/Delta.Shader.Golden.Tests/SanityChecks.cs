using Delta.Shader.Abstractions;
using Delta.Shader.Compiler;
using Delta.Shader.Compiler.IR;
using Delta.Shader.Backend.Glsl;
using Xunit;

namespace Delta.Shader.Golden.Tests;

public class SanityChecks
{
    [Fact]
    public void EmitFromModule_ProducesVulkanStyleComputeSignatureAndResources()
    {
        var module = new ShaderIrModule
        {
            EntryPointName = "ComputeMain",
            LocalSizeX = 16,
            LocalSizeY = 1,
            LocalSizeZ = 1,
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "input",
                    ParameterName = "input",
                    Category = "storage-buffer",
                    Set = 0,
                    Binding = 0,
                    GlslType = "vec4",
                    ReadOnly = true,
                },
                new ShaderIrResource
                {
                    Name = "output",
                    ParameterName = "output",
                    Category = "storage-buffer",
                    Set = 0,
                    Binding = 1,
                    GlslType = "float",
                    ReadOnly = false,
                }
            ],
            Requirements = ["Vulkan 1.2", "GLSL 460"],
            Instructions = ["entrypoint ComputeMain"]
        };

        var emitted = GlslEmitter.EmitFromModule(module);

        Assert.True(emitted.Success);
        Assert.Contains("layout(local_size_x = 16, local_size_y = 1, local_size_z = 1) in;", emitted.Source);
        Assert.Contains("layout(set = 0, binding = 0, std430) readonly buffer _input", emitted.Source);
        Assert.Contains("layout(set = 0, binding = 1, std430) buffer _output", emitted.Source);
        Assert.Contains("vec4 data[]", emitted.Source);
        Assert.Contains("float data_0[]", emitted.Source);
        Assert.Contains("void main()", emitted.Source);
        Assert.DoesNotContain("void ComputeMain()", emitted.Source);

        var manifest = ShaderManifest.FromModule(module);
        Assert.Equal("std430", manifest.StorageLayout);
        Assert.Equal(16u, manifest.Resources[0].Alignment);
        Assert.Equal(16u, manifest.Resources[0].ArrayStride);
        Assert.Equal(0u, manifest.Resources[0].Offset);
        Assert.Null(manifest.Resources[0].MatrixStride);
        Assert.Equal(4u, manifest.Resources[1].Alignment);
        Assert.Equal(4u, manifest.Resources[1].ArrayStride);
    }

    [Fact]
    public void EmitFromModule_SanitizesReservedAndCollidingNames()
    {
        var module = new ShaderIrModule
        {
            EntryPointName = "entry",
            LocalSizeX = 1,
            LocalSizeY = 1,
            LocalSizeZ = 1,
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "if",
                    ParameterName = "if",
                    Category = "storage-buffer",
                    Set = 0,
                    Binding = 0,
                    GlslType = "uint",
                    ReadOnly = true,
                },
                new ShaderIrResource
                {
                    Name = "if",
                    ParameterName = "if2",
                    Category = "storage-buffer",
                    Set = 0,
                    Binding = 1,
                    GlslType = "uint",
                    ReadOnly = false,
                }
            ],
            Requirements = ["Vulkan 1.2", "GLSL 460"],
            Instructions = ["entrypoint entry"]
        };

        var emitted = GlslEmitter.EmitFromModule(module);

        Assert.Contains("layout(set = 0, binding = 0, std430) readonly buffer _if", emitted.Source);
        Assert.Contains("layout(set = 0, binding = 1, std430) buffer _if_0", emitted.Source);
    }

    [Fact]
    public void IdentifierMangler_NormalizesReservedWordsPrefixesAndCollisions()
    {
        var mangler = new GlslIdentifierMangler("main");

        Assert.Equal("_input", mangler.Mangle("input"));
        Assert.Equal("_input_0", mangler.Mangle("_input"));
        Assert.Equal("_gl_Position", mangler.Mangle("gl_Position"));
        Assert.Equal("value_name", mangler.Mangle("value-name"));
        Assert.Equal("value_name_0", mangler.Mangle("value_name"));
        Assert.Equal("_main", mangler.Mangle("main"));
    }

    [Fact]
    public void Std430Layout_ReportsVectorPaddingAndMatrixStrideMetadata()
    {
        var vec3 = ShaderStd430Layout.ForGlslType("vec3");
        Assert.Equal(16u, vec3.Alignment);
        Assert.Equal(12u, vec3.Size);
        Assert.Equal(16u, vec3.ArrayStride);

        var mat3 = ShaderStd430Layout.ForGlslType("mat3");
        Assert.Equal(16u, mat3.Alignment);
        Assert.Equal(16u, mat3.MatrixStride);
        Assert.Equal(48u, mat3.ArrayStride);
    }

    [Fact]
    public void RuntimeArtifactContract_PreservesVersionStageAndKnownAbiMetadata()
    {
        var module = new ShaderIrModule
        {
            EntryPointName = "ComputeMain",
            LocalSizeX = 8,
            LocalSizeY = 1,
            LocalSizeZ = 1,
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "values",
                    ParameterName = "values",
                    Category = "storage-buffer",
                    Set = 2,
                    Binding = 3,
                    GlslType = "vec3",
                    ReadOnly = false
                }
            ]
        };

        var abi = ShaderManifest.FromModule(module).ToAbiManifest(ShaderCompilationOptions.Default);
        var artifact = new ShaderArtifact(new byte[] { 3, 2, 35, 7 }, abi);

        Assert.Equal(ShaderArtifact.CurrentFormatVersion, artifact.FormatVersion);
        Assert.Equal(ShaderStage.Compute, artifact.Stage);
        Assert.Equal("ComputeMain", artifact.Manifest.SourceEntryPointName);
        Assert.Equal("main", artifact.EntryPoint);
        Assert.Equal(ShaderAbiManifest.CurrentVersion, artifact.Manifest.Version);
        Assert.Equal("vulkan1.2", artifact.Manifest.TargetProfile);
        Assert.Equal("460", artifact.Manifest.GlslVersion);
        Assert.Equal("std430", artifact.Manifest.StorageLayout);
        Assert.Equal(2u, artifact.Manifest.Resources[0].Set);
        Assert.Equal(3u, artifact.Manifest.Resources[0].Binding);
        Assert.Equal(ShaderResourceAccess.ReadWrite, artifact.Manifest.Resources[0].Access);
        Assert.Equal(16u, artifact.Manifest.Resources[0].Alignment);
        Assert.Equal(16u, artifact.Manifest.Resources[0].ArrayStride);
        Assert.Equal(12u, artifact.Manifest.Resources[0].Size);
    }
}
