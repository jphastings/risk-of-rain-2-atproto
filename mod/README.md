# ByJP.Ror2.Play

A Risk of Rain 2 BepInEx plugin that publishes each run to the player's atproto
PDS as a `games.gamesgamesgamesgames.actor.play` record, using
[`ByJP.AtprotoGaming.Core`](../../atproto-gaming-dotnet). It exists both as a real
mod and as the first consumer used to pressure-test the package's API — see
[`../docs/api-gaps.md`](../docs/api-gaps.md).

## How it's wired

| Layer | Files | Talks to |
| --- | --- | --- |
| Engine glue | `Plugin.cs`, `Ror2/RunTracker.cs`, `Ror2/StateExtractor.cs`, `Ror2/AchievementPatch.cs` | RoR2 / Unity / BepInEx |
| Mapping (engine-free) | `Mapping/RunSnapshot.cs`, `Mapping/PlayRecordMapper.cs` | the package only |
| Adapters / config | `Adapters/BepInExLogSink.cs`, `Config/Ror2PlayConfig.cs`, `Config/BepInExConfigStore.cs` | both |

The mapping layer references no RoR2 type, so it builds and is reasoned about
against the package alone (that's how the API gaps were found).

**Flow:** `Plugin.Awake` wires the three adapters, reads the BepInEx config, constructs
`AtprotoGamingClient`, and starts `LoginAsync` on a background task. `RunTracker`
opens a `PlaySession` on `Run.onRunStartGlobal`, flips a dirty bit on game events,
and a coroutine emits a throttled snapshot (one record update per ~60 s, plus an
immediate write at game-over). Everything offline-queues automatically.

## Building

Needs a local RoR2 install with **BepInEx 5** (the only dependency — the one game
patch uses Harmony, which ships inside BepInEx; no HookGenPatcher/MMHOOK). The
`.csproj` auto-detects the Steam install per-OS; override in `mod/local.props` if
yours is elsewhere:

```xml
<Project><PropertyGroup>
  <Ror2Dir>/path/to/Risk of Rain 2</Ror2Dir>
</PropertyGroup></Project>
```

The core package isn't on nuget.org yet, so pack it into the local feed first
(`mod/nuget.config` already points at `../packages`):

```sh
dotnet pack ../../atproto-gaming-dotnet/src/ByJP.AtprotoGaming.Core -o ../packages
dotnet build                 # compile
dotnet build -t:Install      # compile + copy into BepInEx/plugins/ByJP.Ror2.Play
```

`-t:Install` deploys the plugin DLL plus its managed dependencies (System.Text.Json
et al.) next to it.

## Configuring

Settings live in BepInEx's config (`BepInEx/config/me.byjp.pesos.ror2.play.cfg`,
editable in a mod manager's config editor) — see the
[install guide](../README.md#configure). Set `[Login]` `Handle` + `AppPassword`
([app password](https://bsky.app/settings/app-passwords)) and restart; until then the
mod loads but publishes nothing (it logs a one-line notice). Optional: `[Recording]`
`Source` (`steam`/`epic`/…) and `ThrottleSeconds`. The `[Cache]` section is
mod-managed. (The offline outbox still lives in `outbox/` next to the DLL.)

## Signing (optional)

Pass a P-256 private `did:key` at build time to embed a signing key; records then
carry a badge.blue inline attestation:

```sh
dotnet build /p:SigningPrivateKey=did:key:z...
```

Without it, records publish unsigned.
