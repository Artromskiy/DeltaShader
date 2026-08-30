#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
OUT="$ROOT/../../src/DeltaShader/CompiledShaders"

if ! command -v glslangValidator >/dev/null 2>&1; then
    echo "[FAIL] glslangValidator was not found on PATH." >&2
    exit 1
fi

if ! command -v spirv-val >/dev/null 2>&1; then
    echo "[FAIL] spirv-val was not found on PATH." >&2
    exit 1
fi

"$ROOT/../../eng/prepare-compiled-shaders.sh"

passed=0
for shader in \
    DeltaShader.Playground.SequenceMovement.comp \
    DeltaShader.Playground.AddBias.AddBias.comp; do
    stem="$OUT/$shader"
    if [ ! -f "$stem.glsl" ] || [ ! -f "$stem.spv" ] || [ ! -f "$stem.shader.json" ]; then
        echo "[FAIL] $shader: expected GLSL, SPIR-V and manifest artifacts are incomplete." >&2
        exit 1
    fi

    echo "[PASS] $shader: C# -> GLSL 460 -> glslangValidator -> spirv-val"
    passed=$((passed + 1))
done

echo "RESULT: $passed/2 shaders compiled and validated for Vulkan 1.2."
