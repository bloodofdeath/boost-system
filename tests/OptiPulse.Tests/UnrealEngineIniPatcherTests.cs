using OptiPulse.Core.Tweaks;
using Xunit;

namespace OptiPulse.Tests;

public sealed class UnrealEngineIniPatcherTests
{
    [Fact]
    public void ApplyAndRemovePatch_PreservesOriginalIniContent()
    {
        var original = """
            [Core.System]
            Paths=../../../Engine/Content
            Paths=%GAMEDIR%Content

            [/Script/Engine.RendererSettings]
            r.DefaultFeature.Bloom=True
            """;

        Assert.False(UnrealEngineIniPatcher.IsPatched(original));

        var patched = UnrealEngineIniPatcher.ApplyPatch(original);
        Assert.True(UnrealEngineIniPatcher.IsPatched(patched));
        Assert.Contains("[Core.System]", patched);
        Assert.Contains("r.CreateShadersOnLoad=1", patched);
        Assert.Contains("r.MotionBlurQuality=0", patched);

        // Idempotency: patching twice shouldn't duplicate marker block
        var patchedTwice = UnrealEngineIniPatcher.ApplyPatch(patched);
        var markerCount = patchedTwice.Split(UnrealEngineIniPatcher.BeginMarker).Length - 1;
        Assert.Equal(1, markerCount);

        // Clean removal restores original content
        var restored = UnrealEngineIniPatcher.RemovePatch(patchedTwice).Trim();
        Assert.False(UnrealEngineIniPatcher.IsPatched(restored));
        Assert.Equal(original.Trim(), restored);
    }

    [Fact]
    public void ApplyAndRemovePatch_HandlesEmptyFile()
    {
        var patched = UnrealEngineIniPatcher.ApplyPatch(string.Empty);
        Assert.True(UnrealEngineIniPatcher.IsPatched(patched));

        var removed = UnrealEngineIniPatcher.RemovePatch(patched);
        Assert.True(string.IsNullOrWhiteSpace(removed));
    }
}
