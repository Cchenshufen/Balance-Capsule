#!/bin/zsh
set -euo pipefail

script_dir="${0:A:h}"
project_root="${script_dir:h}"
dotnet_bin="${DOTNET_BIN:-dotnet}"
artifact_root="$project_root/artifacts/windows"
publish_dir="$artifact_root/publish-win-x64"
package_dir="$artifact_root/BalanceCapsule-1.2.15-win.13-x64"
zip_path="$artifact_root/BalanceCapsule-1.2.15-win.13-x64.zip"
checksum_path="$artifact_root/BalanceCapsule-1.2.15-win.13-x64.sha256"

rm -rf "$publish_dir" "$package_dir"
mkdir -p "$publish_dir" "$package_dir"

DOTNET_CLI_TELEMETRY_OPTOUT=1 "$dotnet_bin" publish \
  "$project_root/src/QuotaOrb.Windows/QuotaOrb.Windows.csproj" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --nologo \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$publish_dir"

cp "$publish_dir/BalanceCapsule.exe" "$package_dir/BalanceCapsule.exe"
cp "$project_root/docs/Balance-Capsule-Windows-安装说明.md" "$package_dir/README.md"

rm -f "$zip_path" "$checksum_path"
(
  cd "$package_dir"
  zip -9 -q "$zip_path" BalanceCapsule.exe README.md
)
(
  cd "$artifact_root"
  shasum -a 256 "${zip_path:t}" > "${checksum_path:t}"
)

file "$package_dir/BalanceCapsule.exe"
ls -lh "$package_dir/BalanceCapsule.exe" "$zip_path" "$checksum_path"
