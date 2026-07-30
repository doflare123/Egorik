using Egorik.Core.Configuration;

namespace Egorik.App.ViewModels;

public sealed class OnboardingViewModel(IAssistantSettingsService settings) : ViewModelBase
{
    private string _assistantName = settings.Current.AssistantName;
    private string? _validationMessage;

    public string AssistantName
    {
        get => _assistantName;
        set => SetProperty(ref _assistantName, value);
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public async Task<bool> CompleteAsync()
    {
        var name = AssistantName.Trim();
        if (name.Length is < 2 or > 24)
        {
            ValidationMessage = "Имя должно содержать от 2 до 24 символов.";
            return false;
        }

        settings.Current.AssistantName = name;
        settings.Current.OnboardingCompleted = true;
        await settings.SaveAsync();
        ValidationMessage = null;
        return true;
    }
}
