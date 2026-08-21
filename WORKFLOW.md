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
dotnet run --project src/Delta.Shader.Tool/Delta.Shader.Tool.csproj \
  -c Release --no-build -- build \
  tests/Delta.Shader.TestShaders/Delta.Shader.TestShaders.csproj \
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

Run the manual GitHub Actions `Code metrics` workflow when maintainability
evidence is needed. It enables CA1501/CA1502/CA1505/CA1506 as report-only
diagnostics and uploads the SARIF, build log and exit summary as artifacts.
