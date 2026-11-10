# Static Harmony declaration inventory

Read the built plugin's metadata without loading it, invoking patch factories or
installing hooks. From the repository root:

```powershell
dotnet run --project Tools/PatchInventory -c Release -- <Ostrabus.dll> <BepInEx/core-directory>
```

The JSON lists HarmonyPatch-decorated types, target attribute arguments and
patch/factory method names. The count is **patch classes**, not installed hooks,
distinct patched native methods or recurring calls. TargetMethods factories can
expand to multiple targets. Prepare can disable classes. Diagnostic patches
installed manually at runtime are outside this inventory. Do not invoke patch
factories offline just to expand the count: some depend on initialized game data.

Output may be redirected to ignored `obj/patch-inventory.json`. The November 9
initial Debug DLL contained 165 decorated patch types. After the native-priority
return repair added one and target-observer consolidation removed one, the
inventory returned to 165; shared refill observers replace two regional
observers without increasing that count. The escape-setter repair adds one,
bringing the current inventory to 166. See the
[behavior audit](../../Notes/AI-BEHAVIOR-PATCH-AUDIT.md) for interpretation.
