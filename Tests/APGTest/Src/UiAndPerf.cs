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

namespace APGTest;

public static class PerfBench
{
    private static double Measure(List<Plant> plants, int rounds)
    {
        var sw = Stopwatch.StartNew();
        var sink = 0f;
        for (var r = 0; r < rounds; r++)
        {
            foreach (var p in plants)
            {
                sink += p.GrowthRate;
            }
        }

        sw.Stop();
        return sw.Elapsed.TotalMilliseconds * 1000.0 / Math.Max(1, rounds * plants.Count) + sink * 0f;
    }

    public static void Run(Map map)
    {
        var all = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Plant>().ToList();
        var wild = all.Where(p => !p.sown && GeneticsDB.IsGeneticPlant(p.def)).Take(2000).ToList();
        var engineered = all.Where(p => CompPlantGenetics.Of(p)?.HasExplicitGenome == true).ToList();
        Measure(wild, 3);
        GeneticsDB.Enabled = false;
        var baseWild = Measure(wild, 20);
        var baseEng = Measure(engineered, 200);
        GeneticsDB.Enabled = true;
        var modWild = Measure(wild, 20);
        var modEng = Measure(engineered, 200);
        APGTestComponent.Info($"perf GrowthRate: wild {baseWild:0.000} -> {modWild:0.000} us/call ({wild.Count} plants), engineered {baseEng:0.000} -> {modEng:0.000} us/call ({engineered.Count} plants)");
        APGTestComponent.Check(modWild - baseWild < 0.5, $"wild plants cost almost nothing extra (+{modWild - baseWild:0.000} us per GrowthRate)");
        APGTestComponent.Check(modEng < 25, $"engineered plants stay cheap ({modEng:0.000} us per GrowthRate)");

        var cells = map.AllCells.Where((_, i) => i % 37 == 0).Take(3000).ToList();
        var rice = ThingDef.Named("Plant_Rice");
        var sw = Stopwatch.StartNew();
        foreach (var c in cells)
        {
            rice.CanEverPlantAt(c, map);
        }

        sw.Stop();
        APGTestComponent.Info($"perf CanEverPlantAt: {sw.Elapsed.TotalMilliseconds * 1000.0 / cells.Count:0.000} us/call with seed assignments on the map");
    }
}

public partial class APGTestComponent
{
    private static void PrepareUi()
    {
        Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
    }

    private static void Shot(string name)
    {
        Info($"shot {name}: windows [{string.Join(", ", Find.WindowStack.Windows.Select(w => w.GetType().Name))}]");
        Directory.CreateDirectory(Shots);
        ScreenCapture.CaptureScreenshot(Path.Combine(Shots, name + ".png"));
    }

    private static void OpenInspect(Thing t)
    {
        Find.Selector.ClearSelection();
        Find.Selector.Select(t, false, false);
        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, false);
        ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).OpenTabType = typeof(ITab_PlantGenetics);
        Find.CameraDriver.SetRootPosAndSize(t.Position.ToVector3Shifted(), 10f);
    }

    private static void CloseAll()
    {
        Find.WindowStack.Windows.Where(w => w is Dialog_NewExperiment or Dialog_ChooseCandidate or FloatMenu).ToList().ForEach(w => w.Close(false));
        Find.MainTabsRoot.EscapeCurrentTab(false);
    }

    private static Building_GeneticsStation Station(string def)
    {
        return map.listerBuildings.AllBuildingsColonistOfClass<Building_GeneticsStation>().FirstOrDefault(s => s.def.defName == def);
    }

    public override void GameComponentUpdate()
    {
        if (finished || shotStage == 0 || Time.frameCount < shotFrame)
        {
            return;
        }

        try
        {
            // Screenshots are taken at the end of the frame, so close windows a step later.
            if (closeNext)
            {
                closeNext = false;
                CloseAll();
                Find.Selector.ClearSelection();
                shotFrame = Time.frameCount + 10;
                return;
            }

            UiStep();
        }
        catch (Exception e)
        {
            Check(false, $"UI step {shotStage} threw: {e}");
            shotStage = 99;
        }

        shotStage++;
        shotFrame = Time.frameCount + 25;
    }

    private static int uiErrorsBefore;
    private static bool closeNext;

    private void UiStep()
    {
        map = Find.CurrentMap;
        var reg = PlantGeneticsRegistry.Instance;
        switch (shotStage)
        {
            case 1:
                ErrorWatch.RecordAll = true;
                uiErrorsBefore = ErrorWatch.Captured.Count;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                var plant = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Plant>()
                    .Where(p => CompPlantGenetics.Of(p)?.HasExplicitGenome == true && reg.Find(CompPlantGenetics.GenomeOf(p)) != null)
                    .OrderByDescending(p => CompPlantGenetics.GenomeOf(p).Quality).FirstOrDefault();
                if (plant != null)
                {
                    OpenInspect(plant);
                }

                break;
            case 3:
                Shot("01_plant_tab");
                break;
            case 4:
                var seed = map.listerThings.ThingsOfDef(APG_DefOf.APG_SeedPacket).OrderByDescending(s => reg.Find(CompSeedGenome.GenomeOf(s))?.parentB ?? -1).FirstOrDefault();
                if (seed != null)
                {
                    OpenInspect(seed);
                }

                break;
            case 6:
                Shot("02_seed_tab");
                CloseAll();
                break;
            case 7:
            {
                var b = Station("APG_CrossingBench");
                var dlg = new Dialog_NewExperiment(b);
                var recs = reg.Records.Where(r => r.Species.defName == "Plant_Rice" && SeedIndex.Total(map, r.Signature) > 0).Take(2).ToList();
                var inputs = Traverse.Create(dlg).Field("inputs").GetValue<List<CultivarRecord>>();
                for (var i = 0; i < recs.Count; i++)
                {
                    inputs[i] = recs[i];
                }

                Find.WindowStack.Add(dlg);
                break;
            }
            case 9:
                Shot("03_new_experiment_cross");
                closeNext = true;
                break;
            case 10:
            {
                var s = Station("APG_GeneSplicer");
                var dlg = new Dialog_NewExperiment(s);
                Traverse.Create(dlg).Field("op").SetValue(DefDatabase<GeneticOperationDef>.GetNamed("APG_Op_EditLocus"));
                var inputs = Traverse.Create(dlg).Field("inputs").GetValue<List<CultivarRecord>>();
                inputs[0] = reg.Records.FirstOrDefault(r => r.Species.defName == "Plant_Rice" && SeedIndex.Total(map, r.Signature) > 0);
                Traverse.Create(dlg).Field("level").SetValue(4);
                Find.WindowStack.Add(dlg);
                break;
            }
            case 12:
                Shot("04_new_experiment_edit");
                closeNext = true;
                break;
            case 13:
            {
                var b = map.listerBuildings.AllBuildingsColonistOfClass<Building_GeneticsStation>().FirstOrDefault(x => x.task?.state == TaskState.AwaitingChoice);
                Check(b != null, "reload: a station still awaits a choice");
                if (b != null)
                {
                    Find.WindowStack.Add(new Dialog_ChooseCandidate(b));
                }

                break;
            }
            case 15:
                Shot("05_choose_candidate");
                closeNext = true;
                break;
            case 16:
            {
                var def = DefDatabase<MainButtonDef>.GetNamed("APG_PlantGenetics");
                Find.MainTabsRoot.SetCurrentTab(def, false);
                var win = def.TabWindow;
                Traverse.Create(win).Field("selected").SetValue(reg.Records.Where(r => r.parentA >= 0 && r.parentB >= 0).LastOrDefault() ?? reg.Records.Last());
                break;
            }
            case 18:
                Shot("06_tab_strains");
                break;
            case 19:
                SetPage(1);
                break;
            case 21:
                Shot("07_tab_library");
                break;
            case 22:
                SetPage(2);
                break;
            case 24:
                Shot("08_tab_progress");
                break;
            case 25:
                CloseAll();
                break;
            case 26:
            {
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Research, false);
                var win = MainButtonDefOf.Research.TabWindow;
                var tab = DefDatabase<ResearchTabDef>.GetNamed("APG_PlantGeneticsTab");
                var t = Traverse.Create(win);
                if (t.Property("CurTab").PropertyExists())
                {
                    t.Property("CurTab").SetValue(tab);
                }
                else
                {
                    t.Field("curTabInt").SetValue(tab);
                }

                break;
            }
            case 28:
                Shot("09_research");
                break;
            case 29:
                CloseAll();
                break;
            case 30:
            {
                var zone = map.zoneManager.AllZones.OfType<Zone_Growing>().FirstOrDefault(z => MapComponent_SeedStock.For(map).Get(z)?.Cultivar != null);
                if (zone != null)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(zone, false, false);
                    Find.CameraDriver.SetRootPosAndSize(zone.Cells[0].ToVector3Shifted(), 12f);
                    var gizmos = SeedStockGizmos.For(zone).ToList();
                    Check(gizmos.Count == 2, $"zone with a strain shows seed stock and save-seed gizmos ({gizmos.Count})");
                    if (gizmos.Count > 0)
                    {
                        ((Command_Action)gizmos[0]).action();
                    }
                }

                break;
            }
            case 32:
                Shot("10_zone_seed_menu");
                closeNext = true;
                break;
            case 33:
            {
                var s = Station("APG_MutagenChamber") ?? Station("APG_CrossingBench");
                Find.CameraDriver.SetRootPosAndSize(s.Position.ToVector3Shifted() + new Vector3(0, 0, -2), 9f);
                break;
            }
            case 34:
                Shot("11_buildings");
                break;
            case 35:
            {
                var b = Station("APG_CrossingBench");
                Find.Selector.ClearSelection();
                Find.Selector.Select(b, false, false);
                break;
            }
            case 37:
                Shot("12_station_selected");
                break;
            case 38:
            {
                // Open every station's dialog and every inspect tab once (smoke).
                foreach (var st in map.listerBuildings.AllBuildingsColonistOfClass<Building_GeneticsStation>().ToList())
                {
                    Find.WindowStack.Add(new Dialog_NewExperiment(st));
                    _ = st.GetInspectString();
                    _ = st.GetGizmos().ToList();
                }

                break;
            }
            case 40:
                CloseAll();
                ErrorWatch.RecordAll = false;
                foreach (var e in ErrorWatch.AllDuringUi.Distinct().Take(10))
                {
                    Info("error during UI (any source): " + e.Replace("\n", " | "));
                }

                Check(ErrorWatch.Captured.Count == uiErrorsBefore, $"UI smoke: no errors from the mod ({ErrorWatch.Captured.Count - uiErrorsBefore})");
                break;
            case 41:
                Finish();
                break;
        }
    }

    private static void SetPage(int page)
    {
        var win = DefDatabase<MainButtonDef>.GetNamed("APG_PlantGenetics").TabWindow;
        var f = AccessTools.Field(win.GetType(), "page");
        f.SetValue(win, Enum.ToObject(f.FieldType, page));
    }
}
