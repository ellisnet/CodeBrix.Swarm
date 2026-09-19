using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Swarm.Core;

namespace CodeBrix.Swarm.Queen.Authentication;

/// <summary>
/// Mints the access tokens a swarm runs on, and reads them back. Only the coordinator ever holds the
/// master secret; a Hive or a Worker carries an opaque token and can learn nothing from it.
/// </summary>
/// <remarks>
/// <para>
/// A token is AES-256-GCM: the encoded form is the nonce, then the authentication tag, then the
/// ciphertext, in a URL-safe encoding so it survives being put on a query string. The role's name is
/// also fed in as associated data, so a token cannot be read at all unless the role agrees.
/// </para>
/// <para>
/// THE TWO ROLES DO NOT SHARE A KEY. Each role's key is derived from the one master secret with
/// HKDF-SHA256 and a different information string, so a Hive's token does not even decrypt at the
/// Worker hub. The role is inside the encrypted payload as well, and is checked after decryption, so
/// the refusal does not rest on the key derivation alone.
/// </para>
/// </remarks>
public sealed class SwarmTokenAuthority
{
    /// <summary>
    /// The shortest master secret that is accepted. A secret shorter than this is refused rather than
    /// stretched: the strength of every token in the swarm rests on it.
    /// </summary>
    public const int MinimumMasterSecretLength = 32;

    private const int AesKeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int MinimumTokenByteLength = NonceLength + TagLength + 1;
    private const string KeyInformationPrefix = "CodeBrix.Swarm.Token.AES-GCM.v1|";

    private static readonly TimeSpan IssuedClockAllowance = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly byte[] _hiveKey;
    private readonly byte[] _workerKey;

    /// <summary>
    /// Creates the authority from the consuming application's master secret.
    /// </summary>
    /// <param name="masterSecret">
    /// The secret the whole swarm's tokens are derived from. It is never compiled into this library
    /// and never leaves the coordinator.
    /// </param>
    /// <exception cref="SwarmTokenException">
    /// The secret is missing, is whitespace, or is shorter than
    /// <see cref="MinimumMasterSecretLength" /> characters.
    /// </exception>
    public SwarmTokenAuthority(string masterSecret)
    {
        if (string.IsNullOrWhiteSpace(masterSecret))
        {
            throw new SwarmTokenException(
                "The swarm's master secret is missing. The consuming application supplies it; it is "
                + "never built into this library.");
        }

        var trimmed = masterSecret.Trim();

        if (trimmed.Length < MinimumMasterSecretLength)
        {
            throw new SwarmTokenException(
                $"The swarm's master secret is too short: it must be at least "
                + $"{MinimumMasterSecretLength} characters, and was {trimmed.Length}.");
        }

        var secretBytes = Encoding.UTF8.GetBytes(trimmed);

        try
        {
            _hiveKey = DeriveKey(secretBytes, SwarmRole.Hive);
            _workerKey = DeriveKey(secretBytes, SwarmRole.Worker);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    /// <summary>
    /// Mints a token for the given role that does not expire.
    /// </summary>
    /// <param name="role">The role the token admits.</param>
    /// <returns>The encoded token.</returns>
    public string CreateToken(SwarmRole role) => CreateToken(role, null);

    /// <summary>
    /// Mints a token for the given role that stops being accepted after the given span.
    /// </summary>
    /// <param name="role">The role the token admits.</param>
    /// <param name="lifetime">How long the token remains good for. It must be longer than nothing.</param>
    /// <returns>The encoded token.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The lifetime is zero or negative.</exception>
    public string CreateToken(SwarmRole role, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime), lifetime, "A token's lifetime must be longer than nothing.");
        }

        return CreateToken(role, DateTime.UtcNow + lifetime);
    }

    /// <summary>
    /// Mints a token for the given role that stops being accepted at the given moment.
    /// </summary>
    /// <param name="role">The role the token admits.</param>
    /// <param name="expiresUtc">When the token stops being accepted, in UTC.</param>
    /// <returns>The encoded token.</returns>
    public string CreateToken(SwarmRole role, DateTime expiresUtc)
        => CreateToken(role, (DateTime?)expiresUtc);

    /// <summary>
    /// Reads a token, insisting that it is for the given role.
    /// </summary>
    /// <param name="expectedRole">The role whose hub the token was presented at.</param>
    /// <param name="token">The encoded token.</param>
    /// <returns>What was inside it.</returns>
    /// <exception cref="SwarmTokenException">
    /// The token is missing, is not decodable, was minted for the other role, has been altered, or
    /// has expired.
    /// </exception>
    public SwarmToken ReadToken(SwarmRole expectedRole, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new SwarmTokenException("No access token was presented.");
        }

        var key = KeyFor(expectedRole);
        var roleName = SwarmRoleNames.For(expectedRole);

        byte[] encoded;

        try
        {
            encoded = Base64Url.DecodeFromChars(token.Trim());
        }
        catch (Exception ex)
        {
            throw new SwarmTokenException("The access token is not in the expected encoding.", ex);
        }

        if (encoded.Length < MinimumTokenByteLength)
        {
            throw new SwarmTokenException("The access token is too short to be a token.");
        }

        var plainText = new byte[encoded.Length - NonceLength - TagLength];

        try
        {
            using var aesGcm = new AesGcm(key, TagLength);
            aesGcm.Decrypt(
                encoded.AsSpan(0, NonceLength),
                encoded.AsSpan(NonceLength + TagLength),
                encoded.AsSpan(NonceLength, TagLength),
                plainText,
                Encoding.UTF8.GetBytes(roleName));
        }
        catch (Exception ex)
        {
            //A token minted for the other role lands here too: its key is a different one, so it
            //does not decrypt at all.
            throw new SwarmTokenException(
                $"The access token could not be read as a {roleName} token. It was minted for a "
                + "different role, was made with a different master secret, or has been altered.",
                ex);
        }

        SwarmTokenPayload payload;

        try
        {
            payload = JsonSerializer.Deserialize<SwarmTokenPayload>(plainText, PayloadOptions);
        }
        catch (JsonException ex)
        {
            throw new SwarmTokenException("The access token's contents are not readable.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainText);
        }

        if (payload == null)
        {
            throw new SwarmTokenException("The access token's contents are empty.");
        }

        if (!SwarmRoleNames.TryParse(payload.Role, out var role))
        {
            throw new SwarmTokenException("The access token names a role the swarm does not know.");
        }

        if (role != expectedRole)
        {
            throw new SwarmTokenException(
                $"The access token is a {payload.Role} token and was presented as a {roleName} one.");
        }

        if (!Guid.TryParse(payload.TokenId, out var tokenId) || tokenId == Guid.Empty)
        {
            throw new SwarmTokenException("The access token has no usable identifier.");
        }

        var now = DateTime.UtcNow;
        var issuedUtc = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAtUnixSeconds).UtcDateTime;

        if (issuedUtc > now + IssuedClockAllowance)
        {
            throw new SwarmTokenException("The access token is dated in the future.");
        }

        DateTime? expiresUtc = payload.ExpiresAtUnixSeconds.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnixSeconds.Value).UtcDateTime
            : null;

        if (expiresUtc.HasValue && expiresUtc.Value <= now)
        {
            throw new SwarmTokenException("The access token has expired.");
        }

        return new SwarmToken(tokenId, role, issuedUtc, expiresUtc);
    }

    /// <summary>
    /// Reads a token without throwing when it cannot be accepted.
    /// </summary>
    /// <param name="expectedRole">The role whose hub the token was presented at.</param>
    /// <param name="token">The encoded token.</param>
    /// <param name="result">What was inside it, or null when it was refused.</param>
    /// <returns>True when the token was accepted.</returns>
    public bool TryReadToken(SwarmRole expectedRole, string token, out SwarmToken result)
    {
        try
        {
            result = ReadToken(expectedRole, token);
            return true;
        }
        catch (SwarmTokenException)
        {
            result = null;
            return false;
        }
    }

    private string CreateToken(SwarmRole role, DateTime? expiresUtc)
    {
        var key = KeyFor(role);
        var roleName = SwarmRoleNames.For(role);
        var issuedUtc = DateTime.UtcNow;

        if (expiresUtc.HasValue && expiresUtc.Value <= issuedUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresUtc), expiresUtc, "A token's expiry must be later than the moment it is minted.");
        }

        var payload = new SwarmTokenPayload
        {
            TokenId = Guid.NewGuid().ToString("D"),
            Role = roleName,
            IssuedAtUnixSeconds = new DateTimeOffset(issuedUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
            ExpiresAtUnixSeconds = expiresUtc.HasValue
                ? new DateTimeOffset(expiresUtc.Value, TimeSpan.Zero).ToUnixTimeSeconds()
                : null
        };

        var plainText = JsonSerializer.SerializeToUtf8Bytes(payload, PayloadOptions);

        try
        {
            var encoded = new byte[NonceLength + TagLength + plainText.Length];
            var nonce = encoded.AsSpan(0, NonceLength);
            var tag = encoded.AsSpan(NonceLength, TagLength);
            var cipherText = encoded.AsSpan(NonceLength + TagLength);

            RandomNumberGenerator.Fill(nonce);

            using var aesGcm = new AesGcm(key, TagLength);
            aesGcm.Encrypt(nonce, plainText, cipherText, tag, Encoding.UTF8.GetBytes(roleName));

            return Base64Url.EncodeToString(encoded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainText);
        }
    }

    private byte[] KeyFor(SwarmRole role)
    {
        return role switch
        {
            SwarmRole.Hive => _hiveKey,
            SwarmRole.Worker => _workerKey,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown swarm role.")
        };
    }

    private static byte[] DeriveKey(byte[] secretBytes, SwarmRole role)
    {
        var information = Encoding.UTF8.GetBytes(KeyInformationPrefix + SwarmRoleNames.For(role));
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, secretBytes, AesKeyLength, info: information);
    }
}
