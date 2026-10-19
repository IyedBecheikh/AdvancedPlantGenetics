using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace AdvancedPlantGenetics;

/// <summary>A named strain: one exact genome of one species.</summary>
public class CultivarRecord : IExposable
{
    public int id;
    public string name;
    public bool playerNamed;
    public Genome template;
    public int parentA = -1;
    public int parentB = -1;
    public int createdTick;
    public string origin;
    public bool analyzed;
    public bool archived;
    public bool favorite;
    public bool hidden;

    public ThingDef Species => template?.species;
    public string Signature => template?.Signature;

    public string LabelCap => name;

    public string FullLabel => Species.label + " '" + name + "'";

    public void ExposeData()
    {
        Scribe_Values.Look(ref id, "id");
        Scribe_Values.Look(ref name, "name");
        Scribe_Values.Look(ref playerNamed, "playerNamed");
        Scribe_Deep.Look(ref template, "template");
        Scribe_Values.Look(ref parentA, "parentA", -1);
        Scribe_Values.Look(ref parentB, "parentB", -1);
        Scribe_Values.Look(ref createdTick, "createdTick");
        Scribe_Values.Look(ref origin, "origin");
        Scribe_Values.Look(ref analyzed, "analyzed");
        Scribe_Values.Look(ref archived, "archived");
        Scribe_Values.Look(ref favorite, "favorite");
        Scribe_Values.Look(ref hidden, "hidden");
    }
}

/// <summary>Colony-wide genetics knowledge: every strain that reached a seed packet, and the allele library.</summary>
public class PlantGeneticsRegistry : GameComponent
{
    public static PlantGeneticsRegistry Instance;

    private List<CultivarRecord> records = [];
    private int nextId = 1;
    private List<GeneLocusDef> libLoci = [];
    private List<int> libMax = [];
    private List<int> libMin = [];
    private HashSet<PlantGeneDef> knownGenes = [];
    private HashSet<PlantGeneDef> observedGenes = [];

    private readonly Dictionary<string, CultivarRecord> bySignature = new();
    private readonly Dictionary<int, CultivarRecord> byId = new();

    public PlantGeneticsRegistry(Game game)
    {
        Instance = this;
    }

    public IReadOnlyList<CultivarRecord> Records => records;
    public IEnumerable<PlantGeneDef> KnownGenes => knownGenes;

    public override void ExposeData()
    {
        Instance = this;
        Scribe_Collections.Look(ref records, "records", LookMode.Deep);
        Scribe_Values.Look(ref nextId, "nextId", 1);
        Scribe_Collections.Look(ref libLoci, "libLoci", LookMode.Def);
        Scribe_Collections.Look(ref libMax, "libMax", LookMode.Value);
        Scribe_Collections.Look(ref libMin, "libMin", LookMode.Value);
        Scribe_Collections.Look(ref knownGenes, "knownGenes", LookMode.Def);
        Scribe_Collections.Look(ref observedGenes, "observedGenes", LookMode.Def);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            records ??= [];
            records.RemoveAll(r => r?.template?.species == null);
            libLoci ??= [];
            libMax ??= [];
            libMin ??= [];
            knownGenes ??= [];
            knownGenes.Remove(null);
            observedGenes ??= [];
            observedGenes.Remove(null);
            // Drop entries whose locus def was removed.
            for (var i = libLoci.Count - 1; i >= 0; i--)
            {
                if (libLoci[i] == null || i >= libMax.Count || i >= libMin.Count)
                {
                    libLoci.RemoveAt(i);
                    if (i < libMax.Count)
                    {
                        libMax.RemoveAt(i);
                    }

                    if (i < libMin.Count)
                    {
                        libMin.RemoveAt(i);
                    }
                }
            }

            Reindex();
        }
    }

    private void Reindex()
    {
        bySignature.Clear();
        byId.Clear();
        foreach (var r in records)
        {
            bySignature[r.Signature] = r;
            byId[r.id] = r;
        }
    }

    // ------------------------------------------------------------------ strains

    public CultivarRecord Find(Genome g)
    {
        return g == null ? null : bySignature.TryGetValue(g.Signature, out var r) ? r : null;
    }

    public CultivarRecord Get(int id)
    {
        return byId.TryGetValue(id, out var r) ? r : null;
    }

    /// <summary>The record for this exact genome, created if new.</summary>
    public CultivarRecord Register(Genome g, string origin, CultivarRecord parentA = null, CultivarRecord parentB = null, bool analyzed = false)
    {
        if (Find(g) is { } existing)
        {
            if (analyzed && !existing.analyzed)
            {
                MarkAnalyzed(existing);
            }

            existing.hidden = false;
            return existing;
        }

        var rec = new CultivarRecord
        {
            id = nextId++,
            template = g.Copy(),
            name = NameMaker.Make(g.species, records.Select(r => r.name)),
            parentA = parentA?.id ?? -1,
            parentB = parentB?.id ?? -1,
            createdTick = Verse.Find.TickManager?.TicksGame ?? 0,
            origin = origin
        };
        records.Add(rec);
        bySignature[rec.Signature] = rec;
        byId[rec.id] = rec;
        if (analyzed)
        {
            MarkAnalyzed(rec);
        }

        NoticeGenes(g);
        return rec;
    }

    public bool IsAnalyzed(Genome g)
    {
        return Find(g)?.analyzed == true;
    }

    public void MarkAnalyzed(CultivarRecord rec)
    {
        rec.analyzed = true;
        var g = rec.template;
        for (var i = 0; i < GeneticsDB.Loci.Count; i++)
        {
            AddToLibrary(GeneticsDB.Loci[i], g.a[i].level);
            AddToLibrary(GeneticsDB.Loci[i], g.b[i].level);
        }

        foreach (var gene in g.genes.Keys)
        {
            knownGenes.Add(gene);
        }
    }

    public void Forget(CultivarRecord rec)
    {
        rec.hidden = true;
        rec.favorite = false;
    }

    public IEnumerable<CultivarRecord> ForSpecies(ThingDef species)
    {
        return records.Where(r => r.Species == species);
    }

    public int ArchivedCount => records.Count(r => r.archived);

    // ------------------------------------------------------------------ allele library

    private void AddToLibrary(GeneLocusDef locus, int level)
    {
        var i = libLoci.IndexOf(locus);
        if (i < 0)
        {
            libLoci.Add(locus);
            libMax.Add(level);
            libMin.Add(level);
            return;
        }

        if (level > libMax[i])
        {
            libMax[i] = level;
        }

        if (level < libMin[i])
        {
            libMin[i] = level;
        }
    }

    public int LibraryMax(GeneLocusDef locus)
    {
        var i = libLoci.IndexOf(locus);
        return i < 0 ? 0 : libMax[i];
    }

    public int LibraryMin(GeneLocusDef locus)
    {
        var i = libLoci.IndexOf(locus);
        return i < 0 ? 0 : libMin[i];
    }

    public bool KnowsGene(PlantGeneDef gene)
    {
        return knownGenes.Contains(gene);
    }

    public void LearnGene(PlantGeneDef gene)
    {
        knownGenes.Add(gene);
    }

    /// <summary>First time a gene is seen expressed in the colony: tell the player.</summary>
    public void NoticeGenes(Genome g)
    {
        foreach (var gene in g.ExpressedGenes)
        {
            if (observedGenes.Add(gene) && Current.ProgramState == ProgramState.Playing)
            {
                Verse.Find.LetterStack.ReceiveLetter("APG.NewTraitLetter".Translate(gene.LabelCap),
                    "APG.NewTraitLetterDesc".Translate(g.species.label, gene.LabelCap, gene.description), LetterDefOf.PositiveEvent);
            }
        }
    }

    public void Rename(CultivarRecord rec, string name)
    {
        rec.name = name;
        rec.playerNamed = true;
    }
}

/// <summary>Short evocative cultivar names ("Amber Dawn", "Riverbend Gold").</summary>
public static class NameMaker
{
    private static readonly string[] First =
    [
        "Amber", "Autumn", "Black", "Bright", "Copper", "Crimson", "Dawn", "Deep", "Dusk", "Ember", "Frost", "Gilded", "Golden",
        "Green", "Hearth", "High", "Iron", "Ivory", "Jade", "Long", "Low", "Marsh", "Midnight", "Mist", "Moon", "Old", "Pale",
        "Prairie", "Quiet", "Red", "River", "Rust", "Silver", "Sky", "Slate", "Spring", "Star", "Stone", "Storm", "Summer",
        "Sun", "Swift", "Tall", "Thunder", "Velvet", "Wild", "Willow", "Winter"
    ];

    private static readonly string[] Second =
    [
        "Bounty", "Crown", "Dawn", "Delight", "Dream", "Field", "Flame", "Gem", "Giant", "Glory", "Harvest", "Heart", "Hope",
        "Jewel", "King", "Lady", "Light", "March", "Mark", "Pearl", "Pride", "Queen", "Reach", "Ridge", "Rose", "Runner",
        "Shade", "Song", "Spire", "Sprout", "Star", "Strike", "Sweet", "Tide", "Vale", "Wonder"
    ];

    public static string Make(ThingDef species, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var f = First.RandomElement();
            var s = Second.RandomElement();
            if (f == s)
            {
                continue;
            }

            var name = f + " " + s;
            if (!used.Contains(name))
            {
                return name;
            }
        }

        return "Strain " + (used.Count + 1);
    }
}
