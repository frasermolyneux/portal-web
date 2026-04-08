# XtremeIdiots Portal Notifications — Invision Community Plugin

## What This Plugin Does

Registers 3 ACP settings for the portal notification widget:
- **Enable Widget** (Yes/No toggle)
- **HMAC Shared Secret** (for signing auth tokens)
- **Portal Base URL** (defaults to `https://portal.xtremeidiots.com`)

This plugin **only provides settings** — the widget itself is delivered via a custom PHP block (see below).

## Installation

1. Upload `plugin.xml` via **ACP → System → Plugins → Install New Plugin**
2. Click the cog icon next to the plugin → configure the 3 settings
3. Create a custom PHP block: **ACP → Pages → Blocks → Create New Block → Custom → PHP**
4. Paste the PHP snippet from `docs/forum-notification-widget.md` as the block content (use `echo`, not `return`)
5. Place the block via the front-end widget manager into the sidebar

See `docs/forum-notification-widget.md` for the full snippet and troubleshooting guide.

## Why a Custom Block?

IPS4's plugin template compiler doesn't support `hash_hmac()` and `base64_encode()` calls needed for HMAC token generation. A custom PHP block executes raw PHP directly, bypassing the template compiler.
