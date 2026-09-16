using Egorik.Infrastructure.Persistence;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class AssistantSettingsMigrationTests
{
    [TestMethod]
    public async Task LoadAsync_OldSettingsAddEmptyDocumentAllowlistWithoutLosingAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "EgorikTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new ApplicationDataPaths(root);
            await File.WriteAllTextAsync(
                paths.SettingsFile,
                """
                {
                  "assistantName": "Тест",
                  "applications": { "Моё приложение": "demo.exe" },
                  "sites": { "Мой сайт": "https://example.com" }
                }
                """);
            var service = new JsonAssistantSettingsService(paths);

            await service.LoadAsync();

            Assert.AreEqual("Тест", service.Current.AssistantName);
            Assert.AreEqual("demo.exe", service.Current.Applications["моё приложение"]);
            Assert.AreEqual("https://example.com", service.Current.Sites["мой сайт"]);
            Assert.IsEmpty(service.Current.Documents);
            Assert.IsEmpty(service.Current.EnabledPlugins);
            Assert.IsEmpty(service.Current.TrustedPluginFingerprints);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
