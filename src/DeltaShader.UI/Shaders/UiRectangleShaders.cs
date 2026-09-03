using Delta.Maths;
using Delta.Shader;
using static Delta.Maths.maths;
using static Delta.Shader.intrinsics;

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
    public VertexColor Color;
}

public readonly struct SolidRectangleVertexContext
{[Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<SolidRectangleParameters> Instances;

    [PushConstant]
    public readonly UiFrameConstants Frame;
}

public readonly struct SolidRectangleFragmentContext
{}

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
    public SegmentRect Rect;
    public VertexColor FillColor;
    public FragmentColor BorderColor;
    public CornerRadii CornerRadii;
    public BorderWidth BorderWidth;
}

public readonly struct RoundedRectangleVertexContext
{[Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<RoundedRectangleParameters> Instances;

    [PushConstant]
    public readonly UiFrameConstants Frame;
}

public readonly struct RoundedRectangleFragmentContext
{}

public static class UiRectangleShaders
{
    private static float2 GetQuadLocal(uint vertexIndex)
    {
        float2 local = new float2(0f, 0f);
        if (vertexIndex == 1u || vertexIndex == 2u || vertexIndex == 4u)
        {
            local = new float2(1f, local.y);
        }

        if (vertexIndex == 2u || vertexIndex == 4u || vertexIndex == 5u)
        {
            local = new float2(local.x, 1f);
        }

        return local;
    }

    private static float2 ToClipPosition(float4 rect, float2 local, float2 resolution)
    {
        float2 pixel = rect.xy + local.xy * rect.zw;
        return pixel / resolution * 2f - 1f;
    }

    private static float GetCornerRadius(float4 cornerRadii, float2 centered)
    {
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

        return radius;
    }

    [VertexShader("solid-rectangle")]
    public static SolidRectanglePayload SolidRectangleVertex(in SolidRectangleVertexContext context, in SolidRectanglePayload input)
    {
        SolidRectangleParameters instance = context.Instances[ShaderBuiltins.InstanceIndex];
        float2 local = GetQuadLocal(ShaderBuiltins.VertexIndex);
        float2 clip = ToClipPosition(instance.Rect, local, context.Frame.Resolution);

        return new SolidRectanglePayload
        {
            Position = new float4(clip.x, clip.y, 0f, 1f),
            Color = new VertexColor(instance.Color)
        };
    }

    [FragmentShader("solid-rectangle")]
    public static float4 SolidRectangleFragment(in SolidRectangleFragmentContext context, in SolidRectanglePayload input)
    {
        float4 color = input.Color.Value;
        return new float4(color.xyz * color.w, color.w);
    }

    [VertexShader("rounded-rectangle")]
    public static RoundedRectanglePayload RoundedRectangleVertex(in RoundedRectangleVertexContext context, in RoundedRectanglePayload input)
    {
        RoundedRectangleParameters instance = context.Instances[ShaderBuiltins.InstanceIndex];
        float2 local = GetQuadLocal(ShaderBuiltins.VertexIndex);
        float2 clip = ToClipPosition(instance.Rect, local, context.Frame.Resolution);

        return new RoundedRectanglePayload
        {
            Position = new float4(clip.x, clip.y, 0f, 1f),
            Uv = new Uv0(local),
            Rect = new SegmentRect(instance.Rect),
            FillColor = new VertexColor(instance.FillColor),
            BorderColor = new FragmentColor(instance.BorderColor),
            CornerRadii = new CornerRadii(instance.CornerRadii),
            BorderWidth = new BorderWidth(instance.BorderWidth)
        };
    }

    [FragmentShader("rounded-rectangle")]
    public static float4 RoundedRectangleFragment(in RoundedRectangleFragmentContext context, in RoundedRectanglePayload input)
    {
        float4 rect = input.Rect.Value;
        float2 size = rect.zw;
        float4 cornerRadii = input.CornerRadii.Value;
        float borderWidth = input.BorderWidth.Value;
        float2 pixel = input.Uv.Value * size;
        float2 halfSize = size * 0.5f;
        float2 centered = pixel - halfSize;
        float radius = GetCornerRadius(cornerRadii, centered);

        float2 q = abs(centered) - halfSize + radius;
        float2 outside = max(q, 0f);
        float outsideDistance = length(outside);
        float insideDistance = min(max(q.x, q.y), 0f);
        float distance = outsideDistance + insideDistance - radius;
        float edge = max(fwidth(distance), 0.0001f);
        float outerCoverage = 1f - smoothstep(-edge, edge, distance);
        if (outerCoverage <= 0f)
        {
            _ = discard;
        }

        float innerCoverage = 1f - smoothstep(-edge, edge, distance + borderWidth);
        float borderCoverage = max(outerCoverage - innerCoverage, 0f);
        float4 premultipliedColor =
            input.FillColor.Value * innerCoverage +
            input.BorderColor.Value * borderCoverage;

        return new float4(premultipliedColor.xyz, outerCoverage);
    }

}
