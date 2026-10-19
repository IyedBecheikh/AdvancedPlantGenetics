using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AdvancedPlantGenetics;

public enum TaskState
{
    Pending,
    AwaitingChoice
}

/// <summary>The experiment or production run a station is set up for.</summary>
public class GeneticTask : IExposable
{
    public GeneticOperationDef op;
    public List<string> inputs = [];
    public GeneLocusDef locus;
    public int level;
    public bool dominant = true;
    public PlantGeneDef gene;
    public int cultivarId = -1;
    public int keepCount;
    public float progress;
    public TaskState state;
    public List<Genome> candidates = [];
    public List<int> parentIds = [];
    public bool revealGenotype;
    public int runs;

    public void ExposeData()
    {
        Scribe_Defs.Look(ref op, "op");
        Scribe_Collections.Look(ref inputs, "inputs", LookMode.Value);
        Scribe_Defs.Look(ref locus, "locus");
        Scribe_Values.Look(ref level, "level");
        Scribe_Values.Look(ref dominant, "dominant", true);
        Scribe_Defs.Look(ref gene, "gene");
        Scribe_Values.Look(ref cultivarId, "cultivarId", -1);
        Scribe_Values.Look(ref keepCount, "keepCount");
        Scribe_Values.Look(ref progress, "progress");
        Scribe_Values.Look(ref state, "state");
        Scribe_Collections.Look(ref candidates, "candidates", LookMode.Deep);
        Scribe_Collections.Look(ref parentIds, "parentIds", LookMode.Value);
        Scribe_Values.Look(ref revealGenotype, "revealGenotype");
        Scribe_Values.Look(ref runs, "runs");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            inputs ??= [];
            candidates ??= [];
            parentIds ??= [];
        }
    }

    public CultivarRecord Target => PlantGeneticsRegistry.Instance.Get(cultivarId);
}

public class Building_GeneticsStation : Building, IThingHolder
{
    private ThingOwner<Thing> container;
    public GeneticTask task;

    private CompPowerTrader power;
    private CompRefuelable fuel;
    private CompAffectedByFacilities facilities;

    public Building_GeneticsStation()
    {
        container = new ThingOwner<Thing>(this);
    }

    public StationExtension Ext => def.GetModExtension<StationExtension>();
    public bool Powered => power == null || power.PowerOn;
    public CompRefuelable Fuel => fuel;
    public ThingOwner Container => container;

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        power = GetComp<CompPowerTrader>();
        fuel = GetComp<CompRefuelable>();
        facilities = GetComp<CompAffectedByFacilities>();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref container, "container", this);
        Scribe_Deep.Look(ref task, "task");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            container ??= new ThingOwner<Thing>(this);
            if (task?.op == null)
            {
                task = null;
            }
        }
    }

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
    }

    public ThingOwner GetDirectlyHeldThings()
    {
        return container;
    }

    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        container.TryDropAll(InteractionCell.IsValid ? InteractionCell : Position, Map, ThingPlaceMode.Near);
        base.DeSpawn(mode);
    }

    // ------------------------------------------------------------------ facilities

    public bool LinkedPowered(ThingDef facilityDef)
    {
        if (facilityDef == null || facilities == null)
        {
            return false;
        }

        foreach (var f in facilities.LinkedFacilitiesListForReading)
        {
            if (f.def == facilityDef && (f.TryGetComp<CompPowerTrader>()?.PowerOn ?? true))
            {
                return true;
            }
        }

        return false;
    }

    public bool HasAnalyzer => LinkedPowered(Ext.analyzerFacility);
    public bool HasCompute => LinkedPowered(Ext.computeFacility);

    public float FuelCost(GeneticTask t)
    {
        if (t?.op == null)
        {
            return 0f;
        }

        var cost = t.op.fuelCost + t.op.fuelPerLevel * Mathf.Max(0, t.level - 2);
        if (HasCompute)
        {
            cost *= 0.75f;
        }

        return Mathf.Ceil(cost);
    }

    public float WorkSpeedFactor => Ext.workSpeed * (HasCompute ? 1.35f : 1f);

    // ------------------------------------------------------------------ inputs

    public List<Genome> LoadedGenomes => container.Select(CompSeedGenome.GenomeOf).Where(g => g != null).ToList();

    /// <summary>Input signatures still missing from the container, one entry per packet needed.</summary>
    public List<string> MissingInputs()
    {
        var missing = new List<string>();
        if (task == null || task.state != TaskState.Pending)
        {
            return missing;
        }

        var have = new Dictionary<string, int>();
        foreach (var t in container)
        {
            var g = CompSeedGenome.GenomeOf(t);
            if (g != null)
            {
                have.TryGetValue(g.Signature, out var n);
                have[g.Signature] = n + t.stackCount;
            }
        }

        foreach (var sig in task.inputs)
        {
            if (have.TryGetValue(sig, out var n) && n > 0)
            {
                have[sig] = n - 1;
            }
            else
            {
                missing.Add(sig);
            }
        }

        return missing;
    }

    public Genome InputGenome(int index)
    {
        if (task == null || index >= task.inputs.Count)
        {
            return null;
        }

        var sig = task.inputs[index];
        return LoadedGenomes.FirstOrDefault(g => g.Signature == sig);
    }

    /// <summary>Why work can't happen now, or null if it can.</summary>
    public string BlockReason()
    {
        if (task == null)
        {
            return "APG.NoTask".Translate();
        }

        if (task.state == TaskState.AwaitingChoice)
        {
            return "APG.AwaitingChoice".Translate();
        }

        if (!task.op.Unlocked)
        {
            return "APG.NeedsResearch".Translate(task.op.researchPrerequisite.LabelCap);
        }

        if (!Powered)
        {
            return "NoPower".Translate();
        }

        if (MissingInputs().Count > 0)
        {
            return "APG.WaitingForInputs".Translate();
        }

        if (fuel != null && fuel.Fuel < FuelCost(task))
        {
            return "APG.NeedsFuel".Translate(FuelCost(task), fuel.Props.FuelLabel);
        }

        var invalid = Operations.Validate(this, task);
        if (invalid != null)
        {
            return invalid;
        }

        if (task.keepCount > 0 && Operations.StockReached(this, task))
        {
            return "APG.StockReached".Translate(task.keepCount);
        }

        return null;
    }

    public bool ReadyToWork => BlockReason() == null;

    public void SetTask(GeneticTask t)
    {
        CancelTask();
        task = t;
    }

    public void CancelTask()
    {
        task = null;
        if (Spawned)
        {
            container.TryDropAll(InteractionCell, Map, ThingPlaceMode.Near);
        }
    }

    // ------------------------------------------------------------------ work

    public void DoWork(float amount, Pawn worker)
    {
        if (task == null || task.state != TaskState.Pending)
        {
            return;
        }

        task.progress += amount * WorkSpeedFactor;
        if (task.progress >= task.op.workAmount)
        {
            Complete(worker);
        }
    }

    protected override void TickInterval(int delta)
    {
        base.TickInterval(delta);
        if (Ext.autonomous && this.IsHashIntervalTick(250, delta) && ReadyToWork)
        {
            DoWork(250f, null);
        }
    }

    public void Complete(Pawn worker)
    {
        var t = task;
        var inputs = t.inputs.Select((_, i) => InputGenome(i)).ToList();
        var reg = PlantGeneticsRegistry.Instance;
        t.parentIds = inputs.Where(g => g != null).Select(g => (reg.Find(g) ?? reg.Register(g, "APG.Origin_Bred")).id).ToList();
        fuel?.ConsumeFuel(FuelCost(t));
        var outcome = Operations.Run(this, t, inputs, worker);

        // Inputs: consumed, or returned to the shelf.
        if (t.op.consumesInputs)
        {
            foreach (var sig in t.inputs)
            {
                var stack = container.FirstOrDefault(x => CompSeedGenome.GenomeOf(x)?.Signature == sig);
                if (stack != null)
                {
                    if (stack.stackCount > 1)
                    {
                        stack.stackCount--;
                    }
                    else
                    {
                        container.Remove(stack);
                        stack.Destroy();
                    }
                }
            }
        }

        container.TryDropAll(InteractionCell, Map, ThingPlaceMode.Near);
        t.progress = 0f;
        t.runs++;

        if (outcome.candidates != null && outcome.candidates.Count > 0)
        {
            t.candidates = outcome.candidates;
            t.state = TaskState.AwaitingChoice;
            Messages.Message("APG.ResultsReady".Translate(LabelCap), this, MessageTypeDefOf.PositiveEvent);
            return;
        }

        if (t.op.canRepeat && t.keepCount > 0 && !Operations.StockReached(this, t))
        {
            return; // reload and go again
        }

        if (!(t.op.canRepeat && t.keepCount > 0))
        {
            task = null;
        }
    }

    public void Choose(Genome chosen)
    {
        if (task == null)
        {
            return;
        }

        var reg = PlantGeneticsRegistry.Instance;
        var pa = task.parentIds.Count > 0 ? reg.Get(task.parentIds[0]) : null;
        var pb = task.parentIds.Count > 1 ? reg.Get(task.parentIds[1]) : null;
        if (chosen != null)
        {
            SeedIndex.Spawn(chosen, task.op.outputCount, InteractionCell, Map, pa, pb, "APG.Origin_" + task.op.kind, task.revealGenotype);
            reg.NoticeGenes(chosen);
        }

        task = null;
    }

    // ------------------------------------------------------------------ UI

    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var g in base.GetGizmos())
        {
            yield return g;
        }

        if (Faction != Faction.OfPlayer)
        {
            yield break;
        }

        yield return new Command_Action
        {
            defaultLabel = "APG.NewExperiment".Translate(),
            defaultDesc = "APG.NewExperimentDesc".Translate(),
            icon = Tex.NewExperiment,
            action = () => Find.WindowStack.Add(new Dialog_NewExperiment(this))
        };

        if (task != null)
        {
            if (task.state == TaskState.AwaitingChoice)
            {
                yield return new Command_Action
                {
                    defaultLabel = "APG.ChooseResult".Translate(),
                    defaultDesc = "APG.ChooseResultDesc".Translate(),
                    icon = Tex.ChooseResult,
                    action = () => Find.WindowStack.Add(new Dialog_ChooseCandidate(this))
                };
            }

            yield return new Command_Action
            {
                defaultLabel = "APG.CancelExperiment".Translate(),
                defaultDesc = "APG.CancelExperimentDesc".Translate(),
                icon = Tex.Cancel,
                action = CancelTask
            };

            if (DebugSettings.ShowDevGizmos && task.state == TaskState.Pending)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: finish now",
                    action = () =>
                    {
                        if (MissingInputs().Count == 0)
                        {
                            Complete(null);
                        }
                        else
                        {
                            Messages.Message("missing inputs", MessageTypeDefOf.RejectInput, false);
                        }
                    }
                };
            }
        }
    }

    public override string GetInspectString()
    {
        var sb = new StringBuilder(base.GetInspectString());
        void Line(string s)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.Append(s);
        }

        if (task == null)
        {
            Line("APG.NoTask".Translate());
        }
        else
        {
            Line("APG.CurrentTask".Translate(Operations.Describe(task)));
            if (task.state == TaskState.Pending)
            {
                Line("APG.Progress".Translate((task.progress / task.op.workAmount).ToStringPercent()));
                var reason = BlockReason();
                if (reason != null)
                {
                    Line(reason);
                }
            }
            else
            {
                Line("APG.AwaitingChoice".Translate());
            }
        }

        if (Ext.analyzerFacility != null)
        {
            Line(HasAnalyzer ? "APG.AnalyzerLinked".Translate() : "APG.AnalyzerNotLinked".Translate(Ext.analyzerFacility.label));
        }

        if (Ext.computeFacility != null && HasCompute)
        {
            Line("APG.ComputeLinked".Translate(Ext.computeFacility.label));
        }

        if (Ext.libraryCapacity > 0)
        {
            Line("APG.LibraryUse".Translate(PlantGeneticsRegistry.Instance.ArchivedCount, Operations.LibraryCapacity(Map)));
        }

        return sb.ToString();
    }
}

// ---------------------------------------------------------------------- jobs

public abstract class WorkGiver_GeneticsStation : WorkGiver_Scanner
{
    protected abstract bool LabWork { get; }

    public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
    {
        return pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_GeneticsStation>()
            .Where(s => s.task != null && s.Ext.labWork == LabWork);
    }

    public override bool ShouldSkip(Pawn pawn, bool forced = false)
    {
        return !pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_GeneticsStation>().Any(s => s.task != null);
    }

    public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        return JobOnThing(pawn, t, forced) != null;
    }

    public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        if (t is not Building_GeneticsStation st || st.task == null || st.task.state != TaskState.Pending || t.IsForbidden(pawn) || t.IsBurning())
        {
            return null;
        }

        if (!st.task.op.Unlocked || !st.Powered)
        {
            return null;
        }

        var missing = st.MissingInputs();
        if (missing.Count > 0)
        {
            foreach (var sig in missing.Distinct())
            {
                var seed = SeedIndex.Stacks(pawn.Map, sig)
                    .Where(s => s.ParentHolder is not Building_GeneticsStation && !s.IsForbidden(pawn) && pawn.CanReserveAndReach(s, PathEndMode.ClosestTouch, Danger.Deadly, 1, 1, null, forced))
                    .OrderBy(s => s.Position.DistanceToSquared(pawn.Position))
                    .FirstOrDefault();
                if (seed != null && pawn.CanReserve(st, 1, -1, null, forced))
                {
                    var haul = JobMaker.MakeJob(JobDefOf.HaulToContainer, seed, st);
                    haul.count = 1;
                    haul.haulMode = HaulMode.ToContainer;
                    return haul;
                }
            }

            JobFailReason.Is("APG.NoInputSeeds".Translate());
            return null;
        }

        if (st.Ext.autonomous || !st.ReadyToWork)
        {
            return null;
        }

        var skill = st.task.op.skill;
        if (skill != null && pawn.skills != null && pawn.skills.GetSkill(skill).Level < st.task.op.minSkill)
        {
            JobFailReason.Is("UnderRequiredSkill".Translate(st.task.op.minSkill));
            return null;
        }

        if (!pawn.CanReserve(st, 1, -1, null, forced))
        {
            return null;
        }

        return JobMaker.MakeJob(APG_DefOf.APG_OperateStation, st);
    }
}

public class WorkGiver_GeneticsStationPlants : WorkGiver_GeneticsStation
{
    protected override bool LabWork => false;
}

public class WorkGiver_GeneticsStationLab : WorkGiver_GeneticsStation
{
    protected override bool LabWork => true;
}

public class JobDriver_OperateStation : JobDriver
{
    private Building_GeneticsStation Station => (Building_GeneticsStation)job.targetA.Thing;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        this.FailOn(() => pawn.IsHashIntervalTick(120) && !Station.ReadyToWork);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
        var work = ToilMaker.MakeToil("OperateStation");
        work.tickIntervalAction = delta =>
        {
            var st = Station;
            var skill = st.task?.op.skill;
            if (skill != null)
            {
                pawn.skills?.Learn(skill, 0.1f * delta);
            }

            var speed = pawn.GetStatValue(skill == SkillDefOf.Plants ? StatDefOf.PlantWorkSpeed : StatDefOf.ResearchSpeed);
            st.DoWork(speed * delta, pawn);
            if (st.task == null || st.task.state != TaskState.Pending || st.task.progress <= 0f)
            {
                ReadyForNextToil();
            }
        };
        work.defaultCompleteMode = ToilCompleteMode.Never;
        work.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
        work.WithEffect(EffecterDefOf.Research, TargetIndex.A);
        work.WithProgressBar(TargetIndex.A, () => Station.task == null ? 1f : Station.task.progress / Station.task.op.workAmount);
        work.activeSkill = () => Station.task?.op.skill;
        yield return work;
    }
}
