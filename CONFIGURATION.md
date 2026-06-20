# AccessoryTab Configuration Guide

## Overview

AccessoryTab can be configured via the `AccessoryTabConfig.json` file, which allows server admins and modpack creators to:
- Enable or disable specific accessory slots
- Redirect slots to vanilla inventory slots
- Connect slots to other mods' inventory systems
- Restrict slots to specific item categories

## Configuration Location

**Server/Singleplayer:**
`VintagestoryData/ModConfig/AccessoryTabConfig.json`

**Client (for reference only - server config takes precedence):**
`VintagestoryData/ModConfig/AccessoryTabConfig.json`

## Basic Configuration

### Enabling/Disabling Slots

The simplest way to disable slots is via the `enabledSlots` array:

```json
{
  "enabledSlots": [0, 1, 2, 3]
}
```

This configuration only shows the first 4 slots (0-3), hiding slots 4-7.

### Slot Configuration

Each slot (0-7) can be configured individually in the `slotMappings` section:

```json
{
  "slotMappings": {
	"0": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Cape Slot",
	  "targetSlot": null,
	  "allowedCategories": []
	}
  }
}
```

## Configuration Options

### Slot Mapping Properties

| Property | Type | Description |
|----------|------|-------------|
| `enabled` | boolean | Whether this slot appears in the GUI |
| `type` | string | Slot behavior: `"accessory"`, `"vanilla"`, or `"modded"` |
| `displayName` | string | Name for tooltips and future UI |
| `targetSlot` | string | Target slot identifier (null for custom accessory slots) |
| `allowedCategories` | array | List of allowed item categories (empty = allow all) |

### Slot Types

#### 1. Accessory (Custom Slot)

Default behavior - items stored in AccessoryTab's custom inventory.

```json
{
  "type": "accessory",
  "displayName": "Accessory Slot",
  "targetSlot": null,
  "allowedCategories": []
}
```

#### 2. Vanilla (Redirect to Game Slot)

Redirects to a vanilla Vintage Story inventory slot.

**Available Vanilla Slots:**
- `head` - Head/helmet slot
- `body` - Torso/chest armor
- `legs` - Leg armor
- `shoulder` - Shoulder armor
- `arms` - Arm armor
- `emblem` - Emblem slot
- `neck` - Necklace/amulet
- `back` - Cloak/cape
- `face` - Face/mask slot
- `hand` - Glove/gauntlet
- `waist` - Belt slot
- `feet` - Boot slot

**Example - Cape Slot:**
```json
{
  "0": {
	"enabled": true,
	"type": "vanilla",
	"displayName": "Cape",
	"targetSlot": "back",
	"allowedCategories": ["Wearable"]
  }
}
```

#### 3. Modded (Connect to Another Mod)

Connects to inventory slots from other mods.

**Format:** `"modid:slotname"`

**Example - Connecting to Hypothetical "MoreWearables" Mod:**
```json
{
  "1": {
	"enabled": true,
	"type": "modded",
	"displayName": "Extra Slot",
	"targetSlot": "morewearables:extraslot",
	"allowedCategories": []
  }
}
```

⚠️ **Note:** Both mods must be installed, and the target mod must expose its inventory slots properly.

### Category Restrictions

Limit which items can be placed in a slot using `allowedCategories`:

**Example - Helmet-Only Slot:**
```json
{
  "allowedCategories": ["Armor-Head", "Hat"]
}
```

**Example - Wearables Only:**
```json
{
  "allowedCategories": ["Wearable", "Clothing"]
}
```

**Empty Array = Allow All:**
```json
{
  "allowedCategories": []
}
```

## Common Configuration Examples

### Example 1: Only 4 Accessory Slots

```json
{
  "enabledSlots": [0, 1, 2, 3],
  "slotMappings": {
	"0": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Accessory 1",
	  "targetSlot": null,
	  "allowedCategories": []
	},
	"1": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Accessory 2",
	  "targetSlot": null,
	  "allowedCategories": []
	},
	"2": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Accessory 3",
	  "targetSlot": null,
	  "allowedCategories": []
	},
	"3": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Accessory 4",
	  "targetSlot": null,
	  "allowedCategories": []
	}
  }
}
```

### Example 2: Mixed Vanilla and Custom Slots

```json
{
  "enabledSlots": [0, 1, 2, 3, 4, 5],
  "slotMappings": {
	"0": {
	  "enabled": true,
	  "type": "vanilla",
	  "displayName": "Cape",
	  "targetSlot": "back",
	  "allowedCategories": ["Wearable"]
	},
	"1": {
	  "enabled": true,
	  "type": "vanilla",
	  "displayName": "Amulet",
	  "targetSlot": "neck",
	  "allowedCategories": ["Wearable"]
	},
	"2": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Ring 1",
	  "targetSlot": null,
	  "allowedCategories": ["Jewelry"]
	},
	"3": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Ring 2",
	  "targetSlot": null,
	  "allowedCategories": ["Jewelry"]
	},
	"4": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Trinket 1",
	  "targetSlot": null,
	  "allowedCategories": []
	},
	"5": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Trinket 2",
	  "targetSlot": null,
	  "allowedCategories": []
	}
  }
}
```

### Example 3: Role-Specific Slots

```json
{
  "enabledSlots": [0, 1, 2, 3],
  "slotMappings": {
	"0": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Helmet",
	  "targetSlot": null,
	  "allowedCategories": ["Armor-Head", "Hat"]
	},
	"1": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Chest Armor",
	  "targetSlot": null,
	  "allowedCategories": ["Armor-Body"]
	},
	"2": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Tool Belt",
	  "targetSlot": null,
	  "allowedCategories": ["Tool"]
	},
	"3": {
	  "enabled": true,
	  "type": "accessory",
	  "displayName": "Cosmetic",
	  "targetSlot": null,
	  "allowedCategories": ["Wearable", "Clothing"]
	}
  }
}
```

## Troubleshooting

### Configuration Not Loading
1. Check file location: `VintagestoryData/ModConfig/AccessoryTabConfig.json`
2. Verify JSON syntax (use a JSON validator)
3. Check server logs for error messages starting with `[AccessoryTab]`

### Slots Not Appearing
1. Verify slot index is in `enabledSlots` array
2. Check `enabled: true` in the slot mapping
3. Ensure slot index is between 0-7

### Vanilla Redirect Not Working
1. Verify the `targetSlot` name matches vanilla slot names exactly
2. Check that `type` is set to `"vanilla"`
3. Some vanilla slots may not be accessible depending on game version

### Modded Redirect Not Working
1. Ensure both mods are installed and loaded
2. Verify the mod exposes its inventory slots properly
3. Check the mod's documentation for the correct slot identifier format

## Default Configuration

If no configuration file exists, AccessoryTab creates a default with all 8 slots enabled as custom accessory slots.

## Reloading Configuration

Configuration is loaded when the server starts. To apply changes:
1. Edit `AccessoryTabConfig.json`
2. Save the file
3. Restart the server

*Note: Hot-reloading is not currently supported.*

## Support & Documentation

- GitHub: https://github.com/Faithfulshot/AccessoryTab
- Vintage Story Forums: [Link to forum thread]
- Wiki: [Link to wiki]

## Advanced: Modpack Integration

For modpack creators wanting to preset configurations:

1. Include a custom `AccessoryTabConfig.json` in your modpack
2. Place it in the ModConfig folder
3. Document any slot redirects or restrictions for players
4. Test thoroughly with all included mods

---

**Note:** This configuration system is compatible with both P2P singleplayer and dedicated servers. Server configuration takes precedence in multiplayer scenarios.
