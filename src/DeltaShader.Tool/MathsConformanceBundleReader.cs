using System.Text.Json;

namespace Delta.Shader.Tool;

internal static class MathsConformanceBundleReader
{
    public static List<ContractFunction> LoadFunctions(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var functions = new List<ContractFunction>();
        foreach (var element in document.RootElement.GetProperty("functions").EnumerateArray())
        {
            var mapping = element.GetProperty("mapping").GetString();
            if (mapping is not ("Builtin" or "Helper"))
            {
                continue;
            }

            var parameters = element.GetProperty("parameterClrNames")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray();
            var modifiers = element.TryGetProperty("parameterModifiers", out var modifierElement)
                ? ReadStrings(modifierElement)
                : parameters.Select(_ => "none").ToArray();
            functions.Add(new ContractFunction(
                element.GetProperty("identity").GetString() ?? string.Empty,
                element.GetProperty("typeClrName").GetString() ?? string.Empty,
                element.GetProperty("clrName").GetString() ?? string.Empty,
                parameters,
                modifiers,
                element.GetProperty("returnClrName").GetString() ?? string.Empty,
                element.GetProperty("mapping").GetString() ?? string.Empty,
                element.GetProperty("glslName").GetString() ?? string.Empty));
        }

        return functions;
    }

    public static ConformanceBundle LoadCaseBundle(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidOperationException("schemaVersion must be 1.");
        }

        if (!string.Equals(
                root.GetProperty("protocol").GetString(),
                "math-cpu-gpu-conformance-v0.1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("protocol is not math-cpu-gpu-conformance-v0.1.");
        }

        var cases = new List<ConformanceCase>();
        foreach (var element in root.GetProperty("cases").EnumerateArray())
        {
            var operation = element.GetProperty("operation");
            var contractFunction = new ContractFunction(
                RequiredString(operation, "identity"),
                RequiredString(operation, "ownerTypeName"),
                RequiredString(operation, "methodName"),
                ReadStrings(operation.GetProperty("parameterTypeNames")),
                operation.GetProperty("parameterTypeNames").EnumerateArray().Select(_ => "none").ToArray(),
                RequiredString(operation, "returnTypeName"),
                RequiredString(operation, "mapping"),
                string.Empty);
            var comparison = element.GetProperty("comparison");
            var dispositions = element.GetProperty("disposition");
            cases.Add(new ConformanceCase(
                RequiredString(element, "id"),
                contractFunction,
                element.GetProperty("inputs").EnumerateArray().Select(ReadValue).ToArray(),
                ReadValue(element.GetProperty("expected")),
                new ComparisonProfile(
                    RequiredString(comparison, "name"),
                    comparison.GetProperty("absoluteTolerance").GetDouble(),
                    comparison.GetProperty("relativeTolerance").GetDouble(),
                    comparison.GetProperty("maxUlps").GetInt32()),
                ReadStrings(element.GetProperty("requiredCapabilities")),
                ReadStrings(element.GetProperty("stages")),
                RequiredString(dispositions, "cpu"),
                RequiredString(dispositions, "shader"),
                RequiredString(dispositions, "render")));
        }

        var coverage = root.GetProperty("coverage");
        var coverageData = new ConformanceCoverage(
            coverage.GetProperty("manifestFunctionCount").GetInt32(),
            coverage.GetProperty("supportedCount").GetInt32(),
            coverage.GetProperty("caseCount").GetInt32(),
            coverage.GetProperty("excludedCount").GetInt32(),
            coverage.GetProperty("unsupportedManifestCount").GetInt32());
        if (coverageData.CaseCount != cases.Count)
        {
            throw new InvalidOperationException("coverage.caseCount does not match cases length.");
        }

        return new ConformanceBundle(cases.ToArray(), coverageData);
    }

    private static ConformanceValue ReadValue(JsonElement element)
        => new(RequiredString(element, "type"), ReadStrings(element.GetProperty("words")));

    private static string[] ReadStrings(JsonElement element)
        => element.EnumerateArray()
            .Select(value => value.GetString() ?? throw new InvalidOperationException("bundle string value is missing."))
            .ToArray();

    private static string RequiredString(JsonElement element, string propertyName)
        => element.GetProperty(propertyName).GetString()
            ?? throw new InvalidOperationException($"bundle property '{propertyName}' is missing.");
}
