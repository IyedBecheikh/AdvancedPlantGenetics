using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

/// <summary>The PlantProperties values genetics can change. Swapped into the def around vanilla plant code.</summary>
public struct PlantStats
{
    public float growDays;
    public float harvestYield;
    public float fertilitySensitivity;
    public float fertilityMin;
    public float growMinGlow;
    public float growOptimalGlow;
    public float minGrowthTemperature;
    public float minOptimalGrowthTemperature;
    public float maxOptimalGrowthTemperature;
    public float maxGrowthTemperature;
    public float lifespanDaysPerGrowDays;
    public float harvestAfterGrowth;
    public Pollution pollution;
    public bool vacuumResistant;
    public bool dieIfNoSunlight;
    public bool dieFromToxicFallout;
    public bool dieIfLeafless;
    public bool neverBlightable;

    public static PlantStats Read(PlantProperties p)
    {
        return new PlantStats
        {
            growDays = p.growDays,
            harvestYield = p.harvestYield,
            fertilitySensitivity = p.fertilitySensitivity,
            fertilityMin = p.fertilityMin,
            growMinGlow = p.growMinGlow,
            growOptimalGlow = p.growOptimalGlow,
            minGrowthTemperature = p.minGrowthTemperature,
            minOptimalGrowthTemperature = p.minOptimalGrowthTemperature,
            maxOptimalGrowthTemperature = p.maxOptimalGrowthTemperature,
            maxGrowthTemperature = p.maxGrowthTemperature,
            lifespanDaysPerGrowDays = p.lifespanDaysPerGrowDays,
            harvestAfterGrowth = p.harvestAfterGrowth,
            pollution = p.pollution,
            vacuumResistant = p.vacuumResistant,
            dieIfNoSunlight = p.dieIfNoSunlight,
            dieFromToxicFallout = p.dieFromToxicFallout,
            dieIfLeafless = p.dieIfLeafless,
            neverBlightable = p.neverBlightable
        };
    }

    public readonly void Write(PlantProperties p)
    {
        p.growDays = growDays;
        p.harvestYield = harvestYield;
        p.fertilitySensitivity = fertilitySensitivity;
        p.fertilityMin = fertilityMin;
        p.growMinGlow = growMinGlow;
        p.growOptimalGlow = growOptimalGlow;
        p.minGrowthTemperature = minGrowthTemperature;
        p.minOptimalGrowthTemperature = minOptimalGrowthTemperature;
        p.maxOptimalGrowthTemperature = maxOptimalGrowthTemperature;
        p.maxGrowthTemperature = maxGrowthTemperature;
        p.lifespanDaysPerGrowDays = lifespanDaysPerGrowDays;
        p.harvestAfterGrowth = harvestAfterGrowth;
        p.pollution = pollution;
        p.vacuumResistant = vacuumResistant;
        p.dieIfNoSunlight = dieIfNoSunlight;
        p.dieFromToxicFallout = dieFromToxicFallout;
        p.dieIfLeafless = dieIfLeafless;
        p.neverBlightable = neverBlightable;
    }
}

/// <summary>What a genome does to its species. Computed once per genome.</summary>
public class Phenotype
{
    public readonly PlantStats stats;
    public readonly PlantStats baseStats;
    public readonly float growthFactor = 1f;
    public readonly float yieldFactor = 1f;
    public readonly float burdenFactor = 1f;
    public readonly float blightResistance;
    public readonly float lifespanFactor = 1f;
    public readonly bool frostHardy;
    public readonly bool circadian;
    public readonly bool runners;
    public readonly bool toxinScrubber;
    public readonly float glowRadius;
    public readonly ColorInt glowColor;
    public readonly List<PlantGeneDef> harvestBonus = [];
    public readonly bool neutral;

    public Phenotype(Genome g)
    {
        var def = g.species;
        if (def?.plant == null)
        {
            neutral = true;
            return;
        }

        baseStats = GeneticsDB.BaseStats(def);
        var s = baseStats;
        var burden = g.Burden;
        burdenFactor = Mathf.Pow(1f - GeneticsDB.BurdenPerPoint, burden);
        var anyChange = burden > 0f;

        for (var i = 0; i < GeneticsDB.Loci.Count; i++)
        {
            var locus = GeneticsDB.Loci[i];
            var e = g.Expressed(i);
            if (e == 0f)
            {
                continue;
            }

            anyChange = true;
            switch (locus.effect)
            {
                case LocusEffect.Growth:
                    growthFactor = locus.curve?.Evaluate(e) ?? 1f + locus.perLevel * e;
                    break;
                case LocusEffect.Yield:
                    yieldFactor = locus.curve?.Evaluate(e) ?? 1f + locus.perLevel * e;
                    break;
                case LocusEffect.FertilityResponse:
                    s.fertilitySensitivity = Mathf.Clamp(s.fertilitySensitivity + locus.perLevel * e, 0f, 3f);
                    break;
                case LocusEffect.SoilTolerance:
                    s.fertilityMin = Mathf.Max(0.05f, s.fertilityMin - locus.perLevel * e);
                    break;
                case LocusEffect.LightEfficiency:
                    s.growMinGlow = Mathf.Clamp(s.growMinGlow - locus.perLevel * e, 0f, 1f);
                    s.growOptimalGlow = Mathf.Clamp(s.growOptimalGlow - locus.perLevel * 0.85f * e, s.growMinGlow, 1.2f);
                    break;
                case LocusEffect.ColdTolerance:
                    s.minGrowthTemperature -= locus.perLevel * e;
                    s.minOptimalGrowthTemperature -= locus.perLevel * e;
                    break;
                case LocusEffect.HeatTolerance:
                    s.maxGrowthTemperature += locus.perLevel * e;
                    s.maxOptimalGrowthTemperature += locus.perLevel * e;
                    break;
                case LocusEffect.BlightResistance:
                    blightResistance = Mathf.Clamp01(locus.perLevel * e);
                    break;
                case LocusEffect.Vigor:
                    lifespanFactor = Mathf.Max(0.3f, 1f + locus.perLevel * e);
                    if (s.harvestAfterGrowth > 0f)
                    {
                        s.harvestAfterGrowth = Mathf.Clamp(s.harvestAfterGrowth + 0.03f * e, 0.05f, 0.9f);
                    }

                    break;
            }
        }

        // Keep temperature bands ordered after big negative changes.
        s.minOptimalGrowthTemperature = Mathf.Min(s.minOptimalGrowthTemperature, s.maxOptimalGrowthTemperature - 1f);
        s.minGrowthTemperature = Mathf.Min(s.minGrowthTemperature, s.minOptimalGrowthTemperature - 1f);
        s.maxGrowthTemperature = Mathf.Max(s.maxGrowthTemperature, s.maxOptimalGrowthTemperature + 1f);

        foreach (var gene in g.ExpressedGenes)
        {
            anyChange = true;
            if (gene.regrowGrowth > 0f && s.harvestAfterGrowth <= 0f && !def.plant.IsTree)
            {
                s.harvestAfterGrowth = Mathf.Clamp(gene.regrowGrowth + 0.03f * g.Expressed(GeneticsDB.IndexOf(LocusEffect.Vigor)), 0.05f, 0.9f);
                // A perennial needs a lifespan of several harvests.
                if (s.lifespanDaysPerGrowDays > 0f)
                {
                    s.lifespanDaysPerGrowDays = Mathf.Max(s.lifespanDaysPerGrowDays, 6f);
                }
            }

            frostHardy |= gene.frostHardy;
            circadian |= gene.circadianOverride;
            runners |= gene.runners;
            toxinScrubber |= gene.toxinScrubber;
            if (gene.pollutionTolerant)
            {
                s.pollution = Pollution.Any;
            }

            if (gene.chemosynthetic)
            {
                s.growMinGlow = 0f;
                s.growOptimalGlow = 0f;
                s.dieIfNoSunlight = false;
            }

            if (gene.deathless)
            {
                s.lifespanDaysPerGrowDays = 0f;
                s.vacuumResistant = true;
                s.dieIfNoSunlight = false;
                s.dieFromToxicFallout = false;
            }

            if (gene.frostHardy)
            {
                s.dieIfLeafless = false;
            }

            if (gene.glowRadius > glowRadius)
            {
                glowRadius = gene.glowRadius;
                glowColor = gene.glowColor;
            }

            if (gene.harvestBonusThing != null)
            {
                harvestBonus.Add(gene);
            }
        }

        if (blightResistance >= 1f)
        {
            s.neverBlightable = true;
        }

        growthFactor *= burdenFactor;
        yieldFactor *= burdenFactor;
        s.growDays = baseStats.growDays / Mathf.Max(0.05f, growthFactor);
        s.harvestYield = baseStats.harvestYield * yieldFactor;
        // growDays shrinks with growth speed; keep lifespan in days independent of it.
        if (s.lifespanDaysPerGrowDays > 0f)
        {
            s.lifespanDaysPerGrowDays *= growthFactor * lifespanFactor;
        }

        stats = s;
        neutral = !anyChange;
    }

    public float LifespanDays => stats.lifespanDaysPerGrowDays > 0f ? stats.growDays * stats.lifespanDaysPerGrowDays : -1f;

    /// <summary>Growth-rate factor from fertility for this phenotype at a given cell fertility.</summary>
    public float FertilityFactor(float fertility)
    {
        return fertility * stats.fertilitySensitivity + (1f - stats.fertilitySensitivity);
    }
}

/// <summary>
/// Swaps a plant's phenotype into its (shared) ThingDef for the duration of a vanilla call. Nested calls are safe:
/// every push saves whatever is in the def and the matching pop restores it.
/// </summary>
public static class PhenotypeSwap
{
    private static readonly Stack<(PlantProperties props, PlantStats saved)> Stack = new();

    public static bool Push(Plant plant)
    {
        if (plant == null || !GeneticsDB.Enabled)
        {
            return false;
        }

        var genome = CompPlantGenetics.GenomeOf(plant);
        return genome != null && Push(plant.def, genome);
    }

    public static bool Push(ThingDef def, Genome genome)
    {
        if (def?.plant == null || genome == null || genome.species != def)
        {
            return false;
        }

        var ph = genome.Phenotype;
        if (ph.neutral)
        {
            return false;
        }

        Stack.Push((def.plant, PlantStats.Read(def.plant)));
        ph.stats.Write(def.plant);
        return true;
    }

    public static void Pop()
    {
        if (Stack.Count == 0)
        {
            return;
        }

        var (props, saved) = Stack.Pop();
        saved.Write(props);
    }

    public static int Depth => Stack.Count;
}
