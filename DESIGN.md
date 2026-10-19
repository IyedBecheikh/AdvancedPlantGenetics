# Advanced Plant Genetics — design

This is how the mod meets the gameplay spec. Code lives in `Source/AdvancedPlantGenetics`, data in `1.6/Defs`.

## 1. Genome model

Every sowable plant (crops, trees, decorative plants) carries a **genome**:

| Part | Content |
|---|---|
| 9 **loci** (`GeneLocusDef`) | two alleles each. An allele has a **level** (−3 … +10) and is **dominant** or **recessive** |
| **Special genes** (`PlantGeneDef`) | 0, 1 or 2 copies. A gene is dominant (one copy expresses it) or recessive (needs two) |
| **Capacity** | how much improvement the genome carries without penalty (base 10, raised by some genes) |

**Expression.** Exactly one dominant allele → that allele is expressed. Both dominant or both recessive → their average.
So a strong recessive allele can hide behind a weak dominant one; that's what genetic analysis uncovers.

**Loci** (symbol, effect per expressed level):

| Locus | Effect |
|---|---|
| G Growth | grow time ÷ (curve: +4 → ×1.35, +6 → ×1.7, +8 → ×2.25, +10 → ×3.0) |
| Y Yield | harvest yield × (curve: +4 → ×1.35, +6 → ×1.75, +8 → ×2.4, +10 → ×3.5) |
| S Fertility response | fertility sensitivity +0.1 per level (signed: + for rich soil/hydroponics, − for poor soil) |
| F Soil tolerance | minimum fertility −0.08 per level |
| L Light efficiency | light needed −0.06 (minimum) / −0.05 (optimum) per level |
| C Cold tolerance | min growth temperatures −3 °C per level |
| H Heat tolerance | max growth temperatures +3 °C per level |
| B Blight resistance | 10 % per level chance to shrug off blight (+10 = immune) |
| V Vigor | lifespan +20 % per level, faster regrowth on regrowing plants |

**Genetic load vs capacity.** Each positive expressed level costs 1 capacity up to +4 and 2 per level above that.
Negative levels on most loci *refund* capacity (max −3). Fertility response costs |level| either way.
Load above capacity is **genetic burden**: −7 % growth and yield per excess point (multiplicative).
This is the specialisation engine: you can't max everything, so a hydroponic crop gives up temperature and soil
tolerance to fund growth, yield and fertility response, while a tundra crop does the opposite. It is a budget,
not a stability meter: genomes never degrade by themselves.

**Inheritance.** Mendelian: offspring take one random allele per locus from each parent, and one copy (or none) of each special gene
from each parent. A homozygous genome breeds true — purebred lines exist naturally. Selfing a hybrid scrambles it.

**Mutation.** Natural: 2 % per locus per seed formation, ±1 level, can't push a locus above +4 (natural cap).
Mutagen chamber: 1–3 targeted-random mutations per candidate, cap +6. Gene editing: exact, cap +8 (library-limited).
Genesis synthesis: exact, cap +10, no library needed. Natural gene mutations (perennial, frost-hardy) are rare;
industrial genes come from the mutagen chamber; spacer/ultratech genes are synthetic only.

## 2. Special genes

| Gene | Tier | Source | Effect |
|---|---|---|---|
| Perennial (recessive) | natural | mutation, wild plants | survives harvest, regrows from 30 % |
| Frost-hardy | natural | mutation | survives frost (goes dormant instead of dying), leafless 20 °C later |
| Polyploid | industrial | colchicine treatment | capacity +6 |
| Bioluminescent | industrial | mutagen | glows (radius 4) — combines with Light efficiency for self-lit gardens |
| Pollution-tolerant | industrial | mutagen | grows on polluted and clean ground |
| Circadian override | spacer | splicing | grows around the clock (no night rest, ≈ ×1.8 growth) |
| Gene compression | spacer | splicing | capacity +6 |
| Runners | spacer | splicing | spreads true-to-type into empty cells of its own zone |
| Toxin scrubber | spacer (Biotech) | splicing | cleans pollution from its cell over time |
| Synthetic chromosome | ultratech | genesis | capacity +12 |
| Chemosynthetic | ultratech | genesis | needs no light at all |
| Deathless | ultratech | genesis | no lifespan; immune to vacuum, darkness and toxic fallout death |
| Ferrophyte / Argentophyte / Aurophyte | ultratech | genesis | harvest also yields steel / silver / gold (phytomining) |

## 3. Seeds and cultivars

- **Seed packet** (`APG_SeedPacket`): one ThingDef for all species; a comp holds species + genome. Stacks only with an identical genome.
  Market value scales with genome quality, so selling premium seed is a viable economy.
- **Wild type** sowing stays exactly like vanilla: no seeds, plants get a natural random genome (landrace).
  Landrace genomes are generated deterministically from the plant's ID and not saved, so wild plants cost nothing in save size.
- **Seed stock** gizmo on growing zones and plant growers: choose *Wild type* or a cultivar. Sowing a cultivar uses one seed
  packet from anywhere on the map and the plant gets that exact genome. No seed hauling.
- **Save seed at harvest** toggle: harvesting a cultivar returns seed that formed by pollination with a neighbour. Off-type seed is rogued
  (discarded). Purebred fields fully replace their seed; hybrid fields don't (like real F1 hybrids) and need propagation.
- **Cultivar registry** (game component): every genome that reaches a seed packet becomes a record with name, lineage (parents),
  generation and origin. Players rename and favourite them in the Genetics tab.

## 4. Eras

| Era | Research | Infrastructure | What it adds |
|---|---|---|---|
| Tribal | Seed saving | — | Collect seeds designation (seed = this plant × neighbour); seed stock per zone; phenotype descriptors; "notable plant" alert |
| Medieval | Plant breeding | Crossing bench | cross or self two packets, see 3+ offspring candidates, keep one; exact expressed values, lineage |
| Medieval | Propagation | Propagation bench | cuttings: 1 packet → 4 identical |
| Industrial | Genetic analysis | Genetic analyzer, gene bank | full genotype (recessives) of analysed strains; analyzer linked to a crossing bench shows candidate genotypes (+1 candidate); gene bank archives strains into the library; culture medium |
| Industrial | Tissue culture | Tissue culture lab | 1 packet → 12 identical (power, culture medium) |
| Industrial | Mutagenesis | Mutagen chamber | induced mutations (cap +6, industrial genes), colchicine treatment (polyploid) |
| Spacer | Gene splicing | Gene splicer + bioinformatics core | set a locus to any level in the allele library (cap +8), splice discovered genes across species; gene reagent |
| Spacer | Seed synthesis | Seed synthesizer | autonomous seed printing from archived strains, "keep N seeds" |
| Spacer | Synthetic traits | — | circadian override, gene compression, runners, toxin scrubber |
| Ultratech | Genome synthesis | Genesis synthesizer (AI persona core) | levels +9/+10, synthetic chromosome, exotic genes; archogenetic catalyst |
| Ultratech | Phytomining | — | metal-producing genes |

Information grows with knowledge: tribal sees words ("vigorous", "frail"), medieval sees numbers and lineage,
industrial sees genotypes of analysed strains, spacer works from a library.

## 5. Integration

- Genetics apply by temporarily swapping the plant's `PlantProperties` values to its phenotype around vanilla methods
  (`TickLong`, `GrowthRate`, `YieldNow`, `PlantCollected`, `CurrentDyingDamagePerTick`, `GetInspectString`, `CheckMakeLeafless`),
  then restoring them. Vanilla growth, light, temperature, fertility, lifespan, season and harvest code all see the
  genetic values, with no reimplementation. Sowing checks (`CanEverPlantAt`, `WorkGiver_GrowerSow.JobOnCell`) use the cultivar assigned to the cell.
- Hydroponics: fertility 2.8 makes Fertility response the key hydroponic trait (S +8 on rice ≈ 4× growth factor vs 2.8×).
- **Progression: Agriculture** (optional): unlocking a crop from a seed bundle also drops heirloom seed packets of it, and a seed packet of a
  locked crop can unlock it (using one packet), since it is seed of that crop just like a bundle.
- Traders sell heirloom seed packets with above-average genomes.
