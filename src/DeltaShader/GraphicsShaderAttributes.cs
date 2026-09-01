namespace Delta.Shader;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class VertexShaderAttribute : Attribute
{
	public string? EntryPointName { get; }

	public VertexShaderAttribute(string? entryPointName = null)
	{
		EntryPointName = entryPointName;
	}
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class FragmentShaderAttribute : Attribute
{
	public string? EntryPointName { get; }

	public FragmentShaderAttribute(string? entryPointName = null)
	{
		EntryPointName = entryPointName;
	}
}

public enum VertexInputRate
{
	Vertex = 0,
	Instance = 1
}

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class InterstageAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class ShaderIntrinsicAttribute : Attribute
{
	public string GlslName { get; }
	public ShaderStage[] Stages { get; }
	public ShaderStage Stage { get; }

	public ShaderIntrinsicAttribute(string glslName, ShaderStage stage)
		: this(glslName, new[] { stage })
	{
	}

	public ShaderIntrinsicAttribute(string glslName, params ShaderStage[] stages)
	{
#if NET10_0_OR_GREATER
		ArgumentNullException.ThrowIfNull(stages);
#else
        if (stages is null)
        {
            throw new ArgumentNullException(nameof(stages));
        }
#endif

		GlslName = glslName;
		if (stages.Length == 0)
		{
			throw new ArgumentException("At least one shader stage is required.", nameof(stages));
		}

		Stages = stages;
		Stage = stages[0];
	}
}

public static class intrinsics
{
	[ShaderIntrinsic("fwidth", ShaderStage.Fragment)]
	public static float fwidth(float value) => throw new NotSupportedException();

	[ShaderIntrinsic("dFdx", ShaderStage.Fragment)]
	public static T ddx<T>(T value) => throw new NotSupportedException();

	[ShaderIntrinsic("dFdy", ShaderStage.Fragment)]
	public static T ddy<T>(T value) => throw new NotSupportedException();

	[ShaderIntrinsic("discard", ShaderStage.Fragment)]
	public static bool discard => throw new NotSupportedException();
}
