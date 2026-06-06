# `ror2.at.core` — game-agnostic library

Working name: `ByJp.AtprotoTracker.Core`. Targets `netstandard2.1` (or 2.0 if
BepInEx 5 forces it). Zero references to any game runtime.

## Goal

Make adding atproto-PDS telemetry to a *new* C# game's mod a job of:

1. Pick which game events should mark the record "dirty."
2. Build the JSON shape of your record.
3. Hand it to `RecordPublisher.Publish(rkey, record)`.

Everything else — login, refresh, retries, queueing, identity resolution,
status surface for a status badge — lives in the core.

## Surface

### `AtprotoClient`

Direct port of [sts2.at/mod/AtProtoClient.cs][sts2-client]. No game-specific
fields.

```csharp
public sealed class AtprotoClient {
    public string Did { get; }
    public bool IsAuthenticated { get; }

    public Task LoginAsync(string pdsUrl, string identifier, string appPassword, CancellationToken ct = default);
    public Task<string> CreateRecordAsync(string collection, object record, CancellationToken ct = default);
    public Task<string> PutRecordAsync(string collection, string rkey, object record, CancellationToken ct = default);
    public Task<JsonNode?> GetRecordAsync(string collection, string rkey, CancellationToken ct = default);
    public Task<JsonNode?> ListRecordsAsync(string collection, string? cursor, int limit, CancellationToken ct = default);
}
```

Implementation owns the `_refreshLock` SemaphoreSlim, 80-minute conservative
TTL, and refresh-on-401 retry.

### `IdentityResolver`

Slingshot wrapper. Returns `MiniDoc(did, handle, pds)`.

```csharp
public static class IdentityResolver {
    public static Task<MiniDoc> ResolveAsync(string identifier, CancellationToken ct = default);
}
```

### `AuthState`

Observable status singleton. The game's UI (main-menu badge, installer health
check) subscribes to `Changed` and re-renders.

```csharp
public enum AuthStatus { Unconfigured, Checking, Ok, Failed, Offline }
public static class AuthState {
    public static AuthStatus Status { get; }
    public static string? Handle { get; }
    public static string? Did { get; }
    public static string? Error { get; }
    public static event Action? Changed;
    public static void Set(AuthStatus s, string? handle = null, string? did = null, string? error = null);
}
```

Static-on-purpose: there's one logged-in identity per mod instance.

### `Outbox`

Per-DID disk queue of prepared payloads. Each entry is the exact JSON we'll
PUT, written ready-to-go so flushing is a single XRPC call. Files are bucketed
by DID so a logged-out account's queue stays untouched until that DID logs back in.

```csharp
public sealed class Outbox {
    public Outbox(string rootDirectory, string collection);

    public void Enqueue(string did, string rkey, JsonNode payload);
    public void Remove(string did, string rkey);
    public Task FlushAsync(AtprotoClient client, Func<JsonNode, bool>? skipPredicate = null, CancellationToken ct = default);
}
```

`skipPredicate` lets the caller hold back e.g. the currently-active run's
queue file so it doesn't race the live publisher. The DID encoding step (`:` →
`%3A`) for Windows filename safety stays inside the core.

### `RecordPublisher`

The glue: takes a record + rkey, attempts a PUT, falls back to the outbox on
transient failure, drops on permanent (4xx) failure. Encapsulates the
"publish or queue" loop that today lives in
[sts2.at/mod/RunPublisher.cs][sts2-publisher].

```csharp
public sealed class RecordPublisher {
    public RecordPublisher(AtprotoClient client, Outbox outbox, string collection);
    public Task PutAsync(string rkey, JsonNode payload, CancellationToken ct = default);
}
```

### `ConfigStore<T>`

Generic load/save for a JSON config file. T is the game-specific config DTO.

```csharp
public sealed class ConfigStore<T> where T : class, new() {
    public ConfigStore(string path);
    public T LoadOrCreate(Action<string>? onFirstRunBanner = null);
    public void Save(T config);
}
```

The "no creds yet" banner is delegated to the caller so each game can phrase
it in its own log format.

### `Tid`

Deterministic TID generator from `(unixSeconds, salt)`. Lifted verbatim from
sts2.at — used to derive a stable, sortable rkey for in-progress records so
multi-update PUTs all target the same record.

### `Iso`

Helpers for the RFC3339-with-millisecond format atproto requires.

### Optional: `Signing/InlineAttestation`

The ECDSA P-256 inline-signing implementation. Off by default; the game's mod
opts in by setting a `SigningKeyProvider`. Same key-rotation story as
sts2.at — verifiers look up the `key` field in a public `keys.json`.

## What the core abstracts over

To stay engine-agnostic, the core uses small adapter interfaces for things
that differ between Unity and Godot:

```csharp
public interface ILogSink {
    void Info(string msg);
    void Warn(string msg);
    void Error(string msg, Exception? ex = null);
}

public interface IClock {
    DateTime UtcNow { get; }
    long UnixSeconds { get; }
}

public interface IFileSystem {
    string ConfigDirectory { get; }   // where config.json lives
    string OutboxRoot { get; }        // where outbox/<did>/<rkey>.json lives
}
```

The RoR2 mod wires:

- `ILogSink` → BepInEx `ManualLogSource`
- `IClock` → `System.DateTime.UtcNow` (Unity time is monotonic but not wall)
- `IFileSystem` → directory next to the plugin DLL

A future Unity-IL2CPP or Godot mod swaps these without touching core code.

## What the core does NOT do

- **Define record schemas.** The caller passes `object` or `JsonNode` payloads.
  Each game's mod owns its lexicon and its DTOs.
- **Know about Steam.** Steam ID extraction (`NetworkUser.id.value` for RoR2)
  lives in the game mod. Core only sees opaque "ally" entries if the game
  passes them.
- **Decide when to emit.** That's policy. The mod calls `PutAsync` when it
  decides the record is dirty.
- **Render any UI.** The badge/status display is the game mod's job; the core
  just publishes `AuthState.Changed` events.
- **Run any background timers.** No `Thread.Start`, no `Timer`. The game mod's
  hook callbacks drive everything.

## Layout (proposed)

```
core/
├── ByJp.AtprotoTracker.Core.csproj
├── AtprotoClient.cs
├── AuthState.cs
├── ConfigStore.cs
├── IdentityResolver.cs
├── Iso.cs
├── Outbox.cs
├── RecordPublisher.cs
├── Tid.cs
├── Abstractions/
│   ├── IClock.cs
│   ├── IFileSystem.cs
│   └── ILogSink.cs
└── Signing/
    ├── InlineAttestation.cs
    └── SigningKeyProvider.cs
```

[sts2-client]: https://github.com/jphastings/slay-the-spire-ii-atproto/blob/main/mod/AtProtoClient.cs
[sts2-publisher]: https://github.com/jphastings/slay-the-spire-ii-atproto/blob/main/mod/RunPublisher.cs
