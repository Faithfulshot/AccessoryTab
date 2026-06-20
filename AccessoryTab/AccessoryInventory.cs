using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace AccessoryTab;

/// <summary>
/// Custom inventory for managing 8 accessory equipment slots per player.
/// Handles persistence to both client settings and server world data.
/// Fires OnAccessoryModified event when items change, allowing decoupled broadcasting and persistence.
/// Follows the official Vintage Story pattern from InventoryGear and InventorySmelting.
/// Used by both server-side (for authoritative state) and client-side (for local UI and remote player rendering).
/// </summary>
public class AccessoryInventory : InventoryBase
{
    #region Fields and Properties

    /// <summary>
    /// Key for saving accessory data to client settings (per-world, per-player).
    /// </summary>
    private const string SaveKey = "accessorytab.slots";

    /// <summary>
    /// The player UID this inventory belongs to.
    /// </summary>
    private readonly string playerId;

    /// <summary>
    /// The 8 accessory slots.
    /// </summary>
    private ItemSlot[] slots;

    /// <summary>
    /// When true, prevents OnAccessoryModified event from firing.
    /// Used during deserialization to avoid redundant network broadcasts.
    /// </summary>
    private bool suppressAccessoryModifiedEvent;

    /// <summary>
    /// Event fired when accessories are modified via GUI or network update.
    /// Allows event handlers to manage broadcasting and persistence.
    /// Server-side handlers broadcast to all clients; client-side handlers update visuals.
    /// </summary>
    public event EventHandler<EventArgs> OnAccessoryModified;

    /// <summary>
    /// Gets the player UID this inventory belongs to.
    /// </summary>
    public string PlayerId => playerId;

    /// <summary>
    /// Gets the total number of accessory slots (always 8).
    /// </summary>
    public override int Count => 8;

    /// <summary>
    /// Accesses a specific accessory slot by index (0-7).
    /// </summary>
    /// <param name="slotId">Slot index (0-7)</param>
    /// <returns>The slot at the specified index, or null if out of range</returns>
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

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new accessory inventory for a player.
    /// </summary>
    /// <param name="inventoryId">The player UID</param>
    /// <param name="api">The core API (client or server)</param>
    public AccessoryInventory(string inventoryId, ICoreAPI api) : base("accessorytab", inventoryId, api)
    {
        playerId = inventoryId;
        slots = GenEmptySlots(Count);
    }

    /// <summary>
    /// Creates a new accessory inventory with optional persistence loading.
    /// Used when creating temporary inventories for remote player rendering (skip loading).
    /// </summary>
    /// <param name="inventoryId">The player UID</param>
    /// <param name="api">The core API (client or server)</param>
    /// <param name="skipLoadPersistedSlot">If true, does not load from client settings (used for remote players)</param>
    public AccessoryInventory(string inventoryId, ICoreAPI api, bool skipLoadPersistedSlot) : base("accessorytab", inventoryId, api)
    {
        playerId = inventoryId;
        slots = GenEmptySlots(Count);

        if (!skipLoadPersistedSlot && api is ICoreClientAPI capi)
        {
            LoadPersistedSlot(capi);
        }
    }

    #endregion

    #region Slot Management

    /// <summary>
    /// Creates a new AccessorySlot instance for this inventory.
    /// Called by base class during slot initialization.
    /// </summary>
    /// <param name="i">Slot index</param>
    /// <returns>A new AccessorySlot</returns>
    protected override ItemSlot NewSlot(int i)
    {
        return new AccessorySlot(this, i);
    }

    /// <summary>
    /// Called when an item is added, removed, or changed in a slot.
    /// Saves to client settings, refreshes local player visuals, and fires OnAccessoryModified event.
    /// The event triggers server broadcasting in multiplayer scenarios.
    /// </summary>
    /// <param name="slot">The slot that was modified</param>
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

    /// <summary>
    /// Ensures the inventory has been loaded from client settings.
    /// If slots are already populated, does nothing. Otherwise, loads from persistent storage.
    /// Used during client initialization to restore accessories before GUI opens.
    /// </summary>
    /// <param name="capi">The client API</param>
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

    #endregion

    #region Serialization and Persistence

    /// <summary>
    /// Deserializes the inventory from a tree attribute structure.
    /// Called by the base inventory system when loading from network or storage.
    /// </summary>
    /// <param name="tree">The tree attribute containing serialized slot data</param>
    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        List<ItemSlot> modifiedSlots = new List<ItemSlot>();
        slots = SlotsFromTreeAttributes(tree, slots, modifiedSlots);

        for (int i = 0; i < modifiedSlots.Count; i++)
        {
            DidModifyItemSlot(modifiedSlots[i]);
        }
    }

    /// <summary>
    /// Serializes the inventory to a tree attribute structure.
    /// Called by the base inventory system when saving to network or storage.
    /// </summary>
    /// <param name="tree">The tree attribute to serialize into</param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        if (slots != null)
        {
            SlotsToTreeAttributes(slots, tree);
        }
    }

    /// <summary>
    /// Handles slot activation (e.g., clicking a slot in the GUI).
    /// Delegates to base implementation after validation.
    /// </summary>
    /// <param name="slotId">The slot being activated</param>
    /// <param name="sourceSlot">The source slot (e.g., the mouse cursor)</param>
    /// <param name="op">The item stack move operation</param>
    /// <returns>Result of the operation</returns>
    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (slotId < 0 || slotId >= Count || sourceSlot == null) return null;
        return base.ActivateSlot(slotId, sourceSlot, ref op);
    }

    /// <summary>
    /// Called when the inventory is closed by a player.
    /// Saves the current state to client settings before closing.
    /// </summary>
    /// <param name="player">The player closing the inventory</param>
    /// <returns>Result of the close operation</returns>
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

    /// <summary>
    /// Loads the accessory inventory from client settings persistence.
    /// Only called on the client side.
    /// </summary>
    /// <param name="api">The client API</param>
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

    /// <summary>
    /// Generates the client settings key for saving this player's accessories.
    /// Key is scoped to both the world/save and the player UID.
    /// </summary>
    /// <param name="capi">The client API</param>
    /// <param name="playerId">The player UID</param>
    /// <returns>The settings key</returns>
    internal static string GetClientSaveKey(ICoreClientAPI capi, string playerId)
    {
        var savegameIdentifier = capi?.World?.SavegameIdentifier;
        if (string.IsNullOrWhiteSpace(savegameIdentifier))
        {
            return $"{SaveKey}.{playerId}";
        }

        return $"{SaveKey}.{savegameIdentifier}.{playerId}";
    }

    /// <summary>
    /// Gets the client settings key for this inventory.
    /// </summary>
    /// <param name="capi">The client API</param>
    /// <returns>The settings key</returns>
    private string GetClientSaveKey(ICoreClientAPI capi)
    {
        return GetClientSaveKey(capi, playerId);
    }

    /// <summary>
    /// Serializes the entire accessory inventory to a Base64 string.
    /// Used for network transmission and persistence to both client settings and server world data.
    /// </summary>
    /// <returns>Base64-encoded representation of all 8 slots</returns>
    public string SerializeSlot()
    {
        var tree = new TreeAttribute();
        ToTreeAttributes(tree);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        tree.ToBytes(bw);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Deserializes accessory inventory from a Base64 string and updates all slots.
    /// Used when receiving network packets or loading from persistence.
    /// Optionally suppresses the OnAccessoryModified event to prevent redundant broadcasts.
    /// </summary>
    /// <param name="b64">Base64-encoded accessory data, or empty/null to clear all slots</param>
    /// <param name="capi">Optional client API for updating persistent settings</param>
    /// <param name="suppressAccessoryModifiedEvent">If true, prevents OnAccessoryModified from firing (used during network sync)</param>
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

    #endregion
}
