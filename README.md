<p align="center"><a href="https://discord.gg/QvqzkB95G4"><img src="https://discord.com/api/guilds/1344851225538986064/widget.png?style=banner3" alt="Discord Banner 3"/></a></p>

***

| Channel | Status |
| --- | --- |
| Stable | [![Stable](https://github.com/crameep/TazUO/actions/workflows/build-test.yml/badge.svg?branch=main)](https://github.com/crameep/TazUO/actions/workflows/build-test.yml) |
| Bleeding Edge | [![Bleeding Edge](https://github.com/crameep/TazUO/actions/workflows/bleeding-edge.yml/badge.svg?branch=dev)](https://github.com/crameep/TazUO/actions/workflows/bleeding-edge.yml) |

# What is TazUO?
**TazUO** was originally a fork from ClassicUO with the mindset of adding features requested by users to improve QOL. **TazUO** has since moved away from ClassicUO, we will keep an eye on ClassicUO updates and incorporate changes or fixes as they have a wider user base that provides bug reports, but **TazUO** will no longer be merging all changes from ClassicUO.

# Play now
The easiest way to play is via the [launcher](https://github.com/crameep/TUO-Launcher/releases/latest).

The launcher has three update channels:
- **Stable** — Tagged releases that have been tested on the bleeding edge
- **Bleeding Edge** — Latest features and fixes, auto-built from the `dev` branch
- **Feature Branch** — Individual feature builds for testing specific changes

# Branching strategy

| Branch | Purpose |
| --- | --- |
| `main` | Stable releases only. Merges from `dev` when ready for release. |
| `dev` | Bleeding edge. All features get merged here for testing before stable. |
| Feature branches | Individual features/fixes. Merged into `dev` when ready. |

# Recent changes

## Auto-Loot Performance Optimizations
Large loot lists (500+ entries) no longer cause stuttering. Three optimizations work together:
- **Graphic Index** — Loot entries bucketed by graphic ID for O(1) lookup instead of linear scan
- **Spatial Tracking** — Maintains a set of nearby ground items instead of scanning all world items
- **Match Cache** — Caches per-serial match results with OPL-aware staleness detection (handles tooltips arriving after item creation)

## Auto-Loot Profiles
Loot configurations can now be organized into multiple profiles:
- Create, rename, delete, and reorder profiles in a sidebar
- Enable/disable individual profiles with checkboxes
- Export/import profiles via clipboard
- Import from other character configurations
- Automatic migration from legacy single-list format

## Scavenger Fix
Fixed scavenger not picking up ground items. The previous priority system caused ground items to be perpetually starved behind corpse items in the action queue. Ground and corpse items now share the same priority tier with an internal bias toward corpse items.

## Decoupled Corpse Opening
Corpse double-click opens now run on a separate queue from item moves. Previously, opening corpses blocked all looting since they shared the same action queue with a single cooldown timer. Looting near multiple corpses is now significantly faster.

## Movement Smoothness
Several per-frame and per-step performance issues identified by comparing against upstream ClassicUO have been fixed:
- **Removed debug render target** — An extra full-screen compositing pass was running every frame (debug flag left enabled)
- **Spatial door lookup** — `TryOpenDoors()` was scanning all world items on every direction change. Now uses tile-based spatial lookup
- **Cached corpse snapshots** — `GetCorpseSnapshot()` was allocating a new array every step. Now caches and only rebuilds when corpses change
- **Gated pathfinding updates** — `WalkableManager` and `LongDistancePathfinder` no longer update every frame when long-distance pathfinding is disabled

## Skills Management Window
New ImGui-based skills management interface.

## Auto-Loot Priority Tiers
Loot entries can be assigned High, Normal, or Low priority. Higher priority items are looted first.

# TazUO features
Check out the [wiki](../../wiki) for details on all the changes TazUO has made for players!

***Most*** features can be disabled if you don't want to use said feature.

- [Launcher](../../wiki/TazUO.Updater-Launcher) - Managing profiles for multiple accounts/servers
- [Grid containers](../../wiki/TazUO.Grid-Containers) - Easily find and move items with our fully customizable grid containers
- [Custom built-in scripting](../../wiki/TazUO.Legion-Scripting) - Built-in powerful scripting languages. **Python** and Legion Script.
- **Assistant features built-in** - Auto buy, sell, auto loot and more
- [Journal](../../wiki/TazUO.Journal) - Vastly improved journal for readability and organization
- [Alternative paperdoll](../../wiki/TazUO.Alternate-Paperdoll) - A new flavor of your paperdoll
- [Improved buff bar](../../wiki/TazUO.Buff-Bars)
- [Client commands](../../wiki/TazUO.Commands) - Several commands have been added for various features
- [Controller support](../../wiki/TazUO.Controller-Support) - That's right, play with your controller!
- [Cooldown bars](../../wiki/TazUO.Cooldown-bars) - Customizable cooldown bars
- [Grid Highlighting](../../wiki/TazUO.Grid-highlighting-based-on-item-properties) - Grid highlighting of items that have specific properties, easier looting!
- [Tooltip overrides](../../wiki/TazUO.Tooltip-Override) - Customize and override any text in tooltips!
- [Custom fonts](../../wiki/TazUO.TTF-Fonts) - BYOF, Bring your own fonts for better readability

There are ***many*** more features to check out in the [wiki](../../wiki) or in game, this list is just a sample!


# Screenshots
![Cooldown](https://user-images.githubusercontent.com/3859393/227056224-ef1c6958-fff5-4698-a21a-c63c5814877c.gif)  
![SlottedInv](https://user-images.githubusercontent.com/3859393/226514464-32919a68-ebad-4ec0-8bcf-8614a5055f7d.gif)  
![Grid Previe](https://user-images.githubusercontent.com/3859393/222873187-c88ad321-8b19-4cfd-9617-7e23b2443b6a.gif)  
![image](https://user-images.githubusercontent.com/3859393/222975241-319e5fa6-2c1e-441d-97e6-b04a5e1f6f3b.png)  
![Journal](https://user-images.githubusercontent.com/3859393/222942915-e31d26aa-e9a7-41df-9c99-570bcc00d1fb.gif)  
![image](https://user-images.githubusercontent.com/3859393/225168130-5ce83950-853d-43ce-9583-65ec4b0ae9d6.png)  
![image](https://user-images.githubusercontent.com/3859393/225307385-c8e8014f-9b84-4fe4-a2cd-f33fbeee9563.png)  
![image](https://user-images.githubusercontent.com/3859393/226114408-28c6556d-6ba8-43c7-bf1a-079342aaeacd.png)  
![image](https://user-images.githubusercontent.com/3859393/226114417-e68b1653-f719-49b3-b799-0beb07e0a211.png)  
