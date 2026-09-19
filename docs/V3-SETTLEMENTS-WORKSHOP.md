# v3 Settlements — workshop notes (2026-09-18)

**Status:** workshop only. After v1 enemy AI + v2 territory.

## Inspo
- **Conan Exiles thralls** + work/walk cycles
- **Manor Lords** hamlet vibe (not full city-builder)

## Core loop
1. Recruit / assign **peasants** (and **guards**) inside claimed fiefs.
2. Assign a **job** at an existing Valheim station or plot: smelter, spinning wheel, farm, etc.
3. Worker runs a light **day cycle** (bed → job → output chest → bed).
4. **Payoff:** generated products (foods, ammunition, basic mats) into clan storage — reward for building and defending a hamlet.

## Biome tiers
Workplace biome (claimed land the worker is based in) gates **reward tier** / recipe unlocks. Higher / harder biomes → better outputs.

## Fake supply chains (ephemeral)
No wagon or pathfinding logistics.
- Multi-biome products (e.g. **jam**) unlock when the **clan** has sufficient **NPC counts in claimed areas of the required biomes**.
- Network is a soft check on census + claims; lose the fief or the workers → lose the link.
- Output still appears at a designated hall / chest, not via simulated hauling.

## Jobs (examples)
| Job | Station / hook | Notes |
|-----|----------------|--------|
| Smelter | Smelter | Bars / fuel consume rules TBD |
| Spinner | Spinning wheel | Cloth / thread |
| Farmer | Cultivate / farm plots | Food staples |
| Guard | Wall / gate | FT-facing; man curtain on Assault |
| (later) | Forge, fermenter, etc. | Expand carefully |

## Non-goals (v3.0)
- Full city-builder / zoning / tax UI
- Real wagon caravans
- Replacing player crafting entirely

## Depends on
- **v1** doctrines so the wall can burn
- **v2** enclose-to-claim + clans so capacity and supply checks have a place to live

## Recruit source (workshop lock 2026-09-18)
- **Ritual item** at claimed hall (“pray to Odin for more souls” — final name TBD).
- Spend a **boss trophy / keyed boss drop 1:1** → spawn one **thrall** of that boss’s **tier**.
- Tier → **look** (biome/boss flavor) + **production rate** (higher bosses = faster labor; guards may share the ladder).
- Server consumes the offering and spawns the thrall ZDO; claim/bed **capacity** still caps headcount.
- Still no free world villager rain; no client-authoritative spawn.

## Villager persistence (workshop lock 2026-09-18)
- Recruited converted mobs are **villagers** (not “thralls” in player-facing copy).
- They are **expensive** → **cannot permanently die** in normal combat.
- On would-be lethal damage: server **cancels death**; villager **flees** (leaves the fight / despawns from the scene); **production pauses**.
- They **return** to linked **bed / hall** after **~1+ in-game days** (configurable).
- Implementation sketch: ZDO tag `ft_villager`; Harmony death/Damage prefix → flee state + world-time respawn schedule.
- Permanent remove only via **dismiss**, **claim abandoned**, or **admin**.
