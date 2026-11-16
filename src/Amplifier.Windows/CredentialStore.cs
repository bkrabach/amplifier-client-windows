using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Amplifier.Windows;

internal sealed class CredentialStore(string directory)
{
    private string PathFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid registration identity.");
        return Path.Combine(directory, id + ".bin");
    }
    public string Read(string id)
    {
        var bytes = Native.Protect(File.ReadAllBytes(PathFor(id)), decrypt: true);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Write(string id, string token)
    {
        Directory.CreateDirectory(directory);
        var bytes = Encoding.UTF8.GetBytes(token);
        try
        {
            var encrypted = Native.Protect(bytes);var path = PathFor(id);var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, encrypted);File.Move(temporary, path, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Remove(string id) => File.Delete(PathFor(id));
}
