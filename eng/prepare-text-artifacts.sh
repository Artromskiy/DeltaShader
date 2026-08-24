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

for tool in glslangValidator spirv-val; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "Missing required shader validation tool '$tool'. Install it before packaging text artifacts." >&2
        exit 127
    fi
done

if ! command -v jq >/dev/null 2>&1; then
    echo "Missing required JSON validation tool 'jq'. Install it before packaging text artifacts." >&2
    exit 127
fi

mkdir -p "$output_directory"

build_args=(
    -c Release
    --disable-build-servers
    -m:1
    /p:UseSharedCompilation=false
    -v:minimal
)
dotnet build "$project_root/src/Delta.Shader.Tool/Delta.Shader.Tool.csproj" "${build_args[@]}"
dotnet build "$project_root/src/Delta.Shader.Text/Delta.Shader.Text.csproj" "${build_args[@]}"

artifact_stems=(
    SdfTextVertex.vert
    SdfTextFragment.frag
    MsdfTextVertex.vert
    MsdfTextFragment.frag
)
for stem in "${artifact_stems[@]}"; do
    rm -f "$output_directory/$stem.glsl" "$output_directory/$stem.spv" "$output_directory/$stem.shader.json"
done

dotnet run \
    --project "$project_root/src/Delta.Shader.Tool/Delta.Shader.Tool.csproj" \
    -c Release --no-build -- \
    build "$project_root/src/Delta.Shader.Text/Delta.Shader.Text.csproj" \
    --backend spirv \
    --profile vulkan1.2 \
    --spirv 1.5 \
    --glsl 460 \
    --out "$output_directory"

for stem in "${artifact_stems[@]}"; do
    for extension in glsl spv shader.json; do
        file="$output_directory/$stem.$extension"
        if [[ ! -s "$file" ]]; then
            echo "Packaging failed: expected non-empty artifact '$file'." >&2
            exit 1
        fi
    done

    manifest="$output_directory/$stem.shader.json"
    if ! jq -e 'type == "object" and (.Version | type == "number") and (.EntryPointName | type == "string") and (.Stage | type == "number")' "$manifest" >/dev/null; then
        echo "Packaging failed: invalid shader manifest '$manifest'." >&2
        exit 1
    fi
done

echo "Prepared SDF/MSDF text artifacts in $output_directory"
