#!/usr/bin/env bash
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "$script_directory/lib/container-images.sh"

commit="${GITHUB_SHA:?GITHUB_SHA must be set by the workflow}"
if ! pitaka_is_full_commit_sha "$commit"; then
    printf 'publish-images: expected a full 40-character commit SHA, got %s\n' "$commit" >&2
    exit 2
fi

platforms="linux/amd64,linux/arm64"
sha_tag="sha-$commit"
api_image="jimbodev0530/pitaka-api"
migrations_image="jimbodev0530/pitaka-migrations"

fail() {
    printf 'publish-images: %s\n' "$1" >&2
    exit 1
}

registry_tag_exists() {
    local image="$1"
    local tag="$2"
    local response_file status

    response_file="$(mktemp)"
    status="$(curl --silent --show-error --output "$response_file" --write-out '%{http_code}' \
        "https://hub.docker.com/v2/repositories/$image/tags/$tag")" || {
        rm -f "$response_file"
        fail "could not check Docker Hub tag $image:$tag"
    }
    rm -f "$response_file"

    case "$status" in
        200) return 0 ;;
        404) return 1 ;;
        *) fail "Docker Hub returned HTTP $status while checking $image:$tag" ;;
    esac
}

publish_fixed_tag() {
    local image="$1"
    local dockerfile_target="$2"

    if registry_tag_exists "$image" "$sha_tag"; then
        printf 'Reusing existing fixed image %s:%s after verification.\n' "$image" "$sha_tag"
    else
        printf 'Building %s:%s for %s.\n' "$image" "$sha_tag" "$platforms"
        docker buildx build \
            --file PitakaApp.Api/Dockerfile \
            --target "$dockerfile_target" \
            --platform "$platforms" \
            --label "org.opencontainers.image.revision=$commit" \
            --annotation "index:org.opencontainers.image.revision=$commit" \
            --tag "$image:$sha_tag" \
            --push \
            .
    fi

    pitaka_validate_image "$image" "$sha_tag" "$commit"
}

publish_fixed_tag "$api_image" final
publish_fixed_tag "$migrations_image" migrations

# Do not move either alias until both fixed artifacts are available and verified.
pitaka_validate_image "$api_image" "$sha_tag" "$commit"
pitaka_validate_image "$migrations_image" "$sha_tag" "$commit"

# Exercise the exact artifacts from Docker Hub against disposable dependencies
# before moving either alias.
./scripts/smoke-published-images.sh "$commit"
printf 'Published and smoke-tested a verified fixed image pair for %s.\n' "$commit"
