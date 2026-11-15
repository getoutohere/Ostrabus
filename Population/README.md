# Random NPC gender distribution

An intentional simulation policy changes native random NPC gender generation to
1% non-binary, 49.5% male and 49.5% female. Vanilla uses 10%, 44% and 46%
respectively. This is a simulation improvement, separate from bug repairs.

The percentages are hardcoded in `PersonSpec.GetRandomGender`; the shipped
`personspecs/personspecs.json` selects random generation with an empty `strGender`
or explicitly assigns a gender. There is no native asset field for the random
weights. `RandomGenderPatch.cs` changes only the two threshold constants, from
10/54 to 1/50.5 on the native 0-to-100 roll. It preserves the single RNG draw,
native callers, name generation and explicit/player-relative asset assignments.
It adds no per-frame work, allocation, state or save schema change. Existing
saved NPCs are unchanged. The native inclusive first threshold is retained;
endpoint quantization is negligible for the stated percentages.

Custom feature `population-gender` is independently selectable under simulation
improvements. Bugfix-only builds omit it; Recommended and Full include it. It is
independent of the fresh-crew identity bugfix.

`Tests/PopulationCheck` passes 23 checks using the actual native method before and after the
production transpiler, controlling only the Unity RNG boundary. It checks
distribution, threshold boundaries, one unchanged 0-to-100 draw, explicit asset
genders, player-relative fallbacks without a player, and the patch's instruction
scope. Homeworld and name services are fixtures; no live game or saves are touched.

```powershell
dotnet build Tests/PopulationCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/PopulationCheck/bin/Release/netstandard2.1/PopulationCheck.dll
```
