using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YDM.Tests;

public class MockHttpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _listenTask;

    public int Port { get; }
    public string BaseUrl => $"http://127.0.0.1:{Port}/";
    public byte[] TestData { get; }
    public string CurrentETag { get; set; } = "\"test-etag-v1\"";
    public bool SlowMode { get; set; } = false;

    public MockHttpServer(int dataSize = 1024 * 1024) // 1 MB default test data
    {
        // Generate deterministic test payload
        TestData = new byte[dataSize];
        for (int i = 0; i < dataSize; i++)
        {
            TestData[i] = (byte)(i % 251);
        }

        // Find available port
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        Port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();

        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();

        _listenTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch
            {
                break;
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        string path = req.Url?.AbsolutePath ?? "/";

        try
        {
            if (path == "/redirect")
            {
                res.StatusCode = (int)HttpStatusCode.Redirect;
                res.Headers.Add("Location", $"{BaseUrl}standard");
                res.Headers.Add("Content-Disposition", "attachment; filename=\"redirected_archive.zip\"");
                res.Close();
                return;
            }

            if (path == "/forbidden")
            {
                res.StatusCode = (int)HttpStatusCode.Forbidden;
                res.StatusDescription = "Forbidden";
                byte[] msg = Encoding.UTF8.GetBytes("Access denied");
                await res.OutputStream.WriteAsync(msg);
                res.Close();
                return;
            }

            if (path == "/unknown-length")
            {
                res.StatusCode = (int)HttpStatusCode.OK;
                res.SendChunked = true;
                res.Headers.Add("Accept-Ranges", "none");
                await res.OutputStream.WriteAsync(TestData);
                res.Close();
                return;
            }

            if (path == "/no-range")
            {
                res.StatusCode = (int)HttpStatusCode.OK;
                res.Headers.Add("Accept-Ranges", "none");
                res.ContentType = "application/octet-stream";
                res.ContentLength64 = TestData.Length;
                await res.OutputStream.WriteAsync(TestData);
                res.Close();
                return;
            }

            if (path == "/standard")
            {
                res.Headers.Add("Accept-Ranges", "bytes");
                res.Headers.Add("ETag", CurrentETag);

                string? rangeHeader = req.Headers["Range"];
                if (!string.IsNullOrWhiteSpace(rangeHeader) && rangeHeader.StartsWith("bytes="))
                {
                    string rangeVal = rangeHeader.Substring(6);
                    string[] parts = rangeVal.Split('-');

                    long start = long.Parse(parts[0]);
                    long end = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) 
                        ? long.Parse(parts[1]) 
                        : TestData.Length - 1;

                    if (end >= TestData.Length) end = TestData.Length - 1;

                    if (start > end || start >= TestData.Length)
                    {
                        res.StatusCode = 416; // Range Not Satisfiable
                        res.Close();
                        return;
                    }

                    long count = end - start + 1;
                    res.StatusCode = (int)HttpStatusCode.PartialContent;
                    res.Headers.Add("Content-Range", $"bytes {start}-{end}/{TestData.Length}");
                    res.ContentLength64 = count;

                    if (SlowMode)
                    {
                        await Task.Delay(150);
                    }
                    await res.OutputStream.WriteAsync(TestData, (int)start, (int)count);
                    res.Close();
                    return;
                }

                // Full resource
                res.StatusCode = (int)HttpStatusCode.OK;
                res.ContentLength64 = TestData.Length;
                await res.OutputStream.WriteAsync(TestData);
                res.Close();
                return;
            }

            // Fallback 404
            res.StatusCode = 404;
            res.Close();
        }
        catch
        {
            try { res.Abort(); } catch { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        _cts.Dispose();
    }
}
