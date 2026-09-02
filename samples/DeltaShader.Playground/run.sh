#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
dotnet build "$ROOT/DeltaShader.Playground.csproj" -c Release
dotnet build "$ROOT/DeltaShader.Playground.AddBias.csproj" -c Release
