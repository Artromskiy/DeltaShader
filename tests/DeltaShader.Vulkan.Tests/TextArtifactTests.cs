using System.Reflection;
using Delta.Render.Text;
using Delta.Shader.Contract;
using Xunit;

namespace Delta.Shader.Vulkan.Tests;

public sealed class TextArtifactTests
{
    [Theory]
    [InlineData(nameof(TextShaders.SdfTextVertex), nameof(TextShaders.SdfTextFragment))]
    [InlineData(nameof(TextShaders.MsdfTextVertex), nameof(TextShaders.MsdfTextFragment))]
    public void TextAuthoringExposesStaticVertexFragmentPair(string vertexName, string fragmentName)
    {
        var vertex = typeof(TextShaders).GetMethod(vertexName, BindingFlags.Public | BindingFlags.Static);
        var fragment = typeof(TextShaders).GetMethod(fragmentName, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(vertex);
        Assert.NotNull(fragment);
        Assert.True(vertex?.IsStatic == true);
        Assert.True(fragment?.IsStatic == true);
    }

    [Theory]
    [InlineData("SdfTextGraphicsShaderProgram", "PackSdfTextVertexParameters", "PackSdfTextFragmentParameters")]
    [InlineData("MsdfTextGraphicsShaderProgram", "PackMsdfTextVertexParameters", "PackMsdfTextFragmentParameters")]
    [InlineData("SdfTextOutlineGlowGraphicsShaderProgram", "PackSdfTextOutlineGlowVertexParameters", "PackSdfTextOutlineGlowFragmentParameters")]
    [InlineData("MsdfTextOutlineGlowGraphicsShaderProgram", "PackMsdfTextOutlineGlowVertexParameters", "PackMsdfTextOutlineGlowFragmentParameters")]
    public void TextGraphicsProgramExposesResolvedAbiAndDirectParameterPackers(
        string programName,
        string vertexPackerName,
        string fragmentPackerName)
    {
        Type programType = typeof(TextShaders).Assembly.GetType("Delta.Render.Text.Shaders." + programName)
            ?? throw new InvalidOperationException("Generated text graphics program was not found: " + programName);

        Assert.NotNull(programType.GetProperty("VertexAbi", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(programType.GetProperty("FragmentAbi", BindingFlags.Public | BindingFlags.Static));
        Assert.Contains(programType.GetMethods(BindingFlags.Public | BindingFlags.Static), method => method.Name == vertexPackerName);
        Assert.Contains(programType.GetMethods(BindingFlags.Public | BindingFlags.Static), method => method.Name == fragmentPackerName);
    }

    [Theory]
    [InlineData("SdfTextOutlineGlowGraphicsShaderProgram")]
    [InlineData("MsdfTextOutlineGlowGraphicsShaderProgram")]
    public void TextEffectProgramsExposeOneSharedEffectPushConstantRange(string programName)
    {
        Type programType = typeof(TextShaders).Assembly.GetType("Delta.Render.Text." + programName)
            ?? throw new InvalidOperationException("Generated text graphics program was not found: " + programName);

        var vertexAbi = (ShaderAbi)programType.GetProperty("VertexAbi", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var fragmentAbi = (ShaderAbi)programType.GetProperty("FragmentAbi", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var vertexPush = Assert.Single(vertexAbi.PushConstants);
        var fragmentPush = Assert.Single(fragmentAbi.PushConstants);

        Assert.Equal(144u, vertexPush.Size);
        Assert.Equal(vertexPush.Size, fragmentPush.Size);
        Assert.Equal(vertexPush.Layout.Size, fragmentPush.Layout.Size);
        Assert.Equal(16u, vertexPush.Layout.Alignment);
        Assert.Equal(144u, vertexPush.Layout.Size);
        Assert.Equal(14, vertexPush.Layout.Members.Count);
    }
}
