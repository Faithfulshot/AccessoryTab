using ProtoBuf;
using System.Collections.Generic;

namespace AccessoryTab;

/// <summary>
/// Network packet for synchronizing configuration from server to clients.
/// Ensures all clients use the server's slot configuration in multiplayer.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class AccessoryConfigSyncPacket
{
    /// <summary>
    /// List of enabled slot indices (0-7).
    /// </summary>
    public List<int> EnabledSlots { get; set; }

    /// <summary>
    /// Serialized slot mappings as JSON string.
    /// Format: Dictionary&lt;string, SlotMapping&gt; serialized to JSON.
    /// </summary>
    public string SlotMappingsJson { get; set; }
}
