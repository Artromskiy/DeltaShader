using System;
using System.Collections.Generic;
using System.Linq;
using Delta.Shader;
namespace Delta.Shader.Compiler.Intrinsics;

internal static class ShaderContractHelperEmitter
{
    public static IReadOnlyList<string> Emit(
        ShaderContractManifest contract,
        ShaderStage stage,
        IEnumerable<string> sourceFragments)
    {
        var candidates = contract.Functions
            .Where(function => function.Mapping == ShaderContractMapping.Helper)
            .Where(function => function.Stages.Count == 0 || function.Stages.Contains(stage.ToString(), StringComparer.OrdinalIgnoreCase))
            .Where(function => !string.IsNullOrWhiteSpace(function.GlslName))
            .OrderBy(HelperOrder)
            .ThenBy(function => function.TypeClrName, StringComparer.Ordinal)
            .ThenBy(function => function.ClrName, StringComparer.Ordinal)
            .ToArray();

        var catalog = new HelperCatalog(candidates);
        var selectedSignatures = new HashSet<string>(StringComparer.Ordinal);
        var generatedSignatures = new HashSet<string>(StringComparer.Ordinal);
        var source = string.Join("\n", sourceFragments);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var function in candidates)
            {
                var canonical = catalog.GetCanonical(function);
                var signature = catalog.GetSignature(canonical);
                if (selectedSignatures.Contains(signature) ||
                    canonical.GlslName is not { Length: > 0 } name ||
                    !source.Contains(name + "(", StringComparison.Ordinal))
                {
                    continue;
                }

                selectedSignatures.Add(signature);
                changed = true;
            }

            foreach (var function in candidates)
            {
                var canonical = catalog.GetCanonical(function);
                var signature = catalog.GetSignature(canonical);
                if (!selectedSignatures.Contains(signature) ||
                    !generatedSignatures.Add(signature))
                {
                    continue;
                }

                var helper = EmitFunction(canonical, catalog);
                if (helper is not null)
                {
                    source += "\n" + helper;
                }
            }
        }

        var emitted = new List<string>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in candidates)
        {
            var canonical = catalog.GetCanonical(function);
            var signature = catalog.GetSignature(canonical);
            if (!selectedSignatures.Contains(signature))
            {
                continue;
            }

            if (!signatures.Add(signature))
            {
                continue;
            }

            var helperSource = EmitFunction(canonical, catalog);
            if (helperSource is not null)
            {
                emitted.Add(helperSource);
            }
        }

        if (selectedSignatures.Any(signature => signature.StartsWith("delta_d_", StringComparison.Ordinal)))
        {
            emitted.Add(EmitDoubleMathHelpers());
        }

        return emitted;
    }

    private static string EmitDoubleMathHelpers()
    {
        const string scalar = @"
double delta_d_sin(double x)
{
    double q = floor(x * 0.636619772367581343075535053490057448 + 0.5);
    int quadrant = int(q);
    double r = x - q * 1.5707963267948966192313216916397527;
    double z = r * r;
    double s = r * (1.0 + z * (-0.166666666666666324348 + z * (0.00833333333332248946124 + z * (-0.000198412698298579493134 + z * (0.00000275573137070700676789 + z * (-0.0000000250507602534068634195 + z * 0.000000000158969099521155010221))))));
    double c = 1.0 + z * (-0.5 + z * (0.0416666666666666019037 + z * (-0.00138888888888741095749 + z * (0.0000248015872894767294178 + z * (-0.000000275573143513906633035 + z * (0.00000000208757232129817482790 + z * -0.0000000000113596475577881948265))))));
    int branch = quadrant & 3;
    if (branch == 0) return s;
    if (branch == 1) return c;
    if (branch == 2) return -s;
    return -c;
}

double delta_d_cos(double x)
{
    double q = floor(x * 0.636619772367581343075535053490057448 + 0.5);
    int quadrant = int(q);
    double r = x - q * 1.5707963267948966192313216916397527;
    double z = r * r;
    double s = r * (1.0 + z * (-0.166666666666666324348 + z * (0.00833333333332248946124 + z * (-0.000198412698298579493134 + z * (0.00000275573137070700676789 + z * (-0.0000000250507602534068634195 + z * 0.000000000158969099521155010221))))));
    double c = 1.0 + z * (-0.5 + z * (0.0416666666666666019037 + z * (-0.00138888888888741095749 + z * (0.0000248015872894767294178 + z * (-0.000000275573143513906633035 + z * (0.00000000208757232129817482790 + z * -0.0000000000113596475577881948265))))));
    int branch = quadrant & 3;
    if (branch == 0) return c;
    if (branch == 1) return -s;
    if (branch == 2) return -c;
    return s;
}

double delta_d_tan(double x) { return delta_d_sin(x) / delta_d_cos(x); }

double delta_d_atan_unit(double x)
{
    double z = x * x;
    double p = -0.0370370370370370370370370370370370;
    p = 0.04 + z * p;
    p = -0.0434782608695652173913043478260870 + z * p;
    p = 0.0476190476190476190476190476190476 + z * p;
    p = -0.0526315789473684210526315789473684 + z * p;
    p = 0.0588235294117647058823529411764706 + z * p;
    p = -0.0666666666666666666666666666666667 + z * p;
    p = 0.0769230769230769230769230769230769 + z * p;
    p = -0.0909090909090909090909090909090909 + z * p;
    p = 0.1111111111111111111111111111111111 + z * p;
    p = -0.1428571428571428571428571428571429 + z * p;
    p = 0.2 + z * p;
    p = -0.3333333333333333333333333333333333 + z * p;
    return x * (1.0 + z * p);
}

double delta_d_atan(double x)
{
    double ax = abs(x);
    double y = ax <= 1.0 ? ax : 1.0 / ax;
    double t = y / (1.0 + sqrt(1.0 + y * y));
    double result = 2.0 * delta_d_atan_unit(t);
    if (ax > 1.0) result = 1.5707963267948966192313216916397527 - result;
    return x < 0.0 ? -result : result;
}

double delta_d_atan2(double y, double x)
{
    double ay = abs(y);
    double ax = abs(x);
    double a = delta_d_atan(ay / max(ax, 0.0000000000000000000000000000000001));
    if (x < 0.0) a = 3.1415926535897932384626433832795029 - a;
    return y < 0.0 ? -a : a;
}

double delta_d_exp(double x)
{
    int n = int(floor(x * 1.4426950408889634073599246810018921 + 0.5));
    double r = x - double(n) * 0.6931471805599453094172321214581766;
    double p = 1.0 + r * (1.0 + r * (0.5 + r * (0.1666666666666666666666666666666667 + r * (0.0416666666666666666666666666666667 + r * (0.0083333333333333333333333333333333 + r * (0.001388888888888888888888888888888889 + r * (0.0001984126984126984126984126984127 + r * (0.0000248015873015873015873015873016 + r * (0.0000027557319223985890652557319224 + r * (0.0000002755731922398589065255731922 + r * (0.0000000250521083854417187757201646 + r * 0.0000000020876756987868098979210090)))))))))));
    return ldexp(p, n);
}

double delta_d_exp2(double x) { return delta_d_exp(x * 0.6931471805599453094172321214581766); }

double delta_d_log(double x)
{
    int exponent;
    double m = frexp(x, exponent);
    double y = (m - 1.0) / (m + 1.0);
    double z = y * y;
    double p = 0.1479819860511658591;
    p = 0.1531383769920937332 + z * p;
    p = 0.1818357216161805012 + z * p;
    p = 0.2222219843214978396 + z * p;
    p = 0.2857142874366239149 + z * p;
    p = 0.3999999999940941908 + z * p;
    p = 0.6666666666666735130 + z * p;
    double series = y * (2.0 + z * p);
    return series + double(exponent) * 0.6931471805599453094172321214581766;
}

double delta_d_log2(double x) { return delta_d_log(x) * 1.4426950408889634073599246810018921; }
double delta_d_pow(double x, double y) { return delta_d_exp(y * delta_d_log(x)); }
double delta_d_radians(double x) { return x * 0.0174532925199432957692369076848861; }
double delta_d_degrees(double x) { return x * 57.2957795130823208767981548141052; }
double delta_d_asin(double x) { return delta_d_atan(x / sqrt(1.0 - x * x)); }
double delta_d_acos(double x) { return 1.5707963267948966192313216916397527 - delta_d_asin(x); }
double delta_d_sinh(double x) { double e = delta_d_exp(x); return 0.5 * (e - 1.0 / e); }
double delta_d_cosh(double x) { double e = delta_d_exp(x); return 0.5 * (e + 1.0 / e); }
double delta_d_tanh(double x) { double e = delta_d_exp(2.0 * x); return (e - 1.0) / (e + 1.0); }
double delta_d_asinh(double x) { return delta_d_log(x + sqrt(x * x + 1.0)); }
double delta_d_acosh(double x) { return delta_d_log(x + sqrt((x - 1.0) * (x + 1.0))); }
double delta_d_atanh(double x) { return 0.5 * delta_d_log((1.0 + x) / (1.0 - x)); }
";

        var helpers = new List<string> { scalar };
        var vectorTypes = new[] { (Type: "dvec2", Components: new[] { "x", "y" }), (Type: "dvec3", Components: new[] { "x", "y", "z" }), (Type: "dvec4", Components: new[] { "x", "y", "z", "w" }) };
        var unary = new[] { "sin", "cos", "tan", "atan", "asin", "acos", "sinh", "cosh", "tanh", "asinh", "acosh", "atanh", "exp", "exp2", "log", "log2", "radians", "degrees" };
        foreach (var name in unary)
        {
            foreach (var vector in vectorTypes)
            {
                var values = string.Join(", ", vector.Components.Select(component => "delta_d_" + name + "(value." + component + ")"));
                helpers.Add(vector.Type + " delta_d_" + name + "(" + vector.Type + " value) { return " + vector.Type + "(" + values + "); }");
            }
        }

        foreach (var vector in vectorTypes)
        {
            var values = string.Join(", ", vector.Components.Select(component => "delta_d_pow(x." + component + ", y." + component + ")"));
            helpers.Add(vector.Type + " delta_d_pow(" + vector.Type + " x, " + vector.Type + " y) { return " + vector.Type + "(" + values + "); }");
            values = string.Join(", ", vector.Components.Select(component => "delta_d_atan2(y." + component + ", x." + component + ")"));
            helpers.Add(vector.Type + " delta_d_atan2(" + vector.Type + " y, " + vector.Type + " x) { return " + vector.Type + "(" + values + "); }");
            values = string.Join(", ", vector.Components.Select(component => "delta_d_atan2(y." + component + ", x)"));
            helpers.Add(vector.Type + " delta_d_atan2(" + vector.Type + " y, double x) { return " + vector.Type + "(" + values + "); }");
            values = string.Join(", ", vector.Components.Select(component => "delta_d_atan2(y, x." + component + ")"));
            helpers.Add(vector.Type + " delta_d_atan2(double y, " + vector.Type + " x) { return " + vector.Type + "(" + values + "); }");
        }

        return string.Join("\n", helpers);
    }

    private static string? EmitFunction(ShaderContractFunction function, HelperCatalog catalog)
    {
        var returnType = catalog.GetReturnType(function);
        var parameterTypes = catalog.GetParameterTypes(function);
        if (returnType is null || parameterTypes is null || function.GlslName is not { Length: > 0 } glslName)
        {
            return null;
        }

        if (string.Equals(function.ClrName, "Select", StringComparison.Ordinal)
            && IsSelectVector(returnType, parameterTypes))
        {
            return EmitSelect(glslName, returnType, parameterTypes[2]);
        }

        var methodName = NormalizeMethodName(function);
        if (string.Equals(function.TypeClrName, "float4x4", StringComparison.Ordinal))
        {
            return EmitMatrix(function, methodName, returnType, parameterTypes, catalog);
        }

        if (string.Equals(function.TypeClrName, "quaternion", StringComparison.Ordinal))
        {
            return EmitQuaternion(function, methodName, returnType, parameterTypes, catalog);
        }

        return null;
    }

    private static string EmitSelect(string name, string returnType, string maskType)
    {
        var components = returnType switch
        {
            "vec2" or "ivec2" or "uvec2" or "dvec2" or "f16vec2" => new[] { "x", "y" },
            "vec3" or "ivec3" or "uvec3" or "dvec3" or "f16vec3" => new[] { "x", "y", "z" },
            "vec4" or "ivec4" or "uvec4" or "dvec4" or "f16vec4" => new[] { "x", "y", "z", "w" },
            _ => Array.Empty<string>()
        };

        var values = components
            .Select(component => "mask." + component + " ? trueValue." + component + " : falseValue." + component);
        return returnType + " " + name + "(" + returnType + " falseValue, " + returnType + " trueValue, " + maskType + " mask) { return " + returnType + "(" + string.Join(", ", values) + "); }";
    }

    private static string? EmitMatrix(
        ShaderContractFunction function,
        string methodName,
        string returnType,
        IReadOnlyList<string> parameterTypes,
        HelperCatalog catalog)
    {
        if (function.GlslName is not { Length: > 0 } name)
        {
            return null;
        }

        if (string.Equals(methodName, "CreateTranslation", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(vec3 translation) { return mat4(vec4(1.0, 0.0, 0.0, 0.0), vec4(0.0, 1.0, 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(translation.x, translation.y, translation.z, 1.0)); }";
        }

        if (string.Equals(methodName, "CreateScale", StringComparison.Ordinal)
            && parameterTypes.Count == 1
            && string.Equals(parameterTypes[0], "float", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(float scale) { return mat4(vec4(scale, 0.0, 0.0, 0.0), vec4(0.0, scale, 0.0, 0.0), vec4(0.0, 0.0, scale, 0.0), vec4(0.0, 0.0, 0.0, 1.0)); }";
        }

        if (string.Equals(methodName, "CreateScale", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(vec3 scale) { return mat4(vec4(scale.x, 0.0, 0.0, 0.0), vec4(0.0, scale.y, 0.0, 0.0), vec4(0.0, 0.0, scale.z, 0.0), vec4(0.0, 0.0, 0.0, 1.0)); }";
        }

        if (string.Equals(methodName, "CreateFromQuaternion", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(vec4 rotation) { float xx = rotation.x * rotation.x; float yy = rotation.y * rotation.y; float zz = rotation.z * rotation.z; float xy = rotation.x * rotation.y; float xz = rotation.x * rotation.z; float yz = rotation.y * rotation.z; float wx = rotation.w * rotation.x; float wy = rotation.w * rotation.y; float wz = rotation.w * rotation.z; return mat4(vec4(1.0 - 2.0 * (yy + zz), 2.0 * (xy + wz), 2.0 * (xz - wy), 0.0), vec4(2.0 * (xy - wz), 1.0 - 2.0 * (xx + zz), 2.0 * (yz + wx), 0.0), vec4(2.0 * (xz + wy), 2.0 * (yz - wx), 1.0 - 2.0 * (xx + yy), 0.0), vec4(0.0, 0.0, 0.0, 1.0)); }";
        }

        if (string.Equals(methodName, "CreateTRS", StringComparison.Ordinal))
        {
            var translation = catalog.FindName("float4x4", "CreateTranslation", "float3");
            var rotation = catalog.FindName("float4x4", "CreateFromQuaternion", "quaternion");
            var scale = catalog.FindName("float4x4", "CreateScale", "float3");
            if (translation is null || rotation is null || scale is null)
            {
                return null;
            }

            return returnType + " " + name + "(vec3 translation, vec4 rotation, vec3 scale) { return " + translation + "(translation) * " + rotation + "(rotation) * " + scale + "(scale); }";
        }

        if (string.Equals(methodName, "CreateLookTo", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(vec3 eyePosition, vec3 direction, vec3 up) { vec3 zAxis = normalize(direction); vec3 xAxis = normalize(cross(up, zAxis)); vec3 yAxis = cross(zAxis, xAxis); return mat4(vec4(xAxis.x, xAxis.y, xAxis.z, 0.0), vec4(yAxis.x, yAxis.y, yAxis.z, 0.0), vec4(zAxis.x, zAxis.y, zAxis.z, 0.0), vec4(-dot(xAxis, eyePosition), -dot(yAxis, eyePosition), -dot(zAxis, eyePosition), 1.0)); }";
        }

        if (string.Equals(methodName, "CreatePerspectiveFieldOfViewLeftHanded", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(float fieldOfView, float aspectRatio, float nearPlaneDistance, float farPlaneDistance) { float yScale = 1.0 / tan(fieldOfView * 0.5); float xScale = yScale / aspectRatio; float range = farPlaneDistance / (farPlaneDistance - nearPlaneDistance); return mat4(vec4(xScale, 0.0, 0.0, 0.0), vec4(0.0, yScale, 0.0, 0.0), vec4(0.0, 0.0, range, 1.0), vec4(0.0, 0.0, -nearPlaneDistance * range, 0.0)); }";
        }

        if (string.Equals(methodName, "TransformDirection", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(mat4 matrix, vec3 direction) { return (matrix * vec4(direction, 0.0)).xyz; }";
        }

        if (string.Equals(methodName, "TransformPoint", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(mat4 matrix, vec3 point) { vec4 value = matrix * vec4(point, 1.0); if (value.w == 0.0) { return value.xyz; } return value.xyz / value.w; }";
        }

        return null;
    }

    private static string? EmitQuaternion(
        ShaderContractFunction function,
        string methodName,
        string returnType,
        IReadOnlyList<string> parameterTypes,
        HelperCatalog catalog)
    {
        if (function.GlslName is not { Length: > 0 } name)
        {
            return null;
        }

        if (string.Equals(methodName, "Conjugate", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(vec4 value) { return vec4(-value.xyz, value.w); }";
        }

        if (string.Equals(methodName, "CreateFromAxisAngle", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(vec3 axis, float angle) { float axisLength = length(axis); vec3 normalizedAxis = axisLength <= 1e-10 ? vec3(0.0) : axis / axisLength; float sine = sin(angle * 0.5); return vec4(-normalizedAxis * sine, cos(angle * 0.5)); }";
        }

        if (string.Equals(methodName, "CreateFromYawPitchRoll", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(float yaw, float pitch, float roll) { float halfYaw = yaw * 0.5; float halfPitch = pitch * 0.5; float halfRoll = roll * 0.5; float sinYaw = sin(halfYaw); float cosYaw = cos(halfYaw); float sinPitch = sin(halfPitch); float cosPitch = cos(halfPitch); float sinRoll = sin(halfRoll); float cosRoll = cos(halfRoll); return vec4(-(cosYaw * sinPitch * cosRoll + sinYaw * cosPitch * sinRoll), -(sinYaw * cosPitch * cosRoll - cosYaw * sinPitch * sinRoll), -(cosYaw * cosPitch * sinRoll - sinYaw * sinPitch * cosRoll), cosYaw * cosPitch * cosRoll + sinYaw * sinPitch * sinRoll); }";
        }

        if (string.Equals(methodName, "Normalize", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(vec4 value) { return value / sqrt(dot(value, value)); }";
        }

        if (string.Equals(methodName, "Inverse", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(vec4 value) { float lengthSquared = dot(value, value); if (lengthSquared <= 1e-20) { return vec4(0.0, 0.0, 0.0, 1.0); } return vec4(-value.xyz, value.w) / lengthSquared; }";
        }

        if (string.Equals(methodName, "Rotate", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(vec4 rotation, vec3 value) { vec3 qv = rotation.xyz; vec3 t = 2.0 * cross(qv, value); return value + rotation.w * t + cross(qv, t); }";
        }

        if (string.Equals(methodName, "Lerp", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(vec4 start, vec4 endValue, float amount) { if (dot(start, endValue) < 0.0) { endValue = -endValue; } vec4 value = start + (endValue - start) * amount; float lengthSquared = dot(value, value); if (lengthSquared <= 1e-20) { return vec4(0.0, 0.0, 0.0, 1.0); } return value / sqrt(lengthSquared); }";
        }

        if (string.Equals(methodName, "Slerp", StringComparison.Ordinal))
        {
            var lerp = catalog.FindName("quaternion", "Lerp", "quaternion", "quaternion", "float");
            if (lerp is null)
            {
                return null;
            }

            return returnType + " " + name + "(vec4 start, vec4 endValue, float amount) { float dotValue = dot(start, endValue); if (dotValue < 0.0) { endValue = -endValue; dotValue = -dotValue; } if (dotValue > 0.9995) { return " + lerp + "(start, endValue, amount); } dotValue = clamp(dotValue, -1.0, 1.0); float angle = acos(dotValue); float scale = 1.0 / sin(angle); return start * (sin((1.0 - amount) * angle) * scale) + endValue * (sin(amount * angle) * scale); }";
        }

        if (string.Equals(methodName, "CreateFromRotationMatrix", StringComparison.Ordinal))
        {
            return returnType + " " + name + "(mat4 matrix) { float trace = matrix[0][0] + matrix[1][1] + matrix[2][2]; if (trace > 0.0) { float s = sqrt(trace + 1.0) * 2.0; return vec4((matrix[1][2] - matrix[2][1]) / s, (matrix[2][0] - matrix[0][2]) / s, (matrix[0][1] - matrix[1][0]) / s, 0.25 * s); } if (matrix[0][0] > matrix[1][1] && matrix[0][0] > matrix[2][2]) { float s = sqrt(1.0 + matrix[0][0] - matrix[1][1] - matrix[2][2]) * 2.0; return vec4(0.25 * s, (matrix[0][1] + matrix[1][0]) / s, (matrix[0][2] + matrix[2][0]) / s, (matrix[1][2] - matrix[2][1]) / s); } if (matrix[1][1] > matrix[2][2]) { float s = sqrt(1.0 + matrix[1][1] - matrix[0][0] - matrix[2][2]) * 2.0; return vec4((matrix[0][1] + matrix[1][0]) / s, 0.25 * s, (matrix[1][2] + matrix[2][1]) / s, (matrix[2][0] - matrix[0][2]) / s); } float s = sqrt(1.0 + matrix[2][2] - matrix[0][0] - matrix[1][1]) * 2.0; return vec4((matrix[0][2] + matrix[2][0]) / s, (matrix[1][2] + matrix[2][1]) / s, 0.25 * s, (matrix[0][1] - matrix[1][0]) / s); }";
        }

        if (string.Equals(methodName, "ToRotationMatrix", StringComparison.Ordinal))
        {
            var matrixName = catalog.FindName("float4x4", "CreateFromQuaternion", "quaternion");
            return matrixName is null ? null : returnType + " " + name + "(vec4 rotation) { return " + matrixName + "(rotation); }";
        }

        return null;
    }

    private static bool IsSelectVector(string returnType, IReadOnlyList<string> parameterTypes)
    {
        return parameterTypes.Count == 3
            && string.Equals(parameterTypes[0], returnType, StringComparison.Ordinal)
            && string.Equals(parameterTypes[1], returnType, StringComparison.Ordinal)
            && returnType is "vec2" or "vec3" or "vec4"
                or "ivec2" or "ivec3" or "ivec4"
                or "uvec2" or "uvec3" or "uvec4"
                or "dvec2" or "dvec3" or "dvec4"
                or "f16vec2" or "f16vec3" or "f16vec4";
    }

    private static string NormalizeMethodName(ShaderContractFunction function)
    {
        if (!string.Equals(function.TypeClrName, "maths", StringComparison.Ordinal)
            || function.ClrName.Length == 0)
        {
            return function.ClrName;
        }

        return char.ToUpperInvariant(function.ClrName[0]) + function.ClrName.Substring(1);
    }

    private static int HelperOrder(ShaderContractFunction function)
    {
        return NormalizeMethodName(function) switch
        {
            "Select" => 0,
            "CreateTranslation" => 10,
            "CreateScale" => 11,
            "CreateFromQuaternion" => 12,
            "CreateLookTo" => 13,
            "CreatePerspectiveFieldOfViewLeftHanded" => 14,
            "TransformDirection" => 15,
            "TransformPoint" => 16,
            "CreateTRS" => 17,
            "Conjugate" => 20,
            "CreateFromAxisAngle" => 21,
            "CreateFromYawPitchRoll" => 22,
            "CreateFromRotationMatrix" => 23,
            "Normalize" => 24,
            "Inverse" => 25,
            "Rotate" => 26,
            "Lerp" => 27,
            "Slerp" => 28,
            "ToRotationMatrix" => 29,
            _ => 100
        };
    }

    private sealed class HelperCatalog
    {
        private readonly IReadOnlyList<ShaderContractFunction> _functions;

        public HelperCatalog(IReadOnlyList<ShaderContractFunction> functions)
        {
            _functions = functions;
        }

        public ShaderContractFunction GetCanonical(ShaderContractFunction function)
        {
            var signature = GetSignature(function);
            return _functions
                .Where(candidate => string.Equals(GetSignature(candidate), signature, StringComparison.Ordinal))
                .OrderBy(candidate => string.Equals(candidate.TypeClrName, "maths", StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(candidate => candidate.TypeClrName, StringComparer.Ordinal)
                .First();
        }

        public string GetSignature(ShaderContractFunction function)
        {
            return function.GlslName + "(" + string.Join(",", GetParameterTypes(function) ?? Array.Empty<string>()) + "):" + (GetReturnType(function) ?? string.Empty);
        }

        public string? GetReturnType(ShaderContractFunction function)
        {
            return string.IsNullOrWhiteSpace(function.ReturnGlslType)
                ? ToGlslType(function.ReturnClrName)
                : function.ReturnGlslType;
        }

        public IReadOnlyList<string>? GetParameterTypes(ShaderContractFunction function)
        {
            if (function.ParameterGlslTypes.Count == function.ParameterClrNames.Count
                && function.ParameterGlslTypes.All(type => type is not null))
            {
                var declaredTypes = new string[function.ParameterGlslTypes.Count];
                for (var index = 0; index < declaredTypes.Length; index++)
                {
                    var type = function.ParameterGlslTypes[index];
                    if (type is null)
                    {
                        return null;
                    }

                    declaredTypes[index] = type;
                }

                return declaredTypes;
            }

            var mappedTypes = new string[function.ParameterClrNames.Count];
            for (var index = 0; index < mappedTypes.Length; index++)
            {
                var type = ToGlslType(function.ParameterClrNames[index]);
                if (type is null)
                {
                    return null;
                }

                mappedTypes[index] = type;
            }

            return mappedTypes;
        }

        public string? FindName(string typeName, string methodName, params string[] parameterTypes)
        {
            return _functions
                .Where(candidate => string.Equals(candidate.TypeClrName, typeName, StringComparison.Ordinal))
                .Where(candidate => string.Equals(NormalizeMethodName(candidate), methodName, StringComparison.Ordinal))
                .Where(candidate => candidate.ParameterClrNames.SequenceEqual(parameterTypes, StringComparer.Ordinal))
                .Select(candidate => candidate.GlslName)
                .FirstOrDefault();
        }

        private static string? ToGlslType(string clrType)
        {
            return clrType switch
            {
                "float" => "float",
                "int" => "int",
                "uint" => "uint",
                "bool" => "bool",
                "float2" => "vec2",
                "float3" => "vec3",
                "float4" => "vec4",
                "int2" => "ivec2",
                "int3" => "ivec3",
                "int4" => "ivec4",
                "uint2" => "uvec2",
                "uint3" => "uvec3",
                "uint4" => "uvec4",
                "bool2" => "bvec2",
                "bool3" => "bvec3",
                "bool4" => "bvec4",
                "float4x4" => "mat4",
                "quaternion" => "vec4",
                _ => null
            };
        }
    }
}
