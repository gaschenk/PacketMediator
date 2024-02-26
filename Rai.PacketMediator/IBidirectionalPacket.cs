// Licensed to Timothy Schenk under the GNU AGPL Version 3 License.

using JetBrains.Annotations;

namespace Rai.PacketMediator;

[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IBidirectionalPacket : IOutgoingPacket, IIncomingPacket;
