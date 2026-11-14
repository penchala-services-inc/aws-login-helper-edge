# Microsoft Edge Add-ons listing copy

**Published.** Live listing: https://microsoftedge.microsoft.com/addons/detail/aws-login-helper/fgkidhckfhjhahiechlealkomjlipjhl
(no separate reviews-tab path the way the Chrome Web Store has `/reviews` -
reviews are left directly from this same listing page). This is the same id
used by the README's `EXTENSIONID`/`extension-id.txt` instructions, and by
the "Rate the extension" links in `content.js`, `options.html`, and the
console banner (`CdpClient.cs`).

Paste these into the Partner Center Edge Add-ons dashboard when publishing.
Search visibility depends mostly on relevance + engagement (installs,
ratings, low uninstall rate), not keyword density, so this copy leads with
the real search terms (AWS, SSO, IAM Identity Center / IIC, login,
multi-account) once, in plain language, rather than repeating them.

## Title

The listing title is pulled from `extension/manifest.json`'s `"name"`
field. Update that if you want this title to show:

```
AWS Login Helper - Multi-Account SSO & IIC Login
```

(48 characters. Currently set to plain `AWS Login Helper` - same name as
the Chrome build, so if both ever end up installed on the same machine,
`edge://extensions` won't visually distinguish them by name alone.)

## Short description (132-character limit)

Shown under the title in search results and the listing header.

```
Sign in once, open every AWS account & role in its own isolated window - fast multi-account IAM Identity Center (SSO/IIC) access.
```

(129 characters.)

## Detailed description

The first sentence or two double as the search-result snippet, so the core
keywords are up front.

```
AWS Login Helper makes working across many AWS accounts through AWS IAM Identity Center (successor to AWS SSO, still commonly called IIC) fast and frustration-free.

Sign into your AWS IAM Identity Center (SSO / IIC) access portal once. From then on, every account and role on your access list opens in its own separate, already-authenticated Edge window - no re-entering your password or MFA code every time you switch accounts, and no accidentally acting in the wrong account because two sessions got mixed up in the same browser profile.

Why teams use it:

• One login, every account - click any account/role on your AWS access portal and it opens instantly, already signed in.
• True isolation - each account/role gets its own separate browser profile and cookie jar, so sessions never bleed into each other.
• Session expiry at a glance - see exactly how long your SSO session and each console session have left, right on the page.
• One click to resume after expiry - if a session does expire, re-auth on the AWS access portal, come back, and one click on the window's note picks up right where you left off - no re-entering MFA in that window.
• Clean up anytime - manage and remove old isolated account profiles from the extension's options page.
• No manual profile switching - skip Edge's built-in profile switcher entirely; this handles it for you.

Requires the free AWS Login Helper companion app (a small, per-user installer - no admin rights needed). The download link and setup steps are on the extension's options page after you install it.

Built for engineers, SREs, and admins who work across many AWS accounts through AWS Organizations + IAM Identity Center SSO every day.
```

## Notes

- Partner Center review checks that requested permissions match what the
  description says the extension does - the permissions list
  (`cookies`, `storage`, `tabs`, `nativeMessaging`, `alarms`) all map
  directly to something described here (reading the SSO cookie, remembering
  settings, opening windows, talking to the companion app, keeping that
  connection warm). Keep new permissions and new description copy in sync if
  either changes.
- Category: **Productivity** or **Developer tools** fits best.
- A screenshot is required before publishing, same as the Chrome listing.
