using Delta.Maths;
using Delta.Shader;
using static Delta.Maths.maths;

namespace Delta.Shader.GrassComposite;

[Interstage]
public struct GrassPayload
{
    [Layout(0)]
    public Position Position;

    [Layout(1)]
    public Uv0 Uv;

    [Layout(2)]
    public VertexColor VertexColor;

    public WorldPosition WorldPosition;
    public WorldNormal WorldNormal;
}

public struct GrassFrame
{
    public float4 LocalColor;
    public float3 LightDirection;
    public float3 CameraPosition;
    public float Time;
    public float Roughness;
}

public readonly struct GrassVertexContext
{
    [Interstage]
    public readonly GrassPayload Vertex;

    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<float4x4> InstanceTransforms;

    [PushConstant]
    public readonly GrassFrame Frame;
}

public readonly struct GrassFragmentContext
{
    [Interstage]
    public readonly GrassPayload Fragment;

    [Layout(0, 1)]
    public readonly SampledTexture2D GrassTexture;

    [PushConstant]
    public readonly GrassFrame Frame;
}

public static class GrassCompositeLayers
{
    [VertexShader("grass-transform-instance")]
    public static GrassPayload TransformAndInstance(in GrassVertexContext context)
    {
        float4 localPosition = context.Vertex.Position.Value;
        float4 worldPosition = context.InstanceTransforms[ShaderBuiltins.InstanceIndex] * localPosition;
        return new GrassPayload
        {
            Position = worldPosition,
            Uv = context.Vertex.Uv,
            VertexColor = context.Vertex.VertexColor,
            WorldPosition = worldPosition.xyz,
            WorldNormal = new float3(0f, 1f, 0f)
        };
    }

    [FragmentShader("grass-textured-lambert")]
    public static float4 TexturedLambert(in GrassFragmentContext context)
    {
        float4 albedo = context.GrassTexture.Sample<float2, float4>(context.Fragment.Uv.Value);
        float diffuse = max(dot(normalize(context.Fragment.WorldNormal.Value), -normalize(context.Frame.LightDirection)), 0f);
        return new float4(albedo.xyz * context.Fragment.VertexColor.Value.xyz * diffuse, albedo.w);
    }

    [FragmentShader("grass-solid-lambert")]
    public static float4 SolidLambert(in GrassFragmentContext context)
    {
        float diffuse = max(dot(normalize(context.Fragment.WorldNormal.Value), -normalize(context.Frame.LightDirection)), 0f);
        return new float4(context.Frame.LocalColor.xyz * diffuse, context.Frame.LocalColor.w);
    }

    [FragmentShader("grass-local-position-color")]
    public static float4 LocalPositionColor(in GrassFragmentContext context)
    {
        float2 localPosition = context.Fragment.Uv.Value;
        float3 color = new float3(localPosition.x, localPosition.y, 1f - localPosition.x);
        return new float4(color, 1f);
    }

    [FragmentShader("grass-local-phong")]
    public static float4 LocalPhong(in GrassFragmentContext context)
    {
        float3 normal = normalize(context.Fragment.WorldNormal.Value);
        float3 light = -normalize(context.Frame.LightDirection);
        float3 view = normalize(context.Frame.CameraPosition - context.Fragment.WorldPosition.Value);
        float diffuse = max(dot(normal, light), 0f);
        float specular = pow(max(dot(normal, normalize(light + view)), 0f), 16f);
        return new float4(context.Frame.LocalColor.xyz * (diffuse + specular), context.Frame.LocalColor.w);
    }

    [FragmentShader("grass-toon")]
    public static float4 Toon(in GrassFragmentContext context)
    {
        float diffuse = max(dot(normalize(context.Fragment.WorldNormal.Value), -normalize(context.Frame.LightDirection)), 0f);
        float band = step(0.5f, diffuse);
        return new float4(context.Frame.LocalColor.xyz * (0.35f + band * 0.65f), context.Frame.LocalColor.w);
    }

    [FragmentShader("grass-pbr")]
    public static float4 Pbr(in GrassFragmentContext context)
    {
        float diffuse = max(dot(normalize(context.Fragment.WorldNormal.Value), -normalize(context.Frame.LightDirection)), 0f);
        float energy = lerp(0.35f, 1f, diffuse) * (1f - context.Frame.Roughness * 0.25f);
        return new float4(context.Frame.LocalColor.xyz * energy, context.Frame.LocalColor.w);
    }

    [FragmentShader("grass-fake-translucent")]
    public static float4 FakeTranslucent(in GrassFragmentContext context)
    {
        float backLight = max(dot(normalize(context.Fragment.WorldNormal.Value), normalize(context.Frame.LightDirection)), 0f);
        float transmission = exp(-context.Frame.Roughness) * backLight;
        return new float4(context.Frame.LocalColor.xyz * (0.6f + transmission), context.Frame.LocalColor.w);
    }
}
