# Explicit AI RCS refueling

`GET /ships/{registryId}/refuel` previews the refill. `POST` applies it. Omit
`targetKg` to request full native RCS capacity, or supply a finite nonnegative
desired **total** fuel mass. Values above capacity are clamped; values below the
current contents never remove fuel. This is RCS gas, not fusion reactants.

```powershell
Invoke-RestMethod 'http://127.0.0.1:18764/ships/O-KA2/refuel'
Invoke-RestMethod -Method Post 'http://127.0.0.1:18764/ships/O-KA2/refuel?targetKg=500'
```

The result reports before/after/capacity/target kilograms, planned addition, actual
addition and whether an existing route was rebased. Native physical filling can
accept less than nominal capacity. Shallow full refills use AIRefuel; partial
refills use the existing corrected RefuelRCS. Materialized refills use native
RefuelRCS and translate the shallow reconciliation budget afterward, preserving
any prior debit still awaiting native SyncFuel. Invalid loaded budgets are refused.

Stored RCS waypoint fuel levels are shifted by the actual addition, preserving
their differences and preventing absolute route reconciliation from erasing the
new fuel. Values are based on the pre-refill snapshot so shared refill observers
cannot shift the route twice. The endpoint preserves targets, orders, crew,
connections and controller state. It doesn't remove ShipRequestedHelp; a native
Hold that waits on that condition can continue waiting despite restored fuel.

The normal listener checks, bounded request queue and generation/timeout checks
apply. Game access occurs on the Unity thread. Preview doesn't call the shallow
capacity getter that would invent an unknown capacity. Player-owned hulls,
non-AI ships, missing/uninitialized ships and invalid fuel/route data are refused.
No periodic scan or background recovery is installed. Ordinary native refills
also retain route budgets through [shared accounting](../Navigation/RCS-REPLENISHMENT.md).

Offline validation: `Tests/ScavFuelDiagnosis` with argument `refuel` runs 31 checks
covering preview, capacity limits, partial/idempotent filling, ownership/input
validation, unchanged structural mass, native shallow filling and native route
consumption after rebasing. An extra refuel callback simulates an existing route
rebase to check duplication. Raw HTTP parsing now accepts `targetKg` before
the endpoint validates its numeric value; the previous transport whitelist
omission was reproduced and repaired.
A loaded pending-debit regression reproduces the
former accidental forgiveness and checks native SyncFuel after the repair.
Materialized filling/delegation uses explicit tank
boundary stand-ins; these checks don't claim a full loaded-scene tank simulation
or run the live HTTP listener.
