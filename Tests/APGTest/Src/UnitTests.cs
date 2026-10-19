using System.Linq;
using AdvancedPlantGenetics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using static APGTest.APGTestComponent;

namespace APGTest;

public static class GenomeTests
{
    public static void Run()
    {
        var rice = ThingDef.Named("Plant_Rice");

        // Expression: one dominant allele wins, otherwise the average.
        var g = new Genome(rice);
        var gi = GeneticsDB.IndexOf(LocusEffect.Growth);
        g.a[gi] = new Allele(1, true);
        g.b[gi] = new Allele(5, false);
        g.Invalidate();
        Check(Mathf.Approximately(g.Expressed(gi), 1f), $"dominant allele expressed over recessive (got {g.Expressed(gi)})");
        g.b[gi] = new Allele(5, true);
        g.Invalidate();
        Check(Mathf.Approximately(g.Expressed(gi), 3f), $"codominant alleles average (got {g.Expressed(gi)})");

        // Code round trip.
        g.genes[APG_DefOf.APG_Perennial] = 1;
        g.Invalidate();
        var copy = new Genome { species = rice };
        copy.Parse(g.Code);
        Check(copy.Signature == g.Signature, "genome code round-trips");

        // Purebred breeds true; hybrids segregate.
        var pure = MakeGenome(rice, (LocusEffect.Growth, 3), (LocusEffect.Yield, 2));
        var allSame = Enumerable.Range(0, 50).All(_ => Genome.Cross(pure, pure, MutationSettings.None).Signature == pure.Signature);
        Check(pure.Purebred && allSame, "purebred x itself always gives the same genome");
        var hybrid = Genome.Cross(pure, new Genome(rice), MutationSettings.None);
        Check(!hybrid.Purebred, "purebred x wild type gives a hybrid");
        var distinct = Enumerable.Range(0, 60).Select(_ => Genome.Cross(hybrid, hybrid, MutationSettings.None).Signature).Distinct().Count();
        Check(distinct >= 4, $"selfing a hybrid segregates into several genotypes ({distinct})");

        // Recessive gene: carriers don't express it, two copies do.
        var carrier = new Genome(rice);
        carrier.SetGene(APG_DefOf.APG_Perennial, 1);
        var homo = new Genome(rice);
        homo.SetGene(APG_DefOf.APG_Perennial, 2);
        Check(!carrier.Expresses(APG_DefOf.APG_Perennial) && homo.Expresses(APG_DefOf.APG_Perennial), "recessive gene needs two copies");
        var kids = Enumerable.Range(0, 400).Select(_ => Genome.Cross(carrier, carrier, MutationSettings.None)).ToList();
        var ratio = kids.Count(k => k.Expresses(APG_DefOf.APG_Perennial)) / 400f;
        Check(ratio > 0.17f && ratio < 0.33f, $"carrier x carrier shows the recessive trait about 1 in 4 ({ratio:P0})");

        // Capacity: +4 costs 4, +6 costs 8, negative refunds, fertility response costs both ways.
        var load = MakeGenome(rice, (LocusEffect.Growth, 6), (LocusEffect.Yield, 4), (LocusEffect.HeatTolerance, -2), (LocusEffect.FertilityResponse, -3)).Load;
        Check(Mathf.Approximately(load, 8f + 4f - 2f + 3f), $"genetic load counts costs and refunds (got {load}, want 13)");
        var burdened = MakeGenome(rice, (LocusEffect.Growth, 6), (LocusEffect.Yield, 6));
        Check(burdened.Burden > 0f && burdened.Phenotype.burdenFactor < 1f, $"load above capacity gives burden ({burdened.Burden})");
        var poly = burdened.Copy();
        poly.SetGene(APG_DefOf.APG_Polyploid, 2);
        Check(poly.Burden < burdened.Burden, "polyploidy raises capacity");

        // Natural mutation never goes past the natural cap.
        var cap = MakeGenome(rice, (LocusEffect.Growth, 4));
        var over = Enumerable.Range(0, 300).Select(_ =>
        {
            var c = cap.Copy();
            c.Mutate(new MutationSettings { mutationsMin = 2, mutationsMax = 3, upBias = 1f, cap = GeneticsDB.NaturalCap });
            return Enumerable.Range(0, c.a.Length).Max(i => Mathf.Max(c.a[i].level, c.b[i].level));
        }).Max();
        Check(over <= 4, $"natural mutation is capped at +4 (max seen {over})");

        // Landraces are deterministic per seed.
        Check(Genome.Landrace(rice, 1234).Signature == Genome.Landrace(rice, 1234).Signature, "landrace genome is deterministic");
    }
}

public static class PhenotypeTests
{
    private static float GrowthAfterTicks(Plant p, int longTicks)
    {
        var before = p.Growth;
        for (var i = 0; i < longTicks; i++)
        {
            p.TickLong();
        }

        return p.Growth - before;
    }

    public static void Run(Map map)
    {
        var rice = ThingDef.Named("Plant_Rice");
        var baseDays = rice.plant.growDays;

        // Growth: G+8 grows 2.25x as fast as a neutral genome under identical conditions.
        var neutral = SpawnPlant(rice, Cell(-6, -4), new Genome(rice), 0.2f);
        var fast = SpawnPlant(rice, Cell(-6, -3), MakeGenome(rice, (LocusEffect.Growth, 8)), 0.2f);
        var dn = GrowthAfterTicks(neutral, 3);
        var df = GrowthAfterTicks(fast, 3);
        var expected = GeneticsDB.Locus(LocusEffect.Growth).curve.Evaluate(8) * MakeGenome(rice, (LocusEffect.Growth, 8)).Phenotype.burdenFactor;
        Check(dn > 0f && Mathf.Abs(df / dn - expected) < 0.05f, $"growth locus speeds growth (ratio {df / Mathf.Max(dn, 1e-6f):0.00}, want {expected:0.00})");
        Check(Mathf.Approximately(rice.plant.growDays, baseDays), $"def values are restored after the swap ({rice.plant.growDays} vs {baseDays})");

        // Yield: Y+6 vs neutral at full growth.
        var y0 = SpawnPlant(rice, Cell(-5, -4), new Genome(rice));
        var y6 = SpawnPlant(rice, Cell(-5, -3), MakeGenome(rice, (LocusEffect.Yield, 6)));
        var avg0 = Enumerable.Range(0, 400).Average(_ => y0.YieldNow());
        var avg6 = Enumerable.Range(0, 400).Average(_ => y6.YieldNow());
        var wantY = GeneticsDB.Locus(LocusEffect.Yield).curve.Evaluate(6) * MakeGenome(rice, (LocusEffect.Yield, 6)).Phenotype.burdenFactor;
        Check(avg0 > 0 && Mathf.Abs((float)(avg6 / avg0) - wantY) < 0.08f, $"yield locus raises harvest (ratio {avg6 / avg0:0.00}, want {wantY:0.00})");

        // Fertility response matters most in hydroponics.
        var basinDef = ThingDef.Named("HydroponicsBasin");
        var basinCell = Cell(4, -4);
        var basin = (Building)GenSpawn.Spawn(ThingMaker.MakeThing(basinDef), basinCell, map, Rot4.North);
        basin.SetFaction(Faction.OfPlayer);
        var cells = basin.OccupiedRect().Cells.ToList();
        var hydroNeutral = SpawnPlant(rice, cells[0], new Genome(rice), 0.3f);
        var hydroSens = SpawnPlant(rice, cells[1], MakeGenome(rice, (LocusEffect.FertilityResponse, 8)), 0.3f);
        var fert = map.fertilityGrid.FertilityAt(cells[0]);
        var sens = rice.plant.fertilitySensitivity;
        var want = (fert * (sens + 0.8f) + 1f - (sens + 0.8f)) / (fert * sens + 1f - sens);
        var got = hydroSens.GrowthRate / hydroNeutral.GrowthRate;
        Check(Mathf.Abs(got - want) < 0.05f, $"fertility response boosts hydroponic growth (ratio {got:0.00}, want {want:0.00}, fertility {fert})");

        // Soil tolerance: sand is too poor for rice unless the strain tolerates it.
        var sandCell = Cell(-3, -4);
        map.terrainGrid.SetTerrain(sandCell, TerrainDefOf.Sand);
        Check(!rice.CanEverPlantAt(sandCell, map), "wild-type rice can't be sown on sand");
        Check(MakeGenome(rice, (LocusEffect.SoilTolerance, 8)).Phenotype.stats.fertilityMin <= map.fertilityGrid.FertilityAt(sandCell), "F+8 lowers minimum fertility below sand");

        // Temperature tolerance shifts the growing range.
        var cold = MakeGenome(rice, (LocusEffect.ColdTolerance, 5)).Phenotype.stats;
        Check(Mathf.Approximately(cold.minGrowthTemperature, rice.plant.minGrowthTemperature - 15f), $"cold tolerance lowers minimum temperature ({cold.minGrowthTemperature})");

        // Light: chemosynthesis needs no light.
        var chemo = new Genome(rice);
        chemo.SetGene(DefDatabase<PlantGeneDef>.GetNamed("APG_Chemosynthetic"), 2);
        Check(chemo.Phenotype.stats.growMinGlow == 0f && chemo.Phenotype.stats.growOptimalGlow == 0f, "chemosynthetic plants need no light");

        // Circadian override: no night rest (checked with the harness' forced-awake off).
        TestOverrides.ForceAwake = false;
        var circ = new Genome(rice);
        circ.SetGene(DefDatabase<PlantGeneDef>.GetNamed("APG_CircadianOverride"), 2);
        var circPlant = SpawnPlant(rice, Cell(-4, -4), circ, 0.5f);
        var resting = Traverse.Create(circPlant).Property("Resting").GetValue<bool>();
        TestOverrides.ForceAwake = true;
        Check(!resting, "circadian override plant never rests");

        // Blight: immune plants can't be blighted.
        var immune = SpawnPlant(rice, Cell(-4, -3), MakeGenome(rice, (LocusEffect.BlightResistance, 10)), 0.7f);
        immune.CropBlighted();
        Check(!immune.BlightableNow && !immune.Blighted, "B+10 plants are immune to blight");

        // Bioluminescence lights the plant up.
        var glow = new Genome(rice);
        glow.SetGene(DefDatabase<PlantGeneDef>.GetNamed("APG_Bioluminescent"), 2);
        var glowPlant = SpawnPlant(rice, Cell(-3, -3), glow);
        CompPlantGenetics.Of(glowPlant).RefreshGlow();
        var glower = glowPlant.AllComps.OfType<CompPlantGlow>().FirstOrDefault();
        Check(glower != null && glower.Glows, "bioluminescent plant glows");

        // Perennial survives harvest.
        var per = new Genome(rice);
        per.SetGene(APG_DefOf.APG_Perennial, 2);
        var perPlant = SpawnPlant(rice, Cell(-2, -3), per);
        perPlant.PlantCollected(map.mapPawns.FreeColonistsSpawned.First(), PlantDestructionMode.Cut);
        Check(!perPlant.Destroyed && perPlant.Growth > 0.2f && perPlant.Growth < 0.5f, $"perennial regrows after harvest (growth {perPlant.Growth:0.00})");

        // Wild plants are untouched: no swap, no genome generated for growth.
        var wild = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Plant>().FirstOrDefault(p => !p.sown && GeneticsDB.IsGeneticPlant(p.def));
        if (wild != null)
        {
            Check(!GeneticPlant.Affects(wild), "wild plants are not affected by genetics");
        }
    }
}

public static class SeedTests
{
    public static void Run(Map map)
    {
        var rice = ThingDef.Named("Plant_Rice");
        var a = MakeGenome(rice, (LocusEffect.Growth, 2));
        var b = MakeGenome(rice, (LocusEffect.Yield, 3));
        var sa = SpawnSeeds(a, 5, Cell(0, 4));
        var sa2 = SeedIndex.MakeSeeds(a, 3);
        var sb = SeedIndex.MakeSeeds(b, 3);
        Check(sa.CanStackWith(sa2) && !sa.CanStackWith(sb), "seed packets stack only with identical genomes");
        var piece = sa.SplitOff(2);
        Check(CompSeedGenome.GenomeOf(piece)?.Signature == a.Signature && CompSeedGenome.GenomeOf(piece) != CompSeedGenome.GenomeOf(sa), "split-off packets carry their own genome copy");
        GenPlace.TryPlaceThing(piece, Cell(1, 4), map, ThingPlaceMode.Near);
        var neutral = SeedIndex.MakeSeeds(new Genome(rice), 1);
        var great = SeedIndex.MakeSeeds(MakeGenome(rice, (LocusEffect.Growth, 6), (LocusEffect.Yield, 4)), 1);
        var vn = neutral.MarketValue;
        var vg = great.MarketValue;
        Check(vg > vn * 2f, $"better genetics are worth more ({vn:0.0} vs {vg:0.0})");
        Check(sa.LabelNoCount.Contains("rice"), $"seed label names the species ({sa.LabelNoCount})");
        Check(SeedCount(a.Signature) == 5, $"seed index counts packets on the map ({SeedCount(a.Signature)})");
        var taken = SeedIndex.TakeOne(map, a.Signature, Cell(0, 4));
        Check(taken?.Signature == a.Signature && SeedCount(a.Signature) == 4, "taking a seed removes one packet");
        sa2.Destroy();
        sb.Destroy();
        neutral.Destroy();
        great.Destroy();
    }
}
