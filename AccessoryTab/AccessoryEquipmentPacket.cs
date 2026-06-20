using ProtoBuf;

namespace AccessoryTab;

/// <summary>
/// Network packet for synchronizing accessory equipment state between server and clients.
/// Serialized using ProtoBuf for efficient network transmission.
/// Used in both P2P and dedicated server scenarios to broadcast accessory changes to all players.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class AccessoryEquipmentPacket
{
    /// <summary>
    /// The unique identifier of the player whose accessories changed.
    /// Used by clients to identify which player's visual state to update.
    /// </summary>
    public string PlayerUID { get; set; }

    /// <summary>
    /// Base64-encoded serialized accessory inventory data.
    /// Empty string indicates the player's accessories should be cleared (e.g., on disconnect).
    /// Contains the full state of all 8 accessory slots.
    /// </summary>
    public string Base64SlotData { get; set; }
}
