namespace Tischkicker.Core;

/// <summary>Zustand der Spieluhr (Countdown pro Halbzeit).</summary>
public readonly record struct ClockState(double ElapsedSec, bool Running, long? StartedAtMs);

/// <summary>Reine Spieluhr-Logik (pausierbarer Countdown). <c>nowMs</c> = Epoch-ms.</summary>
public static class MatchClock
{
    public static double ElapsedSeconds(ClockState s, long nowMs) =>
        s.Running && s.StartedAtMs is { } started
            ? s.ElapsedSec + Math.Max(0, (nowMs - started) / 1000.0)
            : s.ElapsedSec;

    public static double RemainingSeconds(ClockState s, int halfDurationSec, long nowMs) =>
        Math.Max(0, halfDurationSec - ElapsedSeconds(s, nowMs));

    public static bool IsExpired(ClockState s, int halfDurationSec, long nowMs) =>
        RemainingSeconds(s, halfDurationSec, nowMs) <= 0;

    public static ClockState Start(ClockState s, long nowMs) =>
        s.Running ? s : s with { Running = true, StartedAtMs = nowMs };

    public static ClockState Pause(ClockState s, long nowMs) =>
        !s.Running ? s : new ClockState(ElapsedSeconds(s, nowMs), false, null);

    public static ClockState Reset() => new(0, false, null);
}
