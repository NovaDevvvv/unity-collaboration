using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal sealed class HostHeaderProxy : IDisposable
{
    private readonly int targetPort;
    private readonly TcpListener listener;

    internal HostHeaderProxy(int targetPort, int listenPort)
    {
        this.targetPort = targetPort;
        listener = new TcpListener(IPAddress.Loopback, listenPort);
    }

    internal void Start(CancellationToken token)
    {
        listener.Start();
        _ = AcceptLoop(token);
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient incoming = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                _ = Forward(incoming, token);
            }
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
    }

    private async Task Forward(TcpClient incoming, CancellationToken token)
    {
        using (incoming)
        using (TcpClient outgoing = new TcpClient())
        {
            try
            {
                NetworkStream input = incoming.GetStream();
                byte[] headerBytes = await ReadHeaders(input, token).ConfigureAwait(false);
                string headers = Encoding.ASCII.GetString(headerBytes);
                headers = Regex.Replace(headers, @"(?im)^Host:[^\r\n]*$",
                    "Host: 127.0.0.1:" + targetPort);

                await outgoing.ConnectAsync(IPAddress.Loopback, targetPort).ConfigureAwait(false);
                NetworkStream output = outgoing.GetStream();
                byte[] rewritten = Encoding.ASCII.GetBytes(headers);
                await output.WriteAsync(rewritten, 0, rewritten.Length, token).ConfigureAwait(false);

                Task upstream = Pump(input, output, token);
                Task downstream = Pump(output, input, token);
                await Task.WhenAny(upstream, downstream).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
        }
    }

    private static async Task<byte[]> ReadHeaders(NetworkStream stream, CancellationToken token)
    {
        using (MemoryStream bytes = new MemoryStream())
        {
            byte[] one = new byte[1];
            int matched = 0;
            while (bytes.Length < 65536)
            {
                int count = await stream.ReadAsync(one, 0, 1, token).ConfigureAwait(false);
                if (count == 0) throw new IOException("The forwarded connection closed before sending headers.");
                bytes.WriteByte(one[0]);
                bool expected = ((matched == 0 || matched == 2) && one[0] == '\r') ||
                                ((matched == 1 || matched == 3) && one[0] == '\n');
                matched = expected ? matched + 1 : (one[0] == '\r' ? 1 : 0);
                if (matched == 4) return bytes.ToArray();
            }
            throw new IOException("The forwarded request headers were too large.");
        }
    }

    private static async Task Pump(Stream source, Stream destination, CancellationToken token)
    {
        byte[] buffer = new byte[16384];
        while (!token.IsCancellationRequested)
        {
            int count = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
            if (count == 0) return;
            await destination.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        try { listener.Stop(); } catch { }
    }
}
