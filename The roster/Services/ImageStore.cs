using System.Buffers.Binary;
using System.Text.RegularExpressions;
using ClientShowcase.Data;

namespace ClientShowcase.Services;

/// <summary>
/// Stores uploaded images under random names. The admin UI resizes and re-encodes photos in the
/// browser (which also strips EXIF/GPS metadata) and sends a full-size version plus a thumbnail;
/// the server never trusts the file name or content type and checks the real file signature.
/// </summary>
public sealed partial class ImageStore(StoragePaths paths, ILogger<ImageStore> logger)
{
    public const long MaxBytes = 15 * 1024 * 1024;
    public const int MaxFilesPerRequest = 30;

    public sealed class ImageRejectedException(string message) : Exception(message);

    /// <summary>Saves pairs of (full, thumb). When a thumbnail is missing the full image doubles as one.</summary>
    public async Task<List<NewImage>> SaveAsync(IReadOnlyList<IFormFile> files, IReadOnlyList<IFormFile> thumbs, CancellationToken ct)
    {
        var real = files.Where(f => f.Length > 0).ToList();
        if (real.Count > MaxFilesPerRequest)
            throw new ImageRejectedException($"Please upload at most {MaxFilesPerRequest} images at a time.");

        var saved = new List<NewImage>();
        var written = new List<string>();
        try
        {
            for (var i = 0; i < real.Count; i++)
            {
                var full = await ReadAsync(real[i], ct);
                var name = await WriteAsync(full, ct);
                written.Add(name.File);

                var thumbName = name.File;
                if (i < thumbs.Count && thumbs[i].Length > 0)
                {
                    var thumb = await ReadAsync(thumbs[i], ct);
                    thumbName = (await WriteAsync(thumb, ct)).File;
                    written.Add(thumbName);
                }
                saved.Add(new NewImage(name.File, thumbName, name.Width, name.Height));
            }
            return saved;
        }
        catch
        {
            Delete(written);
            throw;
        }
    }

    public void Delete(IEnumerable<string> fileNames)
    {
        foreach (var name in fileNames.Distinct())
        {
            if (!SafeName().IsMatch(name)) continue;
            try
            {
                File.Delete(Path.Combine(paths.MediaRoot, name));
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete media file {File}", name);
            }
        }
    }

    private static async Task<(byte[] Bytes, string Extension, int Width, int Height)> ReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length > MaxBytes)
            throw new ImageRejectedException($"“{file.FileName}” is larger than {MaxBytes / 1024 / 1024} MB.");

        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var (ext, w, h) = Inspect(bytes);
        if (ext is null)
            throw new ImageRejectedException($"“{file.FileName}” is not a supported image. Use JPG, PNG, WebP or AVIF.");
        return (bytes, ext, w, h);
    }

    private async Task<(string File, int Width, int Height)> WriteAsync((byte[] Bytes, string Extension, int Width, int Height) image, CancellationToken ct)
    {
        var name = Guid.NewGuid().ToString("N") + image.Extension;
        await File.WriteAllBytesAsync(Path.Combine(paths.MediaRoot, name), image.Bytes, ct);
        return (name, image.Width, image.Height);
    }

    /// <summary>Identifies the format from its signature and reads pixel dimensions where possible.</summary>
    internal static (string? Extension, int Width, int Height) Inspect(ReadOnlySpan<byte> b)
    {
        if (b.Length < 16) return (null, 0, 0);

        // PNG
        if (b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return b.Length >= 24
                ? (".png", (int)BinaryPrimitives.ReadUInt32BigEndian(b[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(b[20..]))
                : (".png", 0, 0);
        }

        // JPEG
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
        {
            var i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                var marker = b[i + 1];
                if (marker == 0xFF) { i++; continue; }
                if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
                var len = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..]);
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    return (".jpg", BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]));
                }
                i += 2 + len;
            }
            return (".jpg", 0, 0);
        }

        // WebP: RIFF....WEBP
        if (b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8))
        {
            if (b.Length >= 30)
            {
                var chunk = b[12..16];
                if (chunk.SequenceEqual("VP8 "u8))
                    return (".webp", BinaryPrimitives.ReadUInt16LittleEndian(b[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(b[28..]) & 0x3FFF);
                if (chunk.SequenceEqual("VP8L"u8))
                {
                    var bits = BinaryPrimitives.ReadUInt32LittleEndian(b[21..]);
                    return (".webp", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
                }
                if (chunk.SequenceEqual("VP8X"u8))
                    return (".webp", 1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16));
            }
            return (".webp", 0, 0);
        }

        // AVIF: ....ftypavif / avis
        if (b[4..8].SequenceEqual("ftyp"u8) && (b[8..12].SequenceEqual("avif"u8) || b[8..12].SequenceEqual("avis"u8)))
            return (".avif", 0, 0);

        return (null, 0, 0);
    }

    [GeneratedRegex(@"^[a-f0-9]{32}\.(jpg|png|webp|avif)$")]
    private static partial Regex SafeName();
}
