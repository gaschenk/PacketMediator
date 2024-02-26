// Licensed to Timothy Schenk under the Apache 2.0 License.

using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace Rai.PacketMediator;

public class PacketDistributorService<TPacketIdEnum, TSession> : IHostedService
    where TPacketIdEnum : Enum
{
    private readonly PacketDistributor<TPacketIdEnum, TSession> _packetDistributor;

    public PacketDistributorService(IServiceProvider serviceProvider,
        IEnumerable<Assembly> sourcesContainingPackets, IEnumerable<Assembly> sourcesContainingPacketHandlers)
    {
        _packetDistributor = new PacketDistributor<TPacketIdEnum, TSession>(serviceProvider, sourcesContainingPackets,
            sourcesContainingPacketHandlers);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _packetDistributor.DequeuePacketAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task AddPacketAsync(byte[] packetData, TPacketIdEnum operationCode, TSession session)
    {
        return _packetDistributor.AddPacketAsync(packetData, operationCode, session);
    }

    public TPacketIdEnum GetOperationCodeByPacketType(IPacket packet)
    {
        var type = packet.GetType();
        _packetDistributor.PacketIdMap.TryGetValue(type, out var value);
        if (value is null)
        {
            throw new ArgumentOutOfRangeException(type.Name);
        }

        return value;
    }
}
