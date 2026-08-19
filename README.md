# Delta.Shader

Delta.Shader — планируемый компилятор ограниченного подмножества C# в шейдеры для
Vulkan. Пользователь пишет обычные статические C#-методы, использует векторы и
shader-like API из `Delta.Maths`, а Delta.Shader проверяет код через Roslyn и выпускает
SPIR-V вместе с описанием ресурсов шейдера.

Этот каталог пока содержит только технический план. Код следует писать
поэтапно, не создавая заранее пустые проекты для поздних backend-ов.

## Текущий статус реализации

В каталоге уже есть рабочий автономный срез: Roslyn frontend и IR проверяют
поддерживаемые compute entry points, GLSL backend выпускает Vulkan GLSL 460,
а тесты прогоняют результат через `glslangValidator` и `spirv-val`. Compiler и
analyzer-safe код не зависят от Delta.Maths на этапе компиляции; fixture/tests
подключают Maths отдельно через Roslyn metadata.

Полный 0.1 ещё не заявлен: lowering тела shader-метода, manifest/reflection и
headless Vulkan dispatch остаются следующими этапами. Поэтому текущий зелёный
gate доказывает C# metadata/validation -> IR -> GLSL -> SPIR-V validation, но не
исполнение shader на GPU.

### Канонический ABI storage data

Delta.Shader использует только `std430` для storage/shared structured data. Второй
layout, включая `scalarBlockLayout`, намеренно не поддерживается. Manifest/IR
metadata хранит `Offset`, `Alignment`, `Size`, `ArrayStride` и nullable
`MatrixStride`; текущая генерация ресурсов всегда печатает `std430`.

`Delta.Maths.float3` нельзя считать CLR-эквивалентом плотного GLSL `vec3`: в
`std430` у `vec3` alignment и array stride равны 16 байтам, хотя размер CLR
значения может быть 12. Host-side upload должен использовать metadata manifest;
компилятор обязан диагностировать неподдерживаемые structured layouts, а не
молча использовать `Marshal.SizeOf` или вводить универсальную runtime-обёртку.

## Принятое направление

Для первой рабочей версии нужен следующий конвейер:

```text
C# source
  -> Roslyn Compilation + IOperation
  -> проверка разрешённого подмножества C#
  -> типизированный Delta.Shader IR
  -> Vulkan GLSL 460
  -> glslang/shaderc
  -> SPIR-V
  -> spirv-val
  -> VkShaderModule и тестовый dispatch через Silk.NET
```

Первым backend-ом должен быть транспиллер в читаемый Vulkan GLSL. Прямой
генератор SPIR-V полезен как второй backend, но не как первый этап: у него заметно
больше правил корректности — capabilities, decorations, layout, порядок секций
модуля, SSA, `OpPhi` и структурированный control flow. Промежуточное представление
нужно в любом случае, поэтому оно сразу должно быть независимо от GLSL.

Не следует строить компилятор поверх IL. Roslyn даёт исходные позиции, символы,
семантические операции и хорошие диагностические сообщения до потери структуры
исходного кода. Нельзя исполнять пользовательскую сборку или загружать её ради
constant folding: разрешены только константы Roslyn и собственный безопасный
вычислитель IR.

## Границы первой версии

MVP поддерживает только compute shader. Это позволяет проверить весь путь до
реального GPU без окна, surface, swapchain, rasterization и изображений.

Минимальный полезный сценарий:

1. Статический C#-метод помечен как compute entry point.
2. Атрибут задаёт размер local workgroup.
3. Метод читает один storage buffer и пишет во второй.
4. Delta.Shader создаёт `.glsl`, `.spv` и reflection manifest.
5. Headless-тест через `Silk.NET.Vulkan` выполняет dispatch и сравнивает выходной
   буфер с ожидаемыми значениями на CPU.
6. Валидационный слой Vulkan не сообщает ошибок.

В MVP входят:

- `void`-entry point, статические helper-методы и ациклический call graph;
- `bool`, `int`, `uint`, `float`;
- `bool2..4`, `int2..4`, `uint2..4`, `float2..4` из `Delta.Maths`;
- конструкторы векторов, чтение компонентов и swizzle-свойства;
- арифметика, сравнения, присваивания, локальные переменные;
- `if`, условное выражение и структурированные `for`/`while`;
- поддержанный список функций `Delta.Maths.maths`, сопоставленный с GLSL built-ins
  или с явно описанными IR-функциями;
- storage buffers, push constants и specialization constants;
- явные `set`/`binding`; никаких неявно назначаемых binding-ов.

В MVP не входят:

- `double` (требует проверки и включения shader float64 feature/capability);
- `fix` (его C#-реализация не равна нативному типу шейдера);
- `decimal`, строки, классы и любые reference types;
- исключения, `async`, итераторы, LINQ, delegates и lambdas;
- virtual/interface dispatch, reflection, dynamic и allocation;
- recursion, function pointers и небезопасный C#;
- textures, samplers, images, atomics, subgroups и ray tracing;
- matrices до определения единой модели layout и порядка умножения;
- неограниченный C#: поддерживается сознательно небольшой shader subset.

Каждая неподдержанная конструкция должна давать диагностическую ошибку Delta.Shader в
точной позиции исходника, а не падение компилятора и не ошибку от glslang в конце
конвейера.

## Связь с Delta.Maths

`Delta.Maths` хорошо подходит как синтаксический фасад: в ней уже есть векторные
типы, swizzles и lowercase-класс `maths`. При этом backend не должен транслировать
CLR-тела этой библиотеки. Он узнаёт разрешённые типы и методы по полному
`ISymbol`-идентификатору и заменяет их shader-семантикой.

Нужно завести явный реестр intrinsics:

```text
Delta.Maths.float3                   -> vec3
Delta.Maths.int2                     -> ivec2
Delta.Maths.uint4                    -> uvec4
Delta.Maths.bool3                    -> bvec3
Delta.Maths.maths.sin(float/floatN)  -> sin
Delta.Maths.maths.dot(floatN, ...)   -> dot
Delta.Maths.maths.normalize(...)     -> normalize
```

Реестр обязан хранить не только имя, но и допустимые сигнатуры, shader stage,
требуемые capabilities/extensions и правило понижения в IR. Совпадения только по
текстовому имени недостаточно: пользовательский метод `normalize` не является
intrinsic.

Особое внимание требуется layout-у данных. Например, размер C#-структуры и
размещение `vec3` в `std140`/`std430` нельзя считать совпадающими автоматически.
Delta.Shader должен сам вычислять offsets/alignments, записывать их в manifest и проверять
host-side типы. `Marshal.SizeOf` не является источником истины для shader layout.

## Предлагаемая структура решения

```text
Delta.Shader/
  Delta.Shader.sln
  global.json
  Directory.Build.props
  Directory.Packages.props
  src/
    Delta.Shader.Abstractions/          атрибуты и публичные shader/resource contracts
    Delta.Shader.Compiler/              Roslyn frontend, проверки, IR и pipeline
    Delta.Shader.Backend.Glsl/          печать Vulkan GLSL и source map
    Delta.Shader.Tool/                  dotnet delta-shader check/build/emit
    Delta.Shader.Analyzers/             IDE/MSBuild diagnostics и позднее code fixes
  tests/
    Delta.Shader.Compiler.Tests/        unit-тесты lowering и diagnostics
    Delta.Shader.Golden.Tests/          эталонные GLSL/SPIR-V assembly/manifest
    Delta.Shader.Vulkan.Tests/          реальный headless dispatch через Silk.NET
    Delta.Shader.TestShaders/           входные позитивные и негативные C#-шейдеры
  samples/
    ComputeBuffer/              минимальный исполняемый пример
  docs/
    language-subset.md
    resource-model.md
    diagnostics.md
    direct-spirv-backend.md
    adr/
```

На момент начала реализации CLI и тесты разумно нацелить на `.NET 10` LTS.
Analyzer-safe часть `Delta.Shader.Compiler` и `Delta.Shader.Analyzers` должна иметь совместимый с
хостами Roslyn target (обычно `netstandard2.0`), не заставляя IDE загружать
runtime CLI. Загрузка `.csproj` через MSBuild остаётся в `Delta.Shader.Tool`; compiler
core получает готовую `Compilation`. Версии Roslyn, Silk.NET и нативных shader
tools должны быть централизованы и зафиксированы.

Silk.NET 2.x сейчас находится в режиме ограниченной поддержки, а 3.x ещё меняет
bindings. Поэтому Vulkan-вызовы в тестах следует спрятать за одним маленьким
test harness и зафиксировать конкретную стабильную версию 2.x. Это уменьшит цену
будущего перехода на 3.x.

## Публичная модель шейдера

В `Delta.Shader.Abstractions` нужны только декларативные конструкции без зависимости от
Vulkan bindings:

- атрибут stage/entry point;
- размер compute workgroup;
- descriptor set и binding;
- read-only/read-write storage buffer;
- push constant block;
- specialization constant с постоянным ID;
- позднее: location, built-in, uniform buffer, image и sampler.

Имена атрибутов следует выбрать после одного небольшого ADR. Независимо от
названий, compiler contract должен требовать:

- один stage и одно уникальное имя на entry point;
- `static void` и отсутствие обычных runtime-параметров у entry point;
- уникальные пары `(set, binding)`;
- постоянный положительный local size в пределах выбранного target profile;
- явное направление доступа для каждого storage resource;
- отсутствие writable aliasing, пока его семантика не определена.

Ресурсы лучше представлять специальными shader-only wrapper-типами, а не
обычными C#-массивами. Их runtime-члены могут бросать исключение при случайном
CPU-вызове; компилятор распознаёт сами операции символически. Это отделяет
address space от обычного значения и позволяет анализатору запретить неправильное
копирование ресурсов.

## Roslyn frontend и IR

### Вход в компилятор

CLI должен принимать `.csproj` и получать настоящую `Compilation` со всеми
defines, nullable options и project references. Для первой версии допустим
`MSBuildWorkspace`; интеграцию в build следует добавлять после стабилизации CLI,
чтобы не получить рекурсивный вызов MSBuild.

Основные команды планируются такими:

```text
dotnet delta-shader check <project>
dotnet delta-shader emit  <project> --backend glsl --keep-source
dotnet delta-shader build <project> --target vulkan1.2
```

По умолчанию артефакты должны попадать в `obj/Delta.Shader/<configuration>/<tfm>/`, а
публикация рядом с приложением — быть отдельной явной опцией.

### Семантический анализ

Нужно обходить `IOperation`, а не только syntax tree. Типы, overload resolution,
conversions и вызванные методы определяются по символам Roslyn. Для циклов и
ветвлений следует построить Roslyn Control Flow Graph, затем перевести его в
собственные basic blocks и структурированный IR.

До lowering строится call graph от каждого entry point. Он нужен для:

- обнаружения recursion;
- исключения недостижимого пользовательского кода;
- вычисления stage/capability requirements;
- детерминированного порядка функций в выходном модуле.

### Минимальный IR

IR должен различать:

- скалярные, векторные, структурные, pointer/resource и `void`-типы;
- value и address, load/store и access chain;
- constants, unary/binary operations, constructors и conversions;
- function call и intrinsic call;
- basic blocks, branch, conditional branch, loop/selection merge и return;
- globals/resources, entry points, execution modes и decorations;
- target requirements: Vulkan version, SPIR-V version, capability, extension,
  device feature.

Нельзя помещать в IR фрагменты строк GLSL. Иначе прямой SPIR-V backend потребует
переписывания frontend-а. Перед backend-ом нужны отдельные passes: type check,
constant folding, control-flow validation, capability collection и name
sanitization.

### Различия семантики

Анализаторы и документация обязаны явно покрыть различия C#, GLSL и SPIR-V:

- floating-point результат не обязан побитово совпадать с CPU;
- signed overflow и преобразования нельзя молча считать C#-совместимыми;
- short-circuit `&&`/`||` следует понижать через control flow, а не через
  векторные операции;
- порядок вычисления выражений должен сохраняться там, где видимы side effects;
- `checked` запрещён;
- NaN, infinity, denorm и precise/fast-math должны зависеть от target options;
- индексация ресурсов требует bounds policy; в debug-профиле полезна опциональная
  инструментальная проверка, но MVP может требовать доказуемый/контролируемый
  индекс или документировать undefined/out-of-bounds semantics Vulkan.

## GLSL backend

Backend выдаёт детерминированный Vulkan GLSL, начиная с консервативного
`#version 460`. Extensions добавляются только из собранных requirements.

Обязательные правила:

- генерировать Vulkan, а не OpenGL semantics;
- всегда писать `layout(set = ..., binding = ...)` для descriptors;
- использовать blocks для uniform/storage resources;
- явно задавать locations, built-ins, local size и layout;
- не использовать OpenGL default uniforms;
- экранировать имена C#, конфликтующие с GLSL keywords;
- сохранять таблицу соответствия C#-диапазонов строкам GLSL;
- переводить диагностики glslang обратно в исходный `.cs` и позицию;
- не считать успешную генерацию GLSL доказательством корректности SPIR-V.

Для первой реализации предпочтителен официальный `glslang`/`glslc` как внешний
зафиксированный tool. Это делает ошибки и версии хорошо видимыми. После
стабилизации можно добавить in-process adapter поверх `Silk.NET.Shaderc`, не меняя
compiler pipeline.

На каждый build сохраняются:

```text
<entry>.glsl       читаемый промежуточный результат (по опции в release)
<entry>.spv        бинарный модуль
<entry>.spvasm     нормализованный disassembly для тестов (по опции)
<entry>.json       reflection/requirements manifest
```

После компиляции всегда запускается `spirv-val` с тем же явным target environment.
Оптимизация `spirv-opt` добавляется позднее и никогда не заменяет валидацию
неоптимизированного модуля.

## Reflection manifest

Manifest — стабильный контракт между compiler и runtime. Он должен содержать:

- schema version и версию Delta.Shader;
- entry point, stage и local size;
- descriptor sets, bindings, descriptor types, array counts и access flags;
- push-constant ranges;
- specialization constants;
- stage inputs/outputs, когда появятся graphics stages;
- offsets, sizes, strides и выбранный block layout;
- необходимые Vulkan features/extensions и SPIR-V capabilities;
- target environment и hash `.spv`.

Compiler может сформировать manifest из IR, но golden/integration-тест обязан
сверять его с отражением фактического SPIR-V (например, через SPIRV-Reflect). Это
ловит расхождения backend-а и публичного metadata contract.

## Анализаторы

`Delta.Shader.Analyzers` должны использовать тот же набор правил и таблицу intrinsics,
что и CLI. Копировать правила в два проекта нельзя. Analyzer даёт раннюю IDE
диагностику, а CLI повторяет проверку как авторитетный build step.

Начальный набор diagnostic IDs:

| ID | Ошибка |
| --- | --- |
| `DSH001` | неподдерживаемая конструкция C# |
| `DSH002` | неподдерживаемый shader type |
| `DSH003` | вызов метода не входит в intrinsic/user shader call graph |
| `DSH004` | неправильная сигнатура entry point |
| `DSH005` | конфликт descriptor set/binding или specialization ID |
| `DSH006` | тип нельзя безопасно разместить в выбранном buffer layout |
| `DSH007` | операция требует capability/feature вне target profile |
| `DSH008` | recursion или недопустимый call graph |
| `DSH009` | неявное/опасное числовое преобразование |
| `DSH010` | недетерминированная либо неоднозначная shader-семантика |

У каждой диагностики нужны позитивный тест, негативный тест, точная source span и
текст с конкретным исправлением. Code fixes не являются частью MVP; первыми можно
добавить fixes для отсутствующего binding и замены неподдерживаемого API на
известный intrinsic.

Source generator не должен быть главным компилятором: генератор естественно
добавляет C# в `Compilation`, но shader artifacts и внешняя валидация требуют
отдельного CLI/MSBuild pipeline. Позднее incremental generator может создавать
типизированные runtime handles или встраивать уже собранный SPIR-V.

## Тестовая стратегия

### 1. Unit и diagnostics

- один тест на каждую разрешённую `IOperation`;
- негативный тест на каждую запрещённую конструкцию;
- overloads и symbol identity для всех `Delta.Maths.maths` intrinsics;
- call graph, recursion, capabilities и layouts;
- диагностические snapshot-тесты с line/column;
- property-based тесты для type/layout calculator.

### 2. Golden output

- сравнивать форматированный `.glsl`;
- компилировать каждый golden shader официальным GLSL compiler;
- прогонять каждый `.spv` через `spirv-val`;
- сравнивать нормализованный `.spvasm`, а не сырые numeric IDs;
- проверять детерминированность повторной сборкой и сравнением hash;
- фиксировать версии glslang/shaderc и SPIRV-Tools в test report.

### 3. Дифференциальные тесты

Для чистых поддержанных функций допустимо вызвать CPU-реализацию
`Delta.Maths` и сравнить с GPU в пределах заранее заданных абсолютной/относительной
погрешностей. Такие тесты не должны требовать побитового равенства float и не
должны использовать CPU как нормативный oracle для NaN/denorm/overflow.

### 4. Headless Vulkan integration через Silk.NET

Первый integration runner не создаёт окно. Он должен:

1. Найти Vulkan loader и перечислить physical devices.
2. Выбрать compute-capable queue family.
3. Создать instance/device и включить validation layer в строгом режиме.
4. Создать device-local storage buffers и host-visible staging buffers; прямое
   отображение storage buffer допустимо только как проверенный fast path.
5. Создать descriptor set layout строго из manifest.
6. Создать shader module и compute pipeline.
7. Записать upload, dispatch, download и нужные transfer/compute barriers в
   command buffer.
8. Дождаться fence, invalidate non-coherent memory при необходимости, прочитать
   output и сравнить с ожидаемым результатом. При upload аналогично нужен flush.
9. Освободить все Vulkan handles даже при ошибке теста.
10. Завалить тест при validation error.

Для macOS runner должен учитывать portability enumeration/MoltenVK. Если Vulkan
loader, ICD, GPU или validation layer отсутствуют, локальный тест обязан явно
сообщить `Skipped` с причиной. В выделенном CI-профиле отсутствие Vulkan — ошибка,
а не успешный skip.

CI удобно разделить:

- обязательный CPU job: analyzers, unit, golden, glslang и `spirv-val`;
- software Vulkan job: SwiftShader или Mesa lavapipe;
- необязательный/ночной native-GPU job на разных vendors.

После compute MVP добавляется offscreen graphics smoke test: vertex + fragment
pipeline рисует небольшой target image без swapchain, изображение копируется в
buffer и проверяется по нескольким пикселям/hash с допуском.

## Этапы реализации и критерии готовности

### Этап 0. Зафиксировать контракты

- [ ] ADR: C# subset и почему frontend основан на `IOperation`.
- [ ] ADR: GLSL-first и backend-neutral IR.
- [ ] ADR: resource syntax, attributes и explicit bindings.
- [ ] Выбрать default target profile и совместимую пару Vulkan/SPIR-V.
- [ ] Зафиксировать .NET SDK, Roslyn, Silk.NET, glslang и SPIRV-Tools.

Готово, когда один пример compute shader полностью описан на бумаге: входной C#,
ожидаемый GLSL, manifest и ожидаемые значения buffer output.

### Этап 1. Roslyn frontend и analyzer skeleton

- [ ] Найти entry points по символам атрибутов.
- [ ] Построить call graph.
- [ ] Реализовать типы, literals, locals, operators, return и простые вызовы.
- [ ] Ввести общую библиотеку правил для analyzer и CLI.
- [ ] Реализовать `DSH001..DSH010` с тестами.

Готово, когда поддержанный пример превращается в валидированный IR, а каждая
запрещённая конструкция даёт ожидаемую диагностику в исходной позиции.

### Этап 2. GLSL backend

- [ ] Реализовать детерминированный printer и sanitizer имён.
- [ ] Понизить control flow и supported intrinsics.
- [ ] Реализовать resource blocks/layouts и source map.
- [ ] Выпустить GLSL и manifest.

Готово, когда golden GLSL стабилен и компилируется reference compiler-ом.

### Этап 3. SPIR-V toolchain

- [ ] Adapter для glslang/glslc с фиксированным target environment.
- [ ] Перевод diagnostics обратно в C#.
- [ ] Обязательный `spirv-val`.
- [ ] Disassembly/normalization для тестов.
- [ ] Сверка manifest с SPIR-V reflection.

Готово, когда положительный corpus проходит compiler+validator, а намеренно
испорченный модуль надёжно роняет тест.

### Этап 4. Исполнение через Silk.NET

- [ ] Headless compute harness.
- [ ] Validation layer/debug messenger.
- [ ] Storage buffer upload/dispatch/readback.
- [ ] Software Vulkan CI.
- [ ] Sample `ComputeBuffer`.

Готово, когда одна команда компилирует C# shader, исполняет полученный SPIR-V и
сверяет результат с CPU на чистом CI worker-е с software Vulkan.

### Этап 5. Расширение языка

- [ ] Vertex/fragment stages и offscreen test.
- [ ] Stage IO, built-ins и interpolation qualifiers.
- [ ] Uniform buffers, textures/samplers и images.
- [ ] Matrices с документированной convention.
- [ ] Отдельные opt-in profiles для float64, int64, float16 и subgroups.

Каждая новая возможность сначала получает requirement model, analyzer и
негативный capability test, затем backend lowering и только потом публичный API.

### Этап 6. Прямой SPIR-V backend

- [ ] Генерировать enums/operand metadata из официальной JSON grammar.
- [ ] Реализовать interning типов/констант и детерминированный ID allocator.
- [ ] Эмитировать capabilities, extensions, memory model и entry-point interface.
- [ ] Эмитировать decorations: set, binding, location, offset, stride и built-ins.
- [ ] Реализовать functions, basic blocks, merge instructions и `OpPhi`.
- [ ] Поддержать `GLSL.std.450` extended instructions.
- [ ] Прогонять тот же corpus через `spirv-val` и Vulkan execution tests.
- [ ] Делать differential comparison двух backend-ов на одинаковом IR.

Прямой backend готов только тогда, когда он проходит все тесты GLSL backend-а и
не требует специальных исключений в Roslyn frontend-е.

## Указания для прямого SPIR-V backend-а

Если команда решит перейти к нему раньше, читать нужно не только общую SPIR-V
спецификацию. Обязательный набор:

1. SPIR-V Unified Specification: структура модуля, types, SSA, control flow,
   validation rules и logical addressing.
2. Vulkan Specification, раздел **Vulkan Environment for SPIR-V**: дополнительные
   ограничения конкретного client API, допустимые capabilities/extensions и
   связь с device features.
3. SPIR-V machine-readable JSON grammar и headers: opcode/operand metadata нельзя
   перепечатывать вручную.
4. `GLSL.std.450`: номера и правила extended math instructions.
5. SPIRV-Tools: assembler, disassembler и validator должны быть частью каждого
   короткого цикла разработки.

Начинать следует с одного compute-модуля без ветвлений: константа, load, add,
store, return. Затем добавить functions, selection, loop и только после этого
ресурсные структуры. Capabilities должны выводиться из фактически используемых
операций; нельзя безусловно объявлять «всё», потому что Vulkan требует поддержки
заявленных возможностей устройством.

## Версионирование и воспроизводимость

- `global.json` фиксирует .NET SDK с разумным roll-forward policy.
- `Directory.Packages.props` фиксирует Roslyn, Silk.NET и test packages.
- native tool versions записываются в build log и manifest.
- target profile — часть cache key и публичного artifact metadata.
- cache key включает source, references, compiler/backend/tool versions и options.
- обновление Vulkan/SPIR-V target или glslang выполняется отдельным PR с полным
  golden и integration прогоном.
- release build не зависит от наличия случайного `glslangValidator` в `PATH`:
  tool обнаруживается предсказуемо либо его путь передаётся явно.

## Нормативные и рабочие источники

При расхождении README, compiler tool и спецификации источником истины являются
спецификации Khronos для выбранного target environment.

- [Vulkan specification (latest)](https://registry.khronos.org/vulkan/specs/latest/html/vkspec.html)
- [SPIR-V registry](https://registry.khronos.org/SPIR-V/)
- [SPIR-V Unified Specification](https://registry.khronos.org/SPIR-V/specs/unified1/SPIRV.html)
- [GLSL 4.60 specification, включая Vulkan-specific правила](https://registry.khronos.org/OpenGL/specs/gl/GLSLangSpec.4.60.html)
- [Khronos glslang](https://github.com/KhronosGroup/glslang)
- [Khronos SPIRV-Tools](https://github.com/KhronosGroup/SPIRV-Tools)
- [shaderc/glslc](https://github.com/google/shaderc)
- [Roslyn SDK overview](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/)
- [Silk.NET documentation](https://dotnet.github.io/Silk.NET/docs/)
- [Silk.NET repository and version status](https://github.com/dotnet/Silk.NET)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)

## Что считать первой публичной версией

Версия `0.1` — это не максимальный C# subset. Это воспроизводимый вертикальный
срез:

- один понятный compute shader на C# с `Delta.Maths.floatN` и `Delta.Maths.maths`;
- ранние Roslyn diagnostics для всего неподдержанного;
- читаемый Vulkan GLSL;
- валидный SPIR-V и корректный reflection manifest;
- реальный headless dispatch через Silk.NET;
- зелёные CPU, validator и software-Vulkan CI jobs;
- документированные версии, ограничения и сообщения об ошибках.

После этого язык следует расширять маленькими вертикальными срезами, сохраняя
одинаковое поведение IDE analyzer, CLI, backend-а и runtime tests.


## CI, tests, and benchmarks

The GitHub Actions workflow is [`.github/workflows/ci.yml`](.github/workflows/ci.yml).
Pull requests and pushes to `main` build in Release, run correctness tests, and
perform BenchmarkDotNet discovery only; they do not record performance numbers.
Measured benchmarks run only from **Actions → Build, tests and benchmarks → Run
workflow** with `run_benchmarks=true`. Results are uploaded from
`artifacts/benchmarks` for 30 days.

Repository conventions:

- correctness projects are named `*.Tests.csproj`; projects using
  `Microsoft.NET.Test.Sdk` run through `dotnet test`, while custom executable
  harnesses must be listed explicitly in the workflow and return a non-zero exit
  code on failure;
- BenchmarkDotNet projects are named `*.Benchmarks.csproj`; this filename is how
  the workflow discovers them;
- their entry point must forward CLI arguments with
  `BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args)`;
- mark the Delta implementation with `[Benchmark(Baseline = true)]` within every
  comparable benchmark category; use exactly one baseline per category;
- add sibling repositories to the checkout steps whenever a
  `ProjectReference` escapes this repository.

A benchmark added without the naming convention or without CLI argument
forwarding is not registered and must not be treated as CI coverage. Shared
GitHub runners are suitable for comparisons within one run, not for small
cross-run regression claims.
