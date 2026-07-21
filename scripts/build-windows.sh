#!/bin/zsh
set -euo pipefail

script_dir="${0:A:h}"
project_root="${script_dir:h}"
dotnet_bin="${DOTNET_BIN:-dotnet}"
artifact_root="$project_root/artifacts/windows"
single_publish_dir="$artifact_root/publish-win-x64-single"
portable_publish_dir="$artifact_root/publish-win-x64-portable"
package_dir="$artifact_root/BalanceCapsule-1.2.15-win.15-x64"
exe_path="$artifact_root/BalanceCapsule-1.2.15-win.15-x64.exe"
zip_path="$artifact_root/BalanceCapsule-1.2.15-win.15-x64.zip"
checksum_path="$artifact_root/BalanceCapsule-1.2.15-win.15-x64.sha256"

rm -rf "$single_publish_dir" "$portable_publish_dir" "$package_dir"
mkdir -p "$single_publish_dir" "$portable_publish_dir" "$package_dir"

DOTNET_CLI_TELEMETRY_OPTOUT=1 "$dotnet_bin" publish \
  "$project_root/src/QuotaOrb.Windows/QuotaOrb.Windows.csproj" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --nologo \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:EnableCompressionInSingleFile=false \
  -o "$single_publish_dir"

DOTNET_CLI_TELEMETRY_OPTOUT=1 "$dotnet_bin" publish \
  "$project_root/src/QuotaOrb.Windows/QuotaOrb.Windows.csproj" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --nologo \
  -p:PublishSingleFile=false \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$portable_publish_dir"

cp "$single_publish_dir/BalanceCapsule.exe" "$exe_path"
cp -R "$portable_publish_dir"/. "$package_dir"/
cp "$project_root/docs/Balance-Capsule-Windows-安装说明.md" "$package_dir/README.md"

rm -f "$zip_path" "$checksum_path"
(
  cd "$package_dir"
  zip -9 -q -r "$zip_path" .
)
(
  cd "$artifact_root"
  shasum -a 256 "${exe_path:t}" "${zip_path:t}" > "${checksum_path:t}"
)

file "$exe_path" "$package_dir/BalanceCapsule.exe"
ls -lh "$exe_path" "$package_dir/BalanceCapsule.exe" "$zip_path" "$checksum_path"
