using System.IO.Compression;
using Egorik.Core.Speech;
using Egorik.Infrastructure.Persistence;

namespace Egorik.Infrastructure.Speech;

public sealed class VoskVoiceModelService(
    ApplicationDataPaths paths,
    HttpClient httpClient) : IVoiceModelService
{
    private const string ModelName = "vosk-model-small-ru-0.22";
    private static readonly Uri ModelUri =
        new("https://alphacephei.com/vosk/models/vosk-model-small-ru-0.22.zip");

    public string ModelDirectory =>
        Path.Combine(paths.RootDirectory, "models", ModelName);

    public bool IsInstalled =>
        Directory.Exists(Path.Combine(ModelDirectory, "am")) &&
        Directory.Exists(Path.Combine(ModelDirectory, "conf")) &&
        Directory.Exists(Path.Combine(ModelDirectory, "graph"));

    public async ValueTask DownloadAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            progress?.Report(1);
            return;
        }

        var modelsRoot = Path.Combine(paths.RootDirectory, "models");
        Directory.CreateDirectory(modelsRoot);

        var operationId = Guid.NewGuid().ToString("N");
        var archivePath = Path.Combine(modelsRoot, $".download-{operationId}.zip");
        var extractionRoot = Path.Combine(modelsRoot, $".extract-{operationId}");

        try
        {
            using var response = await httpClient.GetAsync(
                ModelUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(archivePath))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;

                    if (contentLength is > 0)
                    {
                        progress?.Report(Math.Min(0.85, totalRead / (double)contentLength * 0.85));
                    }
                }
            }

            Directory.CreateDirectory(extractionRoot);
            ExtractSafely(archivePath, extractionRoot, cancellationToken);
            progress?.Report(0.95);

            var extractedModel = Path.Combine(extractionRoot, ModelName);
            if (!Directory.Exists(Path.Combine(extractedModel, "am")) ||
                !Directory.Exists(Path.Combine(extractedModel, "conf")))
            {
                throw new InvalidDataException("Архив не содержит ожидаемую модель Vosk.");
            }

            if (Directory.Exists(ModelDirectory))
            {
                Directory.Delete(ModelDirectory, true);
            }

            Directory.Move(extractedModel, ModelDirectory);
            progress?.Report(1);
        }
        finally
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            if (Directory.Exists(extractionRoot))
            {
                Directory.Delete(extractionRoot, true);
            }
        }
    }

    private static void ExtractSafely(
        string archivePath,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = Path.GetFullPath(destinationRoot) +
                            Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = Path.GetFullPath(
                Path.Combine(destinationRoot, entry.FullName));
            if (!destination.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Обнаружен небезопасный путь внутри архива модели.");
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }
}
