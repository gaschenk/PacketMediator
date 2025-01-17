// Licensed to Timothy Schenk under the Apache 2.0 License.

using Microsoft.CodeAnalysis;

namespace RaiNote.PacketMediator;

class IntermediatePacketStructHandlerData {
    public IntermediatePacketStructHandlerData(ITypeSymbol packetStructSymbol, ITypeSymbol sessionSymbol) {
        PacketStructSymbol = packetStructSymbol;
        SessionSymbol = sessionSymbol;
    }

    public ITypeSymbol PacketStructSymbol { get; set; }
    public ITypeSymbol SessionSymbol { get; set; }
}
