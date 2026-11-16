# Preserve AI destinations during SFF route completion

## Defect and evidence

Native `Ostranauts.ShipGUIs.Utilities.NavData.TimeAdvance(ShipSitu)` completes a
route during SFF by assigning a new `ShipSitu` to `_shipUs.shipSituTarget`, copying
the endpoint's kinematics into it, advancing it past arrival, then copying that
position/velocity to the vessel. The temporary point has no body lock or body
reference. Using the mission-target field as scratch space therefore discards
the pilot's actual destination. `ShipSitu.UpdateTime` cannot advance that point
with its original body afterward.

E-KRQD / Trying It On For Size was observed about 117,000 km from HQCH, moving
away at roughly 4 km/s, using RCS toward a fixed point another 2.36 million km
away. That target stayed identical over multiple observations and earlier saves;
it had the native temporary-point signature: no body reference/lock, zero body
offset and zero size. Its Navy controller and living captain remained aboard.

The native assembly reproduces the destructive assignment without Ostrabus.
Ordinary completion preserves the target; SFF completion replaces it. JSON
reconstruction preserves the resulting bad point, rather than causing the loss.
The precise historical SFF that damaged E-KRQD was not recorded.

Native `NavyPilot.GetTarget` normally locks generated patrol points to a body.
The station JSON defines HQCH as a Mercury station; the `StationInfluenceRange`
condition describes the authored spawning/patrolling radius. There is no asset
policy requiring an inertially frozen destination. This is a C# playback defect.

## Repair

`SffNavigationDestinationPatch` changes the completion block to use a local
temporary placement point. Autonomous non-player AI retains its mission target;
native endpoint extrapolation, hull placement, fuel reconciliation, return value
and route clearing remain in place. Player-owned/current-player and manually
piloted ships retain the native target replacement. Ordinary playback and
unfinished routes never execute the helper.

The preserved target must also be refreshed to the current epoch. Native
`Dock.ResolveInstantly` omits the coordinate update performed by ordinary
`Dock.RunCommand`. Omitting it from our helper caused civilian ships to moor at
a stale asteroid position and retain a roughly 135,000 km group offset after a
two-hour skip. The helper now invokes native `UpdateTime` on the preserved
target before native instant placement. See the [Ceres reproduction and related
pilot-refusal defect](../Navigation/CERES-SFF-DISPLACEMENT.md).

The PASS integration audit also demonstrated that no GUI completion need precede
an instant mooring. The same patch class now refreshes the argument at
`Dock.ResolveInstantly`'s single `PlaceOrbitPosition` call, mirroring ordinary
`Dock.RunCommand` without changing eligibility or placement. This covers native
non-GUI shortcuts and stale targets after a later coast window. Both GUI flag
values and mooring without preceding playback have native regression controls.

The transpiler checks exactly one target write and four target reads, rejecting
an unfamiliar assembly layout. It adds no per-frame prefix, recorder, saved key
or retained object collection. Its second native target validates exactly one
instant-moor placement call. Custom Builder exposes this as the independent
`sff-navigation-destination` bugfix; it does not require the orbital simulation
improvements.

The existing behavior graphs treated this playback detail as an unexpanded
flight boundary; their earlier scenarios did not establish destination integrity
at SFF completion. The dedicated native test now covers that boundary directly.

## Recover existing ships

The prevention patch deliberately does not guess how to reconstruct previously
damaged anonymous patrol points. The inspection API offers an explicit
[velocity recovery](../Inspection/VELOCITY-RECOVERY.md) without teleporting or
retiring a ship. Recovery now places an explicit native Hold; resuming AI requires
a separate release with a feasible native plan.

## Validation

See [the shipped-assembly tests](../Tests/AiRouteCompletionCheck/README.md).
They reproduce native target loss before applying the production transpiler,
then verify its scope, unchanged final placement, target JSON reconstruction,
unfinished/ordinary playback, and recovery refusal/preservation cases. Live
recovery of E-KRQD initially exposed an unsafe recovery-API continuation: failed
retargeting led to native disposal. That API defect and its correction are
recorded in the recovery document. The November 14 mooring regression adds real
locked-body ephemeris and native instant-placement checks; the earlier harness
substituted ephemeris updates and did not exercise that handoff.
