using HarmonyLib;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AccessoryTab;

public partial class AccessoryTabCore : ModSystem
{
    public static ILogger Logger { get; private set; }
    public static string ModId { get; private set; }
    public static ICoreServerAPI Sapi { get; private set; }
    public static ICoreClientAPI Capi { get; private set; }
    public static Harmony HarmonyInstance { get; private set; }

    private const string AccessorySaveKey = "accessorytab-slots";
    private const string AccessoryTabSeenKey = "accessorytab-seen";
    private const string SlotRulesConfigFile = "AccessoryTabSlotRules.json";

    private static readonly Dictionary<string, AccessoryInventory> ServerInventoriesByUid = new();
    private static Dictionary<int, string> SlotCategoryRules = new();

    public static string GetSlotRule(int slotId)
    {
        return SlotCategoryRules.TryGetValue(slotId, out var value) ? value : null;
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
    }

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        Sapi = api as ICoreServerAPI;
        Capi = api as ICoreClientAPI;
        Logger = Mod.Logger;
        ModId = Mod.Info.ModID;

        Patch();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        Capi = api;

        // Register the client-side network channel and handler on the client API instance.
        try
        {
            api.Network
                .RegisterChannel("accessoryequipment")
                .RegisterMessageType(typeof(AccessoryEquipmentPacket))
                .SetMessageHandler<AccessoryEquipmentPacket>(OnAccessoryEquipmentPacketReceived);
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Failed to register client message handler: {ex.Message}");
        }

        // Register client tick to keep local inventory synchronized for rendering
        // This ensures accessories display on the local player even before the GUI is opened
        api.Event.RegisterGameTickListener((dt) =>
        {
            if (Capi?.World?.Player == null) return;
            try
            {
                AccessoryTabPatches.EnsureLocalInventory(Capi);
            }
            catch (Exception ex)
            {
                Logger?.Error($"[AccessoryTab] Client tick error: {ex.Message}");
            }
        }, 100);

        // Register a tick listener to handle visual refreshes for remote players
        api.Event.RegisterGameTickListener(OnClientVisualRefreshTick, 50);
    }

    private void OnClientVisualRefreshTick(float dt)
    {
        if (Capi == null || Capi.World?.AllPlayers == null) return;

        if (!AccessoryTabPatches.PendingVisualRefresh) return;

        try
        {
            // Trigger re-tesselation of entities that have updated accessories
            foreach (var playerUid in AccessoryTabPatches.PendingAccessoryRefreshPlayerUids.ToList())
            {
                var player = Capi.World.AllPlayers.FirstOrDefault(p => p.PlayerUID == playerUid);
                if (player?.Entity != null)
                {
                    player.Entity.MarkShapeModified();
                }
            }

            AccessoryTabPatches.PendingAccessoryRefreshPlayerUids.Clear();
            AccessoryTabPatches.PendingVisualRefresh = false;
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Visual refresh error: {ex.Message}");
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        Sapi = api;

        // Register the server-side network channel and handler on the server API instance.
        try
        {
            Sapi.Network
                .RegisterChannel("accessoryequipment")
                .RegisterMessageType(typeof(AccessoryEquipmentPacket))
                .SetMessageHandler<AccessoryEquipmentPacket>(OnServerAccessoryEquipmentPacketReceived);
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Failed to register server message handler: {ex.Message}");
        }

        // Register player events now that server subsystems (InventoryManager etc.) are ready
        Sapi.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        Sapi.Event.PlayerLeave += OnPlayerLeave;

        SlotCategoryRules = api.LoadModConfig<Dictionary<int, string>>(SlotRulesConfigFile) ?? new Dictionary<int, string>();

        api.ChatCommands
            .Create("accessorytab")
            .WithDescription("Configure accessory slot category restrictions")
            .WithAdditionalInformation("Usage: /accessorytab slotN <category|clear>")
            .RequiresPrivilege(Privilege.controlserver)
            .HandleWith(args => OnAccessoryTabCommand(args));
    }

    private void OnServerAccessoryEquipmentPacketReceived(IServerPlayer player, AccessoryEquipmentPacket packet)
    {
        if (packet == null || player == null)
        {
            return;
        }

        try
        {
            // Update server inventory if we have it
            if (ServerInventoriesByUid.TryGetValue(player.PlayerUID, out var inv))
            {
                inv.SetSlotFromSerialized(packet.Base64SlotData, suppressAccessoryModifiedEvent: true);

                OnServerInventoryModified(player, inv);
            }
            else
            {
                var tempInv = new AccessoryInventory(player.PlayerUID, Sapi);
                tempInv.OnAccessoryModified += (s, e) => OnServerInventoryModified(player, tempInv);
                tempInv.SetSlotFromSerialized(packet.Base64SlotData, suppressAccessoryModifiedEvent: true);
                ServerInventoriesByUid[player.PlayerUID] = tempInv;

                OnServerInventoryModified(player, tempInv);
            }
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Error handling accessory packet: {ex.Message}");
        }
    }


    private static void OnAccessoryEquipmentPacketReceived(AccessoryEquipmentPacket packet)
    {
        if (packet == null || string.IsNullOrWhiteSpace(packet.PlayerUID))
        {
            return;
        }

        try
        {
            // Check if this is the local player
            var isLocalPlayer = Capi?.World?.Player?.PlayerUID == packet.PlayerUID;

            if (string.IsNullOrWhiteSpace(packet.Base64SlotData))
            {
                AccessoryTabPatches.OtherPlayersAccessories.Remove(packet.PlayerUID);
                AccessoryTabPatches.RemoveRemoteAccessoryCache(packet.PlayerUID);

                // For the local player, do not rewrite the live inventory from the server echo.
                // Only keep the persisted client copy in sync to avoid double-applying GUI actions.
                if (isLocalPlayer)
                {
                    if (Capi?.Settings?.String != null)
                    {
                        Capi.Settings.String[AccessoryInventory.GetClientSaveKey(Capi, packet.PlayerUID)] = "";
                    }
                }
            }
            else
            {
                AccessoryTabPatches.OtherPlayersAccessories[packet.PlayerUID] = packet.Base64SlotData;
                AccessoryTabPatches.UpdateRemoteAccessoryCache(packet.PlayerUID, packet.Base64SlotData, Capi);

                // For the local player, do not rewrite the live inventory from the server echo.
                // Only keep the persisted client copy in sync to avoid double-applying GUI actions.
                if (isLocalPlayer)
                {
                    if (Capi?.Settings?.String != null)
                    {
                        Capi.Settings.String[AccessoryInventory.GetClientSaveKey(Capi, packet.PlayerUID)] = packet.Base64SlotData ?? "";
                    }
                }
            }

            // Queue visual refresh to re-render all entities
            AccessoryTabPatches.PendingVisualRefresh = true;
            AccessoryTabPatches.PendingAccessoryRefreshPlayerUids.Add(packet.PlayerUID);
        }
        catch (Exception ex)
        {
            Logger?.Error("[AccessoryTab] Packet error: " + ex.Message);
        }
    }

    public static void BroadcastAccessoryEquipment(IServerPlayer forPlayer, string base64SlotData)
    {
        if (Sapi == null || forPlayer == null)
        {
            return;
        }

        try
        {
            var packet = new AccessoryEquipmentPacket
            {
                PlayerUID = forPlayer.PlayerUID,
                Base64SlotData = base64SlotData ?? ""
            };

            var recipients = Sapi.World?.AllOnlinePlayers?
                .OfType<IServerPlayer>()
                .ToArray();

            if (recipients == null || recipients.Length == 0)
            {
                return;
            }

            Sapi.Network.GetChannel("accessoryequipment").SendPacket(packet, recipients);
        }
        catch (Exception ex)
        {
            Logger?.Error("[AccessoryTab] Broadcast error: " + ex.Message);
        }
    }

    private static TextCommandResult OnAccessoryTabCommand(TextCommandCallingArgs callArgs)
    {
        string slotToken = callArgs.RawArgs.PopWord();
        string category = callArgs.RawArgs.PopWord();

        if (string.IsNullOrWhiteSpace(slotToken) || string.IsNullOrWhiteSpace(category))
        {
            return TextCommandResult.Error("Usage: /accessorytab slotN <category|clear>");
        }

        if (!slotToken.StartsWith("slot", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(slotToken.Substring(4), out int slotIndex))
        {
            return TextCommandResult.Error("Invalid slot format. Example: slot4");
        }

        slotIndex -= 1;
        if (slotIndex < 0 || slotIndex >= 8)
        {
            return TextCommandResult.Error("Slot must be between slot1 and slot8");
        }

        if (string.Equals(category, "clear", StringComparison.OrdinalIgnoreCase))
        {
            SlotCategoryRules.Remove(slotIndex);
            Sapi.StoreModConfig(SlotCategoryRules, SlotRulesConfigFile);
            return TextCommandResult.Success($"Cleared restriction for slot{slotIndex + 1}");
        }

        SlotCategoryRules[slotIndex] = category.ToLowerInvariant();
        Sapi.StoreModConfig(SlotCategoryRules, SlotRulesConfigFile);
        return TextCommandResult.Success($"Set slot{slotIndex + 1} restriction to '{category.ToLowerInvariant()}'");
    }

    private static void OnPlayerNowPlaying(IServerPlayer byPlayer)
    {
        if (byPlayer == null) return;

        bool hasSeenTab = byPlayer.WorldData.GetModData<bool>(AccessoryTabSeenKey, false);
        if (!hasSeenTab)
        {
            byPlayer.WorldData.SetModData(AccessoryTabSeenKey, true);
        }

        if (!ServerInventoriesByUid.TryGetValue(byPlayer.PlayerUID, out var inv))
        {
            // Ensure we have a valid API reference before creating the inventory
            ICoreAPI apiToUse = Sapi ?? byPlayer.Entity?.Api;
            if (apiToUse == null)
            {
                return;
            }

            inv = new AccessoryInventory(byPlayer.PlayerUID, apiToUse);
            ServerInventoriesByUid[byPlayer.PlayerUID] = inv;

            // Hook into inventory modification events to handle broadcasting and persistence
            inv.OnAccessoryModified += (s, e) => OnServerInventoryModified(byPlayer, inv);
        }

        string saved = byPlayer.WorldData.GetModData<string>(AccessorySaveKey, "");
        if (!string.IsNullOrWhiteSpace(saved))
        {
            inv.SetSlotFromSerialized(saved, null);
        }

        try
        {
            if (byPlayer.InventoryManager == null)
            {
                return;
            }

            byPlayer.InventoryManager.OpenInventory(inv);
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Failed to open inventory for {byPlayer.PlayerName}: {ex.Message}");
            return;
        }

        // Broadcast equipment state to all clients
        BroadcastAccessoryEquipment(byPlayer, inv.SerializeSlot() ?? "");
    }

    private static void OnPlayerLeave(IServerPlayer byPlayer)
    {
        if (byPlayer == null) return;

        if (ServerInventoriesByUid.TryGetValue(byPlayer.PlayerUID, out var inv))
        {
            try
            {
                byPlayer.WorldData.SetModData(AccessorySaveKey, inv.SerializeSlot() ?? "");
                if (byPlayer.InventoryManager != null)
                {
                    byPlayer.InventoryManager.CloseInventory(inv);
                }
            }
            catch (Exception ex)
            {
                Logger?.Error($"[AccessoryTab] Error closing inventory for {byPlayer.PlayerName}: {ex.Message}");
            }
            finally
            {
                ServerInventoriesByUid.Remove(byPlayer.PlayerUID);
            }

            // Tell clients to clear this player's cached accessory state
            BroadcastAccessoryEquipment(byPlayer, "");
        }
    }

    /// <summary>
    /// Handles inventory modification events from AccessoryInventory.
    /// Manages broadcasting and persistence when accessories change.
    /// </summary>
    private static void OnServerInventoryModified(IServerPlayer player, AccessoryInventory inventory)
    {
        if (player == null || inventory == null) return;

        try
        {
            var serialized = inventory.SerializeSlot();

            // Persist to server world data
            player.WorldData.SetModData("accessorytab-slots", serialized ?? "");

            // Broadcast to all clients
            BroadcastAccessoryEquipment(player, serialized);
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Error handling inventory modification for {player.PlayerName}: {ex.Message}");
        }
    }

    public static void Patch()
    {
        if (HarmonyInstance != null) return;

        HarmonyInstance = new Harmony(ModId);

        try
        {
            HarmonyInstance.PatchAll(typeof(AccessoryTabPatches).Assembly);
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Harmony patching failed: {ex.Message}");
        }
    }

    public static void Unpatch()
    {
        HarmonyInstance?.UnpatchAll();
        HarmonyInstance = null;
    }

    public override void Dispose()
    {
        if (Sapi != null)
        {
            Sapi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
            Sapi.Event.PlayerLeave -= OnPlayerLeave;
        }

        ServerInventoriesByUid.Clear();

        Unpatch();
        Logger = null;
        ModId = null;
        Sapi = null;
        Capi = null;
        base.Dispose();
    }
}
