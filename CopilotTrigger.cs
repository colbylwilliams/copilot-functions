using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Octokit;

namespace Copilot.Functions;

public class CopilotTrigger
{
    public const string ProductHeaderName = "MSDevPlatform";

    private static readonly string? CopilotSecret = null;
    public static readonly string ProductHeaderVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

    public static ProductHeaderValue ProductHeader => new(ProductHeaderName, ProductHeaderVersion);

    [Function(nameof(CopilotTrigger))]
    public async Task Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "copilot")] HttpRequest req,
        FunctionContext functionContext, CancellationToken cancellationToken)
    {
        var httpContext = functionContext.GetHttpContext() ?? throw new InvalidOperationException("HttpContext is null");

        if (!req.HasJsonContentType())
        {
            throw new InvalidOperationException("GitHub event does not have the correct content type.");
        }

        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        if (!VerifySignature(req, CopilotSecret, body))
        {
            throw new InvalidOperationException("GitHub event failed signature validation.");
        }

        var github = GetCopilotClient(req);

        var me = await github.User.Current();

        var id = functionContext.InvocationId.Replace("-", string.Empty);

        var completion = new ChatCompletions
        {
            Id = $"{id}0",
            Choices = [new() { Index = 0, Delta = new() { Content = $"Hello {me.Login}, I am the Developer Platform AI." } }]
        };

        httpContext.Response.Headers.ContentType = "text/event-stream";

        await WriteChunkResponseAsync(httpContext, completion, cancellationToken);
        await WriteFinalResponseAsync(httpContext, $"{id}1", cancellationToken);
    }

    private static readonly JsonSerializerOptions jsonOption = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private static async Task WriteChunkResponseAsync(HttpContext http, ChatCompletions completions, CancellationToken cancellationToken)
    {
        await http.Response.WriteAsync("data: ", cancellationToken: cancellationToken);
        await JsonSerializer.SerializeAsync(http.Response.Body, completions, jsonOption, cancellationToken);
        await http.Response.WriteAsync("\n\n", cancellationToken: cancellationToken);
        await http.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteFinalResponseAsync(HttpContext http, string id, CancellationToken cancellationToken)
    {
        await WriteChunkResponseAsync(http, ChatCompletions.Final(id), cancellationToken);

        await http.Response.WriteAsync("data: [DONE]\n\n", cancellationToken: cancellationToken);
        await http.Response.Body.FlushAsync(cancellationToken);
    }

    private static GitHubClient GetCopilotClient(HttpRequest req)
        => req.Headers.TryGetValue("X-GitHub-Token", out var tokens) && tokens is [{ } token]
        ? new GitHubClient(ProductHeader) { Credentials = new Credentials(token, AuthenticationType.Bearer) }
        : throw new InvalidOperationException("GitHub token not found.");

    private static bool VerifySignature(HttpRequest req, string? secret, string body)
    {
        var isSignatureExpected = !string.IsNullOrEmpty(secret);

        if (req.Headers.TryGetValue("X-GitHub-Signature", out var signatures) && signatures is [{ } signature])
        {
            if (!isSignatureExpected)
            {
                return false; // signature wasn't expected, but we got one.
            }

            var keyBytes = Encoding.UTF8.GetBytes(secret!);
            var bodyBytes = Encoding.UTF8.GetBytes(body);

            var hash = HMACSHA256.HashData(keyBytes, bodyBytes);
            var hashHex = Convert.ToHexString(hash);
            var expectedHeader = $"sha256={hashHex.ToLower(CultureInfo.InvariantCulture)}";

            return signature == expectedHeader;
        }
        else if (!isSignatureExpected)
        {
            return true; // signature wasn't expected, nothing to do.
        }
        else
        {
            return false; // signature expected, but we didn't get one.
        }
    }
}
