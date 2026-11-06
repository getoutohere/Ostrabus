# Venus-area fixes

Namespace `Ostrabus.Venus`, formerly `VorbHaulerReturn`. These patches cover:

- VORB hauler route validation, manual navigation fallback and return fuel assistance.
- Enabling native VORB derelict anchoring and correcting the orbit lookup on undock.
- A 70% Venus-specific wreck spawn chance at VORB.

General deploying-hauler home-return recovery and abandoned-thrust cleanup live
in [Haulers](../Haulers/README.md) and apply at all stations. General orbital and
super-fast-forward corrections live in `Orbital`.

The rename changes organization only. Detailed navigation diagnosis and policy
history: [VORB hauler return](../Notes/VORB-HAULER-RETURN-PATCH.md).
