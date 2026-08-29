#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
OUT="$ROOT/../../artifacts/playground"

if ! command -v glslangValidator >/dev/null 2>&1; then
    echo "[FAIL] glslangValidator was not found on PATH." >&2
    exit 1
fi

if ! command -v spirv-val >/dev/null 2>&1; then
    echo "[FAIL] spirv-val was not found on PATH." >&2
    exit 1
fi

mkdir -p "$OUT"
for project in DeltaShader.Playground.csproj DeltaShader.Playground.AddBias.csproj; do
    if ! dotnet run --project "$ROOT/../../src/DeltaShader.Tool/DeltaShader.Tool.csproj" \
        -c Release -- \
        build "$ROOT/$project" \
        --profile vulkan1.2 --spirv 1.5 --glsl 460 --out "$OUT"; then
        echo "[FAIL] $project: C# -> GLSL 460 -> SPIR-V compilation or validation failed." >&2
        exit 1
    fi
done

passed=0
for shader in SequenceMovement AddBias; do
    stem="$OUT/$shader.comp"
    if [ ! -f "$stem.glsl" ] || [ ! -f "$stem.spv" ] || [ ! -f "$stem.shader.json" ]; then
        echo "[FAIL] $shader: expected GLSL, SPIR-V and manifest artifacts are incomplete." >&2
        exit 1
    fi

    echo "[PASS] $shader: C# -> GLSL 460 -> glslangValidator -> spirv-val"
    passed=$((passed + 1))
done

echo "RESULT: $passed/2 shaders compiled and validated for Vulkan 1.2."
