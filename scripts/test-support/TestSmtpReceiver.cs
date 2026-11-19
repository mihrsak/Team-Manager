#nullable disable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class TestSmtpReceiver : IDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private volatile bool stopping;
    private int rejected;
    public readonly ConcurrentQueue<string> Messages = new ConcurrentQueue<string>();
    public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    public int Rejected { get { return rejected; } }

    public TestSmtpReceiver()
    {
        listener.Start();
        Task.Run(() => Serve());
    }

    private async Task Serve()
    {
        while (!stopping)
        {
            try
            {
                using (var client = await listener.AcceptTcpClientAsync())
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII))
                using (var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
                {
                    await writer.WriteLineAsync("220 localhost Test SMTP");
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase) &&
                            Interlocked.CompareExchange(ref rejected, 1, 0) == 0)
                            await writer.WriteLineAsync("451 Temporary test failure");
                        else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("354 End with a dot");
                            var message = new StringBuilder();
                            while ((line = await reader.ReadLineAsync()) != null && line != ".")
                                message.AppendLine(line);
                            if (line == null) break;
                            Messages.Enqueue(message.ToString());
                            await writer.WriteLineAsync("250 Queued");
                        }
                        else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("221 Bye");
                            break;
                        }
                        else await writer.WriteLineAsync("250 OK");
                    }
                }
            }
            catch (IOException) { }
            catch (SocketException) { if (stopping) return; throw; }
            catch (ObjectDisposedException) { if (stopping) return; throw; }
        }
    }

    public void Dispose() { stopping = true; listener.Stop(); }
}
