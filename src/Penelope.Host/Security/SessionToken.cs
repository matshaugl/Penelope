using System.Security.Cryptography;

namespace Penelope.Host.Security;

public sealed class SessionToken
{
    private readonly byte[] _bytes;

    public SessionToken()
    {
        _bytes = RandomNumberGenerator.GetBytes(32);
        Value = Convert.ToHexString(_bytes);
    }

    public string Value { get; }

    public bool Matches(string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        byte[] given;
        try
        {
            given = Convert.FromHexString(provided);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(_bytes, given);
    }
}
