using Egorik.Core.Actions;
using Egorik.Core.Applications;
using Egorik.Core.Commands;

namespace Egorik.Infrastructure.Actions;

public sealed class ApplicationIntegrationActionProvider(IApplicationActionRegistry registry) : IAssistantActionProvider
{
    public ActionProviderManifest Manifest { get; } = new("applications", "Действия приложений");

    public IReadOnlyList<AssistantActionDescriptor> Actions => registry.AvailableApplications
        .SelectMany(application => application.Actions.Where(action => action.Risk == CommandRisk.Low)
            .Select(action => new AssistantActionDescriptor(
                $"applications.{application.Id}.{action.Id}", $"{application.DisplayName}: {action.Title}", action.Description,
                action.Parameters.Select(parameter => new ActionParameterDescriptor(parameter.Name, parameter.Description,
                    parameter.AllowedValues is null ? ActionParameterKind.Text : ActionParameterKind.Choice,
                    parameter.IsRequired, parameter.MaxLength, parameter.AllowedValues)).ToArray()))).ToArray();

    public ValueTask<CommandResult> ExecuteAsync(ActionInvocation invocation, CancellationToken cancellationToken = default)
    {
        var parts = invocation.ActionId.Split('.');
        return parts.Length == 3 && parts[0] == Manifest.Id
            ? registry.ExecuteAsync(new(parts[1], parts[2], invocation.Arguments), CommandRisk.Low, cancellationToken)
            : ValueTask.FromResult(CommandResult.Failure("Действие приложения не найдено."));
    }
}
