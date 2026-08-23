using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delta.Shader.Compiler.Intrinsics;

public enum ShaderContractMapping
{
    None = 0,
    Unknown = 1,
    Unsupported = 2,
    Builtin = 3,
    Helper = 4
}

public sealed class ShaderContractMappingJsonConverter : JsonConverter<ShaderContractMapping>
{
    public override ShaderContractMapping Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            return ShaderContractMapping.Unknown;
        }

        return reader.GetString() switch
        {
            "None" => ShaderContractMapping.None,
            "Unsupported" => ShaderContractMapping.Unsupported,
            "Builtin" => ShaderContractMapping.Builtin,
            "Helper" => ShaderContractMapping.Helper,
            _ => ShaderContractMapping.Unknown
        };
    }

    public override void Write(Utf8JsonWriter writer, ShaderContractMapping value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ShaderContractMapping.None => "None",
            ShaderContractMapping.Unsupported => "Unsupported",
            ShaderContractMapping.Builtin => "Builtin",
            ShaderContractMapping.Helper => "Helper",
            _ => "Unknown"
        });
    }
}
