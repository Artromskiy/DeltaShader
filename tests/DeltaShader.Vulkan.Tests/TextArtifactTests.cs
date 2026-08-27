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
}
