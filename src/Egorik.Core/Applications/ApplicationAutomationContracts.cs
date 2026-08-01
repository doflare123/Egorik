using Egorik.Core.Commands;

namespace Egorik.Core.Applications;

public sealed record ApplicationActionParameterDescriptor(
    string Name,
    string Description,
    bool IsRequired = true,
    int MaxLength = 200,
    IReadOnlyList<string>? AllowedValues = null);

public sealed record ApplicationActionDescriptor(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<ApplicationActionParameterDescriptor> Parameters,
    CommandRisk Risk = CommandRisk.Low,
    bool IsAvailableToAi = false);

public sealed record ApplicationIntegrationDescriptor(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<ApplicationActionDescriptor> Actions);

public sealed record ApplicationActionRequest(
    string ApplicationId,
    string ActionId,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record ApplicationActionValidationResult(bool IsValid, string? Error)
{
    public static ApplicationActionValidationResult Success() => new(true, null);

    public static ApplicationActionValidationResult Failure(string error) => new(false, error);
}

public interface IApplicationIntegration
{
    ApplicationIntegrationDescriptor Descriptor { get; }

    ValueTask<CommandResult> ExecuteAsync(
        ApplicationActionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IApplicationActionRegistry
{
    IReadOnlyList<ApplicationIntegrationDescriptor> AvailableApplications { get; }

    ApplicationActionValidationResult Validate(
        ApplicationActionRequest request,
        CommandRisk maximumAllowedRisk);

    ValueTask<CommandResult> ExecuteAsync(
        ApplicationActionRequest request,
        CommandRisk maximumAllowedRisk,
        CancellationToken cancellationToken = default);
}
