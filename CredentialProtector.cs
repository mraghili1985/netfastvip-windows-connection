using System;
using System.Security.Cryptography;
using System.Text;

internal static class CredentialProtector
{
    private const string Prefix = "dpapi:";

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;

        try
        {
            var data = Encoding.UTF8.GetBytes(value);
            var protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protectedData);
        }
        catch
        {
            return value;
        }
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;

        try
        {
            var protectedData = Convert.FromBase64String(value[Prefix.Length..]);
            var data = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return "";
        }
    }
}
