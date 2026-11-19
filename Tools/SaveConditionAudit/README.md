# Saved actor condition inspection

```powershell
dotnet run --project Tools/SaveConditionAudit -c Release -- <save.zip> "actor ID" DcRad01 IsLifeRadExposure StatRad
```

Streams the archived root CO table one record at a time, then physical ship CO
tables if the actor is materialized locally, retaining only the exact actor ID.
Reports the source entry and requested raw condition assignments, explicit presence,
saved zero markers and
simple scalar values, plus the saved player ID and clock. Duplicate actor IDs
are rejected. DEFAULT definitions, delayed changes and native runtime condition
evaluators are not expanded; absence is distinct from an explicitly saved zero.

No game code or live API is executed and no archive is extracted or written.
The archive SHA-256 is checked before and after inspection; a concurrent save
causes a refusal rather than a report assembled from changing source data.
