using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StationX.Sim;
using Xunit;
using Xunit.Abstractions;

namespace StationX.Tests;

/// <summary>
/// What decides a game: autoplays several seeds and reports, act by act, how busy the Park was, who was on the staff,
/// where the money came from and went, what moved reputation, and which requests were taken, refused or missed and why.
/// Opt-in: PARKDIAG=1 [PARKDIAG_SEEDS=8] [PARKDIAG_DIFF=Normal] dotnet test --filter ParkDiag --logger "console;verbosity=detailed"
/// </summary>
public class ParkDiag(ITestOutputHelper output)
{
    sealed class ActLog
    {
        public string Title = "";
        public int Days, Staff, Wages, Upkeep;
        public Dictionary<Role, int> Roles = new();
        public double Grant, WagesPaid, UpkeepPaid, Rewards, Milestones, OtherIncome, Spent;
        public Dictionary<string, double> Rep = new();
        public double RepStart, RepEnd, RepMin = 100;
        public int Offered, Accepted, Declined, Lapsed, Completed, Failed, FlashOffered, FlashTaken, FlashLost;
        public int Delivered, Asked;
        public Dictionary<string, int> FailWhy = new();
        public Dictionary<StationType, (double busy, double starved, double unstaffed, int n)> Steps = new();
        public Dictionary<Role, (double idle, double work, double carry, double brk, int n)> RoleUse = new();
        public Dictionary<string, int> Findings = new();
        public int Samples;
        /// <summary>Hours of an accepted request's quota, and how far ahead of the deadline it finished.</summary>
        public double SlackSum; public int SlackN;
        public double Fresh, Quota, Mult; public int QuotaDays;
        /// <summary>Hours delivered messages spent in each state (sampled hourly), and how many were delivered.</summary>
        public Dictionary<string, double> Lat = new();
        public int LatN; public double LatAge, LatTransit;
    }

    sealed record Game(int Seed, bool Won, int Day, int Act, List<ActLog> Acts);

    static readonly Regex Money = new(@"\+£(\d+)", RegexOptions.Compiled);
    static readonly Regex Accounts = new(@"grant £(-?\d+), requests £\d+ this week; wages £(\d+), upkeep £(\d+)", RegexOptions.Compiled);

    static Game Play(Difficulty d, int seed)
    {
        var g = GameState.NewGame(seed, d);
        var ai = new AutoPlayer(g) { UseWorkload = Environment.GetEnvironmentVariable("BALANCE_NOWORKLOAD") != "1" };
        var acts = new List<ActLog>();
        ActLog? cur = null;
        int actIx = -1, dayStart = 0;
        Dictionary<string, double> repAt = new();
        var seen = new Dictionary<int, ContractState>();
        int lastSampleHour = -1;
        int deliveredAt = 0;
        var track = new Dictionary<int, (double firstSeen, double created, Dictionary<string, double> h)>();
        void Close()
        {
            if (cur == null) return;
            cur.Days = g.Day - dayStart;
            cur.Staff = g.Staff.Count;
            cur.Wages = g.WeeklyWages;
            cur.Upkeep = g.WeeklyUpkeep;
            foreach (var r in Enum.GetValues<Role>()) cur.Roles[r] = g.Staff.Count(s => s.Role == r);
            foreach (var (k, v) in g.Stats.RepByCause) cur.Rep[k] = v - repAt.GetValueOrDefault(k);
            cur.RepEnd = g.Reputation;
            cur.Delivered = g.Stats.DeliveredTotal - deliveredAt;
        }
        while (g.Status == GameStatus.Playing && g.Day < 320)
        {
            if (g.ActIndex != actIx)
            {
                Close();
                actIx = g.ActIndex; dayStart = g.Day;
                cur = new ActLog { Title = g.Act.Title, RepStart = g.Reputation };
                acts.Add(cur);
                repAt = new(g.Stats.RepByCause);
                deliveredAt = g.Stats.DeliveredTotal;
            }
            if (g.ActCompletePending) g.ContinueAfterAct();
            int funds = g.Funds;
            ai.Think();
            if (g.Funds < funds) cur!.Spent += funds - g.Funds;
            // Contracts seen offered, accepted (or not) and how they ended.
            foreach (var c in g.Contracts)
            {
                seen.TryGetValue(c.Id, out var was);
                bool isNew = !seen.ContainsKey(c.Id);
                if (isNew && c.Tag == null)
                {
                    if (c.Flash) cur!.FlashOffered++; else cur!.Offered++;
                }
                if (c.State == ContractState.Active && (isNew || was == ContractState.Offered))
                {
                    if (c.Flash) cur!.FlashTaken++; else cur!.Accepted++;
                    cur!.Asked += c.Quantity;
                }
                if (c.State == ContractState.Expired && was == ContractState.Offered && !isNew)
                {
                    if (c.Flash) cur!.FlashLost++; else cur!.Declined++;
                }
                seen[c.Id] = c.State;
            }
            int before = g.Funds;
            g.Advance(60);
            int after = g.Funds;
            double counted = 0;
            foreach (var e in g.Events)
            {
                var a = Accounts.Match(e.Text);
                if (a.Success)
                {
                    int gr = int.Parse(a.Groups[1].Value), w = int.Parse(a.Groups[2].Value), u = int.Parse(a.Groups[3].Value);
                    cur!.Grant += gr; cur.WagesPaid += w; cur.UpkeepPaid += u;
                    counted += gr - w - u;
                    continue;
                }
                var m = Money.Match(e.Text);
                if (e.Kind == EventKind.Good && m.Success)
                {
                    int v = int.Parse(m.Groups[1].Value);
                    if (e.Text.Contains("standing order")) cur!.Milestones += v; else if (e.Text.Contains("request complete")) cur!.Rewards += v; else cur!.OtherIncome += v;
                    counted += v;
                }
            }
            double rest = after - before - counted;
            if (rest > 0) cur!.OtherIncome += rest; else cur!.Spent -= rest;
            g.Events.Clear();
            foreach (var (id, st) in seen.Where(x => x.Value == ContractState.Offered).ToList())
                if (g.ContractById(id) == null) { cur!.Declined++; seen[id] = ContractState.Expired; }
            // Endings, with the reason a missed one was missed: where its paper was when the deadline passed.
            foreach (var c in g.Contracts)
            {
                if (!seen.TryGetValue(c.Id, out var was) || was == c.State) continue;
                if (c.State == ContractState.Completed && was == ContractState.Active)
                {
                    cur!.Completed++;
                    cur.SlackSum += (c.Due - g.Now) / 60; cur.SlackN++;
                }
                if (c.State == ContractState.Failed)
                {
                    cur!.Failed++;
                    string why;
                    int unspawned = c.Quantity - c.Spawned;
                    var msgs = g.Messages.Values.Where(x => x.ContractId == c.Id && x.Loc != Loc.Done).ToList();
                    if (unspawned > 0) why = g.Searching(c.Net) ? "not intercepted (frequency change)" : "not yet sent";
                    else if (msgs.Count == 0) why = "nothing left?";
                    else
                    {
                        var stuck = msgs.GroupBy(x => x.Next?.ToString() ?? x.Loc.ToString()).OrderByDescending(x => x.Count()).First().Key;
                        bool keyless = msgs.Any(x => Defs.NetOf(x.Net).Family != Family.Hand && !g.KeyBroken(x.Net, x.Day));
                        why = keyless ? $"key not broken (at {stuck})" : $"at {stuck}";
                    }
                    string kind = c.Flash ? "flash" : c.Standing ? "standing" : c.Tag != null ? "scripted" : "request";
                    why = $"{kind}: {why}";
                    cur.FailWhy[why] = cur.FailWhy.GetValueOrDefault(why) + 1;
                }
                seen[c.Id] = c.State;
            }
            // Where delivered messages spent their time.
            foreach (var m in g.Messages.Values)
            {
                if (!track.TryGetValue(m.Id, out var t)) track[m.Id] = t = (g.Now, m.Created, new());
                string k = m.Next == null ? "key wait" : $"{m.Next} {(m.Loc == Loc.Input ? "tray" : m.Loc == Loc.Processing ? "desk" : m.Loc == Loc.Output ? "out" : m.Loc.ToString())}";
                t.h[k] = t.h.GetValueOrDefault(k) + 1;
            }
            foreach (var id in track.Keys.Where(id => !g.Messages.ContainsKey(id)).ToList())
            {
                var t = track[id];
                track.Remove(id);
                cur!.LatN++; cur.LatAge += (g.Now - t.created) / 60; cur.LatTransit += (t.firstSeen - t.created) / 60;
                foreach (var (k, v) in t.h) cur.Lat[k] = cur.Lat.GetValueOrDefault(k) + v;
            }
            cur!.RepMin = Math.Min(cur.RepMin, g.Reputation);
            if (g.MinuteOfDay < 60 && g.History.Count > 0)
            {
                cur.Fresh += g.History[^1].Fresh; cur.Quota += g.IntelQuota; cur.Mult += g.ExpectationMult; cur.QuotaDays++;
            }
            // Utilisation three times a day, once a full day is on record.
            int hour = (int)(g.Now / 60);
            if (hour % 8 == 4 && hour != lastSampleHour && g.UseHours >= 23)
            {
                lastSampleHour = hour;
                var u = g.Utilisation();
                cur.Samples++;
                foreach (var s in u.Steps)
                {
                    var o = cur.Steps.GetValueOrDefault(s.Type);
                    cur.Steps[s.Type] = (o.busy + s.OfOpen(DeskUse.Working), o.starved + s.OfOpen(DeskUse.Starved), o.unstaffed + s.OfOpen(DeskUse.Unstaffed), o.n + 1);
                }
                foreach (var r in u.Roles)
                {
                    var o = cur.RoleUse.GetValueOrDefault(r.Role);
                    cur.RoleUse[r.Role] = (o.idle + r.Share[(int)PersonUse.Idle], o.work + r.Share[(int)PersonUse.Working], o.carry + r.Share[(int)PersonUse.Carrying], o.brk + r.Share[(int)PersonUse.Break], o.n + 1);
                }
                foreach (var f in u.Findings) cur.Findings[f.Kind] = cur.Findings.GetValueOrDefault(f.Kind) + 1;
            }
        }
        Close();
        return new Game(seed, g.Status == GameStatus.Won, g.Day, g.ActIndex, acts);
    }

    [Fact]
    public void Report()
    {
        if (Environment.GetEnvironmentVariable("PARKDIAG") != "1") return;
        int n = int.TryParse(Environment.GetEnvironmentVariable("PARKDIAG_SEEDS"), out var k) ? k : 8;
        var d = Enum.Parse<Difficulty>(Environment.GetEnvironmentVariable("PARKDIAG_DIFF") ?? "Normal");
        var seeds = Enumerable.Range(0, n).Select(i => 3 + i * 37).ToArray();
        var games = new Game[seeds.Length];
        System.Threading.Tasks.Parallel.For(0, seeds.Length, i => games[i] = Play(d, seeds[i]));
        foreach (var gm in games)
            output.WriteLine($"seed {gm.Seed,4}: {(gm.Won ? "won " : "LOST")} day {gm.Day} act {gm.Act}  rep by act: " +
                             string.Join(" ", gm.Acts.Select(a => $"{a.RepEnd:0}")));
        output.WriteLine($"{d}: {games.Count(x => x.Won)}/{games.Length} won");
        int acts = games.Max(x => x.Acts.Count);
        for (int ai = 0; ai < acts; ai++)
        {
            foreach (var (label, set) in new[] { ("all", games), ("won", games.Where(x => x.Won).ToArray()), ("lost", games.Where(x => !x.Won).ToArray()) })
            {
                var logs = set.Where(x => x.Acts.Count > ai).Select(x => x.Acts[ai]).ToList();
                if (logs.Count == 0 || (label != "all" && logs.Count == games.Count(x => x.Acts.Count > ai))) continue;
                if (label != "all")
                {
                    // Won/lost split: just reputation and money, to see what differs.
                    output.WriteLine($"    [{label} {logs.Count}] rep {logs.Average(a => a.RepStart):0}->{logs.Average(a => a.RepEnd):0} " +
                                     $"staff {logs.Average(a => a.Staff):0} done {logs.Average(a => a.Completed):0.0} failed {logs.Average(a => a.Failed):0.0} declined {logs.Average(a => a.Declined):0.0} " +
                                     $"delivered {logs.Average(a => a.Delivered):0} | " + RepLine(logs));
                    continue;
                }
                var a0 = logs[0];
                double days = Math.Max(1, logs.Average(a => a.Days));
                output.WriteLine($"act {ai,2} {a0.Title} ({logs.Count} games, {days:0.0} days) rep {logs.Average(a => a.RepStart):0}->{logs.Average(a => a.RepEnd):0} (min {logs.Average(a => a.RepMin):0})");
                output.WriteLine($"    staff {logs.Average(a => a.Staff):0.0} [" + string.Join(" ", Enum.GetValues<Role>().Select(r => $"{r} {logs.Average(a => a.Roles.GetValueOrDefault(r)):0.0}")) +
                                 $"]  wages/wk £{logs.Average(a => a.Wages):0} upkeep/wk £{logs.Average(a => a.Upkeep):0}");
                output.WriteLine($"    money in act: grant £{logs.Average(a => a.Grant):0} rewards £{logs.Average(a => a.Rewards):0} milestones £{logs.Average(a => a.Milestones):0} other £{logs.Average(a => a.OtherIncome):0}" +
                                 $" | wages £{logs.Average(a => a.WagesPaid):0} upkeep £{logs.Average(a => a.UpkeepPaid):0} spent £{logs.Average(a => a.Spent):0}");
                output.WriteLine($"    requests: offered {logs.Average(a => a.Offered):0.0} accepted {logs.Average(a => a.Accepted):0.0} declined/lapsed {logs.Average(a => a.Declined):0.0} done {logs.Average(a => a.Completed):0.0} failed {logs.Average(a => a.Failed):0.0}" +
                                 $"  flash {logs.Average(a => a.FlashTaken):0.0}/{logs.Average(a => a.FlashOffered):0.0} (refused {logs.Average(a => a.FlashLost):0.0})" +
                                 $"  asked {logs.Average(a => a.Asked) / days:0.0}/day delivered {logs.Average(a => a.Delivered) / days:0.0}/day" +
                                 $"  slack {(logs.Sum(a => a.SlackN) > 0 ? logs.Sum(a => a.SlackSum) / logs.Sum(a => a.SlackN) : 0):0} h");
                if (logs.Sum(a => a.QuotaDays) > 0)
                    output.WriteLine($"    quota {logs.Sum(a => a.Quota) / logs.Sum(a => a.QuotaDays):0.0}/day fresh {logs.Sum(a => a.Fresh) / logs.Sum(a => a.QuotaDays):0.0}/day slip x{logs.Sum(a => a.Mult) / logs.Sum(a => a.QuotaDays):0.00}");
                int ln = logs.Sum(a => a.LatN);
                if (ln > 0)
                    output.WriteLine($"    latency: age {logs.Sum(a => a.LatAge) / ln:0.0} h, before the Park {logs.Sum(a => a.LatTransit) / ln:0.0} h; in the Park: " +
                        string.Join(" ", logs.SelectMany(a => a.Lat).GroupBy(x => x.Key).Select(x => (x.Key, v: x.Sum(y => y.Value) / ln)).Where(x => x.v >= 0.2).OrderByDescending(x => x.v).Select(x => $"{x.Key} {x.v:0.0}")));
                var why = logs.SelectMany(a => a.FailWhy).GroupBy(x => x.Key).Select(x => $"{x.Key} ×{x.Sum(y => y.Value)}");
                if (why.Any()) output.WriteLine("    missed: " + string.Join("; ", why));
                output.WriteLine("    rep: " + RepLine(logs));
                var steps = logs.SelectMany(a => a.Steps).GroupBy(x => x.Key).OrderBy(x => Array.IndexOf(GameState.UseOrder, x.Key))
                    .Select(x => $"{x.Key} {100 * x.Sum(y => y.Value.busy) / x.Sum(y => y.Value.n):0}%/{100 * x.Sum(y => y.Value.unstaffed) / x.Sum(y => y.Value.n):0}u");
                if (steps.Any()) output.WriteLine("    busy/unstaffed: " + string.Join(" ", steps));
                var roles = logs.SelectMany(a => a.RoleUse).GroupBy(x => x.Key)
                    .Select(x => $"{x.Key} idle {100 * x.Sum(y => y.Value.idle) / x.Sum(y => y.Value.n):0}% work {100 * x.Sum(y => y.Value.work) / x.Sum(y => y.Value.n):0}% carry {100 * x.Sum(y => y.Value.carry) / x.Sum(y => y.Value.n):0}% brk {100 * x.Sum(y => y.Value.brk) / x.Sum(y => y.Value.n):0}%");
                if (roles.Any()) output.WriteLine("    roles: " + string.Join(" | ", roles));
                var finds = logs.SelectMany(a => a.Findings).GroupBy(x => x.Key).Select(x => $"{x.Key} {x.Sum(y => y.Value) / (double)Math.Max(1, logs.Sum(a => a.Samples)):P0}");
                if (finds.Any()) output.WriteLine("    findings (share of samples): " + string.Join(" ", finds));
            }
        }
    }

    static string RepLine(List<ActLog> logs) =>
        string.Join(" ", logs.SelectMany(a => a.Rep).GroupBy(x => x.Key).Select(x => (x.Key, v: x.Sum(y => y.Value) / logs.Count))
                              .Where(x => Math.Abs(x.v) >= 0.5).OrderBy(x => x.v).Select(x => $"{x.Key} {x.v:+0;-0}"));
}
