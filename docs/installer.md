# Installer

Cross-platform Avalonia desktop GUI that gets the mod onto disk and the
player's atproto credentials into `config.json`. Modelled directly on
sts2.at's installer ([`installer/`][sts2-installer]).

## Goals

1. Detect or accept the RoR2 install path.
2. Provision BepInEx if it's missing (or guide the user to install r2modman).
3. Capture handle + app password, validate live, write `config.json`.
4. Apply the Steam launch option tweak on Linux / Steam Deck.
5. Show "Atmosphere" (atproto connection) status the same way the in-game
   badge does, so the user has confidence before they start the game.

The installer is **not** required — players who already use r2modman can
install the Thunderstore package and just edit `config.json` by hand. The
installer exists for the "I just want to play with my friends on the Deck"
path.

## Platform recipes

### Windows

- **Steam path detection.** Read `HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`
  (or `HKCU\Software\Valve\Steam\SteamPath`), then parse
  `steamapps\libraryfolders.vdf` to find the library that contains app id
  632360.
- **BepInEx provisioning.** Check for `winhttp.dll` next to
  `Risk of Rain 2.exe`. If absent, drop in `BepInExPack` (bundled in the
  installer's resources) — exactly the file layout from
  `bbepis-BepInExPack` / `RiskofThunder-RoR2BepInExPack`.
- **Mod folder.** Write to
  `<game-path>\BepInEx\plugins\jphastings-ror2-at\{ror2-at.dll,manifest.json,config.json}`.

### Steam Deck / Linux Proton

This is the path that needs the most care; RoR2 is a Windows binary so
BepInEx loads under Proton via the `winhttp.dll` proxy. There's no
`run_bepinex.sh` for RoR2 — that wrapper is for native-Linux Unity games.

- **Steam path detection.** Default `~/.steam/steam/steamapps/common/Risk of Rain 2/`.
  Same `libraryfolders.vdf` parse for non-default libraries.
- **BepInEx provisioning.** Same Windows-style files in the same locations
  (everything Proton-side is Windows-flavoured).
- **Steam launch option.** Set
  `WINEDLLOVERRIDES="winhttp=n,b" %command%` as the game's Steam launch
  option. This is the single most-forgotten step. Doing it correctly:
  - Read `~/.local/share/Steam/userdata/<steamID>/config/localconfig.vdf`,
    find `apps.632360.LaunchOptions`, set it.
  - Steam must be closed when we write this — otherwise Steam overwrites our
    edit on exit. Detect with `pgrep steam` and prompt.
  - If we can't safely write the file (Steam running, permissions, etc.),
    show the user the exact string and copy-to-clipboard button. r2modman
    does this same fallback.
- **Mod folder.** Same path under the Proton-emulated tree:
  `<game-path>/BepInEx/plugins/jphastings-ror2-at/...`.

### macOS

Skip in v0. RoR2 doesn't have a native macOS build (Whitelight is
Windows/Linux), and the few macOS players run via CrossOver, which the
installer can't reliably probe. Document the manual path in the README
instead.

## Credential capture

Same UX as sts2.at:

1. Handle field (e.g. `you.bsky.social` or a DID).
2. App password field — show/hide toggle, link to
   `https://bsky.app/settings/app-passwords`.
3. **Validate button**: resolve the handle via Slingshot → log in to the PDS
   via `createSession` → show success or the actual error from the PDS. This
   surfaces credential problems *before* the user starts a run and wonders
   why nothing is being posted.
4. Write `config.json` next to the plugin DLL.

The `Validate` flow uses `ror2.at.core` directly — the same code paths the
in-game mod uses. Sharing the library between installer and mod is a
deliberate forcing function that keeps the auth state semantics consistent.

## Updates

The installer is also the update path:

- Check the installed mod's `manifest.json` `version` against the bundled
  version. Offer an in-place upgrade that preserves `config.json` and the
  outbox directory.
- Detect a Thunderstore-managed install (r2modman puts mods under
  `<r2modman-profile>/BepInEx/plugins/...`) and refuse to upgrade — point
  the user back at r2modman for those.

## What we deliberately don't ship

- **BepInEx automatic install on first launch from the mod.** sts2.at's mod
  writes a template `config.json` on first run if missing; we'll do the same
  for `config.json`, but BepInEx itself must be provisioned by either
  r2modman or our installer. The mod assumes BepInEx is already there.
- **DRM / signing checks.** We don't verify whether the player paid for the
  game. That's Steam's job.
- **Telemetry on the installer.** The installer reports nothing to us. The
  only network calls it makes are to the user's PDS (and Slingshot) on
  validation.

## Layout (proposed)

Same shape as sts2.at's installer:

```
installer/
├── App.axaml
├── App.axaml.cs
├── MainWindow.axaml
├── MainWindow.axaml.cs
├── ModInstaller.cs
├── SteamPathFinder.cs        # libraryfolders.vdf parse
├── LaunchOptionsWriter.cs    # localconfig.vdf edit (Linux only)
├── installer.csproj
├── Strings.cs                # i18n keys
├── Strings.<locale>.resx     # the same 12 locales sts2.at supports
└── Resources/
    ├── ror2-at.dll
    ├── manifest.json
    ├── config.example.json
    └── bepinex/              # bundled BepInExPack for first-time install
```

[sts2-installer]: https://github.com/jphastings/slay-the-spire-ii-atproto/tree/main/installer
