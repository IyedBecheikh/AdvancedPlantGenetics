using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

public struct OperationOutcome
{
    public List<Genome> candidates;
}

/// <summary>What each operation kind does. Kept in one place so validation, preview and results agree.</summary>
public static class Operations
{
    public static int LibraryCapacity(Map map)
    {
        var cap = 0;
        foreach (var map2 in Find.Maps)
        {
            foreach (var b in map2.listerBuildings.AllBuildingsColonistOfClass<Building_GeneticsStation>())
            {
                if (b.Ext.libraryCapacity > 0 && b.Powered)
                {
                    cap += b.Ext.libraryCapacity;
                }
            }
        }

        return cap;
    }

    public static int CandidateCount(Building_GeneticsStation st, GeneticTask t, Pawn worker)
    {
        var n = t.op.candidates;
        if (st.HasAnalyzer)
        {
            n++;
        }

        var skill = t.op.skill;
        if (worker?.skills != null && skill != null && worker.skills.GetSkill(skill).Level >= 12)
        {
            n++;
        }

        return n;
    }

    /// <summary>Highest level an edit may set at this station for this locus.</summary>
    public static int MaxEditLevel(GeneticOperationDef op, GeneLocusDef locus)
    {
        var cap = Mathf.Min(op.levelCap, locus.maxLevel);
        return op.useLibrary ? Mathf.Min(cap, Mathf.Max(0, PlantGeneticsRegistry.Instance.LibraryMax(locus))) : cap;
    }

    public static IEnumerable<PlantGeneDef> SpliceableGenes(GeneticOperationDef op)
    {
        var reg = PlantGeneticsRegistry.Instance;
        return GeneticsDB.Genes.Where(g => g.Available && g.spliceable && g.tier <= op.maxGeneTier
                                           && (g.Synthetic ? g.syntheticResearch.IsFinished : reg.KnowsGene(g)));
    }

    public static string Validate(Building_GeneticsStation st, GeneticTask t)
    {
        var inputs = t.inputs.Select((_, i) => st.InputGenome(i)).ToList();
        var g = inputs.FirstOrDefault();
        switch (t.op.kind)
        {
            case OperationKind.Cross:
                if (inputs.Count == 2 && inputs[0] != null && inputs[1] != null && inputs[0].species != inputs[1].species)
                {
                    return "APG.MustBeSameSpecies".Translate();
                }

                break;
            case OperationKind.Archive:
                if (g != null && !PlantGeneticsRegistry.Instance.IsAnalyzed(g))
                {
                    return "APG.MustBeAnalyzed".Translate();
                }

                if (PlantGeneticsRegistry.Instance.ArchivedCount >= LibraryCapacity(st.Map) && PlantGeneticsRegistry.Instance.Find(g)?.archived != true)
                {
                    return "APG.LibraryFull".Translate();
                }

                break;
            case OperationKind.EditLocus:
                if (t.locus == null)
                {
                    return "APG.ChooseLocus".Translate();
                }

                if (t.level > MaxEditLevel(t.op, t.locus) || t.level < t.locus.minLevel)
                {
                    return "APG.LevelNotAvailable".Translate();
                }

                break;
            case OperationKind.SpliceGene:
                if (t.gene == null || !SpliceableGenes(t.op).Contains(t.gene))
                {
                    return "APG.ChooseGene".Translate();
                }

                break;
            case OperationKind.RemoveGene:
                if (t.gene == null || g != null && g.Copies(t.gene) == 0)
                {
                    return "APG.ChooseGene".Translate();
                }

                break;
            case OperationKind.SynthesizeSeeds:
                var rec = t.Target;
                if (rec == null || !rec.archived)
                {
                    return "APG.MustBeArchived".Translate();
                }

                if (LibraryCapacity(st.Map) <= 0)
                {
                    return "APG.NoGeneBank".Translate();
                }

                break;
        }

        return null;
    }

    public static bool StockReached(Building_GeneticsStation st, GeneticTask t)
    {
        string sig = t.op.kind == OperationKind.SynthesizeSeeds ? t.Target?.Signature : t.inputs.FirstOrDefault();
        if (sig == null || t.keepCount <= 0)
        {
            return false;
        }

        return SeedIndex.Total(st.Map, sig) + st.Container.Where(x => CompSeedGenome.GenomeOf(x)?.Signature == sig).Sum(x => x.stackCount) >= t.keepCount;
    }

    public static OperationOutcome Run(Building_GeneticsStation st, GeneticTask t, List<Genome> inputs, Pawn worker)
    {
        var reg = PlantGeneticsRegistry.Instance;
        var outcome = new OperationOutcome();
        var g = inputs.FirstOrDefault();
        var map = st.Map;
        var at = st.InteractionCell;
        var parent = g == null ? null : reg.Find(g) ?? reg.Register(g, "APG.Origin_Bred");
        t.revealGenotype = st.HasAnalyzer || t.op.kind == OperationKind.Mutate && APG_DefOf.APG_GeneticAnalysis.IsFinished;

        switch (t.op.kind)
        {
            case OperationKind.Cross:
            case OperationKind.SelfPollinate:
            {
                var other = t.op.kind == OperationKind.Cross && inputs.Count > 1 && inputs[1] != null ? inputs[1] : g;
                var n = CandidateCount(st, t, worker);
                outcome.candidates = [];
                for (var i = 0; i < n; i++)
                {
                    outcome.candidates.Add(Genome.Cross(g, other, MutationSettings.Natural));
                }

                break;
            }
            case OperationKind.Mutate:
            {
                var n = CandidateCount(st, t, worker);
                outcome.candidates = [];
                var settings = new MutationSettings
                {
                    mutationsMin = 1,
                    mutationsMax = 3,
                    maxStep = 2,
                    cap = t.op.levelCap,
                    upBias = 0.6f,
                    geneChance = 0.15f,
                    mutagen = true
                };
                for (var i = 0; i < n; i++)
                {
                    var c = g.Copy();
                    c.generation++;
                    c.Mutate(settings);
                    outcome.candidates.Add(c);
                }

                break;
            }
            case OperationKind.Propagate:
                SeedIndex.Spawn(g, t.op.outputCount, at, map, parent, null, "APG.Origin_Propagated");
                break;
            case OperationKind.Analyze:
            {
                var rec = reg.Register(g, "APG.Origin_Bred", analyzed: true);
                Messages.Message("APG.AnalyzedMessage".Translate(rec.FullLabel), st, MessageTypeDefOf.PositiveEvent);
                break;
            }
            case OperationKind.Archive:
            {
                var rec = reg.Find(g);
                if (rec != null)
                {
                    rec.archived = true;
                    Messages.Message("APG.ArchivedMessage".Translate(rec.FullLabel), st, MessageTypeDefOf.PositiveEvent);
                }

                break;
            }
            case OperationKind.Polyploidy:
                Edited(g, x => x.SetGene(APG_DefOf.APG_Polyploid, 2), t, st, parent);
                break;
            case OperationKind.SyntheticChromosome:
                Edited(g, x => x.SetGene(APG_DefOf.APG_SyntheticChromosome, 2), t, st, parent);
                break;
            case OperationKind.EditLocus:
                Edited(g, x => x.SetLocus(GeneticsDB.IndexOf(t.locus), t.level, t.dominant), t, st, parent);
                break;
            case OperationKind.SpliceGene:
                Edited(g, x => x.SetGene(t.gene, 2), t, st, parent);
                reg.LearnGene(t.gene);
                break;
            case OperationKind.RemoveGene:
                Edited(g, x => x.SetGene(t.gene, 0), t, st, parent);
                break;
            case OperationKind.SynthesizeSeeds:
            {
                var rec = t.Target;
                if (rec != null)
                {
                    SeedIndex.Spawn(rec.template, t.op.outputCount, at, map, null, null, "APG.Origin_Synthesized", true);
                }

                break;
            }
        }

        return outcome;
    }

    private static void Edited(Genome g, System.Action<Genome> edit, GeneticTask t, Building_GeneticsStation st, CultivarRecord parent)
    {
        var c = g.Copy();
        c.generation++;
        edit(c);
        c.Invalidate();
        // Engineered strains are designed, so their genotype is known.
        SeedIndex.Spawn(c, t.op.outputCount, st.InteractionCell, st.Map, parent, null, "APG.Origin_" + t.op.kind, true);
        PlantGeneticsRegistry.Instance.NoticeGenes(c);
        var rec = PlantGeneticsRegistry.Instance.Find(c);
        Messages.Message("APG.EditedMessage".Translate(rec?.FullLabel ?? c.species.label), st, MessageTypeDefOf.PositiveEvent);
    }

    public static string Describe(GeneticTask t)
    {
        var reg = PlantGeneticsRegistry.Instance;
        string Name(string sig)
        {
            var rec = reg.Records.FirstOrDefault(r => r.Signature == sig);
            return rec?.FullLabel ?? "?";
        }

        var s = t.op.LabelCap.ToString();
        if (t.inputs.Count > 0)
        {
            s += ": " + string.Join(" x ", t.inputs.Select(Name));
        }

        switch (t.op.kind)
        {
            case OperationKind.EditLocus when t.locus != null:
                s += " -> " + t.locus.label + " " + GenomeText.Signed(t.level);
                break;
            case OperationKind.SpliceGene or OperationKind.RemoveGene when t.gene != null:
                s += " -> " + t.gene.label;
                break;
            case OperationKind.SynthesizeSeeds when t.Target != null:
                s += ": " + t.Target.FullLabel;
                break;
        }

        if (t.keepCount > 0)
        {
            s += " (" + "APG.KeepCount".Translate(t.keepCount) + ")";
        }

        return s;
    }
}
