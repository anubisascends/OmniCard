using System.Text;
using AdysTech.CredentialManager;
using OmniCard.Interfaces;

namespace OmniCard.eBay;

public class CredentialStore : ICredentialStore
{
    // Windows Credential Manager caps a single credential blob at 2560 bytes.
    // AdysTech stores the password as UTF-16 (2 bytes/char), so ~1280 chars is
    // the hard ceiling. eBay OAuth access/refresh tokens are far larger than
    // that, so values are transparently split into chunks well under the limit.
    private const int MaxChunkChars = 1000;

    public string? Get(string target)
    {
        var first = RawGet(target);
        if (first is null)
            return null;

        // Reassemble any continuation chunks written by Set.
        var sb = new StringBuilder(first);
        var index = 1;
        string? next;
        while ((next = RawGet(ChunkKey(target, index))) is not null)
        {
            sb.Append(next);
            index++;
        }
        return sb.ToString();
    }

    public void Set(string target, string value)
    {
        // Clear any previous (possibly multi-chunk) value so stale continuation
        // chunks can never be reassembled onto a new, shorter value.
        Delete(target);

        if (value.Length <= MaxChunkChars)
        {
            RawSet(target, value);
            return;
        }

        var offset = 0;
        var index = 0;
        while (offset < value.Length)
        {
            var length = Math.Min(MaxChunkChars, value.Length - offset);
            var chunk = value.Substring(offset, length);
            RawSet(index == 0 ? target : ChunkKey(target, index), chunk);
            offset += length;
            index++;
        }
    }

    public void Delete(string target)
    {
        RawDelete(target);

        // Remove any continuation chunks.
        var index = 1;
        while (RawExists(ChunkKey(target, index)))
        {
            RawDelete(ChunkKey(target, index));
            index++;
        }
    }

    public bool Exists(string target) => RawGet(target) is not null;

    private static string ChunkKey(string target, int index) => $"{target}::chunk::{index}";

    // --- Raw Windows Credential Manager access (overridable for testing) ---

    protected virtual string? RawGet(string target)
    {
        try
        {
            return CredentialManager.GetCredentials(target)?.Password;
        }
        catch
        {
            return null;
        }
    }

    protected virtual void RawSet(string target, string value)
        => CredentialManager.SaveCredentials(target, new System.Net.NetworkCredential("OmniCard", value));

    protected virtual void RawDelete(string target)
    {
        try
        {
            CredentialManager.RemoveCredentials(target);
        }
        catch
        {
            // Ignore if credential doesn't exist
        }
    }

    protected virtual bool RawExists(string target) => RawGet(target) is not null;
}
