using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

public class CompProperties_PlantGenetics : CompProperties
{
    public CompProperties_PlantGenetics()
    {
        compClass = typeof(CompPlantGenetics);
    }
}

/// <summary>
/// The genome of one plant. Plants that were never given a genome (wild or wild-type sown) get a natural one derived
/// from their ID: it's never saved, so it costs nothing for the thousands of wild plants on a map.
/// </summary>
public class CompPlantGenetics : ThingComp
{
    private Genome genome;
    private bool explicitGenome;
    private CompPlantGlow glow;

    public Plant Plant => (Plant)parent;
    public bool HasExplicitGenome => explicitGenome;

    public Genome Genome
    {
        get
        {
            if (genome == null)
            {
                genome = Genome.Landrace(parent.def, parent.thingIDNumber ^ parent.def.shortHash);
                explicitGenome = false;
            }

            return genome;
        }
    }

    public static Genome GenomeOf(Thing t)
    {
        if (t is not ThingWithComps twc || !GeneticsDB.IsGeneticPlant(t.def))
        {
            return null;
        }

        return twc.GetComp<CompPlantGenetics>()?.Genome;
    }

    public static CompPlantGenetics Of(Thing t)
    {
        return t is ThingWithComps twc && GeneticsDB.IsGeneticPlant(t.def) ? twc.GetComp<CompPlantGenetics>() : null;
    }

    public void SetGenome(Genome g)
    {
        genome = g;
        explicitGenome = g != null;
        RefreshGlow();
        if (parent.Spawned)
        {
            parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Things);
        }
    }

    public CultivarRecord Cultivar => explicitGenome ? PlantGeneticsRegistry.Instance?.Find(genome) : null;

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref explicitGenome, "apgExplicit");
        if (explicitGenome || Scribe.mode != LoadSaveMode.Saving)
        {
            var g = explicitGenome ? genome : null;
            Scribe_Deep.Look(ref g, "apgGenome");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                genome = g;
                explicitGenome = g != null;
            }
        }
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        RefreshGlow();
    }

    // ------------------------------------------------------------------ bioluminescence

    public void RefreshGlow()
    {
        if (!parent.Spawned)
        {
            return;
        }

        var radius = explicitGenome ? genome.Phenotype.glowRadius : 0f;
        if (radius > 0f && glow == null)
        {
            glow = new CompPlantGlow(this, radius, genome.Phenotype.glowColor);
            parent.AllComps.Add(glow);
            glow.UpdateLit(parent.Map);
        }
        else if (radius <= 0f && glow != null)
        {
            glow.Remove(parent.Map);
            parent.AllComps.Remove(glow);
            glow = null;
        }
        else
        {
            glow?.UpdateLit(parent.Map);
        }
    }

    // ------------------------------------------------------------------ harvest

    /// <summary>Vanilla calls this once per harvest; extra products with genomes are placed here directly.</summary>
    public override IEnumerable<ThingDefCountClass> GetAdditionalHarvestYield()
    {
        // Only engineered genomes carry bonus genes or belong to a strain; wild plants stop here.
        if (!parent.Spawned || !explicitGenome)
        {
            yield break;
        }

        var g = genome;
        foreach (var gene in g.Phenotype.harvestBonus)
        {
            var count = GenMath.RoundRandom(g.Phenotype.stats.harvestYield * gene.harvestBonusPerYield * Mathf.Lerp(0.5f, 1f, Plant.Growth));
            if (count > 0)
            {
                yield return new ThingDefCountClass(gene.harvestBonusThing, count);
            }
        }

        SeedSaving.OnHarvest(Plant, g);
    }

    // ------------------------------------------------------------------ UI

    public override string CompInspectStringExtra()
    {
        if (Knowledge.Insight == Insight.None && !DebugSettings.godMode)
        {
            return null;
        }

        var g = Genome;
        var sb = new StringBuilder();
        var rec = Cultivar;
        sb.Append("APG.Strain".Translate()).Append(": ");
        sb.Append(rec != null ? rec.name : explicitGenome ? "APG.UnnamedStrain".Translate().ToString() : "APG.Landrace".Translate().ToString());
        var summary = GenomeText.ShortSummary(g);
        if (!summary.NullOrEmpty())
        {
            sb.Append(" (").Append(summary).Append(')');
        }

        return sb.ToString();
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        if (!APG_DefOf.APG_SeedSaving.IsFinished || parent.Faction != null && parent.Faction != Faction.OfPlayer)
        {
            yield break;
        }

        if (parent.Spawned && Plant.HarvestableNow)
        {
            yield return new Command_Toggle
            {
                defaultLabel = "APG.CollectSeeds".Translate(),
                defaultDesc = "APG.CollectSeedsDesc".Translate(),
                icon = Tex.CollectSeeds,
                isActive = () => parent.Map?.designationManager.DesignationOn(parent, APG_DefOf.APG_CollectSeeds) != null,
                toggleAction = () =>
                {
                    var dm = parent.Map.designationManager;
                    var d = dm.DesignationOn(parent, APG_DefOf.APG_CollectSeeds);
                    if (d != null)
                    {
                        dm.RemoveDesignation(d);
                    }
                    else
                    {
                        dm.RemoveAllDesignationsOn(parent);
                        dm.AddDesignation(new Designation(parent, APG_DefOf.APG_CollectSeeds));
                    }
                }
            };
        }

        if (DebugSettings.ShowDevGizmos)
        {
            yield return new Command_Action
            {
                defaultLabel = "DEV: random good genome",
                action = () =>
                {
                    var g = Genome.Heirloom(parent.def, Rand.Int);
                    PlantGeneticsRegistry.Instance.Register(g, "APG.Origin_Debug");
                    SetGenome(g);
                }
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: mutate",
                action = () =>
                {
                    var g = Genome.Copy();
                    g.Mutate(new MutationSettings { mutationsMin = 2, mutationsMax = 3, cap = 10, upBias = 0.8f, maxStep = 3, geneChance = 0.5f, mutagen = true });
                    PlantGeneticsRegistry.Instance.Register(g, "APG.Origin_Debug");
                    SetGenome(g);
                }
            };
        }
    }
}

/// <summary>A glower added at runtime to bioluminescent plants only (no cost for other plants).</summary>
public class CompPlantGlow : CompGlower
{
    private readonly CompPlantGenetics owner;

    public CompPlantGlow(CompPlantGenetics owner, float radius, ColorInt color)
    {
        this.owner = owner;
        parent = owner.parent;
        props = new CompProperties_Glower
        {
            glowRadius = radius,
            glowColor = color,
            overlightRadius = 0f
        };
    }

    protected override bool ShouldBeLitNow => parent.Spawned && owner.Plant.Growth >= 0.25f && !owner.Plant.LeaflessNow;

    public void Remove(Map map)
    {
        if (Glows)
        {
            map.glowGrid.DeRegisterGlower(this);
        }
    }

    public override void PostExposeData()
    {
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        yield break;
    }
}
