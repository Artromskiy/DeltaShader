using Delta.Maths;
using Delta.Shader;
using static Delta.Maths.maths;

namespace Delta.Shader.UI;

public readonly struct UiFrameConstants
{
    public readonly float2 Resolution;

    public UiFrameConstants(float2 resolution)
    {
        Resolution = resolution;
    }
}

public readonly struct SolidRectangleParameters
{
    public readonly float4 Rect;
    public readonly float4 Color;

    public SolidRectangleParameters(float4 rect, float4 color)
    {
        Rect = rect;
        Color = color;
    }
}

[Interstage]
public struct SolidRectanglePayload
{
    public Position Position;
    public Color Color;
}

public readonly struct SolidRectangleVertexContext
{
    [Interstage]
    public readonly SolidRectanglePayload Vertex;

    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<SolidRectangleParameters> Instances;

    [PushConstant]
    public readonly UiFrameConstants Frame;
}

public readonly struct SolidRectangleFragmentContext
{
    [Interstage]
    public readonly SolidRectanglePayload Fragment;
}

public readonly struct RoundedRectangleParameters
{
    public readonly float4 Rect;
    public readonly float4 FillColor;
    public readonly float4 BorderColor;
    public readonly float4 CornerRadii;
    public readonly float BorderWidth;

    public RoundedRectangleParameters(
        float4 rect,
        float4 fillColor,
        float4 borderColor,
        float4 cornerRadii,
        float borderWidth)
    {
        Rect = rect;
        FillColor = fillColor;
        BorderColor = borderColor;
        CornerRadii = cornerRadii;
        BorderWidth = borderWidth;
    }
}


[Interstage]
public struct RoundedRectanglePayload
{
    public Position Position;
    public Uv0 Uv;
    public Color Rect;
    public VertexColor FillColor;
    public FragmentColor BorderColor;
    public Tangent CornerRadii;
    public Uv1 BorderWidth;
}

public readonly struct RoundedRectangleVertexContext
{
    [Interstage]
    public readonly RoundedRectanglePayload Vertex;

    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<RoundedRectangleParameters> Instances;

    [PushConstant]
    public readonly UiFrameConstants Frame;
}

public readonly struct RoundedRectangleFragmentContext
{
    [Interstage]
    public readonly RoundedRectanglePayload Fragment;
}

public static class UiRectangleShaders
{
    [VertexShader("solid-rectangle")]
    public static SolidRectanglePayload SolidRectangleVertex(in SolidRectangleVertexContext context)
    {
        SolidRectangleParameters instance = context.Instances[ShaderBuiltins.InstanceIndex];
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
            instance.Rect.x + local.x * instance.Rect.z,
            instance.Rect.y + local.y * instance.Rect.w);
        float2 clip = new float2(
            pixel.x / context.Frame.Resolution.x * 2f - 1f,
            1f - pixel.y / context.Frame.Resolution.y * 2f);

        return new SolidRectanglePayload
        {
            Position = new float4(clip.x, clip.y, 0f, 1f),
            Color = new Color(instance.Color)
        };
    }

    [FragmentShader("solid-rectangle")]
    public static float4 SolidRectangleFragment(in SolidRectangleFragmentContext context)
        => context.Fragment.Color.Value;

    [VertexShader("rounded-rectangle")]
    public static RoundedRectanglePayload RoundedRectangleVertex(in RoundedRectangleVertexContext context)
    {
        RoundedRectangleParameters instance = context.Instances[ShaderBuiltins.InstanceIndex];
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
            instance.Rect.x + local.x * instance.Rect.z,
            instance.Rect.y + local.y * instance.Rect.w);
        float2 clip = new float2(
            pixel.x / context.Frame.Resolution.x * 2f - 1f,
            1f - pixel.y / context.Frame.Resolution.y * 2f);

        return new RoundedRectanglePayload
        {
            Position = new float4(clip.x, clip.y, 0f, 1f),
            Uv = new Uv0(local),
            Rect = new Color(instance.Rect),
            FillColor = new VertexColor(instance.FillColor),
            BorderColor = new FragmentColor(instance.BorderColor),
            CornerRadii = new Tangent(instance.CornerRadii),
            BorderWidth = new Uv1(new float2(instance.BorderWidth, 0f))
        };
    }

    [FragmentShader("rounded-rectangle")]
    public static float4 RoundedRectangleFragment(in RoundedRectangleFragmentContext context)
    {
        float4 rect = context.Fragment.Rect.Value;
        float2 size = new float2(rect.z, rect.w);
        float4 cornerRadii = context.Fragment.CornerRadii.Value;
        float borderWidth = context.Fragment.BorderWidth.Value.x;
        float2 pixel = context.Fragment.Uv.Value * size;
        float2 halfSize = size * 0.5f;
        float2 centered = pixel - halfSize;
        float radius = cornerRadii.x;
        if (centered.x > 0f)
        {
            if (centered.y > 0f)
            {
                radius = cornerRadii.z;
            }
            else
            {
                radius = cornerRadii.y;
            }
        }
        else if (centered.y > 0f)
        {
            radius = cornerRadii.w;
        }

        float2 q = abs(centered) - halfSize + new float2(radius, radius);
        float2 outside = max(q, 0f);
        float outsideDistance = length(outside);
        float insideDistance = min(max(q.x, q.y), 0f);
        float distance = outsideDistance + insideDistance - radius;
        float edge = ShaderIntrinsics.fwidth(distance);
        float fillCoverage = 1f - smoothstep(-edge, edge, distance);
        float innerCoverage = 1f - smoothstep(-edge, edge, distance + borderWidth);
        float borderCoverage = max(fillCoverage - innerCoverage, 0f);

        return context.Fragment.FillColor.Value * innerCoverage +
            context.Fragment.BorderColor.Value * borderCoverage;
    }
}
