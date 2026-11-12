# Native hull consumer inventory

Run `dotnet run --project Tools/HullConsumerAudit -c Release -- <Assembly-CSharp.dll>`.
Redirect JSON to an ignored local file if desired. Uses Cecil metadata only;
does not execute Unity/game code or install Harmony patches.

Records assembly SHA-256, method signatures, IL offsets and direct references to
native radius/size accessors, shared collision predicates, placement methods and
ShipDraw radius wrappers, including generated methods. Compare reports after a
game update, then inspect changed callers and their callers in native source.

Interpretation and test boundaries live in the
[consumer audit](../../Navigation/Collisions/CONSUMER-AUDIT.md). An unchanged
reference inventory alone does not prove unchanged method behavior.
