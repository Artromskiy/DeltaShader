# DeltaShader documentation

The [project README](../README.md) is the public entry point. This page is a
compact index for authoring, final artifacts and implementation notes.

## Public authoring and cross-project contracts

- [User authoring API](USER_API.md)
- [Final artifact contract](final-artifact-contract.md)
- [Cross-project contract](CONTRACT.md)
- [Shader ABI and artifact project](contract-project.md)
- [Diagnostics](diagnostics.md)

## Scenarios and guides

- [Graphics scenario](graphics-scenario-v0.1.md)
- [Compute scenario](compute-scenario-v0.1.md)
- [Resource model](resource-model.md)
- [Packing guide](README.PACKING.md)
- [UI shader contract](ui-shader-contract.md)
- [UI rendering performance](ui-rendering-performance/README.md)

## Internal and workflow references

- [Internal implementation](INTERNAL.md)
- [Build integration](build-integration.md)
- [Repository layout](repository-layout.md)

Authoring/compiler documents describe how source becomes an artifact. The
renderer consumes only the final SPIR-V payload and `ShaderAbi` described by
the final-artifact contract; intermediate GLSL is an optional inspection
sidecar.
