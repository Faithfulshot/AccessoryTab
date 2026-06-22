using HarmonyLib;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AccessoryTab;

/// <summary>
/// Main mod system for AccessoryTab - manages 8-slot accessory inventory for all players.
/// Handles networking (P2P and dedicated server), GUI integration, rendering, and persistence.
/// Uses Harmony patches to integrate with the character dialog and entity rendering system.
/// </summary>
public partial class AccessoryTabCore : ModSystem
{
    #region Static Properties and Constants

    /// <summary>
    /// Logger instance for all AccessoryTab logging.
    /// </summary>
    public static ILogger Logger { get; private set; }

    /// <summary>
    /// The mod ID registered with Vintage Story.
    /// </summary>
    public static string ModId { get; private set; }

    /// <summary>
    /// Server API - null on client-only instances, set when running as server or host.
    /// </summary>
    public static ICoreServerAPI Sapi { get; private set; }

    /// <summary>
    /// Client API - null on dedicated servers, set when running as client or host.
    /// </summary>
    public static ICoreClientAPI Capi { get; private set; }

    /// <summary>
    /// Harmony instance for applying runtime patches to Vintage Story classes.
    /// </summary>
    public static Harmony HarmonyInstance { get; private set; }

    /// <summary>
    /// Key for saving/loading accessory data in player world data (server-side persistence).
    /// </summary>
    private const string AccessorySaveKey = "accessorytab-slots";

    /// <summary>
    /// Key for tracking whether a player has seen the accessories tab (used for first-time messaging).
    /// </summary>
    private const string AccessoryTabSeenKey = "accessorytab-seen";

    /// <summary>
    /// Config file name for slot category restrictions (configurable via /accessorytab command).
    /// DEPRECATED: Use AccessoryTabConfig.json instead for full slot configuration.
    /// </summary>
    private const string SlotRulesConfigFile = "AccessoryTabSlotRules.json";

    /// <summary>
    /// Main configuration file name for slot mappings and settings.
    /// </summary>
    private const string MainConfigFile = "AccessoryTabConfig.json";

    /// <summary>
    /// Server-side registry of player accessory inventories (PlayerUID -> AccessoryInventory).
    /// Only populated on the server/host for authoritative state.
    /// </summary>
    private static readonly Dictionary<string, AccessoryInventory> ServerInventoriesByUid = new();

    /// <summary>
    /// Slot category restriction rules (SlotIndex -> Category).
    /// Configurable by server admins via /accessorytab command.
    /// DEPRECATED: Use Config.SlotMappings instead.
    /// </summary>
    private static Dictionary<int, string> SlotCategoryRules = new();

    /// <summary>
    /// Main configuration for slot mappings, enabled slots, and behavior.
    /// Loaded from AccessoryTabConfig.json.
    /// </summary>
    public static AccessoryTabConfig Config { get; private set; }

    /// <summary>
    /// Gets the category restriction for a specific accessory slot.
    /// Checks both the old SlotCategoryRules (for backwards compatibility) 
    /// and the new Config.SlotMappings system.
    /// </summary>
    /// <param name="slotId">Slot index (0-7)</param>
    /// <returns>Category restriction, or null if no restriction set</returns>
    public static string GetSlotRule(int slotId)
    {
        // Check new config first
        if (Config?.SlotMappings != null && Config.SlotMappings.TryGetValue(slotId.ToString(), out var mapping))
        {
            if (mapping.AllowedCategories != null && mapping.AllowedCategories.Count > 0)
            {
                return string.Join(",", mapping.AllowedCategories);
            }
        }

        // Fall back to old system for backwards compatibility
        return SlotCategoryRules.TryGetValue(slotId, out var value) ? value : null;
    }

    /// <summary>
    /// Gets the slot configuration for a specific slot index.
    /// </summary>
    /// <param name="slotId">Slot index (0-7)</param>
    /// <returns>SlotMapping configuration, or null if not configured</returns>
    public static SlotMapping GetSlotConfig(int slotId)
    {
        if (Config?.SlotMappings != null && Config.SlotMappings.TryGetValue(slotId.ToString(), out var mapping))
        {
            return mapping;
        }
        return null;
    }

    /// <summary>
    /// Checks if a slot is enabled in the configuration.
    /// </summary>
    /// <param name="slotId">Slot index (0-7)</param>
    /// <returns>True if enabled, false otherwise</returns>
    public static bool IsSlotEnabled(int slotId)
    {
        if (Config?.EnabledSlots != null && !Config.EnabledSlots.Contains(slotId))
        {
            return false;
        }

        var slotConfig = GetSlotConfig(slotId);
        return slotConfig?.Enabled ?? true; // Default to enabled if not configured
    }

    #endregion

    #region Mod Lifecycle

    /// <summary>
    /// Called during mod initialization on both client and server.
    /// </summary>
    /// <param name="api">The core API</param>
    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        // Only load configuration on server side - clients will receive it via network sync
        if (api.Side == EnumAppSide.Server)
        {
            LoadConfiguration(api);
        }
        else
        {
            // Client-side: Load local config as fallback for singleplayer
            // Will be overridden by server config in multiplayer
            LoadConfiguration(api);
            Logger?.Debug("[AccessoryTab] Client loaded local config (will be overridden by server in multiplayer)");
        }
    }

    /// <summary>
    /// Called very early in mod initialization, before assets are loaded.
    /// Initializes static properties and applies Harmony patches.
    /// In P2P mode, this may be called multiple times (once for client, once for server).
    /// </summary>
    /// <param name="api">The core API</param>
    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);

        // Don't overwrite already-set APIs - in P2P mode, both might be called
        if (api is ICoreServerAPI serverApi && Sapi == null)
        {
            Sapi = serverApi;
        }
        if (api is ICoreClientAPI clientApi && Capi == null)
        {
            Capi = clientApi;
        }

        Logger = Mod.Logger;
        ModId = Mod.Info.ModID;

        Patch();
    }

    #endregion

    #region Client-Side Initialization

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        Capi = api;

        if (api == null)
        {
            Logger?.Error("[AccessoryTab] StartClientSide called with null API");
            return;
        }

        // Register the client-side network channel and handler on the client API instance.
        try
        {
            if (api.Network == null)
            {
                Logger?.Error("[AccessoryTab] Client Network API is null, cannot register channel");
                return;
            }

            // Register accessory equipment sync channel
            api.Network
                .RegisterChannel("accessoryequipment")
                .RegisterMessageType(typeof(AccessoryEquipmentPacket))
                .SetMessageHandler<AccessoryEquipmentPacket>(OnAccessoryEquipmentPacketReceived);

            // Register config sync channel - receives server configuration
            api.Network
                .RegisterChannel("accessoryconfig")
                .RegisterMessageType(typeof(AccessoryConfigSyncPacket))
                .SetMessageHandler<AccessoryConfigSyncPacket>(OnConfigSyncPacketReceived);

            Logger?.Debug("[AccessoryTab] Client network channels registered successfully");
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

        if (api == null)
        {
            Logger?.Error("[AccessoryTab] StartServerSide called with null API");
            return;
        }

        // Register the server-side network channel and handler on the server API instance.
        try
        {
            if (api.Network == null)
            {
                Logger?.Error("[AccessoryTab] Server Network API is null, cannot register channel");
                return;
            }

            api.Network
                .RegisterChannel("accessoryequipment")
                .RegisterMessageType(typeof(AccessoryEquipmentPacket))
                .SetMessageHandler<AccessoryEquipmentPacket>(OnServerAccessoryEquipmentPacketReceived);

            // Register config sync channel - sends server configuration to clients
            api.Network
                .RegisterChannel("accessoryconfig")
                .RegisterMessageType(typeof(AccessoryConfigSyncPacket));

            Logger?.Debug("[AccessoryTab] Server network channels registered successfully");
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Failed to register server message handler: {ex.Message}");
        }

        // Register player events now that server subsystems (InventoryManager etc.) are ready
        Sapi.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        Sapi.Event.PlayerJoin += OnPlayerJoin; // Send config when player joins
        Sapi.Event.PlayerLeave += OnPlayerLeave;

        SlotCategoryRules = api.LoadModConfig<Dictionary<int, string>>(SlotRulesConfigFile) ?? new Dictionary<int, string>();

        RegisterAdminCommands(api);
    }

    /// <summary>
    /// Registers all /accessorytab admin commands.
    /// Commands require controlserver privilege and work from both in-game and server console.
    /// </summary>
    private static void RegisterAdminCommands(ICoreServerAPI api)
    {
        var root = api.ChatCommands
            .Create("accessorytab")
            .WithDescription("Manage AccessoryTab slot configuration")
            .RequiresPrivilege(Privilege.controlserver);

        // /accessorytab reload
        root.BeginSubCommand("reload")
            .WithDescription("Reload configuration from AccessoryTabConfig.json")
            .HandleWith(_ =>
            {
                LoadConfiguration(Sapi);
                BroadcastConfigToAllClients();
                return TextCommandResult.Success("AccessoryTab config reloaded and synced to all clients.");
            })
        .EndSubCommand();

        // /accessorytab save
        root.BeginSubCommand("save")
            .WithDescription("Save current in-memory configuration to AccessoryTabConfig.json")
            .HandleWith(_ =>
            {
                if (Config == null)
                    return TextCommandResult.Error("No config loaded.");
                Sapi.StoreModConfig(Config, MainConfigFile);
                return TextCommandResult.Success("Configuration saved to AccessoryTabConfig.json");
            })
        .EndSubCommand();

        // /accessorytab list
        root.BeginSubCommand("list")
            .WithDescription("List all slot states")
            .HandleWith(_ =>
            {
                if (Config == null)
                    return TextCommandResult.Error("No config loaded.");
                var sb = new System.Text.StringBuilder("AccessoryTab slots:\n");
                for (int i = 0; i < 8; i++)
                {
                    bool enabled = IsSlotEnabled(i);
                    var cfg = GetSlotConfig(i);
                    string name = cfg?.DisplayName ?? $"Slot {i + 1}";
                    string type = cfg?.Type.ToString() ?? "accessory";
                    sb.AppendLine($"  Slot {i}: [{(enabled ? "ON " : "OFF")}] {name} ({type})");
                }
                return TextCommandResult.Success(sb.ToString().TrimEnd());
            })
        .EndSubCommand();

        // /accessorytab slot <0-7> <action> [value]
        root.BeginSubCommand("slot")
            .WithDescription("Modify a specific slot. Usage: /accessorytab slot <0-7> <enable|disable|name|type|target|restrict|clearrestrict>")
            .WithArgs(
                api.ChatCommands.Parsers.Int("slotIndex"),
                api.ChatCommands.Parsers.Word("action"),
                api.ChatCommands.Parsers.OptionalAll("value"))
            .HandleWith(args =>
            {
                int slotIndex = (int)args.Parsers[0].GetValue();
                string action = ((string)args.Parsers[1].GetValue())?.ToLowerInvariant();
                string value  = ((string)args.Parsers[2].GetValue())?.Trim();

                if (slotIndex < 0 || slotIndex > 7)
                    return TextCommandResult.Error("Slot index must be 0-7.");
                if (string.IsNullOrWhiteSpace(action))
                    return TextCommandResult.Error("Specify an action: enable, disable, name, type, target, restrict, clearrestrict");

                // Ensure config and mapping exist
                if (Config == null) Config = AccessoryTabConfig.CreateDefault();
                Config.SlotMappings ??= new Dictionary<string, SlotMapping>();
                if (!Config.SlotMappings.ContainsKey(slotIndex.ToString()))
                    Config.SlotMappings[slotIndex.ToString()] = new SlotMapping { DisplayName = $"Accessory Slot {slotIndex + 1}" };

                var mapping = Config.SlotMappings[slotIndex.ToString()];
                string result;

                switch (action)
                {
                    case "enable":
                        mapping.Enabled = true;
                        if (!Config.EnabledSlots.Contains(slotIndex))
                            Config.EnabledSlots.Add(slotIndex);
                        result = $"Slot {slotIndex} enabled.";
                        break;

                    case "disable":
                        mapping.Enabled = false;
                        Config.EnabledSlots.Remove(slotIndex);
                        result = $"Slot {slotIndex} disabled.";
                        break;

                    case "name":
                        if (string.IsNullOrWhiteSpace(value))
                            return TextCommandResult.Error("Provide a display name.");
                        mapping.DisplayName = value;
                        result = $"Slot {slotIndex} name set to '{value}'.";
                        break;

                    case "type":
                        if (!System.Enum.TryParse<SlotType>(value, ignoreCase: true, out var slotType))
                            return TextCommandResult.Error("Valid types: accessory, vanilla, modded");
                        mapping.Type = slotType;
                        result = $"Slot {slotIndex} type set to '{slotType}'.";
                        break;

                    case "target":
                        mapping.TargetSlot = string.IsNullOrWhiteSpace(value) ? null : value;
                        result = $"Slot {slotIndex} target set to '{mapping.TargetSlot ?? "none"}'.";
                        break;

                    case "restrict":
                        if (string.IsNullOrWhiteSpace(value))
                            return TextCommandResult.Error("Provide a category name.");
                        mapping.AllowedCategories ??= new List<string>();
                        if (!mapping.AllowedCategories.Contains(value))
                            mapping.AllowedCategories.Add(value);
                        result = $"Slot {slotIndex} restriction '{value}' added.";
                        break;

                    case "clearrestrict":
                        mapping.AllowedCategories = new List<string>();
                        result = $"Slot {slotIndex} restrictions cleared.";
                        break;

                    default:
                        return TextCommandResult.Error($"Unknown action '{action}'. Use: enable, disable, name, type, target, restrict, clearrestrict");
                }

                // Save and broadcast after every successful change
                Sapi.StoreModConfig(Config, MainConfigFile);
                BroadcastConfigToAllClients();
                return TextCommandResult.Success(result + " Config saved and synced to all clients.");
            })
        .EndSubCommand();
    }

    /// <summary>
    /// Broadcasts the current server configuration to all connected clients.
    /// Called after every admin command that modifies the config.
    /// </summary>
    public static void BroadcastConfigToAllClients()
    {
        if (Config == null) return;

        var serverApi = Sapi;
        if (serverApi == null) return;

        try
        {
            string slotMappingsJson = Newtonsoft.Json.JsonConvert.SerializeObject(Config.SlotMappings ?? new Dictionary<string, SlotMapping>());
            var packet = new AccessoryConfigSyncPacket
            {
                EnabledSlots = Config.EnabledSlots ?? new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 },
                SlotMappingsJson = slotMappingsJson
            };

            var channel = serverApi.Network?.GetChannel("accessoryconfig");
            if (channel == null) return;

            channel.BroadcastPacket(packet);
            Logger?.Notification($"[AccessoryTab] Broadcasted config to all clients ({Config.EnabledSlots?.Count ?? 0} enabled slots)");
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Failed to broadcast config: {ex.Message}");
        }
    }

    /// <summary>
    /// Server-side handler for receiving accessory equipment packets from clients.
    /// Called when a client modifies their accessories in the GUI.
    /// Updates server-side inventory state and broadcasts changes to all other clients.
    /// </summary>
    /// <param name="player">The player who sent the packet</param>
    /// <param name="packet">The accessory equipment packet from the client</param>
    private void OnServerAccessoryEquipmentPacketReceived(IServerPlayer player, AccessoryEquipmentPacket packet)
    {
        if (packet == null || player == null)
        {
            Logger?.Warning("[AccessoryTab] Server received invalid packet");
            return;
        }

        try
        {
            Logger?.Debug($"[AccessoryTab] Server received packet from {player.PlayerName} (UID: {player.PlayerUID})");

            // Update server inventory if we have it
            if (ServerInventoriesByUid.TryGetValue(player.PlayerUID, out var inv))
            {
                Logger?.Debug($"[AccessoryTab] Updating existing inventory for {player.PlayerName}");
                inv.SetSlotFromSerialized(packet.Base64SlotData, suppressAccessoryModifiedEvent: true);

                OnServerInventoryModified(player, inv);
            }
            else
            {
                Logger?.Debug($"[AccessoryTab] Creating new inventory for {player.PlayerName}");
                var tempInv = new AccessoryInventory(player.PlayerUID, Sapi);
                tempInv.OnAccessoryModified += (s, e) => OnServerInventoryModified(player, tempInv);
                tempInv.SetSlotFromSerialized(packet.Base64SlotData, suppressAccessoryModifiedEvent: true);
                ServerInventoriesByUid[player.PlayerUID] = tempInv;

                OnServerInventoryModified(player, tempInv);
            }
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Server error handling packet from {player.PlayerName}: {ex.Message}");
            Logger?.Debug($"[AccessoryTab] Stack trace: {ex.StackTrace}");
        }
    }


    /// <summary>
    /// Client-side handler for receiving accessory equipment packets from the server.
    /// Called when any player's accessories change, including the local player (as server echo).
    /// Handles both P2P and dedicated server packet reception.
    /// </summary>
    /// <param name="packet">The accessory equipment packet from the server</param>
    private static void OnAccessoryEquipmentPacketReceived(AccessoryEquipmentPacket packet)
    {
        if (packet == null || string.IsNullOrWhiteSpace(packet.PlayerUID))
        {
            Logger?.Warning("[AccessoryTab] Received invalid packet on client");
            return;
        }

        try
        {
            // Check if this is the local player
            var isLocalPlayer = Capi?.World?.Player?.PlayerUID == packet.PlayerUID;
            var playerType = isLocalPlayer ? "local player" : "remote player";

            Logger?.Debug($"[AccessoryTab] Client received packet for {playerType} (UID: {packet.PlayerUID})");

            if (string.IsNullOrWhiteSpace(packet.Base64SlotData))
            {
                Logger?.Debug($"[AccessoryTab] Clearing accessories for {playerType}");

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
                Logger?.Debug($"[AccessoryTab] Updating accessories for {playerType} ({packet.Base64SlotData.Length} bytes)");

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

            Logger?.Debug($"[AccessoryTab] Queued visual refresh for {playerType}");
        }
        catch (Exception ex)
        {
            Logger?.Debug($"[AccessoryTab] Client packet error for {packet.PlayerUID}: {ex.Message}");
            Logger?.Debug($"[AccessoryTab] Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Client-side handler for receiving configuration sync packets from the server.
    /// Called when joining a server - overrides local config with server's authoritative config.
    /// </summary>
    /// <param name="packet">The configuration sync packet from the server</param>
    private static void OnConfigSyncPacketReceived(AccessoryConfigSyncPacket packet)
    {
        if (packet == null)
        {
            Logger?.Warning("[AccessoryTab] Received null config sync packet");
            return;
        }

        try
        {
            Logger?.Notification("[AccessoryTab] Received server configuration, applying...");

            // Parse slot mappings from JSON
            var slotMappings = new Dictionary<string, SlotMapping>();
            if (!string.IsNullOrWhiteSpace(packet.SlotMappingsJson))
            {
                try
                {
                    slotMappings = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, SlotMapping>>(packet.SlotMappingsJson);
                }
                catch (Exception ex)
                {
                    Logger?.Error($"[AccessoryTab] Failed to deserialize slot mappings: {ex.Message}");
                }
            }

            // Create new config from server data
            Config = new AccessoryTabConfig
            {
                EnabledSlots = packet.EnabledSlots ?? new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 },
                SlotMappings = slotMappings
            };

            Logger?.Notification($"[AccessoryTab] Applied server config: {Config.EnabledSlots.Count} slots enabled");

            // Log enabled slots for debugging
            foreach (var slotIdx in Config.EnabledSlots)
            {
                if (Config.SlotMappings.TryGetValue(slotIdx.ToString(), out var mapping))
                {
                    Logger?.Debug($"[AccessoryTab]   Slot {slotIdx}: {mapping.DisplayName} ({mapping.Type})");
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Error applying server config: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates an AccessoryEquipmentPacket to ensure it contains valid data before broadcasting.
    /// Prevents crashes from malformed packets, which is critical for servers with 60+ players.
    /// </summary>
    /// <param name="packet">The packet to validate</param>
    /// <returns>True if packet is valid, false otherwise</returns>
    private static bool ValidatePacket(AccessoryEquipmentPacket packet)
    {
        if (packet == null)
        {
            Logger?.Error("[AccessoryTab] Packet validation failed: null packet");
            return false;
        }

        if (string.IsNullOrWhiteSpace(packet.PlayerUID))
        {
            Logger?.Error("[AccessoryTab] Packet validation failed: PlayerUID is null or empty");
            return false;
        }

        // Base64SlotData can be empty (when clearing accessories), but should not be null
        if (packet.Base64SlotData == null)
        {
            Logger?.Warning("[AccessoryTab] Packet has null Base64SlotData, converting to empty string");
            packet.Base64SlotData = "";
        }

        // Optional: Validate Base64 format if data is present
        if (!string.IsNullOrEmpty(packet.Base64SlotData))
        {
            try
            {
                // Quick validation: attempt to decode base64 (doesn't fully deserialize)
                Convert.FromBase64String(packet.Base64SlotData);
            }
            catch (FormatException)
            {
                Logger?.Error($"[AccessoryTab] Packet validation failed: Invalid Base64 format for player {packet.PlayerUID}");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Broadcasts accessory equipment changes to all connected clients.
    /// Uses BroadcastPacket for optimal performance in both P2P and dedicated server scenarios.
    /// This method is called whenever a player's accessories change to synchronize visual state across all clients.
    /// </summary>
    /// <param name="forPlayer">The player whose accessories changed</param>
    /// <param name="base64SlotData">Serialized accessory inventory data (empty string to clear)</param>
    public static void BroadcastAccessoryEquipment(IServerPlayer forPlayer, string base64SlotData)
    {
        // In P2P mode, Sapi might not be set, so try to get the server API from the player's entity
        var serverApi = Sapi ?? (forPlayer?.Entity?.Api as ICoreServerAPI);

        if (serverApi == null || forPlayer == null)
        {
            Logger?.Warning($"[AccessoryTab] Cannot broadcast: Server API is null (Sapi={Sapi != null}, forPlayer={forPlayer != null}, Entity.Api={forPlayer?.Entity?.Api != null})");
            return;
        }

        try
        {
            var packet = new AccessoryEquipmentPacket
            {
                PlayerUID = forPlayer.PlayerUID,
                Base64SlotData = base64SlotData ?? ""
            };

            // Validate packet before broadcasting
            if (!ValidatePacket(packet))
            {
                Logger?.Error($"[AccessoryTab] Invalid packet for player {forPlayer.PlayerName}, skipping broadcast");
                return;
            }

            // Use BroadcastPacket instead of SendPacket for better P2P and performance
            // This works correctly in both dedicated servers and peer-to-peer singleplayer worlds
            var channel = serverApi.Network?.GetChannel("accessoryequipment");
            if (channel == null)
            {
                Logger?.Error("[AccessoryTab] Network channel 'accessoryequipment' not found");
                return;
            }

            channel.BroadcastPacket(packet);

#if DEBUG
            Logger?.Debug($"[AccessoryTab] Broadcasted accessories for {forPlayer.PlayerName} (UID: {forPlayer.PlayerUID})");
#endif
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Broadcast error for player {forPlayer?.PlayerName}: {ex.Message}");
            Logger?.Debug($"[AccessoryTab] Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Called when a player joins the server (before they're fully loaded).
    /// Sends the server's configuration to the client to ensure they use the authoritative server config.
    /// </summary>
    /// <param name="byPlayer">The player joining</param>
    private static void OnPlayerJoin(IServerPlayer byPlayer)
    {
        if (byPlayer == null || Config == null)
        {
            return;
        }

        try
        {
            Logger?.Debug($"[AccessoryTab] Sending server config to {byPlayer.PlayerName}");

            // Serialize slot mappings to JSON
            string slotMappingsJson = "";
            if (Config.SlotMappings != null)
            {
                slotMappingsJson = Newtonsoft.Json.JsonConvert.SerializeObject(Config.SlotMappings);
            }

            var packet = new AccessoryConfigSyncPacket
            {
                EnabledSlots = Config.EnabledSlots ?? new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 },
                SlotMappingsJson = slotMappingsJson
            };

            // Get server API (might be from Sapi or player entity in P2P)
            var serverApi = Sapi ?? (byPlayer.Entity?.Api as ICoreServerAPI);
            if (serverApi == null)
            {
                Logger?.Warning("[AccessoryTab] Cannot send config: Server API not available");
                return;
            }

            var channel = serverApi.Network?.GetChannel("accessoryconfig");
            if (channel != null)
            {
                channel.SendPacket(packet, byPlayer);
                Logger?.Notification($"[AccessoryTab] Sent server config to {byPlayer.PlayerName}");
            }
            else
            {
                Logger?.Error("[AccessoryTab] Config sync channel not found");
            }
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Error sending config to {byPlayer.PlayerName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when a player joins and is ready to play.
    /// Initializes or restores the player's accessory inventory and broadcasts their state to all clients.
    /// Works for both P2P and dedicated server scenarios.
    /// </summary>
    /// <param name="byPlayer">The player joining</param>
    private static void OnPlayerNowPlaying(IServerPlayer byPlayer)
    {
        if (byPlayer == null)
        {
            Logger?.Warning("[AccessoryTab] OnPlayerNowPlaying called with null player");
            return;
        }

        Logger?.Debug($"[AccessoryTab] Player {byPlayer.PlayerName} now playing, initializing accessories");

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
                Logger?.Error($"[AccessoryTab] Cannot initialize inventory for {byPlayer.PlayerName}: No valid API");
                return;
            }

            Logger?.Debug($"[AccessoryTab] Creating new accessory inventory for {byPlayer.PlayerName}");
            inv = new AccessoryInventory(byPlayer.PlayerUID, apiToUse);
            ServerInventoriesByUid[byPlayer.PlayerUID] = inv;

            // Hook into inventory modification events to handle broadcasting and persistence
            inv.OnAccessoryModified += (s, e) => OnServerInventoryModified(byPlayer, inv);
        }

        string saved = byPlayer.WorldData.GetModData<string>(AccessorySaveKey, "");
        if (!string.IsNullOrWhiteSpace(saved))
        {
            Logger?.Debug($"[AccessoryTab] Restoring saved accessories for {byPlayer.PlayerName}");
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

        // Broadcast equipment state to all clients (works in both P2P and dedicated server)
        Logger?.Debug($"[AccessoryTab] Broadcasting initial state for {byPlayer.PlayerName}");
        BroadcastAccessoryEquipment(byPlayer, inv.SerializeSlot() ?? "");
    }

    /// <summary>
    /// Called when a player leaves the server.
    /// Saves their accessory inventory to persistent storage and broadcasts removal to all clients.
    /// </summary>
    /// <param name="byPlayer">The player leaving</param>
    private static void OnPlayerLeave(IServerPlayer byPlayer)
    {
        if (byPlayer == null)
        {
            Logger?.Warning("[AccessoryTab] OnPlayerLeave called with null player");
            return;
        }

        Logger?.Debug($"[AccessoryTab] Player {byPlayer.PlayerName} leaving, saving accessories");

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

    #endregion

    #region Configuration Management

    /// <summary>
    /// Loads the accessory tab configuration from AccessoryTabConfig.json.
    /// Creates a default configuration file if one doesn't exist.
    /// </summary>
    /// <param name="api">The core API</param>
    private static void LoadConfiguration(ICoreAPI api)
    {
        try
        {
            Config = api.LoadModConfig<AccessoryTabConfig>(MainConfigFile);

            if (Config == null)
            {
                Logger?.Notification("[AccessoryTab] No configuration found, creating default AccessoryTabConfig.json");
                Config = AccessoryTabConfig.CreateDefault();
                api.StoreModConfig(Config, MainConfigFile);
            }
            else
            {
                Logger?.Notification($"[AccessoryTab] Loaded configuration: {Config.EnabledSlots.Count} slots enabled");
            }

            // Validate configuration
            if (Config.SlotMappings == null)
            {
                Config.SlotMappings = new Dictionary<string, SlotMapping>();
            }

            if (Config.EnabledSlots == null)
            {
                Config.EnabledSlots = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            }

            // Log slot configuration for debugging
            foreach (var kvp in Config.SlotMappings)
            {
                var slotIndex = kvp.Key;
                var mapping = kvp.Value;

                if (mapping.Enabled)
                {
                    Logger?.Debug($"[AccessoryTab] Slot {slotIndex}: {mapping.DisplayName} (Type: {mapping.Type}, Target: {mapping.TargetSlot ?? "none"})");
                }
                else
                {
                    Logger?.Debug($"[AccessoryTab] Slot {slotIndex}: DISABLED");
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.Error($"[AccessoryTab] Failed to load configuration: {ex.Message}");
            Logger?.Warning("[AccessoryTab] Using default configuration");
            Config = AccessoryTabConfig.CreateDefault();
        }
    }

    #endregion

    #region Harmony Patching

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

    #endregion

    #region Cleanup

    public override void Dispose()
    {
        if (Sapi != null)
        {
            Sapi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
            Sapi.Event.PlayerJoin -= OnPlayerJoin;
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

    #endregion
}
