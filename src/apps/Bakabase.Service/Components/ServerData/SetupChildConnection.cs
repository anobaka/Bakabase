using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.ServerData;

/// <summary>Private parent/child protocol. Public HTTP capabilities are never IPC credentials.</summary>
public sealed class SetupChildConnection : IAsyncDisposable
{
    public const string RoleArgument = "--bakabase-role=";
    internal const string PipeEnvironment = "BAKABASE_SETUP_PIPE";
    internal const string SecretEnvironment = "BAKABASE_SETUP_SECRET";
    internal sealed record Message(string Type, string? Value = null, string? MonitorUrl = null, int? ParentProcessId = null);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly NamedPipeClientStream _pipe;
    private readonly object _sendGate = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _bind = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static SetupChildConnection? Current { get; private set; }
    public string Role { get; }
    public int ParentProcessId { get; private set; }
    public string ParentSetupAddress { get; private set; } = "";
    public string? MonitorUrl { get; private set; }
    private TaskCompletionSource<string>? _setupReply;
    private string _relaySecret = "";
    private readonly SemaphoreSlim _setupGate = new(1, 1);
    private static readonly HttpClient ProxyClient = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(30) };
    public CancellationToken Stopping => _stopping.Token;
    public event Action? StopRequested;
    private SetupChildConnection(NamedPipeClientStream pipe, string role)
    {
        _pipe = pipe; Role = role;
        _reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
    }
    public static async Task<SetupChildConnection?> ConnectAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var roleArg = args.SingleOrDefault(a => a.StartsWith(RoleArgument, StringComparison.Ordinal));
        if (roleArg == null) return null;
        var role = roleArg[RoleArgument.Length..];
        if (role is not ("business" or "worker")) throw new ArgumentException("Unknown internal Setup role.");
        var pipeName = Environment.GetEnvironmentVariable(PipeEnvironment);
        var secret = Environment.GetEnvironmentVariable(SecretEnvironment);
        if (string.IsNullOrEmpty(pipeName) || secret?.Length != 64)
            throw new IOException("Internal Setup roles require a parent connection.");
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(20000, cancellationToken);
            var connection = new SetupChildConnection(pipe, role);
            connection.Send("hello", secret);
            var reply = await ReadMessage(connection._reader, cancellationToken);
            if (reply?.Type != "accepted") throw new IOException("Setup parent authentication failed.");
            if (reply.ParentProcessId is not > 0 || reply.ParentProcessId == Environment.ProcessId)
                throw new IOException("Setup parent did not provide a valid process identity.");
            connection.ParentProcessId = reply.ParentProcessId.Value;
            // Do not let grandchildren accidentally reuse this private channel.
            Environment.SetEnvironmentVariable(PipeEnvironment, null);
            Environment.SetEnvironmentVariable(SecretEnvironment, null);
            connection.MonitorUrl = reply.MonitorUrl;
            connection._relaySecret = secret;
            connection.ParentSetupAddress = reply.Value ?? throw new IOException("Missing parent Setup address.");
            Current = connection;
            _ = connection.Receive();
            return connection;
        }
        catch { await pipe.DisposeAsync(); throw; }
    }
    public async Task BeforeListenAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Stopping);
        Send("binding");
        await _bind.Task.WaitAsync(linked.Token);
    }
    public void Ready(string address)
    {
        if (ImportProgressStore.Current is { Applied: true } store && store.Read()?.Phase == "starting") store.Complete();
        Send("ready", address);
    }
    public async Task<string> CreateSetupSessionAsync(string mode)
    {
        await _setupGate.WaitAsync(Stopping);
        try
        {
            _setupReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Send("setup", mode);
            return await _setupReply.Task.WaitAsync(TimeSpan.FromSeconds(30), Stopping);
        }
        finally { _setupReply = null; _setupGate.Release(); }
    }
    public async Task<bool> TryProxyAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (path is not ("/setup" or "/setup/status" or "/setup/validate" or "/setup/apply" or "/setup/directories" or "/setup/browse" or "/setup/local-session"
                or "/setup/draft" or "/setup/draft/clear" or "/setup/preflight" or "/setup/preflight/tree" or "/setup/preflight/preview") &&
            path != ImportProgressServer.PagePath && path != ImportProgressServer.StatusPath) return false;
        // Never proxy local capability claiming: a remote client is not the private socket's loopback peer.
        if (path == "/setup/local-session") { context.Response.StatusCode = 401; return true; }
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), ParentSetupAddress + path + context.Request.QueryString);
        foreach (var header in new[] { ImportProgressServer.SetupTokenHeader, ImportProgressServer.TokenHeader })
            if (context.Request.Headers.TryGetValue(header, out var value)) request.Headers.TryAddWithoutValidation(header, value.ToString());
        if (path == "/setup/apply") request.Headers.Add("X-Bakabase-Setup-Relay", _relaySecret);
        const int bodyLimit = 2 * 1024 * 1024;
        if (context.Request.ContentLength > bodyLimit) { context.Response.StatusCode = 413; return true; }
        if (HttpMethods.IsPost(context.Request.Method))
        {
            // Bound chunked bodies too; the private server must not be an unbounded relay.
            using var buffer = new MemoryStream();
            var bytes = new byte[4096]; int read;
            while ((read = await context.Request.Body.ReadAsync(bytes, context.RequestAborted)) > 0)
            { if (buffer.Length + read > bodyLimit) { context.Response.StatusCode = 413; return true; } buffer.Write(bytes, 0, read); }
            request.Content = new ByteArrayContent(buffer.ToArray());
            request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType ?? "application/json");
        }
        using var response = await ProxyClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers) context.Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in response.Content.Headers) context.Response.Headers[header.Key] = header.Value.ToArray();
        context.Response.Headers.Remove("transfer-encoding");
        var committedToken = path == "/setup/apply" && response.IsSuccessStatusCode
            ? context.Request.Headers[ImportProgressServer.SetupTokenHeader].ToString() : null;
        void Acknowledge()
        {
            if (committedToken != null)
                try { Send("commit", committedToken); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        }
        if (committedToken != null) context.Response.OnCompleted(() => { Acknowledge(); return Task.CompletedTask; });
        try { await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted); }
        catch (Exception e) when (e is OperationCanceledException or IOException) { Acknowledge(); throw; }
        return true;
    }
    public void Fatal(string error) => Send("fatal", error.Length > 2000 ? error[..2000] : error);
    public void RequestMaintenance(string? operationId = null)
    {
        operationId ??= ImportProgressStore.Current?.Read()?.Id;
        if (!Guid.TryParseExact(operationId, "N", out _)) throw new IOException("A committed operation is required.");
        Send("maintenance", operationId);
    }
    private async Task Receive()
    {
        var parentLost = true;
        try
        {
            while (await ReadMessage(_reader, Stopping) is { } message)
            {
                if (message.Type == "bind") _bind.TrySetResult();
                if (message.Type == "setup-result") _setupReply?.TrySetResult(message.Value!);
                if (message.Type == "setup-error") _setupReply?.TrySetException(new IOException(message.Value));
                if (message.Type == "stop") { parentLost = false; break; }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            parentLost &= !_stopping.IsCancellationRequested;
            _stopping.Cancel();
            StopRequested?.Invoke();
            // The owning coordinator vanished. Give graceful disposal a bounded opportunity,
            // then release OS handles even if a business shutdown hook is stuck.
            if (parentLost) _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(15)); Environment.Exit(1); });
        }
    }
    internal void Send(string type, string? value = null)
    {
        lock (_sendGate) _writer.WriteLine(JsonSerializer.Serialize(new Message(type, value), Json));
    }
    internal static async Task<Message?> ReadMessage(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line == null) return null;
        if (line.Length > 16384) throw new IOException("Setup IPC message is too large.");
        return JsonSerializer.Deserialize<Message>(line, Json) ?? throw new IOException("Invalid Setup IPC message.");
    }
    internal static ProcessStartInfo ChildStartInfo(string role, string[] arguments, string pipeName, string secret)
    {
        var executable = Environment.ProcessPath ?? throw new IOException("Cannot locate the Setup executable.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        foreach (var argument in arguments.Where(a => !a.StartsWith(RoleArgument, StringComparison.Ordinal))) info.ArgumentList.Add(argument);
        info.ArgumentList.Add(RoleArgument + role);
        info.Environment[PipeEnvironment] = pipeName;
        info.Environment[SecretEnvironment] = secret;
        return info;
    }
    public async ValueTask DisposeAsync()
    {
        if (ReferenceEquals(Current, this)) Current = null;
        _stopping.Cancel();
        lock (_sendGate)
        {
            try { _writer.Dispose(); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        }
        _reader.Dispose();
        await _pipe.DisposeAsync();
    }
}
