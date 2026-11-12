using System;
using System.Security.Cryptography;
using System.Text;

namespace Bakabase.Service.Components.Acquisition.Handoffs;

/// <summary>Hashes the exact stored result string, never a reserialized API projection.</summary>
public static class PostAcquisitionResultFingerprint
{
    public static string Hash(string? results) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(results ?? "")));
}
