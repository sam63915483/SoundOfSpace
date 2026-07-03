# Cloudflare Setup Guide — Community Gallery Backend

One-time setup for the photo-upload server (spec: `docs/superpowers/specs/2026-07-03-photos-app-community-gallery-design.md`).
**When to do this:** any time before we build the server (build chunk 5). The local Photos app doesn't need any of this.
**Time:** ~20 minutes. **Cost:** $0 on the free tier.

> ⚠️ **One honest heads-up first:** enabling R2 (the image storage) requires putting a **credit card or PayPal** on file, even though you won't be charged inside the free tier (10 GB storage, millions of operations/month, zero egress fees). Cloudflare may place a small temporary pre-authorization hold to verify the card — that is a hold, not a charge.
> **If you don't want to add a card:** stop after Step 3 and tell Claude — the server design can switch from R2 to Workers KV (no card needed, ~1 GB free ≈ 3,000 photos). Slightly smaller limits, same game-side code.

---

## Step 1 — Create a Cloudflare account

1. Go to https://dash.cloudflare.com/sign-up
2. Sign up with your email + a password. Choose the **Free** plan if asked.
3. Verify your email (click the link they send you).

You do **not** need to add a website/domain — ignore any "add a site" prompts. We only use the *Workers & Pages*, *R2*, and *D1* sections.

## Step 2 — Install Node.js

Cloudflare's command-line tool (`wrangler`) runs on Node.js.

1. Go to https://nodejs.org and download the **LTS** Windows installer (.msi).
2. Run it with all default options.
3. **Open a NEW PowerShell window** (existing windows won't see the new install) and check:

```powershell
node --version
```

Expected: a version number like `v22.x.x`. If you get "not recognized", close and reopen the terminal (or reboot).

## Step 3 — Log in to Cloudflare from the terminal

In any folder:

```powershell
npx wrangler login
```

- First run will ask to install the wrangler package — answer **y**.
- A browser tab opens asking to authorize Wrangler — click **Allow**.
- Back in the terminal you should see `Successfully logged in`.

Verify:

```powershell
npx wrangler whoami
```

Expected: your account email and an Account ID. **Copy the Account ID somewhere** — occasionally useful.

## Step 4 — Enable R2 and create the image bucket

1. In the dashboard (https://dash.cloudflare.com), click **R2 Object Storage** in the left sidebar.
2. Click through the enable/purchase flow — this is where it asks for the payment method (see the heads-up at the top; it stays $0 within free tier).
3. Back in the terminal, create the bucket:

```powershell
npx wrangler r2 bucket create sound-of-space-photos
```

Verify:

```powershell
npx wrangler r2 bucket list
```

Expected: `sound-of-space-photos` in the list.

## Step 5 — Create the D1 database (photo metadata)

```powershell
npx wrangler d1 create sound-of-space-gallery
```

The output prints a config snippet containing a **`database_id`** (a long UUID). **Copy that whole output into a notes file** — Claude needs the `database_id` when writing the server config. (If you lose it: `npx wrangler d1 list` shows it again.)

No card required for D1 — free tier is 5 GB.

## Step 6 — Pick your two secrets

Decide on two random strings and save them somewhere private (password manager or a local note — NOT committed to git):

- **UPLOAD_KEY** — the game sends this with every upload; filters out casual junk. Example format: `sos-upload-7f3k2m9x1q`
- **ADMIN_KEY** — your password for the moderation page where you approve/reject photos. Make this one strong.

They get installed later with `npx wrangler secret put` — we do that together during deployment.

---

## Done — what happens next (with Claude, build chunk 5)

When we build the server, Claude will:

1. Create the `server/photo-gallery/` folder in the repo (Worker code + config + database schema).
2. Put your `database_id` and bucket name into the config.
3. Run the schema against D1, deploy with `npx wrangler deploy` → you get a free URL like `https://photo-gallery.<your-subdomain>.workers.dev`.
4. Install the two secrets (`npx wrangler secret put UPLOAD_KEY`, then `ADMIN_KEY` — each prompts you to paste the value).
5. Put the URL + UPLOAD_KEY into the game's `GalleryConfig.cs`.

## Troubleshooting

- **`npx` / `node` not recognized** → open a brand-new terminal after installing Node; if it persists, reboot.
- **`wrangler login` browser never opens** → copy the URL wrangler prints and open it manually.
- **`r2 bucket create` fails with an authorization/payment error** → R2 wasn't enabled in the dashboard yet (Step 4.1–4.2).
- **Wrong account** (if you ever have more than one): `npx wrangler logout` then `npx wrangler login` again.
