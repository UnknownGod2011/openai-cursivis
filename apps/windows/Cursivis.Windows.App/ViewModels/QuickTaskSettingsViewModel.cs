using Cursivis.Windows.App.Helpers;

namespace Cursivis.Windows.App.ViewModels;

internal sealed class QuickTaskSettingsViewModel : ObservableViewModel
{
    private string _displayName;
    private string _roughDescription;
    private string _finalInstruction;
    private bool _supportsText;
    private bool _supportsImage;
    private bool _mayProposeAction;
    private bool _isApproved;
    private bool _isDirty;
    private bool _isDefinitionValid;
    private string _validationSummary;

    public QuickTaskSettingsViewModel()
    {
        _displayName = ResourceText.Get("PromptOptimizerDefaultName");
        _roughDescription = ResourceText.Get("PromptOptimizerDefaultDescription");
        _finalInstruction = ResourceText.Get("PromptOptimizerDefaultInstruction");
        _supportsText = true;
        _validationSummary = string.Empty;
        Validate();
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
            {
                MarkEdited();
            }
        }
    }

    public string RoughDescription
    {
        get => _roughDescription;
        set
        {
            if (SetProperty(ref _roughDescription, value))
            {
                MarkEdited();
            }
        }
    }

    public string FinalInstruction
    {
        get => _finalInstruction;
        set
        {
            if (SetProperty(ref _finalInstruction, value))
            {
                MarkEdited();
            }
        }
    }

    public bool SupportsText
    {
        get => _supportsText;
        set
        {
            if (SetProperty(ref _supportsText, value))
            {
                MarkEdited();
            }
        }
    }

    public bool SupportsImage
    {
        get => _supportsImage;
        set
        {
            if (SetProperty(ref _supportsImage, value))
            {
                MarkEdited();
            }
        }
    }

    public bool MayProposeAction
    {
        get => _mayProposeAction;
        set
        {
            if (SetProperty(ref _mayProposeAction, value))
            {
                MarkEdited();
            }
        }
    }

    public bool IsApproved
    {
        get => _isApproved;
        set
        {
            if (SetProperty(ref _isApproved, value))
            {
                MarkEdited();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public bool IsDefinitionValid
    {
        get => _isDefinitionValid;
        private set => SetProperty(ref _isDefinitionValid, value);
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    public void RestorePromptOptimizer()
    {
        _displayName = ResourceText.Get("PromptOptimizerDefaultName");
        _roughDescription = ResourceText.Get("PromptOptimizerDefaultDescription");
        _finalInstruction = ResourceText.Get("PromptOptimizerDefaultInstruction");
        _supportsText = true;
        _supportsImage = false;
        _mayProposeAction = false;
        _isApproved = false;

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(RoughDescription));
        OnPropertyChanged(nameof(FinalInstruction));
        OnPropertyChanged(nameof(SupportsText));
        OnPropertyChanged(nameof(SupportsImage));
        OnPropertyChanged(nameof(MayProposeAction));
        OnPropertyChanged(nameof(IsApproved));
        IsDirty = true;
        Validate();
    }

    public void DiscardChanges()
    {
        RestorePromptOptimizer();
        IsDirty = false;
    }

    private void MarkEdited()
    {
        IsDirty = true;
        Validate();
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            SetValidation(false, "QuickTaskValidationNameRequired");
            return;
        }

        if (DisplayName.Trim().Length > 64)
        {
            SetValidation(false, "QuickTaskValidationNameLength");
            return;
        }

        if (string.IsNullOrWhiteSpace(FinalInstruction)
            || FinalInstruction.Trim().Length < 40)
        {
            SetValidation(false, "QuickTaskValidationInstructionRequired");
            return;
        }

        if (FinalInstruction.Length > 8000)
        {
            SetValidation(false, "QuickTaskValidationInstructionLength");
            return;
        }

        if (!SupportsText && !SupportsImage)
        {
            SetValidation(false, "QuickTaskValidationContextRequired");
            return;
        }

        SetValidation(true, "QuickTaskValidationReady");
    }

    private void SetValidation(bool isValid, string messageKey)
    {
        IsDefinitionValid = isValid;
        ValidationSummary = ResourceText.Get(messageKey);
        OnPropertyChanged(nameof(IsApproved));
    }
}
