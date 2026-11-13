namespace BrowserWall.Core.Health;

/// <summary>Result of <see cref="FailureEpisode.Report"/>: whether to write a log line.</summary>
public enum EpisodeChange
{
    /// <summary>Nothing to write.</summary>
    None,

    /// <summary>Went from OK to failing (the first failure of a streak).</summary>
    FirstFailure,

    /// <summary>The first success after a streak of failures.</summary>
    Recovered,
}

/// <summary>
/// A failure streak is reported only twice: at the first failure and at the first success afterwards. Starts in the OK state.
/// Does not touch the clock or files; single-threaded use (the page list is written on the UI thread).
/// </summary>
public sealed class FailureEpisode
{
    private bool _failing;

    /// <summary>Whether a failure streak is in progress.</summary>
    public bool IsFailing => _failing;

    public EpisodeChange Report(bool succeeded)
    {
        if (succeeded)
        {
            if (!_failing)
            {
                return EpisodeChange.None;
            }

            _failing = false;
            return EpisodeChange.Recovered;
        }

        if (_failing)
        {
            return EpisodeChange.None;
        }

        _failing = true;
        return EpisodeChange.FirstFailure;
    }
}
