#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if (($# >= 2)); then
  maths_root="$2"
else
  maths_root="$repo_root/../DeltaMaths"
fi
output_dir="${1:?usage: $0 OUTPUT_DIRECTORY [DELTAMATHS_ROOT]}"

dotnet build "$maths_root/DeltaMaths.csproj" -c Release \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal

dotnet run --project "$repo_root/src/DeltaShader.Tool/DeltaShader.Tool.csproj" \
  -c Release --no-restore -- maths-conformance "$maths_root" --profile vulkan1.2 \
  --spirv 1.5 --glsl 460 --out "$output_dir"
