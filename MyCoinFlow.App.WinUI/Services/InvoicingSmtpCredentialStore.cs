using Windows.Security.Credentials;
using WinUiConnectionStrings = MyCoinFlow.WinUI.Data.ConnectionStrings;

namespace MyCoinFlow.WinUI.Services;

public interface IInvoicingSmtpCredentialStore
{
    bool HasPassword();
    string? GetPassword();
    void SavePassword(string password);
    void RemovePassword();
}

public sealed class InvoicingSmtpCredentialStore : IInvoicingSmtpCredentialStore
{
    private const string ResourceName = "MyCoinFlow.Fakturierung.SMTP";

    public bool HasPassword() => GetCredential() is not null;

    public string? GetPassword()
    {
        var credential = GetCredential();
        if (credential is null) return null;
        credential.RetrievePassword();
        return credential.Password;
    }

    public void SavePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Das SMTP-Kennwort darf nicht leer sein.", nameof(password));

        RemovePassword();
        new PasswordVault().Add(new PasswordCredential(
            ResourceName,
            WinUiConnectionStrings.ActiveDatabaseName,
            password));
    }

    public void RemovePassword()
    {
        var credential = GetCredential();
        if (credential is null) return;
        new PasswordVault().Remove(credential);
    }

    private static PasswordCredential? GetCredential()
    {
        try
        {
            return new PasswordVault()
                .FindAllByResource(ResourceName)
                .FirstOrDefault(value => string.Equals(
                    value.UserName,
                    WinUiConnectionStrings.ActiveDatabaseName,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }
}
