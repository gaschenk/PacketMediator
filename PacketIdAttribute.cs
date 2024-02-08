// Licensed to Timothy Schenk under the GNU AGPL Version 3 License.

namespace Rai.PacketMediator;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public abstract class PacketIdAttribute<TPacketIdEnum> : Attribute where TPacketIdEnum : Enum
{
    protected PacketIdAttribute(TPacketIdEnum code)
    {
        Code = code;
    }

    public TPacketIdEnum Code { get; }
}
