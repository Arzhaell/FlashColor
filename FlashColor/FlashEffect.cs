using System;
using System.Threading;

namespace FlashColor;

/// <summary>
/// Flash blanc façon cinématique : la lumière de la scène monte d'un coup, tient un instant, puis redescend.
/// Il passe par notre shader avant l'adoucissement des lumières fortes, comme une vraie lumière très forte de la scène.
/// </summary>
internal sealed class FlashEffect
{
    private const double RiseMs = 60;
    private const double HoldMs = 100;
    private const double DecayMs = 250; // constante de temps de la descente
    private const double EndLevel = 0.01;

    private Pulse? pulse;

    /// <summary>Lancer un flash ; <paramref name="gameImage"/> : le montrer comme le jeu le rendrait.</summary>
    public void Start(long nowMs, float strengthEv, bool gameImage) =>
        Volatile.Write(ref pulse, new Pulse(nowMs, strengthEv, gameImage));

    /// <summary>Le flash en cours à cet instant, ou null.</summary>
    public Pulse? Current(long nowMs)
    {
        var current = Volatile.Read(ref pulse);
        return current != null && Level(nowMs - current.StartMs) > 0 ? current : null;
    }

    /// <summary>Multiplicateur de lumière à cet instant : 1 hors flash, 2^force au plus fort.</summary>
    public float Gain(long nowMs)
    {
        var current = Volatile.Read(ref pulse);
        return current == null ? 1f : MathF.Pow(2f, current.StrengthEv * (float)Level(nowMs - current.StartMs));
    }

    /// <summary>Niveau du flash (0 à 1) après <paramref name="elapsedMs"/> : montée, palier, puis descente jusqu'à 1 %.</summary>
    internal static double Level(double elapsedMs)
    {
        if (elapsedMs < 0)
            return 0;
        if (elapsedMs < RiseMs)
            return elapsedMs / RiseMs;
        if (elapsedMs < RiseMs + HoldMs)
            return 1;

        var level = Math.Exp(-(elapsedMs - RiseMs - HoldMs) / DecayMs);
        return level < EndLevel ? 0 : level;
    }

    internal sealed record Pulse(long StartMs, float StrengthEv, bool GameImage);
}
