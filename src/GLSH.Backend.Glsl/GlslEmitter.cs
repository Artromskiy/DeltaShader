using System.Text;
using DVG.Shaders.Compiler;
using DVG.Shaders.Compiler.IR;

namespace DVG.Shaders.Backend.Glsl;

public sealed class GlslEmitResult
{
    public string Source { get; init; } = string.Empty;
    public bool Success => !string.IsNullOrWhiteSpace(Source);
}

public static class GlslEmitter
{
    public static GlslEmitResult EmitFromModule(ShaderIrModule module)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#version 450");
        sb.AppendLine($"layout(local_size_x = {module.LocalSizeX}, local_size_y = {module.LocalSizeY}, local_size_z = {module.LocalSizeZ}) in;");
        var identifiers = new GlslIdentifierMangler("main");
        foreach (var resource in module.Resources)
        {
            var storageMode = resource.ReadOnly ? "readonly " : string.Empty;
            var glslType = string.IsNullOrWhiteSpace(resource.GlslType) ? "uint" : resource.GlslType!;
            var rawName = string.IsNullOrWhiteSpace(resource.Name) ? "resource" : resource.Name;
            var resourceName = identifiers.Mangle(rawName, "resource");
            var dataMemberName = identifiers.Mangle("data");
            sb.AppendLine($"layout(set = {resource.Set}, binding = {resource.Binding}, {ShaderStd430Layout.Standard}) {storageMode}buffer {resourceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    {glslType} {dataMemberName}[];");
            sb.AppendLine("};");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("void main()");
        sb.AppendLine("{");
        sb.AppendLine("    // GLSH auto-generated stage stub");
        sb.AppendLine("}");

        return new GlslEmitResult { Source = sb.ToString() };
    }

}
