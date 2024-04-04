// Licensed to Timothy Schenk under the Apache 2.0 License.

using JetBrains.Annotations;

namespace RaiNote.PacketMediator;

[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IOutgoingPacket : IPacket
{
    [UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
    public byte[] Serialize();
}
