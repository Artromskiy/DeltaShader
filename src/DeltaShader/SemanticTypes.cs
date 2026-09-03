using Delta;

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

/// <summary>Fragment color semantic and required terminal fragment output.</summary>
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
#pragma warning restore CA2225
