using Delta.Maths;
using Delta.Shader;
using static Delta.Maths.maths;

namespace Delta.Shader.ComputerPbr;

[Interstage]
public struct ComputerMeshPayload
{
    [Layout(0)]
    public Position Position;

    [Layout(1)]
    public WorldNormal WorldNormal;

    [Layout(2)]
    public Uv0 Uv;

    [Layout(3)]
    public Tangent Tangent;

    public WorldPosition WorldPosition;
}

public readonly struct ComputerMeshFrame
{
    public ComputerMeshFrame(float4x4 modelViewProjection, float3 lightDirection, float3 cameraPosition, float time)
    {
        ModelViewProjection = modelViewProjection;
        LightDirection = lightDirection;
        CameraPosition = cameraPosition;
        Time = time;
    }

    public readonly float4x4 ModelViewProjection;
    public readonly float3 LightDirection;
    public readonly float3 CameraPosition;
    public readonly float Time;
}

public readonly struct ComputerMeshVertexContext
{
    [Interstage]
    public readonly ComputerMeshPayload Vertex;

    [PushConstant]
    public readonly ComputerMeshFrame Frame;
}

public readonly struct ComputerMeshFragmentContext
{
    [Interstage]
    public readonly ComputerMeshPayload Fragment;

    [Layout(0, 4)]
    public readonly SampledTexture2D BaseColor;

    [Layout(0, 5)]
    public readonly SampledTexture2D Metallic;

    [Layout(0, 6)]
    public readonly SampledTexture2D Normal;

    [Layout(0, 7)]
    public readonly SampledTexture2D Roughness;

    [Layout(0, 8)]
    public readonly SampledTexture2D Occlusion;

    [Layout(0, 9)]
    public readonly SampledTexture2D Emissive;

    [PushConstant]
    public readonly ComputerMeshFrame Frame;
}

public static class ComputerPbrTexturedComposite
{
    [VertexShader("computer-pbr-model")]
    public static ComputerMeshPayload Mesh(in ComputerMeshVertexContext context)
    {
        float4 position = context.Vertex.Position.Value;

        return new ComputerMeshPayload
        {
            Position = context.Frame.ModelViewProjection * position,
            Uv = context.Vertex.Uv,
            WorldPosition = position.xyz,
            WorldNormal = normalize(context.Vertex.WorldNormal.Value),
            Tangent = context.Vertex.Tangent
        };
    }

    [FragmentShader("computer-pbr-model")]
    public static float4 MeshFragment(in ComputerMeshFragmentContext context)
    {
        float2 uv = context.Fragment.Uv.Value;
        float4 baseColor = context.BaseColor.Sample<float2, float4>(uv);
        float4 normalSample = context.Normal.Sample<float2, float4>(uv);
        float metallic = context.Metallic.Sample<float2, float4>(uv).x;
        float roughness = max(context.Roughness.Sample<float2, float4>(uv).x, 0.04f);
        float occlusion = context.Occlusion.Sample<float2, float4>(uv).x;
        float3 emissive = context.Emissive.Sample<float2, float4>(uv).xyz;

        float3 geometricNormal = normalize(context.Fragment.WorldNormal.Value);
        float4 tangentValue = context.Fragment.Tangent.Value;
        float3 tangent = normalize(tangentValue.xyz);
        float3 bitangent = normalize(cross(geometricNormal, tangent) * tangentValue.w);
        float3 tangentNormal = normalize(normalSample.xyz * 2.0f - 1.0f);
        float3 normal = normalize(
            tangent * tangentNormal.x +
            bitangent * tangentNormal.y +
            geometricNormal * tangentNormal.z);

        float3 lightDirection = normalize(-context.Frame.LightDirection);
        float3 viewDirection = normalize(context.Frame.CameraPosition - context.Fragment.WorldPosition.Value);
        float3 halfDirection = normalize(lightDirection + viewDirection);
        float diffuse = max(dot(normal, lightDirection), 0.0f);
        float specular = max(dot(normal, halfDirection), 0.0f);
        specular = specular * specular;
        specular = specular * specular;
        specular *= max(0.1f, 1.0f - roughness);

        float3 dielectricF0 = new float3(0.04f, 0.04f, 0.04f);
        float3 f0 = dielectricF0 * (1.0f - metallic) + baseColor.xyz * metallic;
        float3 ambient = baseColor.xyz * (0.03f + 0.07f * diffuse) * occlusion;
        float3 direct = baseColor.xyz * diffuse * (1.0f - metallic) + f0 * specular;
        float3 color = ambient + direct + emissive;

        return new float4(color, baseColor.w);
    }
}
