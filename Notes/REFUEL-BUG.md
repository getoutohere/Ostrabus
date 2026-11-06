# Shallow-ship RefuelRCS defect

## Finding and verification boundary

Confirmed by static inspection of both the `osra-1.0.1.9` source snapshot and the
installed `Ostranauts_Data/Managed/Assembly-CSharp.dll`. The installed method's IL
reads/writes the same incorrect field; this is not inferred solely from names.
No live Unity/in-game reproduction was performed during this audit.

Installed DLL SHA-256:
`7E0024E75BB0DFEF7275A98CB295CBC7C612980F90418527E5B980EF956C112A`.
Assembly metadata version is `4.2.0.0`; that is not a verified game release number.

## Evidence for the fields' actual roles

All source references are relative to `<decompiled-source>`.

| Code | Observed behavior |
| --- | --- |
| `Ship.cs:7769`, `GetRCSRemain()` | For `LoadState <= Loaded.Shallow`, returns `fShallowRCSRemass`. |
| `Ship.cs:7377`, `RemoveGasMass()` | In the shallow branch, subtracts consumed/transferred gas from `fShallowRCSRemass`. |
| `Ship.cs:7872`, `AIRefuel()` | Writes `GetRCSMax()` into `fShallowRCSRemass`. |
| `Ship.cs:801`, `Mass` | In the shallow branch, returns `fShallowMass` plus market cargo mass. |
| `Ship.cs:8611` | Saves `Mass`, remaining RCS fuel, and maximum RCS fuel into three separate fields. Loading restores those fields separately at lines 1598–1600. |
| `Ostranauts.Debugging.Testcases/TestFuelRequest.cs:50–57` | The game's player-refueling test sets `fShallowRCSRemass` to zero, then requires it to become positive to pass. The test was read, not run. |

Together these establish that fuel and ship mass are distinct operational state.
The installed IL was also inspected for the getters, consumption, and AI refuel.

## Defective branch

`Ship.cs:7877`, `RefuelRCS(float amount)`, for shallow ships:

```csharp
double rCSMax = GetRCSMax();
if (fShallowMass + amount < rCSMax) {
    fShallowMass += amount;
    return 0f;
}
fShallowMass = rCSMax;
return (float)(fShallowMass + amount - rCSMax);
```

1. It compares and updates ship mass instead of remaining fuel. The fuel getter
   therefore cannot observe any increase from this branch.
2. The overflow return uses the field **after** replacing it with maximum
   capacity. That expression consequently returns the entire input amount.
   Fixing only the field name would leave this second error.

Installed IL: `IL_0011`/`IL_001E` load `fShallowMass`; `IL_0026` and `IL_0033`
store it. `IL_0039` reloads the overwritten field before calculating the return.
The shallow branch is `IL_0000` through `IL_0044`.

For example, given mass 10,000 kg, remaining fuel 0 kg, capacity 1,000 kg, and an
input of 30 kg, the branch sets ship mass to 1,000 kg, leaves fuel at zero, and
returns 30 kg. This is a calculation from the verified code, not an in-game log.

## Consequence for normal NPC refueling

`Ostranauts.Ships.Commands/TakeFuel.cs:47–62` reads recipient and donor fuel,
requests up to 30 kg per cycle, calls recipient `RefuelRCS(a)`, and subtracts
`a - returnedAmount` from the donor. It returns Running and retries until the
recipient reaches its required fuel or the donor is empty.

With the example above, neither side's fuel changes, so subsequent cycles cannot
make progress while those conditions remain unchanged. With mass initially below
capacity, the other branch can remove donor fuel without increasing recipient
fuel. `PirateShakeDown.StealFuel()` uses the same return-value contract.

For normal, nonnegative refueling input, the shallow branch should instead add
at most `maxFuel - currentFuel` to **fuel**, and return the unaccepted input,
without modifying ship mass. Full/editor-loaded ships use a separate canister
branch; this finding does not establish that branch's correctness.

## Relation to stranded ships and requests for help

The request mechanism still exists in this source. `SosOutOfFuel.RequestHelp()`
can ask the player via `SHIPSosAskPlayer` for a nearby scav when the player is not
docked with a station. Its range is 40 km, or 120 km for a close relationship
(also 120 km when captain/profile is unavailable). Several AI types take a
cleanup/unregister path instead. Another branch sends `SHIPSosStranded` to the
nearest regional station (or last ATC) and unregisters the ship; when already
very close to its target it can return without sending that request.

`TargetNewIfNone.RequestHelp()` has related routing. Message definitions and the
complete station/hauler dispatch chain have not been audited here.

`Hold.RunCommand()` can remain Running when docking time expires if the captain
has `TIsHasRequestedHelp`, or fuel is below 1 kg with no lift-rotor thrust. Thus
waiting with no fuel can be intentional behavior, but a broken transfer can
prevent recovery. This defect explains failed shallow-ship fuel transfers; it
does **not** by itself prove why particular NPCs ran dry, stopped requesting help,
or remained docked in the current save. Those need live ship/AI state inspection.

Suggested in-game reproduction: use a shallow recipient with known nonzero
capacity, zero fuel, and a fueled donor; start a transfer and record load state,
both fuel values, recipient mass, requested amount, and RefuelRCS return value
over several cycles. Compare with a fully loaded recipient.

## Mod fix

`RefuelRCSPatch.cs` replaces only the shallow branch with a Harmony prefix. It
adds fuel up to available capacity and returns the unaccepted amount. It leaves
mass untouched and lets the original canister branch run for Edit/Full ships.
The `refuelai` command now uses this patched method instead of its local workaround.

Validation: project builds without warnings/errors. A standalone harness linking
the actual prefix source passed 2,052 assertions with ship/Harmony doubles,
covering empty/full/overflow/zero-capacity cases, donor/recipient accounting, and
Edit/Full passthrough. This validates arithmetic and branch decisions, not Unity
integration or live AI behavior. Previously corrupted mass, AI registration,
help-request state, and existing stalled commands are not repaired by this patch.
