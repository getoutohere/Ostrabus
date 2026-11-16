# Native SFF destination and velocity-recovery checks

The shared [playback completion repair](../../Navigation/ROUTE-COMPLETION.md)
adds 295 checks for actual native route playback and physics. The PASS instant-
mooring closure brings the total to 689: both GUI flag values, 30-second through
six-hour gaps, and native placement without any preceding route completion.
Its dedicated phase enables real physical integration and body ephemerides;
the earlier destination/velocity-recovery fixtures below keep their original
boundaries. It reproduces the 89.7587 m/s defect before installing the patch and
checks ordinary/PASS-sized steps, remaining coast, GUI preservation, ambient
forces, body-relative endpoints, physical/borrowed torch controls, rejection,
manual thrust, exceptions, nested handlers and changed IL refusal.

Run from the repository root:

```powershell
dotnet build Ostrabus.csproj -c Debug
dotnet build Tests/AiRouteCompletionCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiRouteCompletionCheck/bin/Release/netstandard2.1/AiRouteCompletionCheck.dll
```

The harness executes the shipped `NavData.TimeAdvance` before and after applying
the production transpiler. Ordinary/SFF, completed/unfinished, body/free endpoint,
autonomous/non-AI/player-owned/current-player cases are compared. Native final
placement, route clearing and `ShipSitu` JSON reconstruction are exercised.

Unity object equality, ephemeris advancement, physical integration, station
classification, ownership lookup and maneuver stopping are boundary stand-ins.
Playback destination writes/copies and the native command exits are not replaced.
This proves the destination-overwrite mechanism and repair, rather than full
orbital dynamics, fuel consumption or the exact historical incident chronology.

The November 14 additions run actual locked-body ephemeris updates and native
`Dock.ResolveInstantly`/`PlaceOrbitPosition`. They reproduce skip-sized stale
placement, then verify target refresh and mooring separation at one minute,
two hours and six hours. Final asteroid materialization is a boundary stand-in;
the position calculation is native. Earlier tests replaced ephemeris updates
and therefore missed the preserved-target coordinate handoff.

Native no-pilot flight is also tested before/after the production transpiler:
false success with residual RCS becomes a pending flight with native maneuver
stop. Momentum, cached status reuse, active-course bypass and player controls
are checked. Pilot availability is prescribed at its native getter boundary;
these tests do not simulate biology or hardware failure. See the
[investigation](../../Navigation/CERES-SFF-DISPLACEMENT.md).

The real API handler runs against fixture controllers, native selector/sticky
sequences and a simulated registry. It checks read-only preview, ship/body
references, unchanged position/fuel/crew/controller, cancelled navigation and
saved target fields, native Main reset, and refusal of unsafe/ambiguous requests.
Separate hauler fixtures retain destination and task stage; they do not simulate
an entire cargo delivery. No live game data is modified by these tests.

Continuation tests first reproduce native failed selection, unregistration and
the asynchronous stale-cleanup decision to remove a bounty captain's hull.
They then execute the corrected native priority Hold through 100 hourly AI
updates, 20 SFF dispatches, native/compatible JSON restoration and repeated
cleanup. Eight supported roles are checked at SFF boundaries. Failed release
planning retains the Hold; accepted native candidate data is applied before
release. Final Unity object destruction is outside this harness; the native
cleanup decision scheduling it is exercised.

Transport checks include the supported query allowlist and malformed/duplicate
parameters. Real loopback TCP GET/POST requests also pass through `Serve`, the
request queue, `Pump`, the recovery handler and JSON response writer. Recorder
ticks are replaced at their scene boundary; request handling is unchanged.

The earlier destination/recovery-only run on game 1.0.1.10 passed **366 checks**;
the extended suite above passes **661**. That earlier complete plugin bound all 236
Harmony patch classes without failures or warnings. The isolated Custom Builder
package bound this one patch with zero frame callbacks; its catalog self-test
passed 482 checks. The inspection-only package bound nine patch classes, including
the recovery Hold SFF guard. The existing torch SFF clock regression passed 256 checks.
