using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace AdvancedPlantGenetics;

/// <summary>A zone or grower is set to sow a strain that has no seeds left on the map.</summary>
public class Alert_OutOfSeeds : Alert
{
    private readonly List<GlobalTargetInfo> targets = [];
    private readonly List<string> names = [];
    private int lastTick = -9999;

    public Alert_OutOfSeeds()
    {
        defaultLabel = "APG.AlertOutOfSeeds".Translate();
    }

    private void Recalc()
    {
        if (Find.TickManager.TicksGame - lastTick < 250)
        {
            return;
        }

        lastTick = Find.TickManager.TicksGame;
        targets.Clear();
        names.Clear();
        foreach (var map in Find.Maps)
        {
            var stock = MapComponent_SeedStock.For(map);
            if (stock == null || !stock.HasAny)
            {
                continue;
            }

            foreach (var a in stock.All)
            {
                var rec = a.Cultivar;
                if (rec == null || SeedIndex.Total(map, rec.Signature) > 0)
                {
                    continue;
                }

                // Only matters while there is something left to sow.
                var settable = a.building as IPlantToGrowSettable ?? map.zoneManager.AllZones.FirstOrDefault(z => z.ID == a.zoneId) as IPlantToGrowSettable;
                if (settable == null || settable.GetPlantDefToGrow() != rec.Species)
                {
                    continue;
                }

                var cells = settable.Cells.ToList();
                if (!cells.Any(c => c.GetPlant(map)?.def != rec.Species))
                {
                    continue;
                }

                targets.Add(new GlobalTargetInfo(cells[0], map));
                names.Add(rec.FullLabel);
            }
        }
    }

    public override AlertReport GetReport()
    {
        if (!APG_DefOf.APG_SeedSaving.IsFinished)
        {
            return false;
        }

        Recalc();
        return AlertReport.CulpritsAre(targets);
    }

    public override TaggedString GetExplanation()
    {
        return "APG.AlertOutOfSeedsDesc".Translate(string.Join("\n", names.Distinct().Select(n => "  - " + n)));
    }
}

/// <summary>An experiment finished and is waiting for the player to pick a result.</summary>
public class Alert_ExperimentResults : Alert
{
    public Alert_ExperimentResults()
    {
        defaultLabel = "APG.AlertResults".Translate();
        defaultExplanation = "APG.AlertResultsDesc".Translate();
        defaultPriority = AlertPriority.Medium;
    }

    public override AlertReport GetReport()
    {
        var list = new List<Thing>();
        foreach (var map in Find.Maps)
        {
            foreach (var st in map.listerBuildings.AllBuildingsColonistOfClass<Building_GeneticsStation>())
            {
                if (st.task?.state == TaskState.AwaitingChoice)
                {
                    list.Add(st);
                }
            }
        }

        return AlertReport.CulpritsAre(list);
    }
}

/// <summary>Tribal discovery: a sown landrace plant shows clearly superior traits.</summary>
public class Alert_NotablePlant : Alert
{
    private readonly List<Thing> culprits = [];
    private int lastTick = -99999;

    public Alert_NotablePlant()
    {
        defaultLabel = "APG.AlertNotable".Translate();
        defaultExplanation = "APG.AlertNotableDesc".Translate();
        defaultPriority = AlertPriority.Medium;
    }

    public override AlertReport GetReport()
    {
        if (Knowledge.Insight < Insight.Observed || Knowledge.Insight >= Insight.Sequenced)
        {
            return false;
        }

        if (Find.TickManager.TicksGame - lastTick > 2500)
        {
            lastTick = Find.TickManager.TicksGame;
            culprits.Clear();
            var map = Find.CurrentMap;
            if (map != null)
            {
                foreach (var zone in map.zoneManager.AllZones.OfType<Zone_Growing>())
                {
                    foreach (var c in zone.Cells)
                    {
                        var plant = c.GetPlant(map);
                        if (plant == null || !plant.sown || !plant.HarvestableNow)
                        {
                            continue;
                        }

                        var comp = CompPlantGenetics.Of(plant);
                        if (comp == null || comp.HasExplicitGenome || map.designationManager.DesignationOn(plant) != null)
                        {
                            continue;
                        }

                        if (comp.Genome.Quality >= 3f)
                        {
                            culprits.Add(plant);
                            if (culprits.Count >= 20)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        culprits.RemoveAll(t => t.Destroyed);
        return AlertReport.CulpritsAre(culprits);
    }
}
