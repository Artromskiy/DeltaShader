using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Delta.Shader;
using Delta.Shader.Compiler;

namespace Delta.Shader.Analyzers;

internal static class ArtifactSourceEmitter
{
    public static string EmitAbiFactory(ShaderCompilationManifest manifest)
    {
        var source = new StringBuilder();
        source.AppendLine("    private static Delta.Shader.Contract.ShaderAbi CreateAbi()");
        source.AppendLine("    {");
        source.AppendLine("        return new Delta.Shader.Contract.ShaderAbi(");
        source.AppendLine($"            stage: {Stage(manifest.Stage)},");
        source.AppendLine($"            resources: {Resources(manifest.Resources)},");
        source.AppendLine($"            pushConstants: {PushConstants(manifest.Stage, manifest.PushConstants)},");
        source.AppendLine($"            inputs: {Interfaces(manifest.Inputs)},");
        source.AppendLine($"            outputs: {Interfaces(manifest.Outputs)},");
        source.AppendLine($"            vertexInputs: {VertexInputs(manifest.VertexInputs)},");
        source.AppendLine($"            vertexBuffers: {VertexBuffers(manifest.VertexBufferBindings)},");
        source.AppendLine("            workgroupSize: ");
        source.AppendLine($"                {(manifest.Stage == ShaderStage.Compute ? Workgroup(manifest) : "default")},");
        source.AppendLine("            requiredCapabilities: Delta.Shader.Contract.ShaderCapabilities.None);");
        source.AppendLine("    }");
        return source.ToString();
    }

    private static string Resources(IReadOnlyList<ShaderCompilationResource> resources)
        => ArrayExpression(resources, "Delta.Shader.Contract.ShaderResourceBinding", RenderResource);

    private static string PushConstants(ShaderStage stage, IReadOnlyList<ShaderCompilationPushConstant> pushConstants)
        => ArrayExpression(pushConstants, "Delta.Shader.Contract.ShaderPushConstantRange", push => RenderPushConstant(stage, push));

    private static string Interfaces(IReadOnlyList<ShaderCompilationInterfaceVariable> variables)
        => ArrayExpression(variables, "Delta.Shader.Contract.ShaderInterfaceVariable", RenderInterface);

    private static string VertexInputs(IReadOnlyList<ShaderCompilationVertexInput> inputs)
        => ArrayExpression(inputs, "Delta.Shader.Contract.ShaderVertexInput", RenderVertexInput);

    private static string VertexBuffers(IReadOnlyList<ShaderCompilationVertexBufferBinding> buffers)
        => ArrayExpression(buffers, "Delta.Shader.Contract.ShaderVertexBufferLayout", RenderVertexBuffer);

    private static string Workgroup(ShaderCompilationManifest manifest)
        => $"new Delta.Shader.Contract.ShaderWorkgroupSize({manifest.LocalSizeX}u, {manifest.LocalSizeY}u, {manifest.LocalSizeZ}u)";

    private static string RenderResource(ShaderCompilationResource resource)
    {
        var kind = resource.Category switch
        {
            "storage-buffer" => "StorageBuffer",
            "sampled-texture" or "sampled-texture-2d" => "SampledTexture",
            "combined-texture-sampler" => "CombinedTextureSampler",
            _ => "Unknown"
        };
        var layout = kind is "SampledTexture" or "CombinedTextureSampler"
            ? "Delta.Shader.Contract.ShaderAbiLayout.Empty"
            : RenderLayout(resource.Size, resource.Alignment, resource.ArrayStride, resource.MatrixStride ?? 0u, resource.Members);
        var access = resource.Access == 0
            ? (resource.ReadOnly ? "Read" : "ReadWrite")
            : resource.Access.ToString();
        return $"new Delta.Shader.Contract.ShaderResourceBinding(new Delta.Shader.Contract.ShaderBinding({resource.Set}u, {resource.Binding}u), Delta.Shader.Contract.ShaderResourceKind.{kind}, Delta.Shader.Contract.ShaderResourceAccess.{access}, {StageMask(resource.Stage)}, layout: {layout}, descriptorCount: 1u)";
    }

    private static string RenderPushConstant(ShaderStage stage, ShaderCompilationPushConstant pushConstant)
        => $"new Delta.Shader.Contract.ShaderPushConstantRange(0u, {pushConstant.Size}u, {StageMask(stage)}, {RenderLayout(pushConstant.Size, pushConstant.Alignment, pushConstant.ArrayStride, 0u, pushConstant.Members)})";

    private static string RenderInterface(ShaderCompilationInterfaceVariable variable)
    {
        var location = IsBuiltin(variable.Builtin) ? "null" : variable.Location.ToString(CultureInfo.InvariantCulture) + "u";
        return $"new Delta.Shader.Contract.ShaderInterfaceVariable({ValueType(variable.GlslType)}, Location: {location}, Builtin: {Builtin(variable.Builtin)})";
    }

    private static string RenderVertexInput(ShaderCompilationVertexInput input)
        => $"new Delta.Shader.Contract.ShaderVertexInput({input.Location}u, {input.Binding}u, {input.ByteOffset}u, {ValueType(input.GlslType)}, Delta.Shader.Contract.ShaderVertexInputRate.{input.InputRate})";

    private static string RenderVertexBuffer(ShaderCompilationVertexBufferBinding buffer)
        => $"new Delta.Shader.Contract.ShaderVertexBufferLayout({buffer.Binding}u, {buffer.Stride}u, Delta.Shader.Contract.ShaderVertexInputRate.{buffer.InputRate})";

    private static string RenderLayout(uint size, uint alignment, uint arrayStride, uint matrixStride, IReadOnlyList<ShaderCompilationMember> members)
        => $"new Delta.Shader.Contract.ShaderAbiLayout({size}u, {alignment}u, arrayStride: {arrayStride}u, matrixStride: {matrixStride}u, members: {ArrayExpression(members, "Delta.Shader.Contract.ShaderAbiMember", RenderMember)})";

    private static string RenderMember(ShaderCompilationMember member)
    {
        var nested = IsStructure(member.GlslType)
            ? RenderLayout(member.Size, member.Alignment, member.ArrayStride, member.MatrixStride ?? 0u, member.Members)
            : "null";
        return $"new Delta.Shader.Contract.ShaderAbiMember({ValueType(member.GlslType)}, {member.Offset}u, {member.Size}u, {member.Alignment}u, arrayStride: {member.ArrayStride}u, matrixStride: {member.MatrixStride ?? 0u}u, nestedLayout: {nested})";
    }

    private static string ArrayExpression<T>(IEnumerable<T>? values, string typeName, Func<T, string> render)
    {
        var items = values?.ToArray() ?? Array.Empty<T>();
        if (items.Length == 0)
        {
            return $"Array.Empty<{typeName}>()";
        }

        var rendered = items.Select(value => "                " + render(value));
        return $"new {typeName}[]\n            {{\n{string.Join(",\n", rendered)}\n            }}";
    }

    private static string ValueType(string? glslType)
    {
        if (IsStructure(glslType))
        {
            return "Delta.Shader.Contract.ShaderValueType.Structure";
        }

        var type = glslType ?? string.Empty;
        var (kind, bits, vectorSize, columns) = type switch
        {
            "bool" => ("Boolean", 32u, 1u, 1u),
            "int" => ("SignedInteger", 32u, 1u, 1u),
            "uint" => ("UnsignedInteger", 32u, 1u, 1u),
            "float" => ("FloatingPoint", 32u, 1u, 1u),
            "double" => ("FloatingPoint", 64u, 1u, 1u),
            "vec2" or "vec3" or "vec4" => ("FloatingPoint", 32u, VectorSize(type), 1u),
            "ivec2" or "ivec3" or "ivec4" => ("SignedInteger", 32u, VectorSize(type), 1u),
            "uvec2" or "uvec3" or "uvec4" => ("UnsignedInteger", 32u, VectorSize(type), 1u),
            "bvec2" or "bvec3" or "bvec4" => ("Boolean", 32u, VectorSize(type), 1u),
            "dvec2" or "dvec3" or "dvec4" => ("FloatingPoint", 64u, VectorSize(type), 1u),
            "mat2" or "mat3" or "mat4" => ("FloatingPoint", 32u, VectorSize(type), VectorSize(type)),
            _ => ("Unknown", 0u, 0u, 0u)
        };
        return $"new Delta.Shader.Contract.ShaderValueType(Delta.Shader.Contract.ShaderValueKind.{kind}, {bits}u, {vectorSize}u, {columns}u)";
    }

    private static uint VectorSize(string type) => (uint)(type[type.Length - 1] - '0');

    private static string Stage(ShaderStage stage) => $"Delta.Shader.Contract.ShaderStage.{stage}";

    private static string StageMask(ShaderStage stage) => stage switch
    {
        ShaderStage.Compute => "Delta.Shader.Contract.ShaderStageMask.Compute",
        ShaderStage.Vertex => "Delta.Shader.Contract.ShaderStageMask.Vertex",
        ShaderStage.Fragment => "Delta.Shader.Contract.ShaderStageMask.Fragment",
        _ => "Delta.Shader.Contract.ShaderStageMask.None"
    };

    private static string Builtin(string? builtin) => builtin switch
    {
        "FragmentCoord" => "Delta.Shader.Contract.ShaderBuiltin.FragmentCoordinate",
        "Position" => "Delta.Shader.Contract.ShaderBuiltin.Position",
        "VertexIndex" => "Delta.Shader.Contract.ShaderBuiltin.VertexIndex",
        "InstanceIndex" => "Delta.Shader.Contract.ShaderBuiltin.InstanceIndex",
        "FragmentColor" => "Delta.Shader.Contract.ShaderBuiltin.None",
        null or "" => "Delta.Shader.Contract.ShaderBuiltin.None",
        _ => "Delta.Shader.Contract.ShaderBuiltin.Unknown"
    };

    private static bool IsBuiltin(string? builtin) => !string.IsNullOrWhiteSpace(builtin) && builtin != "FragmentColor";

    private static bool IsStructure(string? glslType)
        => glslType is not null && glslType.Length > 0 && glslType.StartsWith("DeltaStruct_", StringComparison.Ordinal);
}
