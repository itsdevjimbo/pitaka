#!/usr/bin/env bash

readonly PITAKA_API_SMOKE_EVIDENCE_MAX_BYTES=16384

pitaka_validate_api_smoke_evidence() {
    local evidence_path="$1"
    local evidence_size

    if [[ ! -f "$evidence_path" ]]; then
        printf 'api-smoke-evidence: evidence file does not exist: %s\n' "$evidence_path" >&2
        return 1
    fi

    evidence_size="$(wc -c <"$evidence_path")"
    if ((evidence_size > PITAKA_API_SMOKE_EVIDENCE_MAX_BYTES)); then
        printf 'api-smoke-evidence: evidence exceeds %s bytes\n' \
            "$PITAKA_API_SMOKE_EVIDENCE_MAX_BYTES" >&2
        return 1
    fi

    jq -e '
        def exact_keys($expected):
            (keys | sort) == ($expected | sort);
        def sha256_digest:
            type == "string" and test("^sha256:[0-9a-f]{64}$");
        def successful_platform:
            type == "object" and
            exact_keys(["apiReadiness", "childDigest", "migration"]) and
            (.childDigest | sha256_digest) and
            .migration == "success" and
            .apiReadiness == "success";

        type == "object" and
        exact_keys(["image", "run", "schemaVersion", "source"]) and
        .schemaVersion == 1 and
        (.source | type == "object" and exact_keys(["repository", "sha"])) and
        (.source.repository | type == "string" and test("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) and
        (.source.sha | type == "string" and test("^[0-9a-f]{40}$")) and
        (.image | type == "object" and exact_keys(["fixedTag", "indexDigest", "platforms", "repository"])) and
        (.image.repository | type == "string" and length > 0) and
        .image.fixedTag == ("sha-" + .source.sha) and
        (.image.indexDigest | sha256_digest) and
        (.image.platforms | type == "object" and exact_keys(["linux/amd64", "linux/arm64"])) and
        (.image.platforms["linux/amd64"] | successful_platform) and
        (.image.platforms["linux/arm64"] | successful_platform) and
        (.run | type == "object" and exact_keys(["attempt", "id"])) and
        (.run.id | type == "string" and test("^[0-9]+$")) and
        (.run.attempt | type == "number" and floor == . and . >= 1)
    ' "$evidence_path" >/dev/null
}

pitaka_write_api_smoke_evidence() {
    local evidence_path="$1"
    local source_repository="$2"
    local source_sha="$3"
    local image_repository="$4"
    local fixed_tag="$5"
    local image_identity="$6"
    local run_id="$7"
    local run_attempt="$8"
    local temporary_path amd64_digest arm64_digest index_digest

    if [[ ! "$run_id" =~ ^[0-9]+$ ]] || [[ ! "$run_attempt" =~ ^[1-9][0-9]*$ ]]; then
        printf 'api-smoke-evidence: run ID and attempt must be positive decimal values\n' >&2
        return 1
    fi

    index_digest="$(jq -er '.indexDigest' <<<"$image_identity")"
    amd64_digest="$(jq -er '.platforms["linux/amd64"]' <<<"$image_identity")"
    arm64_digest="$(jq -er '.platforms["linux/arm64"]' <<<"$image_identity")"
    temporary_path="$(mktemp "$evidence_path.XXXXXX")"

    if ! jq -n \
        --arg source_repository "$source_repository" \
        --arg source_sha "$source_sha" \
        --arg image_repository "$image_repository" \
        --arg fixed_tag "$fixed_tag" \
        --arg index_digest "$index_digest" \
        --arg amd64_digest "$amd64_digest" \
        --arg arm64_digest "$arm64_digest" \
        --arg run_id "$run_id" \
        --argjson run_attempt "$run_attempt" \
        '{
            schemaVersion: 1,
            source: {
                repository: $source_repository,
                sha: $source_sha
            },
            image: {
                repository: $image_repository,
                fixedTag: $fixed_tag,
                indexDigest: $index_digest,
                platforms: {
                    "linux/amd64": {
                        childDigest: $amd64_digest,
                        migration: "success",
                        apiReadiness: "success"
                    },
                    "linux/arm64": {
                        childDigest: $arm64_digest,
                        migration: "success",
                        apiReadiness: "success"
                    }
                }
            },
            run: {
                id: $run_id,
                attempt: $run_attempt
            }
        }' >"$temporary_path"; then
        rm -f "$temporary_path"
        return 1
    fi

    if ! pitaka_validate_api_smoke_evidence "$temporary_path"; then
        rm -f "$temporary_path"
        return 1
    fi

    mv "$temporary_path" "$evidence_path"
}
