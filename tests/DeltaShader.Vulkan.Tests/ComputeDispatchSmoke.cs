using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DeltaRender;
using DeltaRender.Vulkan;
using DeltaShader.Abstractions;
using DeltaShader.Backend.Glsl;
using DeltaShader.Compiler;
using DeltaShader.Compiler.IR;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;
using Xunit.Sdk;

namespace DeltaShader.Vulkan.Tests;

public sealed class ComputeDispatchSmoke
{
    [VulkanRuntimeFact]
    public async Task ComputeShader_Emits_Spv_Dispatches_Through_DeltaRender()
    {
        var glslang = ToolPath("glslangValidator");
        var spirvVal = ToolPath("spirv-val");
        Assert.NotNull(glslang);
        Assert.NotNull(spirvVal);

        var compilation = await LoadTestShaderCompilationAsync().ConfigureAwait(false);
        var compilationResult = ShaderCompiler.Compile(compilation);
        Assert.True(compilationResult.Success, string.Join(Environment.NewLine, compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(compilationResult.Module);
        Assert.NotNull(compilationResult.Manifest);

        var emit = GlslEmitter.EmitFromModule(compilationResult.Module!);
        Assert.True(emit.Success);

        var workspace = Path.Combine(Path.GetTempPath(), "delta-shader-vulkan-dispatch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var glslFile = Path.Combine(workspace, "compute.glsl");
        var spvFile = Path.Combine(workspace, "compute.spv");
        await File.WriteAllTextAsync(glslFile, emit.Source).ConfigureAwait(false);

        var compile = RunTool(glslang, $"-V --target-env vulkan1.2 -S comp {EscapePath(glslFile)} -o {EscapePath(spvFile)}");
        Assert.True(compile.ExitCode == 0, $"glslang failed: {compile.Output}");

        var validate = RunTool(spirvVal, $"--target-env vulkan1.2 {EscapePath(spvFile)}");
        Assert.True(validate.ExitCode == 0, $"spirv-val failed: {validate.Output}");

        var spv = await File.ReadAllBytesAsync(spvFile).ConfigureAwait(false);
        Assert.NotEmpty(spv);
        var artifact = new ShaderArtifact(spv, compilationResult.AbiManifest!);

        var device = await CreateComputeDeviceOrSkip().ConfigureAwait(false);
        await using var deviceLease = device.ConfigureAwait(false);
        var dispatcher = new ComputeDispatcher<IComputeStorageBuffer>(device, artifact, static buffer => buffer);
        try
        {
            foreach (var elementCount in new[] { 0, 1, 7, 8, 9, 64, 65, 129, 256 })
            {
                await DispatchAndVerifyAsync(device, dispatcher, elementCount, artifact.Manifest.LocalSizeX).ConfigureAwait(false);
            }
        }
        finally
        {
            await dispatcher.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<Compilation> LoadTestShaderCompilationAsync()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "tests", "DeltaShader.TestShaders", "DeltaShader.TestShaders.csproj");

        using var workspace = MSBuildWorkspace.Create();
        var project = await workspace.OpenProjectAsync(projectPath).ConfigureAwait(false);
        var compilation = await project.GetCompilationAsync().ConfigureAwait(false);

        Assert.NotNull(compilation);
        return compilation!;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DeltaShader.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate DeltaShader repository root.");
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

    private static async Task DispatchAndVerifyAsync(VulkanComputeDevice device, ComputeDispatcher<IComputeStorageBuffer> dispatcher, int elementCount, uint localSizeX)
    {
        if (elementCount == 0)
        {
            Assert.Equal(0u, ComputeDispatchDimensions.ForElements(dispatcher.Artifact, 0).X);
            return;
        }

        var outputValues = new float[elementCount];
        for (var i = 0; i < outputValues.Length; i++)
        {
            outputValues[i] = (i + 1) * 0.5f;
        }

        var inputBytes = MemoryMarshal.AsBytes(outputValues.AsSpan()).ToArray();

        var inputCountBytes = checked((ulong)(elementCount * sizeof(float)));
        var input = device.CreateStorageBuffer(inputCountBytes);
        await using var inputLease = input.ConfigureAwait(false);
        var output = device.CreateStorageBuffer(inputCountBytes);
        await using var outputLease = output.ConfigureAwait(false);
        Assert.True(device.Upload(input, inputBytes));
        Assert.True(device.Upload(output, new byte[inputBytes.Length]));

        var groups = (uint)((elementCount + (int)localSizeX - 1) / localSizeX);
        var request = new ComputeDispatchRequest<IComputeStorageBuffer>(
            dispatcher.Artifact,
            ComputeDispatchDimensions.ForElements(dispatcher.Artifact, (uint)elementCount),
            [
                new ComputeDispatchBinding<IComputeStorageBuffer>(0, 0, input),
                new ComputeDispatchBinding<IComputeStorageBuffer>(0, 1, output)
            ]);
        await dispatcher.DispatchAsync(request).ConfigureAwait(false);

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
            return await Task.Run(static () => new VulkanComputeDevice(new VulkanRendererOptions())).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw SkipException.ForSkip($"Skip: unable to initialize DeltaRender Vulkan device. {ex.Message}");
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

    private sealed class VulkanRuntimeFactAttribute : FactAttribute
    {
        public VulkanRuntimeFactAttribute()
        {
            if (ToolPath("glslangValidator") is null || ToolPath("spirv-val") is null)
            {
                Skip = "Requires glslangValidator and spirv-val in PATH.";
            }
            else if (!CanLoadVulkanRuntime())
            {
                Skip = "Requires a loadable MoltenVK runtime (libMoltenVK.dylib) for DeltaRender Vulkan dispatch.";
            }
        }

        private static bool CanLoadVulkanRuntime()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "libMoltenVK.dylib"),
                "libMoltenVK.dylib",
                "MoltenVK"
            };

            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, out var handle))
                {
                    NativeLibrary.Free(handle);
                    return true;
                }
            }

            return false;
        }
    }
}
