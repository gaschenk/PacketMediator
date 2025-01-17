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
}

[GamePacketId(OperationCode.A)]
public struct StructA : IIncomingPacket {
    public void Deserialize(byte[] data) {
        throw new NotImplementedException();
    }
}

[GamePacketId(OperationCode.B)]
public struct StructB : IPacket {
}
[GamePacketId(OperationCode.E)]
public struct StructC : IPacket {
}
[GamePacketId(OperationCode.F)]
public struct StructD : IPacket {
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
