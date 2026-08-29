using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.Playground;

[Interstage]
public struct VolumetricVertexPayload
{
    [Position]
    public float4 Position;

    public float2 Uv;
}

public struct VolumetricFrameConstants
{
    public float2 Resolution;
    public float Time;
}

public readonly struct VolumetricVertexContext
{
    [Interstage]
    public readonly VolumetricVertexPayload Vertex;

    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<float4> Vertices;

    [PushConstant]
    public readonly VolumetricFrameConstants Constants;
}

public readonly struct VolumetricFragmentContext
{
    [Interstage]
    public readonly VolumetricVertexPayload Fragment;

    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<float4> Vertices;

    [PushConstant]
    public readonly VolumetricFrameConstants Constants;
}

public static class VolumetricShader
{
    [VertexShader("volumetric")]
    public static VolumetricVertexPayload Vertex(in VolumetricVertexContext context)
    {
        VolumetricVertexPayload output = default;
        output.Position = context.Vertex.Position;
        output.Uv = context.Vertex.Uv;
        return output;
    }

    [FragmentShader("volumetric")]
    public static float4 Fragment(in VolumetricFragmentContext context)
    {
        // 1. Prepare UV coordinates (-1 to 1) with aspect ratio correction.
        float2 uv = context.Fragment.Uv * 2.0f - 1.0f;
        float aspectRatio = context.Constants.Resolution.x / context.Constants.Resolution.y;
        uv.x *= aspectRatio;

        float time = context.Constants.Time;

        // 2. Initialize the ray origin and direction.
        float3 ro = new float3(0.0f, 0.0f, time * 1.5f);
        float3 rd = maths.normalize(new float3(uv.x, uv.y, 1.0f));

        float angleX = maths.sin(time * 0.3f) * 0.2f;
        float angleY = maths.cos(time * 0.2f) * 0.2f;
        rd = RotateX(rd, angleX);
        rd = RotateY(rd, angleY);

        // 3. Raymarch the signed distance field.
        float t = 0.0f;
        float maxDist = 40.0f;
        float glow = 0.0f;
        bool hit = false;

        for (int i = 0; i < 64; i++)
        {
            float3 p = ro + rd * t;
            float distance = SceneSdf(p, time);
            glow += 0.015f / (0.015f + distance * distance);

            if (distance < 0.001f)
            {
                hit = true;
                break;
            }

            if (t > maxDist)
            {
                break;
            }

            t += distance;
        }

        // 4. Calculate the final color.
        float3 finalColor = new float3(0.0f, 0.0f, 0.0f);
        float3 neonColor = new float3(
            maths.sin(time * 0.5f) * 0.5f + 0.5f,
            maths.sin(time * 0.7f + 2.0f) * 0.5f + 0.5f,
            maths.cos(time * 0.3f) * 0.5f + 0.5f);

        if (hit)
        {
            float3 p = ro + rd * t;
            float3 normal = CalculateNormal(p, time);
            float3 lightDirection = maths.normalize(new float3(0.5f, 1.0f, -0.5f));
            float diffuse = maths.max(maths.dot(normal, lightDirection), 0.0f);
            float fog = maths.exp(-0.08f * t);
            finalColor = (neonColor * diffuse + new float3(0.1f, 0.1f, 0.2f)) * fog;
        }

        finalColor += neonColor * glow * 0.4f;

        // 5. Apply a vignette at the screen edges.
        float vignette = context.Fragment.Uv.x
            * context.Fragment.Uv.y
            * (1.0f - context.Fragment.Uv.x)
            * (1.0f - context.Fragment.Uv.y);
        vignette = maths.clamp(maths.pow(vignette * 16.0f, 0.25f), 0.0f, 1.0f);
        finalColor *= vignette;

        return new float4(finalColor.x, finalColor.y, finalColor.z, 1.0f);
    }

    private static float SceneSdf(float3 p, float time)
    {
        float3 spacing = new float3(3.0f, 3.0f, 3.0f);
        float3 q = maths.fract((p + spacing * 0.5f) / spacing)
            * spacing - spacing * 0.5f;
        float wave = maths.sin(p.x * 0.5f + time)
            * maths.cos(p.y * 0.5f + time) * 0.2f;
        float sphereRadius = 0.5f + wave;
        return maths.length(q) - sphereRadius;
    }

    private static float3 CalculateNormal(float3 p, float time)
    {
        float epsilon = 0.001f;
        float distance = SceneSdf(p, time);
        float3 normal = new float3(
            SceneSdf(new float3(p.x + epsilon, p.y, p.z), time) - distance,
            SceneSdf(new float3(p.x, p.y + epsilon, p.z), time) - distance,
            SceneSdf(new float3(p.x, p.y, p.z + epsilon), time) - distance);
        return maths.normalize(normal);
    }

    private static float3 RotateX(float3 value, float angle)
    {
        float sine = maths.sin(angle);
        float cosine = maths.cos(angle);
        return new float3(
            value.x,
            value.y * cosine - value.z * sine,
            value.y * sine + value.z * cosine);
    }

    private static float3 RotateY(float3 value, float angle)
    {
        float sine = maths.sin(angle);
        float cosine = maths.cos(angle);
        return new float3(
            value.x * cosine + value.z * sine,
            value.y,
            -value.x * sine + value.z * cosine);
    }
}
