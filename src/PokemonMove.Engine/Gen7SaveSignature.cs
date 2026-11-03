// Adapted from PKHeX.Core 26.10.18, Saves/Encryption/MemeCrypto/MemeCrypto.cs.
// Copyright Kaphotics and contributors. GPL-3.0-or-later.
using System.Security.Cryptography;

namespace PokemonMove.Engine;

/// <summary>Sun/Moon and USUM save-file signatures with fixed-width RSA encoding, including leading-zero results.</summary>
public static class Gen7SaveSignature
{
    public static bool Verify(ReadOnlySpan<byte> main)
    {
        return (main.Length is Gen7SaveReader.MainSize or Gen7SaveReader.UsumMainSize)
            && VerifyPayload(main.Slice(Gen7SaveReader.SignatureOffsetFor(main.Length), 128), out var payload)
            && payload.AsSpan(0, 32).SequenceEqual(SHA256.HashData(main.Slice(main.Length - 0x200, Gen7SaveReader.SignatureTableLengthFor(main.Length))));
    }
    public static void Sign(Span<byte> main)
    {
        if (main.Length is not (Gen7SaveReader.MainSize or Gen7SaveReader.UsumMainSize)) throw new InvalidDataException("サン・ムーン／USUMのmainではありません。");
        var signature = main.Slice(Gen7SaveReader.SignatureOffsetFor(main.Length), 128);
        signature.Clear();
        SHA256.HashData(main.Slice(main.Length - 0x200, Gen7SaveReader.SignatureTableLengthFor(main.Length)), signature);
        SignPayload(signature);
    }
    public static void SignPayload(Span<byte> data)
    {
        if (data.Length != 128) throw new ArgumentException("Signature payload must be 128 bytes.", nameof(data));
        Span<byte> hash = stackalloc byte[20]; SHA1.HashData(data[..^8], hash); hash[..8].CopyTo(data[^8..]);
        var key = new Gen7MemeKey(); key.AesEncrypt(data);
        var rsa = data[^96..]; rsa[0] &= 0x7F; key.RsaPrivate(rsa, rsa);
    }
    public static bool VerifyPayload(ReadOnlySpan<byte> input, out byte[] payload)
    {
        payload = [];
        if (input.Length != 128) return false;
        var key = new Gen7MemeKey(); Span<byte> rsa = stackalloc byte[96];
        key.RsaPublic(input[^96..], rsa);
        Span<byte> hash = stackalloc byte[20];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1) rsa[0] |= 0x80;
            var decoded = input.ToArray(); rsa.CopyTo(decoded.AsSpan(decoded.Length - 96)); key.AesDecrypt(decoded);
            SHA1.HashData(decoded.AsSpan(0, decoded.Length - 8), hash);
            if (hash[..8].SequenceEqual(decoded.AsSpan(decoded.Length - 8))) { payload = decoded; return true; }
        }
        return false;
    }
}
