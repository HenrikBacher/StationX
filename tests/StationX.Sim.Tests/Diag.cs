using StationX.Sim;
using Xunit;
using Xunit.Abstractions;

namespace StationX.Tests;

public class Diag(ITestOutputHelper output)
{
    [Fact]
    public void DailyLog()
    {
        // Opt-in: DIAG=1 SEED=42 DAYS=60 dotnet test --filter Diag --logger "console;verbosity=detailed"
        if (Environment.GetEnvironmentVariable("DIAG") != "1") return;
        int seed = int.Parse(Environment.GetEnvironmentVariable("SEED") ?? "1942");
        int days = int.Parse(Environment.GetEnvironmentVariable("DAYS") ?? "40");
        var g = Environment.GetEnvironmentVariable("FREE") is string fa ? GameState.NewGame(seed, mode: GameMode.FreePlay, startAct: int.Parse(fa)) : GameState.NewGame(seed);
        var ai = new AutoPlayer(g) { ContinuePastEnigma = Environment.GetEnvironmentVariable("TUNNY") == "1" };
        int lastDay = -1;
        var log = new List<string>();
        while ((g.Status == GameStatus.Playing || (ai.ContinuePastEnigma && g.CanContinue)) && g.Day < days)
        {
            ai.Think();
            foreach (var e in g.Events) if (e.Kind is EventKind.Bad or EventKind.ActStarted) log.Add($"   {g.Day}.{g.MinuteOfDay / 60:00} {e.Kind}: {e.Text[..Math.Min(110, e.Text.Length)]}");
            g.Events.Clear();
            g.Advance(60);
            if (g.Day != lastDay)
            {
                lastDay = g.Day;
                foreach (var l in log) output.WriteLine(l);
                log.Clear();
                var backlog = string.Join(" ", g.Messages.Values.GroupBy(m => m.Next?.ToString() ?? "wait").Select(x => $"{x.Key}:{x.Count()}"));
                var act = g.Contracts.Where(c => c.State == ContractState.Active).Select(c => $"{c.Net}{c.Delivered}/{c.Quantity}");
                output.WriteLine($"day {g.Day,3} act {g.ActIndex} £{g.Funds,6} rep {g.Reputation,3:0} staff {g.Staff.Count,2} E{g.AverageEnergy:0.00} M{g.AverageMorale:0.00} susp {g.MaxSuspicion,3:0} done {g.Stats.ContractsCompleted} fail {g.Stats.ContractsFailed} | {string.Join(",", act)} | {backlog}");
                if (g.Day is 4000)
                    foreach (var s in g.Staff)
                        output.WriteLine($"        {s.Name,-22} {s.Role,-12} {s.Shift,-7} away={s.Away} E{s.Energy:0.00} M{s.Morale:0.00} task={s.Task?.Kind}/{s.Task?.Phase}/{s.Task?.Dest} path={s.Path.Count} at {s.TilePos}");
                if (g.Day >= 25) output.WriteLine("        stations: " + string.Join(" ", g.Stations.GroupBy(s => s.Type).Select(x => $"{x.Key}x{x.Count()}")));
            }
        }
        output.WriteLine($"status {g.Status} act {g.ActIndex}");
    }
}
