// Derived from PKHeX.Core 26.10.18, Saves/Encryption/MemeCrypto/MemeKey.cs.
// Copyright Kaphotics and contributors. GPL-3.0-or-later.
// Restricted to the published save-file key. RSA integers are zero-padded to the full 96-byte width.
using System.Numerics;
using System.Security.Cryptography;
using System.Diagnostics;
using PKHeX.Core;

namespace PokemonMove.Engine;

internal readonly ref struct Gen7MemeKey
{
    private ReadOnlySpan<byte> DER => DER_3;
    private readonly BigInteger D;
    private readonly BigInteger E;
    private readonly BigInteger N;
    private const int chunk = 16;
    private const int SignatureLength = 96;
    public Gen7MemeKey()
    {
        N = new BigInteger(DER.Slice(0x18, 0x61), isUnsigned: true, isBigEndian: true);
        E = new BigInteger(DER.Slice(0x7B, 3), isUnsigned: true, isBigEndian: true);
        D = new BigInteger(D_3, isUnsigned: true, isBigEndian: true);
    }
    internal void AesDecrypt(Span<byte> data)
    {
        var payload = data[..^SignatureLength];
        var sig = data[^SignatureLength..];
        AesDecrypt(payload, sig);
    }

    /// <summary>
    /// Perform Aes Encryption
    /// </summary>
    internal void AesEncrypt(Span<byte> data)
    {
        var payload = data[..^SignatureLength];
        var sig = data[^SignatureLength..];
        AesEncrypt(payload, sig);
    }

    private void AesDecrypt(ReadOnlySpan<byte> data, Span<byte> sig)
    {
        using var aes = GetAesImpl(data);
        Span<byte> temp = stackalloc byte[chunk];
        Span<byte> nextXor = stackalloc byte[chunk];
        temp.Clear(); nextXor.Clear();

        for (var i = sig.Length - chunk; i >= 0; i -= chunk)
        {
            var slice = sig.Slice(i, chunk);
            Xor(temp, slice);
            aes.DecryptEcb(temp, temp);
            temp.CopyTo(slice);
        }

        Xor(temp, sig[^chunk..]);
        GetSubKey(temp, nextXor);
        for (var i = 0; i < sig.Length; i += chunk)
            Xor(sig.Slice(i, chunk), nextXor);

        nextXor.Clear();
        for (var i = 0; i < sig.Length; i += chunk)
        {
            var slice = sig.Slice(i, chunk);
            slice.CopyTo(temp);
            aes.DecryptEcb(slice, slice);
            Xor(slice, nextXor);
            temp.CopyTo(nextXor);
        }
    }

    private void AesEncrypt(Span<byte> data, Span<byte> sig)
    {
        using var aes = GetAesImpl(data);
        Span<byte> temp = stackalloc byte[chunk];
        Span<byte> nextXor = stackalloc byte[chunk];
        temp.Clear(); nextXor.Clear();

        for (var i = 0; i < sig.Length; i += chunk)
        {
            var slice = sig.Slice(i, chunk);
            Xor(slice, temp);
            aes.EncryptEcb(slice, slice);
            slice.CopyTo(temp);
        }

        Xor(temp, sig[..chunk]);
        GetSubKey(temp, nextXor);
        for (var i = 0; i < sig.Length; i += chunk)
            Xor(sig.Slice(i, chunk), nextXor);

        temp.Clear();
        for (var i = sig.Length - chunk; i >= 0; i -= chunk)
        {
            var slice = sig.Slice(i, chunk);
            slice.CopyTo(nextXor);
            aes.EncryptEcb(slice, slice);
            Xor(slice, temp);
            nextXor.CopyTo(temp);
        }
    }

    private IAesCryptographyProvider.IAes GetAesImpl(ReadOnlySpan<byte> payload)
    {
        // The C# implementation of AES isn't fully allocation-free, so some allocation on key & implementation is needed.
        var key = GetAesKey(payload);

        // Don't dispose in this method, let the consumer dispose.
        // no IV -- all zero.
        return RuntimeCryptographyProvider.Aes.Create(key, CipherMode.ECB, PaddingMode.None);
    }

    /// <summary>
    /// Get the AES key for this MemeKey
    /// </summary>
    private byte[] GetAesKey(ReadOnlySpan<byte> data)
    {
        // HashLengthInBytes is 20.
        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        h.AppendData(DER);
        h.AppendData(data);
        h.GetCurrentHash(hash);
        return hash[..chunk].ToArray(); // need a byte[0x10] (not 0x14) for the AES impl
    }

    private static void GetSubKey(ReadOnlySpan<byte> temp, Span<byte> subkey)
    {
        for (var i = 0; i < temp.Length; i += 2) // Imperfect ROL implementation
        {
            byte b1 = temp[i + 0], b2 = temp[i + 1];
            subkey[i + 0] = (byte)((2 * b1) + (b2 >> 7));
            subkey[i + 1] = (byte)(2 * b2);
            if (i + 2 < temp.Length)
                subkey[i + 1] += (byte)(temp[i + 2] >> 7);
        }
        if ((temp[0] & 0x80) != 0)
            subkey[0xF] ^= 0x87;
    }

    private static void Xor(Span<byte> b1, Span<byte> b2)
    {
        Debug.Assert(b1.Length <= b2.Length);
        for (var i = 0; i < b2.Length; i++)
            b1[i] ^= b2[i];
    }

    /// <summary>
    /// Perform Rsa Decryption
    /// </summary>
    internal void RsaPrivate(ReadOnlySpan<byte> data, Span<byte> outSig)
    {
        var M = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        Exponentiate(M, D, outSig);
    }

    /// <summary>
    /// Perform Rsa Encryption
    /// </summary>
    internal void RsaPublic(ReadOnlySpan<byte> data, Span<byte> outSig)
    {
        var M = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        Exponentiate(M, E, outSig);
    }


    private void Exponentiate(BigInteger value, BigInteger exponent, Span<byte> result)
    {
        var bytes = BigInteger.ModPow(value, exponent, N).ToByteArray(isUnsigned: true, isBigEndian: true);
        result.Clear();
        bytes.CopyTo(result[^bytes.Length..]);
    }
    private static ReadOnlySpan<byte> DER_3 => [ 0x30, 0x7C, 0x30, 0x0D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01, 0x05, 0x00, 0x03, 0x6B, 0x00, 0x30, 0x68, 0x02, 0x61, 0x00, 0xB6, 0x1E, 0x19, 0x20, 0x91, 0xF9, 0x0A, 0x8F, 0x76, 0xA6, 0xEA, 0xAA, 0x9A, 0x3C, 0xE5, 0x8C, 0x86, 0x3F, 0x39, 0xAE, 0x25, 0x3F, 0x03, 0x78, 0x16, 0xF5, 0x97, 0x58, 0x54, 0xE0, 0x7A, 0x9A, 0x45, 0x66, 0x01, 0xE7, 0xC9, 0x4C, 0x29, 0x75, 0x9F, 0xE1, 0x55, 0xC0, 0x64, 0xED, 0xDF, 0xA1, 0x11, 0x44, 0x3F, 0x81, 0xEF, 0x1A, 0x42, 0x8C, 0xF6, 0xCD, 0x32, 0xF9, 0xDA, 0xC9, 0xD4, 0x8E, 0x94, 0xCF, 0xB3, 0xF6, 0x90, 0x12, 0x0E, 0x8E, 0x6B, 0x91, 0x11, 0xAD, 0xDA, 0xF1, 0x1E, 0x7C, 0x96, 0x20, 0x8C, 0x37, 0xC0, 0x14, 0x3F, 0xF2, 0xBF, 0x3D, 0x7E, 0x83, 0x11, 0x41, 0xA9, 0x73, 0x02, 0x03, 0x01, 0x00, 0x01 ];
    private static ReadOnlySpan<byte> D_3   => [ 0x00, 0x77, 0x54, 0x55, 0x66, 0x8F, 0xFF, 0x3C, 0xBA, 0x30, 0x26, 0xC2, 0xD0, 0xB2, 0x6B, 0x80, 0x85, 0x89, 0x59, 0x58, 0x34, 0x11, 0x57, 0xAE, 0xB0, 0x3B, 0x6B, 0x04, 0x95, 0xEE, 0x57, 0x80, 0x3E, 0x21, 0x86, 0xEB, 0x6C, 0xB2, 0xEB, 0x62, 0xA7, 0x1D, 0xF1, 0x8A, 0x3C, 0x9C, 0x65, 0x79, 0x07, 0x76, 0x70, 0x96, 0x1B, 0x3A, 0x61, 0x02, 0xDA, 0xBE, 0x5A, 0x19, 0x4A, 0xB5, 0x8C, 0x32, 0x50, 0xAE, 0xD5, 0x97, 0xFC, 0x78, 0x97, 0x8A, 0x32, 0x6D, 0xB1, 0xD7, 0xB2, 0x8D, 0xCC, 0xCB, 0x2A, 0x3E, 0x01, 0x4E, 0xDB, 0xD3, 0x97, 0xAD, 0x33, 0xB8, 0xF2, 0x8C, 0xD5, 0x25, 0x05, 0x42, 0x51 ];
}
