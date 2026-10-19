using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

// Patch only after defs are loaded (static constructor), never from the Mod constructor.
[StaticConstructorOnStartup]
public static class GeneticsDB
{
    public const string HarmonyId = "iyedm.AdvancedPlantGenetics";
    public const int NaturalCap = 4;
    public const float BaseCapacity = 10f;
    public const float BurdenPerPoint = 0.07f;

    public static readonly List<GeneLocusDef> Loci;
    public static readonly List<PlantGeneDef> Genes;
    public static readonly HashSet<ThingDef> GeneticPlants = [];
    public static bool Enabled = true;
    public static Harmony Harmony;

    private static readonly Dictionary<GeneLocusDef, int> LocusIndex = new();
    private static readonly Dictionary<LocusEffect, int> EffectIndex = new();
    private static readonly Dictionary<ThingDef, PlantStats> Base = new();

    static GeneticsDB()
    {
        Loci = DefDatabase<GeneLocusDef>.AllDefs.OrderBy(l => l.order).ToList();
        for (var i = 0; i < Loci.Count; i++)
        {
            LocusIndex[Loci[i]] = i;
            EffectIndex[Loci[i].effect] = i;
        }

        Genes = DefDatabase<PlantGeneDef>.AllDefs.OrderBy(g => g.order).ToList();

        var tab = typeof(ITab_PlantGenetics);
        foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (def.plant == null || def.IsCorpse || def.plant.sowTags.NullOrEmpty() || def.category != ThingCategory.Plant)
            {
                continue;
            }

            if (!typeof(Plant).IsAssignableFrom(def.thingClass))
            {
                continue;
            }

            GeneticPlants.Add(def);
            Base[def] = PlantStats.Read(def.plant);
            def.comps ??= [];
            if (!def.comps.Any(c => c is CompProperties_PlantGenetics))
            {
                def.comps.Add(new CompProperties_PlantGenetics());
            }

            def.inspectorTabs ??= [];
            if (!def.inspectorTabs.Contains(tab))
            {
                def.inspectorTabs.Add(tab);
                def.inspectorTabsResolved ??= [];
                def.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(tab));
            }
        }

        Harmony = new Harmony(HarmonyId);
        Harmony.PatchAll(Assembly.GetExecutingAssembly());
        Compat.Apply(Harmony);
    }

    public static int IndexOf(GeneLocusDef locus)
    {
        return LocusIndex[locus];
    }

    public static int IndexOf(LocusEffect effect)
    {
        return EffectIndex.TryGetValue(effect, out var i) ? i : 0;
    }

    public static GeneLocusDef Locus(LocusEffect effect)
    {
        return Loci[IndexOf(effect)];
    }

    public static PlantStats BaseStats(ThingDef def)
    {
        if (!Base.TryGetValue(def, out var s))
        {
            s = PlantStats.Read(def.plant);
            Base[def] = s;
        }

        return s;
    }

    public static bool IsGeneticPlant(ThingDef def)
    {
        return def != null && GeneticPlants.Contains(def);
    }

    public static IEnumerable<ThingDef> SowableCrops => GeneticPlants.Where(d => !d.plant.IsTree && d.plant.harvestedThingDef != null);
}

/// <summary>How much of the genome the colony can read. Grows with research.</summary>
public enum Insight
{
    None,
    Observed,
    Measured,
    Sequenced,
    Engineered
}

public static class Knowledge
{
    public static Insight Insight
    {
        get
        {
            if (APG_DefOf.APG_GeneSplicing.IsFinished)
            {
                return Insight.Engineered;
            }

            if (APG_DefOf.APG_GeneticAnalysis.IsFinished)
            {
                return Insight.Sequenced;
            }

            if (APG_DefOf.APG_PlantBreeding.IsFinished)
            {
                return Insight.Measured;
            }

            return APG_DefOf.APG_SeedSaving.IsFinished ? Insight.Observed : Insight.None;
        }
    }

    public static bool SeesNumbers => Insight >= Insight.Measured || DebugSettings.godMode;

    /// <summary>Full genotype (both alleles, hidden genes) is visible for analysed strains.</summary>
    public static bool SeesGenotype(Genome g)
    {
        if (DebugSettings.godMode)
        {
            return true;
        }

        return g != null && Insight >= Insight.Sequenced && PlantGeneticsRegistry.Instance?.IsAnalyzed(g) == true;
    }
}

[StaticConstructorOnStartup]
public static class Tex
{
    public static readonly Texture2D CollectSeeds = ContentFinder<Texture2D>.Get("UI/APG/CollectSeeds");
    public static readonly Texture2D SeedStock = ContentFinder<Texture2D>.Get("UI/APG/SeedStock");
    public static readonly Texture2D SaveSeeds = ContentFinder<Texture2D>.Get("UI/APG/SaveSeeds");
    public static readonly Texture2D NewExperiment = ContentFinder<Texture2D>.Get("UI/APG/NewExperiment");
    public static readonly Texture2D ChooseResult = ContentFinder<Texture2D>.Get("UI/APG/ChooseResult");
    public static readonly Texture2D Cancel = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");
    public static readonly Texture2D Rename = ContentFinder<Texture2D>.Get("UI/Buttons/Rename");
}
