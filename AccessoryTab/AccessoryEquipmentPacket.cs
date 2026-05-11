using ProtoBuf;

namespace AccessoryTab;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class AccessoryEquipmentPacket
{
    public string PlayerUID { get; set; }
    public string Base64SlotData { get; set; }
}
