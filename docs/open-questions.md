# Open questions

Decisions deferred from the planning docs. Roughly ordered by when we'll
need to resolve them.

## Resolved by first build

1. **Target framework.** `netstandard2.1` for the core might be too modern
   for BepInEx 5 + .NET Framework 4.7.2. Either drop core to
   `netstandard2.0`, or build the RoR2 mod against .NET 9 if BepInEx 5 can be
   coaxed into it (probably no). Most current RoR2 mods target `net472`.
   → resolve by trying `netstandard2.0` first.

2. **`MMHOOK_RoR2.dll` vs Harmony.** The plan says MonoMod hooks. Confirm the
   exact event signatures (`Run.onRunStartGlobal` parameter list,
   `Run.onServerGameOver` event vs. virtual). If any of the events the doc
   names turn out to be MonoMod-only (not vanilla), fall back to
   `On.RoR2.Run.*` patches for those specifically.

3. **R2API.NetworkCompatibility yes/no.** A telemetry mod is networking-neutral
   in spirit, but lobby checks may flag us as incompatible without the
   attribute. Add the dependency if the empty-attribute approach doesn't
   work.

## Resolved before first release

4. **Host-only vs every-peer writes.** The doc currently says every peer
   writes its own perspective. Validate this is the right call by playing
   a multiplayer test run: does the host's perspective alone cover what we
   want to show, or do we genuinely benefit from each peer's `currentStats`?
   If host-only, the mod becomes server-only and the install story changes.

4a. **ProperSave SyncVar timing.** With ProperSave installed, we restore
    `startTimeUtc` via `ProperSave.Loading.OnLoadingStarted` →
    `CurrentSave.GetModdedData<DateTime>(...)` and use it for the rkey.
    Verify in a real lobby that on the host the restored value reaches
    clients via SyncVar replication *before* their first rkey
    derivation, so all peers converge on the same rkey from the first
    snapshot. If there's a one-frame divergence, gate emission on a
    stable signal (e.g. wait until `Run.instance.GetStartTimeUtc()`
    matches the host's broadcast value).

5. **Throttle interval default.** 60 s is a starting guess. Lower bound is
   "how often is meaningful state actually changing" — probably 30 s in
   late-loop chaos. Upper bound is "how long can the web viewer stale before
   the user notices" — probably 2 min. Pick and document.

6. **Item pickup timeline vs. per-snapshot diffs.** [`stats.md`](stats.md)
   currently records both `items` (final state) and `pickups[]` (timeline).
   The timeline is nice but doubles the size. Decide if the web viewer
   actually needs the timeline or if it can diff successive snapshots.

7. **Boss kill list granularity.** Storing every boss kill is small (a
   handful per run), but the names overlap with vanilla `damageDealtTo.<body>`
   stats. Decide whether `bosses[]` adds enough for what it costs.

8. **Damage histogram.** Cheapest correct implementation requires an IL hook
   on `HealthComponent.TakeDamage`. Decide if it's worth the maintenance
   surface or if `highestDamageDealt` is enough.

9. **Steam Deck launch-option auto-edit.** Editing `localconfig.vdf` while
   Steam might re-write it is fiddly. Decide between "always copy-to-clipboard
   and let the user paste" (simple, sts2-style) versus "do it for them when
   Steam is closed" (better UX, more failure modes). sts2.at didn't have to
   solve this because StS2's mod loader doesn't need a launch option.

## Achievements

9a. **Catalogue dump cadence.** [`achievements.md`](achievements.md)
    proposes a one-shot mod console command (`dump_achievements`) that
    writes all `AchievementDef`s to disk for the web reader to consume.
    Open: does this live in the mod or in a separate `extract/` tool the
    way sts2.at's `extract/` separates the game-data dump pipeline from
    the runtime mod?

9b. **Modded achievements in the record.** A player with modded characters
    (Trickster, Chirr, etc.) will accumulate `achievementsHeld` entries
    that the web reader doesn't recognise. Decide: drop them from the
    record (whitelist against the catalogue dump), include them as
    "unknown achievement id" so the timeline at least shows *something*,
    or include with a `source: "mod"` tag if we can detect that. The
    `AchievementManager` doesn't tell us which assembly registered each
    def — would need a `PluginInfos` cross-check.

## Lexicon / web reader

10. **Lexicon stewardship.** The `me.byjp.pesos.ror2.run` NSID will live in
    `lexicons/`. Decide whether to publish it at a `_lexicon` HTTP endpoint
    (so other clients can fetch it) or leave it repo-local for now.

11. **`games.gamesgamesgamesgames.game` record for RoR2.** We need to either
    register one with Birbhouse or set `game` to null until a canonical
    record exists. → coordinate with Birbhouse closer to release.

12. **Web reader integration.** sts2.at has a SvelteKit web app at
    `web/`. We'll need an equivalent for RoR2 — but maybe a unified
    `pesos.byjp.me` that handles both games is better than two parallel
    sites. Open.

## Distribution

13. **Thunderstore-only vs Thunderstore + installer.** If r2modman covers
    95% of users, the installer is mostly Steam Deck-and-credential-helper.
    Decide whether to ship the installer at all in v0 or just publish on
    Thunderstore and provide a `setup.sh` for the Deck.

14. **Signing key publication path.** sts2.at uses
    `web/static/.well-known/sts2-mod-keys/keys.json`. Either replicate the
    pattern at a parallel path for RoR2, or unify under a single
    `keys.json` keyed by game id.

15. **Localisation scope.** sts2.at has 12 locales (machine-translated). For
    v0 of ror2.at: English-only is fine. Adopt the same 12 if and when the
    install base warrants it.
