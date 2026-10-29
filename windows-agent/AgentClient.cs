using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WindowsAgent;

/// <summary>
/// Manages the persistent WebSocket connection between the Windows laptop and the relay server.
/// </summary>
public class AgentClient
{
    private readonly string _serverUrl;
    private readonly string _deviceId;
    private string _secretToken;
    private ClientWebSocket? _activeClient;

    public bool IsConnected { get; private set; }
    public string DeviceId => _deviceId;
    public string ServerUrl => _serverUrl;
    public string? PairedPhoneId { get; private set; }
    public string? CurrentPairingCode { get; private set; }
    public int CurrentPairingCodeExpiresIn { get; private set; }
    public static bool HasPairedToken => !string.IsNullOrEmpty(LoadSavedToken());

    public event Action<bool>? ConnectionStatusChanged;
    public event Action<string>? AuthenticationFailed;
    public event Action<string, int>? PairingCodeReceived;
    public event Action<string>? PairingCompleted;
    public event Action<string, bool>? CommandExecuted;

    public const string DefaultServerUrl = "wss://autoflow-84pj.onrender.com";

    public AgentClient(
        string? serverUrl = null,
        string? deviceId = null,
        string? secretToken = null)
    {
        _serverUrl = serverUrl 
            ?? Environment.GetEnvironmentVariable("SERVER_URL") 
            ?? LoadSavedServerUrl()
            ?? DefaultServerUrl;

        _deviceId = deviceId 
            ?? Environment.GetEnvironmentVariable("DEVICE_ID") 
            ?? Environment.MachineName;

        _secretToken = secretToken 
            ?? Environment.GetEnvironmentVariable("AGENT_SECRET") 
            ?? LoadSavedToken()
            ?? "dev-secret-token-123";
    }

    public static string? LoadSavedServerUrl()
    {
        try
        {
            string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_url.txt");
            if (File.Exists(localFile))
            {
                string url = File.ReadAllText(localFile).Trim();
                if (!string.IsNullOrEmpty(url)) return url;
            }

            string appDataFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteLaptopAgent", "server_url.txt");
            if (File.Exists(appDataFile))
            {
                string url = File.ReadAllText(appDataFile).Trim();
                if (!string.IsNullOrEmpty(url)) return url;
            }
        }
        catch { }
        return null;
    }

    public static void SaveServerUrl(string url)
    {
        try
        {
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteLaptopAgent");
            Directory.CreateDirectory(appDataDir);
            File.WriteAllText(Path.Combine(appDataDir, "server_url.txt"), url.Trim());
        }
        catch { }
    }

    private static string GetTokenFilePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteLaptopAgent");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "paired_token.txt");
    }

    public static string? LoadSavedToken()
    {
        try
        {
            string file = GetTokenFilePath();
            if (File.Exists(file))
            {
                string token = File.ReadAllText(file).Trim();
                if (!string.IsNullOrEmpty(token)) return token;
            }
        }
        catch { }
        return null;
    }

    public static void SaveToken(string token)
    {
        try
        {
            string file = GetTokenFilePath();
            File.WriteAllText(file, token.Trim());
            Console.WriteLine($"[Agent Security] Paired token securely saved to disk.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agent Security Error] Failed to save paired token: {ex.Message}");
        }
    }

    public static void ClearSavedToken()
    {
        try
        {
            string file = GetTokenFilePath();
            if (File.Exists(file))
            {
                File.Delete(file);
                Console.WriteLine("[Agent Security] Stale or invalid token cleared from disk.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agent Security Error] Failed to clear token: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the agent connection loop with automatic exponential reconnection.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Agent] Connecting to relay server at {_serverUrl}");
        Console.WriteLine($"[Agent] Device ID: {_deviceId}");

        int reconnectDelaySeconds = 2;
        const int maxDelaySeconds = 30;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = new ClientWebSocket();
            _activeClient = client;

            try
            {
                var serverUri = new Uri(_serverUrl);
                Console.WriteLine($"\n[Agent] Initiating WebSocket connection to {serverUri}...");

                await client.ConnectAsync(serverUri, cancellationToken);
                Console.WriteLine("[Agent] WebSocket connected! Authenticating...");

                // Reset reconnect delay on successful connection
                reconnectDelaySeconds = 2;

                // 1. Send Authentication message
                await AuthenticateAsync(client, cancellationToken);

                // 2. Run listener and heartbeat concurrently
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var receiveTask = ReceiveLoopAsync(client, linkedCts.Token);
                var heartbeatTask = HeartbeatLoopAsync(client, linkedCts.Token);

                // Wait until either socket drops or cancellation is requested
                await Task.WhenAny(receiveTask, heartbeatTask);
                linkedCts.Cancel();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("[Agent] Shutdown requested. Exiting cleanly.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Agent Connection Error] {ex.Message}");
            }
            finally
            {
                if (_activeClient == client)
                {
                    _activeClient = null;
                }
            }

            if (IsConnected)
            {
                IsConnected = false;
                ConnectionStatusChanged?.Invoke(false);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"[Agent] Disconnected. Reconnecting in {reconnectDelaySeconds}s...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(reconnectDelaySeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Exponential backoff
                reconnectDelaySeconds = Math.Min(reconnectDelaySeconds * 2, maxDelaySeconds);
            }
        }
    }

    private async Task AuthenticateAsync(ClientWebSocket client, CancellationToken token)
    {
        string currentToken = LoadSavedToken() ?? _secretToken;
        _secretToken = currentToken;

        var authMessage = new
        {
            type = "auth",
            deviceId = _deviceId,
            deviceType = "laptop",
            token = currentToken
        };

        await SendJsonAsync(client, authMessage, token);
    }

    private async Task HeartbeatLoopAsync(ClientWebSocket client, CancellationToken token)
    {
        while (!token.IsCancellationRequested && client.State == WebSocketState.Open)
        {
            await Task.Delay(TimeSpan.FromSeconds(25), token);

            if (client.State == WebSocketState.Open)
            {
                await SendJsonAsync(client, new { type = "ping" }, token);
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket client, CancellationToken token)
    {
        var buffer = new byte[4096];

        while (!token.IsCancellationRequested && client.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("[Agent] Server closed WebSocket connection.");
                    return;
                }
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            string json = Encoding.UTF8.GetString(ms.ToArray());
            await HandleMessageAsync(client, json, token);
        }
    }

    private async Task HandleMessageAsync(ClientWebSocket client, string json, CancellationToken token)
    {
        try
        {
            var doc = JsonNode.Parse(json);
            if (doc == null) return;

            string? type = doc["type"]?.GetValue<string>();

            switch (type)
            {
                case "auth_result":
                    bool success = doc["success"]?.GetValue<bool>() ?? false;
                    string message = doc["message"]?.GetValue<string>() ?? string.Empty;
                    if (success)
                    {
                        IsConnected = true;
                        ConnectionStatusChanged?.Invoke(true);
                        Console.WriteLine($"[Agent Status] Authentication successful: {message}");
                        Console.WriteLine("[Agent Status] Online and listening for remote phone commands...");
                    }
                    else
                    {
                        IsConnected = false;
                        ConnectionStatusChanged?.Invoke(false);
                        Console.WriteLine($"[Agent Error] Authentication rejected: {message}");

                        AuthenticationFailed?.Invoke(message);
                        Console.WriteLine("[Agent Action] Laptop requires pairing. Select 'Pair New Phone...' from the system tray menu.");
                    }
                    break;

                case "command":
                    string? requestId = doc["requestId"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
                    string? command = doc["command"]?.GetValue<string>()?.ToUpperInvariant();
                    Console.WriteLine($"\n[Agent Command Received] -> {command} (RequestId: {requestId})");

                    bool execSuccess = false;
                    string? errorMessage = null;

                    switch (command)
                    {
                        case "LOCK":
                            execSuccess = SystemOperations.Lock();
                            break;
                        case "SLEEP":
                            execSuccess = SystemOperations.Sleep();
                            break;
                        case "RESTART":
                            // Immediate reboot for remote command
                            execSuccess = SystemOperations.Restart(timeoutSeconds: 0);
                            break;
                        case "SHUTDOWN":
                            // Immediate shutdown for remote command
                            execSuccess = SystemOperations.Shutdown(timeoutSeconds: 0);
                            break;
                        default:
                            errorMessage = $"Unknown command: {command}";
                            Console.WriteLine($"[Agent Error] {errorMessage}");
                            break;
                    }

                    // Send execution result back to server -> phone
                    var resultPayload = new
                    {
                        type = "command_result",
                        requestId = requestId,
                        command = command,
                        success = execSuccess,
                        error = errorMessage
                    };

                    await SendJsonAsync(client, resultPayload, token);
                    Console.WriteLine($"[Agent Result Sent] -> {command} Success: {execSuccess}");
                    CommandExecuted?.Invoke(command ?? "UNKNOWN", execSuccess);
                    break;

                case "pairing_code_created":
                    string? pinCode = doc["code"]?.GetValue<string>();
                    int expiresIn = doc["expiresInSeconds"]?.GetValue<int>() ?? 300;
                    CurrentPairingCode = pinCode;
                    CurrentPairingCodeExpiresIn = expiresIn;
                    PairingCodeReceived?.Invoke(pinCode ?? string.Empty, expiresIn);
                    Console.WriteLine();
                    Console.WriteLine("==============================================================");
                    Console.WriteLine("                  NEW DEVICE PAIRING PIN                      ");
                    Console.WriteLine("==============================================================");
                    Console.WriteLine($"  Laptop Name  : {_deviceId}");
                    Console.WriteLine($"  Pairing Code : [ {pinCode} ]");
                    Console.WriteLine($"  Expires In   : {expiresIn / 60} minutes");
                    Console.WriteLine();
                    Console.WriteLine("  Open Remote Laptop on your phone and enter this PIN.");
                    Console.WriteLine("  Waiting for phone pairing completion...");
                    Console.WriteLine("==============================================================");
                    Console.WriteLine();
                    break;

                case "pairing_completed":
                    string? newToken = doc["token"]?.GetValue<string>();
                    string? phoneId = doc["phoneDeviceId"]?.GetValue<string>();
                    CurrentPairingCode = null;
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        _secretToken = newToken;
                        PairedPhoneId = phoneId;
                        SaveToken(newToken);
                        IsConnected = true;
                        ConnectionStatusChanged?.Invoke(true);
                        PairingCompleted?.Invoke(phoneId ?? "Phone");
                        Console.WriteLine();
                        Console.WriteLine("==============================================================");
                        Console.WriteLine("              PAIRING COMPLETED SUCCESSFULLY!                 ");
                        Console.WriteLine("==============================================================");
                        Console.WriteLine($"  Paired Phone : {phoneId}");
                        Console.WriteLine($"  Token Saved  : {newToken[..Math.Min(12, newToken.Length)]}...");
                        Console.WriteLine("  Laptop is now securely linked to your phone!");
                        Console.WriteLine("==============================================================");
                        Console.WriteLine();
                    }
                    break;

                case "pong":
                    // Heartbeat acknowledged
                    break;

                case "error":
                    string? err = doc["message"]?.GetValue<string>();
                    Console.WriteLine($"[Agent Server Error] {err}");
                    break;

                default:
                    Console.WriteLine($"[Agent] Unhandled message: {json}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agent Error Processing Message] {ex.Message}");
        }
    }

    /// <summary>
    /// Requests a 6-digit numeric pairing PIN code directly on the active connection.
    /// </summary>
    public async Task RequestPairingCodeAsync(CancellationToken cancellationToken = default)
    {
        var client = _activeClient;
        if (client != null && client.State == WebSocketState.Open)
        {
            var request = new
            {
                type = "request_pairing_code",
                deviceId = _deviceId
            };
            await SendJsonAsync(client, request, cancellationToken);
            return;
        }

        // If background client is not connected, connect a dedicated client
        await StartPairingAsync(cancellationToken);
    }

    /// <summary>
    /// Unpairs the laptop by deleting the local token.
    /// </summary>
    public void Unpair()
    {
        ClearSavedToken();
        _secretToken = "dev-secret-token-123";
        PairedPhoneId = null;
        IsConnected = false;
        ConnectionStatusChanged?.Invoke(false);
        AuthenticationFailed?.Invoke("Pairing removed by user.");
    }

    /// <summary>
    /// Starts pairing mode: connects to the relay, requests a 6-digit PIN, and waits for phone pairing.
    /// </summary>
    public async Task StartPairingAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n[Agent] Initiating Dynamic Pairing Mode...");
        Console.WriteLine($"[Agent] Connecting to relay server at {_serverUrl}");

        using var client = new ClientWebSocket();
        var serverUri = new Uri(_serverUrl);
        await client.ConnectAsync(serverUri, cancellationToken);

        // Send request for pairing code
        var request = new
        {
            type = "request_pairing_code",
            deviceId = _deviceId
        };
        await SendJsonAsync(client, request, cancellationToken);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receiveTask = ReceiveLoopAsync(client, linkedCts.Token);
        var heartbeatTask = HeartbeatLoopAsync(client, linkedCts.Token);

        await Task.WhenAny(receiveTask, heartbeatTask);
        linkedCts.Cancel();
    }

    private static async Task SendJsonAsync(ClientWebSocket client, object payload, CancellationToken token)
    {
        if (client.State != WebSocketState.Open) return;

        string json = JsonSerializer.Serialize(payload);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await client.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            token
        );
    }
}
