using System.Globalization;
using System.Reflection;

namespace Delta.Shader.Abstractions;

public static class ShaderStd430Packer
{
    public static byte[] Pack<T>(IReadOnlyList<T> values, ShaderAbiResource resource)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        if (resource is null)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        if (resource.Packing.Scheme != "std430" || resource.Packing.Strategy != "std430-explicit-members")
        {
            throw new NotSupportedException("Only explicit std430 member packing is supported.");
        }

        if (resource.Packing.DirectRawUploadAllowed)
        {
            throw new InvalidOperationException("The shader ABI does not permit raw CLR uploads.");
        }

        if (resource.Packing.Stride != resource.ArrayStride || resource.ArrayStride == 0)
        {
            throw new InvalidOperationException("The shader packing plan stride does not match the resource ArrayStride.");
        }

        if (resource.Members.Count == 0)
        {
            throw new InvalidOperationException("A structured std430 resource requires explicit member metadata.");
        }

        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("Vulkan host packing currently requires little-endian byte order.");
        }

        var bytes = new byte[checked(values.Count * (int)resource.ArrayStride)];
        for (var index = 0; index < values.Count; index++)
        {
            PackMembers(values[index]!, resource.Members, bytes, checked((uint)(index * resource.ArrayStride)));
        }

        return bytes;
    }

    private static void PackMembers(
        object value,
        IReadOnlyList<ShaderAbiMember> members,
        byte[] destination,
        uint baseOffset)
    {
        foreach (var member in members)
        {
            var memberValue = ReadMember(value, member.Name);
            var offset = checked(baseOffset + member.Offset);
            if (member.Members.Count > 0)
            {
                PackMembers(memberValue, member.Members, destination, offset);
                continue;
            }

            PackValue(memberValue, member, destination, offset);
        }
    }

    private static void PackValue(object value, ShaderAbiMember member, byte[] destination, uint offset)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"Shader member '{member.Name}' cannot be null.");
        }

        if (member.GlslType is "float" or "int" or "uint" or "bool")
        {
            WriteScalar(value, member.GlslType, destination, offset);
            return;
        }

        if (TryGetVectorSize(member.GlslType, out var vectorSize))
        {
            var componentType = member.GlslType[0] switch
            {
                'i' => "int",
                'u' => "uint",
                'b' => "bool",
                _ => "float"
            };

            for (var component = 0; component < vectorSize; component++)
            {
                WriteScalar(ReadComponent(value, component), componentType, destination, checked(offset + (uint)(component * 4)));
            }

            return;
        }

        if (TryGetMatrixShape(member.GlslType, out var columns, out var rows))
        {
            var stride = member.MatrixStride ?? checked((uint)(rows * 4));
            for (var column = 0; column < columns; column++)
            {
                var columnValue = ReadNamedMember(value, "c" + column.ToString(CultureInfo.InvariantCulture));
                for (var row = 0; row < rows; row++)
                {
                    WriteScalar(ReadComponent(columnValue, row), "float", destination,
                        checked(offset + (uint)(column * stride) + (uint)(row * 4)));
                }
            }

            return;
        }

        throw new NotSupportedException($"No std430 packer is registered for GLSL type '{member.GlslType}'.");
    }

    private static void WriteScalar(object value, string glslType, byte[] destination, uint offset)
    {
        if (glslType == "float")
        {
            Buffer.BlockCopy(BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture)), 0, destination, (int)offset, 4);
            return;
        }

        var bits = glslType switch
        {
            "int" => unchecked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            "uint" => Convert.ToUInt32(value, CultureInfo.InvariantCulture),
            "bool" => Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? 1u : 0u,
            _ => throw new NotSupportedException($"Unsupported scalar GLSL type '{glslType}'.")
        };

        destination[offset] = (byte)bits;
        destination[offset + 1] = (byte)(bits >> 8);
        destination[offset + 2] = (byte)(bits >> 16);
        destination[offset + 3] = (byte)(bits >> 24);
    }

    private static object ReadMember(object value, string name)
        => ReadNamedMember(value, name);

    private static object ReadNamedMember(object value, string name)
    {
        var type = value.GetType();
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
        if (field is not null)
        {
            return field.GetValue(value)!;
        }

        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property?.GetMethod is not null)
        {
            return property.GetValue(value)!;
        }

        throw new InvalidOperationException($"Host value type '{type}' has no public member '{name}'.");
    }

    private static object ReadComponent(object value, int index)
    {
        var name = index switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "w",
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        try
        {
            return ReadNamedMember(value, name);
        }
        catch (InvalidOperationException)
        {
            return ReadNamedMember(value, name.ToUpperInvariant());
        }
    }

    private static bool TryGetVectorSize(string glslType, out int size)
    {
        size = 0;
        if (glslType is not ("vec2" or "vec3" or "vec4" or "ivec2" or "ivec3" or "ivec4" or
            "uvec2" or "uvec3" or "uvec4" or "bvec2" or "bvec3" or "bvec4"))
        {
            return false;
        }

        size = glslType[glslType.Length - 1] - '0';
        return true;
    }

    private static bool TryGetMatrixShape(string glslType, out int columns, out int rows)
    {
        columns = rows = 0;
        if (glslType is not ("mat2" or "mat3" or "mat4"))
        {
            return false;
        }

        columns = rows = glslType[glslType.Length - 1] - '0';
        return true;
    }
}
