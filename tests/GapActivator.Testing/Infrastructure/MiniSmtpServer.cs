using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GapActivator.Testing;

public sealed record ReceivedMail(string From, IReadOnlyList<string> Recipients, string Data);

/// <summary>Minimal in-process SMTP server on 127.0.0.1 for the e-mail sender tests. No TLS.</summary>
public sealed class MiniSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<ReceivedMail> _received = [];
    private readonly List<TcpClient> _clients = [];
    private readonly Task _acceptLoop;

    public MiniSmtpServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    /// <summary>When set, EHLO advertises AUTH PLAIN.</summary>
    public bool AdvertiseAuthPlain { get; init; }

    /// <summary>When set, the server accepts connections but never sends the 220 greeting.</summary>
    public bool NeverGreet { get; init; }

    /// <summary>When set, RCPT TO is answered with this reply line, for example "550 no such user".</summary>
    public string? RejectRecipientWith { get; init; }

    /// <summary>When set, AUTH is answered with this reply line, for example "535 5.7.8 Authentication credentials invalid".</summary>
    public string? RejectAuthWith { get; init; }

    public IReadOnlyList<ReceivedMail> Received
    {
        get
        {
            lock (_received)
            {
                return [.. _received];
            }
        }
    }

    public string? AuthenticatedUser { get; private set; }

    public string? AuthenticatedPassword { get; private set; }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        lock (_clients)
        {
            foreach (TcpClient client in _clients)
            {
                client.Dispose();
            }
        }

        await _acceptLoop;
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (_clients)
            {
                _clients.Add(client);
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            if (NeverGreet)
            {
                await Task.Delay(Timeout.Infinite, _stop.Token);
                return;
            }

            using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 localhost ready");
            string? from = null;
            List<string> recipients = [];

            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                string verb = line.Split(' ')[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO":
                        if (AdvertiseAuthPlain)
                        {
                            await writer.WriteLineAsync("250-localhost");
                            await writer.WriteLineAsync("250 AUTH PLAIN");
                        }
                        else
                        {
                            await writer.WriteLineAsync("250 localhost");
                        }

                        break;
                    case "AUTH":
                        if (RejectAuthWith is { } authRejection)
                        {
                            await writer.WriteLineAsync(authRejection);
                            break;
                        }

                        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        string payload = words.Length > 2 ? words[2] : await AskAsync(reader, writer);
                        string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Split('\0');
                        AuthenticatedUser = parts[1];
                        AuthenticatedPassword = parts[2];
                        await writer.WriteLineAsync("235 2.7.0 Authentication successful");
                        break;
                    case "MAIL":
                        from = Between(line);
                        recipients = [];
                        await writer.WriteLineAsync("250 OK");
                        break;
                    case "RCPT":
                        if (RejectRecipientWith is { } rejection)
                        {
                            await writer.WriteLineAsync(rejection);
                        }
                        else
                        {
                            recipients.Add(Between(line));
                            await writer.WriteLineAsync("250 OK");
                        }

                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        string data = await ReadDataAsync(reader);
                        lock (_received)
                        {
                            _received.Add(new ReceivedMail(from ?? string.Empty, recipients, data));
                        }

                        await writer.WriteLineAsync("250 OK queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 Bye");
                        return;
                    default:
                        await writer.WriteLineAsync("250 OK");
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    private async Task<string> AskAsync(StreamReader reader, StreamWriter writer)
    {
        await writer.WriteLineAsync("334 ");
        return await reader.ReadLineAsync(_stop.Token) ?? string.Empty;
    }

    private async Task<string> ReadDataAsync(StreamReader reader)
    {
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(_stop.Token) is { } line && line != ".")
        {
            data.AppendLine(line);
        }

        return data.ToString();
    }

    private static string Between(string line)
    {
        int start = line.IndexOf('<', StringComparison.Ordinal) + 1;
        int end = line.IndexOf('>', StringComparison.Ordinal);
        return line[start..end];
    }
}
