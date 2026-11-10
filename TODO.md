# QoL

## Hot key for crew helmet and EVA battery eqip/unequip

Implemented: Shift+F by default, rebindable in the Mod Settings PDA app. See
[functional hotkeys](Hotkeys/README.md) for behavior and offline validation.

The toggle removes an eva helmet from controlled character's head and places it in the next available carried inventory slot (i.e. hand or backpack carried in off-hand) and does the same for EVA batteries slotted in worn (only worn) and does the same. If no room is available, the removal is refused. The toggle writes a single line to the action log in the player's GUI for the controlled character what actions the toggle took i.e.: removed helmet, no room to remove battery.

The opposite action, assuming the player is not wearing a helmet or has a battery slotted into eva, is to search the carried inventory and slot the items in.

The same principal applies for pressure suit helmets (pressure suits don't have batteries though).

##

# Simulation gaps

- [NPCs can be obliterated by administrative ship cleanup](Notes/SIMULATION-GAPS.md):
  preserve character continuity when hull retirement cannot relocate living crew.
