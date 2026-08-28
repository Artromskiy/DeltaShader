#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if (($# > 2)); then
  printf 'usage: %s [OUTPUT_DIRECTORY] [DELTAMATHS_ROOT]\n' "$0" >&2
  exit 64
fi

output_dir="${1:-$repo_root/artifacts/maths-conformance}"
maths_root="${2:-$repo_root/../DeltaMaths}"

for command_name in dotnet glslangValidator spirv-val jq; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'required command not found: %s\n' "$command_name" >&2
    exit 127
  fi
done

if [[ ! -f "$maths_root/src/DeltaMaths/DeltaMaths.csproj" ]]; then
  printf 'DeltaMaths project not found: %s\n' "$maths_root/src/DeltaMaths/DeltaMaths.csproj" >&2
  exit 66
fi

conformance_project="$maths_root/Tests/DeltaMaths.Conformance/DeltaMaths.Conformance.csproj"
if [[ ! -f "$conformance_project" ]]; then
  printf 'DeltaMaths conformance project not found: %s\n' "$conformance_project" >&2
  exit 66
fi

mkdir -p "$(dirname "$output_dir")"
staging_dir="${output_dir}.staging.$$"
rm -rf "$staging_dir"
mkdir -p "$staging_dir"
trap 'rm -rf "$staging_dir"' EXIT

dotnet build "$maths_root/src/DeltaMaths/DeltaMaths.csproj" -c Release \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal

dotnet build "$conformance_project" -c Release \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal

dotnet build "$repo_root/src/DeltaShader.Tool/DeltaShader.Tool.csproj" -c Release \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal

publisher_exit=0
dotnet run --project "$repo_root/src/DeltaShader.Tool/DeltaShader.Tool.csproj" \
  -c Release --no-build --no-restore -- maths-conformance "$maths_root" \
  --profile vulkan1.2 --spirv 1.5 --glsl 460 --out "$staging_dir" || publisher_exit=$?

index_path="$staging_dir/index.json"
if [[ ! -s "$index_path" ]]; then
  printf 'publisher did not produce index.json: %s\n' "$index_path" >&2
  exit "${publisher_exit:-65}"
fi

spv_count="$(find "$staging_dir" -type f -name '*.spv' -print | wc -l | tr -d ' ')"
abi_count="$(find "$staging_dir" -type f -name '*.abi.json' -print | wc -l | tr -d ' ')"
shader_count="$(find "$staging_dir" -type f -name '*.shader.json' -print | wc -l | tr -d ' ')"
if [[ "$spv_count" == "0" ]]; then
  printf 'publisher produced no SPIR-V artifacts: %s\n' "$staging_dir" >&2
  exit 65
fi

while IFS= read -r spv_path; do
  artifact_base="${spv_path%.spv}"
  if [[ ! -s "${artifact_base}.abi.json" || ! -s "${artifact_base}.shader.json" ]]; then
    printf 'missing sidecar for %s\n' "$spv_path" >&2
    exit 65
  fi
done < <(find "$staging_dir" -type f -name '*.spv' -print | sort)

artifact_count="$(jq -er '.ArtifactCount' "$index_path")"
selected_count="$(jq -er '.SelectedCount' "$index_path")"
bundle_case_count="$(jq -er '.BundleCaseCount' "$index_path")"
supported_case_count="$(jq -er '.SupportedCaseCount' "$index_path")"
blocked_count="$(jq -er '[.Cases[] | select(.Status != "passed")] | length' "$index_path")"
missing_diagnostics="$(jq -er '[.Cases[] | select(.Status != "passed") | select((.Diagnostic // "") == "")] | length' "$index_path")"
if [[ "$artifact_count" -ne "$spv_count" || "$artifact_count" -ne "$abi_count" || \
      "$artifact_count" -ne "$shader_count" || "$selected_count" -ne "$bundle_case_count" || \
      "$selected_count" -ne "$supported_case_count" || "$missing_diagnostics" -ne 0 ]]; then
  printf 'artifact sidecar count mismatch: index=%s spv=%s abi=%s shader=%s\n' \
    "$artifact_count" "$spv_count" "$abi_count" "$shader_count" >&2
  printf 'case count mismatch: selected=%s bundle=%s supported=%s\n' \
    "$selected_count" "$bundle_case_count" "$supported_case_count" >&2
  if [[ "$missing_diagnostics" -ne 0 ]]; then
    printf 'blocked cases without diagnostics: %s\n' "$missing_diagnostics" >&2
  fi
  exit 65
fi

rm -rf "$output_dir"
mv "$staging_dir" "$output_dir"
trap - EXIT
printf 'Published %s validated Maths conformance artifacts to %s\n' \
  "$artifact_count/$selected_count" "$output_dir"
if [[ "$blocked_count" -ne 0 ]]; then
  printf '%s cases retain exact compiler diagnostics in index.json\n' "$blocked_count"
fi
