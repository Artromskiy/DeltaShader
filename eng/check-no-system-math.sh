#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

matches="$(rg -n -P \
  --glob '*.cs' \
  --glob '!**/Generated/**' \
  --glob '!**/*.g.cs' \
  --glob '!**/obj/**' \
  --glob '!**/bin/**' \
  --glob '!**/artifacts/**' \
  '\b(?:System\.)?MathF?\s*\.' \
  src tests samples || true)"

if [[ -n "$matches" ]]; then
    printf '%s\n' 'Direct System.Math/MathF usage is not allowed in the DeltaMaths-dependent DeltaShader scope:' >&2
    printf '%s\n' "$matches" >&2
    exit 1
fi

printf '%s\n' 'No direct System.Math/MathF usage found in the checked DeltaMaths-dependent scope.'
