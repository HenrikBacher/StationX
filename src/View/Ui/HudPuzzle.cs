using System;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>Crib dragging on a sheet of squared paper: slide the crib along the ciphertext until no letter falls on itself.</summary>
public partial class Hud
{
    const int Cell = 26;

    void ShowCribPuzzle(int stationId)
    {
        var p = S.PuzzleFor(stationId);
        if (p == null || S.StationById(stationId)?.Current is not int msgId) return;
        int pos = 0;
        var v = Paper(Math.Max(640, (p.Cipher.Length + 2) * Cell + 60));
        v.AddChild(Ink("CRIB DESK  ·  WORK SHEET", 15, Fonts.Heading));
        v.AddChild(Ink("Enigma never enciphers a letter as itself. Slide the crib along the intercept: wherever a crib letter sits on the " +
                       "same letter (marked red) the crib can't be there. Exactly one position is clear. Place it there.", 13, Fonts.Body));

        var sheet = new VBoxContainer();
        sheet.AddThemeConstantOverride("separation", 2);
        v.AddChild(sheet);
        var cipherRow = new HBoxContainer();
        var cribRow = new HBoxContainer();
        var ruler = new HBoxContainer();
        foreach (var row in new[] { ruler, cipherRow, cribRow }) { row.AddThemeConstantOverride("separation", 0); sheet.AddChild(row); }

        var status = Ink("", 13, Fonts.Body);
        v.AddChild(status);

        Label Letter(char c, Color col, Color? bg = null)
        {
            var l = new Label { Text = c.ToString(), HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(Cell, Cell) };
            l.AddThemeFontOverride("font", Fonts.Typewriter);
            l.AddThemeFontSizeOverride("font_size", 18);
            l.AddThemeColorOverride("font_color", col);
            if (bg is Color b)
            {
                var sb = new StyleBoxFlat { BgColor = b };
                l.AddThemeStyleboxOverride("normal", sb);
            }
            return l;
        }

        var red = new Color("a3392b");
        void Redraw()
        {
            foreach (var row in new[] { ruler, cipherRow, cribRow })
                foreach (var c in row.GetChildren()) { row.RemoveChild(c); c.QueueFree(); }
            var crashes = p.Crashes(pos).ToHashSet();
            for (int i = 0; i < p.Cipher.Length; i++)
            {
                var n = new Label { Text = (i + 1) % 5 == 0 ? (i + 1).ToString() : "·", HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(Cell, 14) };
                n.AddThemeFontSizeOverride("font_size", 10);
                n.AddThemeColorOverride("font_color", UiTheme.PaperInk.Lightened(0.45f));
                ruler.AddChild(n);
                bool under = i >= pos && i < pos + p.Crib.Length;
                bool crash = under && crashes.Contains(i - pos);
                cipherRow.AddChild(Letter(p.Cipher[i], crash ? red : UiTheme.PaperInk, under ? new Color(0.85f, 0.8f, 0.65f, 0.6f) : null));
            }
            cribRow.AddChild(new Control { CustomMinimumSize = new Vector2(pos * Cell, Cell) });
            for (int i = 0; i < p.Crib.Length; i++)
                cribRow.AddChild(Letter(p.Crib[i], crashes.Contains(i) ? red : new Color("3d5a80"), crashes.Contains(i) ? new Color(0.9f, 0.6f, 0.55f, 0.5f) : null));
            int n2 = crashes.Count;
            status.Text = n2 == 0 ? $"Position {pos + 1}: no crashes. This could be it." : $"Position {pos + 1}: {n2} crash{(n2 == 1 ? "" : "es")}. Impossible.";
            status.AddThemeColorOverride("font_color", n2 == 0 ? new Color("4f7a3a") : red);
        }

        var left = PaperButton("◀", () => { pos = Math.Max(0, pos - 1); Redraw(); });
        var right = PaperButton("▶", () => { pos = Math.Min(p.Positions - 1, pos + 1); Redraw(); });
        var place = PaperButton("Place the crib here", () =>
        {
            bool live = S.StationById(stationId)?.Current == msgId && S.PuzzleFor(stationId) != null;
            bool ok = live && S.DragCrib(stationId, pos, msgId);
            CloseModal();
            Toast(!live ? "Too late: the desk finished that menu while you worked." : ok ? "The menu is done: off to the Bombes." : "Wrong position: twenty minutes lost.",
                  ok ? EventKind.Good : !live ? EventKind.Info : EventKind.Bad);
        });
        v.AddChild(Row(left, right, Spacer(), PaperButton("Leave it to the desk", CloseModal), place));
        Redraw();
    }

    /// <summary>Banburismus on two pink Banbury sheets: slide the lower one until the repeats (holes that line up) crowd together.</summary>
    void ShowBanbury(int stationId)
    {
        var p = S.BanburyFor(stationId);
        if (p == null || S.StationById(stationId)?.Current is not int msgId) return;
        int off = 0;
        const int C = 20;
        var v = Paper(Math.Max(680, (p.Upper.Length + 2) * C + 60));
        v.AddChild(Ink("HUT 8  ·  BANBURISMUS", 15, Fonts.Heading));
        v.AddChild(Ink("Two messages punched on Banbury sheets. Slide the lower sheet along the upper: where both have the same letter " +
                       "the holes line up (marked). By chance that happens two or three times; where the messages are in depth it " +
                       "happens far more often. Find that offset.", 13, Fonts.Body));
        var upper = new HBoxContainer();
        var lower = new HBoxContainer();
        foreach (var row in new[] { upper, lower }) { row.AddThemeConstantOverride("separation", 0); v.AddChild(row); }
        var status = Ink("", 13, Fonts.Body);
        v.AddChild(status);
        var pink = new Color("e8c4c0");
        var hole = new Color("3a2a28");

        Label Cell(char c, Color bg, bool punched)
        {
            var l = new Label { Text = c.ToString(), HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(C, 24) };
            l.AddThemeFontOverride("font", Fonts.Typewriter);
            l.AddThemeFontSizeOverride("font_size", 15);
            l.AddThemeColorOverride("font_color", punched ? new Color("f4ead8") : UiTheme.PaperInk);
            l.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = punched ? hole : bg });
            return l;
        }

        void Redraw()
        {
            foreach (var row in new[] { upper, lower })
                foreach (var c in row.GetChildren()) { row.RemoveChild(c); c.QueueFree(); }
            var hits = p.Repeats(off).ToHashSet();
            for (int i = 0; i < p.Upper.Length; i++) upper.AddChild(Cell(p.Upper[i], new Color("efe4cf"), hits.Contains(i)));
            lower.AddChild(new Control { CustomMinimumSize = new Vector2(off * C, 24) });
            for (int i = 0; i < p.Lower.Length; i++) lower.AddChild(Cell(p.Lower[i], pink, hits.Contains(i + off)));
            int n = hits.Count;
            status.Text = $"Offset {off}: {n} repeat{(n == 1 ? "" : "s")}." + (n >= 6 ? " That's well beyond chance." : n <= 3 ? " About what chance gives." : "");
        }

        var left = PaperButton("◀", () => { off = Math.Max(0, off - 1); Redraw(); });
        var right = PaperButton("▶", () => { off = Math.Min(BanburyPuzzle.MaxOffset, off + 1); Redraw(); });
        var pick = PaperButton("They're in depth here", () =>
        {
            bool live = S.StationById(stationId)?.Current == msgId && S.BanburyFor(stationId) != null;
            bool ok = live && S.SlideBanbury(stationId, off, msgId);
            CloseModal();
            Toast(!live ? "Too late: the table finished that one while you worked." : ok ? "In depth: the wheel orders are cut for the Bombes." : "Only chance: twenty minutes lost.",
                  ok ? EventKind.Good : !live ? EventKind.Info : EventKind.Bad);
        });
        v.AddChild(Row(left, right, Spacer(), PaperButton("Leave it to the table", CloseModal), pick));
        Redraw();
    }
}
