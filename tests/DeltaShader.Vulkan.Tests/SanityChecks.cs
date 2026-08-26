using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DeltaShader.Abstractions;
using DeltaShader.Backend.Glsl;
using DeltaShader.Compiler;
using DeltaShader.Compiler.IR;
using Xunit;
using Xunit.Sdk;

namespace DeltaShader.Vulkan.Tests;

public class SanityChecks
{
    [Fact]
    public void GlslEmitter_Output_Compiles_And_Validates_With_Glslang_When_Available()
    {
        var glslang = ToolPath("glslangValidator");
        var spirvVal = ToolPath("spirv-val");
        if (string.IsNullOrWhiteSpace(glslang) || string.IsNullOrWhiteSpace(spirvVal))
        {
            throw SkipException.ForSkip("Skip: glslangValidator and/or spirv-val is not installed in PATH.");
        }

        var module = new ShaderIrModule
        {
            EntryPointName = "Compute",
            LocalSizeX = 8,
            LocalSizeY = 1,
            LocalSizeZ = 1,
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "input",
                    ParameterName = "input",
                    Category = ShaderResourceKind.StorageBuffer,
                    Set = 0,
                    Binding = 0,
                    GlslType = "float",
                    ReadOnly = true
                },
                new ShaderIrResource
                {
                    Name = "output",
                    ParameterName = "output",
                    Category = ShaderResourceKind.StorageBuffer,
                    Set = 0,
                    Binding = 1,
                    GlslType = "float",
                    ReadOnly = false
                }
            ],
            Requirements = ["Vulkan 1.2", "GLSL 460", "SPIRV 1.5"]
        };

        var emit = GlslEmitter.EmitFromModule(module);
        Assert.True(emit.Success);
        Assert.Contains("void main()", emit.Source, StringComparison.Ordinal);

        var workspace = Path.Combine(Path.GetTempPath(), "delta-shader-vulkan-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var glslFile = Path.Combine(workspace, "shader.glsl");
        var spvFile = Path.Combine(workspace, "shader.spv");
        File.WriteAllText(glslFile, emit.Source);

        var glslCompile = RunTool(glslang, $"-V --target-env vulkan1.2 -S comp {EscapePath(glslFile)} -o {EscapePath(spvFile)}");
        Assert.True(glslCompile.ExitCode == 0, $"glslang failed: {glslCompile.Output}");

        var validation = RunTool(spirvVal, $"--target-env vulkan1.2 {EscapePath(spvFile)}");
        Assert.True(validation.ExitCode == 0, $"spirv-val failed: {validation.Output}");
    }

    [Fact]
    public void FragmentSampledTexture_SdfStyleGlsl_Compiles_And_Validates_With_Glslang_When_Available()
    {
        var glslang = ToolPath("glslangValidator");
        var spirvVal = ToolPath("spirv-val");
        if (string.IsNullOrWhiteSpace(glslang) || string.IsNullOrWhiteSpace(spirvVal))
        {
            throw SkipException.ForSkip("Skip: glslangValidator and/or spirv-val is not installed in PATH.");
        }

        var module = new ShaderIrModule
        {
            Stage = ShaderStage.Fragment,
            SourceEntryPointName = "MsdfTextFragment",
            EntryPointName = "MsdfTextFragment",
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "atlas",
                    ParameterName = "atlas",
                    Category = ShaderResourceKind.SampledTexture2D,
                    Set = 0,
                    Binding = 3,
                    GlslType = "sampler2D",
                    ReadOnly = true
                }
            ],
            Outputs =
            [
                new ShaderIrInterfaceVariable
                {
                    Name = "color",
                    ParameterName = "color",
                    GlslType = "vec4",
                    GlslName = "fragColor",
                    Builtin = "FragmentColor",
                    Location = 0
                }
            ],
            Body = "vec4 texel = texture(atlas, vec2(0.5)); float median = max(min(texel.r, texel.g), min(max(texel.r, texel.g), texel.b)); float edge = fwidth(median - 0.5); float coverage = 1.0 - smoothstep(-edge, edge, median - 0.5); fragColor = vec4(coverage);",
            Requirements = ["Vulkan 1.2", "GLSL 460", "SPIRV 1.5"]
        };

        var emit = GlslEmitter.EmitFromModule(module);
        Assert.Contains("#version 460", emit.Source, StringComparison.Ordinal);
        Assert.Contains("uniform sampler2D", emit.Source, StringComparison.Ordinal);
        Assert.Contains("texture(", emit.Source, StringComparison.Ordinal);
        Assert.Contains("fwidth", emit.Source, StringComparison.Ordinal);

        var workspace = Path.Combine(Path.GetTempPath(), "delta-shader-vulkan-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var glslFile = Path.Combine(workspace, "fragment.glsl");
        var spvFile = Path.Combine(workspace, "fragment.spv");
        File.WriteAllText(glslFile, emit.Source);

        var glslCompile = RunTool(glslang, $"-V --target-env vulkan1.2 -S frag {EscapePath(glslFile)} -o {EscapePath(spvFile)}");
        Assert.True(glslCompile.ExitCode == 0, $"glslang failed: {glslCompile.Output}\n{emit.Source}");
        var validation = RunTool(spirvVal, $"--target-env vulkan1.2 {EscapePath(spvFile)}");
        Assert.True(validation.ExitCode == 0, $"spirv-val failed: {validation.Output}");
    }

    [Fact]
    public void TextPair_GlyphInstanceSsbo_Compiles_And_Validates_With_Glslang_When_Available()
    {
        var glslang = ToolPath("glslangValidator");
        var spirvVal = ToolPath("spirv-val");
        if (string.IsNullOrWhiteSpace(glslang) || string.IsNullOrWhiteSpace(spirvVal))
        {
            throw SkipException.ForSkip("Skip: glslangValidator and/or spirv-val is not installed in PATH.");
        }

        var glyphInstance = new ShaderResourceMemberManifest
        {
            Name = "PixelMin",
            GlslName = "member_PixelMin",
            GlslType = "vec2",
            Offset = 0,
            Alignment = 8,
            Size = 8,
            ArrayStride = 8
        };

        var vertexModule = new ShaderIrModule
        {
            Stage = ShaderStage.Vertex,
            SourceEntryPointName = "sdf-text",
            EntryPointName = "sdf-text",
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "glyphs",
                    ParameterName = "glyphs",
                    Category = ShaderResourceKind.StorageBuffer,
                    Stage = ShaderStage.Vertex,
                    Set = 0,
                    Binding = 0,
                    GlslType = "DeltaStruct_GlyphInstance",
                    ReadOnly = true,
                    Access = ShaderResourceAccess.ReadOnly,
                    Layout = ShaderStd430Layout.Standard,
                    Std430Layout = ShaderStd430Layout.ForStruct(16, 48),
                    Members =
                    [
                        new ShaderIrStructMember { Name = "PixelMin", GlslName = "member_PixelMin", GlslType = "vec2", Offset = 0, Alignment = 8, Size = 8, ArrayStride = 8 },
                        new ShaderIrStructMember { Name = "PixelMax", GlslName = "member_PixelMax", GlslType = "vec2", Offset = 8, Alignment = 8, Size = 8, ArrayStride = 8 },
                        new ShaderIrStructMember { Name = "UvRect", GlslName = "member_UvRect", GlslType = "vec4", Offset = 16, Alignment = 16, Size = 16, ArrayStride = 16 },
                        new ShaderIrStructMember { Name = "Color", GlslName = "member_Color", GlslType = "vec4", Offset = 32, Alignment = 16, Size = 16, ArrayStride = 16 }
                    ]
                }
            ],
            Structs =
            [
                new ShaderIrStruct
                {
                    Name = "GlyphInstance",
                    GlslName = "DeltaStruct_GlyphInstance",
                    Alignment = 16,
                    Size = 48,
                    ArrayStride = 48,
                    Members =
                    [
                        new ShaderIrStructMember { Name = "PixelMin", GlslName = "member_PixelMin", GlslType = "vec2", Offset = 0, Alignment = 8, Size = 8, ArrayStride = 8 },
                        new ShaderIrStructMember { Name = "PixelMax", GlslName = "member_PixelMax", GlslType = "vec2", Offset = 8, Alignment = 8, Size = 8, ArrayStride = 8 },
                        new ShaderIrStructMember { Name = "UvRect", GlslName = "member_UvRect", GlslType = "vec4", Offset = 16, Alignment = 16, Size = 16, ArrayStride = 16 },
                        new ShaderIrStructMember { Name = "Color", GlslName = "member_Color", GlslType = "vec4", Offset = 32, Alignment = 16, Size = 16, ArrayStride = 16 }
                    ]
                }
            ],
            Inputs =
            [
                new ShaderIrInterfaceVariable { Name = "instanceIndex", ParameterName = "instanceIndex", GlslType = "uint", GlslName = "gl_InstanceIndex", Builtin = "InstanceIndex" },
                new ShaderIrInterfaceVariable { Name = "vertexIndex", ParameterName = "vertexIndex", GlslType = "uint", GlslName = "gl_VertexIndex", Builtin = "VertexIndex" }
            ],
            Outputs =
            [
                new ShaderIrInterfaceVariable { Name = "position", ParameterName = "position", GlslType = "vec4", GlslName = "gl_Position", Builtin = "Position" },
                new ShaderIrInterfaceVariable { Name = "uv", ParameterName = "uv", GlslType = "vec2", GlslName = "varying_0", Location = 0 },
                new ShaderIrInterfaceVariable { Name = "glyphColor", ParameterName = "glyphColor", GlslType = "vec4", GlslName = "varying_1", Location = 1 }
            ],
            PushConstants =
            [
                new ShaderIrPushConstant
                {
                    Name = "TextParameters",
                    ParameterName = "parameters",
                    GlslType = "DeltaPushConstants",
                    Alignment = 16,
                    Size = 64,
                    ArrayStride = 64,
                    Members =
                    [
                        new ShaderIrStructMember { Name = "Resolution", GlslName = "member_Resolution", GlslType = "vec2", Offset = 0, Alignment = 8, Size = 8, ArrayStride = 8 },
                        new ShaderIrStructMember { Name = "TextColor", GlslName = "member_TextColor", GlslType = "vec4", Offset = 16, Alignment = 16, Size = 16, ArrayStride = 16 },
                        new ShaderIrStructMember { Name = "OutlineColor", GlslName = "member_OutlineColor", GlslType = "vec4", Offset = 32, Alignment = 16, Size = 16, ArrayStride = 16 },
                        new ShaderIrStructMember { Name = "OutlineWidth", GlslName = "member_OutlineWidth", GlslType = "float", Offset = 48, Alignment = 4, Size = 4, ArrayStride = 4 }
                    ]
                }
            ],
            Body = @"
                DeltaStruct_GlyphInstance glyph = glyphs.data[gl_InstanceIndex];
                vec2 min = glyph.member_PixelMin;
                vec2 max = glyph.member_PixelMax;
                vec2 uvMin = vec2(glyph.member_UvRect.x, glyph.member_UvRect.y);
                vec2 uvMax = vec2(glyph.member_UvRect.z, glyph.member_UvRect.w);
                vec4 pos = vec4(0.0);
                vec2 outUv = uvMin;
                if (gl_VertexIndex == 0u) { pos = vec4((min.x / pushConstants.member_Resolution.x) * 2.0 - 1.0, (min.y / pushConstants.member_Resolution.y) * 2.0 - 1.0, 0.0, 1.0); outUv = uvMin; }
                else if (gl_VertexIndex == 1u) { pos = vec4((max.x / pushConstants.member_Resolution.x) * 2.0 - 1.0, (min.y / pushConstants.member_Resolution.y) * 2.0 - 1.0, 0.0, 1.0); outUv = vec2(uvMax.x, uvMin.y); }
                else if (gl_VertexIndex == 2u) { pos = vec4((min.x / pushConstants.member_Resolution.x) * 2.0 - 1.0, (max.y / pushConstants.member_Resolution.y) * 2.0 - 1.0, 0.0, 1.0); outUv = vec2(uvMin.x, uvMax.y); }
                else if (gl_VertexIndex == 3u) { pos = vec4((min.x / pushConstants.member_Resolution.x) * 2.0 - 1.0, (max.y / pushConstants.member_Resolution.y) * 2.0 - 1.0, 0.0, 1.0); outUv = vec2(uvMin.x, uvMax.y); }
                else if (gl_VertexIndex == 4u) { pos = vec4((max.x / pushConstants.member_Resolution.x) * 2.0 - 1.0, (min.y / pushConstants.member_Resolution.y) * 2.0 - 1.0, 0.0, 1.0); outUv = vec2(uvMax.x, uvMin.y); }
                else { pos = vec4((max.x / pushConstants.member_Resolution.x) * 2.0 - 1.0, (max.y / pushConstants.member_Resolution.y) * 2.0 - 1.0, 0.0, 1.0); outUv = uvMax; }
                gl_Position = pos;
                varying_0 = outUv;
                varying_1 = glyph.member_Color;
            ",
            Requirements = ["Vulkan 1.2", "GLSL 460", "SPIRV 1.5"]
        };

        var fragmentModule = new ShaderIrModule
        {
            Stage = ShaderStage.Fragment,
            SourceEntryPointName = "sdf-text",
            EntryPointName = "sdf-text",
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "atlas",
                    ParameterName = "atlas",
                    Category = ShaderResourceKind.SampledTexture2D,
                    Stage = ShaderStage.Fragment,
                    Set = 0,
                    Binding = 3,
                    GlslType = "sampler2D",
                    ReadOnly = true,
                    Access = ShaderResourceAccess.ReadOnly,
                    Layout = "opaque"
                }
            ],
            Inputs =
            [
                new ShaderIrInterfaceVariable { Name = "uv", ParameterName = "uv", GlslType = "vec2", GlslName = "varying_0", Location = 0 },
                new ShaderIrInterfaceVariable { Name = "glyphColor", ParameterName = "glyphColor", GlslType = "vec4", GlslName = "varying_1", Location = 1 }
            ],
            Outputs =
            [
                new ShaderIrInterfaceVariable { Name = "color", ParameterName = "color", GlslType = "vec4", GlslName = "fragColor", Builtin = "FragmentColor", Location = 0 }
            ],
            PushConstants = vertexModule.PushConstants,
            Body = @"
                vec4 texel = texture(atlas, varying_0);
                float distance = texel.x - 0.5;
                float edge = fwidth(distance);
                float coverage = 1.0 - smoothstep(-edge, edge, distance);
                fragColor = pushConstants.member_TextColor * varying_1 * coverage;
            ",
            Requirements = ["Vulkan 1.2", "GLSL 460", "SPIRV 1.5"]
        };

        ValidateModule(glslang, spirvVal, vertexModule, "sdf-text.vert");
        ValidateModule(glslang, spirvVal, fragmentModule, "sdf-text.frag");
    }

    [Fact]
    public void EditorViewportCube_VertexAndFragment_Compiles_And_Validates_With_Glslang_When_Available()
    {
        var glslang = ToolPath("glslangValidator");
        var spirvVal = ToolPath("spirv-val");
        if (string.IsNullOrWhiteSpace(glslang) || string.IsNullOrWhiteSpace(spirvVal))
        {
            throw SkipException.ForSkip("Skip: glslangValidator and/or spirv-val is not installed in PATH.");
        }

        var vertexModule = new ShaderIrModule
        {
            Stage = ShaderStage.Vertex,
            SourceEntryPointName = "EditorViewportCubeVertex",
            EntryPointName = "EditorViewportCubeVertex",
            Structs =
            [
                new ShaderIrStruct
                {
                    Name = "SceneParameters",
                    GlslName = "DeltaStruct_SceneParameters",
                    Alignment = 16,
                    Size = 224,
                    ArrayStride = 224,
                    Members =
                    [
                        new ShaderIrStructMember { Name = "Model", GlslName = "member_Model", GlslType = "mat4", Offset = 0, Alignment = 16, Size = 64, ArrayStride = 64, MatrixStride = 16 },
                        new ShaderIrStructMember { Name = "View", GlslName = "member_View", GlslType = "mat4", Offset = 64, Alignment = 16, Size = 64, ArrayStride = 64, MatrixStride = 16 },
                        new ShaderIrStructMember { Name = "Projection", GlslName = "member_Projection", GlslType = "mat4", Offset = 128, Alignment = 16, Size = 64, ArrayStride = 64, MatrixStride = 16 },
                        new ShaderIrStructMember { Name = "LightDirection", GlslName = "member_LightDirection", GlslType = "vec3", Offset = 192, Alignment = 16, Size = 12, ArrayStride = 16 },
                        new ShaderIrStructMember { Name = "LightColor", GlslName = "member_LightColor", GlslType = "vec4", Offset = 208, Alignment = 16, Size = 16, ArrayStride = 16 }
                    ]
                }
            ],
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
                    Attributes =
                    [
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
                    Category = ShaderResourceKind.StorageBuffer,
                    Stage = ShaderStage.Vertex,
                    Set = 0,
                    Binding = 0,
                    GlslType = "DeltaStruct_SceneParameters",
                    ReadOnly = true,
                    Access = ShaderResourceAccess.ReadOnly,
                    Layout = ShaderStd430Layout.Standard,
                    Std430Layout = ShaderStd430Layout.ForStruct(16, 224),
                    Members =
                    [
                        new ShaderIrStructMember { Name = "Model", GlslName = "member_Model", GlslType = "mat4", Offset = 0, Alignment = 16, Size = 64, ArrayStride = 64, MatrixStride = 16 },
                        new ShaderIrStructMember { Name = "View", GlslName = "member_View", GlslType = "mat4", Offset = 64, Alignment = 16, Size = 64, ArrayStride = 64, MatrixStride = 16 },
                        new ShaderIrStructMember { Name = "Projection", GlslName = "member_Projection", GlslType = "mat4", Offset = 128, Alignment = 16, Size = 64, ArrayStride = 64, MatrixStride = 16 },
                        new ShaderIrStructMember { Name = "LightDirection", GlslName = "member_LightDirection", GlslType = "vec3", Offset = 192, Alignment = 16, Size = 12, ArrayStride = 16 },
                        new ShaderIrStructMember { Name = "LightColor", GlslName = "member_LightColor", GlslType = "vec4", Offset = 208, Alignment = 16, Size = 16, ArrayStride = 16 }
                    ]
                }
            ],
            Outputs =
            [
                new ShaderIrInterfaceVariable { Name = "clipPosition", ParameterName = "clipPosition", GlslType = "vec4", GlslName = "gl_Position", Builtin = "Position" },
                new ShaderIrInterfaceVariable { Name = "worldNormal", ParameterName = "worldNormal", GlslType = "vec3", GlslName = "varying_0", Location = 0 },
                new ShaderIrInterfaceVariable { Name = "texCoord", ParameterName = "texCoord", GlslType = "vec2", GlslName = "varying_1", Location = 1 }
            ],
            Body = @"
                vec4 modelPosition = scene.data[0].member_Model * vec4(position, 1.0);
                gl_Position = scene.data[0].member_Projection * scene.data[0].member_View * modelPosition;
                varying_0 = normalize((scene.data[0].member_Model * vec4(normal, 0.0)).xyz);
                varying_1 = uv;
            "
        };

        var fragmentModule = new ShaderIrModule
        {
            Stage = ShaderStage.Fragment,
            SourceEntryPointName = "EditorViewportCubeFragment",
            EntryPointName = "EditorViewportCubeFragment",
            Structs = vertexModule.Structs,
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "scene",
                    ParameterName = "scene",
                    Category = ShaderResourceKind.StorageBuffer,
                    Stage = ShaderStage.Fragment,
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
                    Category = ShaderResourceKind.SampledTexture2D,
                    Stage = ShaderStage.Fragment,
                    Set = 0,
                    Binding = 1,
                    GlslType = "sampler2D",
                    ReadOnly = true,
                    Access = ShaderResourceAccess.ReadOnly,
                    Layout = "opaque"
                }
            ],
            Inputs =
            [
                new ShaderIrInterfaceVariable { Name = "worldNormal", ParameterName = "worldNormal", GlslType = "vec3", GlslName = "varying_0", Location = 0 },
                new ShaderIrInterfaceVariable { Name = "texCoord", ParameterName = "texCoord", GlslType = "vec2", GlslName = "varying_1", Location = 1 }
            ],
            Outputs =
            [
                new ShaderIrInterfaceVariable { Name = "color", ParameterName = "color", GlslType = "vec4", GlslName = "fragColor", Builtin = "FragmentColor", Location = 0 }
            ],
            Body = @"
                vec4 baseColor = texture(albedo, varying_1);
                vec3 lightDirection = normalize(-scene.data[0].member_LightDirection);
                float diffuse = max(0.0, dot(varying_0, lightDirection));
                fragColor = baseColor * scene.data[0].member_LightColor * diffuse;
            "
        };

        ValidateModule(glslang, spirvVal, vertexModule, "editor-viewport-cube.vert");
        ValidateModule(glslang, spirvVal, fragmentModule, "editor-viewport-cube.frag");
    }

    private static string? ToolPath(string toolName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separators = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ';' }
            : new[] { ':' };

        foreach (var part in pathEnv.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(part, toolName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var exeCandidate = candidate + ".exe";
                if (File.Exists(exeCandidate))
                {
                    return exeCandidate;
                }
            }
        }

        return null;
    }

    private static (int ExitCode, string Output) RunTool(string fileName, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var output = new StringBuilder();
        process.Start();
        output.AppendLine(process.StandardOutput.ReadToEnd());
        output.AppendLine(process.StandardError.ReadToEnd());
        process.WaitForExit();

        return (process.ExitCode, output.ToString());
    }

    private static void ValidateModule(string glslang, string spirvVal, ShaderIrModule module, string stem)
    {
        var emit = GlslEmitter.EmitFromModule(module);
        Assert.True(emit.Success);

        var workspace = Path.Combine(Path.GetTempPath(), "delta-shader-vulkan-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var glslFile = Path.Combine(workspace, $"{stem}.glsl");
        var spvFile = Path.Combine(workspace, $"{stem}.spv");
        File.WriteAllText(glslFile, emit.Source);

        var stage = module.Stage == ShaderStage.Vertex ? "vert" : "frag";
        var glslCompile = RunTool(glslang, $"-V --target-env vulkan1.2 -S {stage} {EscapePath(glslFile)} -o {EscapePath(spvFile)}");
        Assert.True(glslCompile.ExitCode == 0, $"glslang failed: {glslCompile.Output}\n{emit.Source}");

        var validation = RunTool(spirvVal, $"--target-env vulkan1.2 {EscapePath(spvFile)}");
        Assert.True(validation.ExitCode == 0, $"spirv-val failed: {validation.Output}");
    }

    private static string EscapePath(string value) => $"\"{value}\"";
}
