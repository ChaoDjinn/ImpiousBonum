using System.Security.Cryptography;
using ImpiousBonum.Core.Updates;

namespace ImpiousBonum.Core.Tests;

public sealed class ReleaseSignatureTests
{
    private const string Version = "0.3.0";
    private const string Package = "ImpiousBonum-0.3.0-full.nupkg";
    private const string Hash = "2114F8E7D9509E4758A0A4A14B3E23F07E4B88E870F63AC3E19505146175A916";

    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly byte[] PublicKey = Key.ExportSubjectPublicKeyInfo();
    private static readonly string Document = ReleaseSignature.Create(Key, Version, Package, Hash);

    [Fact]
    public void Accepts_a_release_signed_with_our_key()
    {
        Assert.True(ReleaseSignature.Verify(Document, PublicKey, Version, Package, Hash));
    }

    [Fact]
    public void Hash_case_does_not_matter()
    {
        Assert.True(ReleaseSignature.Verify(Document, PublicKey, Version, Package, Hash.ToLowerInvariant()));
    }

    [Fact]
    public void Accepts_windows_line_endings()
    {
        Assert.True(ReleaseSignature.Verify(Document.Replace("\n", "\r\n"), PublicKey, Version, Package, Hash));
    }

    [Theory]
    [InlineData("0.3.1", Package, Hash)]
    [InlineData(Version, "ImpiousBonum-0.2.0-full.nupkg", Hash)]
    [InlineData(Version, Package, "0000F8E7D9509E4758A0A4A14B3E23F07E4B88E870F63AC3E19505146175A916")]
    [InlineData(Version, Package, "")]
    [InlineData(Version, Package, null)]
    public void Rejects_a_signature_for_a_different_release(string version, string package, string? hash)
    {
        Assert.False(ReleaseSignature.Verify(Document, PublicKey, version, package, hash));
    }

    [Fact]
    public void Rejects_a_release_signed_with_another_key()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forged = ReleaseSignature.Create(other, Version, Package, Hash);

        Assert.False(ReleaseSignature.Verify(forged, PublicKey, Version, Package, Hash));
    }

    [Fact]
    public void Rejects_an_edited_statement()
    {
        var lines = Document.Split('\n');
        var edited = $"{ReleaseSignature.Statement(Version, Package, "0000" + Hash[4..])}\n{lines[1]}\n";

        Assert.False(ReleaseSignature.Verify(edited, PublicKey, Version, Package, "0000" + Hash[4..]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a signature")]
    [InlineData("ImpiousBonum 0.3.0 ImpiousBonum-0.3.0-full.nupkg 2114F8E7D9509E4758A0A4A14B3E23F07E4B88E870F63AC3E19505146175A916\n!!!not base64!!!")]
    public void Rejects_malformed_documents(string document)
    {
        Assert.False(ReleaseSignature.Verify(document, PublicKey, Version, Package, Hash));
    }

    [Fact]
    public void Rejects_everything_without_a_public_key()
    {
        Assert.False(ReleaseSignature.Verify(Document, [], Version, Package, Hash));
    }
}
