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

> The mod's only dependency is **BepInEx 5**. A mod manager installs that for you, so
> the manager route below is by far the easiest.

### Option A — a mod manager (recommended)

1. Install **[r2modman](https://thunderstore.io/package/ebkr/r2modman/)** or the
   **Thunderstore Mod Manager** (Overwolf).
2. Pick **Risk of Rain 2**, search for **ByJP atproto Play**, and click **Install**.
   BepInEx is pulled in automatically as a dependency.
3. [Configure your handle + app password](#configure).
4. Launch the game **through the mod manager**.

### Option B — manual (from GitHub Releases)

1. Install **BepInEx 5** if you don't have it — the
   [BepInExPack](https://thunderstore.io/package/bbepis/BepInExPack/) (unzip into your
   game folder so `winhttp.dll` sits next to `Risk of Rain 2.exe`).
2. Download the latest **`ByJP_Ror2_atproto_Play_x.y.z.zip`** from the
   [**Releases page**](https://github.com/jphastings/risk-of-rain-2-atproto/releases/latest).
3. Unzip it into `Risk of Rain 2/BepInEx/plugins/` — the zip contains a
   `ByJP.Ror2.Play/` folder with the plugin, its dependencies, and `manifest.json`.
4. [Configure your handle + app password](#configure).
5. Launch the game once.

### Steam Deck / Linux (Proton)

RoR2 is a Windows build, so BepInEx loads under Proton via a `winhttp.dll` proxy. Add
this **Steam launch option** (right-click the game → Properties → Launch Options):

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

A mod manager sets this for you; it's the single most-forgotten step on the Deck.

---

## Configure

You need an atproto account (e.g. [Bluesky](https://bsky.app)) and an **app
password** — *not* your main password. Create one at
**<https://bsky.app/settings/app-passwords>**.

- **Via a mod manager:** open the mod's **Config / Settings editor**, and under
  **`[Login]`** set **`Handle`** (e.g. `you.bsky.social`) and **`AppPassword`**.
- **Manual:** edit
  `Risk of Rain 2/BepInEx/config/me.byjp.pesos.ror2.play.cfg` and set the same two
  values under `[Login]`.

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
