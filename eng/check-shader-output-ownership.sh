#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
violations=()

for project in DeltaRender DeltaEngine DeltaXAML; do
  project_root="$root/$project"
  [[ -d "$project_root" ]] || continue
  while IFS= read -r path; do
    violations+=("${path#$root/}")
  done < <(find "$project_root" \
    -path '*/bin' -prune -o -path '*/obj' -prune -o -path '*/.git' -prune -o \
    -type f \( -name '*.spv' -o -name '*.shader.json' -o -name '*.abi.json' \) -print | sort)
done

if ((${#violations[@]} != 0)); then
  printf 'compiled shader outputs must be published by DeltaShader only:\n' >&2
  printf '  %s\n' "${violations[@]}" >&2
  exit 1
fi

printf 'shader output ownership: valid (DeltaShader is the sole publisher)\n'
