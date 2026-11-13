namespace SurvivalHud.Core;

/// <summary>
/// Shared hover state machine for the compact icons and the expanded HUD.
/// The caller reports whether the pointer is inside the combined hot area (all three icons,
/// the HUD and the gap between them) together with a monotonic timestamp; the controller
/// applies the open delay and the delayed close, so crossing between icons or into the HUD
/// never closes it and brief exits never flicker.
/// </summary>
public sealed class HoverController
{
    private enum State { Closed, Opening, Open, Closing }

    private State _state = State.Closed;
    private long _deadline;

    public int OpenDelayMs { get; set; } = 200;
    public int CloseDelayMs { get; set; } = 400;

    public bool Pinned { get; private set; }

    /// <summary>Whether the HUD should currently be shown.</summary>
    public bool Visible => Pinned || _state is State.Open or State.Closing;

    /// <summary>True while a timer needs to keep feeding <see cref="Update"/> (a delay is pending or the HUD is hover-open).</summary>
    public bool NeedsPolling => _state != State.Closed;

    public bool Update(bool inside, long nowMs)
    {
        switch (_state)
        {
            case State.Closed:
                if (inside)
                {
                    _deadline = nowMs + OpenDelayMs;
                    _state = OpenDelayMs <= 0 ? State.Open : State.Opening;
                }
                break;
            case State.Opening:
                if (!inside) _state = State.Closed;
                else if (nowMs >= _deadline) _state = State.Open;
                break;
            case State.Open:
                if (!inside)
                {
                    _deadline = nowMs + CloseDelayMs;
                    _state = CloseDelayMs <= 0 ? State.Closed : State.Closing;
                }
                break;
            case State.Closing:
                if (inside) _state = State.Open;
                else if (nowMs >= _deadline) _state = State.Closed;
                break;
        }
        return Visible;
    }

    /// <summary>
    /// Pins or unpins the HUD. Unpinning while the pointer is still over the icons leaves the
    /// HUD hover-open, so it closes with the normal delay once the pointer moves away.
    /// </summary>
    public void SetPinned(bool pinned, bool pointerInside)
    {
        Pinned = pinned;
        if (!pinned) _state = pointerInside ? State.Open : State.Closed;
    }

    public void Reset() => _state = State.Closed;
}
