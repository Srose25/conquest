# Experience and upgrade scaling

XP is implemented in `scripts/ProgressionManager.cs`. The XP pickup and denomination composition live in `scripts/XpDropController.cs`, with the pickup scene in `scenes/resources/XpDrop.tscn`. Repair pellets remain separate in `ResourceDropController.cs` and `ResourceDrop.tscn`.

## Level thresholds

The XP meter resets after each level-up. These are the XP required to reach the listed level:

| Level reached | XP required | Automatic upgrade |
|---:|---:|---|
| 1 | 50 | +2 max/current HP, +2 max/current shield, +0.25 bullet damage |
| 2 | 100 | +2 max/current HP, +2 max/current shield, +0.25 bullet damage |
| 3 | 150 | +2 max/current HP, +2 max/current shield, +0.25 bullet damage |
| 4 | 200 | +2 max/current HP, +2 max/current shield, +0.25 bullet damage |
| 5 | 300 | +2 max/current HP, +2 max/current shield, +0.25 bullet damage |
| 6 | 600 | +2 max/current HP, +2 max/current shield, +0.25 bullet damage |
| 7 | 1200 | +2 max/current HP, +2 max/current shield, +0.25 bullet damage |
| 8 | 2000 | +2 max/current HP, +2 max/current shield, +0.25 bullet damage |
| 9 | 4000 | +2 max/current HP, +2 max/current shield, +0.25 bullet damage |
| 10 | 8000 | +2 max/current HP, +2 max/current shield, +0.25 bullet damage |
| 11+ | 16000, then doubles per tier | Same upgrade increments |

## Drop rules

- Small asteroids (`PassiveEntity`, `meteor1.png`) award a random 1–3 XP.
- Medium asteroids (`ActiveEnemy`, `meteor2.png`) award 5 XP.
- Enemy ships award a random 9–15 XP.
- Enemy ships begin spawning only after the player reaches level 1; the starting level remains ship-free.
- Ordinary awards split randomly into 9, 5, 2, and 1 XP sprites (for example, 15 XP may be `5+5+5` or `9+5+1`). The 15 XP sprite is reserved for future boss-sized rewards.
