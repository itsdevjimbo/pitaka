#!/usr/bin/env bash

pitaka_is_full_commit_sha() {
    [[ "$1" =~ ^[[:xdigit:]]{40}$ ]]
}

pitaka_read_image_identity() {
    local reference="$1"
    local expected_commit="$2"
    local manifest

    manifest="$(docker buildx imagetools inspect --format '{{json .Manifest}}' "$reference")" \
        || {
            printf 'container-images: could not inspect %s\n' "$reference" >&2
            return 1
        }

    jq -e \
        --arg reference "$reference" \
        --arg expected_commit "$expected_commit" \
        '
            def digest: test("^sha256:[0-9a-f]{64}$");
            def platform_manifests:
                [.manifests[]? | select(.platform.os == "linux")];

            if .annotations["org.opencontainers.image.revision"] != $expected_commit then
                error(
                    "\($reference) does not report expected source revision \($expected_commit)"
                )
            elif (.digest | type != "string" or (digest | not)) then
                error("\($reference) has no valid index digest")
            elif (
                (platform_manifests | length) != 2 or
                (platform_manifests | map(.platform.architecture) | sort) != ["amd64", "arm64"] or
                (platform_manifests | any(.digest | type != "string" or (digest | not)))
            ) then
                error(
                    "\($reference) must contain exactly one linux/amd64 and one linux/arm64 manifest"
                )
            else
                {
                    sourceRevision: $expected_commit,
                    indexDigest: .digest,
                    platforms: (
                        platform_manifests
                        | map({key: "linux/\(.platform.architecture)", value: .digest})
                        | from_entries
                    )
                }
            end
        ' <<<"$manifest"
}

pitaka_validate_image() {
    local image="$1"
    local tag="$2"
    local expected_commit="$3"
    local reference="$image:$tag"

    pitaka_read_image_identity "$reference" "$expected_commit" >/dev/null
}
