# Native console buffer regression checks

```powershell
dotnet build Tests/ConsoleBufferCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/ConsoleBufferCheck/bin/Release/netstandard2.1/ConsoleBufferCheck.dll
```

Links the production buffer repair and existing console noise filter against the
installed game assembly. Ostranauts 1.0.1.10 passes **3,058 checks**.
Reproduces native `Log` abandoning trimming on a long
leading line. The nonterminating native `LogInfo` control is established from its
unchanged substring loop, rather than hanging the harness.

Checks retention of newest entries and traces, oversized entries/single lines,
UTF-16 boundaries, 3,000 repeated overflows, invalid/configured limits, all log
types, native error counters, visibility settings, filter composition and patch
removal. Optional native file logging retains full Info output while the GUI
history remains bounded. Native IL checks cover both command-write sites and refuse changed write
counts. Native `DrawConsole` executes through the viewport/text-area call sites;
engine GUI submission, input, dimensions and measurement are stubbed. Display
checks cover existing oversized text, consistent rectangles, following the bottom,
preserving history browsing, resize/font invalidation and cached measurement.

No live API is contacted and no game assets or saves are modified. Unity internal
call warnings are expected from the engine-free Mono host; assertions and process
exit status determine success. Real font wrapping/visual appearance remain live
validation.
