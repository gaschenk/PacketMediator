// Licensed to Timothy Schenk under the Apache 2.0 License.

using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace RaiNote.PacketMediator;

public class PacketDistributorService<TPacketIdEnum, TSession> : IHostedService
    where TPacketIdEnum : Enum
{
    private readonly PacketDistributor<TPacketIdEnum, TSession> _packetDistributor;

    public PacketDistributorService(IServiceProvider serviceProvider,
        IEnumerable<Assembly> sourcesContainingPackets, IEnumerable<Assembly> sourcesContainingPacketHandlers)
    {
        _packetDistributor = new PacketDistributor<TPacketIdEnum, TSession>(serviceProvider,
            sourcesContainingPackets,
            sourcesContainingPacketHandlers
        );
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _packetDistributor.DequeuePacketAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task AddPacketAsync(byte[] packetData, TPacketIdEnum operationCode, TSession session)
    {
        return _packetDistributor.AddPacketAsync(packetData, operationCode, session);
    }

    public DotNext.Optional<TPacketIdEnum> GetOperationCodeByPacketType(IPacket packet)
    {
        var type = packet.GetType();
        _packetDistributor.PacketIdMap.TryGetValue(type, out var value);
        return value ?? DotNext.Optional<TPacketIdEnum>.None;
    }
}
