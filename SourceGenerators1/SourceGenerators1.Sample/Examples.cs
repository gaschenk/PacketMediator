using System;
using System.Collections.Generic;
using System.Text;
using JetBrains.Annotations;

namespace SourceGenerators1.Sample;

[GamePacketIdAttribute(OperationCode.LoginRequest)]
public class LoginRequestPacket : IIncomingPacket
{
    public string Username { get; set; }

    public string Password { get; set; }

    public void Deserialize(byte[] data)
    {
        Username = Encoding.ASCII.GetString(data, 0, 32);
        Password = Encoding.ASCII.GetString(data, 32, 64);
    }
}

// GamePacketIdAttribute.cs
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class GamePacketIdAttribute : PacketIdAttribute<OperationCode>
{
    public GamePacketIdAttribute(OperationCode code) : base(code)
    {
    }
}

public enum OperationCode : ushort
{
    LoginRequest = 1
}

