using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AdvancedPlantGenetics;

/// <summary>
/// Vanilla plant methods run with the plant's phenotype swapped into its def, so growth, light, temperature, fertility,
/// lifespan, season and yield code all see genetic values. Only sown or engineered plants are affected: wild plants
/// skip this entirely (no genome is generated for them), which keeps the cost for big wild maps at one dictionary lookup.
/// </summary>
[HarmonyPatch]
public static class Patch_PlantPhenotypeSwap
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Plant), nameof(Plant.TickLong));
        yield return AccessTools.PropertyGetter(typeof(Plant), nameof(Plant.GrowthRate));
        yield return AccessTools.Method(typeof(Plant), nameof(Plant.YieldNow));
        yield return AccessTools.Method(typeof(Plant), nameof(Plant.PlantCollected));
        yield return AccessTools.PropertyGetter(typeof(Plant), nameof(Plant.CurrentDyingDamagePerTick));
        yield return AccessTools.Method(typeof(Plant), nameof(Plant.GetInspectString));
        yield return AccessTools.Method(typeof(Plant), "CheckMakeLeafless");
    }

    public static void Prefix(Plant __instance, out bool __state)
    {
        __state = GeneticPlant.Affects(__instance) && PhenotypeSwap.Push(__instance);
    }

    public static void Finalizer(bool __state)
    {
        if (__state)
        {
            PhenotypeSwap.Pop();
        }
    }
}

public static class GeneticPlant
{
    /// <summary>Genetics apply to explicitly engineered plants and to anything the colony sowed.</summary>
    public static bool Affects(Plant plant)
    {
        if (!GeneticsDB.Enabled || !GeneticsDB.IsGeneticPlant(plant.def))
        {
            return false;
        }

        var comp = plant.GetComp<CompPlantGenetics>();
        return comp != null && (comp.HasExplicitGenome || plant.sown);
    }

    public static Phenotype PhenotypeOf(Plant plant)
    {
        return Affects(plant) ? plant.GetComp<CompPlantGenetics>().Genome.Phenotype : null;
    }
}

[HarmonyPatch(typeof(Plant), nameof(Plant.TickLong))]
public static class Patch_PlantTickLong_Extras
{
    public static void Postfix(Plant __instance)
    {
        if (__instance.Destroyed || !__instance.Spawned)
        {
            return;
        }

        var comp = CompPlantGenetics.Of(__instance);
        if (comp == null || !comp.HasExplicitGenome)
        {
            return;
        }

        var ph = comp.Genome.Phenotype;
        if (ph.neutral)
        {
            return;
        }

        if (ph.glowRadius > 0f)
        {
            comp.RefreshGlow();
        }

        if (ph.runners && __instance.Growth > 0.5f && Rand.MTBEventOccurs(1.5f, 60000f, 2000f))
        {
            PlantSpecials.SpreadRunner(__instance, comp.Genome);
        }

        if (ph.toxinScrubber && ModsConfig.BiotechActive && Rand.MTBEventOccurs(1f, 60000f, 2000f))
        {
            PlantSpecials.Scrub(__instance);
        }
    }
}

public static class PlantSpecials
{
    public static void SpreadRunner(Plant plant, Genome genome)
    {
        var map = plant.Map;
        foreach (var c in GenAdj.CellsAdjacent8Way(plant).InRandomOrder())
        {
            if (!c.InBounds(map) || c.GetPlant(map) != null)
            {
                continue;
            }

            var settable = c.GetPlantToGrowSettable(map);
            if (settable == null || settable.GetPlantDefToGrow() != plant.def || !plant.def.CanNowPlantAt(c, map))
            {
                continue;
            }

            var child = (Plant)GenSpawn.Spawn(plant.def, c, map);
            child.Growth = 0.05f;
            child.sown = true;
            CompPlantGenetics.Of(child)?.SetGenome(genome.Copy());
            return;
        }
    }

    public static void Scrub(Plant plant)
    {
        var map = plant.Map;
        foreach (var c in GenRadial.RadialCellsAround(plant.Position, 1.5f, true))
        {
            if (c.InBounds(map) && map.pollutionGrid.IsPolluted(c))
            {
                map.pollutionGrid.SetPolluted(c, false);
                return;
            }
        }
    }
}

[HarmonyPatch(typeof(Plant), "Resting", MethodType.Getter)]
public static class Patch_PlantResting
{
    public static void Postfix(Plant __instance, ref bool __result)
    {
        if (__result && GeneticPlant.PhenotypeOf(__instance) is { circadian: true })
        {
            __result = false;
        }
    }
}

[HarmonyPatch(typeof(Plant), "LeaflessTemperatureThresh", MethodType.Getter)]
public static class Patch_PlantLeaflessThresh
{
    public static void Postfix(Plant __instance, ref float __result)
    {
        if (GeneticPlant.PhenotypeOf(__instance) is { frostHardy: true })
        {
            __result -= 20f;
        }
    }
}

[HarmonyPatch(typeof(Plant), nameof(Plant.BlightableNow), MethodType.Getter)]
public static class Patch_PlantBlightable
{
    public static void Postfix(Plant __instance, ref bool __result)
    {
        if (__result && GeneticPlant.PhenotypeOf(__instance) is { blightResistance: >= 1f })
        {
            __result = false;
        }
    }
}

[HarmonyPatch(typeof(Plant), nameof(Plant.CropBlighted))]
public static class Patch_PlantCropBlighted
{
    public static bool Prefix(Plant __instance)
    {
        var ph = GeneticPlant.PhenotypeOf(__instance);
        return ph == null || ph.blightResistance <= 0f || !Rand.Chance(ph.blightResistance);
    }
}

// ---------------------------------------------------------------------- sowing

/// <summary>Cheap access to a map's seed assignments; returns null on maps where nothing is assigned.</summary>
public static class SowContext
{
    private static Map lastMap;
    private static MapComponent_SeedStock lastStock;

    public static CultivarRecord CultivarAt(Map map, IntVec3 c, ThingDef plantDef)
    {
        if (map == null || !GeneticsDB.IsGeneticPlant(plantDef))
        {
            return null;
        }

        if (map != lastMap)
        {
            lastMap = map;
            lastStock = MapComponent_SeedStock.For(map);
        }

        if (lastStock == null || !lastStock.HasAny)
        {
            return null;
        }

        return lastStock.CultivarAt(c, plantDef);
    }
}

/// <summary>Planting rules (minimum fertility, map temperature) use the strain assigned to the cell.</summary>
[HarmonyPatch(typeof(PlantUtility), nameof(PlantUtility.CanEverPlantAt),
    [typeof(ThingDef), typeof(IntVec3), typeof(Map), typeof(Thing), typeof(bool), typeof(bool), typeof(bool)],
    [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal])]
public static class Patch_CanEverPlantAt
{
    public static void Prefix(ThingDef plantDef, IntVec3 c, Map map, out bool __state)
    {
        var rec = SowContext.CultivarAt(map, c, plantDef);
        __state = rec != null && PhenotypeSwap.Push(plantDef, rec.template);
    }

    public static void Finalizer(bool __state)
    {
        if (__state)
        {
            PhenotypeSwap.Pop();
        }
    }
}

[HarmonyPatch(typeof(WorkGiver_GrowerSow), nameof(WorkGiver_GrowerSow.JobOnCell))]
public static class Patch_GrowerSowJobOnCell
{
    public static void Prefix(Pawn pawn, IntVec3 c, out CultivarRecord __state)
    {
        var map = pawn.Map;
        var settable = map == null ? null : SowContext.CultivarAt(map, c, c.GetPlantToGrowSettable(map)?.GetPlantDefToGrow());
        __state = settable;
        if (__state != null && !PhenotypeSwap.Push(__state.Species, __state.template))
        {
            // Neutral genome: nothing pushed, but keep the record for the seed check.
            __state = new CultivarRecord { id = -__state.id, template = __state.template };
        }
    }

    public static void Postfix(Pawn pawn, IntVec3 c, CultivarRecord __state, ref Job __result)
    {
        if (__state == null || __result == null || __result.def != JobDefOf.Sow)
        {
            return;
        }

        if (SeedIndex.Available(pawn.Map, __state.Signature) <= 0)
        {
            JobFailReason.Is("APG.NoSeedsReason".Translate());
            __result = null;
        }
    }

    public static void Finalizer(CultivarRecord __state)
    {
        if (__state != null && __state.id > 0)
        {
            PhenotypeSwap.Pop();
        }
    }
}

/// <summary>Sowing a strain gives the new plant that genome and uses one seed packet from the map.</summary>
[HarmonyPatch(typeof(JobDriver_PlantSow), "MakeNewToils")]
public static class Patch_PlantSowToils
{
    private static readonly ConditionalWeakTable<JobDriver_PlantSow, string> Pending = new();

    public static IEnumerable<Toil> Postfix(IEnumerable<Toil> values, JobDriver_PlantSow __instance)
    {
        foreach (var toil in values)
        {
            if (toil.initAction != null && toil.defaultCompleteMode == ToilCompleteMode.Never)
            {
                var original = toil.initAction;
                var driver = __instance;
                toil.initAction = () =>
                {
                    original();
                    OnSowStart(driver);
                };
                toil.AddFinishAction(() => OnSowEnd(driver));
            }

            yield return toil;
        }
    }

    private static void OnSowStart(JobDriver_PlantSow driver)
    {
        if (driver.job.targetA.Thing is not Plant plant || !plant.Spawned)
        {
            return;
        }

        var rec = SowContext.CultivarAt(plant.Map, plant.Position, plant.def);
        if (rec == null)
        {
            return;
        }

        CompPlantGenetics.Of(plant)?.SetGenome(rec.template.Copy());
        SeedIndex.AddPending(plant.Map, rec.Signature, 1);
        Pending.Remove(driver);
        Pending.Add(driver, rec.Signature);
    }

    private static void OnSowEnd(JobDriver_PlantSow driver)
    {
        if (!Pending.TryGetValue(driver, out var signature))
        {
            return;
        }

        Pending.Remove(driver);
        var map = driver.pawn.Map;
        if (map == null)
        {
            return;
        }

        SeedIndex.AddPending(map, signature, -1);
        if (driver.job.targetA.Thing is Plant { Destroyed: false } plant && plant.LifeStage != PlantLifeStage.Sowing)
        {
            SeedIndex.TakeOne(map, signature, plant.Position);
        }
    }
}

[HarmonyPatch(typeof(Zone_Growing), nameof(Zone_Growing.GetGizmos))]
public static class Patch_ZoneGrowingGizmos
{
    public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Zone_Growing __instance)
    {
        foreach (var g in values)
        {
            yield return g;
        }

        foreach (var g in SeedStockGizmos.For(__instance))
        {
            yield return g;
        }
    }
}

[HarmonyPatch(typeof(Building_PlantGrower), nameof(Building_PlantGrower.GetGizmos))]
public static class Patch_PlantGrowerGizmos
{
    public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Building_PlantGrower __instance)
    {
        foreach (var g in values)
        {
            yield return g;
        }

        if (__instance.Faction == Faction.OfPlayer)
        {
            foreach (var g in SeedStockGizmos.For(__instance))
            {
                yield return g;
            }
        }
    }
}
