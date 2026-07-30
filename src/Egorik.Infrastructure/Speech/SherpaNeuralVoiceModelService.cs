using System.Formats.Tar;
using Egorik.Core.Speech;
using Egorik.Infrastructure.Persistence;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Egorik.Infrastructure.Speech;

public sealed class SherpaNeuralVoiceModelService(
    ApplicationDataPaths paths,
    HttpClient httpClient) : INeuralVoiceModelService
{
    private const string ModelName = "vits-piper-ru_RU-denis-medium";
    private const string ModelFileName = "ru_RU-denis-medium.onnx";
    private static readonly Uri ModelUri = new(
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/" +
        "vits-piper-ru_RU-denis-medium.tar.bz2");

    public string ModelDirectory =>
        Path.Combine(paths.RootDirectory, "voices", ModelName);

    public bool IsInstalled =>
        File.Exists(Path.Combine(ModelDirectory, ModelFileName)) &&
        File.Exists(Path.Combine(ModelDirectory, "tokens.txt")) &&
        Directory.Exists(Path.Combine(ModelDirectory, "espeak-ng-data"));

    public async ValueTask DownloadAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            progress?.Report(1);
            return;
        }

        var voicesRoot = Path.Combine(paths.RootDirectory, "voices");
        Directory.CreateDirectory(voicesRoot);

        var operationId = Guid.NewGuid().ToString("N");
        var archivePath = Path.Combine(voicesRoot, $".download-{operationId}.tar.bz2");
        var extractionRoot = Path.Combine(voicesRoot, $".extract-{operationId}");

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
                        progress?.Report(Math.Min(0.88, totalRead / (double)contentLength * 0.88));
                    }
                }
            }

            Directory.CreateDirectory(extractionRoot);
            await ExtractSafelyAsync(archivePath, extractionRoot, cancellationToken);
            progress?.Report(0.97);

            var extractedModel = Path.Combine(extractionRoot, ModelName);
            if (!File.Exists(Path.Combine(extractedModel, ModelFileName)) ||
                !File.Exists(Path.Combine(extractedModel, "tokens.txt")) ||
                !Directory.Exists(Path.Combine(extractedModel, "espeak-ng-data")))
            {
                throw new InvalidDataException(
                    "Архив не содержит ожидаемые файлы нейроголоса.");
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

    private static async Task ExtractSafelyAsync(
        string archivePath,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = Path.GetFullPath(destinationRoot) +
                            Path.DirectorySeparatorChar;

        await using var archiveStream = File.OpenRead(archivePath);
        await using var decompressedStream = await BZip2Stream.CreateAsync(
            archiveStream,
            CompressionMode.Decompress,
            decompressConcatenated: true,
            leaveOpen: false,
            tolerateTruncatedStream: false,
            cancellationToken);
        using var archive = new TarReader(decompressedStream);

        TarEntry? entry;
        while ((entry = await archive.GetNextEntryAsync(
                   copyData: false,
                   cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entryPath = entry.Name.Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entryPath));
            if (!destination.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Обнаружен небезопасный путь внутри архива нейроголоса.");
            }

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                throw new InvalidDataException(
                    "Архив нейроголоса содержит неподдерживаемую ссылку.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var output = File.Create(destination);
            if (entry.DataStream is not null)
            {
                await entry.DataStream.CopyToAsync(output, cancellationToken);
            }
        }
    }
}
