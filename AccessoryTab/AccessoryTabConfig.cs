using System.Collections.Generic;

namespace AccessoryTab;

/// <summary>
/// Configuration model for AccessoryTab slot mappings and settings.
/// Loaded from AccessoryTabConfig.json in the mod config directory.
/// Allows server admins to customize slot behavior, enable/disable slots, 
/// and redirect to vanilla or modded inventory slots.
/// </summary>
public class AccessoryTabConfig
{
    /// <summary>
    /// List of slot indices (0-7) that are enabled.
    /// Slots not in this list will be hidden from the GUI.
    /// </summary>
    public List<int> EnabledSlots { get; set; } = new() { 0, 1, 2, 3, 4, 5, 6, 7 };

    /// <summary>
    /// Mapping of slot index to slot configuration.
    /// Defines behavior for each of the 8 accessory slots.
    /// </summary>
    public Dictionary<string, SlotMapping> SlotMappings { get; set; } = new();

    /// <summary>
    /// Default configuration with all 8 slots enabled as standard accessories.
    /// </summary>
    public static AccessoryTabConfig CreateDefault()
    {
        var config = new AccessoryTabConfig();

        for (int i = 0; i < 8; i++)
        {
            config.SlotMappings[i.ToString()] = new SlotMapping
            {
                Enabled = true,
                Type = SlotType.Accessory,
                DisplayName = $"Accessory Slot {i + 1}",
                TargetSlot = null,
                AllowedCategories = new List<string>()
            };
        }

        return config;
    }
}

/// <summary>
/// Configuration for an individual accessory slot.
/// </summary>
public class SlotMapping
{
    /// <summary>
    /// Whether this slot is enabled and visible in the GUI.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Type of slot: Accessory (custom), Vanilla (redirect to game slot), or Modded (redirect to mod slot).
    /// </summary>
    public SlotType Type { get; set; } = SlotType.Accessory;

    /// <summary>
    /// Display name shown in GUI tooltip or future UI elements.
    /// </summary>
    public string DisplayName { get; set; } = "Accessory Slot";

    /// <summary>
    /// Target slot identifier when Type is Vanilla or Modded.
    /// For Vanilla: use slot names like "back", "neck", "face", etc.
    /// For Modded: use format "modid:slotname"
    /// Null for custom Accessory type.
    /// </summary>
    public string TargetSlot { get; set; }

    /// <summary>
    /// List of allowed item categories/classes for this slot.
    /// Empty list = allow all items.
    /// Example values: "Armor-Head", "Wearable", "Hat", etc.
    /// </summary>
    public List<string> AllowedCategories { get; set; } = new();
}

/// <summary>
/// Type of accessory slot behavior.
/// </summary>
public enum SlotType
{
    /// <summary>
    /// Custom accessory slot managed by this mod (default).
    /// Items stored in AccessoryInventory.
    /// </summary>
    Accessory,

    /// <summary>
    /// Redirect to a vanilla Vintage Story inventory slot.
    /// Items stored in the player's gear inventory.
    /// </summary>
    Vanilla,

    /// <summary>
    /// Redirect to a slot from another mod.
    /// Items stored in that mod's inventory.
    /// </summary>
    Modded
}
