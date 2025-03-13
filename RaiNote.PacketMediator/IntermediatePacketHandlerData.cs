// Licensed to Timothy Schenk under the Apache 2.0 License.

namespace RaiNote.PacketMediator;

internal class IntermediatePacketHandlerData {
    public IntermediatePacketHandlerData(string packetHandlerIdentifier,
        IntermediatePacketStructHandlerData? packetStructHandlerData) {
        PacketHandlerIdentifier = packetHandlerIdentifier;
        PacketStructHandlerData = packetStructHandlerData;
    }

    public string PacketHandlerIdentifier { get; set; }
    public IntermediatePacketStructHandlerData? PacketStructHandlerData { get; set; }
}
