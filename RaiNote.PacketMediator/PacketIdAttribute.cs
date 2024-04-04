// Licensed to Timothy Schenk under the Apache 2.0 License.

namespace RaiNote.PacketMediator;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public abstract class PacketIdAttribute<TPacketIdEnum> : Attribute where TPacketIdEnum : Enum
{
    protected PacketIdAttribute(TPacketIdEnum code)
    {
        Code = code;
    }

    public TPacketIdEnum Code { get; }
}
