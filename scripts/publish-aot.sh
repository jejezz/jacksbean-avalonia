#!/usr/bin/env bash
#
# NativeAOT publish for Linux/macOS (the counterpart of publish-aot.ps1): publish/aot/JacksBean plus its native libraries.
#   ./scripts/publish-aot.sh [rid]        rid: linux-x64 | osx-arm64 | osx-x64 (default: this machine's)
# NativeAOT cannot cross-compile between operating systems — run it on the OS you are building for.
# Linux needs a C toolchain first:  sudo apt-get install -y clang zlib1g-dev

set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

RID="${1:-}"
if [ -z "$RID" ]; then
  case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) RID=linux-x64 ;;
    Darwin-arm64) RID=osx-arm64 ;;
    Darwin-x86_64) RID=osx-x64 ;;
    *) echo "unsupported platform: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
  esac
fi

rm -rf publish/aot
dotnet publish src/JackBean.App -c Release -r "$RID" -p:PublishAot=true -o publish/aot
ls -la publish/aot | head -20
