-- Sound of Space — community photo gallery schema (Cloudflare D1 / SQLite).
-- Apply with:
--   wrangler d1 execute sound-of-space-gallery --remote --file=schema.sql
-- (drop --remote to seed the local `wrangler dev` database instead).

-- Photo metadata. The JPEG bytes themselves live in R2 under key = id.
CREATE TABLE IF NOT EXISTS photos (
  id          TEXT    PRIMARY KEY,          -- crypto.randomUUID() with dashes stripped (32 hex chars)
  title       TEXT    NOT NULL,
  description TEXT,                          -- optional
  created_at  INTEGER NOT NULL,             -- epoch milliseconds
  approved    INTEGER NOT NULL DEFAULT 0,   -- 0 = pending, 1 = approved
  size        INTEGER                        -- byte size of the stored JPEG
);

-- Serves the public list query: WHERE approved = 1 ORDER BY created_at DESC, id DESC.
CREATE INDEX IF NOT EXISTS idx_photos_approved_created
  ON photos (approved, created_at DESC);

-- Per-IP upload rate limiting (hour buckets). bucket_key = "<ipBucket>:<hourNumber>"
-- where ipBucket is the full IPv4 address, or "v6:<first-4-hextets>" (the /64
-- prefix) for IPv6. The counter is incremented atomically in one D1 statement
-- (INSERT ... ON CONFLICT DO UPDATE SET count = count + 1 RETURNING count).
-- window_hour lets us prune stale rows cheaply. NOTE: the table shape is
-- UNCHANGED from the previous version, so no migration/drop is required.
CREATE TABLE IF NOT EXISTS rate_limits (
  bucket_key  TEXT    PRIMARY KEY,
  count       INTEGER NOT NULL DEFAULT 0,
  window_hour INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_rate_limits_window
  ON rate_limits (window_hour);
