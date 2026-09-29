#!/usr/bin/env bash
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "$script_directory/lib/container-images.sh"
source "$script_directory/lib/api-smoke-evidence.sh"

commit="${GITHUB_SHA:?GITHUB_SHA must be set by the workflow}"
if ! pitaka_is_full_commit_sha "$commit"; then
    printf 'publish-images: expected a full 40-character commit SHA, got %s\n' "$commit" >&2
    exit 2
fi

platforms="linux/amd64,linux/arm64"
sha_tag="sha-$commit"
api_image="jimbodev0530/pitaka-api"
source_repository="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY must be set by the workflow}"
run_id="${GITHUB_RUN_ID:?GITHUB_RUN_ID must be set by the workflow}"
run_attempt="${GITHUB_RUN_ATTEMPT:?GITHUB_RUN_ATTEMPT must be set by the workflow}"
evidence_path="${PITAKA_EVIDENCE_PATH:?PITAKA_EVIDENCE_PATH must be set by the workflow}"

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

if registry_tag_exists "$api_image" "$sha_tag"; then
    printf 'Reusing existing fixed image %s:%s after verification.\n' "$api_image" "$sha_tag"
else
    printf 'Building %s:%s for %s.\n' "$api_image" "$sha_tag" "$platforms"
    docker buildx build \
        --file PitakaApp.Api/Dockerfile \
        --target final \
        --platform "$platforms" \
        --label "org.opencontainers.image.revision=$commit" \
        --annotation "index:org.opencontainers.image.revision=$commit" \
        --tag "$api_image:$sha_tag" \
        --push \
        .
fi

tag_identity="$(pitaka_read_image_identity "$api_image:$sha_tag" "$commit")"
index_digest="$(jq -er '.indexDigest' <<<"$tag_identity")"
pinned_identity="$(pitaka_read_image_identity "$api_image@$index_digest" "$commit")"

if ! jq -en \
    --argjson tag_identity "$tag_identity" \
    --argjson pinned_identity "$pinned_identity" \
    '$tag_identity == $pinned_identity' >/dev/null; then
    fail "fixed tag and digest-pinned index identities differ"
fi

# Exercise the exact artifact from Docker Hub against disposable dependencies
# before moving the main alias.
for platform in linux/amd64 linux/arm64; do
    "$script_directory/smoke-published-images.sh" "$commit" "$index_digest" "$platform"
done

post_smoke_tag_identity="$(pitaka_read_image_identity "$api_image:$sha_tag" "$commit")"
if ! jq -en \
    --argjson before "$tag_identity" \
    --argjson after "$post_smoke_tag_identity" \
    '$before == $after' >/dev/null; then
    fail "fixed tag changed while smoke tests were running"
fi

post_smoke_pinned_identity="$(pitaka_read_image_identity "$api_image@$index_digest" "$commit")"
if ! jq -en \
    --argjson before "$pinned_identity" \
    --argjson after "$post_smoke_pinned_identity" \
    '$before == $after' >/dev/null; then
    fail "digest-pinned index changed while smoke tests were running"
fi

pitaka_write_api_smoke_evidence \
    "$evidence_path" \
    "$source_repository" \
    "$commit" \
    "$api_image" \
    "$sha_tag" \
    "$tag_identity" \
    "$run_id" \
    "$run_attempt"

printf 'Published and smoke-tested the verified API image for %s.\n' "$commit"
