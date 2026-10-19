using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

/// <summary>Set up an experiment or production run at a genetics station.</summary>
public class Dialog_NewExperiment : Window
{
    private readonly Building_GeneticsStation station;
    private GeneticOperationDef op;
    private readonly List<CultivarRecord> inputs = [null, null];
    private GeneLocusDef locus;
    private int level;
    private bool dominant = true;
    private PlantGeneDef gene;
    private CultivarRecord target;
    private int keepCount;
    private Vector2 scroll;

    public Dialog_NewExperiment(Building_GeneticsStation station)
    {
        this.station = station;
        doCloseX = true;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
        op = StationOps().FirstOrDefault(o => o.Unlocked);
        locus = GeneticsDB.Loci.FirstOrDefault();
    }

    public override Vector2 InitialSize => new(960f, 680f);

    private IEnumerable<GeneticOperationDef> StationOps()
    {
        return station.Ext.operations.OrderBy(o => o.order);
    }

    private Map Map => station.Map;

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), station.LabelCap);
        Text.Font = GameFont.Small;
        var body = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f - 44f);

        // Operation list
        var left = new Rect(body.x, body.y, 230f, body.height);
        var y = left.y;
        foreach (var o in StationOps())
        {
            var r = new Rect(left.x, y, left.width, 36f);
            if (o == op)
            {
                Widgets.DrawHighlightSelected(r);
            }
            else if (Mouse.IsOver(r))
            {
                Widgets.DrawHighlight(r);
            }

            GUI.color = o.Unlocked ? Color.white : Color.gray;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(r.ContractedBy(6f, 0f), o.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (!o.Unlocked)
            {
                TooltipHandler.TipRegion(r, "APG.NeedsResearch".Translate(o.researchPrerequisite.LabelCap));
            }
            else if (Widgets.ButtonInvisible(r))
            {
                op = o;
            }

            y += 38f;
        }

        // Details
        var right = new Rect(left.xMax + 14f, body.y, body.width - left.width - 14f, body.height);
        Widgets.DrawMenuSection(right);
        var area = right.ContractedBy(10f);
        if (op != null)
        {
            DrawOperation(area);
        }

        // Footer
        var footer = new Rect(inRect.x, inRect.yMax - 38f, inRect.width, 38f);
        var problem = Problem();
        if (problem != null)
        {
            GUI.color = new Color(1f, 0.6f, 0.5f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(footer.x, footer.y, footer.width - 200f, footer.height), problem);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        if (Widgets.ButtonText(new Rect(footer.xMax - 180f, footer.y, 180f, footer.height), "APG.StartExperiment".Translate(), active: problem == null) && problem == null)
        {
            station.SetTask(BuildTask());
            Close();
        }
    }

    private void DrawOperation(Rect area)
    {
        var y = area.y;
        Widgets.Label(new Rect(area.x, y, area.width, 60f), op.description);
        y += 62f;
        var costs = new List<string> { "APG.WorkAmount".Translate(Mathf.RoundToInt(op.workAmount / station.WorkSpeedFactor)) };
        var fuel = station.FuelCost(BuildTask());
        if (fuel > 0f && station.Fuel != null)
        {
            costs.Add("APG.FuelCost".Translate(fuel, station.Fuel.Props.FuelLabel, station.Fuel.Fuel.ToString("0")));
        }

        if (op.skill != null && op.minSkill > 0)
        {
            costs.Add("APG.MinSkill".Translate(op.skill.LabelCap, op.minSkill));
        }

        if (op.HasCandidates)
        {
            costs.Add("APG.CandidatesInfo".Translate(op.candidates + (station.HasAnalyzer ? 1 : 0), op.outputCount));
        }
        else if (op.outputCount > 0 && op.kind is not (OperationKind.Analyze or OperationKind.Archive))
        {
            costs.Add("APG.OutputInfo".Translate(op.outputCount));
        }

        GUI.color = new Color(0.75f, 0.75f, 0.75f);
        Widgets.Label(new Rect(area.x, y, area.width, 24f), string.Join("   ·   ", costs));
        GUI.color = Color.white;
        y += 30f;

        // Inputs
        var inputCount = op.inputs;
        var cardWidth = inputCount == 2 ? (area.width - 10f) / 2f : area.width;
        for (var i = 0; i < inputCount; i++)
        {
            var x = area.x + i * (cardWidth + 10f);
            var idx = i;
            var label = inputs[i] == null ? "APG.ChooseStrain".Translate().ToString() : inputs[i].FullLabel + " (" + "APG.SeedsCount".Translate(SeedIndex.Available(Map, inputs[i].Signature)).ToString() + ")";
            if (Widgets.ButtonText(new Rect(x, y, cardWidth, 30f), label))
            {
                Find.WindowStack.Add(new FloatMenu(StrainOptions(idx).ToList()));
            }
        }

        if (op.kind == OperationKind.SynthesizeSeeds)
        {
            var label = target == null ? "APG.ChooseArchived".Translate().ToString() : target.FullLabel;
            if (Widgets.ButtonText(new Rect(area.x, y, area.width, 30f), label))
            {
                var opts = PlantGeneticsRegistry.Instance.Records.Where(r => r.archived && !r.hidden)
                    .Select(r => new FloatMenuOption(r.FullLabel + " - " + GenomeText.ShortSummary(r.template), () => target = r)).ToList();
                if (opts.Count == 0)
                {
                    opts.Add(new FloatMenuOption("APG.NoArchived".Translate(), null));
                }

                Find.WindowStack.Add(new FloatMenu(opts));
            }

            inputCount = 1;
        }

        y += 36f;

        // Parameters
        var paramHeight = DrawParameters(new Rect(area.x, y, area.width, 70f));
        y += paramHeight;

        // Genome preview
        var cards = new Rect(area.x, y, area.width, area.yMax - y);
        var shown = new List<Genome>();
        if (op.kind == OperationKind.SynthesizeSeeds)
        {
            if (target != null)
            {
                shown.Add(target.template);
            }
        }
        else
        {
            for (var i = 0; i < op.inputs; i++)
            {
                if (inputs[i] != null)
                {
                    shown.Add(inputs[i].template);
                }
            }

            var preview = Preview();
            if (preview != null)
            {
                shown.Add(preview);
            }
        }

        if (shown.Count == 0)
        {
            return;
        }

        var w = (cards.width - 10f * (shown.Count - 1) - 16f) / shown.Count;
        var h = GenomeCard.Height(shown[0]);
        var inner = new Rect(0f, 0f, cards.width - 16f, h);
        Widgets.BeginScrollView(cards, ref scroll, inner);
        for (var i = 0; i < shown.Count; i++)
        {
            var isPreview = i == shown.Count - 1 && Preview() != null;
            var title = isPreview ? "APG.Result".Translate().ToString() : null;
            GenomeCard.Draw(new Rect(i * (w + 10f), 0f, w, h), shown[i], isPreview ? shown[0] : null, isPreview ? true : null, title);
        }

        Widgets.EndScrollView();
    }

    /// <summary>Deterministic operations can show their result before starting.</summary>
    private Genome Preview()
    {
        var g = inputs[0]?.template;
        if (g == null)
        {
            return null;
        }

        var c = g.Copy();
        c.generation++;
        switch (op.kind)
        {
            case OperationKind.EditLocus when locus != null:
                c.SetLocus(GeneticsDB.IndexOf(locus), level, dominant);
                break;
            case OperationKind.SpliceGene when gene != null:
                c.SetGene(gene, 2);
                break;
            case OperationKind.RemoveGene when gene != null:
                c.SetGene(gene, 0);
                break;
            case OperationKind.Polyploidy:
                c.SetGene(APG_DefOf.APG_Polyploid, 2);
                break;
            case OperationKind.SyntheticChromosome:
                c.SetGene(APG_DefOf.APG_SyntheticChromosome, 2);
                break;
            default:
                return null;
        }

        c.Invalidate();
        return c;
    }

    private float DrawParameters(Rect rect)
    {
        var y = rect.y;
        switch (op.kind)
        {
            case OperationKind.EditLocus:
            {
                if (Widgets.ButtonText(new Rect(rect.x, y, 220f, 30f), locus?.LabelCap ?? "APG.ChooseLocus".Translate()))
                {
                    Find.WindowStack.Add(new FloatMenu(GeneticsDB.Loci.Select(l => new FloatMenuOption(l.LabelCap + " (" + "APG.UpTo".Translate(GenomeText.Signed(Operations.MaxEditLevel(op, l))) + ")", () =>
                    {
                        locus = l;
                        level = Mathf.Min(level, Operations.MaxEditLevel(op, l));
                    })).ToList()));
                }

                if (locus != null)
                {
                    var max = Operations.MaxEditLevel(op, locus);
                    var sliderRect = new Rect(rect.x + 232f, y + 4f, rect.width - 232f - 180f, 24f);
                    level = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, level, locus.minLevel, Mathf.Max(max, locus.minLevel + 1), true, "APG.TargetLevel".Translate(GenomeText.Signed(level)), null, null, 1f));
                    level = Mathf.Clamp(level, locus.minLevel, max);
                    Widgets.CheckboxLabeled(new Rect(rect.xMax - 170f, y + 3f, 170f, 24f), "APG.Dominant".Translate(), ref dominant);
                    if (op.useLibrary)
                    {
                        GUI.color = Color.gray;
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(rect.x, y + 32f, rect.width, 20f), "APG.LibraryLimit".Translate(locus.label, GenomeText.Signed(max), GenomeText.Signed(op.levelCap)));
                        Text.Font = GameFont.Small;
                        GUI.color = Color.white;
                    }
                }

                return 58f;
            }
            case OperationKind.SpliceGene:
            case OperationKind.RemoveGene:
            {
                var pool = op.kind == OperationKind.SpliceGene
                    ? Operations.SpliceableGenes(op).ToList()
                    : inputs[0]?.template.genes.Keys.ToList() ?? [];
                if (Widgets.ButtonText(new Rect(rect.x, y, 300f, 30f), gene?.LabelCap ?? "APG.ChooseGene".Translate()))
                {
                    var opts = pool.Select(g => new FloatMenuOption(g.LabelCap + " (" + g.tier.ToStringHuman() + ")", () => gene = g)).ToList();
                    if (opts.Count == 0)
                    {
                        opts.Add(new FloatMenuOption("APG.NoGenesAvailable".Translate(), null));
                    }

                    Find.WindowStack.Add(new FloatMenu(opts));
                }

                if (gene != null)
                {
                    GUI.color = Color.gray;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(rect.x + 310f, y, rect.width - 310f, 34f), gene.description);
                    Text.Font = GameFont.Small;
                    GUI.color = Color.white;
                }

                return 40f;
            }
        }

        if (op.canRepeat)
        {
            var label = keepCount > 0 ? "APG.KeepCount".Translate(keepCount) : "APG.RunOnce".Translate();
            Widgets.Label(new Rect(rect.x, y + 3f, 200f, 24f), label);
            keepCount = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(rect.x + 210f, y + 6f, 260f, 20f), keepCount, 0f, 200f, true, null, null, null, 5f));
            return 34f;
        }

        return 0f;
    }

    private IEnumerable<FloatMenuOption> StrainOptions(int idx)
    {
        var reg = PlantGeneticsRegistry.Instance;
        var other = idx == 1 ? inputs[0] : null;
        var any = false;
        foreach (var sig in SeedIndex.Signatures(Map).ToList())
        {
            var g = SeedIndex.Stacks(Map, sig).Select(CompSeedGenome.GenomeOf).FirstOrDefault();
            if (g == null)
            {
                continue;
            }

            if (other != null && op.kind == OperationKind.Cross && g.species != other.Species)
            {
                continue;
            }

            var rec = reg.Find(g) ?? reg.Register(g, "APG.Origin_Bred");
            any = true;
            var summary = GenomeText.ShortSummary(g);
            yield return new FloatMenuOption(rec.FullLabel + " (" + "APG.SeedsCount".Translate(SeedIndex.Available(Map, sig)) + ")" + (summary.NullOrEmpty() ? "" : " - " + summary), () =>
            {
                inputs[idx] = rec;
                if (idx == 0 && inputs[1] != null && inputs[1].Species != rec.Species)
                {
                    inputs[1] = null;
                }
            });
        }

        if (!any)
        {
            yield return new FloatMenuOption("APG.NoSeedsOnMap".Translate(), null);
        }
    }

    private GeneticTask BuildTask()
    {
        var t = new GeneticTask
        {
            op = op,
            locus = locus,
            level = level,
            dominant = dominant,
            gene = gene,
            cultivarId = target?.id ?? -1,
            keepCount = op != null && op.canRepeat ? keepCount : 0
        };
        if (op == null)
        {
            return t;
        }

        for (var i = 0; i < op.inputs; i++)
        {
            if (inputs[i] != null)
            {
                t.inputs.Add(inputs[i].Signature);
            }
        }

        return t;
    }

    private string Problem()
    {
        if (op == null)
        {
            return "APG.ChooseOperation".Translate();
        }

        if (!op.Unlocked)
        {
            return "APG.NeedsResearch".Translate(op.researchPrerequisite.LabelCap);
        }

        for (var i = 0; i < op.inputs; i++)
        {
            if (inputs[i] == null)
            {
                return "APG.ChooseStrain".Translate();
            }
        }

        if (op.inputs == 2 && inputs[0].Species != inputs[1].Species)
        {
            return "APG.MustBeSameSpecies".Translate();
        }

        if (op.inputs == 2 && inputs[0] == inputs[1] && SeedIndex.Available(Map, inputs[0].Signature) < 2)
        {
            return "APG.NeedTwoPackets".Translate();
        }

        var t = BuildTask();
        switch (op.kind)
        {
            case OperationKind.EditLocus when locus == null:
                return "APG.ChooseLocus".Translate();
            case OperationKind.EditLocus when level > Operations.MaxEditLevel(op, locus):
                return "APG.LevelNotAvailable".Translate();
            case OperationKind.SpliceGene or OperationKind.RemoveGene when gene == null:
                return "APG.ChooseGene".Translate();
            case OperationKind.Archive when !PlantGeneticsRegistry.Instance.IsAnalyzed(inputs[0].template):
                return "APG.MustBeAnalyzed".Translate();
            case OperationKind.SynthesizeSeeds when target == null:
                return "APG.ChooseArchived".Translate();
        }

        _ = t;
        return null;
    }
}

/// <summary>Pick which offspring or mutant to keep.</summary>
public class Dialog_ChooseCandidate : Window
{
    private readonly Building_GeneticsStation station;
    private Vector2 scroll;

    public Dialog_ChooseCandidate(Building_GeneticsStation station)
    {
        this.station = station;
        doCloseX = true;
        forcePause = true;
        absorbInputAroundWindow = true;
    }

    public override Vector2 InitialSize => new(Mathf.Min(UI.screenWidth - 40f, 1300f), Mathf.Min(UI.screenHeight - 40f, 860f));

    public override void DoWindowContents(Rect inRect)
    {
        var task = station.task;
        if (task == null || task.state != TaskState.AwaitingChoice)
        {
            Close();
            return;
        }

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "APG.ChooseResultTitle".Translate(task.op.LabelCap));
        Text.Font = GameFont.Small;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 24f), "APG.ChooseResultHint".Translate(task.op.outputCount));
        GUI.color = Color.white;

        var reg = PlantGeneticsRegistry.Instance;
        var parent = task.parentIds.Count > 0 ? reg.Get(task.parentIds[0])?.template : null;
        var list = new List<(Genome g, string title, bool candidate)>();
        if (parent != null)
        {
            list.Add((parent, "APG.ParentLabel".Translate(reg.Get(task.parentIds[0]).name), false));
        }

        if (task.parentIds.Count > 1 && task.parentIds[1] != task.parentIds[0] && reg.Get(task.parentIds[1]) is { } pb)
        {
            list.Add((pb.template, "APG.ParentLabel".Translate(pb.name), false));
        }

        for (var i = 0; i < task.candidates.Count; i++)
        {
            list.Add((task.candidates[i], "APG.CandidateLabel".Translate(i + 1), true));
        }

        // Grid of cards, three per row, scrolling vertically.
        const int perRow = 3;
        const float gap = 12f;
        var outer = new Rect(inRect.x, inRect.y + 64f, inRect.width, inRect.height - 64f - 44f);
        var cardWidth = (outer.width - 16f - gap * (perRow - 1)) / perRow;
        var h = GenomeCard.Height(task.candidates[0]) + 64f;
        var rows = (list.Count + perRow - 1) / perRow;
        var inner = new Rect(0f, 0f, outer.width - 16f, rows * (h + gap));
        Widgets.BeginScrollView(outer, ref scroll, inner);
        for (var i = 0; i < list.Count; i++)
        {
            var (g, title, candidate) = list[i];
            var r = new Rect(i % perRow * (cardWidth + gap), i / perRow * (h + gap), cardWidth, h);
            Widgets.DrawMenuSection(r);
            var c = r.ContractedBy(8f);
            GenomeCard.Draw(new Rect(c.x, c.y, c.width, c.height - 36f), g, candidate ? parent : null, task.revealGenotype || !candidate && Knowledge.SeesGenotype(g), title);
            if (candidate && Widgets.ButtonText(new Rect(c.x, c.yMax - 30f, c.width, 30f), "APG.KeepThis".Translate()))
            {
                station.Choose(g);
                Close();
                return;
            }
        }

        Widgets.EndScrollView();

        if (Widgets.ButtonText(new Rect(inRect.xMax - 200f, inRect.yMax - 36f, 200f, 36f), "APG.DiscardAll".Translate()))
        {
            station.Choose(null);
            Close();
        }
    }
}
