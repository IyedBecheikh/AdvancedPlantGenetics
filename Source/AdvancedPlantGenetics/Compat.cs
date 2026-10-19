using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AdvancedPlantGenetics;

/// <summary>
/// Optional integrations, applied by name so no other mod is a compile-time dependency.
///
/// Progression: Agriculture locks crops until a seed bundle unlocks them. Here:
/// - unlocking a crop from a bundle also drops heirloom seed packets of it;
/// - a seed packet of a locked crop can unlock it too (it's seed of that crop, like a bundle).
/// </summary>
public static class Compat
{
    private static object paTracker;
    private static MethodInfo paIsUnlocked;
    private static MethodInfo paUnlock;
    private static FieldInfo paInstance;

    public static bool ProgressionAgriculture { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (!ModsConfig.IsActive("ferny.progressionagriculture"))
        {
            return;
        }

        try
        {
            var comp = AccessTools.TypeByName("ProgressionAgriculture.CompUnlockCrop");
            var tracker = AccessTools.TypeByName("ProgressionAgriculture.GameComponent_UnlockedCrops");
            var unlockMethod = comp == null ? null : AccessTools.Method(comp, "Unlock");
            paInstance = tracker == null ? null : AccessTools.Field(tracker, "Instance");
            paIsUnlocked = tracker == null ? null : AccessTools.Method(tracker, "IsCropUnlocked", [typeof(ThingDef)]);
            paUnlock = tracker == null ? null : AccessTools.Method(tracker, "UnlockCrop", [typeof(ThingDef)]);
            if (unlockMethod == null || paInstance == null || paIsUnlocked == null || paUnlock == null)
            {
                Log.Warning("[Advanced Plant Genetics] Progression: Agriculture found, but its API changed; integration disabled.");
                return;
            }

            harmony.Patch(unlockMethod, prefix: new HarmonyMethod(typeof(Compat), nameof(UnlockCropPrefix)));
            ProgressionAgriculture = true;
        }
        catch (Exception e)
        {
            Log.Error("[Advanced Plant Genetics] Progression: Agriculture integration failed: " + e);
        }
    }

    private static object Tracker => paTracker = paInstance?.GetValue(null) ?? paTracker;

    /// <summary>True when Progression: Agriculture keeps this crop locked.</summary>
    public static bool IsCropLocked(ThingDef crop)
    {
        if (!ProgressionAgriculture || crop == null || Tracker == null)
        {
            return false;
        }

        return !(bool)paIsUnlocked.Invoke(Tracker, [crop]);
    }

    public static void UnlockCrop(ThingDef crop)
    {
        if (ProgressionAgriculture && Tracker != null)
        {
            paUnlock.Invoke(Tracker, [crop]);
        }
    }

    /// <summary>Unlocking a crop from a seed bundle also gives heirloom seed packets of it.</summary>
    public static void UnlockCropPrefix(ThingComp __instance)
    {
        try
        {
            var parent = __instance.parent;
            var crop = Traverse.Create(__instance).Property("Props").Field("cropDef").GetValue<ThingDef>();
            var map = parent.MapHeld;
            if (crop == null || map == null || !GeneticsDB.IsGeneticPlant(crop))
            {
                return;
            }

            var g = Genome.Heirloom(crop, Rand.Int);
            SeedIndex.Spawn(g, 8, parent.PositionHeld, map, null, null, "APG.Origin_Heirloom");
        }
        catch (Exception e)
        {
            Log.Warning("[Advanced Plant Genetics] heirloom seeds from crop unlock failed: " + e.Message);
        }
    }
}
