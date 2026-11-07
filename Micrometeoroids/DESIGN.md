Our replacement has two phases per incoming fragment.

**1. Detection**

Only installed, operational, switched-on sensors contribute:

| Sensor | Detection contribution |
|---|---:|
| Radar | **30%**, all directions |
| Lidar | **45%**, all directions |
| Smartlink Charybdis PDC | **40.0%**, its quadrant |
| BNW Zhong Kui PDC | **54.8%**, its quadrant |
| Soyuzneft PDC | **32.7%**, its quadrant |

PDC contributions derive from this gameplay heuristic:

```text
p = 0.40 × √((projectileSpeed / 30000) × (range / 12000))
```

Combine all applicable sensors through their failure probabilities:

```text
P(detection) = 1 − ∏(1 − sensorContribution)
```

This gives diminishing but useful returns from additional sensors. PDC sensors still contribute while reloading or out of ammunition. Successful detection is shared with the defensive guns.

**2. Interception**

Each ready PDC covering the approach gets **one burst opportunity**. Native firing handles ammunition consumption, reloads, jams, and firing effects. **A burst fires before its interception roll, so misses still expend ammunition.**

```text
P(intercept per burst) =
    1 − 0.15 ^ ((actualRoundsFired / 18) × (projectileSpeed / 30000))
```

At 18 rounds per burst:

| PDC | Interception if it fires |
|---|---:|
| Smartlink | **85.0%** |
| BNW | **94.2%** |
| Soyuzneft | **71.8%** |

Additional covering guns provide independent attempts. Reload duration affects readiness, not accuracy. Detection uses PDC quadrants; firing uses the guns’ actual **85° arcs and range**.

Undetected fragments, or fragments that survive all interception attempts, enter the native damage routine. **The unconditional 50% bypass is removed.**

For reference, **2 BNWs + 2 radars + 2 lidars** provide approximately **96.64% overall protection**, assuming coverage, readiness and 18-round bursts.
