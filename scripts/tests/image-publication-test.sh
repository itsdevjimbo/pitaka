#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
fixture_directory="$(mktemp -d)"
trap 'rm -rf "$fixture_directory"' EXIT

commit="0123456789abcdef0123456789abcdef01234567"
index_digest="sha256:1111111111111111111111111111111111111111111111111111111111111111"
amd64_digest="sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
arm64_digest="sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"

mkdir -p "$fixture_directory/bin"
cat >"$fixture_directory/bin/docker" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

if [[ -n "${PITAKA_TEST_DOCKER_LOG:-}" ]]; then
    printf 'API_IMAGE=%s API_PLATFORM=%s docker %s\n' \
        "${API_IMAGE:-}" "${API_PLATFORM:-}" "$*" >>"$PITAKA_TEST_DOCKER_LOG"
fi

case "${1:-} ${2:-} ${3:-}" in
    "buildx imagetools inspect")
        if [[ "${4:-}" != "--format" ]] || [[ "${5:-}" != "{{json .Manifest}}" ]]; then
            printf 'unexpected docker arguments: %s\n' "$*" >&2
            exit 1
        fi

        reference="${6:-}"
        if [[ "$reference" == *@sha256:* ]]; then
            printf '%s\n' "${PITAKA_TEST_PINNED_MANIFEST:-$PITAKA_TEST_MANIFEST}"
        elif [[ -n "${PITAKA_TEST_TAG_STATE_FILE:-}" ]]; then
            if [[ -f "$PITAKA_TEST_TAG_STATE_FILE" ]]; then
                printf '%s\n' "$PITAKA_TEST_MOVED_TAG_MANIFEST"
            else
                : >"$PITAKA_TEST_TAG_STATE_FILE"
                printf '%s\n' "${PITAKA_TEST_TAG_MANIFEST:-$PITAKA_TEST_MANIFEST}"
            fi
        else
            printf '%s\n' "${PITAKA_TEST_TAG_MANIFEST:-$PITAKA_TEST_MANIFEST}"
        fi
        ;;
    "image inspect --format")
        printf '%s\n' "$PITAKA_TEST_RUNTIME_REVISION"
        ;;
    "compose --project-name "*)
        if [[ "$*" == *" run --rm migration" ]] && \
            [[ "${PITAKA_TEST_FAIL_MIGRATION_PLATFORM:-}" == "${API_PLATFORM:-}" ]]; then
            exit 1
        fi
        ;;
    *)
        printf 'unexpected docker arguments: %s\n' "$*" >&2
        exit 1
        ;;
esac
EOF
chmod +x "$fixture_directory/bin/docker"

cat >"$fixture_directory/bin/curl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

arguments="$*"
response_file=""
while (($#)); do
    if [[ "$1" == "--output" ]]; then
        response_file="$2"
        shift 2
        continue
    fi
    shift
done

if [[ "$arguments" == *"http://localhost:18080/openapi/v1.json"* ]] && \
    [[ "${PITAKA_TEST_FAIL_API_PLATFORM:-}" == "${API_PLATFORM:-}" ]]; then
    exit 22
fi

if [[ -n "$response_file" ]]; then
    : >"$response_file"
fi
printf '200'
EOF
chmod +x "$fixture_directory/bin/curl"

cat >"$fixture_directory/bin/sleep" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
chmod +x "$fixture_directory/bin/sleep"

manifest="$(jq -cn \
    --arg revision "$commit" \
    --arg index_digest "$index_digest" \
    --arg amd64_digest "$amd64_digest" \
    --arg arm64_digest "$arm64_digest" \
    '{
        schemaVersion: 2,
        mediaType: "application/vnd.oci.image.index.v1+json",
        digest: $index_digest,
        annotations: {"org.opencontainers.image.revision": $revision},
        manifests: [
            {
                digest: $amd64_digest,
                platform: {os: "linux", architecture: "amd64"}
            },
            {
                digest: $arm64_digest,
                platform: {os: "linux", architecture: "arm64"}
            },
            {
                digest: "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                annotations: {"vnd.docker.reference.type": "attestation-manifest"},
                platform: {os: "unknown", architecture: "unknown"}
            }
        ]
    }')"

actual="$fixture_directory/identity.json"
PATH="$fixture_directory/bin:$PATH" \
    PITAKA_TEST_MANIFEST="$manifest" \
    "$repository_root/scripts/inspect-published-image.sh" \
    "jimbodev0530/pitaka-api:sha-$commit" "$commit" >"$actual"

jq -e \
    --arg revision "$commit" \
    --arg index_digest "$index_digest" \
    --arg amd64_digest "$amd64_digest" \
    --arg arm64_digest "$arm64_digest" \
    '
        keys == ["indexDigest", "platforms", "sourceRevision"] and
        .sourceRevision == $revision and
        .indexDigest == $index_digest and
        .platforms == {
            "linux/amd64": $amd64_digest,
            "linux/arm64": $arm64_digest
        }
    ' "$actual" >/dev/null

printf 'image-publication-test: fixed image identity is captured.\n'

wrong_revision_manifest="$(jq \
    '.annotations["org.opencontainers.image.revision"] = "fedcba9876543210fedcba9876543210fedcba98"' \
    <<<"$manifest")"
if PATH="$fixture_directory/bin:$PATH" \
    PITAKA_TEST_MANIFEST="$wrong_revision_manifest" \
    "$repository_root/scripts/inspect-published-image.sh" \
    "jimbodev0530/pitaka-api:sha-$commit" "$commit" >/dev/null 2>&1; then
    printf 'image inspector accepted the wrong source revision\n' >&2
    exit 1
fi

duplicate_platform_manifest="$(jq \
    '.manifests[1].platform.architecture = "amd64"' <<<"$manifest")"
if PATH="$fixture_directory/bin:$PATH" \
    PITAKA_TEST_MANIFEST="$duplicate_platform_manifest" \
    "$repository_root/scripts/inspect-published-image.sh" \
    "jimbodev0530/pitaka-api:sha-$commit" "$commit" >/dev/null 2>&1; then
    printf 'image inspector accepted duplicate linux/amd64 descriptors\n' >&2
    exit 1
fi

printf 'image-publication-test: revision and platform descriptor mismatches are rejected.\n'

contradictory_pinned_manifest="$(jq \
    '.manifests |= map(
        if .platform.os == "linux" and .platform.architecture == "arm64"
        then .digest = "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
        else .
        end
    )' <<<"$manifest")"

publisher_error="$fixture_directory/publisher-error.txt"
set +e
PATH="$fixture_directory/bin:$PATH" \
    PITAKA_TEST_MANIFEST="$manifest" \
    PITAKA_TEST_TAG_MANIFEST="$manifest" \
    PITAKA_TEST_PINNED_MANIFEST="$contradictory_pinned_manifest" \
    GITHUB_SHA="$commit" \
    GITHUB_REPOSITORY="itsdevjimbo/pitaka" \
    GITHUB_RUN_ID="12345" \
    GITHUB_RUN_ATTEMPT="2" \
    PITAKA_EVIDENCE_PATH="$fixture_directory/evidence.json" \
    "$repository_root/scripts/publish-images.sh" >"$fixture_directory/publisher-output.txt" 2>"$publisher_error"
publisher_status=$?
set -e

if [[ "$publisher_status" -eq 0 ]]; then
    printf 'expected contradictory tag and digest identities to fail publication\n' >&2
    exit 1
fi
if ! grep -q 'fixed tag and digest-pinned index identities differ' "$publisher_error"; then
    printf 'publication failed for the wrong reason:\n' >&2
    sed -n '1,120p' "$publisher_error" >&2
    exit 1
fi

printf 'image-publication-test: contradictory digest identity is rejected.\n'

docker_log="$fixture_directory/docker.log"
PATH="$fixture_directory/bin:$PATH" \
    PITAKA_TEST_DOCKER_LOG="$docker_log" \
    PITAKA_TEST_RUNTIME_REVISION="$commit" \
    "$repository_root/scripts/smoke-published-images.sh" \
    "$commit" "$index_digest" "linux/arm64" >/dev/null

if ! grep -q "API_IMAGE=jimbodev0530/pitaka-api@$index_digest API_PLATFORM=linux/arm64 docker compose" \
    "$docker_log"; then
    printf 'smoke did not use the digest-pinned image with explicit platform selection:\n' >&2
    sed -n '1,120p' "$docker_log" >&2
    exit 1
fi

printf 'image-publication-test: smoke is digest-pinned with an explicit platform.\n'

: >"$docker_log"
PATH="$fixture_directory/bin:$PATH" \
    PITAKA_TEST_DOCKER_LOG="$docker_log" \
    PITAKA_TEST_MANIFEST="$manifest" \
    PITAKA_TEST_RUNTIME_REVISION="$commit" \
    GITHUB_SHA="$commit" \
    GITHUB_REPOSITORY="itsdevjimbo/pitaka" \
    GITHUB_RUN_ID="12345" \
    GITHUB_RUN_ATTEMPT="2" \
    PITAKA_EVIDENCE_PATH="$fixture_directory/evidence.json" \
    "$repository_root/scripts/publish-images.sh" >/dev/null

for platform in linux/amd64 linux/arm64; do
    if ! grep -q "API_IMAGE=jimbodev0530/pitaka-api@$index_digest API_PLATFORM=$platform docker compose" \
        "$docker_log"; then
        printf 'publisher did not smoke %s\n' "$platform" >&2
        exit 1
    fi
done

printf 'image-publication-test: publisher requires both platform smokes.\n'

evidence="$fixture_directory/evidence.json"
if [[ ! -f "$evidence" ]]; then
    printf 'publisher did not create smoke evidence\n' >&2
    exit 1
fi

jq -e \
    --arg source_sha "$commit" \
    --arg index_digest "$index_digest" \
    --arg amd64_digest "$amd64_digest" \
    --arg arm64_digest "$arm64_digest" \
    '
        keys == ["image", "run", "schemaVersion", "source"] and
        .schemaVersion == 1 and
        .source == {
            repository: "itsdevjimbo/pitaka",
            sha: $source_sha
        } and
        .image == {
            repository: "jimbodev0530/pitaka-api",
            fixedTag: ("sha-" + $source_sha),
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
        } and
        .run == {
            id: "12345",
            attempt: 2
        }
    ' "$evidence" >/dev/null

if (( $(wc -c <"$evidence") > 16384 )); then
    printf 'smoke evidence exceeded its 16 KiB bound\n' >&2
    exit 1
fi

printf 'image-publication-test: successful publisher writes bounded run-scoped evidence.\n'

"$repository_root/scripts/validate-api-smoke-evidence.sh" "$evidence"
malformed_evidence="$fixture_directory/malformed-evidence.json"
jq '.unexpected = true' "$evidence" >"$malformed_evidence"
if "$repository_root/scripts/validate-api-smoke-evidence.sh" "$malformed_evidence" 2>/dev/null; then
    printf 'strict evidence validation accepted an unknown field\n' >&2
    exit 1
fi

printf 'image-publication-test: evidence validation rejects fields outside schema version 1.\n'

for expected_workflow_line in \
    'PITAKA_EVIDENCE_PATH: ${{ runner.temp }}/api-smoke-evidence-${{ github.run_id }}-${{ github.run_attempt }}.json' \
    'uses: actions/upload-artifact@v4' \
    'name: api-smoke-evidence-${{ github.run_id }}-${{ github.run_attempt }}' \
    'if-no-files-found: error' \
    'overwrite: false'; do
    if ! grep -Fq "$expected_workflow_line" "$repository_root/.github/workflows/build.yml"; then
        printf 'publisher workflow is missing: %s\n' "$expected_workflow_line" >&2
        exit 1
    fi
done

if ! grep -Fq 'run: ./scripts/tests/image-publication-test.sh' \
    "$repository_root/.github/workflows/tests.yml"; then
    printf 'pull request CI does not run the image publication acceptance test\n' >&2
    exit 1
fi

printf 'image-publication-test: evidence upload is mandatory and covered by pull request CI.\n'

for failure in migration:linux/amd64 api:linux/arm64; do
    failure_kind="${failure%%:*}"
    failure_platform="${failure#*:}"
    failure_evidence="$fixture_directory/$failure_kind-failure-evidence.json"
    failure_error="$fixture_directory/$failure_kind-failure-error.txt"
    if [[ "$failure_kind" == "migration" ]]; then
        failure_environment="PITAKA_TEST_FAIL_MIGRATION_PLATFORM"
    else
        failure_environment="PITAKA_TEST_FAIL_API_PLATFORM"
    fi

    set +e
    env \
        PATH="$fixture_directory/bin:$PATH" \
        PITAKA_TEST_MANIFEST="$manifest" \
        PITAKA_TEST_RUNTIME_REVISION="$commit" \
        "$failure_environment=$failure_platform" \
        GITHUB_SHA="$commit" \
        GITHUB_REPOSITORY="itsdevjimbo/pitaka" \
        GITHUB_RUN_ID="12345" \
        GITHUB_RUN_ATTEMPT="2" \
        PITAKA_EVIDENCE_PATH="$failure_evidence" \
        "$repository_root/scripts/publish-images.sh" >/dev/null 2>"$failure_error"
    failure_status=$?
    set -e

    if [[ "$failure_status" -eq 0 ]]; then
        printf 'expected %s failure on %s to fail publication\n' \
            "$failure_kind" "$failure_platform" >&2
        exit 1
    fi
    if [[ -e "$failure_evidence" ]]; then
        printf '%s failure still produced successful evidence\n' "$failure_kind" >&2
        exit 1
    fi
done

printf 'image-publication-test: either platform migration or API failure rejects publication.\n'

moved_tag_manifest="$(jq \
    '.digest = "sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"' \
    <<<"$manifest")"
tag_state_file="$fixture_directory/tag-state"
movement_error="$fixture_directory/movement-error.txt"
set +e
PATH="$fixture_directory/bin:$PATH" \
    PITAKA_TEST_MANIFEST="$manifest" \
    PITAKA_TEST_TAG_MANIFEST="$manifest" \
    PITAKA_TEST_PINNED_MANIFEST="$manifest" \
    PITAKA_TEST_MOVED_TAG_MANIFEST="$moved_tag_manifest" \
    PITAKA_TEST_TAG_STATE_FILE="$tag_state_file" \
    PITAKA_TEST_RUNTIME_REVISION="$commit" \
    GITHUB_SHA="$commit" \
    GITHUB_REPOSITORY="itsdevjimbo/pitaka" \
    GITHUB_RUN_ID="12345" \
    GITHUB_RUN_ATTEMPT="2" \
    PITAKA_EVIDENCE_PATH="$fixture_directory/evidence.json" \
    "$repository_root/scripts/publish-images.sh" >/dev/null 2>"$movement_error"
movement_status=$?
set -e

if [[ "$movement_status" -eq 0 ]]; then
    printf 'expected a fixed tag that moved during smoke to fail publication\n' >&2
    exit 1
fi
if ! grep -q 'fixed tag changed while smoke tests were running' "$movement_error"; then
    printf 'fixed tag movement failed for the wrong reason:\n' >&2
    sed -n '1,120p' "$movement_error" >&2
    exit 1
fi

printf 'image-publication-test: fixed tag movement during smoke is rejected.\n'
