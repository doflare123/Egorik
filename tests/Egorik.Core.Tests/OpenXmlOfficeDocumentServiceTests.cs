using System.IO.Compression;
using Egorik.Core.Platform;
using Egorik.Infrastructure.Persistence;
using Egorik.Infrastructure.Platform;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class OpenXmlOfficeDocumentServiceTests
{
    [TestMethod]
    public async Task CreateWordNoteAsync_CreatesNewDocumentInApplicationData()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var launcher = new RecordingLauncher();
            var service = new OpenXmlOfficeDocumentService(
                new ApplicationDataPaths(root),
                launcher);

            var result = await service.CreateWordNoteAsync("Привет <Егорик> & друзья");

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsNotNull(launcher.LastTarget);
            Assert.IsTrue(File.Exists(launcher.LastTarget));
            Assert.AreEqual(".docx", Path.GetExtension(launcher.LastTarget));
            using var archive = ZipFile.OpenRead(launcher.LastTarget);
            var document = archive.GetEntry("word/document.xml");
            Assert.IsNotNull(document);
            using var reader = new StreamReader(document.Open());
            var xml = await reader.ReadToEndAsync();
            StringAssert.Contains(xml, "Привет &lt;Егорик&gt; &amp; друзья");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SearchWorkbookAsync_FindsSharedStringAndReportsSheetAndCell()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workbook = Path.Combine(root, "budget.xlsx");
            CreateWorkbook(workbook);
            var service = new OpenXmlOfficeDocumentService(
                new ApplicationDataPaths(root),
                new RecordingLauncher());

            var result = await service.SearchWorkbookAsync(workbook, "калининград");

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.HasCount(1, result.Matches);
            Assert.AreEqual("Бюджет", result.Matches[0].Sheet);
            Assert.AreEqual("B2", result.Matches[0].Cell);
            Assert.AreEqual("Калининград", result.Matches[0].Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SearchWorkbookAsync_RejectsLegacyOrArbitraryFileType()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = new OpenXmlOfficeDocumentService(
                new ApplicationDataPaths(root),
                new RecordingLauncher());

            var result = await service.SearchWorkbookAsync(
                Path.Combine(root, "budget.xls"),
                "доход");

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "только в файлах .xlsx");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CreateWorkbook(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "xl/sharedStrings.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si><t>Доход</t></si>
              <si><t>Калининград</t></si>
            </sst>
            """);
        WriteEntry(
            archive,
            "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Бюджет" sheetId="1" r:id="rId1" /></sheets>
            </workbook>
            """);
        WriteEntry(
            archive,
            "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                            Target="worksheets/sheet1.xml" />
            </Relationships>
            """);
        WriteEntry(
            archive,
            "xl/worksheets/sheet1.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                <row r="2"><c r="B2" t="s"><v>1</v></c></row>
              </sheetData>
            </worksheet>
            """);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "EgorikTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingLauncher : IApplicationLauncher
    {
        public string? LastTarget { get; private set; }

        public ValueTask<LaunchResult> LaunchAsync(
            string target,
            CancellationToken cancellationToken = default)
        {
            LastTarget = target;
            return ValueTask.FromResult(new LaunchResult(true, "Готово."));
        }
    }
}
