using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Amplifier.Core;

public sealed record PairingRequest(string Id, string Code, string VerificationPath, int ExpiresIn, int Interval);

public sealed class PairingClient(HostTransport transport)
{
    private readonly string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public async Task<PairingRequest> BeginAsync(CancellationToken cancellation)
    {
        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        var value = await transport.RequestAsync("/api/devices/pair", new { name = "Amplifier for Windows", challenge }, cancellation);
        var request = System.Text.Json.JsonSerializer.Deserialize<PairingRequest>(value, Json.Options)
            ?? throw new InvalidDataException("Invalid pairing request.");
        if (!Regex.IsMatch(request.Id, "^[A-Za-z0-9_-]{43}$") ||
            !Regex.IsMatch(request.Code, "^[A-Z2-9]{4}-[A-Z2-9]{4}$") ||
            request.VerificationPath != "/devices/pair?id=" + request.Id ||
            request.ExpiresIn is < 1 or > 600 || request.Interval is < 5 or > 30)
            throw new InvalidDataException("Invalid pairing request.");
        return request;
    }

    public async Task<string> CompleteAsync(PairingRequest request, CancellationToken cancellation)
    {
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        expiry.CancelAfter(TimeSpan.FromSeconds(request.ExpiresIn));
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(request.Interval), expiry.Token);
            var value = await transport.RequestAsync("/api/devices/pair/token", new { id = request.Id, verifier }, expiry.Token);
            switch (value.RequiredText("status"))
            {
                case "pending": continue;
                case "approved":
                    var token = value.RequiredText("token");
                    if (value.RequiredText("scope") != "companion-v1" ||
                        !Regex.IsMatch(token, "^amc_[a-f0-9]{32}\\.[A-Za-z0-9_-]{64}$"))
                        throw new InvalidDataException("Unexpected credential scope or format.");
                    return token;
                default: throw new InvalidOperationException("The connection request was denied or revoked.");
            }
        }
    }
}
