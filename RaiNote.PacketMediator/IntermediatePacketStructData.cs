// Licensed to Timothy Schenk under the Apache 2.0 License.

using Microsoft.CodeAnalysis;

namespace RaiNote.PacketMediator;

internal record IntermediatePacketStructData(
    Location SymbolLocation,
    string PacketStructFullIdentifier,
    string? EnumValue,
    string EnumTypeFullIdentifier,
    string EnumMemberIdentifier,
    string? EnumMaxValue,
    bool ImplementsInterface);
