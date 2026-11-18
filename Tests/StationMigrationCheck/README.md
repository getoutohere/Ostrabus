# Station migration persistence contracts

Five checks execute actual 1.1.0.1 `DataHandler.GetCondOwner`/`Ship.SpawnItems`
branches under bundled Mono. They demonstrate saved-definition precedence,
the need to synchronize both saved representations, live-object precedence,
rejection of added items without saved CO data, and admission when that data
is present.

```powershell
dotnet build Tests/StationMigrationCheck -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/StationMigrationCheck/bin/Release/netstandard2.1/StationMigrationCheck.dll
```

Factory calls stop before Unity allocation. The accepted-item reconstruction
boundary returns no GameObject. These are loader-admission/lookup tests, not
kiosk conversion, placement, GUI, rollback or complete save-round-trip tests.
They do not connect to the running game or change saved data.
