using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

public struct Allele(int level, bool dominant)
{
    public int level = level;
    public bool dominant = dominant;

    public override string ToString()
    {
        return (level > 0 ? "+" : "") + level + (dominant ? "D" : "r");
    }
}

/// <summary>How offspring genomes may change while they form.</summary>
public class MutationSettings
{
    public float locusChance;
    public int mutationsMin;
    public int mutationsMax;
    public int maxStep = 1;
    public int cap = GeneticsDB.NaturalCap;
    public float upBias = 0.5f;
    public float geneChance;
    public bool mutagen;

    public static readonly MutationSettings Natural = new()
    {
        locusChance = 0.02f,
        geneChance = 0.004f
    };

    public static readonly MutationSettings None = new();
}

/// <summary>
/// A plant genome: two alleles per locus plus special gene copies. Immutable in practice: operations build new genomes.
/// </summary>
public class Genome : IExposable
{
    public ThingDef species;
    public Allele[] a;
    public Allele[] b;
    public Dictionary<PlantGeneDef, int> genes = new();
    public int generation;

    private string signatureCache;
    private Phenotype phenotypeCache;

    public Genome()
    {
    }

    public Genome(ThingDef species)
    {
        this.species = species;
        var n = GeneticsDB.Loci.Count;
        a = new Allele[n];
        b = new Allele[n];
        for (var i = 0; i < n; i++)
        {
            a[i] = new Allele(0, true);
            b[i] = new Allele(0, true);
        }
    }

    // ------------------------------------------------------------------ expression

    public float Expressed(int i)
    {
        var x = a[i];
        var y = b[i];
        if (x.dominant != y.dominant)
        {
            return x.dominant ? x.level : y.level;
        }

        return (x.level + y.level) / 2f;
    }

    public float Expressed(GeneLocusDef locus)
    {
        return Expressed(GeneticsDB.IndexOf(locus));
    }

    public int Copies(PlantGeneDef gene)
    {
        return genes.TryGetValue(gene, out var c) ? c : 0;
    }

    public bool Expresses(PlantGeneDef gene)
    {
        return Copies(gene) >= (gene.recessive ? 2 : 1);
    }

    public IEnumerable<PlantGeneDef> ExpressedGenes => genes.Keys.Where(Expresses).OrderBy(g => g.order);

    public bool HomozygousAt(int i)
    {
        return a[i].level == b[i].level && a[i].dominant == b[i].dominant;
    }

    public int HomozygousCount
    {
        get
        {
            var n = 0;
            for (var i = 0; i < a.Length; i++)
            {
                if (HomozygousAt(i))
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>Breeds true: every locus homozygous and every gene present in 0 or 2 copies.</summary>
    public bool Purebred => HomozygousCount == a.Length && genes.Values.All(c => c != 1);

    public float Capacity
    {
        get
        {
            var cap = GeneticsDB.BaseCapacity;
            foreach (var g in ExpressedGenes)
            {
                cap += g.capacityBonus;
            }

            return cap;
        }
    }

    public float Load
    {
        get
        {
            var load = 0f;
            for (var i = 0; i < a.Length; i++)
            {
                load += GeneticsDB.Loci[i].Cost(Expressed(i));
            }

            foreach (var g in ExpressedGenes)
            {
                load += g.capacityCost;
            }

            return load;
        }
    }

    public float Burden => Mathf.Max(0f, Load - Capacity);

    /// <summary>Sum of positive expressed levels plus gene count; a rough quality score for value and sorting.</summary>
    public float Quality
    {
        get
        {
            var q = 0f;
            for (var i = 0; i < a.Length; i++)
            {
                var e = Expressed(i);
                if (GeneticsDB.Loci[i].loadMode == LoadMode.Absolute)
                {
                    e = Mathf.Abs(e) * 0.5f;
                }

                q += Mathf.Max(0f, e);
            }

            q += ExpressedGenes.Sum(g => 1f + (int)g.tier - (int)TechLevel.Neolithic);
            return q - Burden;
        }
    }

    public Phenotype Phenotype => phenotypeCache ??= new Phenotype(this);

    public bool IsNeutral => Phenotype.neutral;

    // ------------------------------------------------------------------ identity

    /// <summary>Species + genotype. Two genomes with the same signature are the same strain.</summary>
    public string Signature => signatureCache ??= species?.defName + "|" + Code;

    public string Code
    {
        get
        {
            var sb = new StringBuilder();
            for (var i = 0; i < a.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(GeneticsDB.Loci[i].defName).Append(':').Append(Enc(a[i])).Append(Enc(b[i]));
            }

            foreach (var g in genes.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key.defName))
            {
                sb.Append(';').Append(g.Key.defName).Append(':').Append(g.Value);
            }

            return sb.ToString();
        }
    }

    private static string Enc(Allele al)
    {
        return al.level + (al.dominant ? "D" : "r");
    }

    public void Invalidate()
    {
        signatureCache = null;
        phenotypeCache = null;
    }

    public void Parse(string code)
    {
        var n = GeneticsDB.Loci.Count;
        a = new Allele[n];
        b = new Allele[n];
        for (var i = 0; i < n; i++)
        {
            a[i] = new Allele(0, true);
            b[i] = new Allele(0, true);
        }

        genes.Clear();
        if (code.NullOrEmpty())
        {
            return;
        }

        var parts = code.Split(';');
        foreach (var locusPart in parts[0].Split(','))
        {
            var kv = locusPart.Split(':');
            if (kv.Length != 2)
            {
                continue;
            }

            var locus = DefDatabase<GeneLocusDef>.GetNamedSilentFail(kv[0]);
            if (locus == null)
            {
                continue;
            }

            var alleles = ParseAlleles(kv[1]);
            if (alleles.Count == 2)
            {
                var i = GeneticsDB.IndexOf(locus);
                a[i] = alleles[0];
                b[i] = alleles[1];
            }
        }

        for (var p = 1; p < parts.Length; p++)
        {
            var kv = parts[p].Split(':');
            if (kv.Length == 2 && DefDatabase<PlantGeneDef>.GetNamedSilentFail(kv[0]) is { } gene && int.TryParse(kv[1], out var copies))
            {
                genes[gene] = Mathf.Clamp(copies, 0, 2);
            }
        }

        Invalidate();
    }

    private static List<Allele> ParseAlleles(string s)
    {
        var result = new List<Allele>();
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] is 'D' or 'r')
            {
                if (int.TryParse(s.Substring(start, i - start), out var level))
                {
                    result.Add(new Allele(level, s[i] == 'D'));
                }

                start = i + 1;
            }
        }

        return result;
    }

    public void ExposeData()
    {
        Scribe_Defs.Look(ref species, "species");
        var code = Scribe.mode == LoadSaveMode.Saving ? Code : null;
        Scribe_Values.Look(ref code, "code");
        Scribe_Values.Look(ref generation, "generation");
        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            Parse(code);
        }
    }

    public Genome Copy()
    {
        var g = new Genome
        {
            species = species,
            a = (Allele[])a.Clone(),
            b = (Allele[])b.Clone(),
            genes = new Dictionary<PlantGeneDef, int>(genes),
            generation = generation
        };
        return g;
    }

    public override string ToString()
    {
        return Signature;
    }

    // ------------------------------------------------------------------ editing (used by operations; call Invalidate after)

    public void SetLocus(int i, int level, bool dominant)
    {
        a[i] = new Allele(level, dominant);
        b[i] = new Allele(level, dominant);
        Invalidate();
    }

    public void SetGene(PlantGeneDef gene, int copies)
    {
        if (copies <= 0)
        {
            genes.Remove(gene);
        }
        else
        {
            genes[gene] = Mathf.Min(2, copies);
        }

        Invalidate();
    }

    // ------------------------------------------------------------------ inheritance

    /// <summary>Mendelian cross: one allele per locus and one gene copy (or none) from each parent, then mutation.</summary>
    public static Genome Cross(Genome p1, Genome p2, MutationSettings mutation, List<PlantGeneDef> newGenes = null)
    {
        var child = new Genome(p1.species)
        {
            generation = Mathf.Max(p1.generation, p2.generation) + 1
        };
        for (var i = 0; i < child.a.Length; i++)
        {
            child.a[i] = Rand.Bool ? p1.a[i] : p1.b[i];
            child.b[i] = Rand.Bool ? p2.a[i] : p2.b[i];
        }

        foreach (var gene in p1.genes.Keys.Union(p2.genes.Keys))
        {
            var copies = Gamete(p1.Copies(gene)) + Gamete(p2.Copies(gene));
            if (copies > 0)
            {
                child.genes[gene] = copies;
            }
        }

        child.Mutate(mutation, newGenes);
        child.Invalidate();
        return child;
    }

    private static int Gamete(int copies)
    {
        return copies switch
        {
            >= 2 => 1,
            1 => Rand.Bool ? 1 : 0,
            _ => 0
        };
    }

    /// <summary>Apply random mutations. Mutation never lowers a locus that is already above the cap.</summary>
    public void Mutate(MutationSettings m, List<PlantGeneDef> newGenes = null)
    {
        if (m == null)
        {
            return;
        }

        var forced = m.mutationsMax > 0 ? Rand.RangeInclusive(m.mutationsMin, m.mutationsMax) : 0;
        var loci = Enumerable.Range(0, a.Length).ToList();
        if (forced > 0)
        {
            foreach (var i in loci.InRandomOrder().Take(forced))
            {
                MutateLocus(i, m);
            }
        }
        else if (m.locusChance > 0f)
        {
            foreach (var i in loci)
            {
                if (Rand.Chance(m.locusChance))
                {
                    MutateLocus(i, m);
                }
            }
        }

        if (m.geneChance > 0f && Rand.Chance(m.geneChance))
        {
            var pool = GeneticsDB.Genes.Where(g => g.Available && (m.mutagen ? g.mutagenWeight : g.naturalWeight) > 0f).ToList();
            if (pool.TryRandomElementByWeight(g => m.mutagen ? g.mutagenWeight : g.naturalWeight, out var gene) && Copies(gene) < 2)
            {
                genes[gene] = Copies(gene) + 1;
                newGenes?.Add(gene);
            }
        }

        Invalidate();
    }

    private void MutateLocus(int i, MutationSettings m)
    {
        var locus = GeneticsDB.Loci[i];
        var onA = Rand.Bool;
        var al = onA ? a[i] : b[i];
        var step = Rand.RangeInclusive(1, Mathf.Max(1, m.maxStep));
        var up = Rand.Chance(m.upBias);
        var target = al.level + (up ? step : -step);
        var cap = Mathf.Max(m.cap, al.level);
        target = Mathf.Clamp(target, locus.minLevel, Mathf.Min(cap, locus.maxLevel));
        // New mutant alleles are mostly recessive: improvements hide until purebred or analysed.
        var mutant = new Allele(target, Rand.Chance(0.35f));
        if (onA)
        {
            a[i] = mutant;
        }
        else
        {
            b[i] = mutant;
        }
    }

    // ------------------------------------------------------------------ natural genomes

    /// <summary>A natural (wild or landrace) genome. Deterministic for a given seed.</summary>
    public static Genome Landrace(ThingDef species, int seed, float richness = 0f)
    {
        Rand.PushState(seed);
        try
        {
            var g = new Genome(species);
            for (var i = 0; i < g.a.Length; i++)
            {
                g.a[i] = WildAllele(richness);
                g.b[i] = WildAllele(richness);
            }

            foreach (var gene in GeneticsDB.Genes)
            {
                if (gene.naturalWeight > 0f && gene.Available && Rand.Chance(0.004f * gene.naturalWeight * (1f + richness * 4f)))
                {
                    g.genes[gene] = 1;
                }
            }

            g.Invalidate();
            return g;
        }
        finally
        {
            Rand.PopState();
        }
    }

    private static Allele WildAllele(float richness)
    {
        var r = Rand.Value;
        int level;
        if (r < 0.78f - richness * 0.3f)
        {
            level = 0;
        }
        else if (r < 0.88f - richness * 0.1f)
        {
            level = 1;
        }
        else if (r < 0.94f)
        {
            level = -1;
        }
        else if (r < 0.975f)
        {
            level = 2;
        }
        else if (r < 0.99f)
        {
            level = -2;
        }
        else
        {
            level = 3;
        }

        // The wild-type allele (0) is usually dominant; variants are often recessive.
        var dominant = level == 0 ? Rand.Chance(0.75f) : Rand.Chance(0.45f);
        return new Allele(level, dominant);
    }

    /// <summary>Heirloom seed from traders or crop bundles: a landrace with a couple of fixed improvements.</summary>
    public static Genome Heirloom(ThingDef species, int seed)
    {
        var g = Landrace(species, seed, 0.6f);
        Rand.PushState(seed ^ 0x5EED);
        try
        {
            foreach (var i in Enumerable.Range(0, g.a.Length).InRandomOrder().Take(Rand.RangeInclusive(1, 2)))
            {
                var level = Rand.RangeInclusive(1, 3);
                g.SetLocus(i, level, Rand.Bool);
            }
        }
        finally
        {
            Rand.PopState();
        }

        g.Invalidate();
        return g;
    }
}
