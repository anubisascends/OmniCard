namespace OmniCard.Interfaces;

public interface ICredentialStore
{
    string? Get(string target);
    void Set(string target, string value);
    void Delete(string target);
    bool Exists(string target);
}
