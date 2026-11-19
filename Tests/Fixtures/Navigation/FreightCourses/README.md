# Saved freight physics cases

These stripped inputs support the [native voyage regressions](../../../../Navigation/FREIGHT-CELESTIAL-SAFETY.md).
They retain body elements/hierarchy, saved station motion/classification/ports,
carrier propulsion caches, fuel and course points. They contain no people,
inventories, save archives or personal filesystem paths.

| Input | Carriers | Purpose |
| --- | --- | --- |
| `pathcheck.json` | O-2P3 | Mars ground-port impact; departure prevention and late unrecoverable-state control |
| `luna.json` | O-J3FE0 | Luna departure intersecting its parent body |
| `update.json` | O-KKWJ, O-N2V | Venus impact and clear saved-point comparison |
| `chaos.json` | O-ZNU, O-AL0 | Venus departure impact and Ganymede ground-port approach |
| `roll.json` | All twenty current carriers | Current saved-state recovery and safe-flight controls |

The tool's `root --all-stations` and `fixture` commands extract these fields.
The voyage runner uses historical course origins for preventive replay and actual
snapshot motion for the current fleet. It does not claim it can undo an impact
whose stopping distance already exceeds the remaining clearance.
