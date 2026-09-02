# DeltaShader workflow

## Benchmark parameter policy

BenchmarkDotNet attributes may describe benchmark methods, categories and
lifecycle hooks, but they must not define workload or run parameters. Do not add
`[Params]`, `[ParamsSource]`, `[Arguments]`, `[ArgumentsSource]` or equivalent
parameter attributes. Parse every workload/configuration value from application
command-line arguments (or the invoking script) before BenchmarkDotNet starts,
and pass the resulting values into the benchmark runner. Keep BDN runner
switches such as `--filter` and `--job` separate from workload input. Existing
parameter attributes are migration debt: do not add new uses and replace them
when that benchmark is next modified.


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

Generated shader outputs are never kept in a repository-level catalog. Every
bounded check creates a fresh temporary output directory and invokes the tool
directly for the source project under test:

```bash
out_dir="$(mktemp -d)"
trap 'rm -rf "$out_dir"' EXIT
dotnet run --project src/DeltaShader.Tool/DeltaShader.Tool.csproj \
  -c Release -- build src/DeltaShader.UI/DeltaShader.UI.csproj \
  --profile vulkan1.2 --spirv 1.5 --glsl 460 \
  --optimize performance --out "$out_dir"
```

It uses `--optimize performance`: `glslangValidator` compiles without an
optimization flag for compatibility across runner versions, then `spirv-opt -O`
optimizes the module, and `spirv-val` validates the result. The CLI also accepts
`--optimize none|performance|size`; `size` uses `spirv-opt -Os`. Every SPIR-V
result is validated after optimization.

Build folders, lock files and temporary validator output are not publication
members. There is no persistent `CompiledShaders` output to consume.

The Stage-1 CPU/GPU Maths producer bundle is generated from the DeltaMaths
handoff without changing DeltaMaths tests. The test-only publisher requires an
explicit fresh output directory:

```bash
out_dir="$(mktemp -d)"
trap 'rm -rf "$out_dir"' EXIT
./eng/prepare-maths-conformance-artifacts.sh "$out_dir"
```

The optional positional arguments remain available for an explicit catalog:

```bash
./eng/prepare-maths-conformance-artifacts.sh "$out_dir" ../DeltaMaths
```

This emits generated C# fixtures, GLSL 460, SPIR-V, resolved `ShaderAbi`
(`*.abi.json`) sidecars, source manifests (`*.shader.json`) and `index.json`
without a nested `cases/` directory. The optional positional arguments remain
## Project build integration

An ordinary shader project can opt into automatic compilation by restoring the
`DeltaShader.Tool` package and setting `DeltaShaderEnabled=true`. The package
imports `buildTransitive/DeltaShader.Tool.props` and
`DeltaShader.Tool.targets`; a normal
project build then invokes the existing CLI once after `Build`, publishes
validated outputs under `bin/.../DeltaShader`, and exposes
`@(DeltaShaderArtifact)` items with exact manifest/SPIR-V/GLSL paths. Sources
stay in the owning project, and consumer projects must not check in copies of
the generated files.

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

## Shader output ownership

DeltaShader is the sole owner of generated `.spv`, `.glsl`, `*.shader.json`
and `*.abi.json` outputs. Consumer projects must not check them in beside
their sources. The cross-repository regression gate is:

```bash
./eng/check-shader-output-ownership.sh
```

Build folders, lock files and temporary validator output are not shader
publication members.
## Shader artifact publish parallelism

Each producer check owns an isolated temporary directory and invokes the
Release `DeltaShader.Tool` directly. Persistent catalogs and atomic catalog
swaps are intentionally not part of the workflow, so a later check cannot
consume artifacts from an earlier run.

## Contract release versioning

Unless the change explicitly says otherwise, every change to a public shader
authoring contract, `ShaderAbi`/`ShaderArtifact` shape, generated pack/unpack
API, or frozen DeltaShader-to-consumer semantics must increment the repository
version tag and the `DeltaShader.Tool` NuGet package version together. The tag
uses the `v` prefix (`v0.0.14`); the NuGet package uses the same SemVer without
that prefix (`0.0.14`). Update package references and release documentation to
the same version in the same change.

Internal implementation fixes, diagnostics, tests and documentation-only
changes do not require a version bump unless they change the public contract.
Any deliberate exception to this rule must be stated explicitly in the task
or release change. Before publishing, confirm that the package version and
repository tag identify the same contract revision; do not publish a contract
package under the previous version.
