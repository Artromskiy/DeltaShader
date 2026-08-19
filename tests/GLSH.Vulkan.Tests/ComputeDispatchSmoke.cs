using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Delta.Render.Core;
using Delta.Render.Vulkan;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Delta.Shader.Compiler.IR;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;
using Xunit.Sdk;

namespace Delta.Shader.Vulkan.Tests;

public sealed class ComputeDispatchSmoke
{
    [Fact]
    public async Task ComputeShader_Emits_Spv_Dispatches_Through_DeltaRender()
    {
        var glslang = ToolPath("glslangValidator");
        var spirvVal = ToolPath("spirv-val");
        if (string.IsNullOrWhiteSpace(glslang) || string.IsNullOrWhiteSpace(spirvVal))
        {
            throw new SkipException("Skip: glslangValidator and/or spirv-val is not installed in PATH.");
        }

        var compilation = await LoadTestShaderCompilationAsync();
        var compilationResult = ShaderCompiler.Compile(compilation);
        Assert.True(compilationResult.Success, string.Join(Environment.NewLine, compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(compilationResult.Module);
        Assert.NotNull(compilationResult.Manifest);

        var emit = GlslEmitter.EmitFromModule(compilationResult.Module!);
        Assert.True(emit.Success, emit.Error ?? string.Empty);

        var workspace = Path.Combine(Path.GetTempPath(), "glsh-vulkan-dispatch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var glslFile = Path.Combine(workspace, "compute.glsl");
        var spvFile = Path.Combine(workspace, "compute.spv");
        await File.WriteAllTextAsync(glslFile, emit.Source);

        var compile = RunTool(glslang, $"-V --target-env vulkan1.2 -S comp {EscapePath(glslFile)} -o {EscapePath(spvFile)}");
        Assert.True(compile.ExitCode == 0, $"glslang failed: {compile.Output}");

        var validate = RunTool(spirvVal, $"--target-env vulkan1.2 {EscapePath(spvFile)}");
        Assert.True(validate.ExitCode == 0, $"spirv-val failed: {validate.Output}");

        var spv = await File.ReadAllBytesAsync(spvFile);
        Assert.NotEmpty(spv);
        var metadata = ToRenderMetadata(compilationResult.Manifest!);
        var spirvWords = MemoryMarshal.Cast<byte, uint>(spv);

        await using var device = await CreateComputeDeviceOrSkip();
        await using var pipeline = device.CreateComputePipeline(spvWords, in metadata);

        var manifestPath = Path.Combine(workspace, "compute.shader.json");
        var manifest = RenderManifestFromShaderManifest(compilationResult.Manifest!);
        await File.WriteAllTextAsync(manifestPath, SerializeManifest(manifest), new UTF8Encoding(false));

        foreach (var elementCount in new[] { 0, 1, 7, 8, 9, 64, 65, 129, 256 })
        {
            await DispatchAndVerifyAsync(device, pipeline, elementCount, metadata.LocalSizeX);
        }
    }

    private static async Task<Compilation> LoadTestShaderCompilationAsync()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "GLSH", "tests", "GLSH.TestShaders", "GLSH.TestShaders.csproj");

        using var workspace = MSBuildWorkspace.Create();
        var project = await workspace.OpenProjectAsync(projectPath);
        var compilation = await project.GetCompilationAsync();

        Assert.NotNull(compilation);
        return compilation!;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GLSH.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate GLSH repository root.");
    }

    private static ComputeShaderMetadata ToRenderMetadata(ShaderManifest manifest)
    {
        var bindings = manifest.Resources
            .OrderBy(resource => resource.Set)
            .ThenBy(resource => resource.Binding)
            .Select(resource => new ComputeDescriptorBinding(
                resource.Set,
                resource.Binding,
                ComputeDescriptorKind.StorageBuffer,
                resource.ReadOnly ? ComputeBufferAccess.ReadOnly : ComputeBufferAccess.ReadWrite))
            .ToArray();

        return new ComputeShaderMetadata(
            ComputeAbiLayout.Std430,
            manifest.LocalSizeX,
            manifest.LocalSizeY,
            manifest.LocalSizeZ,
            bindings);
    }

    private static Delta.Render.Core.ShaderAbiManifest RenderManifestFromShaderManifest(ShaderManifest manifest)
    {
        var resources = manifest.Resources
            .Select(resource => new ShaderAbiResource
            {
                Name = resource.Name,
                Kind = ShaderAbiResourceKind.StorageBuffer,
                Stride = resource.ArrayStride,
                Members = new[]
                {
                    new ShaderAbiMember
                    {
                        Name = "data",
                        Offset = 0,
                        Stride = resource.ArrayStride,
                        Size = resource.Size
                    }
                }
            })
            .ToArray();

        return new ShaderAbiManifest
        {
            Version = 1,
            Layout = ShaderAbiLayout.Std430,
            Resources = resources
        };
    }

    private static string SerializeManifest(ShaderAbiManifest manifest)
        => JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });

    private static async Task DispatchAndVerifyAsync(IComputeDevice device, IComputePipeline pipeline, int elementCount, uint localSizeX)
    {
        if (elementCount == 0)
        {
            var noOp = device.Dispatch(pipeline, ReadOnlySpan<ComputeBufferBinding>.Empty, 0);
            Assert.Equal(ComputeDispatchStatus.NoOp, noOp.Status);
            return;
        }

        var outputValues = new float[elementCount];
        for (var i = 0; i < outputValues.Length; i++)
        {
            outputValues[i] = (i + 1) * 0.5f;
        }

        var inputBytes = MemoryMarshal.AsBytes(outputValues.AsSpan()).ToArray();

        var inputCountBytes = checked((ulong)(elementCount * sizeof(float)));
        await using var input = device.CreateStorageBuffer(inputCountBytes);
        await using var output = device.CreateStorageBuffer(inputCountBytes);
        Assert.True(device.Upload(input, inputBytes));
        Assert.True(device.Upload(output, new byte[inputBytes.Length]));

        var groups = (uint)((elementCount + (int)localSizeX - 1) / localSizeX);
        var dispatch = device.Dispatch(
            pipeline,
            new[]
            {
                new ComputeBufferBinding(0, 0, input),
                new ComputeBufferBinding(0, 1, output)
            },
            groups);

        Assert.True(dispatch.Succeeded, dispatch.Error);
        Assert.Equal(ComputeDispatchStatus.Executed, dispatch.Status);

        var readback = new byte[inputBytes.Length];
        Assert.True(device.Readback(output, readback));

        var actual = MemoryMarshal.Cast<byte, float>(readback);
        for (var i = 0; i < outputValues.Length; i++)
        {
            var expected = outputValues[i] * 2f + 1f;
            Assert.Equal(expected, actual[i]);
        }
    }

    private static async Task<VulkanComputeDevice> CreateComputeDeviceOrSkip()
    {
        try
        {
            return await Task.Run(static () => new VulkanComputeDevice(new VulkanRendererOptions()));
        }
        catch (Exception ex)
        {
            throw new SkipException($"Skip: unable to initialize Delta.Render Vulkan device. {ex.Message}");
        }
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
            RedirectStandardError = true
        };

        var output = new StringBuilder();
        process.Start();
        output.AppendLine(process.StandardOutput.ReadToEnd());
        output.AppendLine(process.StandardError.ReadToEnd());
        process.WaitForExit();

        return (process.ExitCode, output.ToString());
    }

    private static string EscapePath(string value) => $"\"{value}\"";
}
