# Status

- Added a root `README.md` so the project is easier to publish and understand on GitHub.
- Expanded `.gitignore` to exclude local Visual Studio/editor artifacts before the first push.
- Removed the empty AccessoryTabInventory.cs file.
- Removed dead/no-op code:
  - AccessorySlot.MarkDirty() override
  - AccessoryTabCore.TryRegisterInventoryClass() and its unused state fields
  - `AccessorySlotGridMouseDownPatch` and `AccessorySlotGridMouseUpPatch`
  - unused reflection field/static init in `AccessoryInventoryModificationPatch`
- Trimmed obvious unused usings in core, inventory, and patches.
- Fixed client-side persisted accessory cache updates to use the scoped world/server save key consistently.
- Backfilled remote entity cache on UID fallback to make remote accessory rendering more reliable.
- Build status: successful after cleanup.

## In-Game Mod Description

Adds an accessory tab with synced extra equipment slots for cosmetic and wearable items, with multiplayer support and more specialized Accessorize-compatible slots planned as a WIP.

## Mod Page Description

`AccessoryTab` adds a dedicated accessories tab to the character screen, giving players extra wearable slots for cosmetic and utility-style equipment without interfering with normal gear handling. The mod includes multiplayer syncing so equipped accessories update correctly between players, and it is designed to work cleanly alongside other mods with similar inventory and rendering interactions.

This project is still a WIP. A planned next step is a compatibility/patch layer for `Accessorize`, including more specialized slot support for things like earrings and other slot-specific accessory types.

