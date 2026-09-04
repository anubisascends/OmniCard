using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using OmniCard.Interfaces;

namespace OmniCard.Web.Services;

/// <summary>
/// Server-side <see cref="ICredentialStore"/> for the web app. The desktop stores eBay OAuth tokens
/// in the Windows Credential Manager (per-user, interactive) via <c>CredentialStore</c>; that is
/// unusable under an IIS app-pool identity, so the web persists them to a single
/// DataProtection-encrypted JSON file under the data directory instead. The site is single-seller
/// (one shared passphrase), so one shared token blob is the right granularity.
/// </summary>
public sealed class WebCredentialStore : ICredentialStore
{
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly object _lock = new();

    public WebCredentialStore(IDataPathService dataPath, IDataProtectionProvider dataProtection)
    {
        _path = Path.Combine(dataPath.DataDirectory, "web-credentials.dat");
        _protector = dataProtection.CreateProtector("OmniCard.Web.Credentials.v1");
    }

    public string? Get(string target)
    {
        lock (_lock)
        {
            var store = Load();
            return store.GetValueOrDefault(target);
        }
    }

    public void Set(string target, string value)
    {
        lock (_lock)
        {
            var store = Load();
            store[target] = value;
            Save(store);
        }
    }

    public void Delete(string target)
    {
        lock (_lock)
        {
            var store = Load();
            if (store.Remove(target))
                Save(store);
        }
    }

    public bool Exists(string target)
    {
        lock (_lock)
        {
            return Load().ContainsKey(target);
        }
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_path))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var json = _protector.Unprotect(protectedBytes);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            // A corrupt or key-rotated blob is treated as "no credentials" — the user simply
            // reconnects. Never throw from a credential read; it would break unrelated flows.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void Save(Dictionary<string, string> store)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(store);
        var protectedBytes = _protector.Protect(json);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllBytes(_path, protectedBytes);
    }
}
