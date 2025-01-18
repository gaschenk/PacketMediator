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
    I = 5000,
}

[GamePacketId(OperationCode.A)]
public struct StructA : IIncomingPacket {
    public void Deserialize(byte[] data) {
        throw new NotImplementedException();
    }
}

[GamePacketId(OperationCode.B)]
public struct StructB : IPacket, IIncomingPacket {
    public void Deserialize(byte[] data) {
        throw new NotImplementedException();
    }
}

[GamePacketId(OperationCode.E)]
public struct StructC : IIncomingPacket {
    public void Deserialize(byte[] data) {
        throw new NotImplementedException();
    }
}

[GamePacketId(OperationCode.H)]
public struct StructD : IIncomingPacket {
    public void Deserialize(byte[] data) {
        throw new NotImplementedException();
    }
}


[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class GamePacketIdAttribute : PacketIdAttribute<OperationCode> {
    public GamePacketIdAttribute(OperationCode code) : base(code) {
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
    public Task HandleAsync(StructB packet, RandomSession session, CancellationToken cancellationToken) {
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
