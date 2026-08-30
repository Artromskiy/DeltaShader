using System;
using System.Collections.Generic;
using System.Text;

namespace Delta.Shader.Backend.Glsl;

/// <summary>
/// Produces deterministic identifiers accepted by Vulkan GLSL 4.60.
/// A single scope must be shared by all names emitted into one module.
/// </summary>
public sealed class GlslIdentifierMangler
{
    private readonly HashSet<string> _usedNames = new(StringComparer.Ordinal);

    public GlslIdentifierMangler(params string[] generatedNames)
    {
        if (generatedNames is null)
        {
            throw new ArgumentNullException(nameof(generatedNames));
        }

        foreach (var name in generatedNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _usedNames.Add(name);
            }
        }
    }

    public string Mangle(string? rawName, string fallback = "identifier")
    {
        if (fallback is null)
        {
            throw new ArgumentNullException(nameof(fallback));
        }

        var candidate = NormalizeAscii(rawName, fallback);
        if (IsReserved(candidate))
        {
            candidate = "_" + candidate;
        }

        var baseName = candidate;
        var suffix = 0;
        while (IsReserved(candidate) || !_usedNames.Add(candidate))
        {
            candidate = baseName + "_" + suffix++;
        }

        return candidate;
    }

    private static string NormalizeAscii(string? rawName, string fallback)
    {
        var value = rawName is not null && !string.IsNullOrWhiteSpace(rawName)
            ? rawName
            : fallback;
        var result = new StringBuilder(value.Length + 1);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var valid = (character >= 'a' && character <= 'z') ||
                        (character >= 'A' && character <= 'Z') ||
                        character == '_' ||
                        (index > 0 && character >= '0' && character <= '9');
            result.Append(valid ? character : '_');
        }

        if (result.Length == 0)
        {
            result.Append(fallback);
        }

        if (result[0] >= '0' && result[0] <= '9')
        {
            result.Insert(0, '_');
        }

        return result.ToString();
    }

    private static bool IsReserved(string value)
    {
        return value.StartsWith("gl_", StringComparison.Ordinal) || ReservedWords.Contains(value);
    }

    private static readonly HashSet<string> ReservedWords = new(
        [
            // GLSL keywords and control flow.
            "attribute", "const", "uniform", "varying", "layout", "centroid", "flat", "smooth",
            "noperspective", "patch", "sample", "invariant", "highp", "mediump", "lowp", "precision",
            "in", "out", "inout", "subroutine", "discard", "return", "break", "continue", "do", "for",
            "while", "switch", "case", "default", "if", "else", "struct", "void", "true", "false",

            // Scalar, vector, matrix and sampler/image types.
            "bool", "int", "uint", "float", "double", "float16_t", "bvec2", "bvec3", "bvec4", "ivec2", "ivec3",
            "ivec4", "uvec2", "uvec3", "uvec4", "vec2", "vec3", "vec4", "dvec2", "dvec3", "dvec4",
            "mat2", "mat3", "mat4", "mat2x2", "mat2x3", "mat2x4", "mat3x2", "mat3x3", "mat3x4",
            "mat4x2", "mat4x3", "mat4x4", "atomic_uint", "sampler", "sampler1D", "sampler2D", "sampler3D",
            "samplerCube", "sampler1DShadow", "sampler2DShadow", "samplerCubeShadow", "sampler1DArray",
            "sampler2DArray", "sampler1DArrayShadow", "sampler2DArrayShadow", "samplerCubeArray",
            "samplerCubeArrayShadow", "sampler2DRect", "sampler2DRectShadow", "samplerBuffer",
            "sampler2DMS", "sampler2DMSArray", "isampler1D", "isampler2D", "isampler3D", "isamplerCube",
            "isampler1DArray", "isampler2DArray", "isampler2DRect", "isamplerBuffer", "isampler2DMS",
            "isampler2DMSArray", "usampler1D", "usampler2D", "usampler3D", "usamplerCube", "usampler1DArray",
            "usampler2DArray", "usampler2DRect", "usamplerBuffer", "usampler2DMS", "usampler2DMSArray",
            "image1D", "image2D", "image3D", "imageCube", "image1DArray", "image2DArray", "imageCubeArray",
            "image2DRect", "imageBuffer", "image2DMS", "image2DMSArray", "iimage1D", "iimage2D", "iimage3D",
            "iimageCube", "iimage1DArray", "iimage2DArray", "iimageCubeArray", "iimage2DRect", "iimageBuffer",
            "iimage2DMS", "iimage2DMSArray", "uimage1D", "uimage2D", "uimage3D", "uimageCube",
            "uimage1DArray", "uimage2DArray", "uimageCubeArray", "uimage2DRect", "uimageBuffer", "uimage2DMS",
            "uimage2DMSArray", "uint64_t", "int64_t", "f16vec2", "f16vec3", "f16vec4", "f16mat2",
            "f16mat3", "f16mat4", "f16mat2x3", "f16mat2x4", "f16mat3x2", "f16mat3x4", "f16mat4x2",
            "f16mat4x3", "dmat2", "dmat3", "dmat4", "dmat2x3", "dmat2x4", "dmat3x2", "dmat3x4",
            "dmat4x2", "dmat4x3", "i64vec2", "i64vec3", "i64vec4", "u64vec2", "u64vec3", "u64vec4",

            // Reserved/future GLSL words.
            "asm", "class", "union", "enum", "typedef", "template", "this", "resource", "goto", "inline",
            "noinline", "public", "static", "extern", "external", "interface", "long", "short", "half",
            "fixed", "unsigned", "superp",

            // Vulkan GLSL interface/storage words and reserved identifiers.
            "input", "output", "common", "partition", "active", "filter", "cast", "namespace", "using",
            "buffer", "shared", "coherent", "volatile", "restrict", "readonly", "writeonly", "location",
            "component", "index", "binding", "set", "offset", "align", "std140", "std430", "push_constant",
            "subpassInput", "subpassInputMS", "subpassInputShadow", "subpassLoad", "rayQueryEXT", "accelerationStructureEXT",
            "texture", "textureProj", "textureLod", "textureGrad", "textureGather", "textureQueryLevels",
            "textureQueryLod", "textureSize", "imageLoad", "imageStore", "imageAtomicAdd", "imageAtomicMin",
            "imageAtomicMax", "imageAtomicAnd", "imageAtomicOr", "imageAtomicXor", "imageAtomicExchange",
            "imageAtomicCompSwap", "memoryBarrier", "barrier", "groupMemoryBarrier", "memoryBarrierBuffer",
            "memoryBarrierImage", "memoryBarrierShared", "main"
        ],
        StringComparer.Ordinal);
}
