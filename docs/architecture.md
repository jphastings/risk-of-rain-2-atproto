# Architecture

Three layers, with strict directional dependencies:

```
ror2.at.installer (Avalonia desktop app)
        │  bundles
        ▼
ror2.at.mod  (BepInEx plugin, .NET Framework 4.7.2 / netstandard2.1)
        │  depends on
        ▼
ror2.at.core (game-agnostic .NET library, netstandard2.1)
```

Data flow is **mod writes → user's PDS → web reads**. There is no central
server. Every client (game mod, web app) is a peer of the atproto network.

## Layer responsibilities

### `ror2.at.core` — game-agnostic

A small reusable library that any C# game's mod can pull in to handle the
atproto side of "post per-run telemetry to the player's PDS." It knows
nothing about games, runs, or stats.

What it owns:

- AT Protocol HTTP client (createSession, refreshSession, createRecord,
  putRecord, getRecord, listRecords).
- Identity resolution (handle/DID → PDS URL via [Slingshot][slingshot]).
- Authentication state machine (`Unconfigured`/`Checking`/`Ok`/`Failed`/`Offline`)
  exposed as an observable.
- On-disk outbox keyed by DID, so queued writes survive game restarts and
  account switches and only flush when the matching DID is logged in.
- Optional ECDSA P-256 attestation signer (CID-first inline signatures, per
  the [badge.blue][badgeblue] convention).
- Config file scaffolding (handle + app password + cached identity).

What it explicitly does **not** own:

- The shape of any record. Callers pass an opaque payload.
- The decision of *when* to emit.
- Anything that imports `UnityEngine.*`, `Godot.*`, or any game DLL.

Detailed surface in [`core-library.md`](core-library.md).

### `ror2.at.mod` — RoR2 BepInEx plugin

The thin RoR2-specific layer. Its job is to translate game events into
"emit a snapshot now" decisions and to fill in the per-run record body.

What it owns:

- BepInEx plugin entry point (`[BepInPlugin]`-attributed class).
- MonoMod `On.RoR2.*` event subscriptions (preferred over Harmony — it's
  the [community-idiomatic][r2wiki-hooking] hook style for RoR2).
- Run-state extraction (live `Run.instance`, `PlayerStatsComponent.currentStats`,
  inventory walk).
- Emission policy: dirty-bit + 60s throttle, with always-emit on game over.
- Main menu badge UI showing atproto connection state.
- Steam ID → atproto DID resolution for ally back-fill.
- Optional ProperSave bridge: when ProperSave is loaded, subscribe to
  its `OnGatherSaveData` / `OnLoadingStarted` events to persist
  `startTimeUtc` across save-and-quit so the rkey stays stable across
  multi-day runs. Soft dep — no-op if ProperSave isn't present.

See [`lifecycle-hooks.md`](lifecycle-hooks.md) and [`stats.md`](stats.md).

### `ror2.at.installer` — Avalonia desktop app

Cross-platform GUI that ships the mod DLL and a credential-entry flow.

What it owns:

- Steam library discovery (parse `libraryfolders.vdf` to find a non-default
  RoR2 install).
- BepInEx provisioning check (either point the user at r2modman or install
  `winhttp.dll` + `BepInEx/` ourselves; see [`installer.md`](installer.md)).
- Steam Deck / Linux Proton recipe: setting
  `WINEDLLOVERRIDES="winhttp=n,b" %command%` as the game's Steam launch option.
- Handle + app-password capture, validated by a live login to confirm
  credentials before the user starts a run.
- "Health check" view that mirrors the in-game badge state.

## Why a separate core library

The Slay the Spire 2 mod (`sts2.at`) and this one share roughly:

- `AtProtoClient.cs` (almost identical)
- `Outbox.cs` (identical save-on-disk semantics)
- `IdentityResolver.cs` (identical Slingshot call)
- `AuthState.cs` + the badge state machine (identical)
- `Config.cs` (almost identical)
- `Signing/*` (identical)
- `Tid.cs` (identical TID generator)

That's roughly 60% of each mod's lines. Extracting that into a NuGet-style
package (`ByJp.AtprotoTracker.Core`) means the third game we add is mostly
just hooks + record shape.

The split also forces a clean abstraction over the game runtime — logging,
the clock, the on-disk location for outbox/config — which lets the core be
unit-tested without dragging in BepInEx or Godot.

## Coordinate-system mismatch with sts2.at

A few things differ from sts2.at and the implementation will need to handle
both:

| Concern | sts2.at | ror2.at |
|---|---|---|
| Engine | Godot.NET | Unity Mono |
| Hook framework | HarmonyX | MonoMod (`On.*`/`IL.*`) via HookGenPatcher |
| Mod loader | Bespoke (StS2 ships its own) | BepInEx 5.4.x |
| Target framework | .NET 9 | .NET Framework 4.7.2 (BepInEx 5 constraint) |
| Multiplayer model | Per-client; no host | Host-authoritative (UNet derivative) |
| Periodic state hook | `SaveManager.Saved` autosave callback | No autosave callback — we throttle ourselves |
| Distribution | GitHub release zip | Thunderstore + bundled installer |

The core library targets **netstandard2.1**, which both .NET Framework 4.7.2
(via .NET Standard 2.0 fallback for some APIs) and .NET 9 can consume. We may
need to downgrade some surface to netstandard2.0 to land on BepInEx 5 cleanly;
that's a decision deferred to implementation.

[slingshot]: https://slingshot.microcosm.blue/
[badgeblue]: https://badge.blue/
[r2wiki-hooking]: https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/C%23-Programming/Hooking/
