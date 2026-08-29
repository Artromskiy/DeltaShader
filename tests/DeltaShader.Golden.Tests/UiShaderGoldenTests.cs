using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler.IR;
using Xunit;

namespace Delta.Shader.Golden.Tests;

public sealed class UiShaderGoldenTests
{
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
            Body = "float edge = fwidth(distance); float fill = 1.0 - smoothstep(-edge, edge, distance); float inner = 1.0 - smoothstep(-edge, edge, distance + borderWidth); float border = max(fill - inner, 0.0); fragColor = fillColor * inner + borderColor * border;"
        };

        var emitted = GlslEmitter.EmitFromModule(module);

        Assert.Contains("#version 460", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("fwidth(distance)", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("1.0 - smoothstep", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("max(fill - inner, 0.0)", emitted.Source, StringComparison.Ordinal);
        Assert.Contains("fillColor * inner + borderColor * border", emitted.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0 - fill", emitted.Source, StringComparison.Ordinal);
    }
}
