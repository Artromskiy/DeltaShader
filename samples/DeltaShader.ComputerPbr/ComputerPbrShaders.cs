using Delta.Maths;
using Delta.Shader;
using static Delta.Maths.maths;

namespace Delta.Shader.ComputerPbr;

[Interstage]
public struct ComputerPayload
{
    public Position Position;
    public Uv0 Uv;
}

public readonly struct ComputerFrame
{
    public readonly float2 Resolution;
    public readonly float Time;
}

public readonly struct VertexContext
{
    [Interstage]
    public readonly ComputerPayload Input;

    [PushConstant]
    public readonly ComputerFrame Frame;
}

public readonly struct FragmentContext
{
    [Interstage]
    public readonly ComputerPayload Input;

    [PushConstant]
    public readonly ComputerFrame Frame;
}

public static class ComputerPbrComposite
{
    [VertexShader("computer-pbr-vertex")]
    public static ComputerPayload Vertex(in VertexContext context)
    {
        uint vertexIndex = ShaderBuiltins.VertexIndex;
        if (vertexIndex == 0u)
        {
            return new ComputerPayload
            {
                Position = new Position(new float4(-1.0f, -1.0f, 0.0f, 1.0f)),
                Uv = new Uv0(new float2(0.0f, 1.0f))
            };
        }

        if (vertexIndex == 1u)
        {
            return new ComputerPayload
            {
                Position = new Position(new float4(3.0f, -1.0f, 0.0f, 1.0f)),
                Uv = new Uv0(new float2(2.0f, 1.0f))
            };
        }

        return new ComputerPayload
        {
            Position = new Position(new float4(-1.0f, 3.0f, 0.0f, 1.0f)),
            Uv = new Uv0(new float2(0.0f, -1.0f))
        };
    }

    [FragmentShader("computer-pbr-fragment")]
    public static float4 Fragment(in FragmentContext context)
    {
        float2 point = context.Input.Uv.Value * 2.0f - new float2(1.0f, 1.0f);
        point.x *= context.Frame.Resolution.x / context.Frame.Resolution.y;
        point.y += 0.03f;

        float4 scene = SampleComputer(point);
        float3 color = ComposePbrLayers(point, scene, context.Frame.Time);
        float coverage = 1.0f - smoothstep(-0.006f, 0.006f, scene.x);
        float3 background = BackgroundLayer(point, context.Frame.Time);
        return new float4(background * (1.0f - coverage) + color * coverage, 1.0f);
    }

    private static float4 SampleComputer(float2 point)
    {
        float4 result = new float4(
            RoundedBox(point - new float2(0.0f, 0.24f), new float2(0.72f, 0.43f), 0.07f),
            0.0f,
            0.58f,
            0.0f);

        float screen = RoundedBox(
            point - new float2(0.0f, 0.24f),
            new float2(0.60f, 0.31f),
            0.035f);
        if (screen < 0.0f)
        {
            result = new float4(screen, 1.0f, 0.24f, 0.08f);
        }

        float stand = RoundedBox(
            point - new float2(0.0f, -0.34f),
            new float2(0.11f, 0.16f),
            0.025f);
        if (stand < result.x)
        {
            result = new float4(stand, 2.0f, 0.68f, 0.0f);
        }

        float baseDistance = RoundedBox(
            point - new float2(0.0f, -0.47f),
            new float2(0.79f, 0.11f),
            0.04f);
        if (baseDistance < result.x)
        {
            result = new float4(baseDistance, 3.0f, 0.72f, 0.0f);
        }

        float keyboard = RoundedBox(
            point - new float2(0.0f, -0.62f),
            new float2(0.53f, 0.065f),
            0.025f);
        if (keyboard < result.x)
        {
            result = new float4(keyboard, 4.0f, 0.42f, 0.0f);
        }

        float indicator = RoundedBox(
            point - new float2(0.60f, -0.47f),
            new float2(0.025f, 0.018f),
            0.012f);
        if (indicator < 0.0f)
        {
            result = new float4(indicator, 5.0f, 0.18f, 1.0f);
        }

        return result;
    }

    private static float SceneDistance(float2 point)
        => SampleComputer(point).x;

    private static float RoundedBox(float2 point, float2 halfSize, float radius)
    {
        float2 q = abs(point) - halfSize + new float2(radius, radius);
        float2 outside = max(q, new float2(0.0f, 0.0f));
        float inside = min(max(q.x, q.y), 0.0f);
        return length(outside) + inside - radius;
    }

    private static float3 SurfaceNormal(float2 point)
    {
        float epsilon = 0.002f;
        float dx = SceneDistance(point + new float2(epsilon, 0.0f)) -
            SceneDistance(point - new float2(epsilon, 0.0f));
        float dy = SceneDistance(point + new float2(0.0f, epsilon)) -
            SceneDistance(point - new float2(0.0f, epsilon));
        return normalize(new float3(dx, dy, 0.72f));
    }

    private static float3 SurfaceAlbedo(float material, float2 point, float time)
    {
        if (material > 0.5f && material < 1.5f)
        {
            float scan = 0.5f + 0.5f * sin(point.y * 28.0f + time * 0.8f);
            return new float3(0.04f, 0.28f + scan * 0.08f, 0.56f + scan * 0.16f);
        }

        if (material > 4.5f)
        {
            return new float3(0.9f, 0.32f, 0.05f);
        }

        if (material > 3.5f)
        {
            return new float3(0.12f, 0.14f, 0.18f);
        }

        if (material > 2.5f)
        {
            return new float3(0.16f, 0.19f, 0.24f);
        }

        return new float3(0.24f, 0.28f, 0.34f);
    }

    private static float3 AmbientLayer(float3 albedo, float3 normal)
    {
        float hemisphere = 0.5f + 0.5f * normal.y;
        return albedo * (0.045f + hemisphere * 0.12f);
    }

    private static float3 DirectLightLayer(
        float3 albedo,
        float3 normal,
        float roughness)
    {
        float3 light = normalize(new float3(-0.45f, 0.65f, 0.75f));
        float3 view = new float3(0.0f, 0.0f, 1.0f);
        float3 halfway = normalize(light + view);
        float diffuse = max(dot(normal, light), 0.0f);
        float specularPower = lerp(8.0f, 64.0f, 1.0f - roughness);
        float specular = pow(max(dot(normal, halfway), 0.0f), specularPower);
        return albedo * diffuse * 0.9f + new float3(specular, specular, specular) * 0.32f;
    }

    private static float3 ClearCoatLayer(float3 normal, float roughness, float material)
    {
        float3 view = new float3(0.0f, 0.0f, 1.0f);
        float fresnel = pow(1.0f - max(dot(normal, view), 0.0f), 5.0f);
        float coat = material < 2.5f ? 0.16f : 0.05f;
        float strength = coat * (0.35f + fresnel) * (1.0f - roughness * 0.5f);
        return new float3(strength, strength, strength);
    }

    private static float3 EmissionLayer(float material, float emission, float time)
    {
        float pulse = 0.8f + 0.2f * sin(time * 1.4f);
        if (material > 0.5f && material < 1.5f)
        {
            return new float3(0.01f, 0.04f, 0.09f) * pulse;
        }

        if (material > 4.5f)
        {
            return new float3(0.7f, 0.08f, 0.01f) * emission * pulse;
        }

        return new float3(0.0f, 0.0f, 0.0f);
    }

    private static float3 BackgroundLayer(float2 point, float time)
    {
        float horizon = 0.5f + 0.5f * point.y;
        float shimmer = 0.5f + 0.5f * sin(point.x * 2.0f + time * 0.12f);
        return new float3(
            0.008f + horizon * 0.012f,
            0.012f + horizon * 0.018f,
            0.025f + horizon * 0.045f + shimmer * 0.006f);
    }

    private static float3 ComposePbrLayers(float2 point, float4 scene, float time)
    {
        float3 normal = SurfaceNormal(point);
        float3 albedo = SurfaceAlbedo(scene.y, point, time);
        float3 ambient = AmbientLayer(albedo, normal);
        float3 direct = DirectLightLayer(albedo, normal, scene.z);
        float3 clearCoat = ClearCoatLayer(normal, scene.z, scene.y);
        float3 emission = EmissionLayer(scene.y, scene.w, time);
        return clamp(
            ambient + direct + clearCoat + emission,
            new float3(0.0f, 0.0f, 0.0f),
            new float3(1.5f, 1.5f, 1.5f));
    }
}
