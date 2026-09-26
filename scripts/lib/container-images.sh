#!/usr/bin/env bash

pitaka_is_full_commit_sha() {
    [[ "$1" =~ ^[[:xdigit:]]{40}$ ]]
}

pitaka_validate_image() {
    local image="$1"
    local tag="$2"
    local expected_commit="$3"
    local reference="$image:$tag"
    local index revision platforms_found

    index="$(docker buildx imagetools inspect --raw "$reference")" \
        || {
            printf 'container-images: could not inspect %s\n' "$reference" >&2
            return 1
        }

    revision="$(jq -er '.annotations["org.opencontainers.image.revision"] // empty' <<<"$index")" \
        || {
            printf 'container-images: %s has no source-revision annotation\n' "$reference" >&2
            return 1
        }
    if [[ "$revision" != "$expected_commit" ]]; then
        printf 'container-images: %s points to source %s, expected %s\n' \
            "$reference" "$revision" "$expected_commit" >&2
        return 1
    fi

    platforms_found="$(jq -er '[.manifests[]?.platform | select(.os == "linux") | .architecture] | unique | sort | join(",")' <<<"$index")" \
        || {
            printf 'container-images: %s has no readable platform manifest list\n' "$reference" >&2
            return 1
        }
    if [[ "$platforms_found" != "amd64,arm64" ]]; then
        printf 'container-images: %s supports [%s], expected linux/amd64 and linux/arm64\n' \
            "$reference" "$platforms_found" >&2
        return 1
    fi
}
