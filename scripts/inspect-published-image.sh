#!/usr/bin/env bash
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "$script_directory/lib/container-images.sh"

reference="${1:-}"
expected_commit="${2:-}"

if [[ -z "$reference" ]] || ! pitaka_is_full_commit_sha "$expected_commit"; then
    printf 'Usage: %s <image-reference> <full-40-character-commit-sha>\n' "$0" >&2
    exit 2
fi

pitaka_read_image_identity "$reference" "$expected_commit"
