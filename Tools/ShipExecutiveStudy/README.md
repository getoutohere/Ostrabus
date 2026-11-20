# Ship mission executive feasibility study

Status: **abandoned on 2026-11-19**. See the
[postmortem](../../Navigation/Architecture/POSTMORTEM.md). Tools and instructions
remain for historical reproduction; runtime adoption is closed.

Offline tools for the [architecture evaluation](../../Navigation/Architecture/README.md).
`Scan` and `Native` characterize the original mechanisms. `Prototype` tests both
the original tree-scheduling experiment and the explicit replacement lifecycle.
`Contracts` is the game-independent reducer for the separate
[normative mission model](../../Navigation/Architecture/Replacement/README.md).
None are deployable patches or contact the running game or edit game/mod sources
or assets.

The current replacement suite binds game **1.1.0.0** and passes **529 assertions
across 101 scenarios**. Its [upgrade qualification](../../Navigation/Architecture/Replacement/UPGRADE-1.1.0.0.md)
separates native mechanisms from fixture boundaries; the earlier 1.0 receipts
remain preserved. The runner refuses an unexpected game assembly hash.

Run the prototype from the repository root (requires a current full Debug plugin):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ShipExecutiveStudy/Prototype/Run.ps1
```

The runner builds only the prototype and existing offline Mono host. See
[prototype scope and results](../../Navigation/Architecture/PROTOTYPE.md) before
interpreting a pass. Detailed output is in ignored `obj/ShipExecutivePrototype`.
The project also builds the Contracts library; its conformance report and
generated model diagram go to ignored `obj/ShipExecutiveModel`.
The Navy pass also writes a direct authored-command inventory there, including
generated surrender-stakes interactions. Its native entrypoint tests execute
the world update and queue under explicit scene/economy fixtures; see
[qualification](../../Navigation/Architecture/Replacement/NAVY-LIFECYCLE.md).
Integrated world cases install the real existing Orbital/SFF/Economy patches
offline with synthetic world/market data. See their
[ownership, accounting and shared time coordination](../../Navigation/Architecture/Replacement/WORLD-SERVICES.md).
The [command-effect pass](../../Navigation/Architecture/Replacement/COMMAND-EFFECTS.md)
compares native fuel transfers across fresh reloads and executes selected shipped
surrender/boarding rules, including native received and in-flight message DTOs.
Physical tanks, personality inputs, scene fanout and crew effects have explicit
qualification limits; these tests do not install a replacement executive.
The [native save/arrival pass](../../Navigation/Architecture/Replacement/SAVE-ARRIVAL.md)
also executes the real collector, threaded/synchronous archive writer and native
wire parser, with mixed crew disposition and ordinary patrol handback tests.
Its ZIP fixtures stay under ignored `obj/ShipExecutiveNativeSaves`.

From the repository root:

```powershell
dotnet run --project Tools/ShipExecutiveStudy/Scan -c Release
dotnet build Tools/ShipExecutiveStudy/Native -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tools/ShipExecutiveStudy/Native/bin/Release/netstandard2.1/Native.dll
```

The scanner reads the shipped assembly with Mono.Cecil, the existing Debug
plugin and selected shipped JSON. It does not load/execute the plugin or invoke
patch factories. Outputs go to ignored `obj/ShipExecutiveStudy`.

The native probe runs shipped command combinators, state persistence, Navy tree
construction and a Navy FFWD continuation under the bundled Mono runtime. It
does not reference Ostrabus. Unity identity, captain, trigger lookup and faction
lookup have explicit offline fixtures. Controlled command leaves characterize
scheduling semantics; this does not simulate an actual battle or flight.

The current snapshot records 13 concrete pilots, 70 concrete command types,
338 selected direct call sites and 395 selected direct field-write sites. These
are exploratory metadata counts, not required hook counts or a completeness
proof. Vector component writes, reflection, delegates, transitive effects and
reachability require separate interpretation. The relevant patch subset is a
namespace/name filter; neither its size nor attribute counts imply removability.

The native probes contain eight observations with 16 assertions. Assertions
characterize the original mechanisms, including behaviors already corrected by
existing production patches. They must not be reported as 16 new defects.
