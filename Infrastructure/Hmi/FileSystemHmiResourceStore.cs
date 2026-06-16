using GeneralHostFrontend.Core.Hmi;

namespace GeneralHostFrontend.Infrastructure.Hmi;

public sealed class FileSystemHmiResourceStore : IHmiResourceStore
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    private readonly string _rootPath;

    public FileSystemHmiResourceStore(string rootPath)
    {
        _rootPath = rootPath;
    }

    public Task<IReadOnlyList<HmiResourceDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var imagesPath = Path.Combine(_rootPath, "images");
        Directory.CreateDirectory(imagesPath);

        var resources = Directory
            .EnumerateFiles(imagesPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                return new HmiResourceDescriptor(
                    $"images/{fileName}",
                    Path.GetFileNameWithoutExtension(path),
                    "Image",
                    $"images/{fileName}");
            })
            .ToArray();

        return Task.FromResult<IReadOnlyList<HmiResourceDescriptor>>(resources);
    }

    public string? ResolvePath(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return null;
        }

        var normalized = resourceId.Replace('\\', '/').Trim().TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        var root = Path.GetFullPath(_rootPath);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return null;
        }

        return fullPath;
    }
}
