# AGENTS.md — InfoLCD Apex Server

Space Engineers 1 mod. Part of Chris's mod collection under
[`gitpush-mod`](https://github.com/gitpush-mod).

## What this is

**InfoLCD - Apex Server** is the server-side edition of
[InfoLCD - Apex Update](https://github.com/gitpush-mod/se-infolcd-apex-update),
built so **console players on crossplay servers** can use the InfoLCD apps.
Status: **open alpha, in development.** Until the spikes land, the code is
still Apex Update's.

Why it exists: consoles can't run mod scripts. Apex Update's apps run on each
player's own machine, so a console refuses to load a world that has it. This
edition runs **only on the server**. The server runs the apps and draws the
screens, and the game sends the finished screens to every player (PC, Xbox,
PlayStation).

It's a fork of Apex Update with **shared git history** (forked at `341f740` on
2026-10-21), so fixes can be cherry-picked across. Apex Update and
[Apex Advanced](https://github.com/gitpush-mod/se-infolcd-apex-advanced) stay
client-side only. This repo is the one deliberate exception (memory
`[project_infolcd_client_only]`).

## Where work lives (RULE — non-negotiable)

**Every task on this mod is a ticket on its project board.** YOU (the agent) create the ticket BEFORE touching anything. No exceptions for "small" work.

**Board:** [InfoLCD Apex Server board](https://github.com/orgs/gitpush-mod/projects/13)

Concrete rules — same as TCS ticket-first:

- **Starting work?** Open a ticket in this repo, add to the board, set Status = **In Progress**, then start.
- **Have an idea for later?** Ticket in **Backlog**. Not in memory, not in a README, not in NOTES.md.
- **Need Chris to check something before closing?** Move the ticket to **In QA** and comment with exactly what he needs to look at. Do NOT set to Done — that's Chris's call after review.
- **Finished?** Close the ticket with a closing summary comment (what you did / problems + solutions / anything NOT done).
- **Same-session micro-work?** Open + close in the same session — but the ticket exists.
- **Older than 30 days in Done?** The org's weekly auto-archive job (`gitpush-mod/.github`) moves it to Archived. The closed ticket + CHANGELOG entry persist.

Automation: new issues and PRs are auto-added to the board (`add-to-project.yml`). Closing a ticket as completed appends an entry to this repo's `CHANGELOG.md` through a `bot/changelog` PR (workflow in `gitpush-mod/.github`). Both use the `PAT_MOD_PROJECT` org secret, which is already installed.

**Do NOT** work on this mod without a ticket. If Chris asks for something small, create + immediately-close the ticket. It's still faster than the paper-trail debt of un-ticketed work.

### The epic and the tester (this repo only)

Everything here rolls up to **[se-infolcd-apex-update#20](https://github.com/gitpush-mod/se-infolcd-apex-update/issues/20)**,
the Discord-synced thread with our console tester, PocketOtter.

- **Every new ticket becomes a sub-issue of #20**, and its body starts with `Epic: gitpush-mod/se-infolcd-apex-update#20`.
- **Board 3 auto-adds #20's sub-issues to the Apex Update board.** After linking a ticket, remove it from board 3. It belongs on board 13 only.
- **Player-facing updates go on #20 as comments.** They mirror to PocketOtter's Discord thread under Chris's name, so **ask Chris before posting**. Write for a player: plain words, no code, and bare URLs. A markdown link posted from GitHub came through badly in Discord on 2026-10-16.
- Technical discussion stays in this repo's tickets.
- **No `bug` or `Suggestion` labels on dev tickets.** Those labels mirror to the Discord feedback channel.
- Keep #20 open until the release ticket closes it.

## For agents

- **Game:** Space Engineers 1
- **SE modding skill:** Chris has an SE modding Claude skill
  ([`Godimas101/se-claude-skill`](https://github.com/Godimas101/se-claude-skill))
  — use it when working on this mod
- **SDK:** `D:\SteamLibrary\steamapps\common\SpaceEngineersModSDK\`
- **Vanilla SBCs (authoritative for TypeId/SubtypeId + schema baselines):**
  `D:\SteamLibrary\steamapps\common\SpaceEngineers\Content\Data\`
- **Workshop mods (dependencies / reference):**
  `D:\SteamLibrary\steamapps\workshop\content\244850\`
- **Design notes:** [`docs/design.md`](docs/design.md) records what the game's own code says about server-side LCDs. Read it before touching the driver.
- **Upstream:** `se-infolcd-apex-update` (remote `upstream`, fetch-only). **Sibling:** `se-infolcd-apex-advanced`.

## Key invariants

- **Server-side only.** All of this mod's code runs on the server. Published
  builds carry the **Server Scripts** tag (`ServerScripts`) on mod.io, and on
  Steam if it ever goes there. With that tag the game skips the mod's scripts
  on every machine that isn't the server, consoles included. Without it,
  consoles refuse to load the world.
- **Self-gate anyway.** Local, unpublished copies have no tag, so their scripts
  also load on clients. Every entry point returns early unless
  `MyAPIGateway.Multiplayer.IsServer`.
- **Nothing that needs a player's machine.** No local player, camera, input,
  HUD/GUI, sounds or `MeasureStringInPixels`. None of it exists on a dedicated
  server.
- **Network budget.** Every redraw is sent to the players in range. Skip
  unchanged frames, throttle refresh, and keep sprite counts sensible.
- **Server CPU budget.** Every screen's work now runs on the server instead of
  on players' PCs. Share scans per grid, budget work per tick, and skip grids
  with no player nearby.
- **Only touch screens assigned to us.** Never take over a screen that runs a
  built-in script or another mod's script.
- **Coexist with Apex Update.** Use unique IDs (session component, script IDs,
  Custom Data markers) so both mods can load in one world.
- **Backward compatibility.** Keep Apex Update's Custom Data sections and keys
  so configs carry over. Add keys; never remove or rename them.
- **Null safety.** Always check block/component existence before use.
- **Position-based screen layout.** Calculate remaining space from current draw
  position — don't hardcode layouts that break on different LCD sizes.

## Structure

- `Data/Scripts/SG/`: the 19 LCD apps (`MahLCDs_Summary_*.cs`), config
  (`MahLCDs_Config.cs`) and drawing helpers (`MahLCDs_SurfaceDrawer.cs`). The
  apps are still written as Text Surface Scripts (Script dropdown,
  `[MyTextSurfaceScript]`). Converting them to server-driven apps is the work.
- `Data/AdditionalItems.ini`: config
- `docs/design.md`: server-side design notes
- `metadata.mod`, `thumb.jpg`: mod metadata. There is **no `modinfo.sbmi`**
  yet; the first publish creates one.

## How to verify (before flagging In QA or closing)

- **Build check.** `powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-check.ps1` compiles `Data/Scripts` against the real Bin64. The baseline at the fork is 0 errors.
- **Local test copy.** Test from `%AppData%\SpaceEngineers\Mods\InfoLCD - Apex Server`, copied from this repo. Never copy Apex Update's AppData folder: its `modinfo.sbmi` carries Apex Update's Workshop ID.
- **Single-player first.** The host is the server, so the server path runs.
- **Then a local dedicated server plus a PC client.** Check that screens update for the client, nothing of ours runs on the client, and a late joiner sees the last frame.
- **Console path.** Only a published build with the Server Scripts tag proves it: open-alpha rounds on the crossplay test server, tracked in tickets and reported on #20.
- **Save-safety.** Load an existing world, add or remove the mod, verify no CustomData strings broke and no blocks vanished.
- **Look for LINQ or per-tick allocations** in scripts before pushing. On this mod they cost the whole server.
- **Watch the logs.** Client: `%AppData%\SpaceEngineers\SpaceEngineers_*.log`. Dedicated server: the `.log` files in the instance folder. If red or yellow lines mention the mod, quote them in the ticket.

## Porting fixes from Apex Update

- `git fetch upstream`, then `git cherry-pick -x <sha>` (`-x` records the source commit). The `upstream` remote fetches `main` only, no tags, and its push URL is disabled.
- When an Apex Update ticket changes shared app or config code, note on that ticket whether it needs porting here, and open the port ticket on this board.

## Publishing + feedback flow

Wired into the [TCS Discord bot](https://github.com/The-Canadian-Space/tcs-forum-watcher). Full flow (what mirrors when, tag map, gotchas): [`mods/AGENTS.md` → Publishing + feedback flow](../AGENTS.md) or the wiki at [`discord/release-and-feedback-flow`](https://docs.thecanadian.space/discord/release-and-feedback-flow/).

- Mod tag: **`SE: Apex Server`**.
- Issues labelled `bug` or `suggestion` → `🐛-mod-feedback` (auto).
- GitHub releases → persistent thread in `📦-mod-updates`. **Public.**
- Open-alpha builds go to mod.io as **InfoLCD - Apex Server (Open Alpha)**, tagged **Server Scripts**. Chris uploads them, and the upload is tracked on the board. There's no Steam Workshop listing yet.
- **Releases are user-driven.** Do NOT run `gh release create` or push a release tag autonomously. If the work looks releasable, **ask** — describe the release notes and let Chris decide.

## MUST NOT

- Modify vanilla SE game files (`steamapps/common/SpaceEngineers/`)
- Publish without the **Server Scripts** tag. Consoles won't load the world.
- Publish under Apex Update's Workshop ID (`3580736330`) or its mod.io page. This is a separate mod.
- Add anything that has to run on a player's machine. That's Apex Update's job.
- Push to `upstream` (Apex Update) from this checkout
- Break save-game compatibility without an explicit version bump
- Upload repo `Sandbox` files to a live SG server (wipes auto-pulled dependencies —
  see memory `[project_sg_core_dependencies]`)

## Related

- Epic + tester thread: [se-infolcd-apex-update#20](https://github.com/gitpush-mod/se-infolcd-apex-update/issues/20)
- Upstream: [`gitpush-mod/se-infolcd-apex-update`](https://github.com/gitpush-mod/se-infolcd-apex-update)
- Sibling: [`gitpush-mod/se-infolcd-apex-advanced`](https://github.com/gitpush-mod/se-infolcd-apex-advanced)
- Parent when checked out under Chris's tree: `mods/AGENTS.md`
- Universal Golden Rules: top-level `AGENTS.md` in `VS Code Projects/`
