using Final = Delta.Shader.Contract;

namespace Delta.Shader.Tool;

internal sealed record ContractFunction(
    string Identity,
    string OwnerType,
    string MethodName,
    string[] ParameterTypes,
    string[] ParameterModifiers,
    string ReturnType,
    string Mapping,
    string GlslName);

internal sealed record ConformanceValue(string Type, string[] Words);

internal sealed record ComparisonProfile(
    string Name,
    double AbsoluteTolerance,
    double RelativeTolerance,
    int MaxUlps);

internal sealed record ConformanceCase(
    string Id,
    ContractFunction Operation,
    IReadOnlyList<ConformanceValue> Inputs,
    ConformanceValue Expected,
    ComparisonProfile Comparison,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> Stages,
    string CpuDisposition,
    string ShaderDisposition,
    string RenderDisposition);

internal sealed record ConformanceCoverage(
    int ManifestFunctionCount,
    int SupportedCount,
    int CaseCount,
    int ExcludedCount,
    int UnsupportedManifestCount);

internal sealed record ConformanceBundle(
    ConformanceCase[] Cases,
    ConformanceCoverage Coverage);

internal sealed class ConformanceIndex
{
    public int SchemaVersion { get; init; } = 1;
    public string ContractPath { get; init; } = string.Empty;
    public string BundlePath { get; init; } = string.Empty;
    public string FixtureSourcePath { get; init; } = string.Empty;
    public int SelectedCount { get; init; }
    public int BundleCaseCount { get; init; }
    public int ManifestFunctionCount { get; init; }
    public int SupportedCaseCount { get; init; }
    public int UnsupportedManifestCount { get; init; }
    public int ExcludedCaseCount { get; init; }
    public int ArtifactCount { get; init; }
    public int CompilerBlockedCount { get; init; }
    public int CapabilityBlockedCount { get; init; }
    public int BackendBlockedCount { get; init; }
    public int ExternalValidationBlockedCount { get; init; }
    public int MismatchedCount { get; init; }
    public int AccountedCount { get; init; }
    public IReadOnlyList<PublishedCase> Cases { get; init; } = Array.Empty<PublishedCase>();
}

internal sealed class ResolvedAbiDocument
{
    public string CaseId { get; init; } = string.Empty;
    public string OperationIdentity { get; init; } = string.Empty;
    public string EntryPointName { get; init; } = string.Empty;
    public Final.ShaderStage Stage { get; init; }
    public string ArtifactPath { get; init; } = string.Empty;
    public required ResolvedShaderAbi Abi { get; init; }
}

internal sealed class ResolvedShaderAbi
{
    public Final.ShaderStage Stage { get; init; }
    public IReadOnlyList<ResolvedResourceBinding> Resources { get; init; } = Array.Empty<ResolvedResourceBinding>();
    public IReadOnlyList<ResolvedPushConstantRange> PushConstants { get; init; } = Array.Empty<ResolvedPushConstantRange>();
    public IReadOnlyList<Final.ShaderInterfaceVariable> Inputs { get; init; } = Array.Empty<Final.ShaderInterfaceVariable>();
    public IReadOnlyList<Final.ShaderInterfaceVariable> Outputs { get; init; } = Array.Empty<Final.ShaderInterfaceVariable>();
    public IReadOnlyList<Final.ShaderVertexInput> VertexInputs { get; init; } = Array.Empty<Final.ShaderVertexInput>();
    public IReadOnlyList<Final.ShaderVertexBufferLayout> VertexBuffers { get; init; } = Array.Empty<Final.ShaderVertexBufferLayout>();
    public IReadOnlyList<ResolvedSpecializationConstant> SpecializationConstants { get; init; } = Array.Empty<ResolvedSpecializationConstant>();
    public Final.ShaderWorkgroupSize WorkgroupSize { get; init; }
    public Final.ShaderCapabilities RequiredCapabilities { get; init; }

    public static ResolvedShaderAbi From(Final.ShaderAbi abi)
        => new()
        {
            Stage = abi.Stage,
            Resources = abi.Resources.Select(ResolvedResourceBinding.From).ToArray(),
            PushConstants = abi.PushConstants.Select(ResolvedPushConstantRange.From).ToArray(),
            Inputs = abi.Inputs,
            Outputs = abi.Outputs,
            VertexInputs = abi.VertexInputs,
            VertexBuffers = abi.VertexBuffers,
            SpecializationConstants = abi.SpecializationConstants
                .Select(ResolvedSpecializationConstant.From)
                .ToArray(),
            WorkgroupSize = abi.WorkgroupSize,
            RequiredCapabilities = abi.RequiredCapabilities
        };
}

internal sealed class ResolvedResourceBinding
{
    public Final.ShaderBinding Binding { get; init; }
    public Final.ShaderResourceKind Kind { get; init; }
    public Final.ShaderResourceAccess Access { get; init; }
    public Final.ShaderStageMask Stages { get; init; }
    public Final.ShaderAbiLayout Layout { get; init; } = Final.ShaderAbiLayout.Empty;
    public uint DescriptorCount { get; init; }

    public static ResolvedResourceBinding From(Final.ShaderResourceBinding resource)
        => new()
        {
            Binding = resource.Binding,
            Kind = resource.Kind,
            Access = resource.Access,
            Stages = resource.Stages,
            Layout = resource.Layout,
            DescriptorCount = resource.DescriptorCount
        };
}

internal sealed class ResolvedPushConstantRange
{
    public uint Offset { get; init; }
    public uint Size { get; init; }
    public Final.ShaderStageMask Stages { get; init; }
    public Final.ShaderAbiLayout Layout { get; init; } = Final.ShaderAbiLayout.Empty;

    public static ResolvedPushConstantRange From(Final.ShaderPushConstantRange pushConstant)
        => new()
        {
            Offset = pushConstant.Offset,
            Size = pushConstant.Size,
            Stages = pushConstant.Stages,
            Layout = pushConstant.Layout
        };
}

internal sealed class ResolvedSpecializationConstant
{
    public uint Id { get; init; }
    public Final.ShaderValueType Type { get; init; }
    public byte[] DefaultValue { get; init; } = Array.Empty<byte>();

    public static ResolvedSpecializationConstant From(Final.ShaderSpecializationConstant constant)
        => new()
        {
            Id = constant.Id,
            Type = constant.Type,
            DefaultValue = constant.DefaultValue.ToArray()
        };
}

internal sealed class PublishedCase
{
    public string CaseId { get; init; } = string.Empty;
    public string SourceCaseId { get; init; } = string.Empty;
    public string OperationIdentity { get; init; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ArtifactPath { get; set; }
    public string? AbiPath { get; set; }
    public string? Diagnostic { get; set; }
    public IReadOnlyList<ConformanceValue> Inputs { get; init; } = Array.Empty<ConformanceValue>();
    public ConformanceValue Expected { get; init; } = new(string.Empty, Array.Empty<string>());
    public ComparisonProfile Comparison { get; init; } = new(string.Empty, 0, 0, 0);
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Stages { get; init; } = Array.Empty<string>();
    public string CpuDisposition { get; init; } = string.Empty;
    public string ShaderDisposition { get; init; } = string.Empty;
    public string RenderDisposition { get; init; } = string.Empty;

    public static PublishedCase FromCase(ConformanceCase conformanceCase, string caseId)
        => new()
        {
            CaseId = caseId,
            SourceCaseId = conformanceCase.Id,
            OperationIdentity = conformanceCase.Operation.Identity,
            Inputs = conformanceCase.Inputs,
            Expected = conformanceCase.Expected,
            Comparison = conformanceCase.Comparison,
            RequiredCapabilities = conformanceCase.RequiredCapabilities,
            Stages = conformanceCase.Stages,
            CpuDisposition = conformanceCase.CpuDisposition,
            ShaderDisposition = conformanceCase.ShaderDisposition,
            RenderDisposition = conformanceCase.RenderDisposition
        };
}
