#!/bin/zsh
set -euo pipefail

script_dir="${0:A:h}"
project_root="${script_dir:h}"
artifact_root="$project_root/artifacts/macos"
app_bundle="$artifact_root/Balance Capsule.app"
contents="$app_bundle/Contents"
binary_dir="$contents/MacOS"
resource_dir="$contents/Resources"
build_root="$artifact_root/build"
stage_root="$artifact_root/dmg-stage"
dmg_path="$artifact_root/BalanceCapsule-mac.15-arm64.dmg"
zip_path="$artifact_root/BalanceCapsule-mac.15-arm64.zip"
notarization_zip="$build_root/BalanceCapsule-notarization.zip"
signing_identity="${BALANCE_CAPSULE_CODESIGN_IDENTITY:--}"
notary_profile="${BALANCE_CAPSULE_NOTARY_PROFILE:-}"
sources=("$project_root"/macos/QuotaOrbMac/*.swift)

mkdir -p "$artifact_root" "$binary_dir" "$resource_dir" "$build_root"

swiftc \
  -swift-version 5 \
  -O \
  -whole-module-optimization \
  -target arm64-apple-macosx26.0 \
  -framework AppKit \
  -framework Foundation \
  -framework CoreImage \
  -framework ImageIO \
  -framework UniformTypeIdentifiers \
  "${sources[@]}" \
  -o "$build_root/BalanceCapsule-arm64"

cp "$build_root/BalanceCapsule-arm64" "$binary_dir/BalanceCapsule"

cp "$project_root/macos/Info.plist" "$contents/Info.plist"
cp "$project_root/macos/THIRD-PARTY-NOTICES.md" "$resource_dir/THIRD-PARTY-NOTICES.md"
chmod 755 "$binary_dir/BalanceCapsule"
mkdir -p "$build_root/icon-preview"
"$binary_dir/BalanceCapsule" --render-preview "$build_root/icon-preview"
icon_master="$build_root/icon-preview/app-icon.png"
iconset="$build_root/AppIcon.iconset"
rm -rf "$iconset"
mkdir -p "$iconset"
sips -z 16 16 "$icon_master" --out "$iconset/icon_16x16.png" >/dev/null
sips -z 32 32 "$icon_master" --out "$iconset/icon_16x16@2x.png" >/dev/null
sips -z 32 32 "$icon_master" --out "$iconset/icon_32x32.png" >/dev/null
sips -z 64 64 "$icon_master" --out "$iconset/icon_32x32@2x.png" >/dev/null
sips -z 128 128 "$icon_master" --out "$iconset/icon_128x128.png" >/dev/null
sips -z 256 256 "$icon_master" --out "$iconset/icon_128x128@2x.png" >/dev/null
sips -z 256 256 "$icon_master" --out "$iconset/icon_256x256.png" >/dev/null
sips -z 512 512 "$icon_master" --out "$iconset/icon_256x256@2x.png" >/dev/null
sips -z 512 512 "$icon_master" --out "$iconset/icon_512x512.png" >/dev/null
cp "$icon_master" "$iconset/icon_512x512@2x.png"
iconutil -c icns "$iconset" -o "$resource_dir/AppIcon.icns"

if [[ "$signing_identity" == "-" ]]; then
  print -u2 "warning: no Developer ID identity configured; producing an ad-hoc signed local build"
  codesign --force --deep --sign - "$app_bundle"
else
  codesign --force --deep --options runtime --timestamp --sign "$signing_identity" "$app_bundle"
fi

if [[ -n "$notary_profile" ]]; then
  if [[ "$signing_identity" == "-" ]]; then
    print -u2 "error: notarization requires BALANCE_CAPSULE_CODESIGN_IDENTITY"
    exit 1
  fi
  if [[ "$signing_identity" != "Developer ID Application:"* ]]; then
    print -u2 "error: public notarization requires a Developer ID Application identity"
    exit 1
  fi
  rm -f "$notarization_zip"
  ditto -c -k --sequesterRsrc --keepParent "$app_bundle" "$notarization_zip"
  xcrun notarytool submit "$notarization_zip" --keychain-profile "$notary_profile" --wait
  xcrun stapler staple "$app_bundle"
  xcrun stapler validate "$app_bundle"
fi

rm -rf "$stage_root"
mkdir -p "$stage_root"
cp -R "$app_bundle" "$stage_root/Balance Capsule.app"
ln -s /Applications "$stage_root/Applications"

rm -f "$dmg_path" "$zip_path"
diskutil image create from \
  --volumeName "Balance Capsule" \
  --format UDZO \
  "$stage_root" \
  "$dmg_path"

ditto -c -k --sequesterRsrc --keepParent "$app_bundle" "$zip_path"
(
  cd "$artifact_root"
  shasum -a 256 "$(basename "$dmg_path")" "$(basename "$zip_path")" > SHA256SUMS.txt
)

file "$binary_dir/BalanceCapsule"
codesign --verify --deep --strict --verbose=2 "$app_bundle"
if [[ -n "$notary_profile" ]]; then
  spctl --assess --type execute --verbose=2 "$app_bundle"
else
  spctl --assess --type execute --verbose=2 "$app_bundle" || true
fi

echo "Built: $dmg_path"
echo "Built: $zip_path"
