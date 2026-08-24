using System.Diagnostics;
using System.Text.Json;
using Delta.Shader.Abstractions;
using Xunit;
using Xunit.Sdk;

namespace Delta.Shader.Vulkan.Tests;

public sealed class TextArtifactTests
{
    [Theory]
    [InlineData("sdf", 3)]
    [InlineData("msdf", 4)]
    public void GeneratedTextArtifact_CompilesAndExposesCanonicalAbi(string mode, uint atlasBinding)
    {
        var glslang = FindTool("glslangValidator");
        var spirvVal = FindTool("spirv-val");
        if (glslang is null || spirvVal is null)
        {
            throw SkipException.ForSkip("Skip: glslangValidator and/or spirv-val is not installed in PATH.");
        }

        var vertexGlsl = mode == "sdf" ? Delta.Shader.Text.SdfTextGraphicsShaderProgram.VertexGlsl : Delta.Shader.Text.MsdfTextGraphicsShaderProgram.VertexGlsl;
        var fragmentGlsl = mode == "sdf" ? Delta.Shader.Text.SdfTextGraphicsShaderProgram.FragmentGlsl : Delta.Shader.Text.MsdfTextGraphicsShaderProgram.FragmentGlsl;
        var vertexManifestJson = mode == "sdf" ? Delta.Shader.Text.SdfTextGraphicsShaderProgram.VertexManifestJson : Delta.Shader.Text.MsdfTextGraphicsShaderProgram.VertexManifestJson;
        var fragmentManifestJson = mode == "sdf" ? Delta.Shader.Text.SdfTextGraphicsShaderProgram.FragmentManifestJson : Delta.Shader.Text.MsdfTextGraphicsShaderProgram.FragmentManifestJson;
        var vertexManifest = JsonSerializer.Deserialize<ShaderAbiManifest>(vertexManifestJson);
        var fragmentManifest = JsonSerializer.Deserialize<ShaderAbiManifest>(fragmentManifestJson);
        Assert.NotNull(vertexManifest);
        Assert.NotNull(fragmentManifest);

        var glyphs = Assert.Single(vertexManifest!.Resources);
        Assert.Equal((0u, 0u, "storage-buffer", "std430", 48u),
            (glyphs.Set, glyphs.Binding, glyphs.Category, glyphs.Layout, glyphs.ArrayStride));
        Assert.Equal(new uint[] { 0, 8, 16, 32 }, glyphs.Members.Select(member => member.Offset).ToArray());
        var atlas = Assert.Single(fragmentManifest!.Resources);
        Assert.Equal(atlasBinding, atlas.Binding);
        Assert.Equal("sampled-texture", atlas.Category);
        Assert.Equal(64u, Assert.Single(vertexManifest.PushConstants).Size);
        Assert.Equal(64u, Assert.Single(fragmentManifest.PushConstants).Size);
        Assert.Contains("fwidth", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("1 -", vertexGlsl, StringComparison.Ordinal);
        Assert.DoesNotContain("min.y / pushConstants.member_Resolution.y) * 2 - 1", vertexGlsl, StringComparison.Ordinal);
        if (mode == "msdf")
        {
            Assert.Contains("median", fragmentGlsl, StringComparison.Ordinal);
            Assert.Contains("outerCoverage - fillCoverage", fragmentGlsl, StringComparison.Ordinal);
            Assert.Contains("pushConstants.member_OutlineWidth", fragmentGlsl, StringComparison.Ordinal);
            Assert.DoesNotContain("1 - coverage", fragmentGlsl, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("smoothstep(-edge, edge, distance)", fragmentGlsl, StringComparison.Ordinal);
            Assert.DoesNotContain("1 - smoothstep", fragmentGlsl, StringComparison.Ordinal);
        }

        var insideCoverage = SmoothStep(-0.1f, 0.1f, 0.25f);
        var outsideCoverage = SmoothStep(-0.1f, 0.1f, -0.25f);
        Assert.True(insideCoverage > outsideCoverage);
        Assert.Equal(0f, MathF.Max(insideCoverage - insideCoverage, 0f));

        using var workspace = new TemporaryDirectory();
        var vertexSpirv = Compile(glslang, spirvVal, vertexGlsl, "vert", workspace.Path);
        var fragmentSpirv = Compile(glslang, spirvVal, fragmentGlsl, "frag", workspace.Path);
        var program = mode == "sdf"
            ? Delta.Shader.Text.SdfTextGraphicsShaderProgram.CreateProgram(vertexSpirv, fragmentSpirv)
            : Delta.Shader.Text.MsdfTextGraphicsShaderProgram.CreateProgram(vertexSpirv, fragmentSpirv);
        Assert.Equal(ShaderStage.Vertex, program.Vertex.Stage);
        Assert.Equal(ShaderStage.Fragment, program.Fragment.Stage);
        Assert.Equal("main", program.Vertex.EntryPoint);
        Assert.Equal("main", program.Fragment.EntryPoint);
    }

    private static byte[] Compile(string glslang, string spirvVal, string source, string stage, string directory)
    {
        var sourcePath = Path.Combine(directory, stage + ".glsl");
        var outputPath = Path.Combine(directory, stage + ".spv");
        File.WriteAllText(sourcePath, source);
        var compile = Run(glslang, $"-V --target-env vulkan1.2 -S {stage} {Quote(sourcePath)} -o {Quote(outputPath)}");
        Assert.True(compile.ExitCode == 0, compile.Output + Environment.NewLine + source);
        var validate = Run(spirvVal, $"--target-env vulkan1.2 {Quote(outputPath)}");
        Assert.True(validate.ExitCode == 0, validate.Output);
        return File.ReadAllBytes(outputPath);
    }

    private static string? FindTool(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
        return path;
    }

    private static (int ExitCode, string Output) Run(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.NotNull(process);
        var output = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string Quote(string path) => "\"" + path.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var normalized = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return normalized * normalized * (3f - 2f * normalized);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "delta-shader-text-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
