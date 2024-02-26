// Licensed to Timothy Schenk under the Apache 2.0 License.

using System.Diagnostics;
using JetBrains.Annotations;

namespace Rai.PacketMediator;

[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IPacketHandler<in TIncomingPacket, in TSession> : IPacketHandler<TSession>
    where TIncomingPacket : IIncomingPacket
{
    async Task<bool> IPacketHandler<TSession>.TryHandleAsync(IIncomingPacket packet, TSession session,
        CancellationToken cancellationToken)
    {
        if (packet is not TIncomingPacket tPacket)
        {
            return false;
        }

        using var activity = new ActivitySource(nameof(PacketMediator)).StartActivity(nameof(HandleAsync));
        activity?.AddTag("Handler", ToString());
        activity?.AddTag("Packet", packet.ToString());
        await HandleAsync(tPacket, session, cancellationToken);

        return true;
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
    public Task HandleAsync(TIncomingPacket packet, TSession session, CancellationToken cancellationToken);
}

public interface IPacketHandler<in TSession>
{
    Task<bool> TryHandleAsync(IIncomingPacket packet, TSession session, CancellationToken cancellationToken);
}
