using System.Runtime.CompilerServices;
using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.Playground;

[Interstage]
public struct VertexPayload
{
    [Position]
    public float4 Position;
    public float2 Uv;
}

public struct FrameConstants
{
    public float2 Resolution;
    public float Time;
}

public readonly struct VertexContext
{
    [Interstage]
    public readonly VertexPayload Vertex;
    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<float4> Vertices;
    [PushConstant]
    public readonly FrameConstants Constants;
}

public readonly struct FragmentContext
{
    [Interstage]
    public readonly VertexPayload Fragment;
    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<float4> Vertices;
    [PushConstant]
    public readonly FrameConstants Constants;
}

// ============================================================================
// ДЖЕНЕРИК АБСТРАКЦИИ ДЛЯ SDF ГЕОМЕТРИИ (Unmanaged Only)
// ============================================================================

public interface ISdfShape
{
    float Evaluate(float3 p, float time);
}

public interface ISdfModifier
{
    float3 ModifySpace(float3 p, float time);
}

// ----------------------------------------------------------------------------
// ПРИМИТИВЫ (Реализации ISdfShape)
// ----------------------------------------------------------------------------

public struct SdfSphere : ISdfShape
{
    public float Radius;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Evaluate(float3 p, float time)
    {
        return maths.length(p) - Radius;
    }
}

public struct SdfBox : ISdfShape
{
    public float3 Size;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Evaluate(float3 p, float time)
    {
        float3 d = maths.abs(p) - Size;
        return maths.length(maths.max(d, 0.0f)) + maths.min(maths.max(d.x, maths.max(d.y, d.z)), 0.0f);
    }
}

// ----------------------------------------------------------------------------
// МОДИФИКАТОРЫ ПРОСТРАНСТВА (Реализации ISdfModifier)
// ----------------------------------------------------------------------------

public struct SpaceTwist : ISdfModifier
{
    public float Amount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float3 ModifySpace(float3 p, float time)
    {
        float c = maths.cos(Amount * p.y + time);
        float s = maths.sin(Amount * p.y + time);

        // Rotate around the Y axis without requiring a 2x2 matrix ABI type.
        float2 rotated = new float2(c * p.x - s * p.z, s * p.x + c * p.z);

        return new float3(rotated.x, p.y, rotated.y);
    }
}

public struct SpaceInfRepeat : ISdfModifier
{
    public float3 Period;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float3 ModifySpace(float3 p, float time)
    {
        return maths.fract((p + Period * 0.5f) / Period) * Period - Period * 0.5f;
    }
}

// ============================================================================
// ОБОБЩЕННЫЙ КОМПОЗИТОР СЦЕНЫ
// ============================================================================

// Комбинирует Модификатор и Форму на уровне типов. Полностью unmanaged.
public struct ModifiedShape<TModifier, TShape> : ISdfShape
    where TModifier : unmanaged, ISdfModifier
    where TShape : unmanaged, ISdfShape
{
    public TModifier Modifier;
    public TShape Shape;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Evaluate(float3 p, float time)
    {
        float3 transformedSpace = Modifier.ModifySpace(p, time);
        return Shape.Evaluate(transformedSpace, time);
    }
}

// Магическая структура-вычислитель реймарчинга. Принимает любую ISdfShape структуру.
public struct Raymarcher<TScene> where TScene : unmanaged, ISdfShape
{
    public TScene Scene;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float3 Render(float3 ro, float3 rd, float time)
    {
        float t = 0.0f;
        float maxDist = 20.0f;
        bool hit = false;
        float glow = 0.0f;

        for (int i = 0; i < 48; i++)
        {
            float3 p = ro + rd * t;
            float d = Scene.Evaluate(p, time);

            // Накапливаем glow-эффект для мистического неонового вида
            glow += 0.01f / (0.01f + d * d);

            if (d < 0.002f)
            {
                hit = true;
                break;
            }
            if (t > maxDist) break;

            t += d;
        }

        float3 baseColor = new float3(0.1f, 0.4f, 0.8f);
        float3 glowColor = new float3(0.9f, 0.2f, 0.6f);

        // Смешиваем базовый цвет по глубине и добавляем объемное свечение
        float3 finalColor = maths.lerp(new float3(0.02f, 0.02f, 0.05f), baseColor, maths.exp(-0.1f * t));
        return finalColor + glowColor * glow * 0.25f;
    }
}

// ============================================================================
// КОД ШЕЙДЕРА
// ============================================================================

public static class GenericShaderPipeline
{
    [VertexShader("template")]
    public static VertexPayload GenericSdfVertex(in VertexContext context)
    {
        VertexPayload output = default;
        output.Position = context.Vertex.Position;
        output.Uv = context.Vertex.Uv;
        return output;
    }

    [FragmentShader("template")]
    public static float4 GenericSdfFragment(in FragmentContext context)
    {
        float2 uv = context.Fragment.Uv * 2.0f - 1.0f;
        float aspectRatio = context.Constants.Resolution.x / context.Constants.Resolution.y;
        uv.x *= aspectRatio;

        float time = context.Constants.Time;

        // Позиция луча
        float3 ro = new float3(0.0f, 0.0f, -4.0f);
        float3 rd = maths.normalize(new float3(uv.x, uv.y, 1.5f));

        // --------------------------------------------------------------------
        // СБОРКА СЦЕНЫ НА СТАТИЧЕСКИХ ДЖЕНЕРИКАХ
        // Скручиваем (Twist) бесконечно повторяющийся (InfRepeat) куб (Box)
        // --------------------------------------------------------------------

        // 1. Создаем примитив
        SdfBox box = new SdfBox { Size = new float3(0.4f, 0.4f, 0.4f) };

        // 2. Оборачиваем в бесконечный повторитель пространства
        var repeatedBox = new ModifiedShape<SpaceInfRepeat, SdfBox>
        {
            Modifier = new SpaceInfRepeat { Period = new float3(2.5f, 2.5f, 2.5f) },
            Shape = box
        };

        // 3. Добавляем поверх закручивание пространства (Twist)
        var twistedScene = new ModifiedShape<SpaceTwist, ModifiedShape<SpaceInfRepeat, SdfBox>>
        {
            Modifier = new SpaceTwist { Amount = 1.5f * maths.sin(time * 0.5f) },
            Shape = repeatedBox
        };

        // 4. Передаем готовую иерархию типов в наш обобщенный Raymarcher
        Raymarcher<ModifiedShape<SpaceTwist, ModifiedShape<SpaceInfRepeat, SdfBox>>> pipeline =
            new Raymarcher<ModifiedShape<SpaceTwist, ModifiedShape<SpaceInfRepeat, SdfBox>>>
            {
                Scene = twistedScene
            };

        // Рендерим
        float3 sceneColor = pipeline.Render(ro, rd, time);

        return new float4(sceneColor.x, sceneColor.y, sceneColor.z, 1.0f);
    }
}
