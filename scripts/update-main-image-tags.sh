#!/usr/bin/env bash
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "$script_directory/lib/container-images.sh"

commit="${GITHUB_SHA:?GITHUB_SHA must be set by the workflow}"
if ! pitaka_is_full_commit_sha "$commit"; then
    printf 'update-main-image-tags: expected a full 40-character commit SHA, got %s\n' "$commit" >&2
    exit 2
fi

api_image="jimbodev0530/pitaka-api"
migrations_image="jimbodev0530/pitaka-migrations"

resolve_main() {
    local revision
    revision="$(git ls-remote origin refs/heads/main | awk 'NR == 1 { print $1 }')"
    if [[ -z "$revision" ]]; then
        printf 'update-main-image-tags: could not resolve origin/main\n' >&2
        return 1
    fi
    printf '%s\n' "$revision"
}

fixed_pair_is_valid() {
    local revision="$1"
    pitaka_validate_image "$api_image" "sha-$revision" "$revision" \
        && pitaka_validate_image "$migrations_image" "sha-$revision" "$revision"
}

advance_tags_to() {
    local revision="$1"
    local sha_tag="sha-$revision"

    # Docker Hub does not make the two moving-tag writes atomic. The caller
    # verifies both immutable artifacts before writing either moving tag.
    docker buildx imagetools create \
        --annotation "index:org.opencontainers.image.revision=$revision" \
        --tag "$api_image:main" "$api_image:$sha_tag"
    docker buildx imagetools create \
        --annotation "index:org.opencontainers.image.revision=$revision" \
        --tag "$migrations_image:main" "$migrations_image:$sha_tag"

    pitaka_validate_image "$api_image" main "$revision"
    pitaka_validate_image "$migrations_image" main "$revision"
}

target="$(resolve_main)"
if [[ "$target" != "$commit" ]] && ! fixed_pair_is_valid "$target"; then
    printf 'Leaving main tags unchanged: %s is no longer origin/main, and the current main image pair is not ready.\n' \
        "$commit"
    exit 0
fi

for attempt in 1 2 3; do
    if ! fixed_pair_is_valid "$target"; then
        printf 'update-main-image-tags: the current main image pair %s is not ready; refusing to advance aliases\n' \
            "$target" >&2
        exit 1
    fi

    advance_tags_to "$target"

    current_main="$(resolve_main)"
    if [[ "$current_main" == "$target" ]]; then
        printf 'Advanced both main image tags to %s.\n' "$target"
        exit 0
    fi

    printf 'origin/main advanced from %s to %s while tags were moving; reconciling to the latest verified pair.\n' \
        "$target" "$current_main"
    target="$current_main"
done

printf 'update-main-image-tags: origin/main kept advancing during tag updates; a later promoter must reconcile the aliases\n' >&2
exit 1
