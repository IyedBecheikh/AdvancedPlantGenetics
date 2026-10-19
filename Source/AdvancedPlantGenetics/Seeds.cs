using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

public class CompProperties_SeedGenome : CompProperties
{
    public CompProperties_SeedGenome()
    {
        compClass = typeof(CompSeedGenome);
    }
}

/// <summary>The genome carried by a seed packet. Packets only stack with identical genomes.</summary>
public class CompSeedGenome : ThingComp
{
    public Genome genome;

    /// <summary>Set while code creates a packet it will fill itself, so PostPostMake doesn't roll a random strain.</summary>
    public static bool Making;

    public CultivarRecord Cultivar => PlantGeneticsRegistry.Instance?.Find(genome);

    public static Genome GenomeOf(Thing t)
    {
        return t is ThingWithComps twc ? twc.GetComp<CompSeedGenome>()?.genome : null;
    }

    public override void PostPostMake()
    {
        base.PostPostMake();
        if (genome == null && !Making && Current.ProgramState == ProgramState.Playing)
        {
            // Generated without a genome (trader stock, debug spawn): an heirloom strain of a random crop.
            var species = GeneticsDB.SowableCrops.Where(d => d.plant.sowResearchPrerequisites.NullOrEmpty() || Rand.Chance(0.3f)).RandomElementWithFallback();
            if (species != null)
            {
                genome = Genome.Heirloom(species, Rand.Int);
                PlantGeneticsRegistry.Instance?.Register(genome, "APG.Origin_Trade");
            }
        }
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Deep.Look(ref genome, "genome");
    }

    public override bool AllowStackWith(Thing other)
    {
        var og = GenomeOf(other);
        return genome != null && og != null && og.Signature == genome.Signature;
    }

    public override void PostSplitOff(Thing piece)
    {
        base.PostSplitOff(piece);
        if (piece != parent && piece is ThingWithComps twc && twc.GetComp<CompSeedGenome>() is { } other)
        {
            other.genome = genome?.Copy();
        }
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        SeedIndex.Dirty(parent.Map);
    }

    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        base.PostDeSpawn(map, mode);
        SeedIndex.Dirty(map);
    }

    public override string TransformLabel(string label)
    {
        if (genome?.species == null)
        {
            return label;
        }

        var rec = Cultivar;
        return rec != null ? "APG.SeedLabelNamed".Translate(genome.species.label, rec.name) : "APG.SeedLabel".Translate(genome.species.label);
    }

    public override string CompInspectStringExtra()
    {
        if (genome == null)
        {
            return null;
        }

        var sb = new StringBuilder();
        var rec = Cultivar;
        if (rec != null)
        {
            sb.Append("APG.Generation".Translate()).Append(": ").Append(genome.generation);
            if (rec.analyzed)
            {
                sb.Append(", ").Append("APG.Analyzed".Translate());
            }

            if (rec.archived)
            {
                sb.Append(", ").Append("APG.Archived".Translate());
            }
        }

        var summary = GenomeText.ShortSummary(genome);
        if (!summary.NullOrEmpty())
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.Append(summary);
        }

        return sb.ToString();
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        // Progression: Agriculture: seed of a locked crop unlocks it, like a seed bundle.
        if (genome?.species != null && parent.Spawned && Compat.IsCropLocked(genome.species))
        {
            yield return new Command_Action
            {
                defaultLabel = "APG.UnlockCrop".Translate(genome.species.label),
                defaultDesc = "APG.UnlockCropDesc".Translate(genome.species.label),
                icon = Tex.SeedStock,
                action = () =>
                {
                    Compat.UnlockCrop(genome.species);
                    parent.SplitOff(1).Destroy();
                    SeedIndex.Dirty(parent.MapHeld);
                }
            };
        }
    }

    public override string GetDescriptionPart()
    {
        return genome == null ? null : GenomeText.LongDescription(genome);
    }
}

/// <summary>Seed packets are worth more when they carry better genetics.</summary>
public class StatPart_SeedQuality : StatPart
{
    public override void TransformValue(StatRequest req, ref float val)
    {
        if (req.HasThing && CompSeedGenome.GenomeOf(req.Thing) is { } g)
        {
            val *= Factor(g);
        }
    }

    public override string ExplanationPart(StatRequest req)
    {
        if (req.HasThing && CompSeedGenome.GenomeOf(req.Thing) is { } g)
        {
            return "APG.SeedQualityValue".Translate() + ": x" + Factor(g).ToString("0.00");
        }

        return null;
    }

    private static float Factor(Genome g)
    {
        var plantValue = g.species?.plant?.harvestedThingDef?.BaseMarketValue ?? 1f;
        var speciesFactor = Mathf.Clamp(plantValue * (g.species?.plant?.harvestYield ?? 1f) / 8f, 0.5f, 6f);
        return speciesFactor * (1f + 0.25f * Mathf.Max(0f, g.Quality));
    }
}

/// <summary>Per-map index of seed packets by strain signature (rebuilt lazily when packets spawn/despawn).</summary>
public static class SeedIndex
{
    private class MapIndex
    {
        public bool dirty = true;
        public int builtTick = -9999;
        public readonly Dictionary<string, List<Thing>> stacks = new();
        public readonly Dictionary<string, int> pending = new();
    }

    private static readonly Dictionary<Map, MapIndex> Maps = new();

    public static void Dirty(Map map)
    {
        if (map != null && Maps.TryGetValue(map, out var idx))
        {
            idx.dirty = true;
        }
    }

    private static MapIndex Get(Map map)
    {
        if (!Maps.TryGetValue(map, out var idx))
        {
            idx = new MapIndex();
            Maps[map] = idx;
            // Forget maps that are gone.
            foreach (var m in Maps.Keys.Where(m => m != map && Find.Maps.IndexOf(m) < 0).ToList())
            {
                Maps.Remove(m);
            }
        }

        var now = Find.TickManager.TicksGame;
        if (idx.dirty || now - idx.builtTick > 600)
        {
            idx.stacks.Clear();
            foreach (var t in map.listerThings.ThingsOfDef(APG_DefOf.APG_SeedPacket))
            {
                var g = CompSeedGenome.GenomeOf(t);
                if (g == null)
                {
                    continue;
                }

                if (!idx.stacks.TryGetValue(g.Signature, out var list))
                {
                    list = [];
                    idx.stacks[g.Signature] = list;
                }

                list.Add(t);
            }

            idx.dirty = false;
            idx.builtTick = now;
        }

        return idx;
    }

    /// <summary>Unforbidden seed packets of this strain on the map, minus seeds promised to sowers in progress.</summary>
    public static int Available(Map map, string signature)
    {
        var idx = Get(map);
        var n = 0;
        if (idx.stacks.TryGetValue(signature, out var list))
        {
            foreach (var t in list)
            {
                if (!t.Destroyed && t.Spawned && !t.IsForbidden(Faction.OfPlayer))
                {
                    n += t.stackCount;
                }
            }
        }

        return n - (idx.pending.TryGetValue(signature, out var p) ? p : 0);
    }

    public static int Total(Map map, string signature)
    {
        var idx = Get(map);
        return idx.stacks.TryGetValue(signature, out var list) ? list.Where(t => !t.Destroyed).Sum(t => t.stackCount) : 0;
    }

    public static IEnumerable<Thing> Stacks(Map map, string signature)
    {
        var idx = Get(map);
        return idx.stacks.TryGetValue(signature, out var list) ? list.Where(t => !t.Destroyed && t.Spawned) : [];
    }

    public static IEnumerable<string> Signatures(Map map)
    {
        return Get(map).stacks.Where(kv => kv.Value.Any(t => !t.Destroyed)).Select(kv => kv.Key);
    }

    public static void AddPending(Map map, string signature, int delta)
    {
        var idx = Get(map);
        idx.pending.TryGetValue(signature, out var p);
        p = Mathf.Max(0, p + delta);
        if (p == 0)
        {
            idx.pending.Remove(signature);
        }
        else
        {
            idx.pending[signature] = p;
        }
    }

    /// <summary>Remove one seed of this strain from the map. Prefers the stack nearest to <paramref name="near"/>.</summary>
    public static Genome TakeOne(Map map, string signature, IntVec3 near)
    {
        var stack = Stacks(map, signature).Where(t => !t.IsForbidden(Faction.OfPlayer)).OrderBy(t => t.PositionHeld.DistanceToSquared(near)).FirstOrDefault()
                    ?? Stacks(map, signature).FirstOrDefault();
        if (stack == null)
        {
            return null;
        }

        var g = CompSeedGenome.GenomeOf(stack).Copy();
        if (stack.stackCount > 1)
        {
            stack.stackCount--;
        }
        else
        {
            stack.Destroy();
        }

        Dirty(map);
        return g;
    }

    public static Thing MakeSeeds(Genome genome, int count)
    {
        Thing t;
        CompSeedGenome.Making = true;
        try
        {
            t = ThingMaker.MakeThing(APG_DefOf.APG_SeedPacket);
        }
        finally
        {
            CompSeedGenome.Making = false;
        }

        var comp = ((ThingWithComps)t).GetComp<CompSeedGenome>();
        comp.genome = genome.Copy();
        t.stackCount = Mathf.Max(1, count);
        return t;
    }

    public static void Spawn(Genome genome, int count, IntVec3 at, Map map, CultivarRecord parentA = null, CultivarRecord parentB = null, string origin = "APG.Origin_Bred", bool analyzed = false)
    {
        PlantGeneticsRegistry.Instance.Register(genome, origin, parentA, parentB, analyzed);
        while (count > 0)
        {
            var n = Mathf.Min(count, APG_DefOf.APG_SeedPacket.stackLimit);
            GenPlace.TryPlaceThing(MakeSeeds(genome, n), at, map, ThingPlaceMode.Near);
            count -= n;
        }

        Dirty(map);
    }
}
