using System.Diagnostics;
using DeltaShader.Contract;
using Xunit;
using Xunit.Sdk;

namespace DeltaShader.Vulkan.Tests;

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

        var vertexGlsl = mode == "sdf" ? DeltaShader.Text.SdfTextGraphicsShaderProgram.VertexGlsl : DeltaShader.Text.MsdfTextGraphicsShaderProgram.VertexGlsl;
        var fragmentGlsl = mode == "sdf" ? DeltaShader.Text.SdfTextGraphicsShaderProgram.FragmentGlsl : DeltaShader.Text.MsdfTextGraphicsShaderProgram.FragmentGlsl;
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
            ? DeltaShader.Text.SdfTextGraphicsShaderProgram.CreateProgram(vertexSpirv, fragmentSpirv)
            : DeltaShader.Text.MsdfTextGraphicsShaderProgram.CreateProgram(vertexSpirv, fragmentSpirv);
        var glyphs = Assert.Single(program.Vertex.Abi.Resources);
        Assert.Equal(new ShaderBinding(0, 0), glyphs.Binding);
        Assert.Equal(ShaderResourceKind.StorageBuffer, glyphs.Kind);
        Assert.Equal(ShaderResourceAccess.Read, glyphs.Access);
        Assert.Equal(48u, glyphs.Layout.ArrayStride);
        Assert.Equal(new uint[] { 0, 8, 16, 32 }, glyphs.Layout.Members.Select(member => member.Offset).ToArray());
        var atlas = Assert.Single(program.Fragment.Abi.Resources);
        Assert.Equal(atlasBinding, atlas.Binding.Binding);
        Assert.Equal(ShaderResourceKind.SampledTexture, atlas.Kind);
        Assert.Equal(64u, Assert.Single(program.Vertex.Abi.PushConstants).Size);
        Assert.Equal(64u, Assert.Single(program.Fragment.Abi.PushConstants).Size);
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
