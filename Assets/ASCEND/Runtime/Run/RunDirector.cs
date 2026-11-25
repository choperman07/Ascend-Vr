using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

namespace Ascend
{
    /// <summary>MAFIA MODE. Every day starts in the half-ruined house: talk to Don Tuchi (text only), his car door
    /// opens, the car takes you to the casino, you have 5 minutes to make his quota. Make it and he takes his cut;
    /// miss it and the car takes you somewhere quiet instead. Day 15 is the rooftop raid against THE HOUSE: win and
    /// the debt is cleared (and a last DOUBLE OR NOTHING), lose and it all starts again from day 1.</summary>
    public class RunDirector : MonoBehaviour
    {
        public static RunDirector I { get; private set; }
        public enum Where { Boot, Home, Casino, Execution, Limbo }
        public Where where = Where.Boot;
        public bool InCasino { get { return where == Where.Casino; } }

        RunWorld world; RunHud hud; DialogBox dialog; ShopPanel homeShop, casinoShop;
        float timeLeft; bool dayRunning, talked, boarding, busy;
        /// <summary>Seconds left in today's casino visit (the wrist watch shows it).</summary>
        public float TimeLeft { get { return timeLeft; } }
        public bool DayRunning { get { return dayRunning; } }
        long netAtDayStart, pendingLoan;
        float warnNext;
        bool paidYesterday;
        GameObject endPanel, doorSign;

        void Awake() { I = this; Run.Active = true; }

        void OnDestroy()
        {
            if (I == this) { I = null; Run.Active = false; }
            RaidArena.Finished -= OnRaid;
            RaidArena.FinalMode = false;
            if (FloorManager.I != null) FloorManager.I.FloorActivated -= OnFloor;
        }

        IEnumerator Start()
        {
            while (!Rig.Ready || !GameState.Exists || FloorManager.I == null) yield return null;
            yield return null;
            world = RunWorld.Build();
            hud = RunHud.Create();
            dialog = DialogBox.Create();
            homeShop = ShopPanel.Create(world.ratStall, world.ratStall.TransformPoint(new Vector3(1.75f, 1.55f, -0.35f)), false);
            casinoShop = ShopPanel.Create(transform, Vector3.zero, true);
            casinoShop.gameObject.SetActive(false);
            var ds = Txt.Make("DoorSign", world.home, world.home.InverseTransformPoint(world.car.DoorPoint) + Vector3.up * 2.1f, "SUBE AL COCHE", 0.2f, Palette.Gold, Palette.UI, TMPro.TextAlignmentOptions.Center, 3f);
            ds.gameObject.AddComponent<Billboard>(); doorSign = ds.gameObject; doorSign.SetActive(false);
            FloorManager.I.FloorActivated += OnFloor;
            RaidArena.Finished += OnRaid;
            NewRun();
        }

        // ------------------------------------------------------------------ run / day flow
        void NewRun()
        {
            if (endPanel != null) Destroy(endPanel);
            Run.Reset();
            pendingLoan = 0; paidYesterday = false;
            var gs = GameState.I;
            gs.chips = 0; gs.ReturnChips(Run.StartChips);
            gs.winStreak = 0;
            StartCoroutine(GoHome());
        }

        IEnumerator Fade(float from, float to, float dur)
        {
            for (float t = 0; t < dur; t += Time.deltaTime) { Rig.I.SetFade(Mathf.Lerp(from, to, t / dur)); yield return null; }
            Rig.I.SetFade(to);
        }

        void LeaveCasino()
        {
            dayRunning = false;
            hud.Set("");
            if (PlayerSeating.Seated) PlayerSeating.I.StandUp(false);
            casinoShop.gameObject.SetActive(false);
            FloorManager.I.HideAll();
            RaidArena.FinalMode = false;
        }

        IEnumerator GoHome()
        {
            busy = true;
            yield return Fade(0f, 1f, 0.5f);
            LeaveCasino();
            foreach (var s in world.sites) world.ShowSite(s, false);
            world.limbo.gameObject.SetActive(false);
            world.home.gameObject.SetActive(true);
            where = Where.Home; talked = false; boarding = false;
            world.car.SetOpen(0f);
            world.SetCalendar(Run.Day);
            Rig.I.Freeze(false);
            Rig.I.Place(world.HomeSpawn, world.HomeYaw);
            Sfx.Music("lounge_loop", 0.16f);
            yield return new WaitForSeconds(0.3f);
            yield return Fade(1f, 0f, 0.8f);
            busy = false;
            StartCoroutine(RunCard.Show("DÍA " + Run.Day + " / " + Run.Days,
                Run.FinalDay ? "HOY NO HAY CUOTA: HAY QUE TUMBAR A THE HOUSE" : "CUOTA DE HOY: " + Run.N(Run.Quota) + " FICHAS", Run.FinalDay ? Palette.Red : Palette.Gold, 3.6f));
        }

        void Talk()
        {
            dialog.Show(world.don.transform, "DON TUCHI", DonLines(), Run.FinalDay ? "VAMOS ALLÁ" : "VALE, VALE...", () =>
            {
                talked = true;
                StartCoroutine(OpenDoor());
            });
        }

        IEnumerator OpenDoor()
        {
            Sfx.Play("push", world.car.DoorPoint, 0.8f);
            for (float t = 0; t < 0.8f; t += Time.deltaTime) { world.car.SetOpen(t / 0.8f); yield return null; }
            world.car.SetOpen(1f);
            GameState.I.Notify("EL COCHE ESTÁ ABIERTO · SÚBETE", Palette.Gold);
        }

        IEnumerator Board()
        {
            boarding = true; busy = true;
            Rig.I.Freeze(true);
            Sfx.UI("push", 0.9f);
            yield return Fade(0f, 1f, 0.6f);
            world.car.SetOpen(0f);
            world.home.gameObject.SetActive(false);
            yield return new WaitForSeconds(0.6f);
            // into the tower: the lobby (or the roof on the last day)
            int f = Run.StartFloor;
            FloorManager.I.Activate(f, true);
            Rig.I.Floor = f;
            Rig.I.Place(Tower.Arrival(f), Tower.ArrivalYaw);
            Rig.I.Freeze(false);
            RaidArena.FinalMode = Run.FinalDay;
            where = Where.Casino;
            timeLeft = Run.DaySeconds; dayRunning = true; warnNext = 30f;
            netAtDayStart = GameState.I.sessionNet;
            yield return new WaitForSeconds(0.4f);
            yield return Fade(1f, 0f, 0.7f);
            busy = false;
            StartCoroutine(RunCard.Show(Run.FinalDay ? "LA AZOTEA" : "¡A JUGAR!",
                Run.FinalDay ? "PULSA START EN EL RAID · SI GANAS, DEUDA SALDADA" : "TIENES " + Run.Clock(timeLeft) + " · CUOTA " + Run.N(Run.Quota), Run.FinalDay ? Palette.Red : Palette.Mint, 3f));
        }

        void OnFloor(int f)
        {
            if (where != Where.Casino || casinoShop == null) return;
            // the black-market kiosk (and the driver) waits next to the lift on every floor
            casinoShop.gameObject.SetActive(true);
            casinoShop.transform.position = Tower.Arrival(f) + new Vector3(1.7f, 1.45f, -1.2f);
        }

        public void EndDayEarly()
        {
            if (!dayRunning || where != Where.Casino) return;
            timeLeft = 0f;
            GameState.I.Notify("EL CHÓFER YA VIENE", Palette.Gold);
        }

        void Update()
        {
            if (world == null || busy) return;
            var gs = GameState.I;
#if UNITY_EDITOR
            // editor-only test keys: F8 ends the day now, F9 adds 5.000 run chips, F10 skips to the last day
            if (XRIn.KeyDown("f8") && dayRunning) timeLeft = 0.01f;
            if (XRIn.KeyDown("f9")) gs.ReturnChips(5000);
            if (XRIn.KeyDown("f7")) gs.TakeChips(gs.chips);
            if (XRIn.KeyDown("f10") && where == Where.Home) { Run.Day = Run.Days; StartCoroutine(GoHome()); return; }
#endif
            switch (where)
            {
                case Where.Home:
                {
                    var head = Rig.I.Head.position;
                    float dDon = Flat(head - world.don.transform.position);
                    world.don.lookAtTarget = dDon < 7f ? Rig.I.Head : null;
                    if (world.rata != null) world.rata.lookAtTarget = Flat(head - world.rata.transform.position) < 6f ? Rig.I.Head : null;
                    if (!talked && !dialog.Open && dDon < 2.8f) Talk();
                    if (talked && !boarding && world.car.Open > 0.9f && Flat(Rig.I.Feet - world.car.DoorPoint) < 2.0f) StartCoroutine(Board());
                    if (doorSign != null) doorSign.SetActive(talked && !boarding);
                    string todo = !talked ? "HABLA CON DON TUCHI (FUERA, JUNTO AL COCHE)" : "SUBE AL COCHE";
                    hud.Set("DÍA " + Run.Day + "/" + Run.Days + "   ·   " + Run.N(gs.chips) + " FICHAS" +
                            (Run.FinalDay ? "" : "   ·   CUOTA " + Run.N(Run.Quota)) + "\n<size=70%><color=#FFC857>" + todo + "</color></size>");
                    break;
                }
                case Where.Casino:
                {
                    if (!dayRunning) break;
                    if (!(Run.FinalDay && RaidArena.FinalFighting)) timeLeft -= Time.deltaTime;
                    if (timeLeft <= warnNext && warnNext > 0f)
                    {
                        if (warnNext >= 30f) gs.Notify("¡QUEDAN 30 SEGUNDOS!", Palette.Red);
                        Sfx.UI("battle_tick", 0.8f, warnNext <= 5f ? 1.3f : 1f);
                        warnNext = warnNext >= 30f ? 10f : warnNext - 1f;
                    }
                    string clock = (timeLeft < 30f ? "<color=#FF4B6A>" : "") + Run.Clock(timeLeft) + (timeLeft < 30f ? "</color>" : "");
                    if (Run.FinalDay)
                        hud.Set("DÍA " + Run.Day + "/" + Run.Days + "   ·   " + clock + "\n<size=70%><color=#FF4B6A>DERROTA A THE HOUSE EN EL RAID</color></size>");
                    else
                    {
                        long q = Run.Quota; bool ok = gs.chips >= q;
                        hud.Set("DÍA " + Run.Day + "/" + Run.Days + "   ·   " + clock + "\n<size=75%>CUOTA  <color=" + (ok ? "#45F07A" : "#FFC857") + ">" + Run.N(gs.chips) + " / " + Run.N(q) + "</color>" + (ok ? "  ✓" : "") + "</size>");
                    }
                    if (timeLeft <= 0f) { dayRunning = false; StartCoroutine(EndOfDay()); }
                    break;
                }
                default:
                    hud.Set("");
                    break;
            }
        }

        static float Flat(Vector3 d) { d.y = 0; return d.magnitude; }

        IEnumerator EndOfDay()
        {
            busy = true;
            var gs = GameState.I;
            if (PlayerSeating.Seated) PlayerSeating.I.StandUp(false);
            if (Run.Today("seguro"))
            {
                long lost = netAtDayStart - gs.sessionNet;
                if (lost > 0) { long back = lost * 3 / 10; gs.ReturnChips(back); gs.Notify("SEGURO DUDOSO · TE DEVUELVE " + Run.N(back), Palette.Silver); }
            }
            if (Run.FinalDay) { yield return RunCard.Show("SE ACABÓ EL TIEMPO", "THE HOUSE SIGUE EN PIE...", Palette.Red, 2.4f); yield return Execution(0); yield break; }
            long q = Run.Quota;
            yield return RunCard.Show("¡SE ACABÓ EL TIEMPO!", "TIENES " + Run.N(gs.chips) + " · CUOTA " + Run.N(q), gs.chips >= q ? Palette.Mint : Palette.Red, 2.6f);
            // Gamble With Your Friends rules: Don Tuchi only checks the chips, he does not take them
            bool paid = false;
            if (gs.chips >= q) paid = true;
            else if (gs.chips >= q * 85 / 100 && Run.UseLawyer())
            {
                paid = true;
                gs.Notify("TU ABOGADO BARATO HA 'NEGOCIADO' · TE LIBRAS POR HOY", Palette.Teal);
            }
            if (paid)
            {
                long back = pendingLoan * 2; pendingLoan = 0;
                Run.BestDay = System.Math.Max(Run.BestDay, Run.Day);
                Run.NextDay(back, gs.chips);
                if (!Run.FinalDay) gs.Notify("MAÑANA LA CUOTA SERÁ " + Run.N(Run.Quota) + " FICHAS", Palette.Gold);
                paidYesterday = true;
                yield return GoHome();
            }
            else yield return Execution(q - gs.chips);
        }

        // ------------------------------------------------------------------ shop effects
        public void OnBought(Run.Mod m, ShopPanel shop)
        {
            var gs = GameState.I;
            switch (m.id)
            {
                case "bebida":
                    Run.BonusSeconds += 60f; if (dayRunning) timeLeft += 60f;
                    gs.Notify("+60 SEGUNDOS · TE TIEMBLA UN OJO", Palette.Lime);
                    break;
                case "prestamo":
                {
                    long loan = Run.Quota / 2 / 50 * 50;
                    pendingLoan += loan; gs.ReturnChips(loan);
                    gs.Notify("TU PRIMO TE PRESTA " + Run.N(loan) + " · MAÑANA LA CUOTA SUBE " + Run.N(loan * 2), Palette.Magenta);
                    break;
                }
                case "todo":
                    StartCoroutine(AllOrNothing());
                    break;
            }
        }

        IEnumerator AllOrNothing()
        {
            var gs = GameState.I;
            var f = Rig.I.HeadForwardFlat;
            var at = Rig.I.Head.position + f * 1.1f + Vector3.down * 0.15f;
            long stake = gs.chips;
            yield return CoinFlip.Flip(at, f, heads =>
            {
                if (heads) { gs.ReturnChips(stake); Juice.Celebrate(at, stake * 2, 2f); gs.Notify("¡X2! FICHAS DUPLICADAS · " + Run.N(gs.chips), Palette.Gold); }
                else { gs.TakeChips(gs.chips); Juice.Lose(at); gs.Notify("HA SALIDO LA A... TE HAS QUEDADO A CERO", Palette.Red); }
                ShopPanel.RefreshAll();
            });
        }

        // ------------------------------------------------------------------ the quiet place / the end
        IEnumerator Execution(long missing)
        {
            busy = true; where = Where.Execution; hud.Set("");
            yield return Fade(Rig.I != null ? 0f : 1f, 1f, 0.6f);
            LeaveCasino();
            world.home.gameObject.SetActive(false);
            var site = world.sites[Random.Range(0, world.sites.Count)];
            world.ShowSite(site, true);
            Rig.I.Place(site.spot, site.yaw);
            Rig.I.Freeze(true);
            site.don.lookAtTarget = Rig.I.Head;
            foreach (var g in site.goons) g.lookAtTarget = Rig.I.Head;
            Sfx.StopMusic();
            yield return new WaitForSeconds(0.5f);
            yield return Fade(1f, 0f, 1.0f);
            string[] lines = Run.FinalDay
                ? new[] { "THE HOUSE siempre gana, amigo. Y yo también.", "Era tu última oportunidad.", "Nada personal. Son negocios." }
                : new[] { missing > 0 ? "Te faltaban " + Run.N(missing) + " fichas. " + Pick(Short) : "Hoy no has traído nada.", Pick(Last), "Nada personal. Son negocios." };
            dialog.Show(site.don.transform, "DON TUCHI", lines, "", null, true);
            for (float w = 0; dialog.Open && w < 20f; w += Time.deltaTime) yield return null;
            // he draws, levels it at you and fires: one flash, one bang, black, and the game-over screen
            var gun = DonGun.Draw(site.don, Rig.I.Head);
            yield return new WaitForSeconds(1.5f);
            if (gun != null) gun.Fire(); else Sfx.UI("crash_boom", 0.8f);
            yield return new WaitForSeconds(0.07f);
            Rig.I.SetFade(1f);
            if (gun != null) Destroy(gun.gameObject, 0.5f);
            XRIn.HapticBoth(1f, 0.35f);
            yield return new WaitForSeconds(1.8f);
            world.ShowSite(site, false);
            yield return Limbo(false);
        }

        void OnRaid(bool won)
        {
            if (!Run.FinalDay || where != Where.Casino) return;
            dayRunning = false;
            StartCoroutine(won ? Ending() : Execution(0));
        }

        IEnumerator Ending()
        {
            busy = true;
            yield return new WaitForSeconds(4.5f);   // the boss explodes, the squad cheers
            yield return Fade(0f, 1f, 0.8f);
            LeaveCasino();
            yield return Limbo(true);
        }

        IEnumerator Limbo(bool won)
        {
            where = Where.Limbo;
            world.limbo.gameObject.SetActive(true);
            Rig.I.Place(world.LimboSpot, 0f);
            Rig.I.Freeze(true);
            Sfx.Music(won ? "dj_anthem" : "lounge_loop", won ? 0.4f : 0.12f);
            yield return new WaitForSeconds(0.3f);
            yield return Fade(1f, 0f, 0.8f);
            busy = false;
            if (won)
            {
                Juice.Celebrate(world.LimboSpot + new Vector3(0, 1.4f, 2f), System.Math.Max(GameState.I.chips, 1000000), 100f);
                BuildEndPanel(true, false);
            }
            else
            {
                Run.RunsLost++;
                BuildEndPanel(false, false);
            }
        }

        void BuildEndPanel(bool won, bool afterFlip)
        {
            if (endPanel != null) Destroy(endPanel);
            endPanel = new GameObject("End Panel");
            endPanel.transform.position = world.LimboSpot + new Vector3(0, 1.45f, 1.7f);
            var p = UIKit.Panel("EndPanel", endPanel.transform, Vector3.zero, Quaternion.identity, new Vector2(1400, 900), 1.5f, won ? Palette.Gold : Palette.Red);
            var gs = GameState.I;
            if (!won)
            {
                UIKit.Label(p, "LA MAFIA TE HA ENCONTRADO", 0, 70, 1400, 120, 92, Palette.Loss, Palette.UI, TextAlignmentOptions.Center);
                UIKit.Label(p, Run.FinalDay ? "THE HOUSE te ha ganado el último día." : "Te quedaste corto el DÍA " + Run.Day + " de " + Run.Days + ".", 0, 200, 1400, 70, 44, Palette.Text, Palette.Body, TextAlignmentOptions.Center);
                UIKit.Label(p, "MAYOR PREMIO: " + Run.N(gs.biggestWin) + "  ·  PARTIDAS PERDIDAS: " + Run.RunsLost, 0, 290, 1400, 50, 28, Palette.Muted, Palette.Mono, TextAlignmentOptions.Center);
                UIKit.Btn(p, "EMPEZAR DE NUEVO (DÍA 1)", 250, 420, 900, 130, Palette.Gold, () => { Rig.I.Freeze(false); NewRun(); }, 52);
                UIKit.Btn(p, "MODO LIBRE", 250, 580, 900, 100, Palette.Teal, SwitchToFree, 38);
            }
            else if (!afterFlip)
            {
                UIKit.Label(p, "¡DEUDA SALDADA!", 0, 50, 1400, 130, 110, Palette.Gold, Palette.UI, TextAlignmentOptions.Center);
                UIKit.Label(p, "Has tumbado a THE HOUSE. Don Tuchi te deja en paz... por ahora.", 0, 190, 1400, 60, 38, Palette.Text, Palette.Body, TextAlignmentOptions.Center);
                UIKit.Label(p, "TE QUEDAN " + Run.N(gs.chips) + " FICHAS", 0, 270, 1400, 80, 60, Palette.Money, Palette.UI, TextAlignmentOptions.Center);
                UIKit.Label(p, "¿DOBLE O NADA?", 0, 370, 1400, 80, 64, Palette.Magenta, Palette.UI, TextAlignmentOptions.Center);
                UIKit.Btn(p, "X2", 160, 480, 520, 150, Palette.Gold, () => StartCoroutine(FinalFlip(true)), 64);
                UIKit.Btn(p, "A", 720, 480, 520, 150, Palette.Gold, () => StartCoroutine(FinalFlip(false)), 64);
                UIKit.Btn(p, "ME PLANTO", 400, 660, 600, 90, Palette.Teal, () => BuildEndPanel(true, true), 36);
            }
            else
            {
                UIKit.Label(p, "FIN", 0, 60, 1400, 130, 120, Palette.Gold, Palette.UI, TextAlignmentOptions.Center);
                UIKit.Label(p, "PUNTUACIÓN FINAL", 0, 210, 1400, 60, 40, Palette.Muted, Palette.UI, TextAlignmentOptions.Center);
                UIKit.Label(p, Run.N(gs.chips) + " FICHAS", 0, 270, 1400, 110, 90, gs.chips > 0 ? Palette.Money : Palette.Loss, Palette.UI, TextAlignmentOptions.Center);
                UIKit.Btn(p, "JUGAR OTRA VEZ", 250, 450, 900, 130, Palette.Gold, () => { Rig.I.Freeze(false); NewRun(); }, 52);
                UIKit.Btn(p, "MODO LIBRE", 250, 610, 900, 100, Palette.Teal, SwitchToFree, 38);
            }
            UIKit.Label(p, "+18 · SOLO FICHAS DE JUEGO · SIN DINERO REAL · ¿PROBLEMAS CON EL JUEGO? JUGARBIEN.ES", 0, 830, 1400, 40, 22, Palette.Muted, Palette.Mono, TextAlignmentOptions.Center);
        }

        IEnumerator FinalFlip(bool pickHeads)
        {
            if (endPanel != null) endPanel.SetActive(false);
            var gs = GameState.I;
            long stake = gs.chips;
            var at = world.LimboSpot + new Vector3(0, 1.4f, 1.2f);
            yield return CoinFlip.Flip(at, Vector3.forward, heads =>
            {
                if (heads == pickHeads) { gs.ReturnChips(stake); Juice.Celebrate(at, stake * 2, 2f); gs.Notify("¡" + (heads ? "X2" : "LA A") + "! DOBLE: " + Run.N(gs.chips), Palette.Gold); }
                else { gs.TakeChips(gs.chips); Juice.Lose(at); gs.Notify("¡" + (heads ? "X2" : "LA A") + "!... NADA. LA CASA SIEMPRE GANA", Palette.Red); }
            });
            BuildEndPanel(true, true);
        }

        void SwitchToFree()
        {
            Run.Enabled = false;
            Rig.I.SetFade(1f);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ------------------------------------------------------------------ Don Tuchi's lines (text only)
        static readonly string[] Paid =
        {
            "Has cumplido. Por ahora.", "Cuenta exacta. Me gusta.", "Mi contable dice que eres un hombre de palabra. Mi contable es mi madre.",
            "Bien. Pero no te acostumbres a sonreír.", "Mira tú. Ayer pensé que no volvías."
        };
        static readonly string[] Threat =
        {
            "Recuerda: la Casa siempre gana. Y yo, también.", "Mi sobrino Tony no tiene paciencia. Ni sentido del humor.",
            "Si ves a El Rata, cómprale algo. Es mi primo.", "Tic, tac. El taxímetro corre.", "Mañana pediré más. Siempre pido más."
        };
        static readonly string[] Short = { "Qué pena, con lo que me caías.", "Te lo advertí.", "No me hagas esto, hombre." };
        static readonly string[] Last = { "Te dije que no me hicieras volver.", "A mí tampoco me gusta esta parte.", "Mira qué sitio tan tranquilo." };
        static string Pick(string[] a) { return a[Random.Range(0, a.Length)]; }

        string[] DonLines()
        {
            long q = Run.Quota;
            string mins = Mathf.RoundToInt(Run.DaySeconds / 60f).ToString();
            if (Run.Day == 1)
                return new[]
                {
                    "Vaya. Por fin te despiertas.",
                    "Me debes mucho dinero, amigo. Mu-chí-si-mo.",
                    "Lo haremos fácil: cada noche me enseñas una cuota. Hoy, " + Run.N(q) + " fichas.",
                    "No me la quedo... todavía. Pero cuanto más tengas, más te pediré mañana.",
                    "Tienes " + mins + " minutos de casino. El coche te lleva y te trae.",
                    "Si no llegas... bueno. Mejor que llegues."
                };
            if (Run.FinalDay)
                return new[]
                {
                    "Último día, chaval.",
                    "THE HOUSE, el dueño de esa torre, me debe a mí más de lo que tú me debes a mí.",
                    "Sube a la azotea y túmbalo. Si ganas, tu deuda queda saldada.",
                    "Si pierdes... ya conoces a mis amigos."
                };
            var l = new List<string>();
            l.Add(paidYesterday ? Pick(Paid) : "Otro día, otra deuda.");
            l.Add("Hoy quiero " + Run.N(q) + " fichas." + (Run.ExtraDebt > 0 ? " Préstamo de tu primo incluido." : ""));
            for (int f = 0; f < Tower.Count - 1; f++)
                if (Run.OpensOn(f) == Run.Day && f > 1) l.Add("Te he conseguido pase para " + Tower.Floors[f].name + ". De nada.");
            if (Run.Day == Run.Days - 1) l.Add("Mañana es el gran día. Ve haciéndote a la idea.");
            l.Add(Pick(Threat));
            return l.ToArray();
        }
    }
}
