namespace StationX.Sim;

/// <summary>
/// Flu waves. Wartime winters brought influenza epidemics (the worst in December 1943, when the Prime Minister himself
/// went down with pneumonia), and a Park of crowded huts, shared canteens and tired people billeted together caught it
/// fast. Here: once a winter at most (own dice), the Medical Officer warns of a wave a few days off. For two weeks colds
/// and flu are several times as likely. Precautions (staggered meals, windows open, anyone with a temperature sent home
/// at once) halve that for a price.
/// </summary>
public sealed partial class GameState
{
    public double FluFrom { get; set; } = -1;
    public double FluUntil { get; set; } = -1;
    public bool FluPrecautions { get; set; }
    /// <summary>The winter (the year it began) of the last wave, so there is one at most each winter.</summary>
    public int FluWinter { get; set; }

    public bool FluWave => Now >= FluFrom && Now < FluUntil;
    public bool FluComing => FluFrom > Now;

    /// <summary>How much likelier colds and flu are now.</summary>
    public double FluMult => !FluWave ? 1 : FluPrecautions ? Defs.FluWaveMult * Defs.FluPrecautionShare : Defs.FluWaveMult;

    int WinterYear => Date.Month >= 10 ? Date.Year : Date.Year - 1;

    void DailyFlu()
    {
        bool wasWave = FluUntil > 0 && Now - Defs.MinutesPerDay < FluUntil && Now >= FluUntil;
        if (wasWave) Emit(EventKind.Good, "The flu has run its course. The Medical Officer says the worst is over.");
        if (FluFrom > 0 && Now - Defs.MinutesPerDay < FluFrom && Now >= FluFrom)
            Emit(EventKind.Bad, "The flu has reached the Park. Expect people off sick for the next fortnight.");
        if (Mode != GameMode.Campaign || ActIndex < 1 || !Winter || FluWinter == WinterYear || FluFrom > Now || FluWave || Dilemmas.Count > 0) return;
        var r = Rng.Seeded(Mix(Seed, Day, 0xF1A0));
        if (!r.Chance(Defs.FluChance)) return;
        WarnOfFlu();
    }

    /// <summary>For screenshots and tests.</summary>
    public void DebugFlu()
    {
        FluWinter = 0;
        WarnOfFlu();
    }

    void WarnOfFlu()
    {
        FluWinter = WinterYear;
        FluFrom = Now + Defs.FluWarningDays * Defs.MinutesPerDay;
        FluUntil = FluFrom + Defs.FluDays * Defs.MinutesPerDay;
        FluPrecautions = false;
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, Title = "Influenza", Tag = "flu",
            Text = $"The Medical Officer reports flu in Bletchley and the villages, filling the cottage hospital. It will be in the huts within " +
                   $"{Defs.FluWarningDays} days, and with everyone crowded together it will spread fast: for a fortnight, expect several times " +
                   "the usual number off sick.",
            Context = "Precautions mean staggered meals in the canteen, windows open whatever the weather, and anyone with a temperature sent " +
                      $"straight back to their billet. They would halve the spread. Rested people catch it less, and a sick bay gets the ill back sooner.",
            Options =
            {
                new() { Label = "Carry on and hope",
                    Outcome = "The Park carries on as before. The Medical Officer writes a short, unhappy note." },
                new() { Label = $"Take precautions (£{Defs.FluPrecautionCost})", Funds = -Defs.FluPrecautionCost, Action = "flu",
                    Outcome = "Notices go up in every hut, the canteen runs three sittings, and the windows stay open. Nobody likes it." },
            },
        });
    }
}
