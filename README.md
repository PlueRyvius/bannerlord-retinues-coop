# Retinues (with Bannerlord Coop support)

Retinues is a custom troop mod for **Mount & Blade II: Bannerlord**.  
It adds flexible troop customization and supporting mechanics designed to fit seamlessly into any playthrough.

**Retinues is made by [bolaft](https://github.com/bolaft)**; all of its design and almost all of its code are theirs.
This fork of [bolaft/bannerlord-retinues](https://github.com/bolaft/bannerlord-retinues) adds support for
[Bannerlord Coop](https://steamcommunity.com/sharedfiles/filedetails/?id=3770450698), so every player in a co-op
campaign has their own custom troops. Single player plays exactly as in Retinues.

[**→ Player Manual**](https://bolaft.github.io/bannerlord-retinues/)
[**→ Nexus Mods**](https://www.nexusmods.com/mountandblade2bannerlord/mods/8847)
[**→ Steam Workshop**](https://steamcommunity.com/sharedfiles/filedetails/?id=3592533567)

(The manual and the mod pages are bolaft's, for Retinues itself.)

---

## Bannerlord Coop support

### What it does

- **Every player has their own troops.** Custom troops belong to a clan or kingdom, not to "the player": each
  player's clan gets its own retinues and, once it holds a fief, its own regular troops, and each player's kingdom its
  own kingdom troops. Up to 5,000 custom troops can exist at once, so a server with dozens of players has room.
- **The server owns every troop.** When a player edits a troop in the editor, their game sends the edit to the
  server, which checks that the troop belongs to that player's clan or kingdom and makes the change as that player
  (gold, stock and roster changes included). The server then sends the changed troops back to the players. A player's
  game never changes troops on its own, so no two games can disagree about them.
- **The server's settings win.** Players use the host's Retinues settings for the session, whatever their own say.
- **Old saves carry over.** A save from before this version loads with its troops given to the player's own clan and
  kingdom, as they were.

### Off in co-op, for now

- **Doctrines and feats** (and the troops they unlock, such as captains).
- **The global editor** (editing culture and other factions' troops).
- **Edits that take time**: equipment and training changes apply at once.
- **Troop import and export.**
- **The "copy your culture's troops or start from scratch?" choice** on a clan's first fief: the server cannot show
  it to a player, so a player's clan always starts from a copy of its culture's troop tree.

Item unlocks, item stocks and troop XP pools stay shared across the campaign, as in single player.

### Installing

- Install Retinues from this fork on the **server and on every player's game**: the same version everywhere. A game
  with a different co-op protocol is refused by the server, and its troop edits stay off.
- Bannerlord Coop is optional. Without it, Retinues runs as in single player. With it, Retinues loads its Coop adapter,
  `Retinues.Coop.dll`, which ships next to `Retinues.dll`.
- Coop's dedicated server cannot run Harmony, ButterLib, UIExtenderEx or MCM, so the server loads them as
  dependencies only. Retinues patches through Coop's own Harmony there and reads its settings file without MCM.

### How it is tested

Besides Retinues' own in-game tests, the co-op support is checked with the in-game test harness of
[ModderLords](https://github.com/PlueRyvius/ModderLords). Its `retinues/editor` suite starts a dedicated server, joins
it with a player's game, and does what a player does in the troop editor:

- creates the player's troops;
- opens the editor;
- renames a troop and trains a skill;
- equips and unequips an item;
- adds and removes an upgrade.

After each edit it checks that the server and the player's game hold the same troop. The same suite runs in single
player.

### Reporting co-op bugs

Report co-op problems **on this fork's issues**, not to bolaft: Retinues' own pages are for Retinues itself. Please
include the Retinues `debug.log` from the player's game and, if you can, from the server.

---

## Build Instructions

See **[BUILD.md](BUILD.md)** for complete setup and usage.

- Quick build (bash / Git Bash):
  ```bash
  ./build.sh -v 12
  ```
- The Coop adapter (`src/Retinues.Coop`) compiles against Bannerlord Coop's own DLLs, found in its Steam Workshop
  folder (item 3770450698); pass `-p:CoopBin=<Coop's bin\Win64_Shipping_Client>` if it is installed elsewhere.

---

## Contributing

- **Fork the repository** and create a feature branch.
- **Test your changes** in-game before submitting a pull request.
- **Format your code** with `csharpier`.
- **Describe your changes** clearly in the PR and reference related issues if applicable.

> Please keep commits focused and include a short rationale in the message body.

---

## Support

- **Bug Reports:** open an issue on GitHub or report on the Nexus Mods page.  
- **Feature Requests:** use the Nexus Mods page.

If you’re reporting a bug, please include:
- Game and mod version
- A short reproduction path
- Relevant logs (if available)
