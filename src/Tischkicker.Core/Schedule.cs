using Tischkicker.Core.Domain;

namespace Tischkicker.Core;

/// <summary>Wohin der Sieger eines K.o.-Spiels vorrückt.</summary>
public readonly record struct Feed(int Round, int Slot, string Side);

/// <summary>Eine generierte Paarung (Runde = Spieltag/Turnierrunde, Slot 0-basiert).</summary>
public readonly record struct Pairing(
    int Round,
    int Slot,
    int? TeamAId,
    int? TeamBId,
    string? GroupName = null,
    Feed? FeedsInto = null,
    Feed? LoserFeedsInto = null,
    bool IsThirdPlace = false);

/// <summary>Spielplan-Generierung als reine Logik (keine DB).</summary>
public static class Schedule
{
    /// <summary>Liga „jeder gegen jeden" (Rotationsverfahren).</summary>
    public static List<Pairing> RoundRobin(IReadOnlyList<int> teamIds)
    {
        var ids = new List<int>(teamIds);
        if (ids.Count < 2) return [];
        if (ids.Count % 2 != 0) ids.Add(-1); // Dummy = spielfrei

        var n = ids.Count;
        var rounds = n - 1;
        var half = n / 2;
        var rotating = new List<int>(ids);
        var pairings = new List<Pairing>();

        for (var r = 0; r < rounds; r++)
        {
            var slot = 0;
            for (var i = 0; i < half; i++)
            {
                var a = rotating[i];
                var b = rotating[n - 1 - i];
                if (a != -1 && b != -1)
                    pairings.Add(new Pairing(r + 1, slot++, a, b));
            }
            // Erstes Element fix, Rest rotieren (letztes nach vorne).
            var first = rotating[0];
            var rest = rotating.GetRange(1, rotating.Count - 1);
            var last = rest[^1];
            rest.RemoveAt(rest.Count - 1);
            rest.Insert(0, last);
            rotating = [first, .. rest];
        }
        return pairings;
    }

    private static int NextPowerOfTwo(int n)
    {
        var p = 1;
        while (p < n) p *= 2;
        return p;
    }

    /// <summary>Standard-Setzliste (1-basiert) in Baum-Reihenfolge.</summary>
    private static List<int> SeedPositions(int size)
    {
        var seeds = new List<int> { 1, 2 };
        while (seeds.Count < size)
        {
            var sum = seeds.Count * 2 + 1;
            var next = new List<int>();
            foreach (var s in seeds) { next.Add(s); next.Add(sum - s); }
            seeds = next;
        }
        return seeds;
    }

    /// <summary>
    /// K.o.-System (Single Elimination) mit Freilosen; n−1 Spiele (plus optional ein
    /// Spiel um Platz 3, in das die beiden Halbfinal-Verlierer einlaufen).
    /// </summary>
    public static List<Pairing> Knockout(IReadOnlyList<int> teamIds, bool thirdPlace = false)
    {
        var n = teamIds.Count;
        if (n < 2) return [];

        var size = NextPowerOfTwo(n);
        var totalRounds = (int)Math.Log2(size);
        var positions = SeedPositions(size)
            .Select(seed => seed - 1 < n ? (int?)teamIds[seed - 1] : null)
            .ToList();

        var pairings = new List<Pairing>();
        var prefill = new Dictionary<(int, int), (int? A, int? B)>();

        Feed? FeedOf(int round, int slot) => round < totalRounds
            ? new Feed(round + 1, slot / 2, slot % 2 == 0 ? "a" : "b")
            : null;

        // Runde 1: echte Paarungen spielen, Freilose vorbelegen.
        for (var s = 0; s < size / 2; s++)
        {
            var a = positions[2 * s];
            var b = positions[2 * s + 1];
            var feeds = FeedOf(1, s);
            if (a is not null && b is not null)
            {
                pairings.Add(new Pairing(1, s, a, b, FeedsInto: feeds));
            }
            else if ((a ?? b) is { } solo && feeds is { } f)
            {
                prefill.TryGetValue((f.Round, f.Slot), out var cur);
                cur = f.Side == "a" ? (solo, cur.B) : (cur.A, solo);
                prefill[(f.Round, f.Slot)] = cur;
            }
        }

        // Runden 2..Finale: jeder Slot ist ein Spiel (evtl. vorbelegt).
        for (var r = 2; r <= totalRounds; r++)
        {
            var slots = size / (int)Math.Pow(2, r);
            for (var s = 0; s < slots; s++)
            {
                prefill.TryGetValue((r, s), out var pf);
                pairings.Add(new Pairing(r, s, pf.A, pf.B, FeedsInto: FeedOf(r, s)));
            }
        }

        // Optionales Spiel um Platz 3: braucht ein Halbfinale (2 Spiele in Runde totalRounds-1).
        if (thirdPlace && totalRounds >= 2)
        {
            var semiRound = totalRounds - 1;
            var bronzeSlot = 1; // Finale liegt auf Slot 0 derselben Runde
            pairings.Add(new Pairing(totalRounds, bronzeSlot, null, null, IsThirdPlace: true));
            for (var i = 0; i < pairings.Count; i++)
            {
                var p = pairings[i];
                if (p.Round == semiRound && !p.IsThirdPlace)
                    pairings[i] = p with { LoserFeedsInto = new Feed(totalRounds, bronzeSlot, p.Slot == 0 ? "a" : "b") };
            }
        }
        return pairings;
    }

    /// <summary>Teilt Teams reihum in Gruppen (Zielgröße ~4, mind. 2 Gruppen).</summary>
    public static List<List<int>> PartitionIntoGroups(IReadOnlyList<int> teamIds, int targetGroupSize = 4)
    {
        var n = teamIds.Count;
        if (n == 0) return [];
        var numGroups = Math.Min(n, Math.Max(2,
            (int)Math.Round((double)n / targetGroupSize, MidpointRounding.AwayFromZero)));
        var groups = new List<List<int>>();
        for (var i = 0; i < numGroups; i++) groups.Add([]);
        for (var i = 0; i < n; i++) groups[i % numGroups].Add(teamIds[i]);
        return groups;
    }

    /// <summary>Gruppenphase: pro Gruppe eine Liga; Gruppen heißen A, B, C …</summary>
    public static List<Pairing> GroupStage(IReadOnlyList<int> teamIds)
    {
        var groups = PartitionIntoGroups(teamIds);
        var pairings = new List<Pairing>();
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var groupName = ((char)('A' + gi)).ToString();
            foreach (var p in RoundRobin(groups[gi]))
                pairings.Add(p with { GroupName = groupName });
        }
        return pairings;
    }

    /// <summary>Dispatcher passend zum Format.</summary>
    public static List<Pairing> Generate(TournamentFormat format, IReadOnlyList<int> teamIds, bool thirdPlace = false) => format switch
    {
        TournamentFormat.RoundRobin => RoundRobin(teamIds),
        TournamentFormat.Knockout => Knockout(teamIds, thirdPlace),
        TournamentFormat.Groups => GroupStage(teamIds),
        _ => [],
    };
}
