# Free-coast integration readiness - November 4, 2026

Ready for deployment and ordinary play based on offline validation. No runtime
changes were needed during this additional audit. The tests execute against the
installed game assemblies under the game's bundled Mono runtime.

## Actual update path

The tests run the shipped private StarSystem.UpdateShip method and the patched
ShipSitu.TimeAdvance method, including native gravity selection, injection and
restoration. Only unrelated UI/shipboard leaf effects are stubbed. They confirm:

- Eligible ships actually reach the improved integrator through the production
  Harmony scope; this is no longer tested only by manually opening that scope.
- RCS, torch thrust, lift, drag, independent external acceleration, ignored gravity
  and docking groups produce exactly identical translational states with coast
  enabled or disabled.
- Plotted playback delegates once and preserves its output even when playback
  clears the route. Playback itself remains an explicit test double; these tests
  establish dispatch compatibility, not a full offline torch-reactor simulation.
- Native external acceleration is restored after the update.
- Native angular motion is preserved.
- Both successful updates and deliberately thrown exceptions release all scoped
  ship/body references. Exceptions propagate unchanged; subsequent updates recover
  and direct prediction calls remain outside the coast scope.
- Atmosphere and malformed-ephemeris guards reject the replacement path.
- Cached preparation and the patched TimeAdvance method allocate zero managed
  bytes in the warmed allocation checks.

## Numerical checks

The endurance cases use captured Venus/Sol orbital elements and the game's
rounded gravity constant. Timesteps alternate between 0.1, 0.53333, 2 and 5.33333
game seconds. An independent universal-variable conic solution supplies endpoint
comparisons; no native cached body velocity is used as the reference velocity.

| Coast | Duration | Absolute energy error | Conic endpoint difference |
|---|---:|---:|---:|
| Circular | 48 game hours | 0.0000000296% | 2.100 km |
| Eccentric | 48 game hours | 0.000001482% | 2.411 km |
| Retrograde eccentric | 48 game hours | 0.000000836% | 2.418 km |
| Escape | 2 game hours | 0.00006232% | 0.00515 km |

Bound cases stayed bound; the escape case retained positive energy. Endpoint
differences are accumulated position/phase errors, not secular energy growth.
The fixed-5.333-second, 136.53-minute patched native-method test has 0.0002387%
energy error. A separate disabled-coast control with the captured Venus elements
reproduces 4.35856% native error over the same duration, demonstrating that the
test detects the defect rather than merely accepting both algorithms.

The complete suite passes 4,204 checks, including the existing SFF, group,
atmosphere, impact, travel and NavData-dispatch regressions. Other in-project
Harmony patches do not target UpdateShip or TimeAdvance outside Orbital.

## Limits and disposition

This validates the new ordinary free-coast path. It does not expand that path to
powered/docked objects, change saves, repair an already corrupted trajectory, or
claim end-to-end validation of every Unity subsystem. The SFF helper's separate
tighter-orbit-solver coordinate discrepancy was corrected on November 6: normal
coast and SFF now share NativeEphemeris, matching native positions without changing
native caches. See the resolution in TIMESTEP-FINDINGS.md.
These limitations do not require another controlled save-load experiment before
using this free-coast improvement in normal play.

Reproduce:

```powershell
dotnet build Tests/OrbitalCheck -c Release --nologo
if ($LASTEXITCODE -eq 0) {
    dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/OrbitalCheck/bin/Release/netstandard2.1/OrbitalCheck.dll
}
dotnet build Ostrabus.csproj -c Debug --nologo
```
