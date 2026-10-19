#!/usr/bin/env bash
export NUGET_PACKAGES="D:\FunNoTrabajo\_devtmp\nuget" DOTNET_CLI_HOME="D:\FunNoTrabajo\_devtmp\home" TMP="D:\FunNoTrabajo\_devtmp\tmp" TEMP="D:\FunNoTrabajo\_devtmp\tmp" DOTNET_NOLOGO=1
ROOT="$(cd "$(dirname "$0")" && pwd)"
dotnet build "$ROOT/Source/AdvancedPlantGenetics/AdvancedPlantGenetics.csproj" -c Release -nologo -v q "$@"
