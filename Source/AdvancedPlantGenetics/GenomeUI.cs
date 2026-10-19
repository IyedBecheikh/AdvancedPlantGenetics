using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

/// <summary>Text forms of a genome, gated by how much the colony can read.</summary>
public static class GenomeText
{
    public static string Signed(float v)
    {
        var s = Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.0");
        return v > 0f ? "+" + s : s;
    }

    public static string ShortSummary(Genome g)
    {
        if (g == null)
        {
            return null;
        }

        var parts = new List<string>();
        var numbers = Knowledge.SeesNumbers;
        for (var i = 0; i < GeneticsDB.Loci.Count; i++)
        {
            var e = g.Expressed(i);
            var locus = GeneticsDB.Loci[i];
            if (numbers)
            {
                if (e != 0f)
                {
                    parts.Add(locus.symbol + Signed(e));
                }
            }
            else if (locus.Word(e) is { } word)
            {
                parts.Add(word);
            }
        }

        parts.AddRange(g.ExpressedGenes.Select(x => x.label));
        if (numbers && g.Burden > 0f)
        {
            parts.Add("APG.BurdenShort".Translate(g.Burden.ToString("0.#")));
        }

        return string.Join(numbers ? " " : ", ", parts);
    }

    public static string LongDescription(Genome g)
    {
        var sb = new StringBuilder();
        sb.AppendLine("APG.Species".Translate() + ": " + g.species.LabelCap);
        var summary = ShortSummary(g);
        sb.AppendLine("APG.Traits".Translate() + ": " + (summary.NullOrEmpty() ? "APG.NoNotableTraits".Translate().ToString() : summary));
        if (Knowledge.SeesNumbers)
        {
            sb.AppendLine(Effects(g));
        }

        return sb.ToString().TrimEndNewlines();
    }

    /// <summary>Effective plant values for this genome, e.g. "Growth x1.35, yield x1.20, ...".</summary>
    public static string Effects(Genome g)
    {
        var ph = g.Phenotype;
        var s = ph.stats;
        var b = ph.baseStats;
        var parts = new List<string>
        {
            "APG.Eff_Growth".Translate(ph.growthFactor.ToString("0.00"), s.growDays.ToString("0.#")),
            "APG.Eff_Yield".Translate(ph.yieldFactor.ToString("0.00"), s.harvestYield.ToString("0.#")),
            "APG.Eff_FertResponse".Translate(s.fertilitySensitivity.ToStringPercent()),
            "APG.Eff_MinFertility".Translate(s.fertilityMin.ToStringPercent()),
            "APG.Eff_Light".Translate(s.growMinGlow.ToStringPercent(), s.growOptimalGlow.ToStringPercent()),
            "APG.Eff_Temperature".Translate(s.minGrowthTemperature.ToStringTemperature("F0"), s.maxGrowthTemperature.ToStringTemperature("F0"))
        };
        if (ph.blightResistance > 0f)
        {
            parts.Add("APG.Eff_Blight".Translate(ph.blightResistance.ToStringPercent()));
        }

        if (s.harvestAfterGrowth > 0f && b.harvestAfterGrowth <= 0f)
        {
            parts.Add("APG.Eff_Regrows".Translate(s.harvestAfterGrowth.ToStringPercent()));
        }

        if (ph.LifespanDays < 0f && b.lifespanDaysPerGrowDays > 0f)
        {
            parts.Add("APG.Eff_NoLifespan".Translate());
        }

        return string.Join(", ", parts);
    }
}

/// <summary>Draws a genome: loci bars, alleles (when known), genes, capacity and effects.</summary>
public static class GenomeCard
{
    private const float RowHeight = 24f;
    private const float BarMin = -3f;
    private const float BarMax = 10f;

    private static readonly Color BarBg = new(0.12f, 0.12f, 0.12f);
    private static readonly Color NegColor = new(0.85f, 0.35f, 0.3f);
    private static readonly Color BurdenColor = new(0.9f, 0.3f, 0.25f);
    private static readonly Color Muted = new(0.65f, 0.65f, 0.65f);

    public static float Height(Genome g)
    {
        return 30f + GeneticsDB.Loci.Count * RowHeight + 30f + 26f + 60f;
    }

    public static void Draw(Rect rect, Genome g, Genome compare = null, bool? forceGenotype = null, string title = null)
    {
        if (g == null)
        {
            return;
        }

        if (Knowledge.Insight == Insight.None && !DebugSettings.godMode)
        {
            Widgets.Label(rect, "APG.NoInsight".Translate());
            return;
        }

        var numbers = Knowledge.SeesNumbers;
        var genotype = forceGenotype ?? Knowledge.SeesGenotype(g);
        var y = rect.y;

        // Header
        Text.Font = GameFont.Small;
        var rec = PlantGeneticsRegistry.Instance?.Find(g);
        var header = title ?? (rec != null ? rec.FullLabel : g.species.LabelCap.ToString());
        var headerRect = new Rect(rect.x, y, rect.width, 26f);
        Widgets.Label(headerRect, header);
        var tags = new List<string>();
        if (numbers)
        {
            tags.Add("APG.GenShort".Translate(g.generation));
        }

        if (genotype)
        {
            tags.Add(g.Purebred ? "APG.Purebred".Translate() : "APG.Hybrid".Translate(g.HomozygousCount, g.a.Length));
        }

        if (tags.Count > 0)
        {
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = Muted;
            Widgets.Label(headerRect, string.Join("  ", tags));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        y += 30f;

        // Loci
        for (var i = 0; i < GeneticsDB.Loci.Count; i++)
        {
            DrawLocus(new Rect(rect.x, y, rect.width, RowHeight), g, i, compare, numbers, genotype);
            y += RowHeight;
        }

        // Genes
        y += 4f;
        var geneRect = new Rect(rect.x, y, rect.width, 24f);
        DrawGenes(geneRect, g, genotype);
        y += 28f;

        // Capacity
        if (numbers)
        {
            var capRect = new Rect(rect.x, y, rect.width, 22f);
            DrawCapacity(capRect, g);
            y += 26f;
            Text.Font = GameFont.Tiny;
            GUI.color = Muted;
            Widgets.Label(new Rect(rect.x, y, rect.width, rect.yMax - y), GenomeText.Effects(g));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
    }

    private static void DrawLocus(Rect row, Genome g, int i, Genome compare, bool numbers, bool genotype)
    {
        var locus = GeneticsDB.Loci[i];
        var e = g.Expressed(i);
        var x = row.x;

        var sym = new Rect(x, row.y + 2f, 20f, 20f);
        Widgets.DrawBoxSolid(sym, locus.color * 0.55f);
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(sym, locus.symbol);
        x += 26f;

        Text.Anchor = TextAnchor.MiddleLeft;
        // Columns shrink in narrow cards (side-by-side comparisons) so the bar keeps a usable width.
        var labelW = row.width < 380f ? 104f : 130f;
        var valueW = numbers ? 44f : 104f;
        var genotypeW = genotype ? 92f : 0f;
        var compareW = compare != null && numbers ? 50f : 0f;
        var labelRect = new Rect(x, row.y, labelW, row.height);
        Widgets.Label(labelRect, locus.LabelCap.ToString().Truncate(labelW));
        x += labelW + 4f;

        var barWidth = Mathf.Max(50f, row.xMax - x - 6f - valueW - genotypeW - compareW);
        var bar = new Rect(x, row.y + 6f, barWidth, row.height - 12f);
        Widgets.DrawBoxSolid(bar, BarBg);
        var zeroX = bar.x + bar.width * Mathf.InverseLerp(BarMin, BarMax, 0f);
        var valX = bar.x + bar.width * Mathf.InverseLerp(BarMin, BarMax, Mathf.Clamp(numbers ? e : Coarse(e), BarMin, BarMax));
        var fill = new Rect(Mathf.Min(zeroX, valX), bar.y, Mathf.Abs(valX - zeroX), bar.height);
        Widgets.DrawBoxSolid(fill, e >= 0f ? locus.color : NegColor);
        // Era caps: natural +4, mutagen +6, editing +8.
        foreach (var cap in new[] { 4f, 6f, 8f })
        {
            var cx = bar.x + bar.width * Mathf.InverseLerp(BarMin, BarMax, cap);
            Widgets.DrawBoxSolid(new Rect(cx, bar.y, 1f, bar.height), new Color(1f, 1f, 1f, 0.15f));
        }

        Widgets.DrawBoxSolid(new Rect(zeroX, bar.y - 2f, 1f, bar.height + 4f), new Color(1f, 1f, 1f, 0.5f));
        x = bar.xMax + 6f;

        var valueRect = new Rect(x, row.y, valueW, row.height);
        if (numbers)
        {
            Widgets.Label(valueRect, GenomeText.Signed(e));
        }
        else
        {
            GUI.color = Muted;
            Widgets.Label(new Rect(x, row.y, valueW, row.height), locus.Word(e) ?? "APG.Typical".Translate().ToString());
            GUI.color = Color.white;
        }

        x += valueW;
        if (genotype)
        {
            GUI.color = Muted;
            Widgets.Label(new Rect(x, row.y, 90f, row.height), g.a[i] + " / " + g.b[i]);
            GUI.color = Color.white;
            x += genotypeW;
        }

        if (compare != null && numbers)
        {
            var d = e - compare.Expressed(i);
            if (Mathf.Abs(d) > 0.01f)
            {
                GUI.color = d > 0f ? new Color(0.5f, 0.9f, 0.5f) : NegColor;
                Widgets.Label(new Rect(x, row.y, 50f, row.height), (d > 0 ? "▲" : "▼") + GenomeText.Signed(d));
                GUI.color = Color.white;
            }
        }

        Text.Anchor = TextAnchor.UpperLeft;
        TooltipHandler.TipRegion(row, locus.LabelCap + "\n\n" + locus.description);
    }

    private static float Coarse(float e)
    {
        return e >= 4f ? 5f : e >= 1.5f ? 2.5f : e <= -1.5f ? -2f : 0f;
    }

    private static void DrawGenes(Rect rect, Genome g, bool genotype)
    {
        var x = rect.x;
        var shown = genotype ? g.genes.Keys.OrderBy(k => k.order).ToList() : g.ExpressedGenes.ToList();
        if (shown.Count == 0)
        {
            GUI.color = Muted;
            Widgets.Label(rect, "APG.NoSpecialGenes".Translate());
            GUI.color = Color.white;
            return;
        }

        Text.Font = GameFont.Tiny;
        foreach (var gene in shown)
        {
            var expressed = g.Expresses(gene);
            var label = gene.LabelCap.ToString();
            if (genotype)
            {
                label += " x" + g.Copies(gene) + (expressed ? "" : " (" + "APG.Carrier".Translate() + ")");
            }

            var w = Text.CalcSize(label).x + 12f;
            if (x + w > rect.xMax)
            {
                break;
            }

            var chip = new Rect(x, rect.y + 2f, w, rect.height - 4f);
            Widgets.DrawBoxSolid(chip, expressed ? gene.color * 0.5f : new Color(0.25f, 0.25f, 0.25f));
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(chip, label);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(chip, gene.LabelCap + " (" + gene.tier.ToStringHuman() + ")\n\n" + gene.description
                                           + (gene.recessive ? "\n\n" + "APG.RecessiveNote".Translate().ToString() : ""));
            x += w + 4f;
        }

        Text.Font = GameFont.Small;
    }

    private static void DrawCapacity(Rect rect, Genome g)
    {
        var load = g.Load;
        var cap = g.Capacity;
        var label = new Rect(rect.x, rect.y, 160f, rect.height);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(label, "APG.Capacity".Translate());
        var bar = new Rect(rect.x + 160f, rect.y + 5f, rect.width - 160f - 120f, rect.height - 10f);
        Widgets.DrawBoxSolid(bar, BarBg);
        var scale = Mathf.Max(cap, load, 1f);
        var used = Mathf.Clamp(load, 0f, cap);
        Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * used / scale, bar.height), new Color(0.45f, 0.7f, 0.9f));
        if (load > cap)
        {
            Widgets.DrawBoxSolid(new Rect(bar.x + bar.width * cap / scale, bar.y, bar.width * (load - cap) / scale, bar.height), BurdenColor);
        }

        var text = load.ToString("0.#") + " / " + cap.ToString("0.#");
        if (g.Burden > 0f)
        {
            text += "  " + "APG.BurdenPct".Translate((1f - g.Phenotype.burdenFactor).ToStringPercent());
        }

        Widgets.Label(new Rect(bar.xMax + 8f, rect.y, 120f, rect.height), text);
        Text.Anchor = TextAnchor.UpperLeft;
        TooltipHandler.TipRegion(rect, "APG.CapacityDesc".Translate(GeneticsDB.BaseCapacity, (GeneticsDB.BurdenPerPoint).ToStringPercent()));
    }
}

/// <summary>Genetics tab on plants and seed packets.</summary>
public class ITab_PlantGenetics : ITab
{
    private Vector2 scroll;

    public ITab_PlantGenetics()
    {
        size = new Vector2(620f, 480f);
        labelKey = "APG.TabGenetics";
    }

    public override bool IsVisible => Genome != null && (Knowledge.Insight > Insight.None || DebugSettings.godMode);

    private Genome Genome => SelThing switch
    {
        Plant p => CompPlantGenetics.GenomeOf(p),
        { } t => CompSeedGenome.GenomeOf(t),
        _ => null
    };

    protected override void FillTab()
    {
        var g = Genome;
        if (g == null)
        {
            return;
        }

        var rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
        var reg = PlantGeneticsRegistry.Instance;
        var rec = reg.Find(g);
        var y = rect.y;

        // Strain line + actions
        var line = new Rect(rect.x, y, rect.width, 28f);
        var name = rec?.name ?? (SelThing is Plant p && !CompPlantGenetics.Of(p).HasExplicitGenome ? "APG.Landrace".Translate().ToString() : "APG.UnnamedStrain".Translate().ToString());
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(line.x, line.y, line.width - 200f, line.height), name);
        Text.Font = GameFont.Small;
        // Leave room for the tab's own close button in the top-right corner.
        var bx = line.xMax - 26f;
        if (rec != null)
        {
            bx -= 28f;
            if (Widgets.ButtonImage(new Rect(bx, line.y + 2f, 24f, 24f), Tex.Rename))
            {
                Find.WindowStack.Add(new Dialog_RenameCultivar(rec));
            }

            TooltipHandler.TipRegion(new Rect(bx, line.y + 2f, 24f, 24f), "Rename".Translate());
            bx -= 110f;
            var fav = rec.favorite;
            Widgets.CheckboxLabeled(new Rect(bx, line.y + 2f, 104f, 24f), "APG.Favorite".Translate(), ref fav);
            rec.favorite = fav;
        }
        else if (SelThing is Plant plant && Knowledge.Insight >= Insight.Observed)
        {
            bx -= 150f;
            if (Widgets.ButtonText(new Rect(bx, line.y, 150f, 26f), "APG.RecordStrain".Translate()))
            {
                var comp = CompPlantGenetics.Of(plant);
                reg.Register(g, comp.HasExplicitGenome ? "APG.Origin_Bred" : "APG.Origin_Wild");
            }

            TooltipHandler.TipRegion(new Rect(bx, line.y, 150f, 26f), "APG.RecordStrainDesc".Translate());
        }

        y += 32f;

        // Lineage
        if (rec != null && Knowledge.SeesNumbers)
        {
            var pa = reg.Get(rec.parentA);
            var pb = reg.Get(rec.parentB);
            var lineage = "APG.Origin".Translate() + ": " + (rec.origin ?? "APG.Origin_Bred").Translate();
            if (pa != null)
            {
                lineage += "  ·  " + "APG.Parents".Translate() + ": " + pa.name + (pb != null && pb != pa ? " x " + pb.name : "");
            }

            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), lineage);
            GUI.color = Color.white;
            y += 24f;
        }

        var cardRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
        var inner = new Rect(0f, 0f, cardRect.width - 16f, GenomeCard.Height(g));
        Widgets.BeginScrollView(cardRect, ref scroll, inner);
        var parentGenome = rec != null ? reg.Get(rec.parentA)?.template : null;
        GenomeCard.Draw(inner, g, parentGenome);
        Widgets.EndScrollView();
    }
}

public class Dialog_RenameCultivar : Dialog_Rename<CultivarRecordRenamable>
{
    public Dialog_RenameCultivar(CultivarRecord rec) : base(new CultivarRecordRenamable(rec))
    {
    }
}

public class CultivarRecordRenamable(CultivarRecord rec) : IRenameable
{
    public string RenamableLabel
    {
        get => rec.name;
        set => PlantGeneticsRegistry.Instance.Rename(rec, value);
    }

    public string BaseLabel => rec.name;
    public string InspectLabel => rec.name;
}
