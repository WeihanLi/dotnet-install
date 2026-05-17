using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace DotNetInstall.Tests.ActionScripts;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "dotnet-install-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string GetPath(params string[] segments) =>
        System.IO.Path.Combine([Path, .. segments]);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed record ScriptProcessResult(int ExitCode, string StdOut, string StdErr);

internal sealed record HttpResponseDefinition(int StatusCode, string ContentType, byte[] Body);

internal sealed class TestHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Dictionary<string, HttpResponseDefinition> _responses = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _serverTask;

    public TestHttpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _serverTask = Task.Run(() => RunAsync(_cancellationTokenSource.Token));
    }

    public Uri BaseUri { get; }

    public void AddJson(string pathAndQuery, string json, int statusCode = 200) =>
        _responses[pathAndQuery] = new HttpResponseDefinition(statusCode, "application/json", Encoding.UTF8.GetBytes(json));

    public void AddText(string pathAndQuery, string text, int statusCode = 200, string contentType = "text/plain") =>
        _responses[pathAndQuery] = new HttpResponseDefinition(statusCode, contentType, Encoding.UTF8.GetBytes(text));

    public void AddBinary(string pathAndQuery, byte[] body, int statusCode = 200, string contentType = "application/octet-stream") =>
        _responses[pathAndQuery] = new HttpResponseDefinition(statusCode, contentType, body);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        await using var stream = client.GetStream();

        var request = await ReadRequestHeadersAsync(stream, cancellationToken);
        var key = TryGetRequestTarget(request) ?? "/";
        if (!_responses.TryGetValue(key, out var response))
        {
            response = new HttpResponseDefinition(404, "text/plain", Encoding.UTF8.GetBytes("Not Found"));
        }

        var statusText = response.StatusCode switch
        {
            200 => "OK",
            404 => "Not Found",
            _ => "Status"
        };

        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {response.StatusCode} {statusText}\r\n" +
            $"Content-Type: {response.ContentType}\r\n" +
            $"Content-Length: {response.Body.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n");

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(response.Body, cancellationToken);
    }

    private static async Task<string> ReadRequestHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        using var requestBytes = new MemoryStream();

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            requestBytes.Write(buffer, 0, bytesRead);
            var request = Encoding.ASCII.GetString(requestBytes.GetBuffer(), 0, (int)requestBytes.Length);
            if (request.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return request;
            }
        }

        return Encoding.ASCII.GetString(requestBytes.ToArray());
    }

    private static string? TryGetRequestTarget(string request)
    {
        using var reader = new StringReader(request);
        var requestLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return null;
        }

        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : null;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _listener.Stop();
        _serverTask.Wait(TimeSpan.FromSeconds(5));
        _cancellationTokenSource.Dispose();
    }
}

internal static class ActionScriptTestHost
{
    public static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(System.IO.Path.Combine(directory.FullName, "DotNetInstall.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Unable to locate repository root.");
        }
    }

    public static string ResolveScriptPath(string relativePath) =>
        System.IO.Path.Combine(RepoRoot, relativePath);

    public static async Task<ScriptProcessResult> RunPowerShellFileAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        IDictionary<string, string?>? environmentVariables = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolvePowerShellExecutablePath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
            {
                if (pair.Value is null)
                {
                    startInfo.Environment.Remove(pair.Key);
                }
                else
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ScriptProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static string ResolvePowerShellExecutablePath()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "pwsh.exe", "powershell.exe" }
            : new[] { "pwsh", "powershell" };

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var candidate in candidates)
        {
            foreach (var entry in pathEntries)
            {
                var fullPath = Path.Combine(entry, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return candidates[0];
    }

    public static Dictionary<string, string> ParseGitHubOutputFile(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var markerIndex = line.IndexOf("<<", StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var name = line[..markerIndex];
                var marker = line[(markerIndex + 2)..];
                var valueLines = new List<string>();
                i++;
                while (i < lines.Length && !string.Equals(lines[i], marker, StringComparison.Ordinal))
                {
                    valueLines.Add(lines[i]);
                    i++;
                }

                result[name] = string.Join(Environment.NewLine, valueLines);
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex > 0)
            {
                result[line[..separatorIndex]] = line[(separatorIndex + 1)..];
            }
        }

        return result;
    }

    public static string CreateSha256FileContent(byte[] bytes, string fileName)
    {
        var hash = SHA256.HashData(bytes);
        return $"{Convert.ToHexString(hash).ToLowerInvariant()}  {fileName}";
    }
}
