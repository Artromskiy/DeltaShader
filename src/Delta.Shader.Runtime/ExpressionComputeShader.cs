using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Delta.Shader.Abstractions;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Delta.Shader.Runtime;

public sealed record ComputeExpressionBinding(
    int ParameterIndex,
    uint Set,
    uint Binding,
    ShaderResourceAccess Access);

public sealed record ComputeExpressionOptions
{
    public IReadOnlyList<ComputeExpressionBinding> Bindings { get; init; } = Array.Empty<ComputeExpressionBinding>();
    public int InvocationParameterIndex { get; init; } = -1;
    public ShaderCompilationOptions CompilationOptions { get; init; } = ShaderCompilationOptions.Default;
    public string? GlslangValidatorPath { get; init; }
    public string? SpirvValidatorPath { get; init; }
}

public sealed class ExpressionShaderCompilationResult
{
    internal ExpressionShaderCompilationResult(
        bool success,
        ShaderArtifact? artifact,
        IReadOnlyList<ShaderDiagnostic> diagnostics,
        string cacheKey,
        bool cacheHit,
        string? generatedSource)
    {
        Success = success;
        Artifact = artifact;
        Diagnostics = diagnostics;
        CacheKey = cacheKey;
        CacheHit = cacheHit;
        GeneratedSource = generatedSource;
    }

    public bool Success { get; }
    public ShaderArtifact? Artifact { get; }
    public IReadOnlyList<ShaderDiagnostic> Diagnostics { get; }
    public string CacheKey { get; }
    public bool CacheHit { get; }
    public string? GeneratedSource { get; }
}

public static class ExpressionComputeShaderCompiler
{
    private static readonly ConcurrentDictionary<string, ShaderArtifact> Cache = new(StringComparer.Ordinal);

    public static ExpressionShaderCompilationResult Compile<TDelegate>(
        Expression<TDelegate> expression,
        ComputeExpressionOptions? options = null)
        where TDelegate : Delegate
    {
        options ??= new ComputeExpressionOptions();
        var sourceResult = ExpressionSourceBuilder.Build(expression, options);
        var cacheKey = ComputeCacheKey(sourceResult.Source ?? expression.ToString(), options);
        if (sourceResult.Diagnostics.Count > 0)
        {
            return new ExpressionShaderCompilationResult(false, null, sourceResult.Diagnostics, cacheKey, false, sourceResult.Source);
        }

        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return new ExpressionShaderCompilationResult(true, cached, Array.Empty<ShaderDiagnostic>(), cacheKey, true, sourceResult.Source);
        }

        var compilation = CreateCompilation(sourceResult.Source!, expression);
        var roslynDiagnostics = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (roslynDiagnostics.Length > 0)
        {
            return Failure(
                cacheKey,
                sourceResult.Source,
                ShaderDiagnosticId.DSH014,
                string.Join(Environment.NewLine, roslynDiagnostics.Select(diagnostic => diagnostic.ToString())));
        }

        var compilerResult = ShaderCompiler.Compile(compilation, options.CompilationOptions);
        if (!compilerResult.Success || compilerResult.Module is null || compilerResult.AbiManifest is null)
        {
            return new ExpressionShaderCompilationResult(false, null, compilerResult.Diagnostics, cacheKey, false, sourceResult.Source);
        }

        var emitted = GlslEmitter.EmitFromModule(compilerResult.Module);
        if (!emitted.Success)
        {
            return Failure(cacheKey, sourceResult.Source, ShaderDiagnosticId.DSH015, "GLSL emission failed.");
        }

        var external = CompileExternal(emitted.Source, compilerResult.AbiManifest, options);
        if (!external.Success)
        {
            return Failure(cacheKey, sourceResult.Source, external.Id, external.Message);
        }

        var artifact = new ShaderArtifact(external.Spirv!, compilerResult.AbiManifest);
        Cache[cacheKey] = artifact;
        return new ExpressionShaderCompilationResult(true, artifact, Array.Empty<ShaderDiagnostic>(), cacheKey, false, sourceResult.Source);
    }

    public static Task<ExpressionShaderCompilationResult> CompileAsync<TDelegate>(
        Expression<TDelegate> expression,
        ComputeExpressionOptions? options = null)
        where TDelegate : Delegate
        => Task.FromResult(Compile(expression, options));

    public static void ClearCache() => Cache.Clear();

    private static ExpressionShaderCompilationResult Failure(
        string cacheKey,
        string? source,
        string id,
        string message)
        => new(false, null, [new ShaderDiagnostic(id, message, Severity: ShaderDiagnosticSeverity.Error)], cacheKey, false, source);

    private static CSharpCompilation CreateCompilation(string source, LambdaExpression expression)
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        AddReference(typeof(object).Assembly, references);
        AddReference(typeof(Enumerable).Assembly, references);
        AddReference(typeof(ShaderArtifact).Assembly, references);
        AddReference(typeof(ComputeShaderAttribute).Assembly, references);
        AddReference(typeof(Expression).Assembly, references);
        AddReference(expression.Type.Assembly, references);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            {
                AddReference(path, references);
            }
        }
        foreach (var parameter in expression.Parameters)
        {
            AddReference(parameter.Type.Assembly, references);
        }
        CollectReferences(expression.Body, references);

        return CSharpCompilation.Create(
            "DeltaShader.RuntimeLambda",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references.Values,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static void CollectReferences(Expression expression, Dictionary<string, MetadataReference> references)
    {
        switch (expression)
        {
            case MethodCallExpression call:
                AddReference(call.Method.DeclaringType!.Assembly, references);
                if (call.Object is not null) CollectReferences(call.Object, references);
                foreach (var argument in call.Arguments) CollectReferences(argument, references);
                break;
            case MemberExpression member:
                AddReference(member.Member.DeclaringType!.Assembly, references);
                if (member.Expression is not null) CollectReferences(member.Expression, references);
                break;
            case NewExpression @new:
                AddReference(@new.Constructor!.DeclaringType!.Assembly, references);
                foreach (var argument in @new.Arguments) CollectReferences(argument, references);
                break;
            case BinaryExpression binary:
                CollectReferences(binary.Left, references);
                CollectReferences(binary.Right, references);
                break;
            case ConditionalExpression conditional:
                CollectReferences(conditional.Test, references);
                CollectReferences(conditional.IfTrue, references);
                CollectReferences(conditional.IfFalse, references);
                break;
            case UnaryExpression unary:
                CollectReferences(unary.Operand, references);
                break;
        }
    }

    private static void AddReference(Assembly assembly, Dictionary<string, MetadataReference> references)
    {
        AddReference(assembly.Location, references);
    }

    private static void AddReference(string? path, Dictionary<string, MetadataReference> references)
    {
        if (!string.IsNullOrWhiteSpace(path) && !references.ContainsKey(path))
        {
            references[path] = MetadataReference.CreateFromFile(path);
        }
    }

    private static string ComputeCacheKey(string source, ComputeExpressionOptions options)
    {
        var identity = source + "\n" + options.CompilationOptions.Profile + "\n" + options.CompilationOptions.Glsl + "\n" + options.CompilationOptions.Spirv + "\n" + options.InvocationParameterIndex + "\n" + string.Join(";", options.Bindings.OrderBy(binding => binding.ParameterIndex).Select(binding => $"{binding.ParameterIndex}:{binding.Set}:{binding.Binding}:{binding.Access}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static (bool Success, byte[]? Spirv, string Id, string Message) CompileExternal(
        string glsl,
        ShaderAbiManifest manifest,
        ComputeExpressionOptions options)
    {
        var glslang = options.GlslangValidatorPath ?? FindTool("glslangValidator");
        var spirvVal = options.SpirvValidatorPath ?? FindTool("spirv-val");
        if (glslang is null || spirvVal is null)
        {
            return (false, null, ShaderDiagnosticId.DSH015, "glslangValidator and spirv-val are required for runtime ShaderArtifact compilation.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "delta-shader-runtime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var glslPath = Path.Combine(directory, "compute.comp.glsl");
        var spirvPath = Path.Combine(directory, "compute.spv");
        File.WriteAllText(glslPath, glsl, new UTF8Encoding(false));
        var compile = RunTool(glslang, $"-V --target-env {Quote(manifest.TargetProfile)} -S comp {Quote(glslPath)} -o {Quote(spirvPath)}");
        if (compile.ExitCode != 0)
        {
            return (false, null, ShaderDiagnosticId.DSH015, $"glslangValidator failed: {compile.Output}");
        }

        var validation = RunTool(spirvVal, $"--target-env {Quote(manifest.TargetProfile)} {Quote(spirvPath)}");
        if (validation.ExitCode != 0)
        {
            return (false, null, ShaderDiagnosticId.DSH015, $"spirv-val failed: {validation.Output}");
        }

        return (true, File.ReadAllBytes(spirvPath), string.Empty, string.Empty);
    }

    private static string? FindTool(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(part, name);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static (int ExitCode, string Output) RunTool(string fileName, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

internal sealed class ExpressionSourceResult
{
    public string? Source { get; init; }
    public IReadOnlyList<ShaderDiagnostic> Diagnostics { get; init; } = Array.Empty<ShaderDiagnostic>();
}

internal static class ExpressionSourceBuilder
{
    public static ExpressionSourceResult Build<TDelegate>(Expression<TDelegate> expression, ComputeExpressionOptions options)
        where TDelegate : Delegate
    {
        var diagnostics = new List<ShaderDiagnostic>();
        var invoke = expression.Type.GetMethod("Invoke");
        if (invoke is null || invoke.ReturnType != typeof(void))
        {
            return Invalid("Expression shader delegate must return void.");
        }

        if (expression.Body is BlockExpression)
        {
            return Invalid("Statement-bodied expression trees are unsupported; use an expression-bodied lambda.");
        }

        if (options.Bindings.Any(binding => binding.ParameterIndex < 0 || binding.ParameterIndex >= expression.Parameters.Count))
        {
            return Invalid("Expression binding parameter index is outside the lambda parameter list.");
        }

        if (options.Bindings.Select(binding => binding.ParameterIndex).Distinct().Count() != options.Bindings.Count)
        {
            return Invalid("Expression bindings must not contain duplicate parameter indices.");
        }

        var bindings = options.Bindings.ToDictionary(binding => binding.ParameterIndex);
        var invocation = options.InvocationParameterIndex;
        if (invocation < 0)
        {
            invocation = expression.Parameters.Count - 1;
        }

        var parameters = new List<string>();
        for (var index = 0; index < expression.Parameters.Count; index++)
        {
            var parameter = expression.Parameters[index];
            var type = parameter.Type;
            if (index == invocation)
            {
                if (type != typeof(uint)) return Invalid("The invocation parameter must be uint.");
                parameters.Add("[GlobalInvocationId] uint " + parameter.Name);
                continue;
            }

            if (!bindings.TryGetValue(index, out var binding))
            {
                return Invalid($"Expression parameter '{parameter.Name}' has no explicit shader resource binding.");
            }

            if (!IsStorageBuffer(type))
            {
                return Invalid($"Expression parameter '{parameter.Name}' must be a storage-buffer wrapper.");
            }

            if (binding.Access == ShaderResourceAccess.WriteOnly)
            {
                return Invalid("WriteOnly expression bindings are unsupported because the shader authoring contract has no write-only storage-buffer wrapper.");
            }

            var isReadOnly = type.GetGenericTypeDefinition() == typeof(ReadOnlyStorageBuffer<>);
            if (isReadOnly && binding.Access != ShaderResourceAccess.ReadOnly)
            {
                return Invalid($"Expression parameter '{parameter.Name}' uses a read-only storage-buffer wrapper but binding access is {binding.Access}.");
            }

            if (!isReadOnly && binding.Access != ShaderResourceAccess.ReadWrite)
            {
                return Invalid($"Expression parameter '{parameter.Name}' uses a read-write storage-buffer wrapper but binding access is {binding.Access}.");
            }

            var attribute = binding.Access == ShaderResourceAccess.ReadOnly
                ? $"[ReadOnlyStorageBuffer({binding.Set}, {binding.Binding})]"
                : binding.Access == ShaderResourceAccess.ReadWrite
                    ? $"[ReadWriteStorageBuffer({binding.Set}, {binding.Binding})]"
                    : $"[ShaderResource({binding.Set}, {binding.Binding}, ShaderResourceAccess.WriteOnly)]";
            parameters.Add(attribute + " " + TypeName(type) + " " + parameter.Name);
        }

        var emitter = new ExpressionBodyEmitter(expression.Parameters);
        var body = emitter.Emit(expression.Body);
        if (emitter.Error is not null)
        {
            return Invalid(emitter.Error);
        }

        var source = "using Delta.Shader.Abstractions;\n" +
            "public static class RuntimeExpressionShader\n{\n" +
            "    [ComputeShader(localSizeX: 64)]\n" +
            "    public static void Compute(" + string.Join(", ", parameters) + ")\n" +
            "    { " + body + "; }\n" +
            "}\n";
        return new ExpressionSourceResult { Source = source, Diagnostics = diagnostics };

        ExpressionSourceResult Invalid(string message)
            => new() { Diagnostics = [new ShaderDiagnostic(ShaderDiagnosticId.DSH014, message, Severity: ShaderDiagnosticSeverity.Error)] };
    }

    private static bool IsStorageBuffer(Type type)
        => type.IsGenericType &&
           (type.GetGenericTypeDefinition() == typeof(ReadOnlyStorageBuffer<>) || type.GetGenericTypeDefinition() == typeof(ReadWriteStorageBuffer<>));

    private static string TypeName(Type type)
    {
        if (type == typeof(uint)) return "uint";
        if (type == typeof(int)) return "int";
        if (type == typeof(float)) return "float";
        if (type == typeof(bool)) return "bool";
        if (type.IsGenericType)
        {
            var name = type.GetGenericTypeDefinition().FullName!.Split('`')[0].Replace('+', '.');
            return "global::" + name + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
        }

        if (type.IsArray || type.IsByRef || type.FullName is null)
        {
            throw new NotSupportedException($"Unsupported expression parameter type '{type}'.");
        }

        return "global::" + type.FullName.Replace('+', '.');
    }
}

internal sealed class ExpressionBodyEmitter
{
    private readonly IReadOnlyCollection<ParameterExpression> _parameters;
    public string? Error { get; private set; }

    public ExpressionBodyEmitter(IReadOnlyCollection<ParameterExpression> parameters) => _parameters = parameters;

    public string Emit(Expression expression)
    {
        if (Error is not null) return string.Empty;
        switch (expression)
        {
            case ParameterExpression parameter:
                return parameter.Name!;
            case ConstantExpression constant:
                return EmitConstant(constant);
            case MemberExpression member:
                return EmitMember(member);
            case MethodCallExpression call:
                return EmitCall(call);
            case BinaryExpression binary:
                return "(" + Emit(binary.Left) + " " + Operator(binary.NodeType) + " " + Emit(binary.Right) + ")";
            case ConditionalExpression conditional:
                return "(" + Emit(conditional.Test) + " ? " + Emit(conditional.IfTrue) + " : " + Emit(conditional.IfFalse) + ")";
            case UnaryExpression unary when unary.NodeType is ExpressionType.Negate or ExpressionType.NegateChecked or ExpressionType.Not or ExpressionType.UnaryPlus:
                return "(" + Operator(unary.NodeType) + Emit(unary.Operand) + ")";
            case NewExpression @new:
                return TypeName(@new.Type) + "(" + string.Join(", ", @new.Arguments.Select(Emit)) + ")";
            default:
                Error = $"Expression node '{expression.NodeType}' is unsupported in runtime shader lambdas.";
                return string.Empty;
        }
    }

    private string EmitMember(MemberExpression member)
    {
        if (member.Expression is null)
        {
            return "global::" + member.Member.DeclaringType!.FullName!.Replace('+', '.') + "." + member.Member.Name;
        }

        return Emit(member.Expression) + "." + (member.Member.Name == "get_Length" ? "Length" : member.Member.Name);
    }

    private string EmitCall(MethodCallExpression call)
    {
        var declaringType = call.Method.DeclaringType?.FullName ?? string.Empty;
        var allowed = declaringType.StartsWith("Delta.Shader.Abstractions.", StringComparison.Ordinal) ||
            declaringType.StartsWith("Delta.Maths.", StringComparison.Ordinal);
        if (!allowed)
        {
            Error = $"Method call '{call.Method}' is not a supported shader intrinsic or storage-buffer operation.";
            return string.Empty;
        }

        var target = call.Object is null
            ? "global::" + declaringType.Replace('+', '.') + "."
            : Emit(call.Object) + ".";
        return target + call.Method.Name + "(" + string.Join(", ", call.Arguments.Select(Emit)) + ")";
    }

    private string EmitConstant(ConstantExpression constant)
    {
        if (constant.Value is uint value) return value.ToString(CultureInfo.InvariantCulture) + "u";
        if (constant.Value is int intValue) return intValue.ToString(CultureInfo.InvariantCulture);
        if (constant.Value is float floatValue) return floatValue.ToString("R", CultureInfo.InvariantCulture) + "f";
        if (constant.Value is double doubleValue) return doubleValue.ToString("R", CultureInfo.InvariantCulture);
        if (constant.Value is bool boolValue) return boolValue ? "true" : "false";
        Error = "Closure, string, object, and null constants are unsupported in runtime shader lambdas.";
        return string.Empty;
    }

    private static string Operator(ExpressionType type) => type switch
    {
        ExpressionType.Add or ExpressionType.AddChecked => "+",
        ExpressionType.Subtract or ExpressionType.SubtractChecked => "-",
        ExpressionType.Multiply or ExpressionType.MultiplyChecked => "*",
        ExpressionType.Divide => "/",
        ExpressionType.Modulo => "%",
        ExpressionType.Equal => "==",
        ExpressionType.NotEqual => "!=",
        ExpressionType.GreaterThan => ">",
        ExpressionType.GreaterThanOrEqual => ">=",
        ExpressionType.LessThan => "<",
        ExpressionType.LessThanOrEqual => "<=",
        ExpressionType.AndAlso => "&&",
        ExpressionType.OrElse => "||",
        ExpressionType.And => "&",
        ExpressionType.Or => "|",
        ExpressionType.ExclusiveOr => "^",
        ExpressionType.Negate or ExpressionType.NegateChecked => "-",
        ExpressionType.Not => "!",
        ExpressionType.UnaryPlus => "+",
        _ => throw new InvalidOperationException($"Unsupported expression operator '{type}'.")
    };

    private static string TypeName(Type type)
        => type == typeof(uint) ? "uint" : type == typeof(int) ? "int" : type == typeof(float) ? "float" : "global::" + type.FullName!.Replace('+', '.');
}
