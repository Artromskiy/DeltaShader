#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ "$#" -ne 1 || -z "$1" ]]; then
    echo "Usage: $0 <output-directory>" >&2
    exit 2
fi

output_directory="$1"
if [[ "$output_directory" == "/" || "$output_directory" == "." ]]; then
    echo "Refusing unsafe output directory: '$output_directory'." >&2
    exit 2
fi

for tool in glslangValidator spirv-opt spirv-val jq; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "Missing required tool '$tool'. Install it before preparing the fullscreen artifact." >&2
        exit 127
    fi
done

mkdir -p "$output_directory"

build_args=(
    -c Release
    --disable-build-servers
    -m:1
    /p:UseSharedCompilation=false
    -v:minimal
)
dotnet build "$project_root/src/DeltaShader.Tool/DeltaShader.Tool.csproj" "${build_args[@]}"
dotnet build "$project_root/tests/DeltaShader.FullscreenFixture/DeltaShader.FullscreenFixture.csproj" "${build_args[@]}"

for stem in Vertex.vert Fragment.frag; do
    for extension in glsl spv shader.json; do
        rm -f "$output_directory/$stem.$extension"
    done
done

dotnet run \
    --project "$project_root/src/DeltaShader.Tool/DeltaShader.Tool.csproj" \
    -c Release --no-build -- \
    build "$project_root/tests/DeltaShader.FullscreenFixture/DeltaShader.FullscreenFixture.csproj" \
    --backend spirv \
    --profile vulkan1.2 \
    --spirv 1.5 \
    --glsl 460 \
    --optimize performance \
    --out "$output_directory"

for stem in Vertex.vert Fragment.frag; do
    for extension in glsl spv shader.json; do
        file="$output_directory/$stem.$extension"
        if [[ ! -s "$file" ]]; then
            echo "Preparation failed: expected non-empty artifact '$file'." >&2
            exit 1
        fi
    done

    manifest="$output_directory/$stem.shader.json"
    if ! jq -e '.EntryPointName == "main" and .GlslVersion == "460" and .StorageLayout == "std430"' "$manifest" >/dev/null; then
        echo "Preparation failed: unresolved graphics manifest '$manifest'." >&2
        exit 1
    fi

    spirv-val --target-env vulkan1.2 "$output_directory/$stem.spv"
done

for source in Vertex.vert Fragment.frag; do
    final_stem="fullscreen-ui.${source#*.}"
    for extension in glsl spv shader.json; do
        mv "$output_directory/$source.$extension" "$output_directory/$final_stem.$extension"
    done
done

echo "Prepared fullscreen artifact pair in $output_directory"
