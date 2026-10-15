namespace Egorik.Core.Platform;

public interface ISystemStartupService
{
    bool IsSupported { get; }

    ValueTask SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
