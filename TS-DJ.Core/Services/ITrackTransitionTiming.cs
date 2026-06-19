namespace TS_DJ.Core.Services;

/// <summary>
/// Hook for services to report track-transition timing to the UI layer.
/// </summary>
public interface ITrackTransitionTiming
{
    void MarkTsDescriptionComplete(TimeSpan elapsed);

    void MarkTsDescriptionSkipped();
}
