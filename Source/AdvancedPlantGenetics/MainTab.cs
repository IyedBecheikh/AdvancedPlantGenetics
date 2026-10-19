using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

/// <summary>Genetics main tab: strain catalog, allele library and era progress.</summary>
public class MainTabWindow_PlantGenetics : MainTabWindow
{
    private enum Page
    {
        Strains,
        Library,
        Progress
    }

    private Page page;
    private Vector2 listScroll;
    private Vector2 cardScroll;
    private Vector2 pageScroll;
    private CultivarRecord selected;
    private ThingDef speciesFilter;
    private bool showHidden;
    private bool onlyWithSeeds;

    public override Vector2 RequestedTabSize => new(1100f, 700f);

    public override void DoWindowContents(Rect inRect)
    {
        var tabs = new List<TabRecord>
        {
            new("APG.PageStrains".Translate(), () => page = Page.Strains, page == Page.Strains),
            new("APG.PageLibrary".Translate(), () => page = Page.Library, page == Page.Library),
            new("APG.PageProgress".Translate(), () => page = Page.Progress, page == Page.Progress)
        };
        var body = new Rect(inRect.x, inRect.y + 32f, inRect.width, inRect.height - 32f);
        TabDrawer.DrawTabs(body, tabs);
        body = body.ContractedBy(10f);
        switch (page)
        {
            case Page.Strains:
                DrawStrains(body);
                break;
            case Page.Library:
                DrawLibrary(body);
                break;
            default:
                DrawProgress(body);
                break;
        }
    }

    // ------------------------------------------------------------------ strains

    private void DrawStrains(Rect rect)
    {
        var reg = PlantGeneticsRegistry.Instance;
        var map = Find.CurrentMap;

        // Filters
        var bar = new Rect(rect.x, rect.y, rect.width, 28f);
        if (Widgets.ButtonText(new Rect(bar.x, bar.y, 200f, 28f), speciesFilter?.LabelCap ?? "APG.AllSpecies".Translate()))
        {
            var opts = new List<FloatMenuOption> { new("APG.AllSpecies".Translate(), () => speciesFilter = null) };
            opts.AddRange(reg.Records.Select(r => r.Species).Distinct().OrderBy(d => d.label).Select(d => new FloatMenuOption(d.LabelCap, () => speciesFilter = d)));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        Widgets.CheckboxLabeled(new Rect(bar.x + 215f, bar.y + 2f, 170f, 24f), "APG.OnlyWithSeeds".Translate(), ref onlyWithSeeds);
        Widgets.CheckboxLabeled(new Rect(bar.x + 400f, bar.y + 2f, 150f, 24f), "APG.ShowHidden".Translate(), ref showHidden);

        var rows = reg.Records
            .Where(r => speciesFilter == null || r.Species == speciesFilter)
            .Where(r => showHidden || !r.hidden)
            .Select(r => (rec: r, seeds: map == null ? 0 : SeedIndex.Total(map, r.Signature)))
            .Where(x => !onlyWithSeeds || x.seeds > 0)
            .OrderByDescending(x => x.rec.favorite)
            .ThenBy(x => x.rec.Species.label)
            .ThenByDescending(x => x.rec.template.Quality)
            .ToList();

        var listRect = new Rect(rect.x, rect.y + 36f, 470f, rect.height - 36f);
        var inner = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(rows.Count * 30f, listRect.height));
        Widgets.BeginScrollView(listRect, ref listScroll, inner);
        var y = 0f;
        foreach (var (rec, seeds) in rows)
        {
            var row = new Rect(0f, y, inner.width, 28f);
            if (rec == selected)
            {
                Widgets.DrawHighlightSelected(row);
            }
            else if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }

            var icon = new Rect(row.x + 2f, row.y + 2f, 24f, 24f);
            Widgets.ThingIcon(icon, rec.Species);
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = rec.hidden ? Color.gray : Color.white;
            Widgets.Label(new Rect(row.x + 32f, row.y, 220f, row.height), (rec.favorite ? "★ " : "") + rec.name);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(row.x + 250f, row.y, 90f, row.height), rec.Species.label);
            Widgets.Label(new Rect(row.x + 340f, row.y, 60f, row.height), seeds > 0 ? "APG.SeedsCount".Translate(seeds).ToString() : "-");
            var flags = (rec.analyzed ? "A" : "") + (rec.archived ? "B" : "");
            Widgets.Label(new Rect(row.x + 410f, row.y, 40f, row.height), flags);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(row, rec.FullLabel + "\n" + GenomeText.ShortSummary(rec.template) + (rec.analyzed ? "\n" + "APG.Analyzed".Translate() : "") + (rec.archived ? "\n" + "APG.Archived".Translate() : ""));
            if (Widgets.ButtonInvisible(row))
            {
                selected = rec;
            }

            y += 30f;
        }

        if (rows.Count == 0)
        {
            Widgets.Label(new Rect(0f, 0f, inner.width, 60f), "APG.NoStrainsYet".Translate());
        }

        Widgets.EndScrollView();

        var detail = new Rect(listRect.xMax + 12f, rect.y + 36f, rect.width - listRect.width - 12f, rect.height - 36f);
        Widgets.DrawMenuSection(detail);
        if (selected == null)
        {
            return;
        }

        var d = detail.ContractedBy(10f);
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(d.x, d.y, d.width - 260f, 32f), selected.FullLabel);
        Text.Font = GameFont.Small;
        if (Widgets.ButtonText(new Rect(d.xMax - 250f, d.y, 80f, 28f), "Rename".Translate()))
        {
            Find.WindowStack.Add(new Dialog_RenameCultivar(selected));
        }

        if (Widgets.ButtonText(new Rect(d.xMax - 165f, d.y, 80f, 28f), selected.favorite ? "APG.Unfavorite".Translate() : "APG.Favorite".Translate()))
        {
            selected.favorite = !selected.favorite;
        }

        if (Widgets.ButtonText(new Rect(d.xMax - 80f, d.y, 80f, 28f), selected.hidden ? "APG.Unhide".Translate() : "APG.Hide".Translate()))
        {
            if (selected.hidden)
            {
                selected.hidden = false;
            }
            else
            {
                reg.Forget(selected);
            }
        }

        var y2 = d.y + 36f;
        if (Knowledge.SeesNumbers)
        {
            var pa = reg.Get(selected.parentA);
            var pb = reg.Get(selected.parentB);
            var text = "APG.Origin".Translate() + ": " + (selected.origin ?? "APG.Origin_Bred").Translate() + "   ·   " + "APG.Created".Translate() +
                       ": " + GenDate.DateShortStringAt(GenDate.TickGameToAbs(selected.createdTick), Find.WorldGrid.LongLatOf(Find.CurrentMap?.Tile ?? 0));
            if (pa != null)
            {
                text += "\n" + "APG.Parents".Translate() + ": " + pa.FullLabel + (pb != null && pb != pa ? " x " + pb.FullLabel : "");
            }

            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(d.x, y2, d.width, 44f), text);
            GUI.color = Color.white;
            y2 += 48f;
        }

        var cardRect = new Rect(d.x, y2, d.width, d.yMax - y2);
        var h = GenomeCard.Height(selected.template);
        var cardInner = new Rect(0f, 0f, cardRect.width - 16f, h);
        Widgets.BeginScrollView(cardRect, ref cardScroll, cardInner);
        GenomeCard.Draw(cardInner, selected.template, reg.Get(selected.parentA)?.template, null, " ");
        Widgets.EndScrollView();
    }

    // ------------------------------------------------------------------ library

    private void DrawLibrary(Rect rect)
    {
        var reg = PlantGeneticsRegistry.Instance;
        var y = rect.y;
        Widgets.Label(new Rect(rect.x, y, rect.width, 48f), "APG.LibraryIntro".Translate());
        y += 52f;
        foreach (var locus in GeneticsDB.Loci)
        {
            var row = new Rect(rect.x, y, rect.width, 26f);
            Widgets.Label(new Rect(row.x, row.y, 200f, row.height), locus.symbol + "  " + locus.LabelCap);
            var bar = new Rect(row.x + 210f, row.y + 6f, 420f, 14f);
            Widgets.DrawBoxSolid(bar, new Color(0.12f, 0.12f, 0.12f));
            float X(float v) => bar.x + bar.width * Mathf.InverseLerp(-3f, 10f, v);
            var min = reg.LibraryMin(locus);
            var max = reg.LibraryMax(locus);
            Widgets.DrawBoxSolid(new Rect(X(min), bar.y, Mathf.Max(2f, X(max) - X(min)), bar.height), locus.color);
            Widgets.DrawBoxSolid(new Rect(X(0f), bar.y - 2f, 1f, bar.height + 4f), Color.white);
            Widgets.Label(new Rect(bar.xMax + 12f, row.y, 200f, row.height), GenomeText.Signed(min) + " … " + GenomeText.Signed(max));
            y += 28f;
        }

        y += 14f;
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(rect.x, y, rect.width, 30f), "APG.GenesHeader".Translate());
        Text.Font = GameFont.Small;
        y += 34f;
        var outer = new Rect(rect.x, y, rect.width, rect.yMax - y);
        var genes = GeneticsDB.Genes.Where(g => g.Available).ToList();
        var inner = new Rect(0f, 0f, outer.width - 16f, genes.Count * 26f);
        Widgets.BeginScrollView(outer, ref pageScroll, inner);
        var gy = 0f;
        foreach (var gene in genes)
        {
            var known = reg.KnowsGene(gene) || gene.Synthetic && gene.syntheticResearch.IsFinished;
            GUI.color = known ? Color.white : Color.gray;
            var r = new Rect(0f, gy, inner.width, 24f);
            Widgets.Label(new Rect(0f, gy, 220f, 24f), known ? gene.LabelCap.ToString() : "???");
            Widgets.Label(new Rect(230f, gy, 120f, 24f), gene.tier.ToStringHuman().CapitalizeFirst());
            var how = known ? (gene.Synthetic ? "APG.GeneSynthetic".Translate() : "APG.GeneKnown".Translate())
                : gene.naturalWeight > 0f ? "APG.GeneSourceNatural".Translate()
                : gene.mutagenWeight > 0f ? "APG.GeneSourceMutagen".Translate()
                : gene.Synthetic ? "APG.GeneSourceResearch".Translate(gene.syntheticResearch.LabelCap)
                : "APG.GeneSourceSpecial".Translate();
            Widgets.Label(new Rect(360f, gy, inner.width - 360f, 24f), how);
            GUI.color = Color.white;
            if (known)
            {
                TooltipHandler.TipRegion(r, gene.description);
            }

            gy += 26f;
        }

        Widgets.EndScrollView();
    }

    // ------------------------------------------------------------------ progress

    private static readonly (TechLevel era, string key, ResearchProjectDef[] research, string[] buildings)[] Eras =
    [
        (TechLevel.Neolithic, "APG.Era_Tribal", [APG_DefOf.APG_SeedSaving], []),
        (TechLevel.Medieval, "APG.Era_Medieval", [APG_DefOf.APG_PlantBreeding, APG_DefOf.APG_Propagation], ["APG_CrossingBench", "APG_PropagationBench"]),
        (TechLevel.Industrial, "APG.Era_Industrial", [APG_DefOf.APG_GeneticAnalysis, APG_DefOf.APG_TissueCulture, APG_DefOf.APG_Mutagenesis],
            ["APG_GeneAnalyzer", "APG_GeneBank", "APG_TissueCultureLab", "APG_MutagenChamber"]),
        (TechLevel.Spacer, "APG.Era_Spacer", [APG_DefOf.APG_GeneSplicing, APG_DefOf.APG_SeedSynthesis, APG_DefOf.APG_SyntheticTraits],
            ["APG_GeneSplicer", "APG_BioinformaticsCore", "APG_SeedSynthesizer"]),
        (TechLevel.Ultra, "APG.Era_Ultra", [APG_DefOf.APG_GenomeSynthesis, APG_DefOf.APG_Phytomining], ["APG_GenesisSynthesizer"])
    ];

    private void DrawProgress(Rect rect)
    {
        var inner = new Rect(0f, 0f, rect.width - 16f, Eras.Length * 150f);
        Widgets.BeginScrollView(rect, ref pageScroll, inner);
        var y = 0f;
        var map = Find.CurrentMap;
        foreach (var (era, key, research, buildings) in Eras)
        {
            var box = new Rect(0f, y, inner.width, 140f);
            Widgets.DrawMenuSection(box);
            var c = box.ContractedBy(10f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(c.x, c.y, 300f, 30f), (key + "_Title").Translate());
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(c.x, c.y + 30f, c.width * 0.55f, c.height - 30f), (key + "_Desc").Translate());
            GUI.color = Color.white;

            var lx = c.x + c.width * 0.58f;
            var ly = c.y;
            foreach (var r in research)
            {
                Check(new Rect(lx, ly, c.xMax - lx, 22f), r.IsFinished, "APG.ResearchItem".Translate(r.LabelCap));
                ly += 22f;
            }

            foreach (var b in buildings)
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(b);
                if (def == null)
                {
                    continue;
                }

                var built = map != null && map.listerBuildings.ColonistsHaveBuilding(def);
                Check(new Rect(lx, ly, c.xMax - lx, 22f), built, "APG.BuildingItem".Translate(def.LabelCap));
                ly += 22f;
            }

            y += 150f;
            _ = era;
        }

        Widgets.EndScrollView();
    }

    private static void Check(Rect r, bool done, string label)
    {
        GUI.color = done ? new Color(0.55f, 0.9f, 0.55f) : Color.gray;
        Widgets.Label(r, (done ? "✔ " : "○ ") + label);
        GUI.color = Color.white;
    }
}
