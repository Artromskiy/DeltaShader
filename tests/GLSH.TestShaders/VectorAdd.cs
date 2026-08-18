using DVG.Shaders.Abstractions;

namespace DVG.Shaders.TestShaders;

public static class VectorAdd
{
        [ComputeShader(localSizeX: 32)]
        public static void Add(
            [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float>? a,
            [ReadOnlyStorageBuffer(0, 1)] ReadOnlyStorageBuffer<float>? b,
            [ReadWriteStorageBuffer(0, 2)] ReadWriteStorageBuffer<float>? outBuffer)
        {
            // Placeholder example for stage 0.1 planning.
            if (a is null || b is null || outBuffer is null)
            {
                return;
            }

            _ = a.Load(0);
            _ = b.Load(0);
            _ = outBuffer[0];
    }
}
