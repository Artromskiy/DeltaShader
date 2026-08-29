using System.Reflection;
using Delta.Shader.Text;
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
    public void TextGraphicsProgramExposesResolvedAbiAndDirectParameterPackers(
        string programName,
        string vertexPackerName,
        string fragmentPackerName)
    {
        Type programType = typeof(TextShaders).Assembly.GetType("Delta.Shader.Text." + programName)
            ?? throw new InvalidOperationException("Generated text graphics program was not found: " + programName);

        Assert.NotNull(programType.GetProperty("VertexAbi", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(programType.GetProperty("FragmentAbi", BindingFlags.Public | BindingFlags.Static));
        Assert.Contains(programType.GetMethods(BindingFlags.Public | BindingFlags.Static), method => method.Name == vertexPackerName);
        Assert.Contains(programType.GetMethods(BindingFlags.Public | BindingFlags.Static), method => method.Name == fragmentPackerName);
    }
}
