using UnityEngine;

namespace Ascend
{
    /// <summary>Counter-style station run by a host (crash, mines, plinko, dice, slots, fishing, coinflip, mystery box):
    /// put chips on BET (or use quick bets) — that is your bet per play; it is re-placed automatically after each play.</summary>
    public abstract class ArcadeStation : TableGame
    {
        protected BetSpot bet;
        public override float TableY { get { return 0.95f; } }
        protected override Vector3 DealerPos { get { return new Vector3(-0.95f, 0, 0.55f); } }
        // fallback only: the chip rack stands on the side table at the stool's left (TableGame.BuildSideTable)
        protected override Vector3 TrayPos { get { return new Vector3(-0.32f, TableY + 0.02f, -0.3f); } }
        protected override Vector3 PanelPos { get { return new Vector3(0.62f, TableY + 0.1f, -0.5f); } }
        protected override BetSpot DefaultSpot { get { return bet; } }
        protected virtual Vector3 BetPos { get { return new Vector3(0.12f, TableY + 0.005f, -0.28f); } }
        protected virtual float CounterWidth { get { return 1.5f; } }
        protected override float[] SeatAngles { get { return new float[0]; } }
        protected override bool SharedPlayerSeat { get { return true; } }

        protected override void BuildTable()
        {
            var k = new MeshKit();
            float w = CounterWidth;
            k.Box(new Vector3(0, TableY * 0.5f, -0.2f), new Vector3(w, TableY, 0.42f), Palette.Hex("120D1C"));
            k.Box(new Vector3(0, TableY + 0.015f, -0.22f), new Vector3(w + 0.06f, 0.03f, 0.5f), Palette.Hex("1B1328"));
            k.Bar(new Vector3(-w * 0.5f - 0.03f, TableY + 0.032f, -0.47f), new Vector3(w * 0.5f + 0.03f, TableY + 0.032f, -0.47f), 0.012f, Accent, 1f);
            k.Bar(new Vector3(-w * 0.5f, 0.05f, -0.42f), new Vector3(w * 0.5f, 0.05f, -0.42f), 0.02f, Accent, 1f);
            k.Ring(BetPos + Vector3.up * 0.026f, 0.085f, 0.095f, 32, Quaternion.identity, Palette.Gold, 1f);
            k.Spawn("Counter", transform, Palette.Glossy, false);
            Txt.Make("BetLabel", transform, BetPos + new Vector3(0, 0.03f, -0.12f), "BET PER PLAY", 0.025f, Palette.Gold, Palette.Display).transform.localRotation = Quaternion.Euler(80, 0, 0);
            bet = BetSpot.Create(this, "BET", 1f, transform, BetPos + Vector3.up * 0.025f, new Vector2(0.18f, 0.18f));
            spots.Add(bet);
            BuildMachine();
        }

        protected abstract void BuildMachine();

        /// <summary>Where chips disappear into the machine.</summary>
        protected virtual Vector3 Intake { get { return W(new Vector3(0, TableY + 0.3f, 0.3f)); } }

        protected long Take() { return ConsumeStake(bet, Intake); }
    }
}
