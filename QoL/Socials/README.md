# Socials contact visibility

Always-on display filtering for Beydat construction drones. Robot01 is the
"Weaver" Light Construction Drone; Robot02 is the "Enforcer" Heavy Security &
Construction Drone. Both definitions are confirmed in native condowners and
personspecs assets. PersonSpec.MakeCondOwner assigns robot names plus integer
surnames, but filtering checks exact Robot01/Robot02 identities instead of that
name pattern. New profiles can retain PersonSpec.strType; saved social profiles
omit it, so the resolved CO's strCODef/strType is also checked. This covers
existing saves. Renaming a drone does not evade the filter; a human with a numbered
name is unaffected. Other robot types are not automatically excluded.

The existing GetKnownSocialContacts postfix in Patch.cs invokes ContactVisibility
alongside its existing missing-CO rule. No additional Harmony hook, frame work,
state cache, new setting or asset modification is needed. Native Socials filters
only match relationship labels, so adding IsRobot to their JSON list alone would
not inspect a contact's character type.

Only the returned display list is filtered. Relationships, familiarity, events,
friendship, AI behavior, NPC existence and saves remain intact. Native Socials
rendering and the existing contact-list sorting continue normally. Construction
drones are also absent from GetKnownSocialContacts consumers such as its Socials
scroll lookup; the social simulation's own relationship dictionary is unchanged.

## Validation

The compiled predicate was exercised offline against the native home save's
487 social profiles and saved CO definitions. It identifies 24 construction
drones, including both Robot01 and Robot02, despite their social profile strType
fields being absent. There are 25 numbered names in that history, demonstrating
why a name-suffix test would be broader than the requested filter. Control cases
preserve ordinary humans, missing type data and unknown robot types while
recognizing both fresh profiles and saved CO definitions. No save was modified.
Private census output is in ignored obj/socials-filter-save-audit.txt and
obj/socials-save-summary.json. Debug builds cleanly; live UI remains to be observed.
