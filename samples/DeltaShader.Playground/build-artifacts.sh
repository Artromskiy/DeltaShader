#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
TOOL_PROJECT="$ROOT/../../src/DeltaShader.Tool/DeltaShader.Tool.csproj"
TEMP_ROOT=$(mktemp -d "${TMPDIR:-/tmp}/delta-shader-playground.XXXXXX")
trap 'rm -rf "$TEMP_ROOT"' EXIT

if ! command -v glslangValidator >/dev/null 2>&1; then
    echo "[FAIL] glslangValidator was not found on PATH." >&2
    exit 1
fi

if ! command -v spirv-val >/dev/null 2>&1; then
    echo "[FAIL] spirv-val was not found on PATH." >&2
    exit 1
fi

if ! command -v spirv-opt >/dev/null 2>&1; then
    echo "[FAIL] spirv-opt was not found on PATH." >&2
    exit 1
fi

dotnet build "$TOOL_PROJECT" -c Release --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal

passed=0
compile_project() {
    project="$1"
    output="$TEMP_ROOT/$(basename "$project" .csproj)"
    mkdir -p "$output"
    dotnet run --project "$TOOL_PROJECT" -c Release --no-build --no-restore -- \
        build "$project" --backend spirv --profile vulkan1.2 --spirv 1.5 \
        --glsl 460 --optimize performance --out "$output"

    for spirv in "$output"/*.spv; do
        [ -f "$spirv" ] || continue
        stem="${spirv%.spv}"
        [ -s "$stem.glsl" ] || exit 1
        [ -s "$stem.shader.json" ] || exit 1
        spirv-val --target-env vulkan1.2 "$spirv"
        passed=$((passed + 1))
    done
}

compile_project "$ROOT/DeltaShader.Playground.csproj"
compile_project "$ROOT/DeltaShader.Playground.AddBias.csproj"

echo "RESULT: $passed shaders compiled into a fresh temporary directory and validated for Vulkan 1.2."
