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
│   ├── DeltaShader.Text/             reusable text shader authoring
│   └── DeltaShader.ShadertoyGallery/ consumer fixture project
├── tests/                            compiler, golden and validation tests
├── docs/                             durable contracts and implementation docs
├── eng/                              repeatable build and validation scripts
├── DeltaShaderPlayground/            standalone editable authoring playground
└── .github/                          CI workflows
```

`src/DeltaShader/` is the primary authoring project. Every additional source
project is a sibling named `src/DeltaShader.<Area>/`; tests and tooling are not
runtime contract assemblies. `DeltaShaderPlayground/` is deliberately kept as
a separate project boundary inside this repository: its `Compute.cs` and
`Program.cs` are easy to find together, while the analyzer only inspects the
shader authoring projects and not host code.

Generated GLSL, SPIR-V, JSON manifests and temporary compiler output belong in
ignored `artifacts/` directories. They are not checked-in source and are not a
second contract. The final consumer boundary remains documented in
[final-artifact-contract.md](final-artifact-contract.md).

The layout gate in `eng/check-layout.sh` validates this project-local shape:
the required top-level directories, `src/DeltaShader/` as the primary project,
and the `src/DeltaShader.<Area>/` naming rule for source siblings.
