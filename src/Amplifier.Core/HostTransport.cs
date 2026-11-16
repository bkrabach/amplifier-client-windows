using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Amplifier.Core;

/// One registered origin, no cookies, redirects, untrusted certificates or ambient browser state.
public sealed class HostTransport : IDisposable
{
    private readonly HttpClient client;
    private readonly SemaphoreSlim requestGate = new(1, 1);
    public Endpoint Endpoint { get; }
    public string ClientId { get; }
    public bool RestrictedCredential { get; }
    public HostTransport(Endpoint endpoint, string? token = null, string? clientId = null,
        HttpMessageHandler? handler = null)
    {
        Endpoint = endpoint;
        RestrictedCredential = token != null;
        ClientId = clientId ?? Guid.NewGuid().ToString("N");
        if (token != null)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(token, "^amc_[a-f0-9]{32}\\.[A-Za-z0-9_-]{64}$"))
                throw new ArgumentException("Only approved restricted device credentials are supported.");
            var dot = token.IndexOf('.');
            if (dot != 36) throw new ArgumentException("Invalid restricted device credential.");
            ClientId = "paired-" + token[4..dot] + "-" + ClientId;
        }
        client = new HttpClient(handler ?? new HttpClientHandler
        { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip })
        { Timeout = TimeSpan.FromSeconds(25) };
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (token != null) client.DefaultRequestHeaders.Add("X-Amplifier-Client", ClientId);
        client.DefaultRequestHeaders.Add("X-Amplifier-State-Transport", "delta-v1");
    }

    public async Task<JsonElement> RequestAsync(string path, object? body, CancellationToken cancellation)
    {
        await requestGate.WaitAsync(cancellation);
        try
        {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, Endpoint.Resource(path));
        if (body != null) request.Content = JsonContent.Create(body, options: Json.Options);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new HostException((int)response.StatusCode, response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Access is unavailable. Check the connection approval.",
                HttpStatusCode.NotFound => "This host does not support the requested client feature.",
                HttpStatusCode.Conflict => "The observation changed; refresh before continuing.",
                _ => "The service request did not complete (" + (int)response.StatusCode + ")."
            });
        const int maximum = 2 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Response exceeds the client limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        var bytes = new byte[16384];
        for (int n; (n = await stream.ReadAsync(bytes, cancellation)) != 0;)
        {
            if (buffer.Length + n > maximum) throw new InvalidDataException("Response exceeds the client limit.");
            buffer.Write(bytes, 0, n);
        }
        using var doc = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 48 });
        return doc.RootElement.Clone();
        }
        finally { requestGate.Release(); }
    }
    public void Dispose() => client.Dispose();
}
