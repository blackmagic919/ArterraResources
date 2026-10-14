#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

python3 scripts/fetch-litenetlib.py
# Publish once; CDK's asset hash identifies the same payload in all environments.
rm -rf .artifacts/service
mkdir -p .artifacts/service
dotnet publish ../ArterraDiscoveryService/Arterra.Discovery.csproj \
  --configuration Release --framework net8.0 --runtime linux-x64 --self-contained true \
  -p:GeneratePackageOnBuild=false -p:EnableTrimAnalyzer=false -p:IsTrimmable=false \
  --output .artifacts/service
cp litenetlib-release.json .artifacts/service/
cp .cache/litenetlib/LICENSE.txt .artifacts/service/LiteNetLib-LICENSE.txt

# HTTP management is a separate Lambda artifact, with no LiteNetLib dependency.
rm -rf .artifacts/game-service
mkdir -p .artifacts/game-service
dotnet publish ../ArterraGameService/Arterra.Game.csproj \
  --configuration Release --framework net8.0 --runtime linux-x64 --self-contained false \
  -p:UseAppHost=false --output .artifacts/game-service
