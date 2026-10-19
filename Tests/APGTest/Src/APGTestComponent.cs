// In-game integration harness for Advanced Plant Genetics. Runs in an isolated save-data folder (see run-ingame-tests.sh).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AdvancedPlantGenetics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace APGTest;

/// <summary>Test-only overrides: force growing season and daytime so growth comparisons are deterministic.</summary>
[StaticConstructorOnStartup]
public static class TestOverrides
{
    public static bool ForceSeason;
    public static bool ForceAwake;
    public static bool ForceLight;

    static TestOverrides()
    {
        var h = new Harmony("apgtest.overrides");
        h.Patch(AccessTools.Method(typeof(PlantUtility), nameof(PlantUtility.GrowthSeasonNow), [typeof(IntVec3), typeof(Map), typeof(ThingDef)]),
            postfix: new HarmonyMethod(typeof(TestOverrides), nameof(Season)));
        // Run before the mod's own Resting postfix doesn't matter: both only turn "resting" off.
        h.Patch(AccessTools.PropertyGetter(typeof(Plant), "Resting"), postfix: new HarmonyMethod(typeof(TestOverrides), nameof(Awake)));
        h.Patch(AccessTools.Method(typeof(PlantUtility), nameof(PlantUtility.GrowthRateFactorFor_Light)), postfix: new HarmonyMethod(typeof(TestOverrides), nameof(Light)));
    }

    public static void Light(ref float __result)
    {
        if (ForceLight)
        {
            __result = 1f;
        }
    }

    public static void Season(ref bool __result)
    {
        if (ForceSeason)
        {
            __result = true;
        }
    }

    public static void Awake(ref bool __result)
    {
        if (ForceAwake)
        {
            __result = false;
        }
    }
}

public partial class APGTestComponent(Game game) : GameComponent
{
    private static string Dir => GenFilePaths.SaveDataFolderPath;
    private static string Marker => Path.Combine(Dir, "apgtest_marker.txt");
    private static string Results => Path.Combine(Dir, "apgtest_results.txt");
    public static string Shots => Path.Combine(Dir, "shots");

    private static int fails;
    private static bool finished;
    private static int stage;
    private static int stageStart;
    private static int shotStage;
    private static int shotFrame;
    private bool started;
    private bool verifyPending;

    // ------------------------------------------------------------------ plumbing

    public static void Check(bool ok, string what)
    {
        if (!ok)
        {
            fails++;
        }

        var line = $"[APGTEST] {(ok ? "PASS" : "FAIL")} {what}";
        Log.Message(line);
        File.AppendAllText(Results, line + "\n");
    }

    public static void Info(string what)
    {
        var line = "[APGTEST] INFO " + what;
        Log.Message(line);
        File.AppendAllText(Results, line + "\n");
    }

    private static void Finish()
    {
        finished = true;
        Check(ErrorWatch.Captured.Count == 0, $"no errors from the mod during the run ({ErrorWatch.Captured.Count})");
        foreach (var e in ErrorWatch.Captured.Distinct().Take(15))
        {
            File.AppendAllText(Results, "[APGTEST]   logged: " + e.Replace("\n", " | ") + "\n");
        }

        ScanGameLog();
        File.AppendAllText(Results, $"[APGTEST] DONE fails={fails}\n");
        if (File.Exists(Marker))
        {
            File.Delete(Marker);
        }

        Application.Quit();
    }

    /// <summary>Errors logged before the harness loaded (startup, XML patches, def errors) only reach the game log.</summary>
    private static void ScanGameLog()
    {
        try
        {
            var path = Application.consoleLogPath;
            if (path.NullOrEmpty() || !File.Exists(path))
            {
                Info("game log not found: " + path);
                return;
            }

            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                text = reader.ReadToEnd();
            }

            var bad = text.Split('\n')
                .Where(l => (l.Contains("Exception") || l.Contains("rror") || l.Contains("failed") || l.Contains("arning"))
                            && !l.Contains("[APGTEST]") && !l.Contains("needs to have <downloadUrl>") && !l.Contains("Fallback handler")
                            && !l.TrimStart().StartsWith("at "))
                .Select(l => l.Trim()).Distinct().ToList();
            Check(bad.Count == 0, $"game log has no errors or warnings ({bad.Count})");
            foreach (var l in bad.Take(10))
            {
                File.AppendAllText(Results, "[APGTEST]   log: " + l + "\n");
            }
        }
        catch (Exception e)
        {
            Info("log scan failed: " + e.Message);
        }
    }

    private static void Next()
    {
        stage++;
        stageStart = Find.TickManager.TicksGame;
    }

    private static int Elapsed => Find.TickManager.TicksGame - stageStart;

    public static void KeepWorking(Pawn worker)
    {
        foreach (var need in worker.needs?.AllNeeds ?? [])
        {
            need.CurLevelPercentage = 1f;
        }

        if (worker.InMentalState)
        {
            worker.MentalState.RecoverFromState();
        }
    }

    public override void LoadedGame()
    {
        if (File.Exists(Marker))
        {
            verifyPending = true;
        }
    }

    public override void GameComponentTick()
    {
        if (finished)
        {
            return;
        }

        try
        {
            if (verifyPending)
            {
                verifyPending = false;
                VerifyAfterReload();
                shotStage = 1;
                shotFrame = Time.frameCount + 60;
                return;
            }

            if (!started)
            {
                if (File.Exists(Marker) || Find.TickManager.TicksGame < 120)
                {
                    return;
                }

                started = true;
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Results, "");
                Setup();
                SynchronousChecks();
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                Next();
                return;
            }

            if (stage > 0)
            {
                ScenarioTick();
            }
        }
        catch (Exception e)
        {
            Check(false, "exception: " + e);
            Finish();
        }
    }

    // ------------------------------------------------------------------ fixtures

    private static Map map;
    private static Pawn worker;
    private static IntVec3 origin;
    private static readonly List<IntVec3> usedCells = [];

    private static void Setup()
    {
        map = Find.CurrentMap;
        TestOverrides.ForceSeason = true;
        TestOverrides.ForceAwake = true;
        TestOverrides.ForceLight = true;
        // Research everything this mod has, plus vanilla prerequisites.
        foreach (var proj in DefDatabase<ResearchProjectDef>.AllDefs.Where(p => p.defName.StartsWith("APG_")).ToList())
        {
            foreach (var pre in proj.prerequisites ?? [])
            {
                Find.ResearchManager.FinishProject(pre, false, null, false);
            }

            Find.ResearchManager.FinishProject(proj, false, null, false);
        }

        worker = map.mapPawns.FreeColonistsSpawned.Where(p => !p.WorkTypeIsDisabled(WorkTypeDefOf.Growing) && !p.WorkTypeIsDisabled(WorkTypeDefOf.Research))
                     .FirstOrDefault()
                 ?? map.mapPawns.FreeColonistsSpawned.First();
        worker.skills.GetSkill(SkillDefOf.Plants).Level = 20;
        worker.skills.GetSkill(SkillDefOf.Intellectual).Level = 20;
        foreach (var other in map.mapPawns.FreeColonistsSpawned.Where(p => p != worker).ToList())
        {
            // Keep other colonists out of the way: they'd take our jobs.
            other.workSettings?.DisableAll();
        }

        origin = FindOpenArea(24, 16);
        ClearArea(CellRect.CenteredOn(origin, 14, 10));
        Info($"worker {worker.LabelShort}, test area at {origin}, season temp {map.mapTemperature.OutdoorTemp:0}");
    }

    private static IntVec3 FindOpenArea(int w, int h)
    {
        var center = worker.Position;
        foreach (var c in GenRadial.RadialCellsAround(center, 60f, true))
        {
            var rect = CellRect.CenteredOn(c, w / 2, h / 2);
            if (!rect.InBounds(map))
            {
                continue;
            }

            if (rect.Cells.All(x => x.Standable(map) && x.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Medium) && !x.Roofed(map) && x.GetFirstBuilding(map) == null && x.GetFirstPawn(map) == null))
            {
                return c;
            }
        }

        return center;
    }

    private static void ClearArea(CellRect rect)
    {
        foreach (var c in rect.ClipInsideMap(map).Cells)
        {
            foreach (var t in c.GetThingList(map).ToList())
            {
                if (t is not Pawn && t.def.destroyable)
                {
                    t.Destroy();
                }
            }

            map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
            map.snowGrid.SetDepth(c, 0f);
        }
    }

    public static IntVec3 Cell(int dx, int dz)
    {
        var c = origin + new IntVec3(dx, 0, dz);
        usedCells.Add(c);
        return c;
    }

    private static ThingDef Rice => ThingDef.Named("Plant_Rice");

    public static Genome MakeGenome(ThingDef species, params (LocusEffect effect, int level)[] loci)
    {
        var g = new Genome(species);
        foreach (var (effect, level) in loci)
        {
            g.SetLocus(GeneticsDB.IndexOf(effect), level, true);
        }

        g.Invalidate();
        return g;
    }

    public static Plant SpawnPlant(ThingDef def, IntVec3 c, Genome g, float growth = 1f)
    {
        foreach (var t in c.GetThingList(map).ToList())
        {
            if (t is Plant)
            {
                t.Destroy();
            }
        }

        var p = (Plant)GenSpawn.Spawn(def, c, map);
        p.Growth = growth;
        p.sown = true;
        if (g != null)
        {
            CompPlantGenetics.Of(p).SetGenome(g);
        }

        return p;
    }

    public static Thing SpawnSeeds(Genome g, int count, IntVec3 c, string origin = "APG.Origin_Debug")
    {
        PlantGeneticsRegistry.Instance.Register(g, origin);
        var t = SeedIndex.MakeSeeds(g, count);
        GenSpawn.Spawn(t, c, map);
        SeedIndex.Dirty(map);
        return t;
    }

    public static Building_GeneticsStation SpawnStation(string defName, IntVec3 c)
    {
        var st = (Building_GeneticsStation)SpawnBuilding(defName, c);
        Feed(st);
        return st;
    }

    public static Building SpawnBuilding(string defName, IntVec3 c)
    {
        var def = ThingDef.Named(defName);
        foreach (var cell in GenAdj.OccupiedRect(c, Rot4.North, def.size).ExpandedBy(1))
        {
            foreach (var t in cell.GetThingList(map).ToList())
            {
                if (t is not Pawn && t.def.destroyable)
                {
                    t.Destroy();
                }
            }
        }

        var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.WoodLog : null);
        thing.SetFaction(Faction.OfPlayer);
        var b = (Building)GenSpawn.Spawn(thing, c, map);
        if (b.GetComp<CompPowerTrader>() is { } power)
        {
            power.PowerOn = true;
        }

        return b;
    }

    public static void Feed(Building_GeneticsStation st)
    {
        var power = st.GetComp<CompPowerTrader>();
        if (power != null)
        {
            power.PowerOn = true;
        }

        var fuel = st.GetComp<CompRefuelable>();
        if (fuel != null && fuel.Fuel < fuel.Props.fuelCapacity)
        {
            fuel.Refuel(fuel.Props.fuelCapacity - fuel.Fuel);
        }
    }

    /// <summary>Load seed packets straight into a station and complete its task without a pawn.</summary>
    public static void RunDirect(Building_GeneticsStation st, GeneticTask task, params Thing[] seeds)
    {
        st.SetTask(task);
        foreach (var s in seeds)
        {
            st.Container.TryAdd(s.SplitOff(1));
        }

        SeedIndex.Dirty(map);
        Feed(st);
        var reason = st.BlockReason();
        Check(reason == null, $"{task.op.defName}: ready to run ({reason ?? "ok"})");
        if (reason == null)
        {
            st.Complete(null);
        }
    }

    public static List<Thing> SeedsOf(string signature)
    {
        SeedIndex.Dirty(map);
        return SeedIndex.Stacks(map, signature).ToList();
    }

    public static int SeedCount(string signature)
    {
        SeedIndex.Dirty(map);
        return SeedIndex.Total(map, signature);
    }

    // ------------------------------------------------------------------ synchronous checks

    private static void SynchronousChecks()
    {
        Check(GeneticsDB.Loci.Count == 9, $"9 loci loaded ({GeneticsDB.Loci.Count})");
        Check(GeneticsDB.Genes.Count >= 14, $"genes loaded ({GeneticsDB.Genes.Count})");
        var crops = DefDatabase<ThingDef>.AllDefs.Where(d => d.plant != null && !d.plant.sowTags.NullOrEmpty() && d.category == ThingCategory.Plant).ToList();
        Check(crops.All(d => d.comps.Any(c => c is CompProperties_PlantGenetics)), $"all {crops.Count} sowable plants have the genetics comp");
        Check(crops.All(d => d.inspectorTabsResolved.Any(t => t is ITab_PlantGenetics)), "all sowable plants have the genetics tab");
        foreach (var name in new[] { "APG_CrossingBench", "APG_PropagationBench", "APG_GeneAnalyzer", "APG_GeneBank", "APG_TissueCultureLab", "APG_MutagenChamber", "APG_GeneSplicer", "APG_BioinformaticsCore", "APG_SeedSynthesizer", "APG_GenesisSynthesizer" })
        {
            Check(DefDatabase<ThingDef>.GetNamedSilentFail(name) != null, "building def " + name);
        }

        if (Compat.ProgressionAgriculture)
        {
            ProgressionAgricultureChecks();
        }

        GenomeTests.Run();
        PhenotypeTests.Run(map);
        SeedTests.Run(map);
    }

    private static void ProgressionAgricultureChecks()
    {
        var locked = GeneticsDB.SowableCrops.Where(Compat.IsCropLocked).ToList();
        Info($"Progression: Agriculture active, {locked.Count} crops locked");
        Check(locked.Count >= 2, $"Progression: Agriculture locks crops ({locked.Count})");
        if (locked.Count < 2)
        {
            return;
        }

        // A seed packet of a locked crop unlocks it and uses one packet.
        var crop = locked[0];
        var packet = SpawnSeeds(Genome.Heirloom(crop, 77), 3, Cell(-2, 6));
        var gizmo = ((ThingWithComps)packet).GetComp<CompSeedGenome>().CompGetGizmosExtra().OfType<Command_Action>().FirstOrDefault();
        Check(gizmo != null, $"locked crop's seed packet offers an unlock ({crop.label})");
        gizmo?.action();
        Check(!Compat.IsCropLocked(crop) && packet.stackCount == 2, $"seed packet unlocked {crop.label} and used one packet (left {packet.stackCount})");

        // Unlocking from a seed bundle also gives heirloom seed.
        var bundleCrop = locked[1];
        var bundleDef = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(d => d.comps.Any(c => c.GetType().Name == "CompProperties_UnlockCrop"
                                                                                             && Traverse.Create(c).Field("cropDef").GetValue<ThingDef>() == bundleCrop));
        if (bundleDef != null)
        {
            var bundle = (ThingWithComps)GenSpawn.Spawn(ThingMaker.MakeThing(bundleDef), Cell(2, 8), map);
            var before = map.listerThings.ThingsOfDef(APG_DefOf.APG_SeedPacket).Where(s => CompSeedGenome.GenomeOf(s)?.species == bundleCrop).Sum(s => s.stackCount);
            var comp = bundle.AllComps.First(c => c.GetType().Name == "CompUnlockCrop");
            AccessTools.Method(comp.GetType(), "Unlock").Invoke(comp, null);
            SeedIndex.Dirty(map);
            var after = map.listerThings.ThingsOfDef(APG_DefOf.APG_SeedPacket).Where(s => CompSeedGenome.GenomeOf(s)?.species == bundleCrop).Sum(s => s.stackCount);
            Check(!Compat.IsCropLocked(bundleCrop) && after - before == 8, $"seed bundle unlocks {bundleCrop.label} and gives 8 heirloom packets ({after - before})");
        }
        else
        {
            Check(false, "no seed bundle def found for " + bundleCrop.label);
        }

        // Everything else in the suite expects vanilla crop availability.
        AccessTools.Method(AccessTools.TypeByName("ProgressionAgriculture.GameComponent_UnlockedCrops"), "UnlockAllCrops").Invoke(null, null);
        Check(!Compat.IsCropLocked(ThingDef.Named("Plant_Rice")), "all crops unlocked for the rest of the suite");
    }

    // ------------------------------------------------------------------ save/reload

    private static void SaveAndReload()
    {
        stage = -1;
        var lines = new List<string>
        {
            PlantGeneticsRegistry.Instance.Records.Count.ToString(),
            string.Join("	", map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Plant>()
                .Where(p => CompPlantGenetics.Of(p)?.HasExplicitGenome == true).Take(30)
                .Select(p => p.thingIDNumber + "#" + CompPlantGenetics.GenomeOf(p).Signature)),
            string.Join(";", map.listerBuildings.AllBuildingsColonistOfClass<Building_GeneticsStation>().Where(s => s.task != null).Select(s => s.thingIDNumber + "=" + s.task.op.defName)),
            string.Join(";", MapComponent_SeedStock.For(map).All.Where(a => a.cultivarId >= 0).Select(a => a.zoneId + "=" + a.cultivarId)),
            PlantGeneticsRegistry.Instance.LibraryMax(GeneticsDB.Locus(LocusEffect.Growth)).ToString()
        };
        File.WriteAllLines(Marker, lines);
        GameDataSaveLoader.SaveGame("apgtest");
        Check(true, "saved");
        GameDataSaveLoader.LoadGame("apgtest");
    }

    private static void VerifyAfterReload()
    {
        map = Find.CurrentMap;
        var lines = File.ReadAllLines(Marker);
        var reg = PlantGeneticsRegistry.Instance;
        Check(reg != null && reg.Records.Count.ToString() == lines[0], $"reload: registry keeps {lines[0]} strains (got {reg?.Records.Count})");
        var plantsOk = 0;
        var plantsBad = 0;
        foreach (var kv in lines[1].Split(['	'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = kv.Split('#');
            var id = int.Parse(parts[0]);
            var plant = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).FirstOrDefault(p => p.thingIDNumber == id);
            if (plant == null)
            {
                continue; // harvested or died in the meantime
            }

            if (CompPlantGenetics.Of(plant)?.HasExplicitGenome == true && CompPlantGenetics.GenomeOf(plant).Signature == parts[1])
            {
                plantsOk++;
            }
            else
            {
                plantsBad++;
            }
        }

        Check(plantsOk > 0 && plantsBad == 0, $"reload: engineered plants keep their exact genome ({plantsOk} ok, {plantsBad} wrong)");
        foreach (var kv in lines[2].Split([';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = kv.Split('=');
            var st = map.listerBuildings.AllBuildingsColonistOfClass<Building_GeneticsStation>().FirstOrDefault(s => s.thingIDNumber.ToString() == parts[0]);
            Check(st?.task?.op?.defName == parts[1], $"reload: station {parts[0]} keeps task {parts[1]} (got {st?.task?.op?.defName})");
        }

        var stock = MapComponent_SeedStock.For(map);
        foreach (var kv in lines[3].Split([';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = kv.Split('=');
            var a = stock.All.FirstOrDefault(x => x.zoneId.ToString() == parts[0]);
            Check(a?.cultivarId.ToString() == parts[1], $"reload: zone {parts[0]} keeps seed stock {parts[1]} (got {a?.cultivarId})");
        }

        Check(reg.LibraryMax(GeneticsDB.Locus(LocusEffect.Growth)).ToString() == lines[4], $"reload: allele library kept (growth max {lines[4]})");
        var seeds = map.listerThings.ThingsOfDef(APG_DefOf.APG_SeedPacket);
        Check(seeds.Count > 0 && seeds.All(s => CompSeedGenome.GenomeOf(s)?.species != null), $"reload: all {seeds.Count} seed stacks keep their genome");
    }
}
