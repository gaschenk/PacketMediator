// Licensed to Timothy Schenk under the Apache 2.0 License.

using Microsoft.CodeAnalysis;

namespace RaiNote.PacketMediator;

class IntermediatePacketHandlerData {
    public IntermediatePacketHandlerData(INamedTypeSymbol? symbol, string packetHandlerIdentifier, IntermediatePacketStructHandlerData? packetStructHandlerData) {
        Symbol = symbol;
        PacketHandlerIdentifier = packetHandlerIdentifier;
        PacketStructHandlerData = packetStructHandlerData;
    }

    public INamedTypeSymbol? Symbol { get; set; }
    public string PacketHandlerIdentifier { get; set; }
    public IntermediatePacketStructHandlerData? PacketStructHandlerData { get; set; }
}
