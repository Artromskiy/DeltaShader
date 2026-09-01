# Computer PBR asset

This directory contains the supplied `Sci-fi_Military_Rugged_Laptop.gltf`
asset, its binary buffer, and the source PNG material maps.

The canonical material mapping used by
`ComputerPbrTexturedComposite` is:

| Set | Binding | Shader resource | glTF map |
| --- | --- | --- | --- |
| 0 | 4 | `BaseColor` | `tosp_Laptop_BaseColor.png` |
| 0 | 5 | `Metallic` | `tosp_Laptop_Metallic.png` |
| 0 | 6 | `Normal` | `tosp_Laptop_Normal.png` |
| 0 | 7 | `Roughness` | `tosp_Laptop_Roughness.png` |
| 0 | 8 | `Occlusion` | `tosp_Laptop_AO.png` |
| 0 | 9 | `Emissive` | `tosp_Laptop_Emissive.png` |

The vertex path expects one host vertex buffer at binding `0`. Its generated
vertex input layout is:

| Location | Field | Format |
| --- | --- | --- |
| 0 | `Position` | `float4` |
| 1 | `WorldNormal` | `float3` |
| 2 | `Uv0` | `float2` |
| 3 | `Tangent` | `float4` |

The generated offsets are `Position=0`, `WorldNormal=16`, `Uv0=28`, and
`Tangent=36`; the complete vertex record stride is `52` bytes.

The producer-generated `VertexAbi` and packers remain authoritative for the
binding stride and byte offsets. The glTF loader may repack its interleaved
source buffer into this generated layout; it must not duplicate the ABI.
The same rule applies to `ComputerMeshFrame` push constants. The glTF loader,
texture upload, descriptor creation, and indexed draw remain renderer-owned.

The asset's connector maps are retained for a future material-slot variant;
the first material path deliberately uses the laptop maps above.

The source files are copied from the user-provided local asset directory.
Their licensing and redistribution status is not inferred by this sample.
