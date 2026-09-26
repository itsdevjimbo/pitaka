#!/usr/bin/env bash
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "$script_directory/lib/container-images.sh"

commit="${1:-}"
if ! pitaka_is_full_commit_sha "$commit"; then
    printf 'Usage: %s <full-40-character-commit-sha>\n' "$0" >&2
    exit 2
fi

repository_root="$(cd "$script_directory/.." && pwd -P)"
api_image="jimbodev0530/pitaka-api:sha-$commit"
project_name="pitaka-image-smoke-$RANDOM-$$"
export API_IMAGE="$api_image"
compose=(docker compose --project-name "$project_name" \
    --file "$repository_root/docker-compose.images-smoke.yml")

cleanup() {
    "${compose[@]}" down --volumes --remove-orphans || true
}
trap cleanup EXIT

printf 'Pulling the fixed API image for %s.\n' "$commit"
"${compose[@]}" pull

revision="$(docker image inspect "$api_image" \
    --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}')"
if [[ "$revision" != "$commit" ]]; then
    printf 'Image %s reports source %s, expected %s.\n' "$api_image" "$revision" "$commit" >&2
    exit 1
fi

"${compose[@]}" up --detach mysql seaweedfs smtp4dev
"${compose[@]}" run --rm migration
"${compose[@]}" up --detach api

for attempt in $(seq 1 45); do
    if curl --fail --silent http://localhost:18080/openapi/v1.json >/dev/null; then
        printf 'Migration bundle in API image %s completed and the API responds at http://localhost:18080.\n' "$commit"
        exit 0
    fi
    sleep 2
done

"${compose[@]}" logs api migration mysql seaweedfs smtp4dev
printf 'The API did not respond after the migration completed.\n' >&2
exit 1
