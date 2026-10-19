using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AdvancedPlantGenetics;

public enum LocusEffect
{
    Growth,
    Yield,
    FertilityResponse,
    SoilTolerance,
    LightEfficiency,
    ColdTolerance,
    HeatTolerance,
    BlightResistance,
    Vigor
}

/// <summary>How a locus' expressed level is charged against genome capacity.</summary>
public enum LoadMode
{
    /// <summary>Positive levels cost, negative levels refund.</summary>
    PositiveRefund,
    /// <summary>Positive levels cost, negative levels are free.</summary>
    Positive,
    /// <summary>Either direction costs |level|.</summary>
    Absolute
}

/// <summary>One gene locus (two alleles). Effects are applied in <see cref="Phenotype"/>.</summary>
public class GeneLocusDef : Def
{
    public string symbol = "?";
    public LocusEffect effect;
    public LoadMode loadMode = LoadMode.PositiveRefund;
    public int order;
    public int minLevel = -3;
    public int maxLevel = 10;
    public Color color = Color.white;

    /// <summary>Factor by expressed level (Growth, Yield).</summary>
    public SimpleCurve curve;

    /// <summary>Linear change per level for linear effects.</summary>
    public float perLevel;

    [MustTranslate] public string positiveWord;
    [MustTranslate] public string negativeWord;
    [MustTranslate] public string strongPositiveWord;

    public string Word(float expressed)
    {
        if (expressed >= 4f && !strongPositiveWord.NullOrEmpty())
        {
            return strongPositiveWord;
        }

        if (expressed >= 1.5f)
        {
            return positiveWord;
        }

        return expressed <= -1.5f ? negativeWord : null;
    }

    public float Cost(float expressed)
    {
        switch (loadMode)
        {
            case LoadMode.Absolute:
                return CostPositive(Mathf.Abs(expressed));
            case LoadMode.Positive:
                return expressed > 0f ? CostPositive(expressed) : 0f;
            default:
                return expressed > 0f ? CostPositive(expressed) : Mathf.Max(expressed, -3f);
        }
    }

    private static float CostPositive(float level)
    {
        return level <= 4f ? level : 4f + 2f * (level - 4f);
    }
}

/// <summary>A special gene: present in 0, 1 or 2 copies, expressed from one copy unless recessive.</summary>
public class PlantGeneDef : Def
{
    public TechLevel tier = TechLevel.Neolithic;
    public bool recessive;
    public float capacityCost;
    public float capacityBonus;
    public int order;
    public Color color = new(0.6f, 0.85f, 0.6f);

    /// <summary>Weight for appearing by natural mutation or in wild plants (0 = never).</summary>
    public float naturalWeight;
    /// <summary>Weight for appearing in mutagen chamber candidates (0 = never).</summary>
    public float mutagenWeight;
    /// <summary>Can be spliced at the gene splicer once known (discovered or synthetic).</summary>
    public bool spliceable = true;
    /// <summary>Synthetic genes need no discovery, only this research.</summary>
    public ResearchProjectDef syntheticResearch;
    public string requiredModId;

    // Effects
    public float regrowGrowth;
    public bool frostHardy;
    public bool pollutionTolerant;
    public bool circadianOverride;
    public bool runners;
    public bool toxinScrubber;
    public bool chemosynthetic;
    public bool deathless;
    public float glowRadius;
    public ColorInt glowColor = new(150, 255, 190, 0);
    public ThingDef harvestBonusThing;
    public float harvestBonusPerYield;

    public bool Available => requiredModId.NullOrEmpty() || ModsConfig.IsActive(requiredModId);
    public bool Synthetic => syntheticResearch != null;
}

public enum OperationKind
{
    Cross,
    SelfPollinate,
    Propagate,
    Analyze,
    Archive,
    Mutate,
    Polyploidy,
    EditLocus,
    SpliceGene,
    RemoveGene,
    SynthesizeSeeds,
    SyntheticChromosome
}

/// <summary>An experiment or production job a genetics station can run.</summary>
public class GeneticOperationDef : Def
{
    public OperationKind kind;
    public int inputs = 1;
    public bool consumesInputs = true;
    public float workAmount = 2000f;
    public SkillDef skill;
    public int minSkill;
    public float fuelCost;
    /// <summary>Extra fuel per target level above 2 (edits).</summary>
    public float fuelPerLevel;
    public ResearchProjectDef researchPrerequisite;
    public int outputCount = 1;
    public int candidates;
    public int levelCap = 4;
    public TechLevel maxGeneTier = TechLevel.Spacer;
    public bool useLibrary;
    public bool canRepeat;
    public int order;

    public bool Unlocked => researchPrerequisite == null || researchPrerequisite.IsFinished;
    public bool HasCandidates => candidates > 0;
}

/// <summary>Attached to genetics station ThingDefs.</summary>
public class StationExtension : DefModExtension
{
    public List<GeneticOperationDef> operations = [];
    public bool labWork;
    public bool autonomous;
    public float workSpeed = 1f;
    /// <summary>Facility ThingDef that, when linked and powered, adds one candidate and reveals genotypes.</summary>
    public ThingDef analyzerFacility;
    /// <summary>Facility ThingDef that, when linked and powered, cuts fuel cost and speeds work.</summary>
    public ThingDef computeFacility;
    public int libraryCapacity;
}

[DefOf]
public static class APG_DefOf
{
    public static ThingDef APG_SeedPacket;
    public static ThingDef APG_GeneAnalyzer;
    public static ThingDef APG_GeneBank;
    public static DesignationDef APG_CollectSeeds;
    public static JobDef APG_CollectSeedsJob;
    public static JobDef APG_OperateStation;

    public static ResearchProjectDef APG_SeedSaving;
    public static ResearchProjectDef APG_PlantBreeding;
    public static ResearchProjectDef APG_Propagation;
    public static ResearchProjectDef APG_GeneticAnalysis;
    public static ResearchProjectDef APG_TissueCulture;
    public static ResearchProjectDef APG_Mutagenesis;
    public static ResearchProjectDef APG_GeneSplicing;
    public static ResearchProjectDef APG_SeedSynthesis;
    public static ResearchProjectDef APG_SyntheticTraits;
    public static ResearchProjectDef APG_GenomeSynthesis;
    public static ResearchProjectDef APG_Phytomining;

    public static PlantGeneDef APG_Perennial;
    public static PlantGeneDef APG_Polyploid;
    public static PlantGeneDef APG_SyntheticChromosome;

    static APG_DefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(APG_DefOf));
    }
}
