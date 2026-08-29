using System.Diagnostics.CodeAnalysis;
using Delta.Maths;
using Delta.Shader;
using static Delta.Maths.maths;

namespace Delta.Shader.Ui;

[SuppressMessage("Design", "CA1051", Justification = "Public fields are the declared shader-visible push-constant payload.")]
[SuppressMessage("Design", "CA1815", Justification = "Field-only shader ABI record; equality is not part of the serialized layout contract.")]
public struct SolidRectangleParameters
{
    public float2 Resolution = default;
    public float4 Rect = default;
    public float4 Color = default;

    public SolidRectangleParameters()
    {
    }
}

[SuppressMessage("Design", "CA1051", Justification = "Public fields are the declared shader-visible interstage payload.")]
[SuppressMessage("Design", "CA1815", Justification = "Field-only shader ABI record; equality is not part of the serialized layout contract.")]
[Interstage]
public struct SolidRectanglePayload
{
    [Position]
    public float4 Position;
}

[SuppressMessage("Design", "CA1051", Justification = "Public fields are the declared shader-visible context ABI.")]
public readonly struct SolidRectangleVertexContext
{
    [Interstage]
    public readonly SolidRectanglePayload Vertex;

    [PushConstant]
    public readonly SolidRectangleParameters Parameters;
}

[SuppressMessage("Design", "CA1051", Justification = "Public fields are the declared shader-visible context ABI.")]
public readonly struct SolidRectangleFragmentContext
{
    [Interstage]
    public readonly SolidRectanglePayload Fragment;

    [PushConstant]
    public readonly SolidRectangleParameters Parameters;
}

[SuppressMessage("Design", "CA1051", Justification = "Public fields are the declared shader-visible push-constant payload.")]
[SuppressMessage("Design", "CA1815", Justification = "Field-only shader ABI record; equality is not part of the serialized layout contract.")]
public struct RoundedRectangleParameters
{
    public float2 Resolution = default;
    public float4 Rect = default;
    public float4 FillColor = default;
    public float4 BorderColor = default;
    public float CornerRadius = default;
    public float BorderWidth = default;

    public RoundedRectangleParameters()
    {
    }
}

[SuppressMessage("Design", "CA1051", Justification = "Public fields are the declared shader-visible interstage payload.")]
[SuppressMessage("Design", "CA1815", Justification = "Field-only shader ABI record; equality is not part of the serialized layout contract.")]
[Interstage]
public struct RoundedRectanglePayload
{
    [Position]
    public float4 Position;
    public float2 Uv;
}

[SuppressMessage("Design", "CA1051", Justification = "Public fields are the declared shader-visible context ABI.")]
public readonly struct RoundedRectangleVertexContext
{
    [Interstage]
    public readonly RoundedRectanglePayload Vertex;

    [PushConstant]
    public readonly RoundedRectangleParameters Parameters;
}

[SuppressMessage("Design", "CA1051", Justification = "Public fields are the declared shader-visible context ABI.")]
public readonly struct RoundedRectangleFragmentContext
{
    [Interstage]
    public readonly RoundedRectanglePayload Fragment;

    [PushConstant]
    public readonly RoundedRectangleParameters Parameters;
}

public static class UiRectangleShaders
{
    [VertexShader("solid-rectangle")]
    public static SolidRectanglePayload SolidRectangleVertex(in SolidRectangleVertexContext context)
    {
        uint vertexIndex = ShaderBuiltins.VertexIndex;
        float2 local = new float2(0f, 0f);
        if (vertexIndex == 1u || vertexIndex == 2u || vertexIndex == 4u)
        {
            local = new float2(1f, local.y);
        }

        if (vertexIndex == 2u || vertexIndex == 4u || vertexIndex == 5u)
        {
            local = new float2(local.x, 1f);
        }

        float2 pixel = new float2(
            context.Parameters.Rect.x + local.x * context.Parameters.Rect.z,
            context.Parameters.Rect.y + local.y * context.Parameters.Rect.w);
        float2 clip = new float2(
            pixel.x / context.Parameters.Resolution.x * 2f - 1f,
            1f - pixel.y / context.Parameters.Resolution.y * 2f);

        return new SolidRectanglePayload
        {
            Position = new float4(clip.x, clip.y, 0f, 1f)
        };
    }

    [FragmentShader("solid-rectangle")]
    public static float4 SolidRectangleFragment(in SolidRectangleFragmentContext context)
        => context.Parameters.Color;

    [VertexShader("rounded-rectangle")]
    public static RoundedRectanglePayload RoundedRectangleVertex(in RoundedRectangleVertexContext context)
    {
        uint vertexIndex = ShaderBuiltins.VertexIndex;
        float2 local = new float2(0f, 0f);
        if (vertexIndex == 1u || vertexIndex == 2u || vertexIndex == 4u)
        {
            local = new float2(1f, local.y);
        }

        if (vertexIndex == 2u || vertexIndex == 4u || vertexIndex == 5u)
        {
            local = new float2(local.x, 1f);
        }

        float2 pixel = new float2(
            context.Parameters.Rect.x + local.x * context.Parameters.Rect.z,
            context.Parameters.Rect.y + local.y * context.Parameters.Rect.w);
        float2 clip = new float2(
            pixel.x / context.Parameters.Resolution.x * 2f - 1f,
            1f - pixel.y / context.Parameters.Resolution.y * 2f);

        return new RoundedRectanglePayload
        {
            Position = new float4(clip.x, clip.y, 0f, 1f),
            Uv = local
        };
    }

    [FragmentShader("rounded-rectangle")]
    public static float4 RoundedRectangleFragment(in RoundedRectangleFragmentContext context)
    {
        float2 size = new float2(context.Parameters.Rect.z, context.Parameters.Rect.w);
        float2 pixel = context.Fragment.Uv * size;
        float2 halfSize = size * 0.5f;
        float2 centered = pixel - halfSize;
        float radius = context.Parameters.CornerRadius;
        float2 q = abs(centered) - halfSize + new float2(radius, radius);
        float2 outside = max(q, 0f);
        float outsideDistance = length(outside);
        float insideDistance = min(max(q.x, q.y), 0f);
        float distance = outsideDistance + insideDistance - radius;
        float edge = ShaderIntrinsics.fwidth(distance);
        float fillCoverage = 1f - smoothstep(-edge, edge, distance);
        float innerCoverage = 1f - smoothstep(-edge, edge, distance + context.Parameters.BorderWidth);
        float borderCoverage = max(fillCoverage - innerCoverage, 0f);

        return context.Parameters.FillColor * innerCoverage +
            context.Parameters.BorderColor * borderCoverage;
    }
}
