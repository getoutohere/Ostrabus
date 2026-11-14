# Local navigation reference regressions

See [the caller audit and repair](../../Navigation/LOCAL-NAVIGATION-FRAMES.md).
Fixtures contain public orbital elements, not save inventory or NPC data.

```
dotnet build Tests/LocalNavigationFrameCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/LocalNavigationFrameCheck/bin/Release/netstandard2.1/LocalNavigationFrameCheck.dll
```

Native orbit updates, patrol/ambush generation, police SFF placement and local
command locking run against the shipped assembly. Controlled world/Unity/cargo
boundaries are documented in the linked audit. No live game requests or mutations
are made by this test project.
