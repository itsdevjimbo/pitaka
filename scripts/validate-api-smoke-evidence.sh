#!/usr/bin/env bash
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "$script_directory/lib/api-smoke-evidence.sh"

evidence_path="${1:-}"
if [[ -z "$evidence_path" ]] || (($# != 1)); then
    printf 'Usage: %s <api-smoke-evidence.json>\n' "$0" >&2
    exit 2
fi

pitaka_validate_api_smoke_evidence "$evidence_path"
