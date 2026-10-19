using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

/// <summary>Which strain a growing zone or plant grower sows, and whether its harvests save seed.</summary>
public class SeedAssignment : IExposable
{
    public int zoneId = -1;
    public Building building;
    public int cultivarId = -1;
    public bool saveSeeds;
    public int seedsSaved;
    public int offTypesCulled;

    public void ExposeData()
    {
        Scribe_Values.Look(ref zoneId, "zoneId", -1);
        Scribe_References.Look(ref building, "building");
        Scribe_Values.Look(ref cultivarId, "cultivarId", -1);
        Scribe_Values.Look(ref saveSeeds, "saveSeeds");
        Scribe_Values.Look(ref seedsSaved, "seedsSaved");
        Scribe_Values.Look(ref offTypesCulled, "offTypesCulled");
    }

    public CultivarRecord Cultivar => PlantGeneticsRegistry.Instance?.Get(cultivarId);
}

public class MapComponent_SeedStock(Map map) : MapComponent(map)
{
    private List<SeedAssignment> assignments = [];

    public static MapComponent_SeedStock For(Map map)
    {
        return map?.GetComponent<MapComponent_SeedStock>();
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref assignments, "assignments", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            assignments ??= [];
            assignments.RemoveAll(a => a == null || a.zoneId < 0 && a.building == null);
        }
    }

    public override void MapComponentTick()
    {
        if (Find.TickManager.TicksGame % 2500 == 17)
        {
            assignments.RemoveAll(a => a.building != null ? a.building.Destroyed : !map.zoneManager.AllZones.Any(z => z.ID == a.zoneId));
        }
    }

    public SeedAssignment Get(IPlantToGrowSettable settable, bool create = false)
    {
        SeedAssignment a = null;
        if (settable is Zone zone)
        {
            a = assignments.FirstOrDefault(x => x.zoneId == zone.ID && x.building == null);
            if (a == null && create)
            {
                a = new SeedAssignment { zoneId = zone.ID };
                assignments.Add(a);
            }
        }
        else if (settable is Building b)
        {
            a = assignments.FirstOrDefault(x => x.building == b);
            if (a == null && create)
            {
                a = new SeedAssignment { building = b };
                assignments.Add(a);
            }
        }

        return a;
    }

    public SeedAssignment At(IntVec3 c)
    {
        var settable = c.GetPlantToGrowSettable(map);
        return settable == null ? null : Get(settable);
    }

    /// <summary>The strain assigned to a cell, if it matches the plant the cell wants.</summary>
    public CultivarRecord CultivarAt(IntVec3 c, ThingDef plantDef)
    {
        var rec = At(c)?.Cultivar;
        return rec != null && (plantDef == null || rec.Species == plantDef) ? rec : null;
    }

    public IEnumerable<SeedAssignment> All => assignments;

    public bool HasAny => assignments.Count > 0;
}

/// <summary>Seed formation in fields: harvest seed saving and collected seed heads.</summary>
public static class SeedSaving
{
    /// <summary>Genome of the nearest neighbouring plant of the same species, the pollen donor. Self if none.</summary>
    public static Genome PollenDonor(Plant plant, Genome self)
    {
        if (!plant.Spawned)
        {
            return self;
        }

        var map = plant.Map;
        var donors = new List<Genome>();
        foreach (var c in GenRadial.RadialCellsAround(plant.Position, 1.9f, false))
        {
            if (!c.InBounds(map))
            {
                continue;
            }

            var other = c.GetPlant(map);
            if (other != null && other != plant && other.def == plant.def && !other.Destroyed && other.Growth > 0.3f)
            {
                donors.Add(CompPlantGenetics.GenomeOf(other));
            }
        }

        return donors.Where(g => g != null).RandomElementWithFallback(self);
    }

    public static void OnHarvest(Plant plant, Genome genome)
    {
        var map = plant.Map;
        var assignment = MapComponent_SeedStock.For(map)?.At(plant.Position);
        if (assignment == null || !assignment.saveSeeds || !APG_DefOf.APG_SeedSaving.IsFinished)
        {
            return;
        }

        var rec = PlantGeneticsRegistry.Instance.Find(genome);
        if (rec == null)
        {
            return;
        }

        var seedCount = Rand.Chance(0.35f) ? 2 : 1;
        var kept = 0;
        for (var i = 0; i < seedCount; i++)
        {
            var child = Genome.Cross(genome, PollenDonor(plant, genome), MutationSettings.Natural);
            // Rogueing: only true-to-type seed is kept.
            if (child.Signature == genome.Signature)
            {
                kept++;
            }
            else
            {
                assignment.offTypesCulled++;
            }
        }

        if (kept > 0)
        {
            assignment.seedsSaved += kept;
            var g = genome.Copy();
            GenPlace.TryPlaceThing(SeedIndex.MakeSeeds(g, kept), plant.Position, map, ThingPlaceMode.Near);
            SeedIndex.Dirty(map);
        }
    }

    /// <summary>A collected seed head: the plant crossed with a neighbour, all seeds share the result.</summary>
    public static void CollectSeedHead(Plant plant, Pawn pawn)
    {
        var map = plant.Map;
        var comp = CompPlantGenetics.Of(plant);
        if (comp == null)
        {
            return;
        }

        var self = comp.Genome;
        var newGenes = new List<PlantGeneDef>();
        var child = Genome.Cross(self, PollenDonor(plant, self), MutationSettings.Natural, newGenes);
        var parentRec = PlantGeneticsRegistry.Instance.Find(self) ?? PlantGeneticsRegistry.Instance.Register(self, comp.HasExplicitGenome ? "APG.Origin_Bred" : "APG.Origin_Wild");
        var skill = pawn?.skills?.GetSkill(SkillDefOf.Plants)?.Level ?? 5;
        var count = 2 + skill / 5;
        SeedIndex.Spawn(child, count, plant.Position, map, parentRec, null, "APG.Origin_Collected");
        Messages.Message("APG.SeedsCollected".Translate(count, plant.def.label, PlantGeneticsRegistry.Instance.Find(child)?.name ?? ""), new TargetInfo(plant.Position, map), MessageTypeDefOf.PositiveEvent, false);
    }
}

/// <summary>Seed stock and seed saving gizmos for zones and plant growers.</summary>
public static class SeedStockGizmos
{
    public static IEnumerable<Gizmo> For(IPlantToGrowSettable settable)
    {
        if (!APG_DefOf.APG_SeedSaving.IsFinished)
        {
            yield break;
        }

        var map = settable is Zone z ? z.Map : ((Thing)settable).Map;
        var stock = MapComponent_SeedStock.For(map);
        if (stock == null)
        {
            yield break;
        }

        var plantDef = settable.GetPlantDefToGrow();
        if (!GeneticsDB.IsGeneticPlant(plantDef))
        {
            yield break;
        }

        var assignment = stock.Get(settable);
        var rec = assignment?.Cultivar;
        if (rec != null && rec.Species != plantDef)
        {
            assignment.cultivarId = -1;
            rec = null;
        }

        var label = rec != null
            ? "APG.SeedStockNamed".Translate(rec.name, SeedIndex.Available(map, rec.Signature))
            : "APG.SeedStockWild".Translate();
        yield return new Command_Action
        {
            defaultLabel = label,
            defaultDesc = "APG.SeedStockDesc".Translate(plantDef.label),
            icon = Tex.SeedStock,
            action = () => Find.WindowStack.Add(new FloatMenu(Options(map, settable, plantDef).ToList()))
        };

        if (rec != null)
        {
            yield return new Command_Toggle
            {
                defaultLabel = "APG.SaveSeeds".Translate(),
                defaultDesc = "APG.SaveSeedsDesc".Translate() + (assignment.seedsSaved + assignment.offTypesCulled > 0
                    ? "\n\n" + "APG.SaveSeedsStats".Translate(assignment.seedsSaved, assignment.offTypesCulled).ToString()
                    : ""),
                icon = Tex.SaveSeeds,
                isActive = () => assignment.saveSeeds,
                toggleAction = () => assignment.saveSeeds = !assignment.saveSeeds
            };
        }
    }

    private static IEnumerable<FloatMenuOption> Options(Map map, IPlantToGrowSettable settable, ThingDef plantDef)
    {
        // Apply to every selected zone/grower of this species, like vanilla's plant selection.
        var targets = Find.Selector.SelectedObjects.OfType<IPlantToGrowSettable>().Where(s => s.GetPlantDefToGrow() == plantDef).ToList();
        if (!targets.Contains(settable))
        {
            targets.Add(settable);
        }

        var stock = MapComponent_SeedStock.For(map);
        yield return new FloatMenuOption("APG.WildType".Translate(), () =>
        {
            foreach (var t in targets)
            {
                var a = stock.Get(t);
                if (a != null)
                {
                    a.cultivarId = -1;
                }
            }
        });

        var reg = PlantGeneticsRegistry.Instance;
        var candidates = reg.ForSpecies(plantDef)
            .Select(r => (rec: r, seeds: SeedIndex.Total(map, r.Signature)))
            .Where(x => x.seeds > 0 || x.rec.favorite || x.rec.archived)
            .OrderByDescending(x => x.rec.favorite)
            .ThenByDescending(x => x.rec.template.Quality)
            .ToList();
        foreach (var (rec, seeds) in candidates)
        {
            var summary = GenomeText.ShortSummary(rec.template);
            var text = rec.name + " (" + "APG.SeedsCount".Translate(seeds) + ")" + (summary.NullOrEmpty() ? "" : " - " + summary);
            yield return new FloatMenuOption(text, () =>
            {
                foreach (var t in targets)
                {
                    stock.Get(t, true).cultivarId = rec.id;
                }
            });
        }

        if (candidates.Count == 0)
        {
            yield return new FloatMenuOption("APG.NoStrainsWithSeeds".Translate(plantDef.label), null);
        }
    }
}
