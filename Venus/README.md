# Venus-area fixes

Namespace `Ostrabus.Venus`, formerly `VorbHaulerReturn`. These patches cover:

- VORB hauler route validation, manual navigation fallback and return fuel assistance.
- Enabling native VORB derelict anchoring and correcting the orbit lookup on undock.
- A configurable Venus-specific wreck spawn chance at VORB (50% by default).
- Correct VORB retrieval dispatch and wreck ownership lookup instead of using VNCA's regional station.
- [Dolly handling](DOLLY-ENCUMBRANCE.md): count loaded dollies at 10% encumbrance on Venus aerostats and physically docked ships, without changing physical mass.

General deploying-hauler home-return recovery and abandoned-thrust cleanup live
in [Haulers](../Haulers/README.md) and apply at all stations. General orbital and
super-fast-forward corrections live in `Orbital`.

## Wreck distribution

PDA **MOD SETTINGS → Venus-specific wrecks** has a whole-percentage slider from
0–100%, default 50%. The remainder uses the native general `RandomShip` pool;
this is a pool-selection probability, not a guaranteed mix in a small batch.
Changes apply to future VORB hauler deployments and persist in BepInEx config as
`[Venus] VenusWreckPercent`. Existing wrecks and other stations are unaffected.
The choice is made once per deployment, including spawn retries. A missing
Venus-specific loot pool retains the native general-pool fallback.

The rename changes organization only. Detailed navigation diagnosis and policy
history: [VORB hauler return](../Notes/VORB-HAULER-RETURN-PATCH.md).

## Retrieval depot

`VorbRetrievalDepotPatch` changes the empty VORB retriever's station lookup inside
native `HaulerRetrievingPilot.GetTarget`. A registered controller with saved
`HomeStation == "VORB"` uses that valid station for recent dispatch messages and
the fallback wreck search. VORB creates VENCCiv wrecks; the previous regional
lookup returned VNCA and searched VNCACiv instead. Existing idle retrievers use
the correction on their next native target selection after loading the build.

Explicit rescue assignments keep native precedence and can target other owners.
Native wreck exclusions, random selection, visit weighting, towing return and
navigation checks remain intact. Invalid/missing homes and non-VORB controllers
retain native behavior. The patch does not add a fleet scan, per-frame callback,
save data, ship references retained across calls, refueling or teleportation.
See the [depot audit](HAULER-RETRIEVAL-AUDIT.md) for both shipped derelict operations.

`Tests/VorbRetrievalCheck` reproduces the native wrong-owner and no-job outcomes
before applying the production transpiler. Its 25 bundled-Mono checks exercise
the shipped target selection, registry/ownership lookup, message precedence and
expiry, plot/hidden/tutorial/load/damage exclusions, native towing return, OKLG,
invalid-home fallbacks and the native saved-controller constructor. Unity object
equality, integer RNG and condition queries use deterministic fixture boundaries;
this is not a full Unity navigation or live delivery test.

```powershell
dotnet build Tests/VorbRetrievalCheck -c Release --nologo
if ($LASTEXITCODE -eq 0) {
    dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/VorbRetrievalCheck/bin/Release/netstandard2.1/VorbRetrievalCheck.dll
}
```
