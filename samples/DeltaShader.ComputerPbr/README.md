# DeltaShader computer PBR sample

This is a producer-owned shader sample. It contains a procedural desktop
computer model rendered by a fullscreen graphics pair:

- `ComputerPbrShaders.cs` defines the fullscreen triangle and the computer
  surface scene.
- `SampleComputer` is the model: monitor shell, emissive screen, stand, base,
  keyboard and status indicator.
- `ComposePbrLayers` is the selected PBR composite call graph. It applies
  ambient, direct light, clear-coat and emission layers in order; the final
  fragment also composites the result over a procedural background layer.

The source uses the normal DeltaShader authoring form, including
`using static Delta.Maths.maths;`. `ComputerFrame` contains only `Resolution`
and `Time` push constants, so the existing headless raster harness can provide
the frame without a descriptor or a local ABI packer.

Build and publish the pair from `DeltaShader`:

```text
./eng/prepare-compiled-shaders.sh
```

The generated pair is published under
`src/DeltaShader/CompiledShaders/`:

- `DeltaShader.ComputerPbr.Vertex.vert.spv`
- `DeltaShader.ComputerPbr.Fragment.frag.spv`

The exact names are also listed in `catalog.json`. Render can use the existing
headless harness without changing its ABI:

```text
dotnet run --project /Users/rum/GitProjects/TheFurnace/DeltaRender/samples/DeltaRender.HeadlessShaderPlayground/DeltaRender.HeadlessShaderPlayground.csproj -c Release -- \
  --vertex /Users/rum/GitProjects/TheFurnace/DeltaShader/src/DeltaShader/CompiledShaders/DeltaShader.ComputerPbr.Vertex.vert.spv \
  --fragment /Users/rum/GitProjects/TheFurnace/DeltaShader/src/DeltaShader/CompiledShaders/DeltaShader.ComputerPbr.Fragment.frag.spv \
  --width 1024 --height 640 --frames 3 --time 1.25 \
  --output /Users/rum/GitProjects/TheFurnace/DeltaShader/artifacts/computer-pbr-headless.ppm
```

This sample does not add runtime compilation or a runtime editor compositor.
The editor can select the source-level composite layer set; the published
artifact remains the ordinary `ShaderArtifact`/`ShaderAbi` graphics pair.
