# Risk of Rain 2 → atproto

A Risk of Rain 2 mod that publishes **every run you play to your own
[atproto](https://atproto.com) PDS** as a `games.gamesgamesgamesgames.actor.play`
record — character, run length, items, route, outcome, co-op party, plus rolling
lifetime stats and achievement counts. There's no central server: the data lands in
*your* repository and you own it.

It's built on the engine-agnostic
[`ByJP.AtprotoGaming.Core`](https://github.com/jphastings/atproto-gaming-dotnet)
package, so the same plumbing can power mods for other games.

---

## Install

It's not on Thunderstore yet, so grab the release zip and install it manually — easiest
with **r2modman**:

1. Install **[r2modman](https://thunderstore.io/package/ebkr/r2modman/)** and create a
   **Risk of Rain 2** profile.
2. In the profile's **Online** tab, install **`BepInExPack`** (the BepInEx mod loader).
   *A local import doesn't pull dependencies, so you must add this yourself.*
3. Download the latest **`Atproto_Play_Tracking_x.y.z.zip`** from the
   [**Releases page**](https://github.com/jphastings/risk-of-rain-2-atproto/releases/latest).
4. In r2modman → **Settings → Import local mod** → pick that zip.
5. [Configure your handle + app password](#configure).
6. Launch with r2modman's **Start modded** (on Steam Deck, do this from Desktop Mode — it
   sets up Proton for you).

> Prefer no mod manager? Install **BepInEx 5** yourself, then from the zip copy the DLLs
> into `Risk of Rain 2/BepInEx/plugins/AtprotoPlayTracking/` and
> `config/atproto-play-tracking.cfg` into `Risk of Rain 2/BepInEx/config/`.

---

## Configure

You need an atproto account (e.g. [Bluesky](https://bsky.app), [Eurosky](https://eurosky.social), [Blacksky](https://blacksky.app)) and an **app password** — *not* your main password. Create one at **<https://bsky.app/settings/app-passwords>**.

The mod ships its config, so you can set this up **before you ever launch**:

- **In the mod manager:** open the **Config editor** and pick
  `atproto-play-tracking.cfg`. It's a plain **text** file (not a form) — under the
  `[Login]` section, edit the lines to read `Handle = you.bsky.social` and
  `AppPassword = xxxx-xxxx-xxxx-xxxx` (no quotes).
- **Manual:** the same file is at
  `Risk of Rain 2/BepInEx/config/atproto-play-tracking.cfg`.

The in-game **`@` badge** (top-right of the main menu) shows whether you're connected —
green when signed in, struck-through when not; click it for details. The read-only
`Status` line in the config says the same (`✓ signed in as …` / `✗ rejected: …`).

Then **restart the game**. Until credentials are set the mod loads but publishes
nothing (it logs a one-line "publishing is OFF" notice).

| Setting           | Section     | Default   | What it does                                                                   |
| ----------------- | ----------- | --------- | ------------------------------------------------------------------------------ |
| `Handle`          | `Login`     | *(blank)* | Your atproto handle or DID.                                                    |
| `AppPassword`     | `Login`     | *(blank)* | An app password from the link above.                                           |
| `Source`          | `Recording` | `steam`   | The platform you play on (`steam`, `epic`, `gog`, …).                          |
| `ThrottleSeconds` | `Recording` | `60`      | Min seconds between in-progress updates (game-over always writes immediately). |

The `[Cache]` section is written by the mod (your resolved DID and stats record) —
leave it alone.

---

## How it works

- One `actor.play` record per run, updated as you play and finalised at game-over.
  The record key is derived from the run's start time + seed, so every player in a
  co-op lobby converges on the **same** record.
- Lifetime totals (playtime, achievements unlocked/total) roll into your
  `actor.stats` record.
- **Offline-safe:** if you're disconnected, updates queue locally and flush the next
  time you're online — a mid-run crash never duplicates or loses data.
- Co-op party members are recorded by their Steam id, resolved to atproto DIDs where
  they have one.

See [`docs/`](docs/) for the design notes, lexicon shape, and the RoR2 hook details.

## Privacy

The mod talks only to **your** PDS (and a Steam→DID lookup for co-op party members).
There's no central server and no telemetry back to us.

## Build from source

See [`mod/README.md`](mod/README.md). The engine-agnostic core lives in the sibling
repo [`atproto-gaming-dotnet`](https://github.com/jphastings/atproto-gaming-dotnet).
