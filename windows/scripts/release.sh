#!/usr/bin/env bash
set -euo pipefail
: "${RELEASE_TAG:?Release tag required}" "${GH_REPO:?Repository required}" "${GH_TOKEN:?Token required}"
version="${RELEASE_TAG#v}"
assets=("Bambu-Bar-$version-windows-x64-setup.exe" "Bambu-Bar-$version-windows-x64-portable.zip" SHA256SUMS)
cd package
for asset in "${assets[@]}"; do test -s "$asset"; done
sha256sum --check --strict SHA256SUMS

# A retry can resume an incomplete draft, but never overwrite a published release.
if draft=$(gh release view "$RELEASE_TAG" --json isDraft --jq .isDraft); then
    if [[ "$draft" != true ]]; then
        echo "Release $RELEASE_TAG is already published; leaving it unchanged."
        exit 0
    fi
else
    options=()
    if [[ "$version" == *-* ]]; then options+=(--prerelease); fi
    gh release create "$RELEASE_TAG" --verify-tag --draft --generate-notes \
        --title "Bambu Bar for Windows $version" "${options[@]}"
fi
gh release upload "$RELEASE_TAG" "${assets[@]}" --clobber
gh release edit "$RELEASE_TAG" --draft=false
