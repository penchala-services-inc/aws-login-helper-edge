#!/usr/bin/env bash
set -euo pipefail

# AWS Login Helper - companion app uninstaller (macOS)
#
# Removes everything install.sh created: the Edge native-messaging-host manifest, the
# installed binary/extension-id.txt, and (on confirmation) this host's own data directory -
# logs, and every dedicated/isolated Edge profile it ever created.
#
# Does NOT touch the extension itself - same note as the Windows build: remove that the
# normal way from edge://extensions.

INSTALL_ROOT="$HOME/Library/Application Support/AWS-Login-Helper-Host-Edge"
BINARY="$INSTALL_ROOT/NativeHost/AWS-Login-Helper-Host-Edge-Mac"
MANIFEST="$HOME/Library/Application Support/Microsoft Edge/NativeMessagingHosts/com.penchala_services_inc.awsloginhelper_edge.json"

if [[ -x "$BINARY" ]]; then
  "$BINARY" --unregister || true
elif [[ -f "$MANIFEST" ]]; then
  echo "Binary already gone - removing the manifest file directly."
  rm -f "$MANIFEST"
else
  echo "No manifest or binary found - nothing to unregister."
fi

if [[ -d "$INSTALL_ROOT" ]]; then
  read -r -p "Remove \"$INSTALL_ROOT\" (binary, logs, and every isolated Edge profile this tool created)? [y/N] " answer
  if [[ "$answer" =~ ^[Yy]$ ]]; then
    rm -rf "$INSTALL_ROOT"
    echo "Removed $INSTALL_ROOT."
  else
    echo "Left $INSTALL_ROOT in place."
  fi
fi

echo "Done. The Edge extension itself isn't touched by this script - remove it from edge://extensions if you want it gone too."
