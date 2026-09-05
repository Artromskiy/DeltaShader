using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Delta.Shader.Compiler.IR;
using Xunit;

namespace Delta.Shader.Golden.Tests;

public sealed class UiShaderGoldenTests
{
    [Fact]
    public void TextCoverage_EmitsPositiveDistanceAndFiniteOutlineBand()
    {
        var module = new ShaderIrModule
        {
            Stage = ShaderStage.Fragment,
            SourceEntryPointName = "SdfTextFragment",
            EntryPointName = "SdfTextFragment",
            PushConstants =
            [new ShaderIrPushConstant
            {
                Name = "DeltaPushConstants",
                ParameterName = "parameters",
                GlslType = "DeltaStruct_TextParameters",
                Alignment = 16,
                Size = 64,
                ArrayStride = 64,
                Members =
                [
                    new ShaderIrStructMember { Name = "Resolution", GlslName = "member_Resolution", GlslType = "vec2", Offset = 0, Alignment = 8, Size = 8, ArrayStride = 8 },
                    new ShaderIrStructMember { Name = "TextColor", GlslName = "member_TextColor", GlslType = "vec4", Offset = 16, Alignment = 16, Size = 16, ArrayStride = 16 },
                    new ShaderIrStructMember { Name = "OutlineColor", GlslName = "member_OutlineColor", GlslType = "vec4", Offset = 32, Alignment = 16, Size = 16, ArrayStride = 16 },
                    new ShaderIrStructMember { Name = "OutlineWidth", GlslName = "member_OutlineWidth", GlslType = "float", Offset = 48, Alignment = 4, Size = 4, ArrayStride = 4 },
                    new ShaderIrStructMember { Name = "DistanceRange", GlslName = "member_DistanceRange", GlslType = "float", Offset = 52, Alignment = 4, Size = 4, ArrayStride = 4 }
                ]
            }],
            Body = "float signedDistance = (texel.x - 0.5) * distanceRange; float edge = max(fwidth(signedDistance) * 0.5, 0.0001); float fillCoverage = smoothstep(-edge, edge, signedDistance); float outerCoverage = smoothstep(-outlineWidth - edge, -outlineWidth + edge, signedDistance); float outlineContribution = max(outerCoverage - fillCoverage, 0.0);"
        };

        var emitted = GlslEmitter.EmitFromModule(module);
        var manifest = ShaderManifest.FromModule(module).ToBuildManifest(ShaderCompilationOptions.Default);

        Assert.Contains("smoothstep(-edge, edge, signedDistance)", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("fwidth(signedDistance) * 0.5", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("smoothstep(-outlineWidth - edge, -outlineWidth + edge, signedDistance)", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("max(outerCoverage - fillCoverage, 0.0)", emitted.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0 - smoothstep", emitted.Source, StringComparison.Ordinal);
        Assert.Equal(64u, Assert.Single(manifest.PushConstants).Size);
        Assert.Equal(52u, Assert.Single(manifest.PushConstants).Members.Single(member => member.Name == "DistanceRange").Offset);
    }

    [Fact]
    public void RoundedRectangleCoverage_EmitsDerivativeFillAndFiniteBorderBand()
    {
        var module = new ShaderIrModule
        {
            Stage = ShaderStage.Fragment,
            SourceEntryPointName = "RoundedRectangleFragment",
            EntryPointName = "RoundedRectangleFragment",
            Outputs = [new ShaderIrInterfaceVariable
            {
                Name = "color",
                ParameterName = "color",
                GlslType = "vec4",
                GlslName = "fragColor",
                Builtin = "FragmentColor"
            }],
            Body = "float edge = max(fwidth(distance), 0.0001); float outer = 1.0 - smoothstep(-edge, edge, distance); float inner = 1.0 - smoothstep(-edge, edge, distance + borderWidth); float border = max(outer - inner, 0.0); vec3 rgb = fillColor.rgb * inner + borderColor.rgb * border; float alpha = outer; fragColor = vec4(rgb, alpha);"
        };

        var emitted = GlslEmitter.EmitFromModule(module);

        Assert.Contains("#version 460", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("fwidth(distance)", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("1.0 - smoothstep", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("max(outer - inner, 0.0)", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("fillColor.rgb * inner + borderColor.rgb * border", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("float alpha = outer", emitted.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0 - outer", emitted.Source, StringComparison.Ordinal);
    }
}
