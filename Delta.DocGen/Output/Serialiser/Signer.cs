using System.Security.Cryptography;
using System.Text;
using Delta.DocGen.Model;

namespace Delta.DocGen.Output.Serialiser;

public static class Signer
{
    public static Envelope Sign(Envelope envelope)
    {
        var unsigned = envelope with { Signature = null };
        var canonical = CanonicalJson.Serialise(unsigned);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        var digest = Convert.ToHexString(hash).ToLowerInvariant();
        return envelope with { Signature = new SignatureRecord("SHA-256", digest) };
    }
}
