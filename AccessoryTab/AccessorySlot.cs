using Vintagestory.API.Common;

namespace AccessoryTab;

/// <summary>
/// Custom item slot for accessories with property-specific validation hooks.
/// This class allows per-slot-type customization (max stack size, category restrictions, etc.)
/// </summary>
public class AccessorySlot : ItemSlot
{
    public AccessorySlot(InventoryBase inventory) : base(inventory)
    {
    }

    /// <summary>
    /// Override this to validate item placement for specific slot types.
    /// Return true to allow placement, false to reject.
    /// </summary>
    /// <remarks>
    /// EXAMPLE: Add property-specific code here to restrict item categories per slot:
    /// 
    /// public override bool CanHold(ItemSlot itemSlot)
    /// {
    ///     if (!base.CanHold(itemSlot)) return false;
    ///     
    ///     // TODO: Implement slot-specific item type validation
    ///     // var slotRule = AccessoryTabCore.GetSlotRule(this.SlotNumber);
    ///     // if (slotRule != null && itemSlot.Itemstack?.Item?.ItemClass != slotRule)
    ///     //     return false;
    ///     
    ///     return true;
    /// }
    /// </remarks>
    public override bool CanHold(ItemSlot itemSlot)
    {
        return base.CanHold(itemSlot);
    }

    /// <summary>
    /// Override this to customize maximum stack size for specific slots.
    /// </summary>
    /// <remarks>
    /// EXAMPLE: Add property-specific code here for max stack size validation:
    /// 
    /// public override int MaxSlotStackSize
    /// {
    ///     get
    ///     {
    ///         // Accessories typically have max stack of 1
    ///         // TODO: Customize per slot if needed
    ///         var item = Itemstack?.Item;
    ///         return item?.MaxStackSize ?? 1;
    ///     }
    /// }
    /// </remarks>
    public override int MaxSlotStackSize => Itemstack?.Item?.MaxStackSize ?? 1;
}
