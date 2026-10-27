using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace YDM.Host;

public class NativeMessageRequest
{
    public string? Action { get; set; }
    public string? Url { get; set; }
    public string? Referrer { get; set; }
    public string? Cookies { get; set; }
    public string? UserAgent { get; set; }
    public string? FileName { get; set; }
}

public class NativeMessageResponse
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string? JobId { get; set; }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new BinaryReader(stdin);
        using var writer = new BinaryWriter(stdout);

        while (true)
        {
            try
            {
                // Read 4-byte little-endian message length prefix
                byte[] lengthBytes = new byte[4];
                int bytesRead = 0;
                while (bytesRead < 4)
                {
                    int n = stdin.Read(lengthBytes, bytesRead, 4 - bytesRead);
                    if (n <= 0) return; // Stdin closed by browser
                    bytesRead += n;
                }

                uint messageLength = BitConverter.ToUInt32(lengthBytes, 0);
                if (messageLength == 0 || messageLength > 1024 * 1024)
                {
                    // Message length exceeds 1 MB limit
                    break;
                }

                byte[] buffer = new byte[messageLength];
                int payloadBytesRead = 0;
                while (payloadBytesRead < messageLength)
                {
                    int n = stdin.Read(buffer, payloadBytesRead, (int)messageLength - payloadBytesRead);
                    if (n <= 0) return;
                    payloadBytesRead += n;
                }

                string json = Encoding.UTF8.GetString(buffer);
                var request = JsonSerializer.Deserialize<NativeMessageRequest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var response = await ProcessRequestAsync(request);
                string responseJson = JsonSerializer.Serialize(response);
                byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);

                byte[] resLengthBytes = BitConverter.GetBytes((uint)responseBytes.Length);
                stdout.Write(resLengthBytes, 0, 4);
                stdout.Write(responseBytes, 0, responseBytes.Length);
                stdout.Flush();
            }
            catch
            {
                break;
            }
        }
    }

    private static async Task<NativeMessageResponse> ProcessRequestAsync(NativeMessageRequest? req)
    {
        if (req == null)
        {
            return new NativeMessageResponse { Status = "error", Message = "Null request payload" };
        }

        if (string.Equals(req.Action, "ping", StringComparison.OrdinalIgnoreCase))
        {
            return new NativeMessageResponse { Status = "ok", Message = "YDM Native Host is active" };
        }

        if (string.Equals(req.Action, "download", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(req.Url))
        {
            // Forward request to YDM UI via Named Pipe
            try
            {
                using var pipe = new NamedPipeClientStream(".", "YDM_IPC", PipeDirection.InOut);
                await pipe.ConnectAsync(1500);

                string pipePayload = JsonSerializer.Serialize(req);
                byte[] pipeBytes = Encoding.UTF8.GetBytes(pipePayload);
                byte[] lenPrefix = BitConverter.GetBytes(pipeBytes.Length);

                await pipe.WriteAsync(lenPrefix);
                await pipe.WriteAsync(pipeBytes);
                await pipe.FlushAsync();

                // Read response from UI pipe
                byte[] respLenBytes = new byte[4];
                await pipe.ReadExactlyAsync(respLenBytes, 0, 4);
                int respLen = BitConverter.ToInt32(respLenBytes, 0);

                byte[] respBuffer = new byte[respLen];
                await pipe.ReadExactlyAsync(respBuffer, 0, respLen);

                string pipeRespJson = Encoding.UTF8.GetString(respBuffer);
                return JsonSerializer.Deserialize<NativeMessageResponse>(pipeRespJson) 
                       ?? new NativeMessageResponse { Status = "accepted" };
            }
            catch (Exception ex)
            {
                return new NativeMessageResponse 
                { 
                    Status = "error", 
                    Message = $"YDM Desktop App is not responding: {ex.Message}" 
                };
            }
        }

        return new NativeMessageResponse { Status = "error", Message = $"Unknown action: {req.Action}" };
    }
}
