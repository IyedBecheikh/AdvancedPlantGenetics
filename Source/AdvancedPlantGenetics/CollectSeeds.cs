using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace AdvancedPlantGenetics;

/// <summary>Growers collect a seed head from designated mature plants.</summary>
public class WorkGiver_CollectSeeds : WorkGiver_Scanner
{
    public override PathEndMode PathEndMode => PathEndMode.Touch;

    public override Danger MaxPathDanger(Pawn pawn)
    {
        return Danger.Deadly;
    }

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
    {
        foreach (var d in pawn.Map.designationManager.designationsByDef[APG_DefOf.APG_CollectSeeds])
        {
            if (d.target.HasThing)
            {
                yield return d.target.Thing;
            }
        }
    }

    public override bool ShouldSkip(Pawn pawn, bool forced = false)
    {
        return !pawn.Map.designationManager.AnySpawnedDesignationOfDef(APG_DefOf.APG_CollectSeeds);
    }

    public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        if (t is not Plant plant || pawn.Map.designationManager.DesignationOn(t, APG_DefOf.APG_CollectSeeds) == null)
        {
            return false;
        }

        if (!plant.HarvestableNow)
        {
            pawn.Map.designationManager.RemoveAllDesignationsOn(t);
            return false;
        }

        return !t.IsForbidden(pawn) && pawn.CanReserve(t, 1, -1, null, forced);
    }

    public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        return JobMaker.MakeJob(APG_DefOf.APG_CollectSeedsJob, t);
    }
}

public class JobDriver_CollectSeeds : JobDriver
{
    private float workDone;

    private Plant Plant => (Plant)job.targetA.Thing;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref workDone, "workDone");
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        this.FailOnThingMissingDesignation(TargetIndex.A, APG_DefOf.APG_CollectSeeds);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        var work = ToilMaker.MakeToil("CollectSeeds");
        work.tickIntervalAction = delta =>
        {
            pawn.skills?.Learn(SkillDefOf.Plants, 0.11f * delta);
            workDone += pawn.GetStatValue(StatDefOf.PlantWorkSpeed) * delta;
            if (workDone >= WorkNeeded)
            {
                var plant = Plant;
                SeedSaving.CollectSeedHead(plant, pawn);
                plant.Map.designationManager.RemoveAllDesignationsOn(plant);
                plant.def.plant.soundHarvestFinish?.PlayOneShot(pawn);
                plant.PlantCollected(pawn, PlantDestructionMode.Cut);
                ReadyForNextToil();
            }
        };
        work.defaultCompleteMode = ToilCompleteMode.Never;
        work.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
        work.WithEffect(EffecterDefOf.Harvest_Plant, TargetIndex.A);
        work.WithProgressBar(TargetIndex.A, () => workDone / WorkNeeded, true);
        work.activeSkill = () => SkillDefOf.Plants;
        yield return work;
    }

    private float WorkNeeded => Plant.def.plant.harvestWork * 1.5f;
}
