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
        return JsonSerializer.Deserialize<ShaderContractManifest>(stream)
            ?? throw new InvalidOperationException("The embedded DeltaMaths shader contract is empty.");
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

    [JsonPropertyName("requiredCapability")]
    public string? RequiredCapability { get; set; }

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

    [JsonPropertyName("returnClrName")]
    public string ReturnClrName { get; set; } = string.Empty;

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
