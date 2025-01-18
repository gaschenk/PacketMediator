// Licensed to Timothy Schenk under the Apache 2.0 License.

using Microsoft.CodeAnalysis;

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

internal record IntermediatePacketStructData(
    Location SymbolLocation,
    string PacketStructFullIdentifier,
    string EnumValue,
    string EnumTypeFullIdentifier,
    string EnumMemberIdentifier,
    string EnumMaxValue,
    bool ImplementsInterface);

internal record IntermediateHandlerAndStructTuple(
    IntermediatePacketHandlerData HandlerData,
    IntermediatePacketStructData StructData);
