// Licensed to Timothy Schenk under the Apache 2.0 License.

using JetBrains.Annotations;

namespace Rai.PacketMediator;

[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IBidirectionalPacket : IOutgoingPacket, IIncomingPacket;
