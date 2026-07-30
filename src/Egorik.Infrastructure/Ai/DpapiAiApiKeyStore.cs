using System.Security.Cryptography;
using System.Text;
using Egorik.Core.Ai;
using Egorik.Infrastructure.Persistence;

namespace Egorik.Infrastructure.Ai;

public sealed class DpapiAiApiKeyStore(ApplicationDataPaths paths) : IAiApiKeyStore
{
    private const string EnvironmentVariableName = "EGORIK_AI_API_KEY";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Egorik.AiApiKey.v1");

    public bool HasApiKey =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariableName)) ||
        File.Exists(paths.AiApiKeyFile);

    public string? GetApiKey()
    {
        var environmentValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        if (!File.Exists(paths.AiApiKeyFile))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Хранилище API-ключа пока реализовано только для Windows.");
        }

        try
        {
            var encrypted = File.ReadAllBytes(paths.AiApiKeyFile);
            var decrypted = ProtectedData.Unprotect(
                encrypted,
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception exception) when (
            exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new AiPlannerException(
                "не удалось прочитать сохранённый API-ключ.",
                exception);
        }
    }

    public void SaveApiKey(string apiKey)
    {
        apiKey = apiKey.Trim();
        if (apiKey.Length is < 4 or > 512 || apiKey.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "API-ключ должен содержать от 4 до 512 символов без пробелов.",
                nameof(apiKey));
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Сохранение API-ключа пока реализовано только для Windows.");
        }

        Directory.CreateDirectory(paths.RootDirectory);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey),
            Entropy,
            DataProtectionScope.CurrentUser);
        var temporaryFile = paths.AiApiKeyFile + ".tmp";

        File.WriteAllBytes(temporaryFile, encrypted);
        File.Move(temporaryFile, paths.AiApiKeyFile, true);
    }

    public void DeleteApiKey()
    {
        if (File.Exists(paths.AiApiKeyFile))
        {
            File.Delete(paths.AiApiKeyFile);
        }
    }
}
