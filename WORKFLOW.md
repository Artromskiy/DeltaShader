# DeltaShader workflow

Restore once, then use bounded Release checks:

```bash
dotnet restore DeltaShader.slnx
dotnet build DeltaShader.slnx -c Release --no-restore \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal
dotnet test DeltaShader.slnx -c Release --no-build --no-restore \
  --disable-build-servers -m:1
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

Inspect emitted GLSL and manifest as well as test totals. `dotnet test` and
`MSBuildWorkspace` need local IPC; a sandbox `SocketException`/named-pipe denial
requires rerunning the same command outside the sandbox, not parallel retries.
The real GPU compute smoke is owned by DeltaRender.

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
