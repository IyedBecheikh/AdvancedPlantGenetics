using System.Collections.Generic;
using System.Linq;
using AdvancedPlantGenetics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace APGTest;

public partial class APGTestComponent
{
    private static Zone_Growing sowZone;
    private static CultivarRecord sowRec;
    private static Plant collectPlant;
    private static Genome collectA;
    private static Genome collectB;
    private static Zone_Growing harvestZone;
    private static List<Plant> harvestPlants = [];
    private static Genome purebred;
    private static Building_GeneticsStation bench;
    private static Genome crossA;
    private static Genome crossB;
    private static Building_GeneticsStation synth;
    private static CultivarRecord synthRec;
    public static Building_GeneticsStation uiBench;

    private static readonly AccessTools.FieldRef<ThingDef> WantedPlantDef = AccessTools.StaticFieldRefAccess<ThingDef>(AccessTools.Field(typeof(WorkGiver_Grower), "wantedPlantDef"));

    private static Zone_Growing MakeZone(IEnumerable<IntVec3> cells, ThingDef plant)
    {
        var zone = new Zone_Growing(map.zoneManager);
        map.zoneManager.RegisterZone(zone);
        foreach (var c in cells)
        {
            foreach (var t in c.GetThingList(map).ToList())
            {
                if (t is Plant)
                {
                    t.Destroy();
                }
            }

            zone.AddCell(c);
        }

        zone.SetPlantDefToGrow(plant);
        return zone;
    }

    private static bool Idle(JobDef busy)
    {
        return worker.CurJobDef != busy && (worker.jobs.curDriver == null || worker.CurJobDef == JobDefOf.Wait_Wander || worker.CurJobDef == JobDefOf.GotoWander || worker.CurJobDef == JobDefOf.Wait || worker.CurJobDef == JobDefOf.Wait_MaintainPosture || worker.jobs.curJob.playerForced == false);
    }

    private static Job SowJob(IntVec3 c)
    {
        WantedPlantDef() = null;
        var giver = (WorkGiver_GrowerSow)DefDatabase<WorkGiverDef>.GetNamed("GrowerSow").Worker;
        return giver.JobOnCell(worker, c, true);
    }

    private static void ScenarioTick()
    {
        KeepWorking(worker);
        var rice = ThingDef.Named("Plant_Rice");
        switch (stage)
        {
            // -------------------------------------------------------------- sowing a strain uses its seed
            case 1:
            {
                var cells = new[] { Cell(0, 0), Cell(1, 0), Cell(0, 1), Cell(1, 1) };
                sowZone = MakeZone(cells, rice);
                var g = MakeGenome(rice, (LocusEffect.Growth, 3), (LocusEffect.ColdTolerance, 2));
                SpawnSeeds(g, 3, Cell(3, 0));
                sowRec = PlantGeneticsRegistry.Instance.Find(g);
                MapComponent_SeedStock.For(map).Get(sowZone, true).cultivarId = sowRec.id;
                Next();
                break;
            }
            case 2:
            {
                var sown = sowZone.Cells.Count(c => c.GetPlant(map)?.def == rice && c.GetPlant(map).LifeStage != PlantLifeStage.Sowing);
                if (worker.CurJobDef != JobDefOf.Sow)
                {
                    var target = sowZone.Cells.FirstOrDefault(c => c.GetPlant(map)?.def != rice);
                    var job = target.IsValid ? SowJob(target) : null;
                    if (job != null)
                    {
                        worker.jobs.StartJob(job, JobCondition.InterruptForced);
                    }
                    else if (sown >= 3 || Elapsed > 6000)
                    {
                        var plants = sowZone.Cells.Select(c => c.GetPlant(map)).Where(p => p?.def == rice).ToList();
                        var matching = plants.Count(p => CompPlantGenetics.Of(p).HasExplicitGenome && CompPlantGenetics.GenomeOf(p).Signature == sowRec.Signature);
                        Check(matching == 3, $"sowing a strain gives plants its exact genome ({matching}/3)");
                        Check(SeedCount(sowRec.Signature) == 0, $"each sown plant used one seed packet (left {SeedCount(sowRec.Signature)})");
                        var last = sowZone.Cells.FirstOrDefault(c => c.GetPlant(map)?.def != rice);
                        Check(last.IsValid && SowJob(last) == null, "no seed left: the last cell isn't sown");
                        Next();
                    }
                }

                if (Elapsed > 8000)
                {
                    Check(false, $"sowing timed out ({sown} sown)");
                    Next();
                }

                break;
            }

            // -------------------------------------------------------------- collect seeds from a superior plant
            case 3:
            {
                collectA = MakeGenome(rice, (LocusEffect.Growth, 2));
                collectB = MakeGenome(rice, (LocusEffect.Yield, 2));
                collectPlant = SpawnPlant(rice, Cell(6, 0), collectA);
                SpawnPlant(rice, Cell(7, 0), collectB);
                PlantGeneticsRegistry.Instance.Register(collectA, "APG.Origin_Debug");
                map.designationManager.AddDesignation(new Designation(collectPlant, APG_DefOf.APG_CollectSeeds));
                Next();
                break;
            }
            case 4:
            {
                if (collectPlant.Destroyed)
                {
                    var parent = PlantGeneticsRegistry.Instance.Find(collectA);
                    var child = PlantGeneticsRegistry.Instance.Records.LastOrDefault(r => r.parentA == parent.id && r.origin == "APG.Origin_Collected");
                    Check(child != null && SeedCount(child.Signature) >= 2, $"collected seeds are a strain with the plant as parent ({(child == null ? "none" : SeedCount(child.Signature) + " seeds")})");
                    if (child != null)
                    {
                        var gi = GeneticsDB.IndexOf(LocusEffect.Growth);
                        var yi = GeneticsDB.IndexOf(LocusEffect.Yield);
                        // Wild-type alleles are dominant like ours, so each locus averages to +1 (barring a rare mutation).
                        Check(Mathf.Abs(child.template.Expressed(gi) - 1f) < 0.6f && Mathf.Abs(child.template.Expressed(yi) - 1f) < 0.6f,
                            $"seed head is a cross with the neighbour (G {child.template.Expressed(gi)}, Y {child.template.Expressed(yi)})");
                    }

                    Next();
                }
                else if (worker.CurJobDef != APG_DefOf.APG_CollectSeedsJob)
                {
                    var giver = DefDatabase<WorkGiverDef>.GetNamed("APG_CollectSeeds").Worker as WorkGiver_Scanner;
                    if (giver.HasJobOnThing(worker, collectPlant, true))
                    {
                        worker.jobs.StartJob(giver.JobOnThing(worker, collectPlant, true), JobCondition.InterruptForced);
                    }
                }

                if (Elapsed > 6000)
                {
                    Check(false, "collect seeds timed out");
                    Next();
                }

                break;
            }

            // -------------------------------------------------------------- harvest seed saving keeps purebred stock
            case 5:
            {
                purebred = MakeGenome(rice, (LocusEffect.Yield, 3), (LocusEffect.Vigor, 1));
                PlantGeneticsRegistry.Instance.Register(purebred, "APG.Origin_Debug");
                var cells = new[] { Cell(10, 0), Cell(11, 0), Cell(10, 1), Cell(11, 1), Cell(12, 0), Cell(12, 1) };
                harvestZone = MakeZone(cells, rice);
                var a = MapComponent_SeedStock.For(map).Get(harvestZone, true);
                a.cultivarId = PlantGeneticsRegistry.Instance.Find(purebred).id;
                a.saveSeeds = true;
                harvestZone.allowSow = false;
                harvestPlants = cells.Select(c => SpawnPlant(rice, c, purebred.Copy())).ToList();
                Next();
                break;
            }
            case 6:
            {
                var left = harvestPlants.Where(p => !p.Destroyed).ToList();
                if (left.Count == 0)
                {
                    var a = MapComponent_SeedStock.For(map).Get(harvestZone);
                    Check(a.seedsSaved >= 3 && SeedCount(purebred.Signature) >= 3, $"saving seed from a purebred field returns seed ({a.seedsSaved} saved, {a.offTypesCulled} off-types)");
                    Next();
                }
                else if (worker.CurJobDef != JobDefOf.Harvest)
                {
                    var job = JobMaker.MakeJob(JobDefOf.Harvest, left[0]);
                    for (var i = 1; i < left.Count; i++)
                    {
                        job.AddQueuedTarget(TargetIndex.A, left[i]);
                    }

                    worker.jobs.StartJob(job, JobCondition.InterruptForced);
                }

                if (Elapsed > 8000)
                {
                    Check(false, "harvest timed out");
                    Next();
                }

                break;
            }

            // -------------------------------------------------------------- crossing bench with real hauling and work
            case 7:
            {
                bench = SpawnStation("APG_CrossingBench", Cell(-8, 2));
                crossA = MakeGenome(rice, (LocusEffect.Growth, 4), (LocusEffect.HeatTolerance, 2));
                crossB = MakeGenome(rice, (LocusEffect.Yield, 4), (LocusEffect.ColdTolerance, 2));
                SpawnSeeds(crossA, 2, Cell(-10, 4));
                SpawnSeeds(crossB, 2, Cell(-11, 4));
                bench.SetTask(new GeneticTask { op = DefDatabase<GeneticOperationDef>.GetNamed("APG_Op_Cross"), inputs = [crossA.Signature, crossB.Signature] });
                Next();
                break;
            }
            case 8:
            {
                if (bench.task?.state == TaskState.AwaitingChoice)
                {
                    var cands = bench.task.candidates;
                    Check(cands.Count == 4, $"crossing gives 3 candidates +1 for a skilled botanist ({cands.Count})");
                    Check(cands.All(c => c.generation == 1), "offspring are one generation on");
                    Check(SeedCount(crossA.Signature) == 1 && SeedCount(crossB.Signature) == 1, $"each parent used one packet ({SeedCount(crossA.Signature)}, {SeedCount(crossB.Signature)})");
                    var chosen = cands[0];
                    bench.Choose(chosen);
                    var rec = PlantGeneticsRegistry.Instance.Find(chosen);
                    Check(SeedCount(chosen.Signature) == 4 && rec?.parentA >= 0 && rec.parentB >= 0, $"keeping a candidate gives 4 packets with both parents recorded ({SeedCount(chosen.Signature)})");
                    Next();
                }
                else if (worker.CurJobDef != JobDefOf.HaulToContainer && worker.CurJobDef != APG_DefOf.APG_OperateStation)
                {
                    var giver = (WorkGiver_Scanner)DefDatabase<WorkGiverDef>.GetNamed("APG_GeneticsStationPlants").Worker;
                    var job = giver.JobOnThing(worker, bench, true);
                    if (job != null)
                    {
                        worker.jobs.StartJob(job, JobCondition.InterruptForced);
                    }
                }

                if (Elapsed > 10000)
                {
                    var giverDiag = (WorkGiver_Scanner)DefDatabase<WorkGiverDef>.GetNamed("APG_GeneticsStationPlants").Worker;
                    JobFailReason.Clear();
                    var diagJob = giverDiag.JobOnThing(worker, bench, true);
                    Check(false, $"crossing timed out (state {bench.task?.state}, reason {bench.BlockReason()}, missing {bench.MissingInputs().Count}; " +
                                 $"worker job {worker.CurJobDef?.defName}, giver job {diagJob?.def.defName ?? "none"} ({JobFailReason.Reason}); " +
                                 $"A seeds {SeedCount(crossA.Signature)} at {string.Join(",", SeedsOf(crossA.Signature).Select(t => t.Position + (t.IsForbidden(worker) ? "F" : "")))}, " +
                                 $"B seeds {SeedCount(crossB.Signature)} at {string.Join(",", SeedsOf(crossB.Signature).Select(t => t.Position + (t.IsForbidden(worker) ? "F" : "")))}, " +
                                 $"container {bench.Container.Count}, worker carrying {worker.carryTracker.CarriedThing?.LabelShort})");
                    Next();
                }

                break;
            }

            // -------------------------------------------------------------- every lab operation, run directly
            case 9:
                LabOperations();
                Next();
                break;
            case 10:
                Feed(synth);
                foreach (var b in map.listerBuildings.AllBuildingsColonistOfClass<Building_GeneticsStation>())
                {
                    Feed(b);
                }

                if (SeedCount(synthRec.Signature) >= 20)
                {
                    Check(true, $"seed synthesizer prints archived seed by itself up to the stock target ({SeedCount(synthRec.Signature)})");
                    Check(synth.BlockReason() != null, "synthesizer stops at the stock target");
                    Next();
                }
                else if (Elapsed > 40000)
                {
                    Check(false, $"seed synthesizer timed out ({SeedCount(synthRec.Signature)} seeds, reason {synth.BlockReason()})");
                    Next();
                }

                break;
            case 11:
                PerfBench.Run(map);
                PrepareUi();
                Next();
                break;
            default:
                SaveAndReload();
                break;
        }
    }

    private static Thing Seeds(Genome g, int n, IntVec3 c)
    {
        return SpawnSeeds(g, n, c);
    }

    private static void LabOperations()
    {
        var rice = ThingDef.Named("Plant_Rice");
        var corn = ThingDef.Named("Plant_Corn");
        var reg = PlantGeneticsRegistry.Instance;
        GeneticOperationDef Op(string n) => DefDatabase<GeneticOperationDef>.GetNamed(n);

        // Analyzer: reveals genotype, fills the library.
        var analyzer = SpawnStation("APG_GeneAnalyzer", Cell(-12, -4));
        var hidden = new Genome(rice);
        var gi = GeneticsDB.IndexOf(LocusEffect.Growth);
        hidden.a[gi] = new Allele(0, true);
        hidden.b[gi] = new Allele(5, false);
        hidden.Invalidate();
        var hs = Seeds(hidden, 2, Cell(-12, -2));
        Check(!Knowledge.SeesGenotype(hidden), "genotype hidden before analysis");
        RunDirect(analyzer, new GeneticTask { op = Op("APG_Op_Analyze"), inputs = [hidden.Signature] }, hs);
        Check(reg.IsAnalyzed(hidden) && Knowledge.SeesGenotype(hidden), "analysis reveals the genotype");
        Check(reg.LibraryMax(GeneticsDB.Locus(LocusEffect.Growth)) == 5, $"analysis adds the hidden +5 allele to the library ({reg.LibraryMax(GeneticsDB.Locus(LocusEffect.Growth))})");
        Check(SeedCount(hidden.Signature) == 2, "analysis returns the packet");

        // Gene bank: archive.
        var bank = SpawnStation("APG_GeneBank", Cell(-10, -4));
        RunDirect(bank, new GeneticTask { op = Op("APG_Op_Archive"), inputs = [hidden.Signature] }, SeedsOf(hidden.Signature).First());
        Check(reg.Find(hidden).archived, "gene bank archives an analysed strain");

        // Tissue culture: 1 -> 12.
        var lab = SpawnStation("APG_TissueCultureLab", Cell(-12, -8));
        var tc = MakeGenome(rice, (LocusEffect.Yield, 2));
        var tcs = Seeds(tc, 1, Cell(-9, -8));
        RunDirect(lab, new GeneticTask { op = Op("APG_Op_Micropropagate"), inputs = [tc.Signature] }, tcs);
        Check(SeedCount(tc.Signature) == 12, $"micropropagation turns 1 packet into 12 ({SeedCount(tc.Signature)})");

        // Mutagen: candidates differ from the parent and stay under +6.
        var mut = SpawnStation("APG_MutagenChamber", Cell(-8, -8));
        var mg = MakeGenome(rice, (LocusEffect.Growth, 5));
        var ms = Seeds(mg, 1, Cell(-6, -8));
        RunDirect(mut, new GeneticTask { op = Op("APG_Op_Mutate"), inputs = [mg.Signature] }, ms);
        var cands = mut.task?.candidates ?? [];
        var maxLevel = cands.Count == 0 ? 99 : cands.Max(c => Enumerable.Range(0, c.a.Length).Max(i => Mathf.Max(c.a[i].level, c.b[i].level)));
        var wantCands = 4 + (mut.HasAnalyzer ? 1 : 0);
        Check(cands.Count == wantCands && cands.All(c => c.Signature != mg.Signature) && maxLevel <= 6, $"mutagen gives {wantCands} distinct mutants capped at +6 (analyzer linked: {mut.HasAnalyzer}; got {cands.Count}, max {maxLevel})");
        mut.Choose(cands.FirstOrDefault());

        // Colchicine: polyploid.
        var cg = MakeGenome(rice, (LocusEffect.Growth, 6), (LocusEffect.Yield, 6));
        var cs = Seeds(cg, 1, Cell(-6, -9));
        RunDirect(mut, new GeneticTask { op = Op("APG_Op_Colchicine"), inputs = [cg.Signature] }, cs);
        var poly = reg.Records.LastOrDefault(r => r.parentA == reg.Find(cg).id);
        Check(poly != null && poly.template.Copies(APG_DefOf.APG_Polyploid) == 2 && poly.template.Capacity == cg.Capacity + 6f && SeedCount(poly.Signature) == 3,
            "colchicine treatment makes a polyploid strain (+6 capacity, 3 packets)");

        // Gene splicer: edit limited by the library (+5 known from the analysed strain).
        var splicer = SpawnStation("APG_GeneSplicer", Cell(-4, -8));
        SpawnBuilding("APG_BioinformaticsCore", Cell(-1, -8));
        var eg = new Genome(corn);
        var es = Seeds(eg, 2, Cell(-4, -10));
        var tooHigh = new GeneticTask { op = Op("APG_Op_EditLocus"), inputs = [eg.Signature], locus = GeneticsDB.Locus(LocusEffect.Growth), level = 7, dominant = true };
        splicer.SetTask(tooHigh);
        splicer.Container.TryAdd(es.SplitOff(1));
        Check(splicer.BlockReason() != null, $"splicer refuses levels beyond the allele library ({splicer.BlockReason()})");
        splicer.CancelTask();
        RunDirect(splicer, new GeneticTask { op = Op("APG_Op_EditLocus"), inputs = [eg.Signature], locus = GeneticsDB.Locus(LocusEffect.Growth), level = 5, dominant = true },
            SeedsOf(eg.Signature).First());
        var edited = reg.Records.LastOrDefault(r => r.Species == corn && r.template.Expressed(gi) == 5f);
        Check(edited != null && edited.template.HomozygousAt(gi) && edited.analyzed, "splicer writes a library allele across species (rice +5 into corn), homozygous and known");
        Check(splicer.HasCompute, "bioinformatics core links to the splicer");

        // Splice a synthetic gene.
        var circ = DefDatabase<PlantGeneDef>.GetNamed("APG_CircadianOverride");
        RunDirect(splicer, new GeneticTask { op = Op("APG_Op_Splice"), inputs = [eg.Signature], gene = circ }, SeedsOf(eg.Signature).First());
        Check(reg.Records.Any(r => r.Species == corn && r.template.Expresses(circ)), "splicer inserts a synthetic gene");

        // Genesis: +10 without library.
        var genesis = SpawnStation("APG_GenesisSynthesizer", Cell(4, -9));
        var gg = MakeGenome(rice, (LocusEffect.Growth, 2));
        var gs = Seeds(gg, 1, Cell(7, -9));
        RunDirect(genesis, new GeneticTask { op = Op("APG_Op_GenesisEdit"), inputs = [gg.Signature], locus = GeneticsDB.Locus(LocusEffect.Yield), level = 10, dominant = true }, gs);
        var yi = GeneticsDB.IndexOf(LocusEffect.Yield);
        Check(reg.Records.Any(r => r.template.Expressed(yi) == 10f), "genesis synthesizer writes +10 alleles");

        // Seed synthesizer: autonomous, keeps 20 in stock.
        synth = SpawnStation("APG_SeedSynthesizer", Cell(8, -5));
        synthRec = reg.Find(hidden);
        synth.SetTask(new GeneticTask { op = Op("APG_Op_Synthesize"), cultivarId = synthRec.id, keepCount = 20 });
        Check(synth.BlockReason() == null, $"seed synthesizer ready ({synth.BlockReason() ?? "ok"})");

        // A finished crossing for the UI screenshots.
        uiBench = SpawnStation("APG_CrossingBench", Cell(4, 3));
        uiBench.GetComp<CompAffectedByFacilities>();
        var ua = MakeGenome(rice, (LocusEffect.Growth, 4), (LocusEffect.LightEfficiency, 3));
        var ub = MakeGenome(rice, (LocusEffect.Yield, 4), (LocusEffect.BlightResistance, 3));
        var uas = Seeds(ua, 1, Cell(2, 6));
        var ubs = Seeds(ub, 1, Cell(3, 6));
        RunDirect(uiBench, new GeneticTask { op = Op("APG_Op_Cross"), inputs = [ua.Signature, ub.Signature] }, uas, ubs);
        Check(uiBench.task?.state == TaskState.AwaitingChoice, "direct cross awaits a choice");
    }
}
