# About

This project is meant as a simple-enough API to create different network packet definitions, which are required to be
serialized and deserialized in a high throughput environment.
The primary user audience would be for server emulation for old software, specifically games, that have been abandoned
or simply canceled together.
Rai.PacketMediator was formerly a part of a larger side-project to create a server emulator in a more modern .NET manner
than older projects for an old 2D side-scrolling mmorpg called *Wonderking*.

# Caveats

It does not and probably will not support ahead-of-time compilation for a long time, as to why it can be inferred by
reading the "how it works" section.
Furthermore, with how the current API is designed, there will be lots of reoccurring text components specifically
looking at how packet handlers are implemented.

# How it works

This library is using DotNext to generate on startup the required methods for packet handling and passing around of
packets.
That is the primary reason why it will not support ahead-of-time compilation as custom IL is emitted.

It will primarily create a contract that needs to be followed for packet definitions and their respective handlers.

# Samples

Minimal setup using NetCoreServer for TCPSession and MassTransit for Consumption
While this, so to say, minimal sample is already rather long as there are lots of parts that need to be considered when
bootstrapping this library.
It should give a general overview on how interact and a possible solution as to how to transfer packets between a
session and a handler.

```csharp
// OperationCode.cs
public enum OperationCode : ushort
{
    LoginRequest = 1
}

// LoginRequestPacket.cs
[GamePacketIdAttribute(OperationCode.LoginRequest)]
public class LoginRequestPacket : IIncomingPacket
{
    public required string Username { get; set; }

    public required string Password { get; set; }

    public void Deserialize(byte[] data)
    {
        Username = Encoding.ASCII.GetString(data, 0, 32)
        Password = Encoding.ASCII.GetString(data, 32, 64)
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

// LoginHandler.cs
public class LoginHandler : IPacketHandler<LoginRequest, AuthSession>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LoginHandler> _logger;
    private readonly DbContext _dbContext;

    public LoginHandler(ILogger<LoginHandler> logger, DbContext dbContext, IConfiguration configuration)
    {
        _logger = logger;
        _dbContext = dbContext;
        _configuration = configuration;
    }

    public async Task HandleAsync(LoginInfoPacket packet, AuthSession session, CancellationToken cancellationToken)
    {
        // Insert logic here
        _ = session.SendAsync(new byte[]{});
    }
}

// Packetconsumer.cs
public class PacketConsumer : IConsumer<RawPacket>
{
    private readonly PacketDistributorService<OperationCode, AuthSession> _distributorService;

    public PacketConsumer(PacketDistributorService<OperationCode, AuthSession> distributorService)
    {
        _distributorService = distributorService;
    }

    public Task Consume(ConsumeContext<RawPacket> context)
    {
        return _distributorService.AddPacketAsync(context.Message.MessageBody, context.Message.OperationCode,
            context.Message.Session);
    }
}

// RawPacket.cs
[MessageUrn("packets")]
public class RawPacket
{
    public RawPacket(OperationCode operationCode, Span<byte> messageBody, Guid sessionId, AuthSession session)
    {
        MessageBody = messageBody.ToArray();
        SessionId = sessionId;
        Session = session;
        OperationCode = operationCode;
    }

    public OperationCode OperationCode { get; }
    public byte[] MessageBody { get; }
    public Guid SessionId { get; }
    public AuthSession Session { get; }
}

// AuthSession.cs
public class AuthSession : TcpSession
{
    private readonly PacketDistributorService<OperationCode, AuthSession> _distributorService;
    private readonly ILogger<AuthSession> _logger;
    private readonly IMediator _mediator;

    public AuthSession(TcpServer
            server, IMediator mediator, ILogger<AuthSession> logger,
        PacketDistributorService<OperationCode, AuthSession> distributorService) : base(server)
    {
        _mediator = mediator;
        _logger = logger;
        _distributorService = distributorService;
    }

    public Guid AccountId { get; set; }

    public Task SendAsync(IOutgoingPacket packet)
    {
        var opcode = _distributorService.GetOperationCodeByPacketType(packet);

        Span<byte> packetData = packet.Serialize();
        var length = (ushort)(packetData.Length + 2);

        Span<byte> buffer = stackalloc byte[length];
        buffer.Clear();
        packetData.CopyTo(buffer[2..length]);

        var bytesOfOpcode = BitConverter.GetBytes((ushort)opcode);

        for (var i = 0; i < bytesOfOpcode.Length || i < 2; i++)
        {
            buffer[2 + i] = bytesOfOpcode[i];
        }

        SendAsync(buffer);
        return Task.CompletedTask;
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        Span<byte> decryptedBuffer = stackalloc byte[(int)size];

        var dataBuffer = Decrypt(buffer.AsSpan(2, (int)size - 2));

        var opCode = BitConverter.ToUInt16(buffer.ToArray(), 0);

        var rawPacket = new RawPacket((OperationCode)opCode, dataBuffer, Id, this);

        _ = _mediator.Send(rawPacket);
        base.OnReceived(decryptedBuffer.ToArray(), offset, decryptedBuffer.Length);
    }
}

// Program.cs
// create builder
builder.Services.AddSingleton(provider =>
new PacketDistributorService<OperationCode, AuthSession>(
    provider.GetRequiredService<IServiceProvider>(),
    new List<Assembly> { Assembly.GetAssembly(typeof(OperationCode)) }.AsReadOnly(),
    new List<Assembly> { Assembly.GetAssembly(typeof(LoginHandler)) }.AsReadOnly()
));
builder.Services.AddHostedService(provider =>
    provider.GetService<PacketDistributorService<OperationCode, AuthSession>>() ??
    throw new InvalidOperationException());
// build services


```
