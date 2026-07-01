using AdysTech.CredentialManager;

namespace OmniCard.Services;

public interface ICredentialStore
{
    string? Get(string target);
    void Set(string target, string value);
    void Delete(string target);
    bool Exists(string target);
}

public class CredentialStore : ICredentialStore
{
    public string? Get(string target)
    {
        try
        {
            var cred = CredentialManager.GetCredentials(target);
            return cred?.Password;
        }
        catch
        {
            return null;
        }
    }

    public void Set(string target, string value)
    {
        CredentialManager.SaveCredentials(target, new System.Net.NetworkCredential("OmniCard", value));
    }

    public void Delete(string target)
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

    public bool Exists(string target)
    {
        return Get(target) is not null;
    }
}
