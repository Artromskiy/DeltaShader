using System.Text.Json;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public sealed class ShadertoyGalleryTests
{
    private static readonly JsonSerializerOptions CatalogJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    [Trait("Area", "ShaderToyGallery")]
    public async Task InternalGallery_HasFiftyTraceableFixtures_AndCompilesToVulkanGlsl()
    {
        var shaderRoot = FindShaderRoot();
        var catalogPath = Path.Combine(shaderRoot, "src", "DeltaShader.ShadertoyGallery", "gallery", "catalog.json");
        var catalogJson = await File.ReadAllTextAsync(catalogPath).ConfigureAwait(true);
        var catalog = JsonSerializer.Deserialize<GalleryCatalog>(catalogJson, CatalogJsonOptions);

        Assert.NotNull(catalog);
        Assert.Equal(50, catalog!.Entries.Count);
        Assert.Equal(catalog.Entries.Count, catalog.Entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(catalog.Entries.Count, catalog.Entries.Select(entry => entry.SourceIdentifier).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(catalog.Entries.Count, catalog.Entries.Select(entry => entry.EntryPoint).Distinct(StringComparer.Ordinal).Count());

        var galleryRoot = Path.Combine(shaderRoot, "src", "DeltaShader.ShadertoyGallery");
        foreach (var entry in catalog.Entries)
        {
            Assert.Equal("compiled", entry.Status);
            Assert.Equal("independent-recreation", entry.Implementation);
            Assert.False(string.IsNullOrWhiteSpace(entry.SourceAuthor));
            Assert.False(string.IsNullOrWhiteSpace(entry.SourceDate));
            Assert.False(string.IsNullOrWhiteSpace(entry.LicenseStatus));
            Assert.StartsWith("https://www.shadertoy.com/view/", entry.SourceUrl, StringComparison.Ordinal);
            Assert.Equal(entry.SourceIdentifier, entry.SourceUrl["https://www.shadertoy.com/view/".Length..]);

            var fixturePath = Path.GetFullPath(Path.Combine(galleryRoot, entry.File.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(fixturePath.StartsWith(galleryRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal), fixturePath);
            Assert.True(File.Exists(fixturePath), fixturePath);
        }

        using var workspace = CreateWorkspace();
        var projectPath = Path.Combine(galleryRoot, "DeltaShader.ShadertoyGallery.csproj");
        Project project = await workspace.OpenProjectAsync(projectPath).ConfigureAwait(true);
        Compilation? compilation = await project.GetCompilationAsync().ConfigureAwait(true);
        Assert.NotNull(compilation);

        var roslynErrors = compilation!.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(roslynErrors);

        var results = ShaderCompiler.CompileAll(compilation);
        Assert.Equal(catalog.Entries.Count, results.Count);
        foreach (var result in results)
        {
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
            Assert.NotNull(result.Module);
            var glsl = GlslEmitter.EmitFromModule(result.Module!).Source;

            Assert.Contains("#version 460", glsl, StringComparison.Ordinal);
            Assert.Contains("layout(push_constant, std430) uniform DeltaPushConstants", glsl, StringComparison.Ordinal);
            Assert.Contains("layout(location = 0) out vec4 fragColor;", glsl, StringComparison.Ordinal);
            Assert.Contains("void main()", glsl, StringComparison.Ordinal);
            Assert.DoesNotContain("maths.", glsl, StringComparison.Ordinal);
            Assert.DoesNotContain("iResolution", glsl, StringComparison.Ordinal);
            Assert.DoesNotContain("iTime", glsl, StringComparison.Ordinal);
            Assert.DoesNotContain("iChannel", glsl, StringComparison.Ordinal);
            Assert.DoesNotContain("mainImage", glsl, StringComparison.Ordinal);
        }
    }

    private static MSBuildWorkspace CreateWorkspace()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        return MSBuildWorkspace.Create();
    }

    private static string FindShaderRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DeltaShader.slnx")) &&
                Directory.Exists(Path.Combine(current.FullName, "src", "DeltaShader.ShadertoyGallery")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the DeltaShader Shadertoy gallery project.");
    }

#pragma warning disable CA1812 // System.Text.Json creates the catalog DTOs through reflection.
    private sealed class GalleryCatalog
    {
        public List<GalleryEntry> Entries { get; set; } = [];
    }

    private sealed class GalleryEntry
    {
        public string Id { get; set; } = string.Empty;
        public string EntryPoint { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string SourceIdentifier { get; set; } = string.Empty;
        public string SourceAuthor { get; set; } = string.Empty;
        public string SourceDate { get; set; } = string.Empty;
        public string LicenseStatus { get; set; } = string.Empty;
        public string Implementation { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
    }
#pragma warning restore CA1812
}
