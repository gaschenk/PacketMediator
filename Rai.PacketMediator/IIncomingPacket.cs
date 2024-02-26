// Licensed to Timothy Schenk under the Apache 2.0 License.

using JetBrains.Annotations;

namespace Rai.PacketMediator;

[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IIncomingPacket : IPacket
{
    [UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
    public void Deserialize(byte[] data);
}
