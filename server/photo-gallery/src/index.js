/**
 * Sound of Space — community photo gallery Worker.
 *
 * Routes (see README / spec for the full contract):
 *   POST /photos                     upload (X-Upload-Key), lands approved=0
 *   GET  /photos?limit=&cursor=      public list of APPROVED photos, newest first
 *   GET  /img/{id}                   stream JPEG from R2 (approved only, or admin key)
 *   GET  /admin                      inline HTML moderation page (admin key)
 *   GET  /admin/pending              JSON list of pending photos (admin key)
 *   POST /admin/{id}/approve         approve (admin key)
 *   POST /admin/{id}/reject          delete from R2 + D1 (admin key)
 *
 * CORS: intentionally omitted. The Unity game client is not a browser and is
 * not bound by the same-origin policy, so it needs no CORS headers. The /admin
 * page is served from this same Worker origin, so its fetch() calls are
 * same-origin too. Adding permissive CORS would only make the upload/admin
 * endpoints reachable from arbitrary third-party web pages — a downside with no
 * upside here — so we deliberately send no Access-Control-* headers.
 */

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------
const MAX_BODY_BYTES = 1024 * 1024; // 1 MB — whole request body
const MAX_FILE_BYTES = 1024 * 1024; // 1 MB — the decoded image part
const MAX_TITLE_LEN = 100;
const MAX_DESC_LEN = 500;
const MAX_UPLOADS_PER_HOUR = 10;
const MAX_PENDING = 500; // global cap on approved=0 rows (denial-of-wallet backstop)
const DEFAULT_LIMIT = 20;
const MAX_LIMIT = 50;
const ID_RE = /^[a-f0-9]{32}$/; // randomUUID() with dashes stripped

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------
export default {
  async fetch(request, env, ctx) {
    try {
      return await route(request, env, ctx);
    } catch (err) {
      // Never leak stack traces to the client; log for the operator only.
      console.error("Unhandled error:", err && err.stack ? err.stack : err);
      return errorResponse("Internal server error", 500);
    }
  },
};

async function route(request, env, ctx) {
  const url = new URL(request.url);
  const path = url.pathname;
  const method = request.method;

  // Health check / root.
  if (path === "/" && method === "GET") {
    return json({ ok: true, service: "photo-gallery" });
  }

  if (path === "/photos") {
    if (method === "POST") return handleUpload(request, env, ctx);
    if (method === "GET") return handleList(request, env);
    return errorResponse("Method not allowed", 405);
  }

  if (path.startsWith("/img/")) {
    if (method !== "GET") return errorResponse("Method not allowed", 405);
    return handleImage(request, env, ctx, path.slice("/img/".length));
  }

  // ----- Admin surface (all admin-key protected) -----
  if (path === "/admin" && method === "GET") {
    return handleAdminPage(request, env);
  }
  if (path === "/admin/pending" && method === "GET") {
    return handleAdminPending(request, env);
  }
  const approveMatch = path.match(/^\/admin\/([^/]+)\/approve$/);
  if (approveMatch && method === "POST") {
    return handleApprove(request, env, approveMatch[1]);
  }
  const rejectMatch = path.match(/^\/admin\/([^/]+)\/reject$/);
  if (rejectMatch && method === "POST") {
    return handleReject(request, env, rejectMatch[1]);
  }

  return errorResponse("Not found", 404);
}

// ---------------------------------------------------------------------------
// POST /photos  — upload
// ---------------------------------------------------------------------------
async function handleUpload(request, env, ctx) {
  // 1. Auth (shared upload key baked into the game client).
  //    Fail CLOSED: reject when the caller sent no key OR the server secret is
  //    unset, so a misconfigured Worker never authorizes on "" == "".
  const providedKey = request.headers.get("X-Upload-Key") || "";
  if (!providedKey || !env.UPLOAD_KEY) {
    return errorResponse("Unauthorized", 401);
  }
  if (!(await safeEqual(providedKey, env.UPLOAD_KEY))) {
    return errorResponse("Unauthorized", 401);
  }

  // 2. Rate limit per IP (hour bucket) — ATOMIC increment-and-return so the
  //    check can't be raced by concurrent requests, and run EARLY (before the
  //    expensive formData parse). Counts EVERY authenticated attempt, including
  //    ones we later reject for validation, so invalid-upload floods are
  //    throttled too. IPv6 callers bucket by /64 prefix (an attacker usually
  //    owns a whole /64), so per-address rotation can't dodge the limit.
  const ipBucket = ipRateBucket(request.headers.get("CF-Connecting-IP") || "unknown");
  const hour = Math.floor(Date.now() / 3600000);
  const bucketKey = `${ipBucket}:${hour}`;
  const rlRow = await env.DB.prepare(
    "INSERT INTO rate_limits (bucket_key, count, window_hour) VALUES (?1, 1, ?2) " +
      "ON CONFLICT(bucket_key) DO UPDATE SET count = count + 1 RETURNING count"
  )
    .bind(bucketKey, hour)
    .first();
  const attempts = rlRow && typeof rlRow.count === "number" ? rlRow.count : 1;
  if (attempts > MAX_UPLOADS_PER_HOUR) {
    return errorResponse("Rate limit exceeded, try again later", 429);
  }

  // Opportunistically prune stale rate-limit rows (~5% of uploads) so old hour
  // buckets can't accumulate forever. Non-blocking; no cron trigger needed.
  if (Math.random() < 0.05) {
    ctx.waitUntil(
      env.DB.prepare("DELETE FROM rate_limits WHERE window_hour < ?")
        .bind(hour)
        .run()
    );
  }

  // 3. Global pending cap — the real denial-of-wallet backstop. Bounds R2 fill
  //    and keeps the moderation queue reachable no matter how the per-IP limit
  //    is bypassed (IP rotation, shared upload key spread across many hosts).
  const pendingRow = await env.DB.prepare(
    "SELECT COUNT(*) AS n FROM photos WHERE approved = 0"
  ).first();
  const pending = pendingRow && typeof pendingRow.n === "number" ? pendingRow.n : 0;
  if (pending >= MAX_PENDING) {
    return errorResponse("Upload queue is full, try again later", 503);
  }

  // 4. Cheap early body-size reject via Content-Length, enforced on the DECLARED
  //    length BEFORE we buffer anything. A missing or non-numeric length is
  //    rejected outright so a chunked / length-less body can't stream unbounded.
  //    The real ceiling is the post-decode byte check further down.
  const clHeader = request.headers.get("Content-Length");
  const contentLength = clHeader === null ? NaN : Number(clHeader);
  if (!Number.isFinite(contentLength) || contentLength < 1 || contentLength > MAX_BODY_BYTES) {
    return errorResponse("Payload too large or missing Content-Length (max 1 MB)", 413);
  }

  // 5. Must be multipart/form-data.
  const contentType = request.headers.get("Content-Type") || "";
  if (!contentType.includes("multipart/form-data")) {
    return errorResponse("Expected multipart/form-data", 400);
  }

  let form;
  try {
    form = await request.formData();
  } catch {
    return errorResponse("Malformed multipart body", 400);
  }

  // 6. Validate the image part.
  const file = form.get("image");
  if (!file || typeof file === "string" || typeof file.arrayBuffer !== "function") {
    return errorResponse("Missing image file", 400);
  }
  if (file.type !== "image/jpeg") {
    return errorResponse("Only image/jpeg is accepted", 415);
  }
  if (typeof file.size === "number" && file.size > MAX_FILE_BYTES) {
    return errorResponse("Payload too large (max 1 MB)", 413);
  }

  const buffer = await file.arrayBuffer();
  if (buffer.byteLength > MAX_FILE_BYTES) {
    return errorResponse("Payload too large (max 1 MB)", 413);
  }
  const bytes = new Uint8Array(buffer);
  // JPEG magic number: FF D8 FF. Trust the bytes, not the declared type.
  if (bytes.length < 3 || bytes[0] !== 0xff || bytes[1] !== 0xd8 || bytes[2] !== 0xff) {
    return errorResponse("File is not a valid JPEG", 415);
  }

  // 7. Validate text fields.
  const rawTitle = form.get("title");
  const rawDesc = form.get("description");
  const title = typeof rawTitle === "string" ? rawTitle.trim() : "";
  const description = typeof rawDesc === "string" ? rawDesc.trim() : "";
  if (title.length === 0) {
    return errorResponse("Title is required", 400);
  }
  if (title.length > MAX_TITLE_LEN) {
    return errorResponse(`Title must be ${MAX_TITLE_LEN} characters or fewer`, 400);
  }
  if (description.length > MAX_DESC_LEN) {
    return errorResponse(`Description must be ${MAX_DESC_LEN} characters or fewer`, 400);
  }

  // 8. Store. R2 object key == photo id. Write the D1 row FIRST (approved=0),
  //    THEN the R2 bytes; if the put throws, roll the row back. This ordering
  //    avoids orphaning an R2 object that no metadata row references (the old
  //    order leaked bytes forever on any insert failure). The inverse — a row
  //    with a missing object — is self-healing: /img 404s, /photos won't list
  //    it until approved, and reject still deletes the row.
  const id = crypto.randomUUID().replace(/-/g, "");
  const createdAt = Date.now();
  await env.DB.prepare(
    "INSERT INTO photos (id, title, description, created_at, approved, size) VALUES (?, ?, ?, ?, 0, ?)"
  )
    .bind(id, title, description || null, createdAt, buffer.byteLength)
    .run();
  try {
    await env.BUCKET.put(id, buffer, {
      httpMetadata: { contentType: "image/jpeg" },
    });
  } catch (err) {
    console.error("R2 put failed, rolling back row:", err && err.stack ? err.stack : err);
    try {
      await env.DB.prepare("DELETE FROM photos WHERE id = ?").bind(id).run();
    } catch (cleanupErr) {
      console.error("Row rollback also failed:", cleanupErr);
    }
    return errorResponse("Failed to store image", 500);
  }

  return json(
    {
      id,
      imageUrl: `/img/${id}`,
      title,
      description,
      createdAt,
    },
    201
  );
}

// ---------------------------------------------------------------------------
// GET /photos  — public list (approved only, cursor paginated)
// ---------------------------------------------------------------------------
async function handleList(request, env) {
  const url = new URL(request.url);

  // Clamp limit to 1..50, default 20.
  let limit = parseInt(url.searchParams.get("limit") || "", 10);
  if (!Number.isFinite(limit)) limit = DEFAULT_LIMIT;
  limit = Math.max(1, Math.min(MAX_LIMIT, limit));

  const cursorParam = url.searchParams.get("cursor");
  let cursor = null;
  if (cursorParam) {
    cursor = decodeCursor(cursorParam);
    if (!cursor) return errorResponse("Invalid cursor", 400);
  }

  // Fetch limit+1 so we can tell whether a further page exists.
  let rows;
  if (cursor) {
    rows = await env.DB.prepare(
      "SELECT id, title, description, created_at FROM photos " +
        "WHERE approved = 1 AND (created_at < ?1 OR (created_at = ?1 AND id < ?2)) " +
        "ORDER BY created_at DESC, id DESC LIMIT ?3"
    )
      .bind(cursor.createdAt, cursor.id, limit + 1)
      .all();
  } else {
    rows = await env.DB.prepare(
      "SELECT id, title, description, created_at FROM photos " +
        "WHERE approved = 1 ORDER BY created_at DESC, id DESC LIMIT ?1"
    )
      .bind(limit + 1)
      .all();
  }

  const results = rows.results || [];
  let nextCursor = null;
  if (results.length > limit) {
    const last = results[limit - 1]; // last item we actually return
    nextCursor = encodeCursor(last.created_at, last.id);
    results.length = limit; // trim the probe row
  }

  const items = results.map((r) => ({
    id: r.id,
    title: r.title,
    description: r.description || "",
    imageUrl: `/img/${r.id}`,
    createdAt: r.created_at,
  }));

  // The approved list only changes when the admin approves a photo, so ~30s of
  // staleness is fine. A short public Cache-Control lets the edge / browser
  // absorb list spam (incl. cache-busting query strings that vary the URL)
  // instead of hitting the Worker + D1 every time. (True edge caching of a
  // Worker response also needs a CF Cache Rule; the header is the low-effort
  // half that browsers and any configured rule will honor.)
  return json({ items, nextCursor }, 200, { "Cache-Control": "public, max-age=30" });
}

// ---------------------------------------------------------------------------
// GET /img/{id}  — stream JPEG from R2
// ---------------------------------------------------------------------------
async function handleImage(request, env, ctx, id) {
  if (!ID_RE.test(id)) {
    return errorResponse("Not found", 404);
  }

  // Cache key is the PATH ONLY (query string stripped) so `?x=<random>`
  // cache-busting can't force MISSes that burn a D1 read + R2 get every time.
  // Only APPROVED responses are ever written here (see below), so a hit is
  // always a public, servable image.
  const cacheUrl = new URL(request.url);
  cacheUrl.search = "";
  const cacheKey = new Request(cacheUrl.toString(), { method: "GET" });
  const cache = caches.default;

  const hit = await cache.match(cacheKey);
  if (hit) return hit; // served without touching D1 or R2

  const row = await env.DB.prepare("SELECT approved FROM photos WHERE id = ?")
    .bind(id)
    .first();
  if (!row) {
    return errorResponse("Not found", 404);
  }

  const approved = row.approved === 1;
  const isAdmin = await isAdminRequest(request, env);

  // Unapproved images are ONLY visible to the admin (preview). Otherwise the
  // bucket would become free anonymous image hosting. Return 404 (not 403) so
  // we don't confirm the existence of pending content to anonymous callers.
  if (!approved && !isAdmin) {
    return errorResponse("Not found", 404);
  }

  const obj = await env.BUCKET.get(id);
  if (!obj) {
    return errorResponse("Not found", 404);
  }

  const headers = new Headers();
  obj.writeHttpMetadata(headers);
  headers.set("Content-Type", "image/jpeg");
  // L1: never let a browser sniff these bytes as anything other than an image,
  // and always render inline (never a download / HTML document).
  headers.set("X-Content-Type-Options", "nosniff");
  headers.set("Content-Disposition", "inline");
  if (obj.httpEtag) headers.set("ETag", obj.httpEtag);

  if (approved) {
    // M1: cache approved images for only 1 HOUR (not the old
    // max-age=31536000, immutable). A year-long immutable cache would let a
    // rejected / taken-down image keep serving from caches essentially forever.
    // A full edge purge needs API tokens (out of scope for a solo dev), so a
    // moderate TTL is the pragmatic takedown-propagation window.
    headers.set("Cache-Control", "public, max-age=3600");
    const response = new Response(obj.body, { headers });
    // Populate the edge cache under the path-only key so repeat / cache-busted
    // requests skip D1 + R2 entirely.
    ctx.waitUntil(cache.put(cacheKey, response.clone()));
    return response;
  }

  // Admin preview of a pending image: never cache (it may be rejected) and
  // never write it to the shared edge cache.
  headers.set("Cache-Control", "no-store");
  return new Response(obj.body, { headers });
}

// ---------------------------------------------------------------------------
// Admin — auth helpers
// ---------------------------------------------------------------------------
// Accept the admin key via `Authorization: Bearer <key>` OR `?key=<key>`.
// The query form exists because <img> tags and simple fetches on the admin
// page cannot set custom headers.
async function isAdminRequest(request, env) {
  const url = new URL(request.url);
  const auth = request.headers.get("Authorization") || "";
  let provided = "";
  if (auth.startsWith("Bearer ")) {
    provided = auth.slice("Bearer ".length);
  } else {
    provided = url.searchParams.get("key") || "";
  }
  if (!provided) return false;
  return safeEqual(provided, env.ADMIN_KEY || "");
}

async function requireAdmin(request, env) {
  if (await isAdminRequest(request, env)) return null;
  return errorResponse("Unauthorized", 401);
}

// ---------------------------------------------------------------------------
// GET /admin  — inline moderation page
// ---------------------------------------------------------------------------
async function handleAdminPage(request, env) {
  const denied = await requireAdmin(request, env);
  if (denied) return denied;
  return new Response(ADMIN_HTML, {
    status: 200,
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      // The page URL embeds the admin key; keep it out of shared caches.
      "Cache-Control": "no-store",
      // M4: the admin key rides in ?key=; no-referrer stops it leaking via the
      // Referer header on any outbound request the page makes. Residual risk:
      // it's still visible in the URL bar / browser history — acceptable for a
      // single-operator page; a signed-token system would be over-engineering.
      "Referrer-Policy": "no-referrer",
    },
  });
}

// ---------------------------------------------------------------------------
// GET /admin/pending  — JSON list of pending photos (newest first)
// ---------------------------------------------------------------------------
async function handleAdminPending(request, env) {
  const denied = await requireAdmin(request, env);
  if (denied) return denied;

  const rows = await env.DB.prepare(
    "SELECT id, title, description, created_at, size FROM photos " +
      "WHERE approved = 0 ORDER BY created_at DESC, id DESC LIMIT 200"
  ).all();

  const items = (rows.results || []).map((r) => ({
    id: r.id,
    title: r.title,
    description: r.description || "",
    createdAt: r.created_at,
    size: r.size,
  }));

  return json({ items }, 200, { "Cache-Control": "no-store" });
}

// ---------------------------------------------------------------------------
// POST /admin/{id}/approve
// ---------------------------------------------------------------------------
async function handleApprove(request, env, id) {
  const denied = await requireAdmin(request, env);
  if (denied) return denied;
  if (!ID_RE.test(id)) return errorResponse("Not found", 404);

  const res = await env.DB.prepare("UPDATE photos SET approved = 1 WHERE id = ?")
    .bind(id)
    .run();
  if (!res.meta || res.meta.changes === 0) {
    return errorResponse("Not found", 404);
  }
  return json({ ok: true, id, approved: true });
}

// ---------------------------------------------------------------------------
// POST /admin/{id}/reject  — delete from R2 AND D1
// ---------------------------------------------------------------------------
async function handleReject(request, env, id) {
  const denied = await requireAdmin(request, env);
  if (denied) return denied;
  if (!ID_RE.test(id)) return errorResponse("Not found", 404);

  // Delete the bytes first (idempotent), then the metadata row.
  await env.BUCKET.delete(id);
  const res = await env.DB.prepare("DELETE FROM photos WHERE id = ?")
    .bind(id)
    .run();
  if (!res.meta || res.meta.changes === 0) {
    return errorResponse("Not found", 404);
  }
  return json({ ok: true, id, deleted: true });
}

// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------
function json(obj, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      ...extraHeaders,
    },
  });
}

function errorResponse(message, status) {
  return json({ error: message }, status);
}

// Coarsen the client IP into a rate-limit bucket key.
//   IPv4  -> the full address.
//   IPv6  -> the /64 network prefix (first 4 hextets). A single attacker
//            typically controls an entire /64, so limiting the whole prefix
//            stops trivial per-address rotation within it from buying more
//            upload quota.
function ipRateBucket(ip) {
  if (!ip || ip === "unknown") return "unknown";
  if (!ip.includes(":")) return ip; // IPv4 (or an already-opaque token)
  // IPv6: strip any zone id (%eth0) and brackets, expand "::", take 4 groups.
  const addr = ip.split("%")[0].replace(/[[\]]/g, "");
  const halves = addr.split("::");
  const head = halves[0] ? halves[0].split(":") : [];
  const tail = halves.length > 1 && halves[1] ? halves[1].split(":") : [];
  const fill = Math.max(0, 8 - head.length - tail.length);
  const groups = head.concat(Array(fill).fill("0"), tail);
  const prefix = groups
    .slice(0, 4)
    .map((g) => (g || "0").padStart(4, "0").toLowerCase());
  return "v6:" + prefix.join(":");
}

// Constant-time string comparison via HMAC over a per-call random key.
// Hashing both inputs to a fixed length means we leak neither the secret's
// length nor its bytes through timing.
async function safeEqual(a, b) {
  const enc = new TextEncoder();
  const keyBytes = crypto.getRandomValues(new Uint8Array(32));
  const key = await crypto.subtle.importKey(
    "raw",
    keyBytes,
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const ha = new Uint8Array(await crypto.subtle.sign("HMAC", key, enc.encode(a)));
  const hb = new Uint8Array(await crypto.subtle.sign("HMAC", key, enc.encode(b)));
  let diff = 0;
  for (let i = 0; i < ha.length; i++) diff |= ha[i] ^ hb[i];
  return diff === 0;
}

// Opaque cursor: base64url("<created_at>.<id>").
function encodeCursor(createdAt, id) {
  return base64UrlEncode(`${createdAt}.${id}`);
}

function decodeCursor(cursor) {
  try {
    const s = base64UrlDecode(cursor);
    const dot = s.indexOf(".");
    if (dot < 0) return null;
    const createdAt = parseInt(s.slice(0, dot), 10);
    const id = s.slice(dot + 1);
    if (!Number.isFinite(createdAt) || !ID_RE.test(id)) return null;
    return { createdAt, id };
  } catch {
    return null;
  }
}

function base64UrlEncode(str) {
  return btoa(str).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function base64UrlDecode(str) {
  let s = str.replace(/-/g, "+").replace(/_/g, "/");
  while (s.length % 4) s += "=";
  return atob(s);
}

// ---------------------------------------------------------------------------
// Admin HTML (inline, no external assets, no frameworks).
// User-supplied title/description are inserted via textContent (never innerHTML)
// so moderator eyes never trigger stored XSS.
// ---------------------------------------------------------------------------
const ADMIN_HTML = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Photo Gallery — Moderation</title>
<style>
  :root { color-scheme: dark; }
  body { font-family: system-ui, sans-serif; margin: 0; background:#111; color:#eee; }
  header { padding: 16px 20px; background:#1c1c1c; border-bottom:1px solid #333; }
  h1 { margin:0; font-size:18px; }
  #status { padding:12px 20px; color:#aaa; }
  #list { display:grid; grid-template-columns:repeat(auto-fill,minmax(280px,1fr)); gap:16px; padding:20px; }
  .card { background:#1c1c1c; border:1px solid #333; border-radius:8px; overflow:hidden; display:flex; flex-direction:column; }
  .card img { width:100%; height:200px; object-fit:cover; background:#000; }
  .card .meta { padding:12px; flex:1; }
  .card .title { font-weight:600; margin:0 0 6px; word-break:break-word; }
  .card .desc { margin:0 0 6px; color:#bbb; font-size:14px; white-space:pre-wrap; word-break:break-word; }
  .card .date { color:#777; font-size:12px; }
  .card .actions { display:flex; gap:8px; padding:12px; border-top:1px solid #333; }
  button { flex:1; padding:8px; border:0; border-radius:6px; cursor:pointer; font-size:14px; }
  .approve { background:#1f7a34; color:#fff; }
  .reject { background:#8a2020; color:#fff; }
  button:disabled { opacity:.5; cursor:default; }
</style>
</head>
<body>
<header><h1>Photo Gallery — Pending Moderation</h1></header>
<div id="status">Loading…</div>
<div id="list"></div>
<script>
  // The admin key travels in the page URL (?key=...). Reuse it for every
  // fetch and for <img> src (img tags cannot send Authorization headers).
  const KEY = new URLSearchParams(location.search).get("key") || "";
  const statusEl = document.getElementById("status");
  const listEl = document.getElementById("list");

  function fmtDate(ms) {
    try { return new Date(ms).toLocaleString(); } catch { return String(ms); }
  }

  async function load() {
    statusEl.textContent = "Loading…";
    listEl.textContent = "";
    let data;
    try {
      const res = await fetch("/admin/pending?key=" + encodeURIComponent(KEY));
      if (res.status === 401) { statusEl.textContent = "Unauthorized — bad or missing ?key."; return; }
      if (!res.ok) { statusEl.textContent = "Error loading (" + res.status + ")."; return; }
      data = await res.json();
    } catch (e) {
      statusEl.textContent = "Network error.";
      return;
    }
    const items = data.items || [];
    statusEl.textContent = items.length ? (items.length + " pending") : "Nothing pending. All caught up.";
    for (const item of items) render(item);
  }

  function render(item) {
    const card = document.createElement("div");
    card.className = "card";

    const img = document.createElement("img");
    img.src = "/img/" + encodeURIComponent(item.id) + "?key=" + encodeURIComponent(KEY);
    img.alt = "";
    card.appendChild(img);

    const meta = document.createElement("div");
    meta.className = "meta";
    const title = document.createElement("p");
    title.className = "title";
    title.textContent = item.title;          // textContent => no XSS
    const desc = document.createElement("p");
    desc.className = "desc";
    desc.textContent = item.description || "";
    const date = document.createElement("p");
    date.className = "date";
    date.textContent = fmtDate(item.createdAt);
    meta.appendChild(title); meta.appendChild(desc); meta.appendChild(date);
    card.appendChild(meta);

    const actions = document.createElement("div");
    actions.className = "actions";
    const approve = document.createElement("button");
    approve.className = "approve"; approve.textContent = "Approve";
    const reject = document.createElement("button");
    reject.className = "reject"; reject.textContent = "Reject";
    approve.onclick = () => act(item.id, "approve", card, [approve, reject]);
    reject.onclick = () => act(item.id, "reject", card, [approve, reject]);
    actions.appendChild(approve); actions.appendChild(reject);
    card.appendChild(actions);

    listEl.appendChild(card);
  }

  async function act(id, action, card, buttons) {
    buttons.forEach(b => b.disabled = true);
    try {
      const res = await fetch("/admin/" + encodeURIComponent(id) + "/" + action + "?key=" + encodeURIComponent(KEY), { method: "POST" });
      if (res.ok) {
        card.remove();
      } else {
        alert(action + " failed (" + res.status + ")");
        buttons.forEach(b => b.disabled = false);
      }
    } catch (e) {
      alert("Network error");
      buttons.forEach(b => b.disabled = false);
    }
  }

  load();
</script>
</body>
</html>`;
