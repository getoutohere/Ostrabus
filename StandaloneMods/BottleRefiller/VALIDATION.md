# Bottle Refiller validation

Tested baseline: Ostranauts 1.1.0.4, standalone version 0.1.7. Release DLL builds with
zero warnings/errors; the native/math/audio/indicator fixture passes 34,873
assertions, the sprite fixture passes 3,101 assertions and the installer passes
40 checks. Scene rendering and audible output remain live-test boundaries.

Release-source cleanup removes rejected sprite drafts, comparison sheets and
obsolete generation notes. The retained masters and composition inputs reproduce
their selected exports byte-for-byte; all five shipped textures are unchanged.
The sprite fixture still passes 3,101 assertions. Development tools now write
temporary previews to ignored `obj/BottleRefillerArt/`, and regenerating them
does not recreate discarded files beside the retained art sources.

Version 0.1.7 removes game assembly hash/version restrictions from both startup
and installation. The existing Harmony target binding remains authoritative;
if binding fails, startup unpatches this plugin and logs the actual hook error.
The installer retains the game/BepInEx path checks, transactional native stock
composition and plugin copy verification. Native mod-list `strGameVersion`
records the tested baseline; its use in native `GUIModRow` is a display label
and age coloring, not an enablement gate.

The installer fixture appends a byte to a copied native DLL, verifies its hash
changed and that its managed assembly identity remains valid, then successfully
reinstalls with native load order preserved. The native fixture still binds and
executes the actual AddCommand prefix. This checks removal of the artificial
restriction; it does not claim validation of unknown future API/asset changes.

The user verified basic operation in 0.1.0 and identified missing active sound,
instant filling, absent loose packaging, wall-hidden output hardware and
incorrect pickup of an installed unit. The cases below now cover their causes;
the updated scene appearance and audible result still require playtesting.
In 0.1.2, live screenshots confirmed the indicator was incorrectly on the intake
side and the loose art did not match native packing. Version 0.1.3 moves the
unlit lamp recess and native green indicator onto the exposed bottle-side face.
The user rejected 0.1.3's loose art because it depicted the native pump instead
of the custom refiller. Version 0.1.4 uses the approved V12 artwork: the actual
native flat tan sleeve overlays the custom equipment, with its lower fold
shortened to fit. It retains 319 exposed equipment pixels unchanged and covers
103 with the sleeve. The pale lower section is equipment housing, not packing
foam. The user approved the exported artwork in the four-panel comparison;
its in-game appearance still needs verification.

Version 0.1.5 adds distinct damaged installed/loose textures and matching
portraits. Cracks, scorching and broken face details occur on both exposed
ends. The loose version applies the unchanged V12 sleeve to the damaged
equipment. Healthy artwork, gas handling and work behavior are unchanged.
The user approved the four-state exported comparison before packaging 0.1.5.

Version 0.1.6 adds amber ON/idle indication using the native `ItmLitYellow`
sprite at the same position as green. The two renderer states are mutually
exclusive; native placement, scaling and visibility are retained. Tests cover
amber idle, green filling, return to amber, OFF/power loss/damage/uninstall dark,
paused ON/OFF changes and avoiding repeated renderer writes for unchanged state.

The reported uninstall disappearance was investigated using save `debug` and
the live read-only API. The same loose-item ID remained alive on the player's
ship with `IsInContainer`; its saved parent and live parent resolved to an
underfloor rack. Native `CondOwner.ModeSwitchInternal` and `DropCO` can place
an uninstalled item in nearby storage. No deletion was observed and native
placement behavior is unchanged.

## Automated evidence

The native regression fixture runs in the game's bundled Mono, loading shipped
conditions, triggers, work templates and loot. Native `Installables.Create`
generates the new work actions and uses predeclared, suppressed conversion loot.

Gas tests invoke the production adapter and **real native `GasContainer.Run`**,
including native condition changes, mole bookkeeping, mixed temperature, total
and partial pressure and cached gas mass. Scene danger labels and face updates
are fixture boundaries; pressure-difference physics is deferred by the fixture.
The tests do not replace thermodynamics with a second implementation. The
headless Mono host prints unresolved Unity internal-call notices when Harmony
prepares scene wrapper methods; those wrappers are replaced by the documented
fixture boundaries. The notices are not emitted by the running plugin.

Coverage includes one to four bottles, insufficient supply, contaminated mixes,
hot/cold gases, existing contents, pending native gas deltas, repeated fills,
species and mass conservation, cap enforcement, OFF, loss of power, damage,
frozen objects, duplicate bottle IDs, shallow/same-ID/preview copies, compartment
identity outside the ship item registry, actual compartment extraction, void
exclusion, canister priority and ambiguous sources. Native `GUIOnOff.SetPower`
switches the saved flags; native inventory fitting admits four 1x1 bottles and
rejects a fifth. The production Harmony target actually binds and normalizes
a legacy grid, without changing another equipment definition. Native `GetPos`
executes all four rotated intake transforms with only Transform getters stubbed.
Seeded mathematical cases independently stress limits, mixture proportions,
invalid inputs, overfull bottles and proportional allocation.

Native time-series tests cover one/four initially empty bottles with 100 K,
293 K and 1000 K source gas. They verify 90% pressure at tick five, full at tick
thirty, monotonic tapered filling, conservation on every step and no further
gas debit when complete. Only the device's one-second service clock is retained;
the curve infers its position from pressure without per-bottle timers. Pauses,
same-epoch calls, rewinds and six-hour jumps are bounded by the service clock.
The offline native-gas average was approximately 0.026 ms per service check
(one to four bottles, no scene physics or audio rendering); this is not a full
Unity frame benchmark or an FPS guarantee.

Audio tests execute the component's start/stop and Update branches with native
audio sources and child creation as explicit engine boundaries. They verify two
native motor/hiss profiles, no repeated allocation/restart while active, cleanup
when idle/unloaded/destroyed, and immediate stops on OFF, lost power and pause.
The tests do not claim that sound was rendered by the headless host. Indicator
checks execute the actual filling/idle/unload/OFF/power-loss/damage/uninstall/pause branches with
the Unity renderer setter and scene lookup as explicit boundaries. Native
`Item.SetData` owns the authored light position and material; `ResetTransforms`
handles scale and rotation. `CondOwner` hides its GameObject for visibility,
while the refiller independently toggles the renderer for activity, so visibility
changes cannot turn an idle lamp on. Pausing silences audio but retains the last
simulated indicator state; switching OFF while paused still extinguishes it.
No render or light Harmony hook is added.
Amber uses a second native sprite renderer. Both are cached after lookup by
their native shared-material image names, and changed only on state transitions.

Handling checks execute native ground-inventory and drag-slot tests. Mounted
world instances are hidden from the movable grid and lose drag eligibility;
loose forms retain it. An old carried instance stays visible for the native
Repack interaction, while its carried-only trigger forbids bypassing real
uninstallation. Reconstruction metadata is retained before runtime normalization.
Sprite checks verify the actual 16x48 exports contain visible pixels beyond the
wall-tile boundary, select distinct tan loose packaging and have clean alpha.
Additional checks compare the production sprites with the approved V9 installed
and V12 loose files by SHA-256, preventing selection of a wrong historical image.
They also verify that the indicator sits in its unlit black recess in the
exposed bottle-side tile. The obsolete pale-cradle assertion has been removed:
it encoded a mistaken interpretation of the native artwork. Pixel checks do
not establish artistic fidelity; the exported V12 comparison was reviewed by
the user before this package was prepared.
Both damaged forms are also compared with their authored exports and checked
to differ from healthy artwork. Native asset checks verify all four item and
portrait texture IDs select their matching state; damaged forms have no light.

Commands from the repository root:

```powershell
dotnet build Tests/BottleRefillerCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/BottleRefillerCheck/bin/Release/netstandard2.1/BottleRefillerCheck.dll
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BottleRefillerCheck/Test-Installer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BottleRefillerCheck/Test-Art.ps1
```

Installer tests use a workspace fixture, not the installed game or real settings.
They cover custom mod paths, install/reinstall, other enabled/disabled shop mods,
commented stock JSON, inventory preservation on removal, failed-install rollback
and keeping the omnibus DLL untouched. The distribution contains one plugin DLL
and only this mod's native assets, with the established native installer helpers.

## Live acceptance still required

Healthy and damaged artwork have been approved outside the game; scene rendering
and damage/repair transitions with the new art remain unverified in game.
Check native rack/control-panel
layout, placing the intake against a real tank in each rotation, power loss and
restoration, save/reload while ON, and install/uninstall/damage/repair with four
bottles present. Verify cabin extraction changes the actual room's gas and that
mixture tooltips distinguish pressure from oxygen content. Native physics,
rendering, game save reconstruction and full scene item conversion cannot be
proven by the offline fixture.
