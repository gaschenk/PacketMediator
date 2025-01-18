// Licensed to Timothy Schenk under the Apache 2.0 License.

using Microsoft.CodeAnalysis;

namespace RaiNote.PacketMediator;

internal class IntermediatePacketStructHandlerData {
    public IntermediatePacketStructHandlerData(string packetStructFullIdentifier, string sessionFullIdentifier) {
        PacketStructFullIdentifier = packetStructFullIdentifier;
        SessionFullIdentifier = sessionFullIdentifier;
    }

    public string PacketStructFullIdentifier { get; set; }
    public string SessionFullIdentifier { get; set; }
}
