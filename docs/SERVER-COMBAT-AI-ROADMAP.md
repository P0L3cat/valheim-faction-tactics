# Server-side combat AI roadmap (post 0.2.3 live pause)

**Date:** 2026-09-19 CT  
**Live status:** GPortal FT DLL disabled (`FactionTactics.dll.disabled-rollback`); sticky + `EnableEnemyServerOwnership` false. Clean bounce 2026-09-19 ~17:20 CT — FT not loading.

## Proven on dedicated (keep)

| Layer | Evidence |
|-------|----------|
| Enemy ZDO claim + CreateObject | `enemyMai>0`, `updateAIHits` climb |
| Discovery → squads | `roman:8` / `ambush:8`, `discovered=1 active=1` |
| Sole-brain Prefix | `orders=[Hold…]`, `baseAIUpdateHits≈0` while `updateAIHits` rises |
| Sticky ReleaseNearby | `enemyServerOwned>0`, `enemyClientOwned=0`, reclaim logs |
| Form-up before Hold (0.2.2) | Code committed; not fully proven under sticky |

## Failed live (0.2.3 sticky)

Nate: mobs **immobile**, **don’t see player**, **can’t be attacked**. Sticky flipped authority correctly but thin ownership ≠ full SSS simulation.

Vanilla constraints:

- `BaseAI.UpdateAI` / `Character.CustomFixedUpdate` gate on `ZNetView.IsOwner()` — client must not own FT enemies.
- Server must own **and** produce visible motion + damageable Characters (client hits via RPC to owner).
- `MonsterAI.UpdateTarget` uses `Player.IsPlayerInRange` / `FindEnemy` — must work with peer players (`Player.m_localPlayer` is null on dedicated).

## 0.3.x goals (server-only, no client FT)

1. **Sense** — peer-player targeting on dedicated; heartbeat `hasTarget` / hear / see.
2. **Move** — form-up MoveTo then Hold; prove ZDO/transform sync so clients see motion.
3. **Fight** — `DoAttack` with live target; prove client→server damage on server-owned Characters.
4. **Sticky** — keep enemy-only ReleaseNearby; default **false** until sense+move+hit smoke green.
5. **Config** — sticky off by default; ownership PoC for capture tests only.

## Non-goals

- Client-side Faction Tactics DLL (Nate lock).
- Full SSS on GPortal unless ordered.
- Raising spawn/raid rates.
