using System.Security.Cryptography;
using System.Text;

namespace ImpiousBonum.Core.Updates;

/// <summary>
/// Proves a release came from us. Each release carries a small text asset: one line naming the version, the full
/// package and its SHA-256, then an ECDSA P-256 signature of that line. The app holds the public key; only the release
/// workflow holds the private key. Velopack already checks the download against the SHA-256 in releases.win.json, so
/// once that hash is signed, the package it downloads is ours.
/// </summary>
public static class ReleaseSignature
{
    private const string Product = "ImpiousBonum";

    /// <summary>Name of the release asset holding the signature for <paramref name="version"/>.</summary>
    public static string AssetName(string version) => $"{Product}-{version}-signature.txt";

    public static string Statement(string version, string packageFileName, string sha256) =>
        $"{Product} {version} {packageFileName} {sha256.ToUpperInvariant()}";

    public static string Create(ECDsa privateKey, string version, string packageFileName, string sha256)
    {
        var statement = Statement(version, packageFileName, sha256);
        var signature = privateKey.SignData(Encoding.UTF8.GetBytes(statement), HashAlgorithmName.SHA256);
        return $"{statement}\n{Convert.ToBase64String(signature)}\n";
    }

    /// <summary>
    /// True only when <paramref name="document"/> vouches for exactly this version, package and hash, and was signed
    /// by the holder of the private key matching <paramref name="publicKey"/> (a SubjectPublicKeyInfo).
    /// </summary>
    public static bool Verify(string document, ReadOnlySpan<byte> publicKey, string version, string packageFileName, string? sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256) || publicKey.IsEmpty)
            return false;

        var lines = document.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 2 || !string.Equals(lines[0], Statement(version, packageFileName, sha256), StringComparison.Ordinal))
            return false;

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out _);
            return key.VerifyData(Encoding.UTF8.GetBytes(lines[0]), Convert.FromBase64String(lines[1]), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
