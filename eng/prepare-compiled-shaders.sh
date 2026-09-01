#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_dir="$repo_root/src/DeltaShader/CompiledShaders"
tool_project="$repo_root/src/DeltaShader.Tool/DeltaShader.Tool.csproj"

projects=(
  "src/DeltaShader.UI/DeltaShader.UI.csproj"
  "src/DeltaShader.Text/DeltaShader.Text.csproj"
  "src/DeltaShader.Mesh/DeltaShader.Mesh.csproj"
  "samples/DeltaShader.ShadertoyGallery/DeltaShader.ShadertoyGallery.csproj"
  "tests/DeltaShader.TestShaders/DeltaShader.TestShaders.csproj"
  "tests/DeltaShader.ComputeTextureFixture/DeltaShader.ComputeTextureFixture.csproj"
  "tests/DeltaShader.Compiler.ReferenceFixtures/DeltaShader.Compiler.ReferenceFixtures.csproj"
  "tests/DeltaShader.FullscreenFixture/DeltaShader.FullscreenFixture.csproj"
  "samples/DeltaShader.Playground/DeltaShader.Playground.csproj"
  "samples/DeltaShader.Playground/DeltaShader.Playground.AddBias.csproj"
)

for command_name in dotnet glslangValidator spirv-opt spirv-val jq; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'required command not found: %s\n' "$command_name" >&2
    exit 127
  fi
done

if [[ ! -f "$tool_project" ]]; then
  printf 'DeltaShader.Tool project not found: %s\n' "$tool_project" >&2
  exit 66
fi

staging_dir="$(mktemp -d "${TMPDIR:-/tmp}/delta-shader-compiled.XXXXXX")"
trap 'rm -rf "$staging_dir"' EXIT
entries_path="$staging_dir/entries.ndjson"
: > "$entries_path"

dotnet build "$tool_project" -c Release \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal

tool_binaries=("$repo_root"/src/DeltaShader.Tool/bin/Release/*/DeltaShader.Tool.dll)
if ((${#tool_binaries[@]} != 1)); then
  printf 'expected exactly one Release DeltaShader.Tool binary, found %s\n' "${#tool_binaries[@]}" >&2
  exit 66
fi
tool_binary="${tool_binaries[0]}"

artifact_count=0
project_count=0
compile_project() {
  local project="$1"
  local project_path="$repo_root/$project"
  local project_name="${project##*/}"
  project_name="${project_name%.csproj}"
  local project_output="$staging_dir/$project_name"
  local log_path="$staging_dir/logs/$project_name.log"

  mkdir -p "$project_output"
  dotnet "$tool_binary" \
    build "$project_path" --profile vulkan1.2 --spirv 1.5 --glsl 460 --optimize performance \
    --out "$project_output" > "$log_path" 2>&1
}

wait_for_compile_batch() {
  local failed=0
  local index
  for index in "${!compile_pids[@]}"; do
    if ! wait "${compile_pids[$index]}"; then
      printf 'shader project compilation failed: %s\n' "${compile_projects[$index]}" >&2
      cat "${compile_logs[$index]}" >&2
      failed=1
    fi
  done

  for index in "${!compile_logs[@]}"; do
    cat "${compile_logs[$index]}"
  done

  compile_pids=()
  compile_projects=()
  compile_logs=()
  if ((failed != 0)); then
    exit 1
  fi
}

mkdir -p "$staging_dir/logs"
compile_pids=()
compile_projects=()
compile_logs=()
max_parallel=4

for project in "${projects[@]}"; do
  project_path="$repo_root/$project"
  if [[ ! -f "$project_path" ]]; then
    printf 'shader project not found: %s\n' "$project_path" >&2
    exit 66
  fi

  project_name="${project##*/}"
  project_name="${project_name%.csproj}"

  printf 'Compiling %s\n' "$project"
  compile_project "$project" &
  compile_pids+=("$!")
  compile_projects+=("$project")
  compile_logs+=("$staging_dir/logs/$project_name.log")

  if ((${#compile_pids[@]} >= max_parallel)); then
    wait_for_compile_batch
  fi

done

if ((${#compile_pids[@]} > 0)); then
  wait_for_compile_batch
fi

for project in "${projects[@]}"; do
  project_name="${project##*/}"
  project_name="${project_name%.csproj}"
  project_output="$staging_dir/$project_name"
  project_files=()
  for generated_path in "$project_output"/*; do
    [[ -f "$generated_path" ]] || continue
    generated_name="$(basename "$generated_path")"
    published_name="$project_name.$generated_name"
    if [[ -e "$staging_dir/$published_name" ]]; then
      printf 'generated output collision: %s\n' "$published_name" >&2
      exit 65
    fi
    mv "$generated_path" "$staging_dir/$published_name"
    project_files+=("$published_name")
    if [[ "$generated_name" == *.spv ]]; then
      artifact_count=$((artifact_count + 1))
    fi
  done

  if ((${#project_files[@]} == 0)); then
    printf 'shader project produced no generated output: %s\n' "$project" >&2
    exit 65
  fi

  file_json="$(printf '%s\n' "${project_files[@]}" | jq -R -s 'split("\n") | map(select(length > 0))')"
  jq -n \
    --arg project "$project_name" \
    --arg project_path "$project" \
    --argjson files "$file_json" \
    '{project: $project, projectPath: $project_path, files: $files}' >> "$entries_path"
  project_count=$((project_count + 1))
done

while IFS= read -r spv_path; do
  artifact_base="${spv_path%.spv}"
  if [[ ! -s "${artifact_base}.glsl" || ! -s "${artifact_base}.shader.json" ]]; then
    printf 'missing GLSL or shader manifest sidecar for %s\n' "$spv_path" >&2
    exit 65
  fi
  spirv-val --target-env vulkan1.2 "$spv_path"
done < <(find "$staging_dir" -maxdepth 1 -type f -name '*.spv' -print | sort)

if [[ "$artifact_count" -eq 0 ]]; then
  printf 'publisher produced no SPIR-V artifacts\n' >&2
  exit 65
fi

jq -s \
  --arg output_directory "src/DeltaShader/CompiledShaders" \
  --arg profile "vulkan1.2" \
  --arg spirv_version "1.5" \
  --arg glsl_version "460" \
  --argjson project_count "$project_count" \
  --argjson artifact_count "$artifact_count" \
  '{schema: 1, outputDirectory: $output_directory, profile: $profile,
    spirv: $spirv_version, glsl: $glsl_version, projectCount: $project_count,
    artifactCount: $artifact_count, entries: .}' \
  "$entries_path" > "$staging_dir/catalog.json"

mkdir -p "$output_dir"
if [[ -s "$output_dir/catalog.json" ]]; then
  jq -r '.entries[]?.files[]?' "$output_dir/catalog.json" | while IFS= read -r old_name; do
    [[ -n "$old_name" ]] || continue
    [[ "$old_name" != */* ]] || continue
    rm -f "$output_dir/$old_name"
  done
fi

find "$staging_dir" -maxdepth 1 -type f \
  ! -name 'entries.ndjson' -exec cp {} "$output_dir/" \;

trap - EXIT
rm -rf "$staging_dir"
printf 'Published %s validated shader artifacts from %s projects to %s\n' \
  "$artifact_count" "$project_count" "$output_dir"
