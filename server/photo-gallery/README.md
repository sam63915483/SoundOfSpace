# photo-gallery (Cloudflare Worker)

Community photo gallery backend for *Sound of Space*: R2 for image bytes, D1 for
metadata, an upload key for the game client, and an admin key for a small inline
moderation page. **DRAFT for controller review.**

## Deploy (in order)

You have already run `wrangler d1 create sound-of-space-gallery`. Then:

```bash
# 1. Paste the database_id that `d1 create` printed into wrangler.toml
#    (replace PASTE_YOUR_DATABASE_ID_HERE).

# 2. Create the tables on the REMOTE (production) D1 database.
wrangler d1 execute sound-of-space-gallery --remote --file=schema.sql

# 3. Set the two secrets (prompts for the value; never stored in git/config).
#    UPLOAD_KEY is PUBLIC (it ships inside the game binary and is extractable) —
#    it only gates casual abuse and feeds the per-IP rate limit.
#    ADMIN_KEY is the real secret: it travels in the /admin URL (?key=), so use a
#    LONG RANDOM VALUE (e.g. `openssl rand -hex 32`). There is no brute-force
#    lockout by design (auth is rejected before any DB work), so entropy is your
#    only protection — do not use a short or guessable value.
wrangler secret put UPLOAD_KEY
wrangler secret put ADMIN_KEY

# 4. Ship it.
wrangler deploy
```

## Local testing (`wrangler dev`)

`wrangler dev` uses a local R2 + local D1 by default. Seed the local DB and put
the secrets in a gitignored `.dev.vars` file:

```bash
# seed the LOCAL d1 (omit --remote)
wrangler d1 execute sound-of-space-gallery --file=schema.sql

# .dev.vars  (gitignored — do NOT commit)
# UPLOAD_KEY=dev-upload-key
# ADMIN_KEY=dev-admin-key

wrangler dev
```

Then open `http://localhost:8787/admin?key=dev-admin-key`.

## Example requests

Assuming the Worker is at `$BASE` (e.g. `http://localhost:8787`):

```bash
# Upload a photo (multipart). Lands as pending (approved=0).
curl -X POST "$BASE/photos" \
  -H "X-Upload-Key: dev-upload-key" \
  -F "image=@shot.jpg;type=image/jpeg" \
  -F "title=Nebula over the cabin" \
  -F "description=Caught this on the ride down."

# List approved photos (public, newest first). Follow nextCursor for more.
curl "$BASE/photos?limit=20"
curl "$BASE/photos?limit=20&cursor=<nextCursor-from-previous-response>"

# Approve a pending photo (admin). Grab an id from the /admin page or:
curl "$BASE/admin/pending?key=dev-admin-key"
curl -X POST "$BASE/admin/<id>/approve" -H "Authorization: Bearer dev-admin-key"

# Reject (deletes from R2 + D1).
curl -X POST "$BASE/admin/<id>/reject?key=dev-admin-key"
```

## Notes

- `imageUrl` values are **relative** (`/img/{id}`); the game client prepends the
  deployed base URL.
- `GET /img/{id}` serves approved images only, cached for **1 hour** (`public,
  max-age=3600`) and additionally served from the Workers Cache API keyed on the
  path (query strings are stripped, so `?x=random` can't force cache misses). The
  1h TTL — rather than a year-long immutable cache — is the takedown-propagation
  window: a rejected image stops serving within ~1h without a paid edge purge.
  Pending images are visible only with the admin key (for the preview page) and
  are sent `Cache-Control: no-store` (never cached).
- `GET /photos` sends `Cache-Control: public, max-age=30` so list spam is
  absorbed by browser/edge caches (list content only changes on approve).
- No CORS headers by design — see the header comment in `src/index.js`.
- Rate limit: 10 uploads/hour per IP, counted atomically in D1. IPv6 clients are
  bucketed by their /64 prefix (not the exact address) so a single attacker
  can't rotate addresses within their allocation to bypass the limit.
- Global backstop: uploads are rejected with **503** once 500 photos are pending
  moderation (`MAX_PENDING`), bounding R2 fill and keeping the queue reviewable.
