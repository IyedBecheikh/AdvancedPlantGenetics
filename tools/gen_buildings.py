# Generates 1.6/Defs/Buildings_PlantGenetics.xml from a compact table so the station defs stay consistent.
# Run: python -I tools/gen_buildings.py   (from the repo root)
import os

B = [
    dict(d="APG_CrossingBench", l="crossing bench", size=(2, 1), r="APG_PlantBreeding",
         desc="A potting bench with brushes, pollen bags and labelled pots for controlled crosses. Cross two strains or self-pollinate one, then pick which offspring to keep. A linked genetic analyzer shows the offspring's genotypes and raises one more candidate.",
         cost={"WoodLog": 75, "Steel": 20}, ops=["APG_Op_Cross", "APG_Op_Self"], lab=False, analyzer=True, tech="Medieval", work=2200, hp=120),
    dict(d="APG_PropagationBench", l="propagation bench", size=(2, 1), r="APG_Propagation",
         desc="Trays of rooting medium under a cloth cover. Grows cuttings into identical seed: one packet becomes four.",
         cost={"WoodLog": 80, "Cloth": 10}, ops=["APG_Op_Cuttings"], lab=False, tech="Medieval", work=1800, hp=100),
    dict(d="APG_GeneAnalyzer", l="genetic analyzer", size=(1, 1), r="APG_GeneticAnalysis",
         desc="Sequences plant genomes, revealing hidden recessive alleles and carried genes. Also a facility: linked to a crossing bench or mutagen chamber, it shows the offspring's genotypes and adds one more candidate. Uses culture medium.",
         cost={"Steel": 100, "ComponentIndustrial": 4}, power=250, fuel=("APG_CultureMedium", 10), ops=["APG_Op_Analyze"], lab=True, facility=True, tech="Industrial", work=5000, hp=150),
    dict(d="APG_GeneBank", l="gene bank", size=(1, 1), r="APG_GeneticAnalysis",
         desc="Cryogenic sample racks and an index. Archives analysed strains for the seed synthesizer. Each powered gene bank holds 20 strains.",
         cost={"Steel": 90, "ComponentIndustrial": 3}, power=150, ops=["APG_Op_Archive"], lab=True, library=20, tech="Industrial", work=4000, hp=150),
    dict(d="APG_TissueCultureLab", l="tissue culture lab", size=(2, 2), r="APG_TissueCulture",
         desc="A laminar-flow hood, autoclave and growth shelves for micropropagation. One packet becomes twelve identical packets. Uses culture medium.",
         cost={"Steel": 150, "ComponentIndustrial": 5}, power=350, fuel=("APG_CultureMedium", 20), ops=["APG_Op_Micropropagate"], lab=True, tech="Industrial", work=7000, hp=200),
    dict(d="APG_MutagenChamber", l="mutagen chamber", size=(2, 2), r="APG_Mutagenesis",
         desc="A shielded irradiation chamber with chemical baths. Produces mutant offspring to choose from and doubles chromosome sets with colchicine. Uses culture medium. Link a genetic analyzer to raise one more candidate.",
         cost={"Steel": 200, "ComponentIndustrial": 6, "Uranium": 25}, power=600, fuel=("APG_CultureMedium", 20), ops=["APG_Op_Mutate", "APG_Op_Colchicine"], lab=True, analyzer=True, tech="Industrial", work=9000, hp=250),
    dict(d="APG_GeneSplicer", l="gene splicer", size=(2, 2), r="APG_GeneSplicing",
         desc="Precision genome editor. Sets a locus to any level in the allele library (up to +8) and splices known or synthetic genes across species. Uses gene reagent. A linked bioinformatics core cuts reagent use and speeds work.",
         cost={"Steel": 120, "Plasteel": 80, "ComponentSpacer": 4}, power=1200, fuel=("APG_GeneReagent", 20), ops=["APG_Op_EditLocus", "APG_Op_Splice", "APG_Op_RemoveGene"], lab=True, compute=True, tech="Spacer", work=14000, hp=250),
    dict(d="APG_BioinformaticsCore", l="bioinformatics core", size=(1, 1), r="APG_GeneSplicing",
         desc="A rack of sequence-folding processors. Facility: linked to a gene splicer, seed synthesizer or genesis synthesizer, it cuts reagent use by 25% and speeds work by 35%.",
         cost={"Steel": 80, "Gold": 20, "ComponentSpacer": 2}, power=400, ops=[], computeFacility=True, tech="Spacer", work=6000, hp=150),
    dict(d="APG_SeedSynthesizer", l="seed synthesizer", size=(2, 2), r="APG_SeedSynthesis",
         desc="Prints viable seed of archived strains from gene reagent. Runs on its own, with no colonist, and can keep a chosen stock of a strain topped up. Needs a powered gene bank.",
         cost={"Steel": 150, "Plasteel": 100, "ComponentSpacer": 5}, power=800, fuel=("APG_GeneReagent", 20), ops=["APG_Op_Synthesize"], lab=True, autonomous=True, compute=True, tech="Spacer", work=14000, hp=250),
    dict(d="APG_GenesisSynthesizer", l="genesis synthesizer", size=(3, 3), r="APG_GenomeSynthesis",
         desc="An ultratech genome foundry built around a persona core. Writes alleles up to +10 with no library, installs synthetic chromosomes and splices ultratech genes. Runs on archogenetic catalyst. A linked bioinformatics core cuts catalyst use and speeds work.",
         cost={"Steel": 200, "Plasteel": 300, "Gold": 100, "Uranium": 100, "ComponentSpacer": 12, "AIPersonaCore": 1}, power=3000, fuel=("APG_ArchogeneticCatalyst", 10), ops=["APG_Op_GenesisEdit", "APG_Op_GenesisSplice", "APG_Op_SynthChromosome"], lab=True, compute=True, tech="Ultra", work=40000, hp=500),
]

FUEL_LABEL = {"APG_CultureMedium": "culture medium", "APG_GeneReagent": "gene reagent", "APG_ArchogeneticCatalyst": "catalyst"}
SKILL = {"Medieval": 3, "Industrial": 5, "Spacer": 8, "Ultra": 10}


def building(b):
    w, h = b["size"]
    tex = b["d"][4:]
    station = bool(b["ops"])
    off = -2 if h == 3 else -1
    # Buildings overhang their footprint a little, like vanilla (multi-analyzer: 2x2 drawn at 2.47).
    k = 1.2 if w >= 3 else 1.3
    o = ['  <ThingDef ParentName="BuildingBase">',
         f'    <defName>{b["d"]}</defName>',
         f'    <label>{b["l"]}</label>',
         f'    <description>{b["desc"]}</description>']
    if station:
        o.append('    <thingClass>AdvancedPlantGenetics.Building_GeneticsStation</thingClass>')
    o += ['    <graphicData>',
          f'      <texPath>Things/Building/APG/{tex}</texPath>',
          '      <graphicClass>Graphic_Single</graphicClass>',
          f'      <drawSize>({w * k:.2f},{h * k:.2f})</drawSize>',
          f'      <shadowData><volume>({w * 0.85:.1f}, 0.5, {h * 0.8:.1f})</volume></shadowData>',
          '    </graphicData>',
          f'    <uiIconPath>Things/Building/APG/{tex}</uiIconPath>',
          f'    <size>({w},{h})</size>',
          '    <rotatable>false</rotatable>',
          '    <altitudeLayer>Building</altitudeLayer>',
          '    <passability>PassThroughOnly</passability>',
          '    <pathCost>50</pathCost>',
          '    <fillPercent>0.5</fillPercent>',
          '    <castEdgeShadows>true</castEdgeShadows>',
          '    <canOverlapZones>false</canOverlapZones>',
          '    <designationCategory>APG_PlantGenetics</designationCategory>',
          '    <minifiedDef>MinifiedThing</minifiedDef>',
          '    <thingCategories><li>BuildingsProduction</li></thingCategories>',
          f'    <techLevel>{b["tech"]}</techLevel>']
    if station:
        o += ['    <hasInteractionCell>true</hasInteractionCell>',
              f'    <interactionCellOffset>(0,0,{off})</interactionCellOffset>']
    o.append('    <terrainAffordanceNeeded>Medium</terrainAffordanceNeeded>')
    if b.get("autonomous"):
        o.append('    <tickerType>Normal</tickerType>')
    o += ['    <statBases>',
          f'      <MaxHitPoints>{b["hp"]}</MaxHitPoints>',
          f'      <WorkToBuild>{b["work"]}</WorkToBuild>',
          f'      <Mass>{15 * w * h}</Mass>',
          f'      <Flammability>{"1.0" if b.get("stuff") else "0.5"}</Flammability>',
          '    </statBases>']
    if b.get("stuff"):
        o += [f'    <stuffCategories><li>{b["stuff"][0]}</li></stuffCategories>',
              f'    <costStuffCount>{b["stuff"][1]}</costStuffCount>']
    if b["cost"]:
        o.append('    <costList>')
        o += [f'      <{k}>{v}</{k}>' for k, v in b["cost"].items()]
        o.append('    </costList>')
    o += [f'    <researchPrerequisites><li>{b["r"]}</li></researchPrerequisites>',
          f'    <constructionSkillPrerequisite>{SKILL[b["tech"]]}</constructionSkillPrerequisite>',
          '    <comps>']
    if b.get("power"):
        o += ['      <li Class="CompProperties_Power">',
              '        <compClass>CompPowerTrader</compClass>',
              f'        <basePowerConsumption>{b["power"]}</basePowerConsumption>',
              '        <shortCircuitInRain>true</shortCircuitInRain>',
              '      </li>',
              '      <li Class="CompProperties_Flickable"/>']
    if b.get("fuel"):
        f, cap = b["fuel"]
        o += ['      <li Class="CompProperties_Refuelable">',
              f'        <fuelCapacity>{cap}</fuelCapacity>',
              f'        <initialConfigurableTargetFuelLevel>{cap}</initialConfigurableTargetFuelLevel>',
              '        <fuelConsumptionRate>0</fuelConsumptionRate>',
              '        <consumeFuelOnlyWhenUsed>true</consumeFuelOnlyWhenUsed>',
              '        <targetFuelLevelConfigurable>true</targetFuelLevelConfigurable>',
              '        <showAllowAutoRefuelToggle>true</showAllowAutoRefuelToggle>',
              f'        <fuelFilter><thingDefs><li>{f}</li></thingDefs></fuelFilter>',
              f'        <fuelLabel>{FUEL_LABEL[f]}</fuelLabel>',
              f'        <fuelGizmoLabel>{FUEL_LABEL[f].capitalize()}</fuelGizmoLabel>',
              '        <drawOutOfFuelOverlay>false</drawOutOfFuelOverlay>',
              '        <drawFuelGaugeInMap>false</drawFuelGaugeInMap>',
              '      </li>']
    links = (["APG_GeneAnalyzer"] if b.get("analyzer") else []) + (["APG_BioinformaticsCore"] if b.get("compute") else [])
    if links:
        o += ['      <li Class="CompProperties_AffectedByFacilities">',
              '        <linkableFacilities>' + "".join(f"<li>{x}</li>" for x in links) + '</linkableFacilities>',
              '      </li>']
    if b.get("facility") or b.get("computeFacility"):
        o += ['      <li Class="CompProperties_Facility">',
              '        <maxSimultaneous>1</maxSimultaneous>',
              '        <maxDistance>8</maxDistance>',
              '      </li>']
    o.append('    </comps>')
    if b.get("facility") or b.get("computeFacility") or links:
        o.append('    <placeWorkers><li>PlaceWorker_ShowFacilitiesConnections</li></placeWorkers>')
    if station:
        o += ['    <modExtensions>',
              '      <li Class="AdvancedPlantGenetics.StationExtension">',
              '        <operations>' + "".join(f"<li>{x}</li>" for x in b["ops"]) + '</operations>',
              f'        <labWork>{str(b["lab"]).lower()}</labWork>']
        if b.get("autonomous"):
            o.append('        <autonomous>true</autonomous>')
        if b.get("analyzer"):
            o.append('        <analyzerFacility>APG_GeneAnalyzer</analyzerFacility>')
        if b.get("compute"):
            o.append('        <computeFacility>APG_BioinformaticsCore</computeFacility>')
        if b.get("library"):
            o.append(f'        <libraryCapacity>{b["library"]}</libraryCapacity>')
        o += ['      </li>', '    </modExtensions>']
    o.append('  </ThingDef>')
    return "\n".join(o)


HEADER = """<?xml version="1.0" encoding="utf-8"?>
<!-- Generated by tools/gen_buildings.py: edit the table there, not this file. -->
<Defs>
  <DesignationCategoryDef>
    <defName>APG_PlantGenetics</defName>
    <label>genetics</label>
    <description>Plant breeding and genetic engineering.</description>
    <order>550</order>
    <specialDesignatorClasses>
      <li>Designator_Cancel</li>
      <li>Designator_Deconstruct</li>
    </specialDesignatorClasses>
  </DesignationCategoryDef>
"""

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
path = os.path.join(root, "1.6", "Defs", "Buildings_PlantGenetics.xml")
with open(path, "w", encoding="utf-8", newline="\n") as f:
    f.write(HEADER + "\n" + "\n\n".join(building(b) for b in B) + "\n</Defs>\n")
print("wrote", path)
