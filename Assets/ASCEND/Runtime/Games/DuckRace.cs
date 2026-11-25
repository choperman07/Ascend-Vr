using System.Collections;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Duck Race (rules from Roberto's "Gamble With Your Friends" rebuild): five rubber ducks race down a neon
    /// canal. Pick a duck, bet, press RACE: every duck paddles 0.25 – 1 m forward every 0.5 – 1 s along a 6 m course
    /// (shown at 1:5 scale). The first duck over the line wins; the right pick pays 4x.</summary>
    public class DuckRace : ArcadeStation
    {
        const int N = 5;
        const float Course = 6f, Scale = 0.2f;     // the race is run in "real" metres, drawn at 1:5
        const float StartX = -0.6f;
        const double Pays = 4.0;
        static readonly Color[] Colors = { Palette.Hex("FFD91A"), Palette.Hex("E64D33"), Palette.Hex("3399F2"), Palette.Hex("4DCC4D"), Palette.Hex("CC66E6") };
        static readonly string[] Names = { "YELLOW", "RED", "BLUE", "GREEN", "PURPLE" };

        Transform canal;
        readonly Transform[] ducks = new Transform[N];
        readonly PokeButton[] pickBtn = new PokeButton[N];
        PokeButton raceBtn;
        TextMeshPro boardT;
        int pick;
        bool racing, demo;
        /// <summary>Trailer: a race with no chips on it.</summary>
        public void DemoRace() { demo = true; StartCoroutine(Race()); }
        long stake;

        protected override string Rules { get { return "PICK A DUCK, BET, PRESS RACE\nFIVE DUCKS · FIRST OVER THE LINE WINS\nTHE RIGHT DUCK PAYS 4x"; } }
        protected override Vector2 PanelSize { get { return new Vector2(560, 400); } }
        protected override float CounterWidth { get { return 1.7f; } }

        static float LaneZ(int i) { return -0.2f + i * 0.1f; }

        protected override void BuildMachine()
        {
            canal = Build.Empty("Canal", transform, new Vector3(0, 1.14f, 0.36f), Quaternion.Euler(-22f, 0, 0)).transform;
            var k = new MeshKit();
            k.Box(new Vector3(0, -0.03f, 0), new Vector3(1.45f, 0.05f, 0.58f), Palette.Hex("120D1C"), 0f);
            for (int i = 0; i < N; i++)
            {
                k.Box(new Vector3(0, 0f, LaneZ(i)), new Vector3(1.36f, 0.01f, 0.085f), Palette.Hex("1C5C8C"), 0.35f);
                k.Bar(new Vector3(-0.68f, 0.008f, LaneZ(i) - 0.046f), new Vector3(0.68f, 0.008f, LaneZ(i) - 0.046f), 0.004f, Palette.Text, 0.4f);
            }
            float finish = StartX + Course * Scale;
            k.Box(new Vector3(StartX - 0.01f, 0.008f, 0.0f), new Vector3(0.006f, 0.006f, 0.5f), Palette.Text, 0.8f);
            for (int s = 0; s < 10; s++)
                k.Box(new Vector3(finish + 0.02f, 0.008f, -0.225f + s * 0.05f), new Vector3(0.025f, 0.006f, 0.05f), s % 2 == 0 ? Palette.Text : Palette.Hex("111111"), s % 2 == 0 ? 1f : 0f);
            k.Spawn("Lanes", canal, Palette.Neon, false);
            Txt.Make("Finish", canal, new Vector3(finish + 0.02f, 0.012f, 0.3f), "FINISH", 0.035f, Palette.Gold, Palette.Display).transform.localRotation = Quaternion.Euler(70f, 0, 0);

            for (int i = 0; i < N; i++)
            {
                ducks[i] = Build.Empty("Duck" + i, canal, Vector3.zero).transform;
                var d = new MeshKit();
                d.Sphere(new Vector3(0, 0.02f, 0), new Vector3(0.03f, 0.022f, 0.025f), 12, Colors[i], 0.5f);
                d.Sphere(new Vector3(0.022f, 0.048f, 0), 0.016f, 10, Colors[i], 0.5f);
                d.Box(new Vector3(0.04f, 0.045f, 0), new Vector3(0.016f, 0.006f, 0.012f), Palette.Hex("FF9A1A"), 0.8f);
                d.Sphere(new Vector3(0.03f, 0.053f, 0.009f), 0.003f, 6, Palette.Hex("111111"), 0f);
                d.Sphere(new Vector3(0.03f, 0.053f, -0.009f), 0.003f, 6, Palette.Hex("111111"), 0f);
                d.Spawn("Body", ducks[i], Palette.Glossy, false);
                int n = i;
                pickBtn[i] = PokeButton.Create("Pick" + i, transform, new Vector3(-0.66f + i * 0.12f, TableY + 0.035f, -0.36f), Quaternion.identity, 0.032f, Colors[i], (i + 1).ToString(), () => Pick(n), 0.05f);
            }
            ResetDucks();

            boardT = Txt.Glow("Board", transform, new Vector3(0, 1.62f, 0.62f), "", 0.09f, Palette.Gold);
            raceBtn = PokeButton.Create("Race", transform, new Vector3(0.6f, TableY + 0.035f, -0.36f), Quaternion.identity, 0.045f, Palette.Gold, "RACE", () => StartCoroutine(Race()), 0.05f);
            Pick(0);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            for (int i = 0; i < N; i++)
            {
                int n = i;
                UIKit.Btn(p, (i + 1).ToString(), 20 + i * 104, y, 96, 70, Colors[i], () => Pick(n), 32);
            }
            UIKit.Btn(p, "RACE", 20, y + 80, 520, 90, Palette.Gold, () => StartCoroutine(Race()), 40);
        }

        void ResetDucks()
        {
            for (int i = 0; i < N; i++) ducks[i].localPosition = new Vector3(StartX, 0.006f, LaneZ(i));
        }

        void Pick(int i)
        {
            if (racing) { Sfx.UI("ui_error", 0.3f); return; }
            pick = i;
            boardT.text = "YOUR DUCK: <color=#" + ColorUtility.ToHtmlStringRGB(Colors[i]) + ">" + Names[i] + "</color> · PAYS 4x";
            Sfx.UI("ui_click", 0.4f);
        }

        IEnumerator Race()
        {
            if (racing) yield break;
            stake = demo ? 100 : Take();
            if (stake <= 0) { Status("PLACE A BET ON THE CIRCLE", Palette.Gold); yield break; }
            racing = true; raceBtn.SetInteractable(false);
            foreach (var b in pickBtn) b.SetInteractable(false);
            ResetDucks();
            Status("AND THEY'RE OFF!", Palette.Teal);
            Say("good_luck");
            Sfx.UI("whistle", 0.5f);

            // every duck paddles at a cruising speed that changes every 0.5 – 1 s (the same 0.25 – 1 m per stroke as
            // before, so the odds are untouched), but the speed eases from one stroke to the next: no hops, no stops
            var pos = new float[N]; var vel = new float[N]; var want = new float[N]; var next = new float[N]; var wob = new float[N];
            for (int i = 0; i < N; i++) { next[i] = 0f; wob[i] = Random.value * 6f; }
            int winner = -1; float clock = 0f;
            while (winner < 0)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.05f); clock += dt;
                float lead = 0f;
                for (int i = 0; i < N; i++)
                {
                    if (clock >= next[i])
                    {
                        float every = 0.5f + (float)Odds.R() * 0.5f;
                        want[i] = (float)(0.25 + Odds.R() * 0.75) / every;
                        next[i] = clock + every;
                    }
                    vel[i] = Mathf.Lerp(vel[i], want[i], 1f - Mathf.Exp(-5f * dt));
                    pos[i] += vel[i] * dt;
                    lead = Mathf.Max(lead, pos[i]);
                    float bob = Mathf.Sin(clock * (5f + vel[i] * 3f) + wob[i]);
                    ducks[i].localPosition = new Vector3(StartX + Mathf.Min(pos[i], Course + 0.25f) * Scale, 0.006f + (bob * 0.5f + 0.5f) * 0.004f, LaneZ(i) + Mathf.Sin(clock * 1.7f + wob[i]) * 0.006f);
                    // nose down a touch when sprinting, a gentle side-to-side waddle
                    ducks[i].localRotation = Quaternion.Euler(Mathf.Sin(clock * 6f + wob[i]) * 7f, Mathf.Sin(clock * 1.7f + wob[i]) * 6f, -Mathf.Clamp(vel[i] - 0.8f, -0.5f, 1f) * 9f + bob * 3f);
                }
                // first over the line wins (photo finish: whoever is further)
                if (lead >= Course) { float best = 0f; for (int i = 0; i < N; i++) if (pos[i] > best) { best = pos[i]; winner = i; } }
                yield return null;
            }
            bool won = winner == pick;
            boardT.text = "WINNER: <color=#" + ColorUtility.ToHtmlStringRGB(Colors[winner]) + ">" + Names[winner] + "</color>";
            Status(won ? "YOUR DUCK WINS! 4x · " + ((long)(stake * Pays)).ToString("N0") : Names[winner] + " WINS · STAKE LOST", won ? Palette.Mint : Palette.Muted);
            if (won) Say("cashout");
            if (!demo) PayDirect(won ? (long)(stake * Pays) : 0, stake, ducks[winner].position);
            else if (won) Juice.Sparks(ducks[winner].position, Palette.Gold, 30);
            bool wasDemo = demo;
            demo = false;
            stake = 0;
            // trailer: hold the photo finish on screen until the shot is over, so the ducks don't vanish mid-shot
            yield return new WaitForSeconds(wasDemo ? 6f : 2f);
            ResetDucks();
            racing = false; raceBtn.SetInteractable(true);
            foreach (var b in pickBtn) b.SetInteractable(true);
            Pick(pick);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (canal == null || !GameState.Exists) return;
            if (racing && stake > 0)
            {
                GameState.I.Pay(stake, stake, transform.position);
                GameState.I.Notify("DUCK RACE · RACE VOID · " + stake.ToString("N0") + " RETURNED", Palette.Gold);
            }
            stake = 0; racing = false;
            ResetDucks();
            if (raceBtn != null) raceBtn.SetInteractable(true);
            foreach (var b in pickBtn) if (b != null) b.SetInteractable(true);
        }
    }
}
