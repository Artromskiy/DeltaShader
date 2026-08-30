using Delta.Maths;

namespace Delta.Shader;

// Implicit conversions are the intentional ergonomic boundary for shader semantic wrappers.
#pragma warning disable CA2225
/// <summary>Vertex clip-space position and the required vertex position semantic.</summary>
public readonly struct Position
{
    public readonly float4 Value;

    public Position(float4 value)
    {
        Value = value;
    }

    public static implicit operator Position(float4 value) => new(value);

    public static implicit operator float4(Position value) => value.Value;
}

/// <summary>First texture-coordinate semantic passed between graphics stages.</summary>
public readonly struct Uv0
{
    public readonly float2 Value;

    public Uv0(float2 value)
    {
        Value = value;
    }

    public static implicit operator Uv0(float2 value) => new(value);

    public static implicit operator float2(Uv0 value) => value.Value;
}

/// <summary>Second texture-coordinate semantic passed between graphics stages.</summary>
public readonly struct Uv1
{
    public readonly float2 Value;

    public Uv1(float2 value)
    {
        Value = value;
    }

    public static implicit operator Uv1(float2 value) => new(value);

    public static implicit operator float2(Uv1 value) => value.Value;
}

/// <summary>Generic final color semantic for a graphics composite.</summary>
public readonly struct Color
{
    public readonly float4 Value;

    public Color(float4 value)
    {
        Value = value;
    }

    public static implicit operator Color(float4 value) => new(value);

    public static implicit operator float4(Color value) => value.Value;
}

/// <summary>Color supplied by a vertex-stage layer.</summary>
public readonly struct VertexColor
{
    public readonly float4 Value;

    public VertexColor(float4 value)
    {
        Value = value;
    }

    public static implicit operator VertexColor(float4 value) => new(value);

    public static implicit operator float4(VertexColor value) => value.Value;
}

/// <summary>Color supplied or changed by a fragment-stage layer.</summary>
public readonly struct FragmentColor
{
    public readonly float4 Value;

    public FragmentColor(float4 value)
    {
        Value = value;
    }

    public static implicit operator FragmentColor(float4 value) => new(value);

    public static implicit operator float4(FragmentColor value) => value.Value;
}

/// <summary>World-space position semantic.</summary>
public readonly struct WorldPosition
{
    public readonly float3 Value;

    public WorldPosition(float3 value)
    {
        Value = value;
    }

    public static implicit operator WorldPosition(float3 value) => new(value);

    public static implicit operator float3(WorldPosition value) => value.Value;
}

/// <summary>World-space normal semantic.</summary>
public readonly struct WorldNormal
{
    public readonly float3 Value;

    public WorldNormal(float3 value)
    {
        Value = value;
    }

    public static implicit operator WorldNormal(float3 value) => new(value);

    public static implicit operator float3(WorldNormal value) => value.Value;
}

/// <summary>Tangent-frame semantic.</summary>
public readonly struct Tangent
{
    public readonly float4 Value;

    public Tangent(float4 value)
    {
        Value = value;
    }

    public static implicit operator Tangent(float4 value) => new(value);

    public static implicit operator float4(Tangent value) => value.Value;
}
#pragma warning restore CA2225
