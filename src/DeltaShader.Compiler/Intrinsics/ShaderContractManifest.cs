using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delta.Shader.Compiler.Intrinsics;

public sealed class ShaderContractManifest
{
    private const string LegacyDeltaMathsNamespace = "DeltaMaths";
    private const string CanonicalDeltaMathsNamespace = "Delta.Maths";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = string.Empty;

    [JsonPropertyName("types")]
    public IReadOnlyList<ShaderContractType> Types { get; set; } = Array.Empty<ShaderContractType>();

    [JsonPropertyName("functions")]
    public IReadOnlyList<ShaderContractFunction> Functions { get; set; } = Array.Empty<ShaderContractFunction>();

    public string GetClrMetadataName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("A shader-contract CLR type name is required.", nameof(typeName));
        }

        var clrNamespace = string.Equals(Namespace, LegacyDeltaMathsNamespace, StringComparison.Ordinal)
            ? CanonicalDeltaMathsNamespace
            : Namespace;
        return clrNamespace + "." + typeName;
    }

    public static ShaderContractManifest LoadEmbedded()
    {
        var assembly = typeof(ShaderContractManifest).GetTypeInfo().Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith("shader-contract.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException("The DeltaMaths shader contract was not embedded in Delta.Shader.Compiler.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded DeltaMaths shader contract could not be opened.");
        var manifest = JsonSerializer.Deserialize<ShaderContractManifest>(stream)
            ?? throw new InvalidOperationException("The embedded DeltaMaths shader contract is empty.");
        manifest.Validate();
        return manifest;
    }

    public void Validate()
    {
        foreach (var type in Types)
        {
            type.ValidateMatrixMetadata();
        }
    }
}

public sealed class ShaderContractType
{
    [JsonPropertyName("clrName")]
    public string ClrName { get; set; } = string.Empty;

    [JsonPropertyName("glslName")]
    public string? GlslName { get; set; }

    [JsonPropertyName("mapping")]
    [JsonConverter(typeof(ShaderContractMappingJsonConverter))]
    public ShaderContractMapping Mapping { get; set; } = ShaderContractMapping.Unsupported;

    [JsonPropertyName("columnMajor")]
    public bool? ColumnMajor { get; set; }

    [JsonPropertyName("alignment")]
    public uint? Alignment { get; set; }

    [JsonPropertyName("matrixStride")]
    public uint? MatrixStride { get; set; }

    [JsonPropertyName("matrixColumns")]
    public uint? MatrixColumns { get; set; }

    [JsonPropertyName("matrixRows")]
    public uint? MatrixRows { get; set; }

    [JsonPropertyName("elementGlslType")]
    public string? ElementGlslType { get; set; }

    [JsonPropertyName("size")]
    public uint? Size { get; set; }

    [JsonPropertyName("requiredCapability")]
    public string? RequiredCapability { get; set; }

    public void ValidateMatrixMetadata()
    {
        if (Mapping == ShaderContractMapping.Unsupported ||
            GlslName is not { Length: > 0 } glslName ||
            !glslName.StartsWith("mat", StringComparison.Ordinal))
        {
            return;
        }

        if (!TryGetMatrixDimensions(glslName, out var columns, out var rows))
        {
            throw new InvalidOperationException($"Unsupported matrix GLSL type '{glslName}' in the shader contract.");
        }

        var matrixStride = rows == 2 ? 8u : 16u;
        var size = columns * matrixStride;
        if (ColumnMajor != true || Alignment != matrixStride || MatrixStride != matrixStride ||
            MatrixColumns != columns || MatrixRows != rows ||
            !string.Equals(ElementGlslType, "float", StringComparison.Ordinal) || Size != size)
        {
            throw new InvalidOperationException($"Invalid std430 matrix metadata for '{ClrName}' ({glslName}).");
        }
    }

    private static bool TryGetMatrixDimensions(string glslName, out uint columns, out uint rows)
    {
        columns = 0;
        rows = 0;
        if (glslName.Length == 4 && glslName[3] is >= '2' and <= '4')
        {
            columns = (uint)(glslName[3] - '0');
            rows = columns;
            return true;
        }

        if (glslName.Length == 6 && glslName[3] is >= '2' and <= '4' &&
            glslName[4] == 'x' && glslName[5] is >= '2' and <= '4')
        {
            columns = (uint)(glslName[3] - '0');
            rows = (uint)(glslName[5] - '0');
            return true;
        }

        return false;
    }

}

public sealed class ShaderContractFunction
{
    [JsonPropertyName("typeClrName")]
    public string TypeClrName { get; set; } = string.Empty;

    [JsonPropertyName("clrName")]
    public string ClrName { get; set; } = string.Empty;

    [JsonPropertyName("mathsName")]
    public string DeltaMathsName { get; set; } = string.Empty;

    [JsonPropertyName("parameterClrNames")]
    public IReadOnlyList<string> ParameterClrNames { get; set; } = Array.Empty<string>();

    [JsonPropertyName("parameterGlslTypes")]
    public IReadOnlyList<string?> ParameterGlslTypes { get; set; } = Array.Empty<string?>();

    [JsonPropertyName("returnClrName")]
    public string ReturnClrName { get; set; } = string.Empty;

    [JsonPropertyName("returnGlslType")]
    public string? ReturnGlslType { get; set; }

    [JsonPropertyName("glslName")]
    public string? GlslName { get; set; }

    [JsonPropertyName("mapping")]
    [JsonConverter(typeof(ShaderContractMappingJsonConverter))]
    public ShaderContractMapping Mapping { get; set; } = ShaderContractMapping.Unsupported;

    [JsonPropertyName("requiredCapability")]
    public string? RequiredCapability { get; set; }

    [JsonPropertyName("stages")]
    public IReadOnlyList<string> Stages { get; set; } = Array.Empty<string>();

    [JsonPropertyName("shaderZone")]
    public string? ShaderZone { get; set; }
}
