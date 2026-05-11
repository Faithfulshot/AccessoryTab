using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace AccessoryTab;

/// <summary>
/// Custom inventory for accessories with 8 slots.
/// Fires OnAccessoryModified event when items change, allowing decoupled broadcasting and persistence.
/// Follows the official Vintage Story pattern from InventoryGear and InventorySmelting.
/// </summary>
public class AccessoryInventory : InventoryBase
{
    private const string SaveKey = "accessorytab.slots";
    private readonly string playerId;
    private ItemSlot[] slots;
    private bool suppressAccessoryModifiedEvent;

    /// <summary>
    /// Event fired when accessories are modified. Allows event handlers to manage broadcasting and persistence.
    /// </summary>
    public event EventHandler<EventArgs> OnAccessoryModified;

    public string PlayerId => playerId;
    public override int Count => 8;

    public override ItemSlot this[int slotId]
    {
        get
        {
            if (slotId < 0 || slotId >= Count) return null;
            return slots?[slotId];
        }
        set
        {
            if (slotId < 0 || slotId >= Count) throw new ArgumentOutOfRangeException(nameof(slotId));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (slots != null)
            {
                slots[slotId] = value;
            }
        }
    }

    public AccessoryInventory(string inventoryId, ICoreAPI api) : base("accessorytab", inventoryId, api)
    {
        playerId = inventoryId;
        slots = GenEmptySlots(Count);
    }

    public AccessoryInventory(string inventoryId, ICoreAPI api, bool skipLoadPersistedSlot) : base("accessorytab", inventoryId, api)
    {
        playerId = inventoryId;
        slots = GenEmptySlots(Count);

        if (!skipLoadPersistedSlot && api is ICoreClientAPI capi)
        {
            LoadPersistedSlot(capi);
        }
    }

    protected override ItemSlot NewSlot(int i)
    {
        return new AccessorySlot(this);
    }

    public override void OnItemSlotModified(ItemSlot slot)
    {
        base.OnItemSlotModified(slot);

        var capi = Api as ICoreClientAPI;
        if (capi != null)
        {
            var serialized = SerializeSlot();
            capi.Settings.String[GetClientSaveKey(capi)] = serialized;
        }

        var localPlayer = (Api as ICoreClientAPI)?.World?.Player;
        if (localPlayer != null && localPlayer.PlayerUID == playerId)
        {
            localPlayer.Entity?.MarkShapeModified();
        }

        if (!suppressAccessoryModifiedEvent)
        {
            OnAccessoryModified?.Invoke(this, EventArgs.Empty);
        }
    }

    public void EnsureLoaded(ICoreClientAPI capi)
    {
        bool anyLoaded = false;
        for (int i = 0; i < Count; i++)
        {
            if (this[i]?.Itemstack != null)
            {
                anyLoaded = true;
                break;
            }
        }

        if (anyLoaded)
        {
            return;
        }

        LoadPersistedSlot(capi);
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        List<ItemSlot> modifiedSlots = new List<ItemSlot>();
        slots = SlotsFromTreeAttributes(tree, slots, modifiedSlots);

        for (int i = 0; i < modifiedSlots.Count; i++)
        {
            DidModifyItemSlot(modifiedSlots[i]);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        if (slots != null)
        {
            SlotsToTreeAttributes(slots, tree);
        }
    }

    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (slotId < 0 || slotId >= Count || sourceSlot == null) return null;
        return base.ActivateSlot(slotId, sourceSlot, ref op);
    }

    public override object Close(IPlayer player)
    {
        try
        {
            // Save on close
            var capi = Api as ICoreClientAPI;
            if (capi != null)
            {
                capi.Settings.String[GetClientSaveKey(capi)] = SerializeSlot();
            }
            return base.Close(player);
        }
        catch (Exception ex)
        {
            AccessoryTabCore.Logger?.Error($"[AccessoryTab] Error closing inventory: {ex.Message}");
            throw;
        }
    }

    private void LoadPersistedSlot(ICoreClientAPI api)
    {
        if (api is not ICoreClientAPI capi) return;
        var saveKey = GetClientSaveKey(capi);
        if (!capi.Settings.String.Exists(saveKey)) return;

        string b64 = capi.Settings.String[saveKey];
        if (!string.IsNullOrWhiteSpace(b64))
        {
            SetSlotFromSerialized(b64, capi);
        }
    }

    internal static string GetClientSaveKey(ICoreClientAPI capi, string playerId)
    {
        var savegameIdentifier = capi?.World?.SavegameIdentifier;
        if (string.IsNullOrWhiteSpace(savegameIdentifier))
        {
            return $"{SaveKey}.{playerId}";
        }

        return $"{SaveKey}.{savegameIdentifier}.{playerId}";
    }

    private string GetClientSaveKey(ICoreClientAPI capi)
    {
        return GetClientSaveKey(capi, playerId);
    }

    public string SerializeSlot()
    {
        var tree = new TreeAttribute();
        ToTreeAttributes(tree);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        tree.ToBytes(bw);
        return Convert.ToBase64String(ms.ToArray());
    }

    public void SetSlotFromSerialized(string b64, ICoreClientAPI capi = null, bool suppressAccessoryModifiedEvent = false)
    {
        var previousSuppressEventState = this.suppressAccessoryModifiedEvent;
        this.suppressAccessoryModifiedEvent = suppressAccessoryModifiedEvent;

        if (string.IsNullOrWhiteSpace(b64))
        {
            try
            {
                for (int i = 0; i < Count; i++)
                {
                    var slot = this[i];
                    if (slot != null)
                    {
                        slot.Itemstack = null;
                        MarkSlotDirty(i);
                    }
                }
                if (capi != null) capi.Settings.String[GetClientSaveKey(capi)] = "";
            }
            catch (Exception ex)
            {
                AccessoryTabCore.Logger?.Error($"[AccessoryTab] Error clearing slots: {ex.Message}");
            }
            finally
            {
                this.suppressAccessoryModifiedEvent = previousSuppressEventState;
            }

            return;
        }

        try
        {
            byte[] data = Convert.FromBase64String(b64);
            var tree = TreeAttribute.CreateFromBytes(data);

            // Deserialize slots from tree attributes
            List<ItemSlot> modifiedSlots = new List<ItemSlot>();
            SlotsFromTreeAttributes(tree, slots, modifiedSlots);

            // Fire modification events for all changed slots
            for (int i = 0; i < modifiedSlots.Count; i++)
            {
                DidModifyItemSlot(modifiedSlots[i]);
            }
        }
        catch (Exception ex)
        {
            AccessoryTabCore.Logger?.Error($"[AccessoryTab] Error deserializing slots: {ex.Message}");
        }
        finally
        {
            this.suppressAccessoryModifiedEvent = previousSuppressEventState;
        }
    }
}
