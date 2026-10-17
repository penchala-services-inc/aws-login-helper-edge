# Development (AWS Login Helper - Edge)

Building from source, running an unpacked copy locally, and how the
extension + companion app work together under the hood. See
[README.md](README.md) for what the extension is and how to install it.

## Running it locally

For building from source, or testing before the extension is published.

### 1. Build the native host

Requires the .NET 8 SDK on a Windows machine.

```
cd native-host
dotnet publish -c Release
```

Produces `native-host/bin/Release/net8.0/win-x64/publish/AWS-Login-Helper-Host-Edge.exe`,
a single self-contained `.exe`. Try it by hand:

```
AWS-Login-Helper-Host-Edge.exe --version
AWS-Login-Helper-Host-Edge.exe --register
```

`--register` writes `native-messaging-host.json` next to itself and a
`HKEY_CURRENT_USER\Software\Microsoft\Edge\NativeMessagingHosts\com.penchala_services_inc.awsloginhelper_edge`
value pointing at it - both without admin rights. You can also sanity-check
the exe's stdio framing directly, bypassing Edge entirely, with
`test-native-host.ps1`.

### 2. Build and run the installer

Requires the WiX v4 CLI (`dotnet tool install --global wix`) plus the .NET SDK.

```
cd installer/AWS-Login-Helper-Host-Edge
dotnet build -c Release
```

Produces `installer/AWS-Login-Helper-Host-Edge/bin/Release/AWS-Login-Helper-Host-Edge.msi`.
Double-clicking it (or `msiexec /i AWS-Login-Helper-Host-Edge.msi`) installs
per-user: copies the exe + `extension-id.txt` to
`%LocalAppData%\AWS-Login-Helper-Host-Edge\NativeHost\`, registers the native
messaging host with Edge, and (if the extension isn't already installed)
opens the Edge Add-ons listing so it can be added with one click.

**`EXTENSIONID` defaults to the real, published Edge Add-ons id**
(`fgkidhckfhjhahiechlealkomjlipjhl`, same as `extension-id.txt`). If the
listing's id ever changes, edit the `Value` in
`installer/AWS-Login-Helper-Host-Edge/Package.wxs` and rebuild, or supply a
different one at install time the same way described next.

### 3. Load the extension unpacked

Edge will run any folder as an extension in Developer mode - no store
review needed to test:

- Go to `edge://extensions`, turn on **Developer mode**, click
  **Load unpacked**, and select this project's `extension/` folder.
- Edge assigns it its own id (different from the published one) - copy it
  from the extension's card.
- Register that id with the native host - either at install time:
  ```
  msiexec /i AWS-Login-Helper-Host-Edge.msi EXTENSIONID=<your unpacked extension's id>
  ```
  or, if already installed, without reinstalling:
  ```
  "%LocalAppData%\AWS-Login-Helper-Host-Edge\NativeHost\AWS-Login-Helper-Host-Edge.exe" --register <your unpacked extension's id>
  ```
  Either way this is additive - `extension-id.txt` keeps one id per line, so
  a locally-loaded copy and the published extension can both work side by
  side on the same machine without breaking each other.

### 4. Verify

- `edge://extensions` -> the extension is installed and enabled.
- Open the extension's popup -> "Check companion app" reports OK.
- Sign into the AWS IIC portal, click an "Isolated (Window)" link -> a
  second, separate Edge window opens, already signed into that
  account/role. `edge://version` inside it shows a different profile path
  (under `AWS-Login-Helper-Host-Edge\EdgeProfiles\...`) than your normal Edge -
  that's expected (see "Development" below).

## Development

### What's different from the Chrome build

- **Launches Edge, not Chrome.** `EdgeManager.FindEdgeExe()` looks in
  `%ProgramFiles%\Microsoft\Edge\Application\msedge.exe`,
  `%ProgramFiles(x86)%\...`, `%LocalAppData%\Microsoft\Edge\Application\msedge.exe`,
  and the `App Paths` registry key, in that order.
- **Its own registry hive** -
  `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.penchala_services_inc.awsloginhelper_edge`,
  not Chrome's `Software\Google\Chrome\...` key.
- **Its own install folder** - `%LocalAppData%\AWS-Login-Helper-Host-Edge\`,
  separate from the Chrome build's `AWS-Login-Helper-Host-Chrome\`.
- **Its own extension store** - the Microsoft Edge Add-ons store, not the
  Chrome Web Store.
- **`allowed_origins` still uses the `chrome-extension://` scheme** - not a
  bug, Edge extensions use the same URI scheme Chrome does; there's no
  separate `edge-extension://` scheme.

Everything else - the CDP-based banner/title injection, the cold-start
prewarm mechanism, and the additive multi-extension-id support - was
ported unchanged in logic from the Chrome build, since none of it is
Chrome-specific.

### The three pieces

```
extension/     Edge MV3 extension - runs in the user's normal, everyday Edge.
                Watches the AWS IIC/SSO portal, and asks the native host to
                open each account/role in its own isolated window.
native-host/   AWS-Login-Helper-Host-Edge.exe - a small, self-contained C#
                program that Edge launches via Native Messaging. It owns a
                SEPARATE, dedicated Edge instance where each AWS role gets
                its own profile, and uses the Chrome DevTools Protocol (CDP,
                which Edge implements identically since it's a Chromium
                fork) to copy the SSO cookie into the right one, open it,
                and label it.
installer/     A per-user MSI (WiX v4) that installs the native host and
                registers it with Edge - no admin rights needed anywhere.
native-host-mac/  The macOS build of the native host (same logic as
                native-host/, launches Microsoft Edge.app and registers under
                ~/Library/Application Support/Microsoft Edge/NativeMessagingHosts).
package-mac/   build.sh / install.sh / uninstall.sh that produce and install
                the Apple Silicon + Intel tarballs.
```

### Why a *separate* Edge instance, not the user's real profiles

Edge's remote debugging port (what CDP needs to set cookies in a profile
that isn't the one the extension runs in) can only be turned on when an
Edge **process** starts - it can't be attached retroactively to the user's
already-running everyday Edge, and Edge collapses every profile under one
`--user-data-dir` into a single process anyway, so a flag passed on a second
launch gets silently forwarded to (and ignored by) the already-running one.

The native host sidesteps both problems by launching Edge pointed at its
**own** `--user-data-dir` (`%LocalAppData%\AWS-Login-Helper-Host-Edge\EdgeProfiles`),
completely separate from the user's normal profile data. Every AWS
account/role becomes its own `--profile-directory` inside it - its own
cookie jar, its own window, persists across restarts, functionally
identical to a Firefox container.

### How a click flows end to end

1. `content.js` (running in the user's normal Edge) finds each account/role
   link on the IIC portal page and adds an "Isolated (Window)" link next to it.
2. Clicking it sends `openAccountRole` to `background.js`, which reads the
   current `.awsapps.com` cookies and hands them to the native host over a
   long-lived native-messaging connection.
3. The native host makes sure the dedicated Edge instance is running, opens
   (or reuses) that role's `--profile-directory` on a tab, sets the SSO
   cookie(s) over that tab's own CDP WebSocket (`Network.setCookie`), then
   navigates it straight to the real AWS console URL.
4. The user sees a normal Edge window, already authenticated, for that
   specific account/role.

### How the account/role banner and title are shown

A colored banner pinned to the bottom of the window (`account_id |
account_name | role_name`, red for Prod, green for NonProd/sandbox/test/dev,
dark blue otherwise) plus the account/role prefixed onto the tab title. This
is injected directly by the native host over the same CDP connection - a
small script evaluated in the page (`CdpClient.BuildConsoleBannerScript`),
right after the cookie sync. There is no extension loaded in the dedicated
instance at all (Chromium removed the `--load-extension` flag entirely
starting Chrome/Edge 137, June 2025, which is what an earlier version of
this relied on), so a lightweight watcher (`CdpClient.RegisterProfileWindowAsync`/
its instance watcher loop) polls for navigation - and for brand new tabs
opened by hand in that same window, e.g. ctrl-clicking a service in AWS's own
console nav - re-injecting the script and reporting the service visited back
to the extension (so "Recent Logins & Services" on the portal page stays
accurate) each time, without holding a debugger session attached the whole
time (which was tried first and caused AWS's own session-refresh flow to kick
the user out - see the comments in `CdpClient.cs` for the full history). A
plain refresh of a console tab (F5) keeps the same URL, so that alone can't
tell the watcher the page reloaded and wiped the banner - it also
periodically checks (`CdpClient.IsBannerPresentAsync`) whether the banner div
is still actually there and re-injects if not, without double-reporting it
as a new service visit.

Session expiry is shown in two places, both cookie-driven: the portal page
(`content.js`, reading the `x-amz-sso_authn` cookie) and the console banner
(the native host, reading the `aws-creds` cookie over the same CDP
connection right after the sync).

### Multiple isolated profiles

Each account/role's dedicated Edge profile can be listed and deleted from
the extension's options page (`edge://extensions` -> Details -> Extension
options) - useful for cleaning up old/unused ones. Deleting removes that
profile's own folder (cookies, session) under `EdgeProfiles\`; nothing else
is affected.

## Known limitations / things to revisit

- **Cold-start placeholder window still shows in the taskbar and Alt+Tab
  switcher.** The off-screen positioning trick
  (`--window-position=-32000,-32000 --window-size=1,1`) hides it visually
  but Windows still grants it a normal taskbar button and Alt+Tab entry. A
  real fix would need a Win32 `WS_EX_TOOLWINDOW` style on the window handle -
  not pursued, same as the Chrome build.
- **Cookie sync is `.awsapps.com` cookies only** - if AWS ever moves session
  state elsewhere, this needs updating.
- **First role opened after a reboot** takes an extra second or two, since
  that's when the dedicated Edge instance has to actually start.
- **No uninstall of the extension itself** - the MSI only manages the
  companion app. Removing the extension is done from `edge://extensions`
  like any other extension.
- **Edge must be installed** somewhere `EdgeManager.FindEdgeExe()` can find
  it (Program Files, per-user `%LocalAppData%`, or the registered
  "App Paths" registry key).
- **Intermittent CDP port-check delays** can happen on a machine where
  antivirus/EDR intercepts the native host's loopback HTTP calls - needs a
  Defender/EDR exclusion from IT, not a code fix, if it shows up.

## Release builds and versioning

See [CLAUDE.md](../CLAUDE.md) at the repo root for the versioning convention
(extension, native host, and installer all move together as one number) and
[../build-release.sh](../build-release.sh) for building release MSI/tarball
artifacts for all four (browser x OS) combinations in one pass. Each release
is logged in [releases.md](releases.md).
