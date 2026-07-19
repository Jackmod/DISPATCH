using System.Security.Cryptography;

namespace Dispatch.Core.Infrastructure;

/// <summary>SHA-256 hashing, in the one form the whole app uses.</summary>
/// <remarks>
/// Every hash in Dispatch is a lower-case hex SHA-256 with a <c>sha256:</c>
/// prefix, so the journal, the install record, the quarantine manifest and the
/// integrity auditor all compare like with like. Streaming rather than reading
/// the whole file keeps a several-hundred-megabyte archive from being pulled
/// into memory to be hashed.
/// </remarks>
public static class Hashing
{
    /// <summary>Hashes a file, returning <c>sha256:</c> followed by lower-case hex.</summary>
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: true);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Hashes bytes already in memory.</summary>
    public static string Sha256(ReadOnlySpan<byte> data) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
