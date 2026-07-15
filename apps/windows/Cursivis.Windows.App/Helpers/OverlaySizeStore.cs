using System.Text.Json;
using Cursivis.Infrastructure.Storage.Persistence;
using Cursivis.Windows.Platform.Overlays;

namespace Cursivis.Windows.App.Helpers;

internal sealed class OverlaySizeStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _filePath;
    private readonly AtomicFileWriter _writer = new(backupRetentionCount: 1);

    public OverlaySizeStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public OverlaySize? TryLoad()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            SavedSize? value = JsonSerializer.Deserialize<SavedSize>(
                File.ReadAllBytes(_filePath),
                SerializerOptions);
            return value is { Width: >= 780 and <= 2400, Height: >= 300 and <= 1800 }
                ? new OverlaySize(value.Width, value.Height)
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public Task SaveAsync(OverlaySize size, CancellationToken cancellationToken = default)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            new SavedSize(size.Width, size.Height),
            SerializerOptions);
        return _writer.WriteAsync(_filePath, bytes, cancellationToken);
    }

    private sealed record SavedSize(int Width, int Height);
}
