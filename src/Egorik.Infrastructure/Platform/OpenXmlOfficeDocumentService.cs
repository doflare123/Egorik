using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Egorik.Core.Commands;
using Egorik.Core.Platform;
using Egorik.Infrastructure.Persistence;

namespace Egorik.Infrastructure.Platform;

public sealed class OpenXmlOfficeDocumentService(
    ApplicationDataPaths paths,
    IApplicationLauncher launcher) : IOfficeDocumentService
{
    private const long MaximumWorkbookLength = 100 * 1024 * 1024;
    private const long MaximumXmlEntryLength = 24 * 1024 * 1024;
    private const int MaximumMatches = 20;

    public async ValueTask<LaunchResult> CreateWordNoteAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4_000)
        {
            return new LaunchResult(false, "Текст заметки пуст или длиннее 4000 символов.");
        }

        string documentPath;
        try
        {
            documentPath = await Task.Run(
                () => CreateWordDocument(text.Trim(), cancellationToken),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new LaunchResult(false, $"Не удалось создать документ Word: {exception.Message}");
        }

        var launch = await launcher.LaunchAsync(documentPath, cancellationToken);
        return launch.IsSuccess
            ? new LaunchResult(true, "Заметка создана и открыта в приложении для документов Word.")
            : new LaunchResult(
                false,
                $"Заметка создана, но открыть её не удалось: {launch.Message}");
    }

    public async ValueTask<SpreadsheetSearchResult> SearchWorkbookAsync(
        string path,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(query))
        {
            return Failure("Не указан файл Excel или текст для поиска.");
        }

        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return Failure("Поддерживается безопасный поиск только в файлах .xlsx.");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return Failure("В настройках книги Excel нужен полный локальный путь.");
        }

        try
        {
            return await Task.Run(
                () => SearchWorkbook(path, query.Trim(), cancellationToken),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or XmlException or ArgumentException or
                NotSupportedException or OverflowException or System.Security.SecurityException)
        {
            return Failure($"Не удалось прочитать книгу Excel: {exception.Message}");
        }
    }

    private string CreateWordDocument(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(paths.GeneratedDocumentsDirectory);
        var fileName = $"Заметка-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.docx";
        var path = Path.Combine(paths.GeneratedDocumentsDirectory, fileName);
        try
        {
            using (var file = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteXmlEntry(archive, "[Content_Types].xml", writer =>
                {
                    writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                    writer.WriteStartElement("Default");
                    writer.WriteAttributeString("Extension", "rels");
                    writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
                    writer.WriteEndElement();
                    writer.WriteStartElement("Default");
                    writer.WriteAttributeString("Extension", "xml");
                    writer.WriteAttributeString("ContentType", "application/xml");
                    writer.WriteEndElement();
                    writer.WriteStartElement("Override");
                    writer.WriteAttributeString("PartName", "/word/document.xml");
                    writer.WriteAttributeString(
                        "ContentType",
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                });

                WriteXmlEntry(archive, "_rels/.rels", writer =>
                {
                    writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                    writer.WriteStartElement("Relationship");
                    writer.WriteAttributeString("Id", "rId1");
                    writer.WriteAttributeString(
                        "Type",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
                    writer.WriteAttributeString("Target", "word/document.xml");
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                });

                WriteXmlEntry(archive, "word/document.xml", writer =>
                {
                    const string wordNamespace =
                        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    writer.WriteStartElement("w", "document", wordNamespace);
                    writer.WriteStartElement("w", "body", wordNamespace);
                    foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal)
                                 .Replace('\r', '\n')
                                 .Split('\n'))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.WriteStartElement("w", "p", wordNamespace);
                        writer.WriteStartElement("w", "r", wordNamespace);
                        writer.WriteStartElement("w", "t", wordNamespace);
                        writer.WriteAttributeString("xml", "space", null, "preserve");
                        writer.WriteString(line);
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndElement();
                });
            }

            return path;
        }
        catch
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            throw;
        }
    }

    private static SpreadsheetSearchResult SearchWorkbook(
        string path,
        string query,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            return Failure("Настроенный файл Excel не найден.");
        }

        if (fileInfo.Length > MaximumWorkbookLength)
        {
            return Failure("Файл Excel слишком большой для безопасного локального поиска.");
        }

        using var file = new FileStream(
            fileInfo.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        var totalXmlLength = 0L;
        var xmlEntryCount = 0;
        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            xmlEntryCount++;
            totalXmlLength = checked(totalXmlLength + entry.Length);
            if (xmlEntryCount > 500 || totalXmlLength > MaximumWorkbookLength * 2)
            {
                return Failure("Структура книги Excel слишком большая для безопасного поиска.");
            }
        }

        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);
        var sharedStrings = ReadSharedStrings(entries);
        var sheetNames = ReadSheetNames(entries);
        var normalizedQuery = TextNormalizer.Normalize(query);
        var matches = new List<SpreadsheetCellMatch>();

        foreach (var entry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeXmlEntry(entry);
            var document = LoadXml(entry);
            XNamespace spreadsheet =
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sheet = sheetNames.TryGetValue(entry.FullName, out var configuredName)
                ? configuredName
                : Path.GetFileNameWithoutExtension(entry.Name);

            foreach (var cell in document.Descendants(spreadsheet + "c"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = ReadCellValue(cell, spreadsheet, sharedStrings);
                var rawMatch = value.Contains(query, StringComparison.OrdinalIgnoreCase);
                var normalizedMatch = normalizedQuery.Length > 0 &&
                                      TextNormalizer.Normalize(value).Contains(
                                          normalizedQuery,
                                          StringComparison.Ordinal);
                if (value.Length == 0 || (!rawMatch && !normalizedMatch))
                {
                    continue;
                }

                matches.Add(new SpreadsheetCellMatch(
                    sheet,
                    (string?)cell.Attribute("r") ?? "?",
                    value.Length <= 300 ? value : value[..300] + "…"));
                if (matches.Count == MaximumMatches)
                {
                    return Success(matches, wasLimited: true);
                }
            }
        }

        return Success(matches, wasLimited: false);
    }

    private static IReadOnlyList<string> ReadSharedStrings(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (!entries.TryGetValue("xl/sharedStrings.xml", out var entry))
        {
            return [];
        }

        EnsureSafeXmlEntry(entry);
        var document = LoadXml(entry);
        XNamespace spreadsheet =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document
            .Descendants(spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(spreadsheet + "t").Select(node => node.Value)))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadSheetNames(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (!entries.TryGetValue("xl/workbook.xml", out var workbookEntry) ||
            !entries.TryGetValue("xl/_rels/workbook.xml.rels", out var relationsEntry))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        EnsureSafeXmlEntry(workbookEntry);
        EnsureSafeXmlEntry(relationsEntry);
        var workbook = LoadXml(workbookEntry);
        var relations = LoadXml(relationsEntry);
        XNamespace spreadsheet =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelations =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelations =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        var targets = relations
            .Descendants(packageRelations + "Relationship")
            .Where(relation =>
                !string.Equals(
                    (string?)relation.Attribute("TargetMode"),
                    "External",
                    StringComparison.OrdinalIgnoreCase))
            .Select(relation => new
            {
                Id = (string?)relation.Attribute("Id"),
                Target = NormalizeWorkbookTarget((string?)relation.Attribute("Target"))
            })
            .Where(relation => relation.Id is not null && relation.Target is not null)
            .ToDictionary(
                relation => relation.Id!,
                relation => relation.Target!,
                StringComparer.Ordinal);

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Descendants(spreadsheet + "sheet"))
        {
            var id = (string?)sheet.Attribute(officeRelations + "id");
            var name = (string?)sheet.Attribute("name");
            if (id is not null && name is not null && targets.TryGetValue(id, out var target))
            {
                names[target] = name;
            }
        }

        return names;
    }

    private static string ReadCellValue(
        XElement cell,
        XNamespace spreadsheet,
        IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(spreadsheet + "t").Select(node => node.Value));
        }

        var value = cell.Element(spreadsheet + "v")?.Value ?? string.Empty;
        if (type == "s" &&
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        return value;
    }

    private static string? NormalizeWorkbookTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var normalized = target.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("../", StringComparison.Ordinal))
        {
            return null;
        }

        return normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : "xl/" + normalized;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlEntryLength
        });
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static void EnsureSafeXmlEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > MaximumXmlEntryLength)
        {
            throw new InvalidDataException("XML-часть книги превышает безопасный размер.");
        }
    }

    private static void WriteXmlEntry(
        ZipArchive archive,
        string name,
        Action<XmlWriter> write)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = false
        });
        writer.WriteStartDocument();
        write(writer);
        writer.WriteEndDocument();
    }

    private static SpreadsheetSearchResult Success(
        IReadOnlyList<SpreadsheetCellMatch> matches,
        bool wasLimited)
    {
        var message = matches.Count == 0
            ? "Совпадений в книге Excel не найдено."
            : wasLimited
                ? $"Найдены первые {matches.Count} совпадений."
                : $"Найдено совпадений: {matches.Count}.";
        return new SpreadsheetSearchResult(true, message, matches);
    }

    private static SpreadsheetSearchResult Failure(string message) =>
        new(false, message, []);
}
