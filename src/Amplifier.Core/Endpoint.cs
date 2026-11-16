namespace Amplifier.Core;

public sealed record Endpoint
{
    public Uri Origin { get; }
    public Endpoint(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath != "/")
            throw new ArgumentException("Enter an HTTPS service address; local development may use loopback HTTP.");
        Origin = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public Uri Resource(string relative)
    {
        if (!relative.StartsWith('/') || relative.StartsWith("//") || relative.Contains('\\'))
            throw new InvalidDataException("The service returned an invalid resource address.");
        var result = new Uri(Origin, relative);
        if (result.GetLeftPart(UriPartial.Authority) != Origin.GetLeftPart(UriPartial.Authority) ||
            result.UserInfo.Length != 0 || result.Fragment.Length != 0)
            throw new InvalidDataException("The service returned a resource outside its origin.");
        return result;
    }

    public Uri Chat(string id) => Resource("/?chat=" + Uri.EscapeDataString(id));
    public override string ToString() => Origin.GetLeftPart(UriPartial.Authority);
}
