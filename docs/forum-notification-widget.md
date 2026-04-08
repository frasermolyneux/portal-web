# Forum Notification Widget — Invision Community Integration

## Overview

This document describes how to embed the XtremeIdiots Portal notification widget on the xtremeidiots.com Invision Community forum. The widget shows personalised notifications for logged-in forum users and a public activity feed for guests.

## Installation (2 steps)

### Step 1: Install the Settings Plugin

1. Upload `docs/invision-plugin/plugin.xml` via **ACP → System → Plugins → Install New Plugin**
2. Click the cog icon next to the plugin to open settings
3. Set **HMAC Shared Secret** — paste from Azure Key Vault (`external-widget-hmac-secret`)
4. Set **Portal Base URL** — `https://portal.xtremeidiots.com` (default)
5. Set **Enable Widget** — Yes

This plugin only registers settings — it does not inject any code or hooks.

### Step 2: Create a Custom PHP Block

1. Go to **ACP → Pages → Blocks → Create New Block → Custom → PHP**
2. Give it a name (e.g., "Portal Notifications")
3. Paste the following as the block content:

```php
$portalToken = '';
$portalUrl = rtrim( \IPS\Settings::i()->portal_base_url ?: 'https://portal.xtremeidiots.com', '/' );
$member = \IPS\Member::loggedIn();
if ( $member->member_id ) {
    $secret = \IPS\Settings::i()->portal_hmac_secret;
    if ( !empty( $secret ) ) {
        $memberId = (string) $member->member_id;
        $timestamp = (string) time();
        $hmac = hash_hmac( 'sha256', "{$memberId}:{$timestamp}", $secret );
        $portalToken = base64_encode( "{$memberId}:{$timestamp}:{$hmac}" );
    }
}
echo '<div id="portal-notifications-widget" data-token="' . htmlspecialchars( $portalToken, ENT_QUOTES, 'UTF-8' ) . '" data-portal-url="' . htmlspecialchars( $portalUrl, ENT_QUOTES, 'UTF-8' ) . '"></div>';
echo '<script src="' . htmlspecialchars( $portalUrl, ENT_QUOTES, 'UTF-8' ) . '/js/forum-widget.js" defer></script>';
```

4. Save the block
5. Place the block via the front-end widget manager — drag it into the sidebar or wherever you want it

## Prerequisites

- The HMAC shared secret must be configured in both:
  - **Portal**: via Azure App Configuration key `XtremeIdiots:ExternalWidget:HmacSecret` (auto-provisioned by portal-environments Terraform)
  - **Forum**: via the plugin settings page (Step 1 above)
- The portal must be deployed with the external notifications API (`/api/external/notifications`)

## How It Works

1. **Token generation** (forum-side): The plugin widget generates an HMAC-SHA256 signed token containing the logged-in forum member's ID and a Unix timestamp, signed with the shared secret.

2. **Token format**: `Base64({forumMemberId}:{timestampUnix}:{hmacHex})`

3. **Widget load**: The JavaScript widget (`forum-widget.js`, served from the portal) reads the token from the `data-token` attribute and calls the portal API.

4. **API response**:
   - **No token / invalid token**: Returns a public feed (recent admin actions)
   - **Valid token**: Portal maps the forum member ID to a UserProfile, returns personalised notifications scoped to the user's permissions

5. **Polling**: For authenticated users, the widget polls every 60 seconds for new notifications.

6. **Security**: Tokens expire after 5 minutes, preventing replay attacks. The HMAC signature prevents token forgery.

## Widget Features

- **Unread count badge** with notification bell
- **Notification list** with title, message, and relative timestamps
- **Mark as read** on click (individual) and "Read all" button
- **Unclaimed actions banner** for admins with pending actions
- **"View all in Portal"** link to the full notifications page
- **Graceful degradation** — shows public feed if not authenticated
- **Self-contained** — no jQuery or other dependencies required
- **Responsive** — max-width 400px, fits sidebar widgets

## Styling

The widget injects its own CSS with `xi-pw-` prefixed class names to avoid conflicts with the forum theme. The default styling uses a neutral colour scheme that should work with most themes.

To customise, override the styles in your forum theme CSS:
```css
/* Example: match dark theme */
.xi-portal-widget { background: #1a1a2e; color: #eee; border-color: #333; }
.xi-pw-header { background: #16213e; }
.xi-pw-item { color: #ddd; border-bottom-color: #333; }
.xi-pw-item:hover { background: #1a1a2e; color: #fff; }
```

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| Widget shows "Portal URL not configured" | Missing portal base URL in plugin settings | Check plugin settings in ACP |
| Widget shows "Unable to load notifications" | CORS or network error | Check browser console; verify portal CORS allows the forum domain |
| All users see public feed only | HMAC secret mismatch | Ensure the plugin's HMAC secret matches the Azure Key Vault value |
| Token always expired | Server clock drift | Ensure both servers have NTP synced clocks (±1 minute tolerance) |

## Updating

To update the plugin, upload the new `plugin.xml` via ACP → System → Plugins. Settings are preserved across updates.

To rotate the HMAC secret: update both the Azure Key Vault value and the plugin setting in ACP. Changes take effect immediately.
