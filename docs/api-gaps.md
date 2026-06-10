# Package API gaps found building the RoR2 mod

Notes from writing a real consumer (`mod/`) against `ByJP.AtprotoGaming.Core`.
The mapping layer (`mod/Mapping/`) compiles against the package unchanged, so the
surface is coherent — these are friction points and missing affordances, ranked
by how much they bit a real game integration.

## Resolution status (package v0.x)

| # | Gap | Status |
|---|---|---|
| 1 | Append dedupe / crash duplication | ✅ `SetAcquisitions` (replace) + `AddAcquisition` dedupe by `instanceId` |
| 2 | No `forkedFrom`/top-level setter | ✅ `PlaySession.ForkPlay(id?)` clones values + sets `forkedFrom` |
| 3 | Route stops immutable, lag a stage | ✅ `RouteArrive`/`RouteLeave` upsert by `instanceId` (`arrivedAt`/`leftAt`) |
| 4 | No monotonic/max progress | ✅ `UpdateProgress(name, value, ProgressOp{Add,Subtract,Min,Max})` |
| 5 | Increment vs absolute observation | ☑️ confirmed fine — both modes load-bearing |
| 6 | Steam lookup `ulong` vs string | ✅ `SteamDidResolver.LookupDid(string)` everywhere |
| 7 | Achievement recording | ✅ `Stats.AchievementsUnlockedAsync(unlocked, total)` writes the count into the linked actor.stats record (the lexicon stores counts, not per-achievement entries) |

The mod (`mod/Mapping/PlayRecordMapper.cs`) now re-states the full acquisitions
list and re-arrives/leaves every route stop each snapshot, with no per-emit
bookkeeping — the package dedupes, so a mid-run crash can't duplicate entries.

Original findings, for the record:

## What worked well (don't lose these)

- **Three-adapter wiring** (`ILogSink`/`IFileSystem`/`IClock`) mapped onto BepInEx
  in ~10 lines (`BepInExLogSink`, `FileSystem.NextTo<Plugin>()`). No engine types
  leaked into the package.
- **`OpenPlay` → `BeginUpdate` → `CommitAsync`** reads naturally and batches a
  whole throttle window of events into one PUT — exactly what RoR2's noisy event
  stream needs.
- **`AtValue` implicits** — `SetProgress("totalKills", killsLong)`,
  `SetSetting("difficulty", 5)`, and `SetProgress("killsAs", nestedJsonObject)`
  all "just work" with no `JsonValue.Create` noise.
- **Offline-by-default** — `CommitAsync` queues when offline, so the RoR2 side has
  zero network/retry code. `DerivePlayID(startedAt, seed)` gives the
  multiplayer-convergent rkey for free.
- **`SetProgress` guard** rejecting `"outcome"`/`"route"` (plus the camelCase
  analyzer) caught a real foot-gun while mapping dynamic stat keys.
- **`RollingStats.EnsureAndUpdateAsync` returns the stats URI**, and `stats` is
  auto-injected at write time — the lexicon's required `stats` field was handled
  for us. `PlaySession.Rkey` and `PutResult.Uri` were there when we wanted to log
  the landed record.

## Gaps

### 1. Append ops have no dedupe key → duplicates across snapshots and on crash
`AddAcquisition`/`AddRouteStop` append, and ops are replayed against the *real*
record at flush. The consumer must therefore send only the items it hasn't sent
before — but the session exposes no read of current acquisitions, so we track an
`_emittedItems` counter in memory (`PlayRecordMapper`). That counter resets if the
game crashes mid-run, while the record on the PDS keeps its items → **re-appended
duplicates on resume**. Crash-resilience is a headline feature of the package, so
this is the most important gap.

**Suggestion:** make appends idempotent by a caller-supplied key (upsert/dedupe at
apply on `item.id` + an optional occurrence key), or add a `SetAcquisitions(list)`
replace op. RoR2 re-reads the full inventory each snapshot, so a replace would be
strictly simpler for us than tail-tracking.

### 2. No setter for `forkedFrom` (or other top-level lexicon fields)
`PlayUpdate` covers progress/settings/acquisitions/route/outcome/playingWith/finish,
but not the lexicon's `forkedFrom` (a `com.atproto.repo.strongRef`). The package
even ships `StrongRef.Create(...)` that produces exactly this value — with nowhere
to attach it on a play record. RoR2 doesn't fork saves, but StS2-style consumers
will need it.

**Suggestion:** `SetForkedFrom(JsonObject strongRef)`, or a general
`Set(string topLevelField, AtValue)` escape hatch.

### 3. Route stops can't be updated after append (`endedAt` backfill)
A stage's `endedAt` is only known when the player leaves it, so with append-only
`AddRouteStop` we withhold each stop until it completes (`PlayRecordMapper`
skips stops with a null `endedAt`). Result: the **current stage never appears** in
in-progress snapshots — the live record always lags one stage behind, which defeats
the "still alive on Petrichor V, 18 min in" liveness the docs want.

**Suggestion:** upsert route stops by `id` so an open stop can be written and later
amended with its `endedAt`.

### 4. No monotonic / `max` progress op
RoR2 has high-water-mark stats (`highestDamageDealt`, `highestLevel`). With the
offline queue, an older queued snapshot can flush after a newer one; absolute
`SetProgress` would then regress the high-water value to the stale reading.

**Suggestion:** `MaxProgress(name, value)` (apply = `max(existing, value)`),
mirroring `IncrementProgress`'s resolve-at-write model.

### 5. `IncrementProgress` doesn't fit game-authoritative absolute counters
RoR2's `StatSheet` is already absolute, so the mapper only ever uses `SetProgress`
and never `IncrementProgress`. Not a defect — just confirmation that **both** modes
are load-bearing, and that the absolute path is the common one for games whose
engine owns the totals. Pair with #4: absolute-with-monotonic covers most game
stats; increment covers client-derived deltas.

### 6. `SteamDidResolver.LookupDidAsync(ulong)` vs string ids everywhere else
`participant.steam` (lexicon) and the engine-free snapshot carry the SteamID64 as a
decimal string; the resolver takes a `ulong`, so the mapper parses back and forth
and has to branch on "not numeric" (EGS users).

**Suggestion:** add a `LookupDidAsync(string subject)` overload (or accept the
decimal string directly), matching how ids appear in records.

### 7. Achievement recording — RESOLVED
Original assumption was a separate per-achievement collection. The upstream
`games.gamesgamesgamesgames.actor.stats` lexicon instead stores achievements as a
count (`achievements: { unlocked, total }`), so the fix is
`Stats.AchievementsUnlockedAsync(game, source, unlocked, total)` — it finds/creates
the stats record, sets the counts (preserving playtime/lastPlayed), and no-ops when
unchanged so the bulk profile-load re-fire doesn't spam. The mod recomputes
`(unlocked, total)` on each unlock and pushes them, gated on a count change.

## Not gaps (verified against source while writing this)

- `PlaySession.Rkey` exists — we can log/share the resolved key (and it's the
  sanitised one, so multiplayer peers can confirm convergence).
- `CommitAsync`/`PutResult` returns the published `Uri`.
- `stats` is resolved and injected at write time — consumers don't set it.
- The package adds its own `versions.additional` entry; we only pass our mod's.
