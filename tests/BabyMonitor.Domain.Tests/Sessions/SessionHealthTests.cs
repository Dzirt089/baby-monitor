using BabyMonitor.Domain.Sessions;

namespace BabyMonitor.Domain.Tests.Sessions;

/// <summary>Инварианты SessionHealth: «transport connected» не равен «пользователь слышит звук».</summary>
public sealed class SessionHealthTests
{
    [Fact]
    public void IsUserHearingAudio_только_при_connected_control_и_flowing_media()
    {
        Assert.False(new SessionHealth(SessionChannelState.Connected, AudioFlowState.Inactive, true, false).IsUserHearingAudio);
        Assert.False(new SessionHealth(SessionChannelState.Degraded, AudioFlowState.Flowing, true, false).IsUserHearingAudio);
        Assert.True(new SessionHealth(SessionChannelState.Connected, AudioFlowState.Flowing, true, false).IsUserHearingAudio);
    }

    [Fact]
    public void Initial_снимок_не_утверждает_звук()
    {
        Assert.Equal(SessionChannelState.Unknown, SessionHealth.Initial.Control);
        Assert.Equal(AudioFlowState.Inactive, SessionHealth.Initial.Media);
        Assert.False(SessionHealth.Initial.IsUserHearingAudio);
    }
}
