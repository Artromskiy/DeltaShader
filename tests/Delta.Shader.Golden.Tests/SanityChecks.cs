using Delta.Shader.Abstractions;
using Delta.Shader.Compiler;
using Delta.Shader.Compiler.IR;
using Delta.Shader.Backend.Glsl;
using Xunit;

namespace Delta.Shader.Golden.Tests;

public class SanityChecks
{
    [Fact]
    public void EmitFromModule_ProducesVulkanFullscreenVertexInterface()
    {
        var module = new ShaderIrModule
        {
            Stage = ShaderStage.Vertex,
            SourceEntryPointName = "Vertex",
            EntryPointName = "Vertex",
            Inputs = [new ShaderIrInterfaceVariable { Name = "vertexIndex", ParameterName = "vertexIndex", GlslType = "uint", GlslName = "gl_VertexIndex", Builtin = "VertexIndex" }],
            Outputs =
            [
                new ShaderIrInterfaceVariable { Name = "position", ParameterName = "position", GlslType = "vec4", GlslName = "gl_Position", Builtin = "Position" },
                new ShaderIrInterfaceVariable { Name = "uv", ParameterName = "uv", GlslType = "vec2", GlslName = "varying_0", Location = 0 }
            ],
            Body = "gl_Position = vec4(-1.0, -1.0, 0.0, 1.0); varying_0 = vec2(0.0, 0.0);"
        };

        var emitted = GlslEmitter.EmitFromModule(module);
        Assert.Contains("#version 460", emitted.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("local_size", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("layout(location = 0) out vec2 varying_0;", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("gl_Position = vec4", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("void main()", emitted.Source, StringComparison.Ordinal);
        var abi = ShaderManifest.FromModule(module).ToAbiManifest(ShaderCompilationOptions.Default);
        Assert.Equal(ShaderStage.Vertex, abi.Stage);
        Assert.Equal("Vertex", abi.SourceEntryPointName);
        Assert.Equal("main", abi.EntryPointName);
    }

    [Fact]
    public void EmitFromModule_ProducesVulkanFullscreenFragmentPushConstantAndDerivativeAbi()
    {
        var module = new ShaderIrModule
        {
            Stage = ShaderStage.Fragment,
            SourceEntryPointName = "Fragment",
            EntryPointName = "Fragment",
            Inputs =
            [
                new ShaderIrInterfaceVariable { Name = "fragmentCoord", ParameterName = "fragmentCoord", GlslType = "vec2", GlslName = "gl_FragCoord", Builtin = "FragmentCoord" },
                new ShaderIrInterfaceVariable { Name = "uv", ParameterName = "uv", GlslType = "vec2", GlslName = "varying_0", Location = 0 }
            ],
            Outputs = [new ShaderIrInterfaceVariable { Name = "color", ParameterName = "color", GlslType = "vec4", GlslName = "fragColor", Builtin = "FragmentColor" }],
            PushConstants =
            [new ShaderIrPushConstant
            {
                Name = "DeltaPushConstants", ParameterName = "constants", GlslType = "DeltaStruct_Constants", Alignment = 16, Size = 16, ArrayStride = 16,
                Members =
                [
                    new ShaderIrStructMember { Name = "Resolution", GlslName = "member_Resolution", GlslType = "vec2", Offset = 0, Alignment = 8, Size = 8, ArrayStride = 8 },
                    new ShaderIrStructMember { Name = "Time", GlslName = "member_Time", GlslType = "float", Offset = 8, Alignment = 4, Size = 4, ArrayStride = 4 }
                ]
            }],
            Body = "float d = fwidth(uv.x); fragColor = vec4(smoothstep(0.0, d, d));"
        };

        var emitted = GlslEmitter.EmitFromModule(module);
        Assert.Contains("layout(push_constant, std430) uniform DeltaPushConstants", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("layout(location = 0) in vec2 varying_0;", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("layout(location = 0) out vec4 fragColor;", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("fwidth", emitted.Source, StringComparison.Ordinal);
        var abi = ShaderManifest.FromModule(module).ToAbiManifest(ShaderCompilationOptions.Default);
        Assert.Equal(ShaderStage.Fragment, abi.Stage);
        Assert.Single(abi.PushConstants);
        Assert.Equal(16u, abi.PushConstants[0].Size);
    }

    [Fact]
    public void EmitFromModule_ProducesVertexInputDeclarationsAndGraphicsBindings()
    {
        var module = new ShaderIrModule
        {
            Stage = ShaderStage.Vertex,
            SourceEntryPointName = "EditorViewportCubeVertex",
            EntryPointName = "EditorViewportCubeVertex",
            VertexInputs =
            [
                new ShaderIrVertexInput { Name = "position", ParameterName = "position", GlslName = "position", GlslType = "vec3", Location = 0, Binding = 0, ByteOffset = 0, InputRate = VertexInputRate.Vertex, ByteSize = 12, Alignment = 4, FormatHint = "VK_FORMAT_R32G32B32_SFLOAT" },
                new ShaderIrVertexInput { Name = "normal", ParameterName = "normal", GlslName = "normal", GlslType = "vec3", Location = 1, Binding = 0, ByteOffset = 12, InputRate = VertexInputRate.Vertex, ByteSize = 12, Alignment = 4, FormatHint = "VK_FORMAT_R32G32B32_SFLOAT" },
                new ShaderIrVertexInput { Name = "uv", ParameterName = "uv", GlslName = "uv", GlslType = "vec2", Location = 2, Binding = 0, ByteOffset = 24, InputRate = VertexInputRate.Vertex, ByteSize = 8, Alignment = 4, FormatHint = "VK_FORMAT_R32G32_SFLOAT" }
            ],
            VertexBuffers =
            [
                new ShaderIrVertexBufferBinding
                {
                    Binding = 0,
                    Stride = 32,
                    InputRate = VertexInputRate.Vertex,
                    Attributes = [
                        new ShaderIrVertexInput { Name = "position", ParameterName = "position", GlslName = "position", GlslType = "vec3", Location = 0, Binding = 0, ByteOffset = 0, InputRate = VertexInputRate.Vertex, ByteSize = 12, Alignment = 4, FormatHint = "VK_FORMAT_R32G32B32_SFLOAT" },
                        new ShaderIrVertexInput { Name = "normal", ParameterName = "normal", GlslName = "normal", GlslType = "vec3", Location = 1, Binding = 0, ByteOffset = 12, InputRate = VertexInputRate.Vertex, ByteSize = 12, Alignment = 4, FormatHint = "VK_FORMAT_R32G32B32_SFLOAT" },
                        new ShaderIrVertexInput { Name = "uv", ParameterName = "uv", GlslName = "uv", GlslType = "vec2", Location = 2, Binding = 0, ByteOffset = 24, InputRate = VertexInputRate.Vertex, ByteSize = 8, Alignment = 4, FormatHint = "VK_FORMAT_R32G32_SFLOAT" }
                    ]
                }
            ],
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "scene",
                    ParameterName = "scene",
                    Category = "storage-buffer",
                    Stage = ShaderStage.Vertex,
                    Set = 0,
                    Binding = 0,
                    GlslType = "DeltaStruct_SceneParameters",
                    ReadOnly = true,
                    Access = ShaderResourceAccess.ReadOnly,
                    Layout = ShaderStd430Layout.Standard,
                    Std430Layout = ShaderStd430Layout.ForStruct(16, 224)
                },
                new ShaderIrResource
                {
                    Name = "albedo",
                    ParameterName = "albedo",
                    Category = "sampled-texture",
                    Stage = ShaderStage.Vertex,
                    Set = 0,
                    Binding = 1,
                    GlslType = "sampler2D",
                    ReadOnly = true,
                    Access = ShaderResourceAccess.ReadOnly,
                    Layout = "opaque"
                }
            ],
            Outputs =
            [
                new ShaderIrInterfaceVariable { Name = "position", ParameterName = "position", GlslType = "vec4", GlslName = "gl_Position", Builtin = "Position" },
                new ShaderIrInterfaceVariable { Name = "worldNormal", ParameterName = "worldNormal", GlslType = "vec3", GlslName = "varying_0", Location = 0 },
                new ShaderIrInterfaceVariable { Name = "texCoord", ParameterName = "texCoord", GlslType = "vec2", GlslName = "varying_1", Location = 1 }
            ],
            Body = "gl_Position = scene.data[0].Projection * scene.data[0].View * scene.data[0].Model * vec4(position, 1.0); varying_0 = normal; varying_1 = uv;"
        };

        var emitted = GlslEmitter.EmitFromModule(module);
        Assert.Contains("layout(location = 0) in vec3 position;", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("layout(location = 1) in vec3 normal;", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("layout(location = 2) in vec2 uv;", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("layout(set = 0, binding = 0, std430) readonly buffer", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("layout(set = 0, binding = 1) uniform sampler2D", emitted.Source, StringComparison.Ordinal);

        var abi = ShaderManifest.FromModule(module).ToAbiManifest(ShaderCompilationOptions.Default);
        Assert.Equal(3, abi.VertexInputs.Count);
        Assert.Single(abi.VertexBufferBindings);
        Assert.Equal(32u, abi.VertexBufferBindings[0].Stride);
        Assert.Equal("VK_FORMAT_R32G32B32_SFLOAT", abi.VertexInputs[0].FormatHint);
        Assert.Equal(12u, abi.VertexInputs[0].ByteSize);
        Assert.Equal(224u, abi.Resources[0].Size);
        Assert.True(abi.Resources[0].ReadOnly);
        Assert.Equal(ShaderResourceAccess.ReadOnly, abi.Resources[0].Access);
    }
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
                    Layout = ShaderStd430Layout.Standard,
                    Std430Layout = ShaderStd430Layout.ForStruct(16, 16),
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
                    Layout = ShaderStd430Layout.Standard,
                    Std430Layout = ShaderStd430Layout.ForStruct(4, 4),
                }
            ],
            Requirements = ["Vulkan 1.2", "GLSL 460"],
            Instructions = ["entrypoint ComputeMain"]
        };

        var emitted = GlslEmitter.EmitFromModule(module);

        Assert.True(emitted.Success);
        Assert.Contains("layout(local_size_x = 16, local_size_y = 1, local_size_z = 1) in;", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("layout(set = 0, binding = 0, std430) readonly buffer _input", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("layout(set = 0, binding = 1, std430) buffer _output", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("vec4 data[]", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("float data_0[]", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("void main()", emitted.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("void ComputeMain()", emitted.Source, StringComparison.Ordinal);

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

        Assert.Contains("layout(set = 0, binding = 0, std430) readonly buffer _if", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("layout(set = 0, binding = 1, std430) buffer _if_0", emitted.Source, StringComparison.Ordinal);
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
                    ReadOnly = false,
                    Layout = ShaderStd430Layout.Standard,
                    Std430Layout = ShaderStd430Layout.ForStruct(16, 16)
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
        Assert.Equal(16u, artifact.Manifest.Resources[0].Size);
    }

    [Fact]
    public void RuntimeArtifactContract_RoundTripsReadOnlyAndVertexInputAbiMetadata()
    {
        var module = new ShaderIrModule
        {
            Stage = ShaderStage.Vertex,
            SourceEntryPointName = "EditorViewportCubeVertex",
            EntryPointName = "EditorViewportCubeVertex",
            VertexInputs =
            [
                new ShaderIrVertexInput
                {
                    Name = "position",
                    ParameterName = "position",
                    GlslName = "position",
                    GlslType = "vec3",
                    Location = 0,
                    Binding = 0,
                    ByteOffset = 0,
                    InputRate = VertexInputRate.Vertex,
                    ByteSize = 12,
                    Alignment = 4,
                    FormatHint = "VK_FORMAT_R32G32B32_SFLOAT"
                }
            ],
            VertexBuffers =
            [
                new ShaderIrVertexBufferBinding
                {
                    Binding = 0,
                    Stride = 12,
                    InputRate = VertexInputRate.Vertex,
                    Attributes = [
                        new ShaderIrVertexInput
                        {
                            Name = "position",
                            ParameterName = "position",
                            GlslName = "position",
                            GlslType = "vec3",
                            Location = 0,
                            Binding = 0,
                            ByteOffset = 0,
                            InputRate = VertexInputRate.Vertex,
                            ByteSize = 12,
                            Alignment = 4,
                            FormatHint = "VK_FORMAT_R32G32B32_SFLOAT"
                        }
                    ]
                }
            ],
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "scene",
                    ParameterName = "scene",
                    Category = "storage-buffer",
                    Stage = ShaderStage.Vertex,
                    Set = 0,
                    Binding = 0,
                    GlslType = "DeltaStruct_SceneParameters",
                    ReadOnly = true,
                    Access = ShaderResourceAccess.ReadOnly,
                    Layout = ShaderStd430Layout.Standard,
                    Std430Layout = ShaderStd430Layout.ForStruct(16, 224)
                }
            ]
        };

        var manifest = ShaderManifest.FromModule(module);
        var abi = manifest.ToAbiManifest(ShaderCompilationOptions.Default);
        var artifact = new ShaderArtifact(new byte[] { 1, 2, 3, 4 }, abi);

        Assert.Single(manifest.VertexInputs);
        Assert.Equal((0u, "vec3", 12u, 4u, "VK_FORMAT_R32G32B32_SFLOAT"),
            (manifest.VertexInputs[0].Location, manifest.VertexInputs[0].GlslType, manifest.VertexInputs[0].ByteSize, manifest.VertexInputs[0].Alignment, manifest.VertexInputs[0].FormatHint));
        Assert.Single(manifest.VertexBufferBindings);
        Assert.Equal(12u, manifest.VertexBufferBindings[0].Stride);
        Assert.Equal(ShaderResourceAccess.ReadOnly, artifact.Manifest.Resources[0].Access);
        Assert.True(artifact.Manifest.Resources[0].ReadOnly);
        Assert.Single(artifact.Manifest.VertexInputs);
        Assert.Equal("VK_FORMAT_R32G32B32_SFLOAT", artifact.Manifest.VertexInputs[0].FormatHint);
        Assert.Equal(224u, artifact.Manifest.Resources[0].Size);
        Assert.Single(artifact.Manifest.VertexBufferBindings);
        Assert.Equal(12u, artifact.Manifest.VertexBufferBindings[0].Stride);
    }

    [Fact]
    public void RuntimeArtifactContract_RejectsLegacyAbiVersions()
    {
        var manifest = new ShaderAbiManifest { Version = ShaderAbiManifest.CurrentVersion - 1 };
        Assert.Throws<ArgumentException>(() => new ShaderArtifact(new byte[] { 1, 2, 3, 4 }, manifest));
    }

    [Fact]
    public void ComputeDispatchRequest_ValidatesArtifactBindingsAndCalculatesGroups()
    {
        var manifest = new ShaderAbiManifest
        {
            Stage = ShaderStage.Compute,
            LocalSizeX = 8,
            LocalSizeY = 1,
            LocalSizeZ = 1,
            Resources =
            [new ShaderAbiResource
            {
                Set = 0,
                Binding = 1,
                Access = ShaderResourceAccess.ReadWrite
            }]
        };
        var artifact = new ShaderArtifact(new byte[] { 3, 2, 35, 7 }, manifest);

        var dimensions = ComputeDispatchDimensions.ForElements(artifact, 9);
        var request = new ComputeDispatchRequest<int>(
            artifact,
            dimensions,
            [new ComputeDispatchBinding<int>(0, 1, 42)]);

        Assert.Equal(new ComputeDispatchDimensions(2, 1, 1), request.Dimensions);
        Assert.Equal(42, request.Bindings[0].Resource);
        Assert.Throws<ArgumentException>(() => new ComputeDispatchRequest<int>(
            artifact,
            dimensions,
            [new ComputeDispatchBinding<int>(0, 0, 42)]));
    }

    [Fact]
    public void EmitFromModule_EmitsStructuredStd430RecordAndMemberMetadata()
    {
        var module = new ShaderIrModule
        {
            EntryPointName = "Compute",
            LocalSizeX = 8,
            LocalSizeY = 1,
            LocalSizeZ = 1,
            Structs =
            [
                new ShaderIrStruct
                {
                    Name = "TransformRecord",
                    GlslName = "DeltaStruct_TransformRecord",
                    Alignment = 16,
                    Size = 96,
                    ArrayStride = 96,
                    Members =
                    [
                        new ShaderIrStructMember
                        {
                            Name = "Position",
                            GlslName = "member_Position",
                            GlslType = "vec3",
                            Offset = 0,
                            Alignment = 16,
                            Size = 12,
                            ArrayStride = 16
                        },
                        new ShaderIrStructMember
                        {
                            Name = "Rotation",
                            GlslName = "member_Rotation",
                            GlslType = "vec4",
                            Offset = 16,
                            Alignment = 16,
                            Size = 16,
                            ArrayStride = 16
                        },
                        new ShaderIrStructMember
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
                    ]
                }
            ],
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "records",
                    ParameterName = "records",
                    Category = "storage-buffer",
                    Set = 0,
                    Binding = 0,
                    GlslType = "DeltaStruct_TransformRecord",
                    ReadOnly = false,
                    Layout = ShaderStd430Layout.Standard,
                    Std430Layout = ShaderStd430Layout.ForStruct(16, 96),
                    Members =
                    [
                        new ShaderIrStructMember
                        {
                            Name = "Position",
                            GlslName = "member_Position",
                            GlslType = "vec3",
                            Offset = 0,
                            Alignment = 16,
                            Size = 12,
                            ArrayStride = 16
                        },
                        new ShaderIrStructMember
                        {
                            Name = "Rotation",
                            GlslName = "member_Rotation",
                            GlslType = "vec4",
                            Offset = 16,
                            Alignment = 16,
                            Size = 16,
                            ArrayStride = 16
                        },
                        new ShaderIrStructMember
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
                    ]
                }
            ]
        };

        var emitted = GlslEmitter.EmitFromModule(module);
        var abi = ShaderManifest.FromModule(module).ToAbiManifest(ShaderCompilationOptions.Default);
        var resource = abi.Resources.Single();

        Assert.Contains("struct DeltaStruct_TransformRecord", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("vec3 member_Position;", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("mat4 member_Transform;", emitted.Source, StringComparison.Ordinal);
        Assert.Equal(96u, resource.ArrayStride);
        Assert.Equal(3, resource.Members.Count);
        Assert.Equal(32u, resource.Members[2].Offset);
        Assert.Equal(16u, resource.Members[2].MatrixStride);
    }

    [Fact]
    public void EmitFromModule_OrdersNestedStructDependenciesBeforeContainingStruct()
    {
        var inner = new ShaderIrStruct
        {
            Name = "Inner",
            GlslName = "DeltaStruct_Inner",
            Alignment = 16,
            Size = 16,
            ArrayStride = 16,
            Members =
            [
                new ShaderIrStructMember
                {
                    Name = "Value",
                    GlslName = "member_Value",
                    GlslType = "vec3",
                    Offset = 0,
                    Alignment = 16,
                    Size = 12,
                    ArrayStride = 16
                }
            ]
        };
        var outer = new ShaderIrStruct
        {
            Name = "Outer",
            GlslName = "DeltaStruct_Outer",
            Alignment = 16,
            Size = 16,
            ArrayStride = 16,
            Members =
            [
                new ShaderIrStructMember
                {
                    Name = "Inner",
                    GlslName = "member_Inner",
                    GlslType = "DeltaStruct_Inner",
                    Offset = 0,
                    Alignment = 16,
                    Size = 16,
                    ArrayStride = 16,
                    Members = inner.Members
                }
            ]
        };

        var emitted = GlslEmitter.EmitFromModule(new ShaderIrModule
        {
            EntryPointName = "Compute",
            LocalSizeX = 1,
            LocalSizeY = 1,
            LocalSizeZ = 1,
            Structs = [outer, inner]
        }).Source;

        Assert.True(emitted.IndexOf("struct DeltaStruct_Inner", StringComparison.Ordinal) <
                    emitted.IndexOf("struct DeltaStruct_Outer", StringComparison.Ordinal));
    }
}
