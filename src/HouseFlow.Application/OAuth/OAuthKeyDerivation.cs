using System.Security.Cryptography;
using System.Text;

namespace HouseFlow.Application.OAuth;

/// <summary>
/// Keys of the OAuth authorization server, derived from <c>Jwt:Key</c> with HKDF-SHA512 (the pattern
/// of the refresh tokens' grace siblings). Deterministic: every replica, and every restart, signs and
/// encrypts with the same keys, without any new secret to provision. Each purpose has its own key,
/// and none is <c>Jwt:Key</c> itself. Rotating <c>Jwt:Key</c> invalidates the OAuth tokens in flight
/// (clients then go through the authorization flow again), like the API's own access tokens.
/// </summary>
public static class OAuthKeyDerivation
{
    public const string SigningInfo = "houseflow/oauth/signing";
    public const string EncryptionInfo = "houseflow/oauth/encryption";

    /// <summary>512-bit HMAC key signing every OAuth token (HS512).</summary>
    public static byte[] SigningKey(string jwtKey) => Derive(jwtKey, SigningInfo, 64);

    /// <summary>256-bit AES key wrapping the content keys of the encrypted tokens (A256KW).</summary>
    public static byte[] EncryptionKey(string jwtKey) => Derive(jwtKey, EncryptionInfo, 32);

    private static byte[] Derive(string jwtKey, string info, int length) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA512, Encoding.UTF8.GetBytes(jwtKey), length,
            info: Encoding.UTF8.GetBytes(info));
}
