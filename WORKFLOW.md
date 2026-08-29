# DeltaShader workflow

## Repository layout gate

The repository follows the project-local layout in
[docs/repository-layout.md](docs/repository-layout.md). Before restore/build
or a structural handoff, run:

```bash
./eng/check-layout.sh
```

The gate checks the required top-level directories, rejects unexpected tracked
top-level folders, requires `src/DeltaShader/` as the primary source project,
and requires source siblings to use the `src/DeltaShader.<Area>/` form.

Restore once, then use bounded Release checks:

```bash
dotnet restore DeltaShader.slnx
dotnet build DeltaShader.slnx -c Release --no-restore \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal
# Keep test projects separate so one stuck test host cannot hide results from
# the other gates. The compiler gallery/integration host is bounded separately.
dotnet test tests/DeltaShader.Compiler.Tests/DeltaShader.Compiler.Tests.csproj \
  -c Release --no-build --no-restore --disable-build-servers -m:1 \
  /p:UseSharedCompilation=false \
  --filter 'FullyQualifiedName!~ShadertoyGalleryTests'
dotnet test tests/DeltaShader.Golden.Tests/DeltaShader.Golden.Tests.csproj \
  -c Release --no-build --no-restore --disable-build-servers -m:1 \
  /p:UseSharedCompilation=false
dotnet test tests/DeltaShader.Vulkan.Tests/DeltaShader.Vulkan.Tests.csproj \
  -c Release --no-build --no-restore --disable-build-servers -m:1 \
  /p:UseSharedCompilation=false
# Do not retry this command automatically after a timeout. On macOS, perl is
# used because the base system does not provide a portable timeout utility.
perl -e 'alarm 60; exec @ARGV' -- \
  dotnet test tests/DeltaShader.Compiler.Tests/DeltaShader.Compiler.Tests.csproj \
    -c Release --no-build --no-restore --disable-build-servers -m:1 \
    /p:UseSharedCompilation=false \
    --filter 'FullyQualifiedName~ShadertoyGalleryTests'
```

Real compiler output must also pass the CLI and external validators:

```bash
out_dir="$(mktemp -d)"
dotnet run --project src/DeltaShader.Tool/DeltaShader.Tool.csproj \
  -c Release --no-build -- build \
  tests/DeltaShader.TestShaders/DeltaShader.TestShaders.csproj \
  --profile vulkan1.2 --spirv 1.5 --glsl 460 --out "$out_dir"
for shader in "$out_dir"/*.spv; do
  spirv-val --target-env vulkan1.2 "$shader"
done
```

The Stage-1 CPU/GPU Maths producer bundle is generated from the DeltaMaths
handoff without changing DeltaMaths tests. With no arguments, the publisher
atomically refreshes the stable ignored catalog at
`artifacts/maths-conformance`:

```bash
./eng/prepare-maths-conformance-artifacts.sh
```

The optional positional arguments remain available for an explicit catalog:

```bash
./eng/prepare-maths-conformance-artifacts.sh "$out_dir" ../DeltaMaths
```

This emits generated C# fixtures, GLSL 460, SPIR-V, resolved `ShaderAbi`
(`*.abi.json`) sidecars, source manifests (`*.shader.json`) and `index.json`.
The script builds Maths, the DeltaMaths conformance project, and the Tool from
the current checkout, validates that every supported handoff case has a
matching artifact/sidecar set, and requires the handoff `shader-contract.json`,
`shader-conformance.json`, `glslangValidator`, `spirv-val` and `jq`. Cases that
the current compiler/backend cannot lower remain in `index.json` with their
exact diagnostic; they are never silently emitted as artifacts. Generated
binaries are ignored by Git. Platform-specific `packages.lock.json` changes
caused by restore are dependency metadata and are reviewed separately from
this artifact publication.

Inspect emitted GLSL and manifest as well as test totals. `dotnet test` and
`MSBuildWorkspace` need local IPC; a sandbox `SocketException`/named-pipe denial
requires rerunning the same command outside the sandbox, not parallel retries.
The real GPU compute smoke is owned by DeltaRender.

For the deterministic fullscreen graphics fixture, use the checked-in project
that links the canonical C# source without duplicating the shader definition:

```bash
out_dir="$(mktemp -d)"
./eng/prepare-fullscreen-artifact.sh "$out_dir"
```

The script requires `glslangValidator`, `spirv-val`, and `jq`; it builds the
tool and fixture from a clean checkout, emits `fullscreen-ui.vert.*` and
`fullscreen-ui.frag.*` into the requested directory, and validates both SPIR-V
modules. No generated output is checked in.

## Code metrics

Run the same analyzer/code-metrics build locally and in the manual GitHub
Actions workflow through the repository wrapper:

```bash
./eng/code-metrics.sh -v:q
```

`eng/code-metrics.sh` converts `CODE_METRICS_ERROR_LOG` (default:
`artifacts/code-metrics/diagnostics.sarif`) to an absolute path before
MSBuild starts, so multi-project builds write one repository-level SARIF
instead of resolving a missing directory relative to each project. An
explicit destination is supported:

```bash
CODE_METRICS_ERROR_LOG=/tmp/code-metrics.sarif ./eng/code-metrics.sh -v:q
```

Inspect the SARIF and summary artifacts from the manual workflow. The rules
CA1501/CA1502/CA1505/CA1506 are report-only signals; do not refactor a method
for one isolated warning. Refactor when several metrics remain over their
limits, the issue persists across runs, or profiling identifies a hot path.

For local application run `./eng/format.sh`; for a non-mutating check use
`FORMAT_CHECK=1 ./eng/format.sh`. The script uses `dotnet format whitespace
--folder` to avoid the MSBuild/Roslyn workspace load that can hang on macOS
with .NET 10. It checks/applies whitespace only; analyzer/style diagnostics
remain covered by the build and SARIF metrics workflow.

## Generated source style

When an analyzer or source generator emits multiline C# source, prefer C# raw
string literals (`"""..."""`) over escaped strings, repeated concatenation or
line-by-line `Append` calls. Use interpolation only for the values that are
actually dynamic, and keep ordinary escaped strings for short single-line
fragments.
