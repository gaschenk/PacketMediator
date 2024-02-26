// Licensed to Timothy Schenk under the Apache 2.0 License.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading.Channels;
using DotNext.Collections.Generic;
using DotNext.Linq.Expressions;
using DotNext.Metaprogramming;
using Microsoft.Extensions.DependencyInjection;

namespace Rai.PacketMediator;

public class PacketDistributor<TPacketIdEnum, TSession> where TPacketIdEnum : Enum
{
    private readonly Channel<ValueTuple<byte[], TPacketIdEnum, TSession>> _channel;

    private readonly ImmutableDictionary<TPacketIdEnum,
        Func<byte[], IIncomingPacket>> _deserializationMap;

    private readonly ConcurrentDictionary<TPacketIdEnum, IPacketHandler<TSession>?> _packetHandlersInstantiation;

    public PacketDistributor(IServiceProvider serviceProvider,
        IEnumerable<Assembly> sourcesContainingPackets, IEnumerable<Assembly> sourcesContainingPacketHandlers)
    {
        _channel = Channel.CreateUnbounded<ValueTuple<byte[], TPacketIdEnum, TSession>>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false
        });
        var containingPackets = sourcesContainingPackets as Assembly[] ?? sourcesContainingPackets.ToArray();
        var allIncomingPackets = GetAllPackets(containingPackets, typeof(IIncomingPacket));
        var allOutgoingPackets = GetAllPackets(containingPackets, typeof(IOutgoingPacket));

        var packetHandlers = GetAllPacketHandlersWithId(sourcesContainingPacketHandlers);

        PacketIdMap = allOutgoingPackets.Select(x => new { PacketId = x.Key, Type = x.Value })
            .ToImmutableDictionary(x => x.Type, x => x.PacketId);

        var tempDeserializationMap =
            new ConcurrentDictionary<TPacketIdEnum, Func<byte[], IIncomingPacket>>();
        _packetHandlersInstantiation = new ConcurrentDictionary<TPacketIdEnum, IPacketHandler<TSession>?>();
        packetHandlers.ForEach(packetHandlerPair =>
        {
            var packetHandler =
                ActivatorUtilities.GetServiceOrCreateInstance(serviceProvider,
                    packetHandlerPair.Value);
            _packetHandlersInstantiation.TryAdd(packetHandlerPair.Key, packetHandler as IPacketHandler<TSession>);
        });
        allIncomingPackets.ForEach(packetsType =>
        {
            var lambda = CodeGenerator.Lambda<Func<byte[], IIncomingPacket>>(fun =>
            {
                var argPacketData = fun[0];
                var newPacket = packetsType.Value.New();

                var packetVariable = CodeGenerator.DeclareVariable(packetsType.Value, "packet");
                CodeGenerator.Assign(packetVariable, newPacket);
                CodeGenerator.Call(packetVariable, nameof(IIncomingPacket.Deserialize), argPacketData);

                CodeGenerator.Return(packetVariable);
            }).Compile();
            tempDeserializationMap.TryAdd(packetsType.Key, lambda);
        });

        _deserializationMap = tempDeserializationMap.ToImmutableDictionary();
    }

    public ImmutableDictionary<Type, TPacketIdEnum> PacketIdMap { get; }

    private static IEnumerable<KeyValuePair<TPacketIdEnum, Type>> GetAllPackets(
        IEnumerable<Assembly> sourcesContainingPackets, Type packetType)
    {
        var packetsWithId = sourcesContainingPackets.SelectMany(a => a.GetTypes()
                .Where(type => type is { IsInterface: false, IsAbstract: false } &&
                               type.GetInterfaces().Contains(packetType)
                               && type.GetCustomAttributes<PacketIdAttribute<TPacketIdEnum>>().Any()
                ))
            .Select(type =>
                new { Type = type, Attribute = type.GetCustomAttribute<PacketIdAttribute<TPacketIdEnum>>() })
            .Select(x => new KeyValuePair<TPacketIdEnum, Type>(x.Attribute!.Code, x.Type));

        return packetsWithId;
    }

    private static IEnumerable<KeyValuePair<TPacketIdEnum, Type>> GetAllPacketHandlersWithId(
        IEnumerable<Assembly> sourcesContainingPacketHandlers)
    {
        var packetHandlersWithId = sourcesContainingPacketHandlers.SelectMany(assembly => assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false } && Array.Exists(t
                        .GetInterfaces(), i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPacketHandler<,>)))
                .Select(packetHandlerType => new
                {
                    Type = packetHandlerType,
                    PacketId = packetHandlerType
                        .GetInterfaces().First(t1 =>
                            t1 is { IsGenericType: true } &&
                            t1.GetGenericTypeDefinition() == typeof(IPacketHandler<,>)).GetGenericArguments()
                        .First(genericType => genericType.GetInterfaces().Any(packetType =>
                            packetType == typeof(IPacket)))
                        .GetCustomAttribute<PacketIdAttribute<TPacketIdEnum>>()
                }))
            .Where(x => x.PacketId != null)
            .Select(x => new KeyValuePair<TPacketIdEnum, Type>(x.PacketId!.Code, x.Type));

        return packetHandlersWithId;
    }

    public async Task AddPacketAsync(byte[] packetData, TPacketIdEnum operationCode, TSession session)
    {
        await _channel.Writer.WriteAsync((packetData, operationCode, session));
    }

    public async Task DequeuePacketAsync(CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_channel.Reader.TryRead(out var item))
            {
                await InvokePacketHandlerAsync(item, cancellationToken);
            }
        }
    }

    private async Task InvokePacketHandlerAsync((byte[], TPacketIdEnum, TSession) valueTuple,
        CancellationToken cancellationToken)
    {
        var (packetData, operationCode, session) = valueTuple;
        if (!_deserializationMap.TryGetValue(operationCode, out var func))
        {
            return;
        }

        var packet = func(packetData);

        await _packetHandlersInstantiation[operationCode]?.TryHandleAsync(packet, session, cancellationToken)!;
    }
}
