# Unity Services Setup Guide — Relay + Lobby (code-and-password multiplayer)

One-time setup so friends can join your game by typing a 4-digit code instead of
your IP address.

**When to do this:** before I wire up the lobby flow. Nothing else in the game
depends on it, and Phase A testing is unaffected either way.
**Time:** ~15 minutes, most of it waiting for the Editor to import packages.
**Cost:** £0 — Relay and Lobby both have free tiers, and two friends playing
will not come close to them.

> **One honest heads-up:** I could not verify from here whether Unity currently
> asks for a payment method when you first enable these services. Cloudflare
> does for R2, so it is worth being ready for. **If it asks for a card and you
> would rather not, stop and tell me** — we build the whole flow against
> localhost instead and swap the transport in later. Nothing is wasted; the
> lobby UI, the prompt, the pod spawn and the waiting screen are all
> transport-agnostic.

---

## What you are actually turning on

Two separate services that work together:

- **Relay** is the thing that removes the IP address. Both machines make an
  *outbound* connection to a Unity server in the middle, which passes packets
  between them. Home routers already allow outbound connections, so there is no
  port forwarding and no firewall prompt.
- **Lobby** is the thing that makes it a *code*. It stores your session's relay
  details behind a short code and a password, so your friend types four digits
  instead of a long relay token.

You need both. Relay alone would still mean copy-pasting a join code that Unity
generates for you.

---

## Step 1 — Sign in to Unity inside the Editor

1. Open the project in Unity.
2. Top-right of the Editor window, click the **account icon** (a person's head).
3. If it says **Sign in**, sign in with the same Unity account you use for the
   Hub. If it already shows your name, you are done with this step.

---

## Step 2 — Link the project to Unity Cloud

This is the step that matters most. Right now the project is not linked to
anything — `ProjectSettings/ProjectSettings.asset` has an empty
`cloudProjectId`, which is why none of this works yet.

1. **Edit → Project Settings → Services**.
2. You will see either a **Create project ID** button or a dropdown to link an
   existing one. Either is fine:
   - **Create** makes a new Unity Cloud project named after this Unity project.
   - **Link** attaches it to one you already have.
3. If it asks which **organization** to use, pick your personal one (it is
   usually your username).
4. Wait for it to finish. The Services window should end up showing a project
   name and an organization rather than a Create button.

**How to check it worked:** the file `ProjectSettings/ProjectSettings.asset`
should now have a non-empty `cloudProjectId:` line. You can also just tell me
and I will check.

✅ **Done** — linked as project **Solar System 2** under organization
`sam63915483`, cloud project id `a74fcb20…`.

> **One thing to watch:** `cloudEnabled` in ProjectSettings and `m_Enabled` in
> `UnityConnectSettings.asset` are both still `0`. That is usually harmless —
> those are the legacy Unity Connect / Analytics switches, not the UGS SDK — but
> if `UnityServices.InitializeAsync()` ever fails with a project-not-found
> error, flipping Services on in **Edit → Project Settings → Services** is the
> first thing to try.

---

## Step 3 — Turn on Relay and Lobby in the dashboard

The services are **not** in the project's own left-hand menu, which is the
obvious place to look and the wrong one. They live under Products:

1. Go to **https://cloud.unity.com** and sign in with the same account.
2. Make sure the project selected at the top is **Solar System 2**.
3. In the sidebar, click the **Products** tab.
4. Scroll to **Gaming Services → Multiplayer**.
5. Find **Relay** and click **Launch**. This adds Relay to the **Shortcuts**
   section of the sidebar and opens its Overview page.
6. On that Overview page there is an **activation toggle**. Make sure it is
   **On**.
7. Go back to **Products → Gaming Services → Multiplayer** and do exactly the
   same for **Lobby**.

You do not need to configure anything inside either one — no regions, no limits,
no fleet settings. The defaults are correct for what we are doing.

> If you get a **403** error later when the game tries to connect, it is almost
> always one of these two toggles being off, or the dashboard project not being
> the one the Editor is linked to. That is the first thing to check.

---

## Step 4 — Install the four packages · ✅ DONE

Already done for you through the Editor, so there is nothing to do here. For the
record, what went in:

| Package | Version |
|---|---|
| `com.unity.services.core` | 1.18.0 |
| `com.unity.services.authentication` | 3.7.3 |
| `com.unity.services.relay` | 1.2.0 |
| `com.unity.services.lobby` | 1.3.0 |

`com.unity.services.qos` 1.3.2 came along as a dependency, and Newtonsoft JSON
moved 3.2.1 → 3.2.2. **Unity Transport stayed at 1.5.0**, which is the one that
mattered — Relay and Lobby ask for older transport versions, and a downgrade
there would have broken Netcode. It resolved upward correctly. Project compiles
clean.

**Already installed, nothing to do:** Netcode for GameObjects 1.12 and Unity
Transport 1.5, whose Relay support is compiled in — so the transport half of
this needed no new code at all.

---

## Step 5 — Tell me it is done

That is everything on your side. Send me a message and I will:

- verify the project is linked and the packages resolved,
- add anonymous sign-in (no accounts, no logins for your friends — the game
  signs itself in silently on first launch),
- wire the lobby create/join flow to the menu,
- keep a **Local** toggle that skips Relay entirely and uses `127.0.0.1` exactly
  as today, so same-machine testing stays instant.

---

## Things worth knowing before you start

**Your friends do not need any of this.** They do not need a Unity account, a
login, or a dashboard. The game signs itself in anonymously in the background.
They type four digits and a password.

**The password is whatever you type, including nothing.** Unity's lobby service
does require 8–64 characters, but the game hashes whatever you type into a
fixed-length token and hands *that* to the service — so you can use a 3-letter
password, or leave it blank for a session anyone with the code can join. Both
machines derive the same token from the same typed password, so the check still
happens on Unity's servers and a wrong password is still rejected before it
reaches netcode. Nothing was weakened; you just don't have to satisfy Unity's
rule yourself.

**Four digits is 10,000 possible codes**, so two live sessions can occasionally
want the same one. The game handles it by rolling a different code — you may
just see the number change once while the session is starting.

**This does not affect single-player at all.** Everything goes behind
`FeatureVault.Multiplayer`. Set it false and the Multiplayer button disappears,
the "play together?" prompt never fires, and the game boots exactly as it does
now.

**If anything in the dashboard looks different from this guide**, Unity has
moved something — send me a screenshot rather than guessing, and I will update
these steps.
