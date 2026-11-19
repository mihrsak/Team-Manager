#nullable disable
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

public sealed class TestImapMailbox : IDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly X509Certificate2 certificate;
    private volatile bool stopping;
    private volatile bool seen;
    public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    public bool Seen { get { return seen; } }
    public string LastCommand { get; private set; }
    private const string Message =
        "From: sender@example.test\r\nTo: mailbox@example.test\r\nMessage-ID: <imap-test@example.test>\r\n" +
        "Subject: QA IMAP issue\r\nDate: Tue, 01 Dec 2026 12:00:00 +0000\r\n" +
        "MIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n" +
        "Content-Transfer-Encoding: base64\r\n\r\nUExBSU4gdGV4dCBmcm9tIElNQVA=\r\n";

    public TestImapMailbox(X509Certificate2 certificate)
    {
        this.certificate = certificate;
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
                using (var stream = new SslStream(client.GetStream()))
                {
                    await stream.AuthenticateAsServerAsync(certificate);
                    using (var reader = new StreamReader(stream, Encoding.ASCII))
                    using (var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
                    {
                        await writer.WriteLineAsync("* OK [CAPABILITY IMAP4rev1] Test IMAP");
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            var space = line.IndexOf(' ');
                            if (space < 1) break;
                            var tag = line.Substring(0, space);
                            var command = line.Substring(space + 1).ToUpperInvariant();
                            LastCommand = command.Split(' ')[0];
                            if (command == "CAPABILITY") await writer.WriteLineAsync("* CAPABILITY IMAP4rev1");
                            else if (command.StartsWith("LIST"))
                            {
                                if (command.EndsWith("\"\""))
                                    await writer.WriteLineAsync("* LIST (\\Noselect) \"/\" \"\"");
                                else await writer.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" INBOX");
                            }
                            else if (command.StartsWith("SELECT"))
                            {
                                await writer.WriteLineAsync("* FLAGS (\\Seen)");
                                await writer.WriteLineAsync("* 1 EXISTS");
                                await writer.WriteLineAsync("* 0 RECENT");
                                await writer.WriteLineAsync("* OK [UIDVALIDITY 1]");
                                await writer.WriteLineAsync("* OK [UIDNEXT 2]");
                                await writer.WriteLineAsync(tag + " OK [READ-WRITE] Selected");
                                continue;
                            }
                            else if (command.StartsWith("UID SEARCH")) await writer.WriteLineAsync(seen ? "* SEARCH" : "* SEARCH 1");
                            else if (command.StartsWith("UID FETCH"))
                            {
                                await writer.WriteLineAsync("* 1 FETCH (UID 1 BODY[] {" + Message.Length + "}");
                                await writer.WriteAsync(Message);
                                await writer.WriteLineAsync(")");
                            }
                            else if (command.StartsWith("UID STORE")) seen = true;
                            else if (command == "LOGOUT")
                            {
                                await writer.WriteLineAsync("* BYE");
                                await writer.WriteLineAsync(tag + " OK Logout");
                                break;
                            }
                            else if (!command.StartsWith("LOGIN"))
                            {
                                await writer.WriteLineAsync(tag + " BAD Unsupported test command");
                                continue;
                            }
                            await writer.WriteLineAsync(tag + " OK Done");
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (System.Security.Authentication.AuthenticationException) { }
            catch (SocketException) { if (stopping) return; throw; }
            catch (ObjectDisposedException) { if (stopping) return; throw; }
        }
    }
    public void Dispose() { stopping = true; listener.Stop(); }
}
