using Egorik.Core.Speech;

namespace Egorik.App.ViewModels;

public sealed class VoiceWakeIndicator
{
    public bool IsActive { get; private set; }

    public bool ObserveActivity(string recognizedText, string assistantName)
    {
        if (WakePhraseParser.Parse(recognizedText, assistantName).Kind !=
            WakePhraseKind.NotAddressed)
        {
            IsActive = true;
        }

        return IsActive;
    }

    public bool ObserveState(VoiceInputState state)
    {
        IsActive = state == VoiceInputState.ListeningForCommand;
        return IsActive;
    }

    public bool AcceptCommand()
    {
        IsActive = false;
        return IsActive;
    }
}
