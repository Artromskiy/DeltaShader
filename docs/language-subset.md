# Language subset
 
## MVP 0.1 target (compute-only)

Поддерживается compute-подмножество, компилирующееся через Roslyn-проход:

- Скелет entry point:
  - статический `void` метод;
  - атрибут `[ComputeShader]`;
  - поддержка `local_size_x`, `local_size_y`, `local_size_z`;
- Параметры entry point:
  - примитивы: `bool`, `int`, `uint`, `float`;
  - векторы `float2..4`, `int2..4`, `uint2..4`, `bool2..4` из `Delta.Maths`;
  - `ReadOnlyStorageBuffer<T>` и `ReadWriteStorageBuffer<T>` из `Delta.Shader.Abstractions`;
  - `T` для буферов должен быть скалярным или векторным типом из списка выше;
- `Delta.Maths` инстринсики:
  - `Delta.Maths.maths.sin`, `.cos`, `.tan`, `.dot`, `.normalize` распознаются по `ISymbol`,
    а не по имени;
  - векторы: конструкторы, `op_*` операторы и swizzle-свойства.
- `ReadOnlyStorageBuffer<T>` / `ReadWriteStorageBuffer<T>` проходят только если параметр
  помечен соответствующим атрибутом и имеет явные `set`/`binding`.
- Сетапы:
  - `double` пока недоступен (требуется feature/profiling для float64);
  - `fix` в MVP не поддерживается, вызывает диагностический error.
