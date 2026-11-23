using OptiPulse.Core.Models;

namespace OptiPulse.Core.Tweaks;

public interface ITweak
{
    TweakDescriptor Descriptor { get; }
    TweakStatusInfo GetStatus();
    TweakOperationResult Apply();
    TweakOperationResult Revert();
}
