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

public readonly struct RoundedRectangleSliceParameters
{
	public readonly float4 FillColor;
	public readonly float4 BorderColor;
	public readonly float4 CornerRadii;
	public readonly float4 SegmentRect;
	public readonly float4 CornerData;
	public readonly float BorderWidth;

	public RoundedRectangleSliceParameters(
		float4 fillColor,
		float4 borderColor,
		float4 cornerRadii,
		float4 segmentRect,
		float4 cornerData,
		float borderWidth)
	{
		FillColor = fillColor;
		BorderColor = borderColor;
		CornerRadii = cornerRadii;
		SegmentRect = segmentRect;
		CornerData = cornerData;
		BorderWidth = borderWidth;
	}
}

public enum RoundedRectangleSliceRegion
{
	Center,
	Top,
	Right,
	Bottom,
	Left,
	TopLeft,
	TopRight,
	BottomRight,
	BottomLeft
}

public static class RoundedRectangleSliceBuilder
{
	public static int Build(
		in RoundedRectangleParameters rectangle,
		Span<RoundedRectangleSliceParameters> destination)
	{
		if (destination.Length < 9)
		{
			throw new ArgumentException("The destination must hold up to nine rounded rectangle slice records.", nameof(destination));
		}

		float width = Maths.Max(rectangle.Rect.z, 0f);
		float height = Maths.Max(rectangle.Rect.w, 0f);
		if (width <= 0f || height <= 0f)
		{
			return 0;
		}

		float4 rect = new float4(rectangle.Rect.x, rectangle.Rect.y, width, height);
		float4 radii = NormalizeRadii(rectangle.CornerRadii, width, height);
		float left = Maths.Max(radii.x, radii.w);
		float right = Maths.Max(radii.y, radii.z);
		float top = Maths.Max(radii.x, radii.y);
		float bottom = Maths.Max(radii.w, radii.z);
		float x0 = rect.x;
		float x1 = x0 + left;
		float x2 = x0 + width - right;
		float y0 = rect.y;
		float y1 = y0 + top;
		float y2 = y0 + height - bottom;
		int count = 0;

		Append(
			destination,
			ref count,
			rectangle,
			radii,
			new float4(x1, y1, x2 - x1, y2 - y1),
			new float4(0f, 0f, 0f, 0f));
		Append(
			destination,
			ref count,
			rectangle,
			radii,
			new float4(x1, y0, x2 - x1, top),
			new float4(0f, 0f, 0f, 0f));
		Append(
			destination,
			ref count,
			rectangle,
			radii,
			new float4(x2, y1, right, y2 - y1),
			new float4(0f, 0f, 0f, 0f));
		Append(
			destination,
			ref count,
			rectangle,
			radii,
			new float4(x1, y2, x2 - x1, bottom),
			new float4(0f, 0f, 0f, 0f));
		Append(
			destination,
			ref count,
			rectangle,
			radii,
			new float4(x0, y1, left, y2 - y1),
			new float4(0f, 0f, 0f, 0f));
		AppendCorner(destination, ref count, rectangle, radii, new float4(x0, y0, left, top), x1, y1, radii.x);
		AppendCorner(destination, ref count, rectangle, radii, new float4(x2, y0, right, top), x2, y1, radii.y);
		AppendCorner(destination, ref count, rectangle, radii, new float4(x2, y2, right, bottom), x2, y2, radii.z);
		AppendCorner(destination, ref count, rectangle, radii, new float4(x0, y2, left, bottom), x1, y2, radii.w);
		return count;
	}

	public static int BuildClipAware(
		in RoundedRectangleParameters rectangle,
		float4 clipRect,
		Span<ClipAwareRoundedRectangleSliceParameters> destination)
	{
		if (destination.Length < 9)
		{
			throw new ArgumentException("The destination must hold up to nine clip-aware rounded rectangle slice records.", nameof(destination));
		}

		Span<RoundedRectangleSliceParameters> slices = stackalloc RoundedRectangleSliceParameters[9];
		int count = Build(in rectangle, slices);
		for (int index = 0; index < count; index++)
		{
			RoundedRectangleSliceParameters slice = slices[index];
			destination[index] = new ClipAwareRoundedRectangleSliceParameters(
				slice.FillColor,
				slice.BorderColor,
				slice.CornerRadii,
				slice.SegmentRect,
				slice.CornerData,
				slice.BorderWidth,
				clipRect);
		}

		return count;
	}

	private static float4 NormalizeRadii(float4 radii, float width, float height)
	{
		radii = new float4(
			Maths.Max(radii.x, 0f),
			Maths.Max(radii.y, 0f),
			Maths.Max(radii.z, 0f),
			Maths.Max(radii.w, 0f));
		float scale = 1f;
		scale = LimitScale(scale, width, radii.x + radii.y);
		scale = LimitScale(scale, width, radii.w + radii.z);
		scale = LimitScale(scale, height, radii.x + radii.w);
		scale = LimitScale(scale, height, radii.y + radii.z);
		return radii * scale;
	}

	private static float LimitScale(float scale, float extent, float sum)
		=> sum > 0f ? Maths.Min(scale, extent / sum) : scale;

	private static void AppendCorner(
		Span<RoundedRectangleSliceParameters> destination,
		ref int count,
		in RoundedRectangleParameters rectangle,
		float4 radii,
		float4 segmentRect,
		float centerX,
		float centerY,
		float radius)
	{
		Append(
			destination,
			ref count,
			rectangle,
			radii,
			segmentRect,
			new float4(centerX, centerY, radius, radius > 0f ? 1f : 0f));
	}

	private static void Append(
		Span<RoundedRectangleSliceParameters> destination,
		ref int count,
		in RoundedRectangleParameters rectangle,
		float4 radii,
		float4 segmentRect,
		float4 cornerData)
	{
		if (segmentRect.z <= 0f || segmentRect.w <= 0f)
		{
			return;
		}

		destination[count++] = new RoundedRectangleSliceParameters(
			rectangle.FillColor,
			rectangle.BorderColor,
			radii,
			segmentRect,
			cornerData,
			Maths.Max(rectangle.BorderWidth, 0f));
	}
}

public readonly struct ClipAwareSolidRectangleParameters
{
	public readonly float4 Rect;
	public readonly float4 Color;
	public readonly float4 ClipRect;

	public ClipAwareSolidRectangleParameters(float4 rect, float4 color, float4 clipRect)
	{
		Rect = rect;
		Color = color;
		ClipRect = clipRect;
	}
}

[Interstage]
public struct ClipAwareSolidRectanglePayload
{
	public Position Position;
	public Color Color;
	public ClipRect ClipRect;
}

public readonly struct ClipAwareSolidRectangleVertexContext
{
	[Interstage]
	public readonly ClipAwareSolidRectanglePayload Vertex;

	[Layout(0, 0)]
	public readonly ReadOnlyStorageBuffer<ClipAwareSolidRectangleParameters> Instances;

	[PushConstant]
	public readonly UiFrameConstants Frame;
}

public readonly struct ClipAwareSolidRectangleFragmentContext
{
	[Interstage]
	public readonly ClipAwareSolidRectanglePayload Fragment;
}

public readonly struct ClipAwareRoundedRectangleParameters
{
	public readonly float4 Rect;
	public readonly float4 FillColor;
	public readonly float4 BorderColor;
	public readonly float4 CornerRadii;
	public readonly float BorderWidth;
	public readonly float4 ClipRect;

	public ClipAwareRoundedRectangleParameters(
		float4 rect,
		float4 fillColor,
		float4 borderColor,
		float4 cornerRadii,
		float borderWidth,
		float4 clipRect)
	{
		Rect = rect;
		FillColor = fillColor;
		BorderColor = borderColor;
		CornerRadii = cornerRadii;
		BorderWidth = borderWidth;
		ClipRect = clipRect;
	}
}

[Interstage]
public struct ClipAwareRoundedRectanglePayload
{
	public Position Position;
	public Uv0 Uv;
	public Color Rect;
	public VertexColor FillColor;
	public FragmentColor BorderColor;
	public CornerRadii CornerRadii;
	public BorderWidth BorderWidth;
	public ClipRect ClipRect;
}

public readonly struct ClipAwareRoundedRectangleVertexContext
{
	[Interstage]
	public readonly ClipAwareRoundedRectanglePayload Vertex;

	[Layout(0, 0)]
	public readonly ReadOnlyStorageBuffer<ClipAwareRoundedRectangleParameters> Instances;

	[PushConstant]
	public readonly UiFrameConstants Frame;
}

public readonly struct ClipAwareRoundedRectangleFragmentContext
{
	[Interstage]
	public readonly ClipAwareRoundedRectanglePayload Fragment;
}

public readonly struct ClipAwareRoundedRectangleSliceParameters
{
	public readonly float4 FillColor;
	public readonly float4 BorderColor;
	public readonly float4 CornerRadii;
	public readonly float4 SegmentRect;
	public readonly float4 CornerData;
	public readonly float BorderWidth;
	public readonly float4 ClipRect;

	public ClipAwareRoundedRectangleSliceParameters(
		float4 fillColor,
		float4 borderColor,
		float4 cornerRadii,
		float4 segmentRect,
		float4 cornerData,
		float borderWidth,
		float4 clipRect)
	{
		FillColor = fillColor;
		BorderColor = borderColor;
		CornerRadii = cornerRadii;
		SegmentRect = segmentRect;
		CornerData = cornerData;
		BorderWidth = borderWidth;
		ClipRect = clipRect;
	}
}

[Interstage]
public struct ClipAwareRoundedRectangleSlicePayload
{
	public Position Position;
	public Pixel Pixel;
	public VertexColor FillColor;
	public FragmentColor BorderColor;
	public SegmentRect SegmentRect;
	public CornerData CornerData;
	public BorderWidth BorderWidth;
	public ClipRect ClipRect;
}

public readonly struct ClipAwareRoundedRectangleSliceVertexContext
{
	[Interstage]
	public readonly ClipAwareRoundedRectangleSlicePayload Vertex;

	[Layout(0, 0)]
	public readonly ReadOnlyStorageBuffer<ClipAwareRoundedRectangleSliceParameters> Instances;

	[PushConstant]
	public readonly UiFrameConstants Frame;
}

public readonly struct ClipAwareRoundedRectangleSliceFragmentContext
{
	[Interstage]
	public readonly ClipAwareRoundedRectangleSlicePayload Fragment;
}


[Interstage]
public struct RoundedRectanglePayload
{
	public Position Position;
	public Uv0 Uv;
	public Color Rect;
	public VertexColor FillColor;
	public FragmentColor BorderColor;
	public CornerRadii CornerRadii;
	public BorderWidth BorderWidth;
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

[Interstage]
public struct RoundedRectangleSlicePayload
{
	public Position Position;
	public Pixel Pixel;
	public VertexColor FillColor;
	public FragmentColor BorderColor;
	public SegmentRect SegmentRect;
	public CornerData CornerData;
	public BorderWidth BorderWidth;
}

public readonly struct RoundedRectangleSliceVertexContext
{
	[Interstage]
	public readonly RoundedRectangleSlicePayload Vertex;

	[Layout(0, 0)]
	public readonly ReadOnlyStorageBuffer<RoundedRectangleSliceParameters> Instances;

	[PushConstant]
	public readonly UiFrameConstants Frame;
}

public readonly struct RoundedRectangleSliceFragmentContext
{
	[Interstage]
	public readonly RoundedRectangleSlicePayload Fragment;
}

public static class UiRectangleShaders
{
	private static bool IsInsideClip(ClipRect clip)
	{
		float pixelX = ShaderBuiltins.FragmentCoord.X;
		float pixelY = ShaderBuiltins.FragmentCoord.Y;
		return pixelX >= clip.Value.x &&
			pixelY >= clip.Value.y &&
			pixelX < clip.Value.x + clip.Value.z &&
			pixelY < clip.Value.y + clip.Value.w;
	}

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
			pixel.y / context.Frame.Resolution.y * 2f - 1f);

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
			pixel.y / context.Frame.Resolution.y * 2f - 1f);

		return new RoundedRectanglePayload
		{
			Position = new float4(clip.x, clip.y, 0f, 1f),
			Uv = new Uv0(local),
			Rect = new Color(instance.Rect),
			FillColor = new VertexColor(instance.FillColor),
			BorderColor = new FragmentColor(instance.BorderColor),
			CornerRadii = new CornerRadii(instance.CornerRadii),
			BorderWidth = new BorderWidth(instance.BorderWidth)
		};
	}

	[FragmentShader("rounded-rectangle")]
	public static float4 RoundedRectangleFragment(in RoundedRectangleFragmentContext context)
	{
		float4 rect = context.Fragment.Rect.Value;
		float2 size = new float2(rect.z, rect.w);
		float4 cornerRadii = context.Fragment.CornerRadii.Value;
		float borderWidth = context.Fragment.BorderWidth.Value;
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
		float edge = fwidth(distance);
		float fillCoverage = 1f - smoothstep(-edge, edge, distance);
		if (fillCoverage <= 0f)
		{
			discard();
		}
		float innerCoverage = 1f - smoothstep(-edge, edge, distance + borderWidth);
		float borderCoverage = max(fillCoverage - innerCoverage, 0f);

		return context.Fragment.FillColor.Value * innerCoverage +
			context.Fragment.BorderColor.Value * borderCoverage;
	}

	[VertexShader("rounded-rectangle-slice")]
	public static RoundedRectangleSlicePayload RoundedRectangleSliceVertex(in RoundedRectangleSliceVertexContext context)
	{
		RoundedRectangleSliceParameters instance = context.Instances[ShaderBuiltins.InstanceIndex];
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
			instance.SegmentRect.x + local.x * instance.SegmentRect.z,
			instance.SegmentRect.y + local.y * instance.SegmentRect.w);
		float2 clip = new float2(
			pixel.x / context.Frame.Resolution.x * 2f - 1f,
			pixel.y / context.Frame.Resolution.y * 2f - 1f);

		return new RoundedRectangleSlicePayload
		{
			Position = new float4(clip.x, clip.y, 0f, 1f),
			Pixel = new Pixel(pixel),
			FillColor = new VertexColor(instance.FillColor),
			BorderColor = new FragmentColor(instance.BorderColor),
			SegmentRect = new SegmentRect(instance.SegmentRect),
			CornerData = new CornerData(instance.CornerData),
			BorderWidth = new BorderWidth(instance.BorderWidth)
		};
	}

	[FragmentShader("rounded-rectangle-slice")]
	public static float4 RoundedRectangleSliceFragment(in RoundedRectangleSliceFragmentContext context)
	{
		float4 cornerData = context.Fragment.CornerData.Value;
		float4 segmentRect = context.Fragment.SegmentRect.Value;
		float borderWidth = context.Fragment.BorderWidth.Value;
		float2 pixel = context.Fragment.Pixel.Value;
		float isCorner = cornerData.w;
		float distance = 0f;
		if (isCorner > 0.5f)
		{
			distance = length(pixel - new float2(cornerData.x, cornerData.y)) - cornerData.z;
		}
		else
		{
			float left = segmentRect.x - pixel.x;
			float right = pixel.x - (segmentRect.x + segmentRect.z);
			float top = segmentRect.y - pixel.y;
			float bottom = pixel.y - (segmentRect.y + segmentRect.w);
			distance = max(max(left, right), max(top, bottom));
		}

		float edge = fwidth(distance);
		float fillCoverage = 1f - smoothstep(-edge, edge, distance);
		if (fillCoverage <= 0f)
		{
			discard();
		}
		float innerDistance = 0f;
		if (isCorner > 0.5f)
		{
			float innerRadius = max(cornerData.z - borderWidth, 0f);
			innerDistance = length(pixel - new float2(cornerData.x, cornerData.y)) - innerRadius;
		}
		else
		{
			float left = segmentRect.x + borderWidth - pixel.x;
			float right = pixel.x - (segmentRect.x + segmentRect.z - borderWidth);
			float top = segmentRect.y + borderWidth - pixel.y;
			float bottom = pixel.y - (segmentRect.y + segmentRect.w - borderWidth);
			innerDistance = max(max(left, right), max(top, bottom));
		}

		float innerCoverage = 1f - smoothstep(-edge, edge, innerDistance);
		float borderCoverage = max(fillCoverage - innerCoverage, 0f);
		return context.Fragment.FillColor.Value * innerCoverage +
			context.Fragment.BorderColor.Value * borderCoverage;
	}

	[VertexShader("clip-aware-solid-rectangle")]
	public static ClipAwareSolidRectanglePayload ClipAwareSolidRectangleVertex(in ClipAwareSolidRectangleVertexContext context)
	{
		ClipAwareSolidRectangleParameters instance = context.Instances[ShaderBuiltins.InstanceIndex];
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
			pixel.y / context.Frame.Resolution.y * 2f - 1f);

		return new ClipAwareSolidRectanglePayload
		{
			Position = new Position(new float4(clip.x, clip.y, 0f, 1f)),
			Color = new Color(instance.Color),
			ClipRect = new ClipRect(instance.ClipRect)
		};
	}

	[FragmentShader("clip-aware-solid-rectangle")]
	public static float4 ClipAwareSolidRectangleFragment(in ClipAwareSolidRectangleFragmentContext context)
	{
		if (!IsInsideClip(context.Fragment.ClipRect))
		{
			discard();
		}

		return context.Fragment.Color.Value;
	}

	[VertexShader("clip-aware-rounded-rectangle")]
	public static ClipAwareRoundedRectanglePayload ClipAwareRoundedRectangleVertex(in ClipAwareRoundedRectangleVertexContext context)
	{
		ClipAwareRoundedRectangleParameters instance = context.Instances[ShaderBuiltins.InstanceIndex];
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
			pixel.y / context.Frame.Resolution.y * 2f - 1f);

		return new ClipAwareRoundedRectanglePayload
		{
			Position = new Position(new float4(clip.x, clip.y, 0f, 1f)),
			Uv = new Uv0(local),
			Rect = new Color(instance.Rect),
			FillColor = new VertexColor(instance.FillColor),
			BorderColor = new FragmentColor(instance.BorderColor),
			CornerRadii = new CornerRadii(instance.CornerRadii),
			BorderWidth = new BorderWidth(instance.BorderWidth),
			ClipRect = new ClipRect(instance.ClipRect)
		};
	}

	[FragmentShader("clip-aware-rounded-rectangle")]
	public static float4 ClipAwareRoundedRectangleFragment(in ClipAwareRoundedRectangleFragmentContext context)
	{
		if (!IsInsideClip(context.Fragment.ClipRect))
		{
			discard();
		}

		float4 rect = context.Fragment.Rect.Value;
		float2 size = new float2(rect.z, rect.w);
		float4 cornerRadii = context.Fragment.CornerRadii.Value;
		float borderWidth = context.Fragment.BorderWidth.Value;
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
		float edge = fwidth(distance);
		float fillCoverage = 1f - smoothstep(-edge, edge, distance);
		if (fillCoverage <= 0f)
		{
			discard();
		}
		float innerCoverage = 1f - smoothstep(-edge, edge, distance + borderWidth);
		float borderCoverage = max(fillCoverage - innerCoverage, 0f);

		return context.Fragment.FillColor.Value * innerCoverage +
			context.Fragment.BorderColor.Value * borderCoverage;
	}

	[VertexShader("clip-aware-rounded-rectangle-slice")]
	public static ClipAwareRoundedRectangleSlicePayload ClipAwareRoundedRectangleSliceVertex(in ClipAwareRoundedRectangleSliceVertexContext context)
	{
		ClipAwareRoundedRectangleSliceParameters instance = context.Instances[ShaderBuiltins.InstanceIndex];
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
			instance.SegmentRect.x + local.x * instance.SegmentRect.z,
			instance.SegmentRect.y + local.y * instance.SegmentRect.w);
		float2 clip = new float2(
			pixel.x / context.Frame.Resolution.x * 2f - 1f,
			pixel.y / context.Frame.Resolution.y * 2f - 1f);

		return new ClipAwareRoundedRectangleSlicePayload
		{
			Position = new Position(new float4(clip.x, clip.y, 0f, 1f)),
			Pixel = new Pixel(pixel),
			FillColor = new VertexColor(instance.FillColor),
			BorderColor = new FragmentColor(instance.BorderColor),
			SegmentRect = new SegmentRect(instance.SegmentRect),
			CornerData = new CornerData(instance.CornerData),
			BorderWidth = new BorderWidth(instance.BorderWidth),
			ClipRect = new ClipRect(instance.ClipRect)
		};
	}

	[FragmentShader("clip-aware-rounded-rectangle-slice")]
	public static float4 ClipAwareRoundedRectangleSliceFragment(in ClipAwareRoundedRectangleSliceFragmentContext context)
	{
		if (!IsInsideClip(context.Fragment.ClipRect))
		{
			discard();
		}

		float4 cornerData = context.Fragment.CornerData.Value;
		float4 segmentRect = context.Fragment.SegmentRect.Value;
		float borderWidth = context.Fragment.BorderWidth.Value;
		float2 pixel = context.Fragment.Pixel.Value;
		float isCorner = cornerData.w;
		float distance = 0f;
		if (isCorner > 0.5f)
		{
			distance = length(pixel - new float2(cornerData.x, cornerData.y)) - cornerData.z;
		}
		else
		{
			float left = segmentRect.x - pixel.x;
			float right = pixel.x - (segmentRect.x + segmentRect.z);
			float top = segmentRect.y - pixel.y;
			float bottom = pixel.y - (segmentRect.y + segmentRect.w);
			distance = max(max(left, right), max(top, bottom));
		}

		float edge = fwidth(distance);
		float fillCoverage = 1f - smoothstep(-edge, edge, distance);
		if (fillCoverage <= 0f)
		{
			discard();
		}
		float innerDistance = 0f;
		if (isCorner > 0.5f)
		{
			float innerRadius = max(cornerData.z - borderWidth, 0f);
			innerDistance = length(pixel - new float2(cornerData.x, cornerData.y)) - innerRadius;
		}
		else
		{
			float left = segmentRect.x + borderWidth - pixel.x;
			float right = pixel.x - (segmentRect.x + segmentRect.z - borderWidth);
			float top = segmentRect.y + borderWidth - pixel.y;
			float bottom = pixel.y - (segmentRect.y + segmentRect.w - borderWidth);
			innerDistance = max(max(left, right), max(top, bottom));
		}

		float innerCoverage = 1f - smoothstep(-edge, edge, innerDistance);
		float borderCoverage = max(fillCoverage - innerCoverage, 0f);
		return context.Fragment.FillColor.Value * innerCoverage +
			context.Fragment.BorderColor.Value * borderCoverage;
	}
}
