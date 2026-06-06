# ror2.at — planning docs

A Risk of Rain 2 (RoR2) mod that posts per-run telemetry to the player's
[AT Protocol](https://atproto.com) PDS, plus a reusable game-agnostic core
that another C# game's mod can consume.

The docs in this folder are a planning artifact, not a spec. They capture what
we know going in so the implementation phase doesn't have to re-research the
same questions. None of this is set in stone; see
[`open-questions.md`](open-questions.md) for the things we'll decide as we go.

## Read in this order

1. [`architecture.md`](architecture.md) — three-layer model: core lib, RoR2 mod,
   installer. Where each layer's responsibilities end.
2. [`core-library.md`](core-library.md) — the game-agnostic atproto library.
   API surface, what it must abstract over so it works in Unity and Godot.
3. [`lifecycle-hooks.md`](lifecycle-hooks.md) — RoR2-specific. The events we
   hook to decide "is now a meaningful moment to emit?"
4. [`stats.md`](stats.md) — what data each emission carries. Built on RoR2's
   `RoR2.Stats.StatSheet` plus extracted run/inventory state.
5. [`achievements.md`](achievements.md) — how the mod listens for unlocks
   and snapshots lifetime achievement progress per peer.
6. [`atproto-records.md`](atproto-records.md) — lexicon shape, rkey strategy,
   how multi-update and multiplayer work.
7. [`installer.md`](installer.md) — Windows + Steam Deck install UX. BepInEx
   provisioning, credential capture, atmosphere check.
8. [`open-questions.md`](open-questions.md) — decisions still owed.

## Sister projects

This is the second mod in a small series. The first, [sts2.at][sts2], targets
Slay the Spire 2 and has the same shape (mod writes to PDS, web reads from
PDS, no central server). Most of the atproto plumbing here is lifted from
sts2.at — the goal of the [core library](core-library.md) is to make that
sharing explicit so the third game we add is mostly just hook code.

[sts2]: https://github.com/jphastings/slay-the-spire-ii-atproto
