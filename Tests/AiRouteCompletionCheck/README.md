# Native SFF destination and velocity-recovery checks

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

On game 1.0.1.10: **346 checks passed**. The complete plugin also bound all 236
Harmony patch classes without failures or warnings. The isolated Custom Builder
package bound this one patch with zero frame callbacks; its catalog self-test
passed 482 checks. The inspection-only package bound nine patch classes, including
the recovery Hold SFF guard. The existing torch SFF clock regression passed 256 checks.
