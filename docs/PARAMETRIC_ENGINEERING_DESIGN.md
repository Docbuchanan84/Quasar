# Parametric engineering design — interaction and learning pillar

**Decision status:** Approved product/design direction (owner, 2026-11-10).

**Implementation status:** The [Fusion Torch Designer](TORCH_DESIGNER.md)
implements the first component workflow. The reusable cross-domain designer
remains future work.

**Source of truth:** The owner's live Quasar Game Design Record. This public
specification is an actionable handoff, not a competing copy of that record.

## Intent

Make engineering individual components and complex assemblies a substantial,
rewarding part of Quasar across space, land, sea, air, planetary, industrial,
electronic and other domains. Preserve deep, causally connected technical choices
without requiring the player to draw, model or manually place geometry.

**Players set parameters. Quasar generates the technical representations,
calculates consequences, and teaches the underlying concepts.**

This is a broad interaction principle, not a decision that every domain shares
identical equations, components, manufacturing processes or simulation fidelity.

## Player interaction contract — approved

- **Structured inputs:** dropdowns, sliders, number/unit fields, text fields,
  toggles, checkboxes, selectable swatches/palettes, option cards, matrices,
  tables and expandable subsystem groups, chosen by the actual engineering task.
- **Precision:** sliders support rapid exploration while number fields allow
  exact quantities. Show units, ranges, increment sizes, constraints, and how
  variables are coupled. Distinguish an exploratory target value from a
  validated achievable value.
- **No manual art/CAD requirement:** never require freehand drawing, manually
  shaping a mesh, painting an art canvas, or constructing diagrams by hand to
  engineer a component. Detail comes from parameters and decisions.
- **Depth on demand:** reveal subsystem detail and dependencies progressively.
  Provide sensible defaults, comparisons and assistance without removing access
  to fine-grained controls.
- **Phone-first usability:** large touch targets, editable values, responsive
  stacked panels, keyboard accessibility, readable technical labels, and
  deliberate treatment of expensive rendering in the mobile browser.

## Automatically generated, live technical visualization — approved

Parameter changes should produce immediate, meaningful visual feedback where
shape, arrangement, structure, or process can be represented usefully.

An engine nozzle is the reference example: varying throat diameter, expansion
ratio, or length updates a generated longitudinal profile. Its numeric
dimensions, derived metrics, alerts and highlighted features stay synchronized.
Selecting or focusing an illustrated feature can reveal its associated inputs,
explanations, dependencies and engineering effects. This is **not** a manual
drawing or editing surface.

Use the right representation for the domain: cross-sections, silhouettes,
cutaways, schematics, dimension guides, graphs, flow diagrams, animated process
views or parameter-driven 3D previews. The representation must remain functional
without expensive art production or hand-drawn assets.

**Implementation candidates, not approved universal requirements:** SVG
geometry for crisp 2D diagrams, CSS for simple presentation, PixiJS for complex
2D visualizations, Babylon.js/procedural geometry for useful 3D views. Choose per
problem and validate responsiveness, cost, and accessibility; do not lock every
designer to SVG or require 3D.

## Teach during design — approved

For each consequential input or derived measurement, help the player answer:

1. **What is it?** Define the term, units, physical role and relevant context.
2. **What changes, and why?** Explain the direction of effect, dependent
   subsystems, limiting constraints and competing tradeoffs.
3. **What does it mean for the larger system?** Connect the choice to assembly
   behavior, spacecraft/vehicle/facility performance, resources, reliability or
   whatever outcome the domain actually models.

Show concise contextual explanations by default; offer optional deeper
equations, worked examples, visual highlights, warnings, and comparisons.
Educational explanations should arise in the workflow, not only in a detached
tutorial. A numerical preview must not imply that an unmodeled effect has been
verified or that a simplified model predicts real-world hardware precisely.

## Data and simulation boundaries — approved direction

- **C# remains authoritative** for validated design inputs, physical derived
  values, engineering constraints, simulated effects and equipment that can be
  installed/used. Presentation is not a second physics or rules engine.
- The browser submits proposed parameters and renders the result; any
  client-side preview for responsiveness is explicitly non-authoritative and
  reconciles with the canonical C# evaluation.
- One design revision should feed the controls, visualization, explanation,
  validation and downstream simulation consistently. Avoid independently
  hard-coded performance values in artwork or UI.
- Model traceable dependencies: a component change can alter parent mass,
  thermal/electrical/mechanical loads, operating envelope and ultimately
  performance. Only claim relationships the model actually calculates.
- Saving an exploratory, incomplete or invalid draft is desirable; installing
  or certifying an unusable design requires separate validation. Specific
  validity and save-contract rules remain to be designed.

## Illustrative domain use cases, not fixed feature scope

- **Rocket engine:** nozzle/chamber geometry, propellant feed and cooling,
  parameter-linked sections and performance readouts.
- **Airframe or turbine:** stages, blade/profile families, operating envelopes,
  structural and thermal tradeoffs.
- **Naval hull or submarine:** hull form, beam, draft and compartment-driven
  profiles alongside displacement and stability information.
- **Ground vehicle or weapon system:** drivetrain, suspension, armor,
  mechanisms, electrical systems and dependencies.
- **Electronics, power and industry:** configurable circuits, reactors,
  processes, machinery and facilities represented with suitable schematics.

## Original nozzle proof-of-concept proposal

The implemented first slice uses a fusion torch, with three design levers,
a disposable burn comparison, and a campaign refit. The
[designer handoff](DESIGNER_HANDOFF.md) describes its scope and open questions.
The proposal below records the earlier nozzle-focused candidate.

Use one **parameterized nozzle/engine designer** integrated with today's
SpacecraftPropulsion foundation:

1. Edit a small set of mutually related geometric and operational parameters
   through controls, including exact numeric input.
2. Update a generated nozzle SVG profile live, labeling throat/exit dimensions.
3. Show derived values, model assumptions, infeasible combinations, and
   linked educational explanations. Separate geometric facts from approximate
   thermodynamic/flight calculations.
4. Keep revisions and allow comparison; save incomplete work with warnings.
5. Validate at least two distinct engine designs and demonstrate different,
   traceable spacecraft performance when installed in otherwise identical
   Pathfinder test conditions.

**Do not implement the above merely because this direction is approved.**
Specific physics equations, materials models, component tree design, persistence
shape, renderer selection, testing/reliability, manufacturing, in-world design
firms and milestone sequencing remain open to focused approval and testing.
The current `ChemicalEngine` record is a minimal simulation fixture, not the
final engineering-component schema.

## Acceptance principles for future development

- A parameter change that affects represented geometry changes the preview
  without player drawing and without falsifying dimensions.
- Reported calculated values and usable equipment derive from the same
  authoritative design revision.
- Out-of-range or incompatible input produces understandable feedback;
  invalid drafts do not silently become operational hardware.
- A player can discover *what*, *why*, and *system consequences* while working.
- Works on desktop and the actual target iPhone, including touch, keyboard,
  screen-size constraints, slow renders, and canceled/revised calculations.
- A visualization can be turned off without changing the canonical simulation.

Related records: [decisions](DECISIONS.md), [implementation plan](PROJECT_PLAN.md),
[frontend architecture](FRONTEND_ARCHITECTURE.md) and the owner's live shared
Game Design Record (access via [sources](SOURCES.md)).
