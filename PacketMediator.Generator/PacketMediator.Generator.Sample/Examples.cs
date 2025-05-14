// Licensed to Timothy Schenk under the Apache 2.0 License.

using System;
using System.Threading;
using System.Threading.Tasks;
using RaiNote.PacketMediator;

namespace PacketMediator.Generator.Sample;

public enum OperationCode {
    A,
    B,
    C,
    D,
    E = 10,
    F,
    G,
    H = 100,
    I = 200
}

[GamePacketId(OperationCode.A, 100)]
public struct StructA : IIncomingPacket {
    public PacketSerializationCodes Deserialize(Span<byte> data) {
        throw new NotImplementedException();
    }

    public int GetCurrentMaxSize() {
        throw new NotImplementedException();
    }
}

[GamePacketId(OperationCode.B, 100)]
public struct StructB : IPacket, IIncomingPacket {

    public PacketSerializationCodes Deserialize(Span<byte> data) {
        throw new NotImplementedException();
    }

    public int GetCurrentMaxSize() {
        throw new NotImplementedException();
    }
}

[GamePacketId(OperationCode.E, 100)]
public struct StructC : IIncomingPacket {
    public PacketSerializationCodes Deserialize(Span<byte> data) {
        throw new NotImplementedException();
    }

    public int GetCurrentMaxSize() {
        throw new NotImplementedException();
    }
}

[GamePacketId(OperationCode.H, 100)]
public struct StructD : IIncomingPacket {
    public PacketSerializationCodes Deserialize(Span<byte> data) {
        throw new NotImplementedException();
    }

    public int GetCurrentMaxSize() {
        throw new NotImplementedException();
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class GamePacketIdAttribute : PacketIdAttribute<OperationCode> {
    public GamePacketIdAttribute(OperationCode code, int size) : base(code, size) {
    }
}

public class RandomSession {
}

public class HandlerA : IPacketHandler<StructA, RandomSession> {
    public async Task HandleAsync(StructA packet, RandomSession session, CancellationToken cancellationToken) {
        await Task.Delay(2000, cancellationToken);
        throw new NotImplementedException();
    }
}

public class TestHandler : IPacketHandler<StructB, RandomSession> {
    public async Task HandleAsync(StructB packet, RandomSession session, CancellationToken cancellationToken) {
        await Task.Delay(2000, cancellationToken);
        throw new NotImplementedException();
    }
}

public class BHandler : IPacketHandler<StructC, RandomSession> {
    public Task HandleAsync(StructC packet, RandomSession session, CancellationToken cancellationToken) {
        throw new NotImplementedException();
    }
}

public class DHandler : IPacketHandler<StructD, RandomSession> {
    public Task HandleAsync(StructD packet, RandomSession session, CancellationToken cancellationToken) {
        throw new NotImplementedException();
    }
}
