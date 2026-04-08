# Forum Notification Widget — Invision Community Integration

## Overview

This document describes how to embed the XtremeIdiots Portal notification widget on the xtremeidiots.com Invision Community forum. The widget shows personalised notifications for logged-in forum users and a public activity feed for guests.

## Prerequisites

1. The HMAC shared secret must be configured in both:
   - **Portal**: via Azure App Configuration key `XtremeIdiots:ExternalWidget:HmacSecret` (auto-provisioned by portal-environments Terraform)
   - **Forum**: as a custom Invision Community setting (see step 2 below)

2. The portal must be deployed with the external notifications API (`/api/external/notifications`)

## Step 1: Add the HMAC Secret to Invision Community

1. Go to **ACP → System → Settings** (or use a custom plugin settings page)
2. Add a custom setting:
   - **Key**: `portal_hmac_secret`
   - **Value**: Copy the HMAC secret from Azure Key Vault (`external-widget-hmac-secret` in the shared Key Vault)

> **Important**: The secret value must match exactly between the portal and forum. After the initial Terraform apply, retrieve the value from Azure Key Vault and paste it into the forum setting.

## Step 2: Add the Widget to the Forum Template

Edit the Invision Community theme template where you want the widget to appear (e.g., sidebar, footer, or a custom page block).

### Option A: Global Template (sidebar on all pages)

Edit the **globalTemplate** or **sidebar** template in your theme:

```html
{{if member.member_id}}
    <?php
        $secret = \IPS\Settings::i()->portal_hmac_secret;
        $memberId = \IPS\Member::loggedIn()->member_id;
        $timestamp = time();
        $hmac = hash_hmac('sha256', "{$memberId}:{$timestamp}", $secret);
        $token = base64_encode("{$memberId}:{$timestamp}:{$hmac}");
    ?>
    <div id="portal-notifications-widget"
         data-token="<?php echo $token; ?>"
         data-portal-url="https://portal.xtremeidiots.com">
    </div>
{{else}}
    <div id="portal-notifications-widget"
         data-token=""
         data-portal-url="https://portal.xtremeidiots.com">
    </div>
{{endif}}
<script src="https://portal.xtremeidiots.com/js/forum-widget.js" defer></script>
```

### Option B: Using an Invision Community Plugin Hook

If you prefer not to mix PHP into templates, create a simple plugin:

**File: `hooks/portalWidget.php`**
```php
class portalWidget
{
    public function globalTemplate($html)
    {
        $member = \IPS\Member::loggedIn();
        $token = '';

        if ($member->member_id) {
            $secret = \IPS\Settings::i()->portal_hmac_secret;
            $memberId = $member->member_id;
            $timestamp = time();
            $hmac = hash_hmac('sha256', "{$memberId}:{$timestamp}", $secret);
            $token = base64_encode("{$memberId}:{$timestamp}:{$hmac}");
        }

        $widget = <<<HTML
<div id="portal-notifications-widget"
     data-token="{$token}"
     data-portal-url="https://portal.xtremeidiots.com">
</div>
<script src="https://portal.xtremeidiots.com/js/forum-widget.js" defer></script>
HTML;

        // Insert before </body>
        return str_replace('</body>', $widget . '</body>', $html);
    }
}
```

### Option C: Custom HTML Block (simplest)

If you just want a quick test, use the **Pages** app or a **Custom Block**:

1. Go to **ACP → Pages → Blocks → Create New Block**
2. Choose **Custom HTML**
3. Paste:
```html
<?php
$token = '';
$member = \IPS\Member::loggedIn();
if ($member->member_id) {
    $secret = \IPS\Settings::i()->portal_hmac_secret;
    $memberId = $member->member_id;
    $timestamp = time();
    $hmac = hash_hmac('sha256', "{$memberId}:{$timestamp}", $secret);
    $token = base64_encode("{$memberId}:{$timestamp}:{$hmac}");
}
?>
<div id="portal-notifications-widget"
     data-token="<?php echo $token; ?>"
     data-portal-url="https://portal.xtremeidiots.com">
</div>
<script src="https://portal.xtremeidiots.com/js/forum-widget.js" defer></script>
```
4. Place the block in your desired sidebar/widget area

## How It Works

1. **Token generation** (forum-side): When a logged-in forum user loads a page, the PHP template generates an HMAC-SHA256 signed token containing their forum member ID and a Unix timestamp, signed with the shared secret.

2. **Token format**: `Base64({forumMemberId}:{timestampUnix}:{hmacHex})`

3. **Widget load**: The JavaScript widget (`forum-widget.js`) reads the token from the `data-token` attribute and calls the portal API.

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
| Widget shows "Portal URL not configured" | Missing `data-portal-url` attribute | Ensure the attribute is set in the template |
| Widget shows "Unable to load notifications" | CORS or network error | Check browser console; verify portal CORS allows the forum domain |
| All users see public feed only | HMAC secret mismatch | Ensure the forum `portal_hmac_secret` matches the Azure Key Vault value |
| Token always expired | Server clock drift | Ensure both servers have NTP synced clocks (±1 minute tolerance) |
