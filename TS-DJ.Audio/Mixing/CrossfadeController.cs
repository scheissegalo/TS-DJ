using TS_DJ.Core.Models;

namespace TS_DJ.Audio.Mixing;

/// <summary>
/// Overlap crossfade between two decks by ramping output gain.
/// </summary>
public sealed class CrossfadeController
{
    private readonly object _sync;
    private DeckChannel? _fromDeck;
    private DeckChannel? _toDeck;
    private DateTime _startedUtc;
    private TimeSpan _duration = TimeSpan.FromSeconds(4);
    private bool _active;
    private Action? _onComplete;

    public CrossfadeController(object sync) => _sync = sync;

    public bool IsActive
    {
        get
        {
            lock (_sync)
                return _active;
        }
    }

    public bool Enabled { get; set; }

    public TimeSpan Duration
    {
        get
        {
            lock (_sync)
                return _duration;
        }
        set
        {
            lock (_sync)
                _duration = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(4) : value;
        }
    }

    public void StartCrossfade(DeckChannel from, DeckChannel to, Action? onComplete = null)
    {
        lock (_sync)
        {
            _fromDeck = from;
            _toDeck = to;
            _startedUtc = DateTime.UtcNow;
            _active = true;
            _onComplete = onComplete;

            from.SetCrossfadeGain(1f);
            to.SetCrossfadeGain(0f);
            to.Play();
        }
    }

    public void HardCut(DeckChannel from, DeckChannel to)
    {
        lock (_sync)
        {
            from.SetCrossfadeGain(0f);
            from.Cue();
            to.SetCrossfadeGain(1f);
            to.Play();
            CancelInternal();
        }
    }

    public void Cancel(DeckChannel? deckA = null, DeckChannel? deckB = null)
    {
        lock (_sync)
        {
            ResetDeckGainsLocked(_fromDeck, _toDeck);
            CancelInternal();
            if (deckA is not null || deckB is not null)
                ResetAllDeckGainsLocked(deckA, deckB);
        }
    }

    public void Tick()
    {
        Action? complete = null;

        lock (_sync)
        {
            if (!_active || _fromDeck is null || _toDeck is null)
                return;

            var elapsed = DateTime.UtcNow - _startedUtc;
            var progress = _duration <= TimeSpan.Zero
                ? 1.0
                : Math.Clamp(elapsed.TotalMilliseconds / _duration.TotalMilliseconds, 0.0, 1.0);

            _fromDeck.SetCrossfadeGain((float)(1.0 - progress));
            _toDeck.SetCrossfadeGain((float)progress);

            if (progress < 1.0)
                return;

            _fromDeck.Cue();
            _fromDeck.SetCrossfadeGain(1f);
            _toDeck.SetCrossfadeGain(1f);
            complete = _onComplete;
            CancelInternal();
        }

        complete?.Invoke();
    }

    public static void ResetAllDeckGains(DeckChannel deckA, DeckChannel deckB)
    {
        deckA.ResetCrossfadeGain();
        deckB.ResetCrossfadeGain();
    }

    private static void ResetDeckGainsLocked(DeckChannel? from, DeckChannel? to)
    {
        from?.ResetCrossfadeGain();
        to?.ResetCrossfadeGain();
    }

    private static void ResetAllDeckGainsLocked(DeckChannel? deckA, DeckChannel? deckB)
    {
        deckA?.ResetCrossfadeGain();
        deckB?.ResetCrossfadeGain();
    }

    private void CancelInternal()
    {
        _active = false;
        _fromDeck = null;
        _toDeck = null;
        _onComplete = null;
    }
}
