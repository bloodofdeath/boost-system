using System.Windows.Input;
using OptiPulse.Core.Models;
using OptiPulse.Core.Tweaks;

namespace OptiPulse.App.ViewModels;

public sealed class TweakCardViewModel : ViewModelBase
{
    private bool _isEnabled;
    private bool _isBusy;
    private string _statusNote = string.Empty;
    private bool _isSupported = true;
    private bool _isUpdatingSilently;

    public ITweak Tweak { get; }
    public TweakDescriptor Descriptor => Tweak.Descriptor;

    public string Id => Descriptor.Id;
    public string Title => Descriptor.Title;
    public string Description => Descriptor.Description;
    public string NotRecommendedWhen => Descriptor.NotRecommendedWhen;
    public string TechnicalDetails => Descriptor.TechnicalDetails;
    public string IconKey => Descriptor.IconKey;
    public TweakCategory Category => Descriptor.Category;
    public TweakNoticeLevel NoticeLevel => Descriptor.NoticeLevel;
    public string NoticeTooltip => Descriptor.NoticeTooltip;
    public bool IsBeta => Descriptor.IsBeta;
    public bool RequiresAdmin => Descriptor.RequiresAdmin;
    public bool RequiresRebootForFullEffect => Descriptor.RequiresRebootForFullEffect;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isUpdatingSilently)
            {
                SetProperty(ref _isEnabled, value);
                return;
            }

            if (_isEnabled == value) return;
            SetProperty(ref _isEnabled, value);
            ToggleRequested?.Invoke(this, value);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusNote
    {
        get => _statusNote;
        set => SetProperty(ref _statusNote, value);
    }

    public bool IsSupported
    {
        get => _isSupported;
        set => SetProperty(ref _isSupported, value);
    }

    public ICommand ShowNotRecommendedCommand { get; }
    public event Action<TweakCardViewModel, bool>? ToggleRequested;

    public TweakCardViewModel(ITweak tweak, Action<TweakCardViewModel> showNotRecommended)
    {
        Tweak = tweak;
        ShowNotRecommendedCommand = new RelayCommand(() => showNotRecommended(this));
    }

    public void SetStateSilently(bool isEnabled, string statusNote, bool isSupported = true)
    {
        _isUpdatingSilently = true;
        try
        {
            IsEnabled = isEnabled;
            StatusNote = statusNote;
            IsSupported = isSupported;
        }
        finally
        {
            _isUpdatingSilently = false;
        }
    }
}
