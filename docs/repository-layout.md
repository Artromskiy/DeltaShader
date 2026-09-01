# DeltaShader repository layout

This document is the project-local layout contract for DeltaShader. It records
the current source and tooling boundaries; it does not define the runtime
artifact ABI.

```text
DeltaShader/
├── src/
│   ├── DeltaShader/                  user authoring API
│   ├── DeltaShader.Contract/         final runtime artifact contract
│   ├── DeltaShader.Analyzers/        Roslyn analyzer and generator
│   ├── DeltaShader.Compiler/         compiler frontend and typed IR
│   ├── DeltaShader.Backend.Glsl/     GLSL lowering backend
│   ├── DeltaShader.Tool/             build and artifact tooling
│   ├── DeltaShader.Mesh/             reusable mesh vertex/fragment authoring
│   ├── DeltaShader.Text/             reusable text shader authoring
│   └── DeltaShader.UI/               reusable UI shader authoring
├── samples/                          editable sample projects
│   ├── DeltaShader.Playground/       shader authoring playground
│   └── DeltaShader.ShadertoyGallery/ ShaderToy fixture project
├── tests/                            compiler, golden and validation tests
├── docs/                             durable contracts and implementation docs
├── eng/                              repeatable build and validation scripts
└── .github/                          CI workflows
```

`src/DeltaShader/` is the primary authoring project. Every additional source
project is a sibling named `src/DeltaShader.<Area>/`; samples, tests and tooling
are not runtime contract assemblies. `samples/DeltaShader.Playground/` is
deliberately kept as a separate project boundary inside this repository: its
`Compute.cs` and `Program.cs` are easy to find together, while the analyzer only
inspects the
shader authoring projects and not host code. It has no nested Git repository and
no project-specific solution; the primary project is part of `DeltaShader.slnx`.

Generated GLSL, SPIR-V and shader JSON for DeltaShader-owned sources are
published by the canonical shader publisher into
`src/DeltaShader/CompiledShaders`. The directory is generated and ignored; it
is not checked-in source and is not a second contract. Ordinary shader
projects may opt into the MSBuild integration in
[build-integration.md](build-integration.md); those projects publish into
their own `bin/.../DeltaShader` deployment directory while keeping staging
under `obj`. Build output, lock files and temporary validation files remain
outside the checked-in source boundaries. The final consumer boundary remains
documented in [final-artifact-contract.md](final-artifact-contract.md).

The layout gate in `eng/check-layout.sh` validates this project-local shape:
the required top-level directories, `src/DeltaShader/` as the primary project,
and the `src/DeltaShader.<Area>/` naming rule for source siblings.
