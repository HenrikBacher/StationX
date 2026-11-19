using System;
using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class Variety
{
    [Fact]
    public void TalesFillVariablesChoicesAndPairs()
    {
        var t = new Tale(new Random(3), new DateOnly(1941, 5, 1));
        t["city"] = "Hull";
        t.Set("ship", "BISMARCK", "Bismarck");
        Assert.Equal("A raid on Hull.", t.Fill("A raid on {city}."));
        Assert.Equal("{missing}", t.Fill("{missing}"));
        for (int i = 0; i < 50; i++)
        {
            var s = t.Fill("{grim|bleak|{city}}");
            Assert.Contains(s, new[] { "grim", "bleak", "Hull" });
        }
        // The German and the English twin make the same choices.
        for (int i = 0; i < 50; i++)
        {
            var (de, en) = t.Pair("{EINS|ZWO|DREI} {ship}", "{one|two|three} {ship}");
            int k = Array.IndexOf(new[] { "EINS", "ZWO", "DREI" }, de.Split(' ')[0]);
            Assert.Equal(new[] { "one", "two", "three" }[k] + " Bismarck", en);
            Assert.EndsWith("BISMARCK", de);
        }
        // Same dice, same text.
        Assert.Equal(new Tale(new Random(9), t.Date).Fill("{a|b|c|d|e|f}{a|b|c|d|e|f}"), new Tale(new Random(9), t.Date).Fill("{a|b|c|d|e|f}{a|b|c|d|e|f}"));
    }

    static readonly Net[] Nets = Enum.GetValues<Net>();

    [Fact]
    public void EveryNetworkHasADecisionOnEveryDate()
    {
        var g = GameState.NewGame(1);
        for (var d = new DateOnly(1939, 11, 1); d < new DateOnly(1944, 7, 1); d = d.AddDays(9))
            foreach (var n in Nets)
                Assert.True(g.ScenarioCount(n, d) > 0, $"{n} on {d}");
    }

    [Fact]
    public void ScenariosReadCleanlyAndNoOptionIsStrictlyWorse()
    {
        var g = GameState.NewGame(1);
        var dates = new[] { new DateOnly(1940, 2, 1), new DateOnly(1940, 5, 28), new DateOnly(1940, 8, 20), new DateOnly(1940, 11, 20),
                            new DateOnly(1941, 5, 24), new DateOnly(1941, 8, 1), new DateOnly(1942, 5, 1), new DateOnly(1942, 7, 1),
                            new DateOnly(1943, 5, 1), new DateOnly(1944, 3, 1) };
        int seen = 0;
        foreach (var n in Nets)
        foreach (var id in g.ScenarioIds(n).Distinct())
        foreach (var date in dates)
        for (int seed = 0; seed < 4; seed++)
        {
            var d = g.ScenarioById(n, id, date, seed);
            seen++;
            foreach (var s in new[] { d.Title, d.Text, d.Context }.Concat(d.Options.SelectMany(o => new[] { o.Label, o.Outcome })))
            {
                Assert.False(string.IsNullOrWhiteSpace(s), $"{id}: empty text");
                Assert.DoesNotContain("{", s);
                Assert.DoesNotContain("}", s);
            }
            Assert.Equal(3, d.Options.Count);
            foreach (var a in d.Options)
            foreach (var b in d.Options.Where(b => b != a))
                Assert.False(Dominated(a, b), $"{id}: '{a.Label}' is strictly worse than '{b.Label}'");
        }
        Assert.True(seen > 200);
    }

    /// <summary>True when b is at least as good as a in everything and better in something.</summary>
    static bool Dominated(DilemmaOption a, DilemmaOption b)
    {
        var av = new[] { a.Rep, -a.Suspicion, a.Funds, a.Points, a.Morale * 100, -a.Tonnage / 1000 };
        var bv = new[] { b.Rep, -b.Suspicion, b.Funds, b.Points, b.Morale * 100, -b.Tonnage / 1000 };
        return av.Zip(bv).All(p => p.Second >= p.First) && av.Zip(bv).Any(p => p.Second > p.First);
    }

    [Fact]
    public void TheSameDecisionDoesNotComeRoundTooSoon()
    {
        var g = GameState.NewGame(5);
        var titles = Enumerable.Range(0, 6).Select(_ =>
        {
            var d = g.DebugIntelDilemma(Net.Red);
            g.NextId++;
            return d.Title;
        }).ToList();
        Assert.True(titles.Distinct().Count() >= 2, string.Join(", ", titles));
    }

    [Fact]
    public void InterceptsCarryRoutineProcedureAndReadCleanly()
    {
        foreach (var net in Nets)
        {
            var date = net == Net.Tunny ? new DateOnly(1943, 6, 1) : new DateOnly(1941, 9, 1);
            var texts = Enumerable.Range(0, 400).Select(i => Intercepts.For(3, i, net, 5, date, 600, false)).ToList();
            Assert.Contains(texts, x => x.German.Contains("ERBITTE WIEDERHOLUNG") || x.German.Contains("GEBURTSTAG") || x.German.Contains("RUFZEICHEN"));
            Assert.All(texts, x => Assert.DoesNotContain("{", x.German + x.English));
            // The network's own traffic still makes up most of it.
            Assert.True(texts.Count(x => x.German.Contains("KEINE BESONDEREN")) < 120, net.ToString());
            Assert.True(texts.Select(x => x.English).Distinct().Count() > 250, $"{net}: {texts.Select(x => x.English).Distinct().Count()} distinct");
        }
    }
}
