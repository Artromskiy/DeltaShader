using System;
using Delta.Shader;
using Delta.Shader.Compiler;

namespace Delta.Shader.Analyzers;

internal static partial class ArtifactSourceEmitter
{
    public static string EmitAbiFactory(
        ShaderCompilationManifest manifest,
        string factoryName = "CreateAbi")
    {
        var workgroupSize = manifest.Stage == ShaderStage.Compute ? Workgroup(manifest) : "default";
        return $$"""
            private static Delta.Shader.Contract.ShaderAbi {{factoryName}}()
            {
                return new Delta.Shader.Contract.ShaderAbi(
                    stage: {{Stage(manifest.Stage)}},
                    resources: {{Resources(manifest.Resources)}},
                    pushConstants: {{PushConstants(manifest.Stage, manifest.PushConstants)}},
                    inputs: {{Interfaces(manifest.Inputs)}},
                    outputs: {{Interfaces(manifest.Outputs)}},
                    vertexInputs: {{VertexInputs(manifest.VertexInputs)}},
                    vertexBuffers: {{VertexBuffers(manifest.VertexBufferBindings)}},
                    workgroupSize: {{workgroupSize}},
                    requiredCapabilities: Delta.Shader.Contract.ShaderCapabilities.None);
            }
            """;
    }

    public static string EmitAbiAccessor(string propertyName, string factoryName)
    {
        var fieldName = "s_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
        return $$"""
                private static readonly Delta.Shader.Contract.ShaderAbi {{fieldName}} = {{factoryName}}();
                public static Delta.Shader.Contract.ShaderAbi {{propertyName}} => {{fieldName}};
            """;
    }
}
