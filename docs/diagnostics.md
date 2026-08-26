# Diagnostics

Canonical diagnostic families:

- `DSH001` — unsupported syntax or construct;
- `DSH002` — unsupported shader-visible type/resource type;
- `DSH003` — unsupported method call;
- `DSH004` — invalid or missing entry point;
- `DSH005` — descriptor/specialization binding conflict;
- `DSH006` — unsafe or incompatible buffer/push-constant layout;
- `DSH007` — capability or feature outside the target profile;
- `DSH008` — invalid body/call graph, including recursion;
- `DSH009` — unsafe conversion;
- `DSH010` — reference type in the reachable shader-visible type graph;
- `DSH011` — invalid stage builtin/resource parameter use;
- `DSH012` — invalid graphics output or varying contract;
- `DSH013` — mismatched vertex/fragment interface;
- `DSH014` — analyzer-rejected managed/runtime construct such as capture,
  reflection or virtual/interface dispatch;
- `DSH015` — reserved;
- `DSH016` — compile-time compute artifact generation failure;
- `DSH017` — generated graphics program does not contain exactly one vertex and
  one fragment entry;
- `DSH018` — duplicate graphics source entry name;
- `DSH019` — compile-time graphics artifact generation failure.

`src/DeltaShader.Compiler/ShaderDiagnosticId.cs` owns identifiers and
`src/DeltaShader.Analyzers/AnalyzerReleases.Unshipped.md` owns release
tracking. New diagnostics update both files and add a source-location test.
