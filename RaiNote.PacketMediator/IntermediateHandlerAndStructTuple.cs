// Licensed to Timothy Schenk under the Apache 2.0 License.

namespace RaiNote.PacketMediator;

internal record IntermediateHandlerAndStructTuple(
    IntermediatePacketHandlerData HandlerData,
    IntermediatePacketStructData StructData);
