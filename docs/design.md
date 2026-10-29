# Server-side design notes

What Space Engineers' own code says about running InfoLCD on the server. These
notes come from reading the game's Bin64 assemblies on 2026-10-21; no game
files were changed. The spikes on the
[board](https://github.com/orgs/gitpush-mod/projects/13) confirm or correct
each point in-game. Update this file when they do.

## 1. Why Apex Update fails on console

`Sandbox.Game.World.MyScriptManager.LoadData()` walks the world's mod list.
For each mod that has workshop or mod.io data:

1. If its tags contain `ServerScripts` **and this machine is not the server**,
   the game skips the mod's scripts entirely. Nothing is compiled.
2. Otherwise it compiles the scripts. On a platform that can't compile
   (`MyLoadingRuntimeCompilationNotSupportedException`, i.e. consoles):
   - a mod tagged `NoScripts` only logs a rule violation
     (`ModRuleViolation_RuntimeScripts`: "Mods in current world use scripts
     which are not supported on current platform…");
   - any other mod rethrows, and the world fails to load.

Apex Update has scripts and neither tag, so consoles fail. Tagging Apex Update
`ServerScripts` wouldn't help. Its apps draw on each player's machine, so with
the tag only the server would run them, and every screen would stay blank.

What this means for this mod:

- **Publish with the Server Scripts tag.** The in-game publisher adds
  `NoScripts` automatically when a mod has no script files
  (`MyWorkshop.PublishItemBlocking`). `ServerScripts` has to be picked by hand.
- **Self-gate on `IsServer`.** The tag check only applies to mods with workshop
  data (`ModItem.IsModData()`), so a local, unpublished copy is compiled on
  every machine.
- **No console-hosted games.** In console single-player or a console-hosted
  game the console is the server, so it has to compile, and can't.

**Confirmed in S2 (2026-10-28):** a PlayStation player joined the test
crossplay dedicated server running the `ServerScripts`-tagged build with no
error and saw the server-drawn screens. So did a PC player on the same server.

## 2. How a server-drawn frame reaches players

- `IMyTextSurface.DrawFrame()` → `MyTextPanelComponent.DispatchSprites` → a
  `MySerializableSpriteCollection` that is replicated to clients. A
  Programmable Block's screen uses the same path on a dedicated server.
- **LCD panels:** the frame lives in a synced field
  (`MyTextPanelComponent.m_externalSprites`), and the block's saved data has a
  `Sprites` field (`MyObjectBuilder_TextPanel.Sprites`). **But S1 (single
  player, 2026-10-27) found it empty**: a tagged LCD saved
  `<Sprites><Length>0</Length>`, and no drawn frame was anywhere in the world
  save. So the driver must redraw every screen right after load, and a late
  joiner sees a blank screen until the next draw (confirmed in S2, below).
- **Multi-screen blocks** (cockpits, programmable blocks, …):
  `MyMultiTextPanelComponent.OnUpdateSpriteCollection` is an event marked
  `[Reliable, Server, Broadcast, DistanceRadius(32)]`. Only players within
  32 m receive each update. Their saved data has a `Sprites` field too
  (`MySerializedTextPanelData.Sprites`), but S1 found no drawn frames there
  either. Someone who walks up later can see a stale or blank screen until the
  next send, so these screens need a periodic re-send.
- For drawn sprites to show, the screen should be in script mode with no
  script selected, as with a Programmable Block screen. Confirmed in S1: the
  server sets both itself, and the screen switches over with no player action.
- **Players don't keep drawn frames (S2, 2026-10-28, PlayStation and PC).** A
  player who joins, or who walks 40 m away and comes back, sees a blank screen
  until the next redraw, on LCD panels and cockpits alike. So the driver redraws
  a screen when a player joins or comes near it, and keeps a periodic redraw
  as the fallback. The redraw interval is the longest a player can face a blank
  screen, so change detection can't simply skip unchanged frames.

## 3. Reusing the 19 apps

- `MyTextSurfaceScriptBase(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)`
  only stores fields (`m_surface`, `m_block`, `m_size`, `m_halfSize`,
  `m_scale`, colours). `MyTSSCommon.Run()` only reads `ScriptBackgroundColor`
  and `ScriptForegroundColor`.
- So a session component can construct an app directly and call `Run()` on its
  own schedule.
- Known changes:
  - Every app returns early on `IsDedicated` (19 places).
  - Every app registers itself with
    `[MyTextSurfaceScript("LCDInfoScreen…", "$IOS LCD - …")]`. Apex Update
    registers the same IDs, so they'd clash in a world with both mods. They
    would also put dropdown entries on PC clients of non-crossplay servers.
  - Frames go straight to `DrawFrame()`, with no buffer to compare against, so
    unchanged frames can't be skipped yet.
- The apps don't use the local player, camera, input or
  `MeasureStringInPixels` (checked at the fork).

## 4. Setting up a screen from a console

Confirmed on PlayStation by our alpha tester on 2026-10-22 (S0):

- **Custom Data works.** It opens and takes typing with a controller. There's
  a learning curve, but it's workable, and a plugged-in keyboard and mouse
  work too.
- **Renaming blocks works.**

So Custom Data is the setup surface, the same place Apex Update keeps its
config. Block-name tags aren't needed for console access.

What the game code says, for reference: the terminal's Custom Data control
(`MyTerminalBlock.CreateTerminalControls`) has no platform gate, and the text
editor (`MyGuiScreenText`) checks
`MyPlatformGameSettings.IsMultilineEditableByGamepad`, which is false on PC.

## 5. Crossplay server facts (community docs)

- Crossplay means Network Type **EOS** plus **Console Compatibility**, and
  mods come from **mod.io only**; a Steam Workshop mod stops the world from
  starting
  ([GameServerKings](https://www.gameserverkings.com/knowledge-base/space-engineers/space-engineers-crossplay/)).
- Server admins add a mod.io mod by ID in `Sandbox_config.sbc`, with
  `PublishedServiceName` set to `mod.io`
  ([Host Havoc](https://hosthavoc.com/wiki/game-servers/space-engineers/add-modio-mods)).
- "Some script mods can be run strictly server side (which must be tagged with
  ServerScripts)" ([SE wiki: Scripts](https://spaceengineers.wiki.gg/wiki/Scripts),
  [SE wiki: Mods](https://spaceengineers.wiki.gg/wiki/Mods)).
- A dedicated server has no login, so it can't download friends-only Workshop
  items
  ([SE wiki](https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Creating_And_Uploading_Mods)).
  A hidden mod.io listing probably has the same problem (unverified), which is
  why the alpha listing is public.
