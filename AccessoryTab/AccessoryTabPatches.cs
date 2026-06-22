using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace AccessoryTab;

public static class AccessoryTabPatches
{
    public const string AccessoryTabPatchCategory = "AccessoryTabPatches";

    public static AccessoryInventory AccessoryInventory { get; set; }
    public static ICoreClientAPI ClientApi { get; set; }
    public static bool PendingVisualRefresh { get; set; }
    public static ElementBounds AccessoryInsetBounds { get; set; }
    
    // Storage for other players' equipped accessories (PlayerUID -> Base64SlotData)
    public static Dictionary<string, string> OtherPlayersAccessories { get; } = new();
    public static Dictionary<long, string> OtherPlayersAccessoriesByEntityId { get; } = new();
    public static Dictionary<string, long> OtherPlayerEntityIdsByUid { get; } = new();

    // PlayerUIDs that should be re-tesselated on the client due to equipment changes.
    public static HashSet<string> PendingAccessoryRefreshPlayerUids { get; } = new();

    public static AccessoryInventory EnsureLocalInventory(ICoreClientAPI capi)
    {
        var uid = capi?.World?.Player?.PlayerUID;
        if (string.IsNullOrWhiteSpace(uid))
        {
            return null;
        }

        if (AccessoryInventory == null || !string.Equals(AccessoryInventory.PlayerId, uid, StringComparison.Ordinal))
        {
            if (AccessoryInventory != null)
            {
                capi.World?.Player?.InventoryManager?.CloseInventory(AccessoryInventory);
            }

            AccessoryInventory = new AccessoryInventory(uid, capi);
        }

        AccessoryInventory.EnsureLoaded(capi);
        capi.World?.Player?.InventoryManager?.OpenInventory(AccessoryInventory);
        return AccessoryInventory;
    }

    public static bool TryGetPlayerUidForEntity(ICoreClientAPI capi, Entity entity, out string playerUid)
    {
        playerUid = null;

        if (capi?.World == null || entity == null) return false;

        if (capi.World.Player?.Entity?.EntityId == entity.EntityId)
        {
            playerUid = capi.World.Player?.PlayerUID;
            return !string.IsNullOrWhiteSpace(playerUid);
        }

        var players = capi.World.AllPlayers;
        if (players == null) return false;

        var match = players.FirstOrDefault(p => p.Entity?.EntityId == entity.EntityId);
        playerUid = match?.PlayerUID;
        return !string.IsNullOrWhiteSpace(playerUid);
    }

    public static void UpdateRemoteAccessoryCache(string playerUid, string base64SlotData, ICoreClientAPI capi)
    {
        if (string.IsNullOrWhiteSpace(playerUid) || string.IsNullOrWhiteSpace(base64SlotData) || capi?.World?.AllPlayers == null) return;

        var player = capi.World.AllPlayers.FirstOrDefault(p => p.PlayerUID == playerUid);
        var entityId = player?.Entity?.EntityId;
        if (entityId != null)
        {
            OtherPlayerEntityIdsByUid[playerUid] = entityId.Value;
            OtherPlayersAccessoriesByEntityId[entityId.Value] = base64SlotData;
        }
    }

    public static void RemoveRemoteAccessoryCache(string playerUid)
    {
        if (string.IsNullOrWhiteSpace(playerUid)) return;

        if (OtherPlayerEntityIdsByUid.TryGetValue(playerUid, out var entityId))
        {
            OtherPlayersAccessoriesByEntityId.Remove(entityId);
            OtherPlayerEntityIdsByUid.Remove(playerUid);
        }
    }

    public static bool TryGetAccessoryDataForEntity(ICoreClientAPI capi, Entity entity, out string base64SlotData)
    {
        base64SlotData = null;

        if (entity == null) return false;

        if (OtherPlayersAccessoriesByEntityId.TryGetValue(entity.EntityId, out base64SlotData))
        {
            return !string.IsNullOrWhiteSpace(base64SlotData);
        }

        if (!TryGetPlayerUidForEntity(capi, entity, out var playerUid)) return false;
        if (!OtherPlayersAccessories.TryGetValue(playerUid, out base64SlotData) || string.IsNullOrWhiteSpace(base64SlotData)) return false;

        OtherPlayerEntityIdsByUid[playerUid] = entity.EntityId;
        OtherPlayersAccessoriesByEntityId[entity.EntityId] = base64SlotData;
        return true;
    }
}

[HarmonyPatch(typeof(GuiDialogCharacter), "OnGuiOpened")]
[HarmonyPatchCategory(AccessoryTabPatches.AccessoryTabPatchCategory)]
public static class AccessoryTabGuiPatch
{
    private static readonly FieldInfo CapiField = AccessTools.Field(typeof(GuiDialog), "capi");

    [HarmonyPrefix]
    public static void PrefixOnGuiOpened(GuiDialogCharacter __instance)
    {
        try
        {
            AccessoryTabPatches.ClientApi = CapiField?.GetValue(__instance) as ICoreClientAPI ?? AccessoryTabPatches.ClientApi;

            var dialog = __instance as GuiDialogCharacterBase;
            if (dialog == null)
            {
                return;
            }

            var capi = AccessoryTabCore.Capi ?? AccessoryTabPatches.ClientApi;
            if (capi != null)
            {
                AccessoryTabPatches.EnsureLocalInventory(capi);
            }
            else
            {
                return;
            }

            bool tabExists = dialog.Tabs.Exists(tab => tab.Name == "Accessories");
            if (tabExists)
            {
                return;
            }

            int tabIndex = dialog.Tabs.Count;
            dialog.Tabs.Add(new GuiTab()
            {
                Name = "Accessories",
                DataInt = tabIndex
            });

            dialog.RenderTabHandlers.Add(ComposeAccessoriesTab);
        }
        catch (Exception ex)
        {
            AccessoryTabCore.Logger?.Error("[AccessoryTab] GUI patch error: " + ex.Message);
        }
    }

    private static void ComposeAccessoriesTab(GuiComposer composer)
    {
        try
        {
            var capi = AccessoryTabCore.Capi ?? AccessoryTabPatches.ClientApi;
            if (capi == null)
            {
                composer.AddStaticText("Waiting for client api...", CairoFont.WhiteSmallText(), ElementBounds.Fixed(10, 40, 220, 20));
                return;
            }

            var inv = AccessoryTabPatches.EnsureLocalInventory(capi);
            if (inv == null)
            {
                composer.AddStaticText("Waiting for player uid...", CairoFont.WhiteSmallText(), ElementBounds.Fixed(10, 40, 220, 20));
                return;
            }

            // Validate that all slots exist before composing the GUI
            for (int i = 0; i < 8; i++)
            {
                if (inv[i] == null)
                {
                    composer.AddStaticText("Inventory slots not initialized...", CairoFont.WhiteSmallText(), ElementBounds.Fixed(10, 40, 220, 20));
                    return;
                }
            }

            double unscaledSlotPadding = GuiElementItemSlotGridBase.unscaledSlotPadding;

            ElementBounds slotTemplate = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + unscaledSlotPadding, 1, 1).FixedGrow(0, unscaledSlotPadding);
            double slotW = slotTemplate.fixedWidth;
            double slotH = slotTemplate.fixedHeight;

            // Keep model inset in the familiar position (left/center area)
            ElementBounds leftPattern = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + unscaledSlotPadding, 1, 6).FixedGrow(0, unscaledSlotPadding);
            ElementBounds insetBounds = ElementBounds.Fixed(0, 22 + unscaledSlotPadding, 190, leftPattern.fixedHeight - 2 * unscaledSlotPadding - 4);
            AccessoryTabPatches.AccessoryInsetBounds = insetBounds;

            // Two columns of 4 slots on the RIGHT side of model window
            double col1X = insetBounds.fixedX + insetBounds.fixedWidth + 14;
            double col2X = col1X + slotW + 14;

            double topMargin = 8;
            double bottomMargin = 8;
            double topY = insetBounds.fixedY + topMargin;
            double bottomY = insetBounds.fixedY + insetBounds.fixedHeight - bottomMargin - slotH;
            double step = (bottomY - topY) / 3.0;

            // Slot positions: [slotIndex, x, y]
            var slotPositions = new (int idx, double x, double y)[]
            {
                (0, col1X, topY),
                (1, col1X, topY + step),
                (2, col1X, topY + step * 2),
                (3, col1X, bottomY),
                (4, col2X, topY),
                (5, col2X, topY + step),
                (6, col2X, topY + step * 2),
                (7, col2X, bottomY),
            };

            // Semi-transparent dark overlay color for disabled slots
            var disabledColor = new double[] { 0, 0, 0, 0.65 };

            ElementBounds labelBounds = ElementBounds.Fixed(insetBounds.fixedX + 6, insetBounds.fixedY + 2, 140, 20);
            composer.AddStaticText("Accessories", CairoFont.WhiteSmallText(), labelBounds);

            foreach (var (idx, x, y) in slotPositions)
            {
                var bounds = ElementBounds.Fixed(x, y, slotW, slotH);
                bool enabled = AccessoryTabCore.IsSlotEnabled(idx);

                // Always render the slot grid so the slot background is visible
                composer.AddItemSlotGrid(inv,  SendPacketHandler  ,1, new[] { idx }, bounds, $"accSlot{idx}");

                // Overlay a dark transparent rectangle on disabled slots
                if (!enabled)
                {
                    // Shrink inward slightly so the overlay sits neatly inside the slot border
                    var overlayBounds = ElementBounds.Fixed(x + 2, y + 2, slotW - 4, slotH - 4);
                    double[] dc = disabledColor;
                }
            }

            composer.AddInset(insetBounds, 0);
        }
        catch (Exception ex)
        {
            AccessoryTabCore.Logger?.Error("[AccessoryTab] Tab composition error: " + ex.Message);
        }
    }

    /// <summary>
    /// Handles sending client packets to the server when accessories are modified in the GUI.
    /// Used by the item slot grid in the character dialog's accessories tab.
    /// </summary>
    /// <param name="packet">The packet to send to the server</param>
    
    private static void SendPacketHandler(object packet)
    {
        try
        {
            if (packet == null)
            {
                AccessoryTabCore.Logger?.Warning("[AccessoryTab] SendPacketHandler received null packet");
                return;
            }

            var capi = AccessoryTabCore.Capi ?? AccessoryTabPatches.ClientApi;
            if (capi == null)
            {
                AccessoryTabCore.Logger?.Error("[AccessoryTab] Cannot send packet: Client API is null");
                return;
            }

            if (capi.Network == null)
            {
                AccessoryTabCore.Logger?.Error("[AccessoryTab] Cannot send packet: Network API is null");
                return;
            }

            capi.Network.SendPacketClient(packet);

            AccessoryTabCore.Logger?.Debug("[AccessoryTab] Sent packet to server from client");
        }
        catch (Exception ex)
        {
            AccessoryTabCore.Logger?.Error("[AccessoryTab] Packet send error: " + ex.Message);
        }
    }
    
}

[HarmonyPatch(typeof(GuiDialogCharacter), "OnRenderGUI")]
[HarmonyPatchCategory(AccessoryTabPatches.AccessoryTabPatchCategory)]
public static class AccessoryGuiRenderShimPatch
{
    private static readonly FieldInfo CurTabField = AccessTools.Field(typeof(GuiDialogCharacter), "curTab");
    private static readonly FieldInfo InsetSlotBoundsField = AccessTools.Field(typeof(GuiDialogCharacter), "insetSlotBounds");

    [HarmonyPrefix]
    private static void Prefix(GuiDialogCharacter __instance, out int __state)
    {
        __state = -1;

        try
        {
            var dialog = __instance as GuiDialogCharacterBase;
            if (dialog == null || CurTabField == null) return;

            int curTab = (int)CurTabField.GetValue(__instance);
            if (curTab < 0 || curTab >= dialog.Tabs.Count) return;
            if (!string.Equals(dialog.Tabs[curTab].Name, "Accessories", StringComparison.Ordinal)) return;

            if (InsetSlotBoundsField != null && AccessoryTabPatches.AccessoryInsetBounds != null)
            {
                InsetSlotBoundsField.SetValue(__instance, AccessoryTabPatches.AccessoryInsetBounds);
            }

            __state = curTab;
            CurTabField.SetValue(__instance, 0);
        }
        catch
        {
        }
    }

    [HarmonyPostfix]
    private static void Postfix(GuiDialogCharacter __instance, int __state)
    {
        if (__state < 0 || CurTabField == null) return;

        try
        {
            CurTabField.SetValue(__instance, __state);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(EntityBehaviorPlayerInventory), "OnTesselation")]
[HarmonyPatchCategory(AccessoryTabPatches.AccessoryTabPatchCategory)]
public static class AccessoryRenderHookPatch
{
    private static readonly MethodInfo AddGearToShapeMethod = ResolveAddGearToShapeMethod();

    private static MethodInfo ResolveAddGearToShapeMethod()
    {
        var type = typeof(EntityBehaviorPlayerInventory);
        while (type != null)
        {
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            foreach (var mi in methods)
            {
                if (mi.Name != "addGearToShape") continue;

                var p = mi.GetParameters();
                if (p.Length < 6) continue;
                if (p[0].ParameterType != typeof(Shape)) continue;
                if (p[1].ParameterType != typeof(ItemSlot)) continue;
                if (p[2].ParameterType != typeof(string)) continue;
                if (p[3].ParameterType != typeof(string)) continue;
                if (p[4].ParameterType != typeof(bool).MakeByRefType()) continue;
                if (p[5].ParameterType != typeof(string[]).MakeByRefType()) continue;

                return mi;
            }

            type = type.BaseType;
        }

        return null;
    }

    [HarmonyPrefix]
    private static void Prefix(
        EntityBehaviorPlayerInventory __instance,
        ref Shape entityShape,
        string shapePathForLogging,
        ref bool shapeIsCloned,
        ref string[] willDeleteElements)
    {
        try
        {
            var bhEntity = __instance.entity;
            if (bhEntity == null || bhEntity.Api?.Side != EnumAppSide.Client) return;

            var capi = AccessoryTabCore.Capi ?? AccessoryTabPatches.ClientApi;
            if (capi == null || AddGearToShapeMethod == null) return;

            var localEntity = capi.World?.Player?.Entity;
            if (localEntity == null) return;

            bool isLocalPlayer = bhEntity.EntityId == localEntity.EntityId;

            if (isLocalPlayer)
            {
                // Render local player's accessories
                // Ensure inventory is loaded
                var inv = AccessoryTabPatches.AccessoryInventory;
                if (inv == null)
                {
                    inv = AccessoryTabPatches.EnsureLocalInventory(capi);
                }

                if (inv != null)
                {
                    ApplyAccessoriesToShape(__instance, entityShape, inv, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);
                }
            }
            else
            {
                // Render other players' accessories from broadcast data
                if (!AccessoryTabPatches.TryGetAccessoryDataForEntity(capi, bhEntity, out var base64SlotData)) return;
                AccessoryTabPatches.TryGetPlayerUidForEntity(capi, bhEntity, out var playerUID);

                try
                {
                    var tempInventoryId = string.IsNullOrWhiteSpace(playerUID)
                        ? bhEntity.EntityId.ToString()
                        : playerUID;

                    var tempInv = new AccessoryInventory(tempInventoryId, capi, skipLoadPersistedSlot: true);
                    tempInv.SetSlotFromSerialized(base64SlotData, suppressAccessoryModifiedEvent: true);

                    ApplyAccessoriesToShape(__instance, entityShape, tempInv, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);
                }
                catch (Exception ex)
                {
                    AccessoryTabCore.Logger?.Error($"[AccessoryTab] Error rendering remote accessories: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AccessoryTabCore.Logger?.Error("[AccessoryTab] Render hook error: " + ex.Message);
        }
    }

    private static void ApplyAccessoriesToShape(
        EntityBehaviorPlayerInventory instance,
        Shape entityShape,
        AccessoryInventory inv,
        string shapePathForLogging,
        ref bool shapeIsCloned,
        ref string[] willDeleteElements)
    {
        if (inv == null || AddGearToShapeMethod == null) return;

        for (int i = 0; i < inv.Count; i++)
        {
            var slot = inv[i];
            var stack = slot?.Itemstack;

            if (stack == null)
            {
                continue;
            }

            object[] args;
            if (AddGearToShapeMethod.GetParameters().Length >= 7)
            {
                args = new object[]
                {
                    entityShape,
                    slot,
                    "default",
                    shapePathForLogging,
                    shapeIsCloned,
                    willDeleteElements,
                    null
                };
            }
            else
            {
                args = new object[]
                {
                    entityShape,
                    slot,
                    "default",
                    shapePathForLogging,
                    shapeIsCloned,
                    willDeleteElements
                };
            }

            try
            {
                var shaped = AddGearToShapeMethod.Invoke(instance, args) as Shape;
                if (shaped != null) entityShape = shaped;

                shapeIsCloned = (bool)args[4];
                willDeleteElements = (string[])args[5];
            }
            catch (Exception ex)
            {
                AccessoryTabCore.Logger?.Error($"[AccessoryTab]   Slot {i}: Failed to apply - {ex.Message}");
            }
        }
    }
}

[HarmonyPatch(typeof(AccessoryInventory), "OnItemSlotModified")]
[HarmonyPatchCategory(AccessoryTabPatches.AccessoryTabPatchCategory)]
public static class AccessoryInventoryModificationPatch
{
    [HarmonyPostfix]
    public static void Postfix(AccessoryInventory __instance, ItemSlot slot)
    {
        try
        {
            var capi = AccessoryTabCore.Capi;
            if (capi?.World?.Player?.Entity == null) return;

            var localPlayer = capi.World.Player;
            AccessoryTabPatches.PendingVisualRefresh = true;
            AccessoryTabPatches.PendingAccessoryRefreshPlayerUids.Add(localPlayer.PlayerUID);
        }
        catch (Exception ex)
        {
            AccessoryTabCore.Logger?.Error($"[AccessoryTab] Error in inventory modification patch: {ex.Message}");
        }
    }
}


