using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Signaling;

/// <summary>
/// Пара тестовых устройств Parent/Child с in-memory Keystore и trusted store. По умолчанию оба
/// store содержат identity peer (доверие взаимно).
/// </summary>
internal sealed class SignalingTestPeers : IDisposable
{
    public SignalingTestPeers(bool trustEachOther = true)
    {
        if (trustEachOther)
        {
            ParentTrustedStore.Trust(ChildKeyStore, BabyMonitor.Domain.Devices.DeviceRole.Parent);
            ChildTrustedStore.Trust(ParentKeyStore, BabyMonitor.Domain.Devices.DeviceRole.Child);
        }
    }

    public FakePairingKeyStore ParentKeyStore { get; } = new();

    public FakePairingKeyStore ChildKeyStore { get; } = new();

    public InMemoryTrustedDeviceStore ParentTrustedStore { get; } = new();

    public InMemoryTrustedDeviceStore ChildTrustedStore { get; } = new();

    public SignalingIdentity CreateParentIdentity() =>
        new(ParentKeyStore, ParentTrustedStore, ChildKeyStore.FingerprintSha256);

    public SignalingIdentity CreateChildIdentity() =>
        new(ChildKeyStore, ChildTrustedStore, ParentKeyStore.FingerprintSha256);

    public void Dispose()
    {
        ParentKeyStore.Dispose();
        ChildKeyStore.Dispose();
    }
}

/// <summary>Loopback-соединение signaling: клиент Parent и сервер Child, оба результата сохраняются.</summary>
internal sealed class SignalingLoopback : IAsyncDisposable
{
    private readonly TlsSignalingServerSession _serverSession;

    private SignalingLoopback(
        TlsSignalingServerSession serverSession,
        Result<TlsSignalingConnection> clientResult,
        Result<TlsSignalingConnection> serverResult)
    {
        _serverSession = serverSession;
        ClientResult = clientResult;
        ServerResult = serverResult;
    }

    public Result<TlsSignalingConnection> ClientResult { get; }

    public Result<TlsSignalingConnection> ServerResult { get; }

    public TlsSignalingServerSession ServerSession => _serverSession;

    public static async Task<SignalingLoopback> ConnectAsync(
        SignalingTestPeers peers,
        SignalingOptions? parentOptions = null,
        SignalingOptions? childOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peers);
        var serverSession = await SignalingLoopbackHarness
            .StartServerAsync(peers.CreateChildIdentity(), childOptions, cancellationToken);
        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", serverSession.BoundPort),
            peers.CreateParentIdentity(),
            parentOptions);

        var clientTask = client.ConnectAsync(cancellationToken);
        var serverTask = serverSession.AcceptAsync(cancellationToken);
        var clientResult = await clientTask;
        var serverResult = await serverTask;
        return new SignalingLoopback(serverSession, clientResult, serverResult);
    }

    public async ValueTask DisposeAsync()
    {
        if (ClientResult.IsSuccess)
        {
            await ClientResult.Value.DisposeAsync();
        }

        if (ServerResult.IsSuccess)
        {
            await ServerResult.Value.DisposeAsync();
        }

        await _serverSession.DisposeAsync();
    }
}

/// <summary>Запуск signaling listener на свободном порту для loopback-тестов.</summary>
internal static class SignalingLoopbackHarness
{
    public static async Task<TlsSignalingServerSession> StartServerAsync(
        SignalingIdentity childIdentity,
        SignalingOptions? options,
        CancellationToken cancellationToken)
    {
        var server = new TlsSignalingServer(childIdentity, options);
        var startResult = await server.StartAsync(0, cancellationToken);
        if (!startResult.IsSuccess)
        {
            throw new InvalidOperationException("Не удалось открыть signaling-порт: " + startResult.Error!.Code);
        }

        return startResult.Value;
    }

    public static TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

    public static CancellationTokenSource CreateTimeout() => new(DefaultTimeout);
}
