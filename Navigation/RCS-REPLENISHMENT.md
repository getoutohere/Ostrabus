# RCS replenishment and stored flight budgets

Native `NavData.TimeAdvance` reconciles remaining RCS gas against absolute fuel
levels stored in its waypoints. `Ship.AIRefuel` and `Ship.RefuelRCS` change the gas
without translating those levels. The next plan update can consequently consume
the new gas as though it were an unpaid flight debit. This is shared accounting,
not a VORB-specific navigation policy or an asset-defined fuel requirement.

`RcsReplenishment` observes both setters. It adds the **actual accepted mass** to
every finite RCS waypoint budget in the same ship's still-current plan. Differences
between budgets remain unchanged, so planned expenditure still applies. Refilling
can make a previously negative arrival budget affordable; budget translation runs
before the existing VORB validator next evaluates that route. A replaced plan,
invalid point or overflowing sum prevents route writes. No-op fills do nothing.

For loaded physical tanks, the same addition goes into `fShallowRCSRemass` while
preserving any pending debit. Native `SyncFuel` can then subtract the prior
expenditure without erasing the refill or forgiving spending. Full `AIRefuel`
only sets a shallow counter; it is not treated as physical gas delivery.

Scope is initialized AI ships, independent of pilot role, faction and home.
Player-owned ships, the controlled ship and player-manned loaded navigation are
excluded. There is no automatic fuel grant, route replacement, target selection,
reserve policy, new saved field, periodic scan or retained ship reference.
The no-plan shallow observer allocates zero bytes after warmup in the native test.
Existing VORB diagnostic state can count additions without creating state or
evaluating navigation during the accounting commit.

The explicit [refuel API](../Inspection/REFUEL.md) retains its pre-call snapshot
contract: it writes original budgets plus the actual final addition, avoiding
double translation when these observers also run. Its loaded reconciliation also
uses a pre-call snapshot to retain the prior debit; assigning the physical total
would forgive that spending. The corresponding regression failed before repair.

## Related simplifications

`RetargetFuelPatch` now owns the single `TargetData.ApplyToShip` observation.
One semantic before/after target comparison clears the shared manual prediction
and invokes the existing VORB-only route cleanup. Equivalent targets preserve
state; the cleanup's scope has not been widened.

The VORB FlyTo prefix no longer restores empty-hauler home targets. General
deployer release/empty-haul/idle recovery already reaches native homeward FlyTo,
including a saved targetless Main[3] after its failure/idle retry. VORB anchoring,
invalid-route containment and manual fallback remain in their existing hooks.

## Reproduction and regression evidence

The preceding [regional audit](../Notes/AI-REGIONAL-SCOPE-AUDIT.md) records the
pre-repair witness: 100 kg + 20 kg refill - 25 kg travel left 75 kg outside VORB.
The same seven prescribed role/home cases now retain 95 kg. This is a reproduced
mechanism; fixture coincidence does not establish any particular ship's history.

Build Debug first, then `Tests/ScavFuelDiagnosis` in Release. Run its DLL through
`Tests/SaveJsonCompatibility/Host` using the game's bundled Mono:

- `scope`: 70 assertions, cross-role/home translation, unchanged retarget scope,
  refill-before-validation ordering and ordinary deployer Main recovery.
- `replenish-native`: three checks reproduce refill erasure with native AIRefuel
  and TimeAdvance, before any production patches are installed.
- `replenish`: 21 checks of actual/partial/capped/no-op/repeated refills; native plan advance
  and SyncFuel; pending loaded debit; foreign/replaced/invalid plans; player
  exclusions; no-plan allocation check. Physical tank I/O is an explicit fixture.
- `refuel`: 31 explicit API checks with production refill observers and raw HTTP
  query parsing, including the corrected targetKg whitelist.
- `fuel-patched` and `recovery`: mixed flight accounting and native SOS regressions.

The lifecycle and priority suites separately exercise target replacement and
native higher-priority behavior. Graph scenarios are reference consistency checks,
not extra native or live tests. This repair has offline validation; loaded-scene
Unity tank behavior has not been newly playtested.
