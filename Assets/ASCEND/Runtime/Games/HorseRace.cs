using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Neon Derby: six cyber-horses race around a tabletop holo-track. Every race posts new odds; each horse
    /// pays 0.95 ÷ its win chance (RTP 95% on every horse). The winner is drawn from those chances, then the race is
    /// run with real overtakes and a photo-finish camera flash.</summary>
    public class HorseRace : TableGame
    {
        const int N = 6;
        static readonly string[] Names = { "NEON NOVA", "GLITCH", "BYTE RUNNER", "LUCKY HEX", "RONIN RUSH", "MOONSHOT" };
        static readonly Color[] Colors = { Palette.Teal, Palette.Magenta, Palette.Gold, Palette.Mint, Palette.Violet, Palette.Coral };
        static readonly float[] Chances = { 0.30f, 0.22f, 0.17f, 0.13f, 0.10f, 0.08f };
        readonly float[] chance = new float[N];
        readonly BetSpot[] bets = new BetSpot[N];
        readonly TextMeshPro[] oddsT = new TextMeshPro[N];
        readonly Transform[] horse = new Transform[N];
        readonly Transform[][] legs = new Transform[N][];
        Transform track;
        PokeButton raceBtn;
        TextMeshPro callT;
        const float A = 1.25f, B = 0.45f;   // oval half-length / half-width (lane 0)
        const float LaneW = 0.045f;          // 7 lane lines fit on the 3.15 × 1.45 m plinth (0.06 hung the outer lanes off it)

        public override bool AutoHidePanel { get { return true; } }
        protected override string Rules { get { return "PICK A HORSE (OR SEVERAL) · PRESS RACE\nPAYOUTS CHANGE EVERY RACE\nPAYOUT SHOWN ON EACH LANE"; } }
        protected override Vector3 DealerPos { get { return new Vector3(1.75f, 0, 0.5f); } }
        protected override Vector3 TrayPos { get { return new Vector3(-0.6f, TableY + 0.02f, -0.58f); } }
        protected override Vector3 PanelPos { get { return new Vector3(0.6f, TableY + 0.1f, -0.72f); } }
        protected override Vector3 StatusPos { get { return new Vector3(0, 2.25f, 0.9f); } }
        protected override BetSpot DefaultSpot { get { int best = 0; for (int i = 1; i < N; i++) if (chance[i] > chance[best]) best = i; return bets[best]; } }
        protected override List<Vector4> SeatSpots { get { return new List<Vector4> { new Vector4(-1.4f, 0, -1.05f, 15f), new Vector4(-0.7f, 0, -1.2f, 5f), new Vector4(0.75f, 0, -1.2f, -5f), new Vector4(1.45f, 0, -1.05f, -15f) }; } }

        /// <summary>Lane lines are at whole lanes; horse i runs in the middle of its lane (i + 0.5).</summary>
        Vector3 Oval(float t, float lane)
        {
            float a = A + lane * LaneW, b = B + lane * LaneW;
            // stadium shape: straights + semicircles, parameter t in [0,1)
            float straight = 2f * (a - b), arc = Mathf.PI * b, per = 2f * straight + 2f * arc;
            float d = Mathf.Repeat(t, 1f) * per;
            if (d < straight) return new Vector3(-(a - b) + d, 0, -b);
            d -= straight;
            if (d < arc) { float ang = -Mathf.PI / 2f + d / b; return new Vector3((a - b) + Mathf.Cos(ang) * b, 0, Mathf.Sin(ang) * b); }
            d -= arc;
            if (d < straight) return new Vector3((a - b) - d, 0, b);
            d -= straight;
            { float ang = Mathf.PI / 2f + d / b; return new Vector3(-(a - b) + Mathf.Cos(ang) * b, 0, Mathf.Sin(ang) * b); }
        }

        protected override void BuildTable()
        {
            // betting counter with six lanes of bet spots
            var body = new MeshKit { boxUV = 1.4f };
            body.Box(new Vector3(0, TableY * 0.5f - 0.03f, -0.2f), new Vector3(2.6f, TableY - 0.06f, 0.6f), Color.white, 0f);
            body.Spawn("Counter", transform, Palette.Wood);
            var top = new MeshKit();
            top.Box(new Vector3(0, TableY - 0.02f, -0.2f), new Vector3(2.66f, 0.04f, 0.66f), Palette.Hex("100C1A"), 0f);
            top.Bar(new Vector3(-1.33f, TableY, -0.53f), new Vector3(1.33f, TableY, -0.53f), 0.03f, Palette.Gold, 0.9f);
            top.Bar(new Vector3(-1.3f, 0.06f, -0.5f), new Vector3(1.3f, 0.06f, -0.5f), 0.02f, Accent, 1f);
            top.Spawn("Top", transform, Palette.Lacquer, false);
            var col = gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0, 0.7f, 0.35f); col.size = new Vector3(3.2f, 1.4f, 1.8f);
            for (int i = 0; i < N; i++)
            {
                float x = -1.05f + i * 0.42f;
                var tk = new MeshKit();
                tk.Box(new Vector3(x, TableY + 0.003f, -0.2f), new Vector3(0.38f, 0.006f, 0.5f), Palette.Hex("0A0812"), 0f);
                tk.Ring(new Vector3(x, TableY + 0.008f, -0.25f), 0.1f, 0.112f, 28, Quaternion.identity, Colors[i], 1f);
                tk.Spawn("Lane" + i, transform, Palette.Neon, false);
                Txt.Make("Name" + i, transform, new Vector3(x, TableY + 0.01f, -0.05f), Names[i], 0.032f, Colors[i], Palette.Display).transform.localRotation = Quaternion.Euler(80, 0, 0);
                oddsT[i] = Txt.Make("Odds" + i, transform, new Vector3(x, TableY + 0.01f, -0.4f), "", 0.03f, Palette.Text, Palette.Mono);
                oddsT[i].transform.localRotation = Quaternion.Euler(80, 0, 0);
                bets[i] = BetSpot.Create(this, Names[i], 2f, transform, new Vector3(x, TableY + 0.006f, -0.25f), new Vector2(0.3f, 0.3f));
                spots.Add(bets[i]);
            }
            // the holo-track on a plinth behind the counter
            track = Build.Empty("Track", transform, new Vector3(0, 1.05f, 0.75f)).transform;
            var pl = new MeshKit();
            pl.Box(new Vector3(0, -0.55f, 0), new Vector3(3.1f, 1.0f, 1.4f), Palette.Hex("0E0A16"), 0f);
            pl.Box(new Vector3(0, -0.03f, 0), new Vector3(3.15f, 0.06f, 1.45f), Palette.Hex("17112A"), 0f);
            pl.Spawn("Plinth", track, Palette.Lacquer, false);
            var tk2 = new MeshKit();
            for (int lane = 0; lane <= N; lane++)
            {
                Vector3 prev = Oval(0, lane) + Vector3.up * 0.005f - new Vector3(0, 0, 0.03f) * 0;
                for (int s = 1; s <= 96; s++)
                {
                    var p = Oval(s / 96f, lane) + Vector3.up * 0.005f;
                    var off = (lane - 0.5f) * 0f;
                    tk2.Bar(prev + new Vector3(0, 0, 0), p, lane == 0 || lane == N ? 0.008f : 0.003f, lane == 0 || lane == N ? Palette.Teal : Palette.Violet, lane == 0 || lane == N ? 1f : 0.5f);
                    prev = p;
                }
            }
            var fl = Oval(0.0f, 0); var fl2 = Oval(0.0f, N);
            tk2.Bar(fl + Vector3.up * 0.006f, fl2 + Vector3.up * 0.006f, 0.012f, Palette.Text, 1f);   // finish line
            tk2.Spawn("Lanes", track, Palette.Neon, false);
            var grass = new MeshKit();
            grass.Box(new Vector3(0, 0.002f, 0), new Vector3(2.6f, 0.004f, 0.75f), Palette.Hex("0D2A22"), 0.15f);
            grass.Spawn("Infield", track, Palette.Neon, false);
            Txt.Glow("Logo", track, new Vector3(0, 0.08f, 0), "NEON DERBY", 0.16f, Palette.Gold).transform.localRotation = Quaternion.Euler(60, 0, 0);
            for (int i = 0; i < N; i++) horse[i] = BuildHorse(i);
            callT = Txt.Glow("Call", transform, new Vector3(0, 1.85f, 0.95f), "", 0.09f, Palette.Text);
            raceBtn = PokeButton.Create("Race", transform, new Vector3(0.95f, TableY + 0.035f, -0.48f), Quaternion.identity, 0.05f, Palette.Gold, "RACE", () => StartCoroutine(Race()));
            NewOdds();
            Place(0f);
        }

        Transform BuildHorse(int i)
        {
            var root = Build.Empty("Horse" + i, track, Vector3.zero).transform;
            var k = new MeshKit();
            var c = Colors[i];
            k.Capsule(new Vector3(-0.045f, 0.075f, 0), new Vector3(0.045f, 0.075f, 0), 0.028f, 10, Palette.Hex("1A1428"), 0.1f);   // body along +X
            k.Capsule(new Vector3(0.05f, 0.08f, 0), new Vector3(0.075f, 0.12f, 0), 0.014f, 8, Palette.Hex("1A1428"), 0.1f);       // neck
            k.Box(new Vector3(0.085f, 0.125f, 0), new Vector3(0.04f, 0.022f, 0.022f), Palette.Hex("1A1428"), 0.1f);             // head
            k.Bar(new Vector3(-0.05f, 0.1f, 0), new Vector3(0.06f, 0.1f, 0), 0.006f, c, 1f);                                       // neon stripe
            k.Sphere(new Vector3(0.098f, 0.13f, 0.012f), 0.005f, 6, c, 1f);
            k.Box(new Vector3(-0.01f, 0.108f, 0), new Vector3(0.03f, 0.008f, 0.04f), c, 0.8f);                                   // saddle
            k.Spawn("Body", root, Palette.Glossy, false);
            Txt.Make("Num", root, new Vector3(0, 0.15f, 0), (i + 1).ToString(), 0.04f, c, Palette.Display).gameObject.AddComponent<Billboard>();
            legs[i] = new Transform[4];
            for (int l = 0; l < 4; l++)
            {
                var lk = new MeshKit(); lk.Bar(Vector3.zero, new Vector3(0, -0.06f, 0), 0.007f, c, 0.7f);
                legs[i][l] = lk.Spawn("Leg" + l, root, new Vector3(l < 2 ? 0.035f : -0.035f, 0.06f, l % 2 == 0 ? 0.012f : -0.012f), Quaternion.identity, Palette.Neon, false).transform;
            }
            return root;
        }

        void NewOdds()
        {
            var shuffled = new List<float>(Chances);
            for (int i = shuffled.Count - 1; i > 0; i--) { int j = Odds.Range(0, i + 1); var t = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = t; }
            for (int i = 0; i < N; i++)
            {
                chance[i] = shuffled[i];
                // tiny epsilon so float rounding never pays a chip less (0.95f / 0.1f = 9.4999...); the label is truncated, so
                // the printed payout is never more than what is paid
                bets[i].pays = 0.95f / chance[i] + 1e-5f;
                oddsT[i].text = "PAYS " + PaysLabel(i) + "x";
            }
        }

        string PaysLabel(int i) { return (System.Math.Floor(bets[i].pays * 100.0) / 100.0).ToString("0.00"); }

        void Place(float t0)
        {
            for (int i = 0; i < N; i++)
            {
                var p = Oval(t0, i + 0.5f);
                horse[i].localPosition = p;
                var dir = Oval(t0 + 0.003f, i + 0.5f) - p;
                if (dir.sqrMagnitude > 1e-6f) horse[i].localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0, -90, 0);
            }
        }

        Vector3 LaneOffset(int lane, float t) { return Vector3.zero; }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            for (int i = 0; i < N; i++)
            {
                int k = i;
                UIKit.Btn(p, "#" + (i + 1), 20 + (i % 3) * 176, y + (i / 3) * 62, 168, 56, Colors[i], () => bets[k].AddValue(minBet, W(TrayPos) + Vector3.up * 0.05f), 26);
            }
            UIKit.Btn(p, "RACE", 20, y + 130, 520, 76, Palette.Gold, () => StartCoroutine(Race()), 38);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 430); } }

        int PickWinner()
        {
            double r = Odds.R(), acc = 0;
            for (int i = 0; i < N; i++) { acc += chance[i]; if (r < acc) return i; }
            return N - 1;
        }

        IEnumerator Race()
        {
            if (!BeginRound()) yield break;
            raceBtn.SetInteractable(false);
            int winner = PickWinner();
            // finishing order: winner first, others shuffled
            var order = new List<int>(); for (int i = 0; i < N; i++) if (i != winner) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--) { int j = Odds.Range(0, i + 1); var t = order[i]; order[i] = order[j]; order[j] = t; }
            order.Insert(0, winner);
            var finish = new float[N];
            for (int rank = 0; rank < N; rank++) finish[order[rank]] = 11.5f + rank * Random.Range(0.25f, 0.55f) + (rank == 1 ? Random.Range(0.02f, 0.12f) : 0f);
            Status("AND THEY'RE OFF!", Palette.Gold);
            Say("derby_off");
            Sfx.Play("whistle", track.position, 0.8f);
            var crowd = Sfx.Loop("casino_ambience_loop", track, 0.0f);
            // each horse gets a wavy pace curve; the integral reaches 1 lap exactly at its finish time
            var phase = new float[N]; var amp = new float[N];
            for (int i = 0; i < N; i++) { phase[i] = Random.value * 6f; amp[i] = Random.Range(0.04f, 0.09f); }
            float T = 0f, end = finish[order[N - 1]] + 0.3f;
            var pos = new float[N];
            bool stretchCalled = false;
            while (T < end)
            {
                T += Time.deltaTime;
                if (!stretchCalled && T > finish[winner] * 0.72f) { stretchCalled = true; Say(finish[order[1]] - finish[winner] < 0.35f ? "derby_photo" : "derby_stretch"); }
                int leader = 0;
                for (int i = 0; i < N; i++)
                {
                    float k = Mathf.Clamp01(T / finish[i]);
                    float wobble = Mathf.Sin(k * Mathf.PI * 3f + phase[i]) * amp[i] * Mathf.Sin(k * Mathf.PI);
                    // starts from rest but still runs at speed through the line (a smoothstep brought every horse to a crawl
                    // at the post, bunching the field into what looked like a dead heat with the clamp)
                    pos[i] = k >= 1f ? 1f : Mathf.Clamp(k * k * (2f - k) + wobble * (1f - k) * (1f - k), 0f, 0.995f);
                    if (pos[i] > pos[leader]) leader = i;
                    var p = Oval(pos[i], i + 0.5f);
                    horse[i].localPosition = p + Vector3.up * Mathf.Abs(Mathf.Sin(T * 18f + i)) * 0.01f * (k < 1f ? 1f : 0f);
                    var ahead = Oval(pos[i] + 0.003f, i + 0.5f);
                    var dir = ahead - p; if (dir.sqrMagnitude > 1e-6f) horse[i].localRotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir).normalized * 0f + dir.normalized, Vector3.up) * Quaternion.Euler(0, -90, 0);
                    for (int l = 0; l < 4; l++) legs[i][l].localRotation = Quaternion.Euler(0, 0, k < 1f ? Mathf.Sin(T * 22f + l * 1.6f + i) * 35f : 0f);
                }
                if (T < finish[winner]) { callT.text = Names[leader] + " LEADS"; callT.color = Colors[leader]; }
                else { callT.text = "WINNER · " + Names[winner]; callT.color = Colors[winner]; }
                if (crowd != null) crowd.volume = Mathf.Lerp(crowd.volume, 0.35f + Mathf.Clamp01(T / finish[winner]) * 0.4f, Time.deltaTime);
                yield return null;
            }
            if (crowd != null) Destroy(crowd.gameObject);
            Sfx.Play("crowd_cheer", track.position, 0.9f);
            if (bets[winner].Total > 0) Juice.Burst("confetti", W(new Vector3(0, 1.6f, 0.75f)), 50);   // only for the player's horse
            callT.text = "WINNER · " + Names[winner];
            callT.color = Colors[winner];
            Status("#" + (winner + 1) + " " + Names[winner] + " WINS · " + PaysLabel(winner) + "x", Colors[winner]);
            bets[winner].Highlight(true, Colors[winner]);
            yield return new WaitForSeconds(1.2f);
            float m = bets[winner].pays;
            yield return StartCoroutine(Settle(s => s == bets[winner] ? m : 0f));
            bets[winner].Highlight(false, Palette.Gold);
            yield return new WaitForSeconds(0.8f);
            NewOdds();
            Place(0f);
            callT.text = "NEW ODDS POSTED";
            raceBtn.SetInteractable(true);
            Status("PLACE YOUR BETS", Palette.Text);
        }
    
        protected override void OnRoundAborted() { if (raceBtn != null) raceBtn.SetInteractable(true); }
    }
}
