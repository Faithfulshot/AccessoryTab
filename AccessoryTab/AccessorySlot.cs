using System;
using Vintagestory.API.Common;

namespace AccessoryTab;

/// <summary>
/// Custom item slot for accessories with config-driven validation.
/// Blocks item placement when a slot is disabled, and enforces per-slot category restrictions.
/// </summary>
public class AccessorySlot : ItemSlot
{
    /// <summary>
    /// The index (0-7) of this slot within the AccessoryInventory.
    /// Used to look up configuration such as enabled/disabled state and category restrictions.
    /// </summary>
    public int SlotIndex { get; }

    public AccessorySlot(InventoryBase inventory, int slotIndex) : base(inventory)
    {
        SlotIndex = slotIndex;
    }

    /// <summary>
    /// Determines whether a specific item can be placed in this accessory slot.
    /// Returns false immediately if the slot is disabled via server configuration.
    /// Also enforces per-slot category restrictions configured by admins.
    /// </summary>
    /// <param name="itemSlot">The slot containing the item being placed</param>
    /// <returns>True if the item can be placed, false otherwise</returns>
    public override bool CanHold(ItemSlot itemSlot)
    {
        // Disabled slots reject all items
        if (!AccessoryTabCore.IsSlotEnabled(SlotIndex))
            return false;

        if (!base.CanHold(itemSlot)) return false;

        // Enforce per-slot category restrictions from config
        var slotConfig = AccessoryTabCore.GetSlotConfig(SlotIndex);
        if (slotConfig?.AllowedCategories != null && slotConfig.AllowedCategories.Count > 0)
        {
            var itemClass = itemSlot.Itemstack?.Item?.ItemClass.ToString()
                         ?? itemSlot.Itemstack?.Block?.Code?.ToString();
            if (itemClass == null) return false;

            foreach (var cat in slotConfig.AllowedCategories)
            {
                if (itemClass.IndexOf(cat, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the maximum stack size allowed in this accessory slot.
    /// Disabled slots report 0 to further prevent interaction.
    /// </summary>
    public override int MaxSlotStackSize => AccessoryTabCore.IsSlotEnabled(SlotIndex)
        ? (Itemstack?.Item?.MaxStackSize ?? 1)
        : 0;
}