using System.Net.WebSockets;
using System.Text;

namespace Keystream.Actions.StreamerBot;

/// <summary>The production transport: a ClientWebSocket with proper
/// message reassembly (Streamer.bot messages may arrive fragmented).
/// Mirrors <c>Keystream.Actions.Obs.ClientWebSocketTransport</c> line
/// for line in behaviour; the class name differs on purpose - the
/// §147 frozen contract forbids the name
/// <c>ClientWebSocketTransport</c> in this namespace (collision with
/// the Obs namespace).</summary>
public sealed class StreamerBotWebSocketTransport : IStreamerBotTransport
{
    private readonly ClientWebSocket _socket = new();

    public async Task ConnectAsync(Uri uri, CancellationToken cancellation)
        => await _socket.ConnectAsync(uri, cancellation).ConfigureAwait(false);

    public async Task SendAsync(string json, CancellationToken cancellation)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellation)
            .ConfigureAwait(false);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellation)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer, cancellation).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task CloseAsync()
    {
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                    .ConfigureAwait(false);
        }
        catch { /* already gone */ }
    }

    public void Dispose()
    {
        try { _ = CloseAsync(); } catch { }
        _socket.Dispose();
    }
}
