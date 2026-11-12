# Quasar designer handoff

## Try the current slice

Open **Torch Designer** from the top navigation. Start with the stock drive,
then try a **2 m nozzle**, **240 GW jet power**, and **800 km/s exhaust speed**.
The draft should show **600 kN** of thrust. In **Run test burn**, stock uses
**9 kg** and the draft uses **22.5 kg** of reaction mass in the same 30 seconds.
Try **240 GW** with the stock **1.5 m** nozzle to see how the page explains
the limit and how to recover.

**Fit to Pathfinder** installs the draft on the active ship while paused and
coasting. It keeps the tank and fuel. The separate **Debug refuel** control
fills the installed tank for testing. Return to **System Lab** to plot a
course with the fitted drive. Continuous advances through burn and gravity
notices and pauses on arrival. An opted-in iPhone can show that arrival on
its Lock Screen.

## Design direction

- Teach each control where the player uses it. Explain the part, the units,
  the effect of changing it, and what that does to the ship. Use concrete
  comparisons with the stock drive and explain how to recover from an invalid
  combination.
- Keep the designer phone-friendly. Provide sliders for exploration, exact
  fields for precise inputs, and a generated diagram that follows the
  parameters. Test burns use identical starting conditions. Campaign changes
  have an explicit confirmation.
- C# owns validation and the values used in flight. The torch currently
  assumes supplied power. A reactor is a separate future component that
  can serve multiple ship systems.

## What to review next

Walk through the invalid nozzle case, a test burn, and the fit confirmation on
an iPhone in portrait. Flag where you have to scroll back to see the effect of
a control, where a term needs an explanation, and whether the difference
between a disposable test and a campaign fit is clear.

For the next design discussion, sketch the information a drive would need
from a future ship power system. Consider what the player should see when
power is shared or insufficient. Reactor types, power allocation, cooling,
and saving reusable component designs are open design questions.

See the [Torch Designer guide](TORCH_DESIGNER.md), the
[approved engineering direction](PARAMETRIC_ENGINEERING_DESIGN.md), and the
[project plan](PROJECT_PLAN.md).
