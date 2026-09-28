using System.Security.Cryptography;
using ImpiousBonum.Core.Updates;

// Creates the update signing key and signs releases with it. See "Update signing" in the README.
//   new-key <public key file> <private key file>
//   sign <public key file> <version> <full package>   (private key in the UPDATE_SIGNING_KEY environment variable)

const string PrivateKeyVariable = "UPDATE_SIGNING_KEY";

return args switch
{
    ["new-key", var publicPath, var privatePath] => NewKey(publicPath, privatePath),
    ["sign", var publicPath, var version, var package] => Sign(publicPath, version, package),
    _ => Usage(),
};

static int NewKey(string publicPath, string privatePath)
{
    if (File.Exists(publicPath) || File.Exists(privatePath))
    {
        Console.Error.WriteLine("A key file already exists. Replacing the key stops installed copies accepting updates, so delete it yourself if you really mean to.");
        return 1;
    }

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(publicPath))!);
    File.WriteAllText(publicPath, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) + "\n");
    File.WriteAllText(privatePath, Convert.ToBase64String(key.ExportPkcs8PrivateKey()) + "\n");

    Console.WriteLine($"Public key:  {Path.GetFullPath(publicPath)} (commit this)");
    Console.WriteLine($"Private key: {Path.GetFullPath(privatePath)}");
    Console.WriteLine($"Store the private key as the {PrivateKeyVariable} secret of the 'release' environment, keep a copy somewhere safe offline, then delete this file.");
    return 0;
}

static int Sign(string publicPath, string version, string package)
{
    var privateKey = Environment.GetEnvironmentVariable(PrivateKeyVariable);
    if (string.IsNullOrWhiteSpace(privateKey))
    {
        Console.Error.WriteLine($"{PrivateKeyVariable} isn't set.");
        return 1;
    }

    using var key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey.Trim()), out _);

    // A secret that doesn't match the key built into the app would produce releases nobody can install.
    var publicKey = Convert.FromBase64String(File.ReadAllText(publicPath).Trim());
    if (!key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(publicKey))
    {
        Console.Error.WriteLine($"{PrivateKeyVariable} doesn't match {publicPath}.");
        return 1;
    }

    string sha256;
    using (var stream = File.OpenRead(package))
        sha256 = Convert.ToHexString(SHA256.HashData(stream));

    var packageName = Path.GetFileName(package);
    var document = ReleaseSignature.Create(key, version, packageName, sha256);
    if (!ReleaseSignature.Verify(document, publicKey, version, packageName, sha256))
    {
        Console.Error.WriteLine("The new signature doesn't verify.");
        return 1;
    }

    var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(package))!, ReleaseSignature.AssetName(version));
    File.WriteAllText(output, document);
    Console.WriteLine($"Signed {packageName} ({sha256}) -> {output}");
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  ReleaseSigning new-key <public key file> <private key file>");
    Console.Error.WriteLine($"  ReleaseSigning sign <public key file> <version> <full package>   ({PrivateKeyVariable} holds the private key)");
    return 2;
}
