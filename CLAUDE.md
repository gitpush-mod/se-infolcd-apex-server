# CLAUDE.md

Claude Code loads this file automatically at session start.

**→ Read [AGENTS.md](AGENTS.md) first.** It has this mod's canonical rules (target game, invariants, ticket-first and epic rules).

Also read parent [`../AGENTS.md`](../AGENTS.md) and universal [`VS Code Projects/AGENTS.md`](../../AGENTS.md).

Reminders that bite:
- **Server-side only, the opposite of Apex Update.** Our code runs on the server. Published builds need the Server Scripts tag, and every entry point self-gates on `IsServer`.
- **Epic tickets are sub-issues of their epic** (today: se-infolcd-apex-update#20). Take them back off board 3 after linking. Comments on Discord-synced issues mirror to Discord, so Chris approves them first.
- **Backward-compat on CustomData** — never rename or remove config keys.
- **Never publish under Apex Update's Workshop ID.** Its `modinfo.sbmi` was removed at the fork on purpose.
