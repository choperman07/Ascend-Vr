using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ascend
{
    /// <summary>The ASCEND menu (≡ / Y / B button): Wallet & deposits, your Profile, daily Missions, Settings and Online.</summary>
    public class MainMenu : MonoBehaviour
    {
        public static MainMenu I { get; private set; }
        RectTransform panel, body;
        readonly List<Button> tabs = new List<Button>();
        int tab;
        bool open;
        TextMeshProUGUI balanceT;
        static readonly string[] TabNames = { "WALLET", "PROFILE", "MISSIONS", "SETTINGS", "ONLINE" };

        void Awake() { I = this; }

        const float PanelW = 1400f, PanelH = 900f, WorldW = 1.2f;
        TextMeshProUGUI sessionT;

        void Start()
        {
            // party-casino layout: sticker logo top-left, big tab list down the left with a solid highlight block,
            // balance top-right, the page on the right, help link bottom-left
            panel = UIKit.Panel("ASCEND Menu", transform, Vector3.zero, Quaternion.identity, new Vector2(PanelW, PanelH), WorldW, Palette.Violet);
            UISkin.Logo(panel, 44, 30, 360, 96);
            UIKit.Label(panel, "PLAY CHIPS", 820, 30, 380, 30, 24, Palette.Muted, Palette.UI, TextAlignmentOptions.MidlineRight);
            balanceT = UIKit.Label(panel, "", 700, 56, 500, 76, 70, Palette.Money, Palette.UI, TextAlignmentOptions.MidlineRight);
            sessionT = UIKit.Label(panel, "", 700, 128, 500, 36, 30, Palette.Loss, Palette.UI, TextAlignmentOptions.MidlineRight);
            UIKit.Btn(panel, "X", PanelW - 112, 36, 72, 72, Palette.Loss, Toggle, 40);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int k = i;
                var b = UIKit.Btn(panel, TabNames[i], 36, 250 + i * 96, 330, 84, Palette.Lime, () => Show(k), 46);
                var cap = b.GetComponentInChildren<TextMeshProUGUI>();
                cap.alignment = TextAlignmentOptions.MidlineLeft; cap.margin = new Vector4(18, 0, 0, 0);
                b.targetGraphic.color = new Color(0, 0, 0, 0);   // tabs are bare text until hovered / chosen, like a title menu
                foreach (var img in b.GetComponentsInChildren<Image>()) if (img.name == "Outline") img.enabled = false;
                tabs.Add(b);
            }
            body = UIKit.Rect("Body", panel, 400, 182, 1140, 640);
            body.localScale = new Vector3(0.86f, 0.86f, 1f);
            UIKit.Label(panel, "NEED HELP?  JUGARBIEN.ES", 44, 762, 340, 36, 26, Palette.Text, Palette.UI, TextAlignmentOptions.MidlineLeft);
            UIKit.Label(panel, "18+ · PLAY CHIPS ONLY · NO REAL MONEY", 44, 806, 1310, 30, 18, Palette.Muted, Palette.Mono, TextAlignmentOptions.MidlineLeft);
            panel.gameObject.SetActive(false);
            GameState.I.Changed += RefreshBalance;
            RefreshBalance();
        }

        void OnDestroy() { if (GameState.Exists) GameState.I.Changed -= RefreshBalance; }

        void RefreshBalance()
        {
            if (balanceT == null) return;
            var gs = GameState.I;
            balanceT.text = gs.chips.ToString("N0");
            sessionT.text = "SESSION " + (gs.sessionNet >= 0 ? "+" : "") + gs.sessionNet.ToString("N0") + "  ·  JACKPOT " + gs.jackpot.ToString("N0");
            sessionT.color = gs.sessionNet >= 0 ? Palette.Money : Palette.Loss;
        }

        void Update()
        {
            if (XRIn.Down(Hand.Left, Btn.Menu) || XRIn.Down(Hand.Left, Btn.Secondary) || XRIn.Down(Hand.Right, Btn.Secondary) || XRIn.KeyDown("tab") || XRIn.KeyDown("m"))
                Toggle();
        }

        public void Toggle()
        {
            open = !open;
            if (open)
            {
                var rig = Rig.I;
                var f = rig != null ? rig.HeadForwardFlat : Vector3.forward;
                var head = rig != null ? rig.Head.position : Vector3.up * 1.6f;
                transform.position = head + f * 0.85f + Vector3.down * 0.15f;
                transform.rotation = Quaternion.LookRotation(f) * Quaternion.Euler(10f, 0, 0);
                panel.gameObject.SetActive(true);
                Show(tab);
                Sfx.UI("ui_open", 0.6f);
                StartCoroutine(Pop());
            }
            else
            {
                panel.gameObject.SetActive(false);
                Sfx.UI("ui_close", 0.6f);
            }
        }

        IEnumerator Pop()
        {
            for (float t = 0; t < 0.18f; t += Time.deltaTime) { panel.localScale = Vector3.one * (WorldW / PanelW) * Fx.EaseOutBack(t / 0.18f); yield return null; }
            panel.localScale = Vector3.one * (WorldW / PanelW);
        }

        public void OpenTab(int t) { tab = t; if (!open) Toggle(); else Show(t); }

        void Show(int t)
        {
            tab = t;
            for (int i = 0; i < tabs.Count; i++) UISkin.SetSelected(tabs[i], i == t);
            UIKit.Clear(body);
            switch (t)
            {
                case 0: Wallet(); break;
                case 1: Profile(); break;
                case 2: Missions(); break;
                case 3: Settings(); break;
                default: Online(); break;
            }
        }

        // ------------------------------------------------------------ WALLET / DEPOSIT
        static readonly string[] Methods = { "GAME WALLET", "CRYPTO", "NFT", "CARD", "BANK", "GIFT CARD", "PREPAID" };
        static readonly string[] Notes =
        {
            "TOP UP FROM YOUR GAME-PLATFORM WALLET", "CONNECT A WALLET · STABLECOINS", "VERIFY AN NFT YOU OWN · IT JOINS YOUR COLLECTION",
            "DEBIT OR CREDIT CARD", "INSTANT BANK TRANSFER", "REDEEM A GIFT CODE", "PREPAID VOUCHER"
        };
        static readonly long[] Amounts = { 10000, 25000, 50000, 100000 };
        int method, amountIdx = 1; bool working;
        float resetArmedUntil;

        void Wallet()
        {
            if (Run.Active) { RunWallet(); return; }
            UIKit.Label(body, "DEPOSIT METHOD", 36, 0, 600, 40, 28, Palette.Muted, Palette.Display);
            for (int i = 0; i < Methods.Length; i++)
            {
                int k = i;
                var b = UIKit.Btn(body, Methods[i], 30 + (i % 4) * 285, 44 + (i / 4) * 86, 275, 76, Palette.Mint, () => { method = k; Show(0); }, 30);
                if (i == method) UISkin.SetSelected(b, true);
            }
            UIKit.Label(body, Notes[method], 36, 222, 1130, 40, 24, Palette.Text, Palette.Mono);
            bool nft = Methods[method] == "NFT";
            if (!nft)
            {
                for (int i = 0; i < Amounts.Length; i++)
                {
                    int k = i;
                    var b = UIKit.Btn(body, Amounts[i].ToString("N0"), 30 + i * 285, 272, 275, 86, Palette.Teal, () => { amountIdx = k; Show(0); }, 40);
                    if (i == amountIdx) UISkin.SetSelected(b, true);
                }
            }
            UIKit.Btn(body, nft ? "VERIFY NFT" : "ADD " + Amounts[amountIdx].ToString("N0") + " PLAY CHIPS", 30, 380, 780, 110, Palette.Gold, () => { if (!working) StartCoroutine(Deposit()); }, 46);
            UIKit.Btn(body, "CASH OUT 10K", 830, 380, 340, 110, Palette.Silver, () => GameState.I.Withdraw(10000), 36);
            UIKit.Label(body, "DEMO: EVERY METHOD ONLY ADDS FICTIONAL PLAY CHIPS. NOTHING IS CHARGED AND NO PAYMENT DATA IS ASKED FOR. A REAL PRODUCT WOULD NEED AGE AND IDENTITY CHECKS, DEPOSIT LIMITS AND A GAMBLING LICENCE.",
                36, 505, 1130, 90, 20, Palette.Muted, Palette.Mono, TextAlignmentOptions.TopLeft);
        }

        /// <summary>Mafia mode: no deposits. The day, the quota, what you carry, and the driver.</summary>
        void RunWallet()
        {
            var gs = GameState.I;
            UIKit.Label(body, "MODO MAFIA · DÍA " + Run.Day + " DE " + Run.Days, 36, 0, 1100, 60, 44, Palette.Gold, Palette.UI);
            UIKit.Label(body, Run.FinalDay ? "HOY: DERROTA A THE HOUSE EN LA AZOTEA. SI GANAS, DEUDA SALDADA."
                                           : "CUOTA DE HOY: " + Run.N(Run.Quota) + "  ·  TIENES " + Run.N(gs.chips), 36, 70, 1100, 50, 32, Palette.Text, Palette.Body);
            var owned = string.Join("\n", new List<string>(Run.OwnedLabels()).ToArray());
            UIKit.Label(body, owned.Length > 0 ? "MODIFICADORES:\n" + owned : "SIN MODIFICADORES · EL RATA VENDE COSAS JUNTO AL ASCENSOR Y EN CASA", 36, 140, 1100, 260, 26, Palette.Muted, Palette.Mono, TextAlignmentOptions.TopLeft);
            if (RunDirector.I != null && RunDirector.I.InCasino)
                UIKit.Btn(body, "LLAMAR AL CHÓFER (ACABAR EL DÍA YA)", 30, 420, 780, 100, Palette.Red, () => { Toggle(); RunDirector.I.EndDayEarly(); }, 34);
            UIKit.Label(body, "En modo mafia no hay depósitos: solo cuentan las fichas que ganes. Fichas de juego, sin dinero real.", 36, 540, 1130, 60, 20, Palette.Muted, Palette.Mono, TextAlignmentOptions.TopLeft);
        }

        IEnumerator Deposit()
        {
            working = true;
            string m = Methods[method];
            foreach (var s in new[] { "CONNECTING", "VERIFYING", "CONFIRMED" })
            {
                GameState.I.Notify(m + " · " + s + "…", Palette.Mint);
                Sfx.UI("ui_click", 0.4f);
                yield return new WaitForSeconds(0.45f);
            }
            if (m == "NFT")
            {
                var r = Odds.RollRarity(Odds.CrateBase);
                GameState.I.AddNft(r);
                GameState.I.Notify("NFT VERIFIED · " + r.ToString().ToUpperInvariant() + " ADDED · SET IT AS AVATAR IN PROFILE", Palette.RarityColor(r));
                Sfx.UI("reveal_" + r.ToString().ToLowerInvariant(), 0.8f);
            }
            else
            {
                GameState.I.Grant(Amounts[amountIdx], "DEMO " + m);
                Sfx.UI("chips_payout", 0.9f);
                Voice.Announce("ann_deposit");
                if (Rig.I != null) Juice.Burst("chips", Rig.I.Head.position + Rig.I.HeadForwardFlat * 0.7f + Vector3.down * 0.3f, 30);
            }
            working = false;
            if (open && tab == 0) Show(0);
        }

        // ------------------------------------------------------------ PROFILE
        void Profile()
        {
            var gs = GameState.I;
            var pic = UIKit.Pic(body, Palette.Card(gs.nft), 40, 0, 250, 354);
            var tier = gs.CurrentTier;
            UIKit.Label(body, gs.playerName, 320, 0, 840, 70, 64, Palette.Text, Palette.Display);
            UIKit.Label(body, GameState.TierNames[(int)tier] + "  ·  " + gs.nft.ToString().ToUpperInvariant() + " NFT  ·  +" + Mathf.RoundToInt((gs.NftBoost - 1f) * 100) + "% XP BOOST", 320, 70, 840, 44, 32, Palette.TierColor(tier), Palette.Display);
            var bar = UIKit.Bar(body, 320, 124, 820, 22, Palette.Teal);
            UIKit.SetBar(bar, gs.TierProgress);
            UIKit.Label(body, (int)tier >= 4 ? gs.xp.ToString("N0") + " XP · MAX TIER" : gs.xp.ToString("N0") + " / " + gs.NextTierXP.ToString("N0") + " XP > " + GameState.TierNames[(int)tier + 1], 320, 150, 840, 34, 22, Palette.Muted, Palette.Mono);
            UIKit.Label(body, "SESSION  " + (gs.sessionNet >= 0 ? "+" : "") + gs.sessionNet.ToString("N0") + "   ·   BIGGEST WIN  " + gs.biggestWin.ToString("N0") + "\nROUNDS  " + gs.rounds + "   ·   WINS  " + gs.wins + "   ·   ACHIEVEMENTS  " + new List<string>(gs.Achievements).Count,
                320, 196, 840, 70, 24, Palette.Text, Palette.Mono, TextAlignmentOptions.TopLeft);
            UIKit.Label(body, "YOUR NFTS · TAP TO WEAR", 320, 280, 840, 34, 24, Palette.Muted, Palette.Display);
            for (int r = 1; r <= 5; r++)
            {
                var rr = (Rarity)r;
                bool own = gs.Owns(rr);
                var b = UIKit.Btn(body, rr.ToString().ToUpperInvariant() + (own ? "" : " ·"), 320 + (r - 1) * 168, 318, 160, 60, Palette.RarityColor(rr), () => { GameState.I.SetNft(rr); Show(1); }, 24);
                b.interactable = own && gs.nft != rr;
            }
            UIKit.Label(body, "LOOK", 40, 392, 270, 56, 26, Palette.Muted, Palette.Display, TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < AvatarStyles.Count; i++)
            {
                var st = (AvatarStyle)i;
                var sb = UIKit.Btn(body, AvatarStyles.Names[i], 320 + i * 210, 392, 202, 56, i == 0 ? Palette.Magenta : Palette.Teal, () => { AvatarStyles.Current = st; GameState.I.Notify("AVATAR LOOK · " + AvatarStyles.Names[(int)st], Palette.Teal); Show(1); }, 19);
                sb.interactable = AvatarStyles.Current != st;
            }
            UIKit.Label(body, "Other players see you as your 3D avatar: NEON RONIN wears your NFT's armour. Preview it at the lobby AVATAR STUDIO.", 40, 462, 1120, 40, 21, Palette.Muted, Palette.Body);
            pic.name = "Avatar";
        }

        // ------------------------------------------------------------ MISSIONS
        void Missions()
        {
            var gs = GameState.I;
            UIKit.Label(body, "DAILY MISSIONS", 36, 0, 600, 50, 40, Palette.Text, Palette.Display);
            UIKit.Label(body, "THE HOUSE JACKPOT  " + gs.jackpot.ToString("N0") + " · GROWS WITH 1% OF EVERY BET · WON ON THE ROOF", 36, 50, 1130, 34, 22, Palette.Gold, Palette.Mono);
            for (int i = 0; i < gs.missions.Count; i++)
            {
                var m = gs.missions[i];
                float y = 100 + i * 150;
                UIKit.Box(body, 30, y, 1140, 136, UIKit.CellBg);
                UIKit.Label(body, m.title, 50, y + 8, 800, 50, 34, m.done ? Palette.Mint : Palette.Text, Palette.Display);
                UIKit.Label(body, "+" + m.rewardXP + " XP" + (m.rewardChips > 0 ? " · +" + m.rewardChips.ToString("N0") + " CHIPS" : "") + (m.crate ? " · CRATE" : ""), 50, y + 56, 800, 34, 22, Palette.Gold, Palette.Mono);
                var bar = UIKit.Bar(body, 50, y + 98, 1000, 18, m.done ? Palette.Mint : Palette.Violet);
                UIKit.SetBar(bar, m.progress / (float)m.target);
                UIKit.Label(body, m.done ? "DONE" : m.progress + " / " + m.target, 860, y + 10, 290, 50, 32, m.done ? Palette.Mint : Palette.Muted, Palette.Display, TextAlignmentOptions.MidlineRight);
            }
        }

        // ------------------------------------------------------------ SETTINGS (+ responsible play)
        void Settings()
        {
            var gs = GameState.I;
            Row(0, "MUSIC", (gs.musicVolume * 100).ToString("0") + "%", () => { gs.musicVolume = Mathf.Clamp01(gs.musicVolume - 0.1f); gs.SettingsChanged(); Show(3); }, () => { gs.musicVolume = Mathf.Clamp01(gs.musicVolume + 0.1f); gs.SettingsChanged(); Show(3); });
            Row(1, "SOUND FX", (gs.sfxVolume * 100).ToString("0") + "%", () => { gs.sfxVolume = Mathf.Clamp01(gs.sfxVolume - 0.1f); gs.SettingsChanged(); Show(3); }, () => { gs.sfxVolume = Mathf.Clamp01(gs.sfxVolume + 0.1f); gs.SettingsChanged(); Show(3); });
            Row(2, "SESSION LOSS LIMIT", gs.sessionLossLimit <= 0 ? "OFF" : gs.sessionLossLimit.ToString("N0"), () => { gs.SetLossLimit(gs.sessionLossLimit - 10000); Show(3); }, () => { gs.SetLossLimit(gs.sessionLossLimit + 10000); Show(3); });
            Row(3, "REALITY CHECK", gs.realityCheckMinutes.ToString("0") + " MIN", () => { gs.realityCheckMinutes = Mathf.Max(1, gs.realityCheckMinutes - 5); gs.SettingsChanged(); Show(3); }, () => { gs.realityCheckMinutes = Mathf.Min(120, gs.realityCheckMinutes + 5); gs.SettingsChanged(); Show(3); });
            UIKit.Btn(body, gs.demoUnlockAll ? "DEMO MODE: ON (ALL FLOORS OPEN)" : "DEMO MODE: OFF (FLOORS BY TIER)", 30, 340, 560, 80, Palette.Violet, () => { gs.SetDemo(!gs.demoUnlockAll); Show(3); }, 26);
            UIKit.Btn(body, "TAKE A BREAK (60 S)", 610, 340, 560, 80, Palette.Mint, () => { gs.TakeBreak(60f); Toggle(); }, 30);
            // one stray poke used to wipe chips, XP, NFTs and achievements: the first tap only arms it for 4 seconds
            bool armed = Time.unscaledTime < resetArmedUntil;
            UIKit.Btn(body, armed ? "TAP AGAIN TO RESET EVERYTHING" : "RESET PROGRESS", 30, 440, 560, 70, Palette.Red, () =>
            {
                if (Time.unscaledTime < resetArmedUntil) { resetArmedUntil = 0f; gs.ResetProgress(); gs.Notify("PROGRESS RESET", Palette.Red); }
                else { resetArmedUntil = Time.unscaledTime + 4f; }
                Show(3);
            }, armed ? 24 : 28);
            var gq = GraphicsQuality.I;
            if (gq != null) UIKit.Btn(body, "GRAPHICS: " + (gq.Current == GraphicsQuality.Preset.Quality ? "QUALITY (SHARPER)" : "PERFORMANCE (SMOOTHER)"), 610, 440, 560, 70, Palette.Teal, () => { gq.Toggle(); Show(3); }, 26);
            UIKit.Btn(body, Run.Enabled ? "MODO: MAFIA (15 DÍAS) → LIBRE" : "MODO: CASINO LIBRE → MAFIA", 30, 520, 560, 56, Palette.Red, () =>
            {
                Run.Enabled = !Run.Enabled;
                if (Rig.I != null) Rig.I.SetFade(1f);
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }, 24);
            if (!Run.Active) UIKit.Btn(body, "REPLAY INTRO", 610, 590, 360, 50, Palette.Gold, () => { Toggle(); IntroCinematic.Replay(); }, 22);
            UIKit.Label(body, "Need help? In Spain: jugarbien.es · Class prototype, play chips only.", 410, 520, 760, 40, 20, Palette.Muted, Palette.Body, TextAlignmentOptions.MidlineLeft);
        }

        void Row(int i, string label, string value, UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus)
        {
            float y = i * 80;
            UIKit.Label(body, label, 40, y, 520, 70, 34, Palette.Text, Palette.Display);
            UIKit.Btn(body, "-", 640, y + 4, 90, 66, Palette.Teal, minus, 40);
            UIKit.Label(body, value, 740, y, 330, 70, 34, Palette.Gold, Palette.Display, TextAlignmentOptions.Center);
            UIKit.Btn(body, "+", 1080, y + 4, 90, 66, Palette.Teal, plus, 40);
        }

        // ------------------------------------------------------------ ONLINE (multiplayer + voice)
        void Online()
        {
            var net = Online_I();
            UIKit.Label(body, "PLAY WITH OTHERS", 36, 0, 900, 50, 40, Palette.Text, Palette.Display);
            UIKit.Label(body, net != null ? net.StatusText : "ONLINE MODULE NOT IN SCENE", 36, 56, 1130, 120, 26, Palette.Muted, Palette.Mono, TextAlignmentOptions.TopLeft);
            if (net != null)
            {
                UIKit.Btn(body, net.Connected ? "LEAVE ONLINE FLOOR" : "JOIN THE ONLINE FLOOR", 30, 190, 560, 100, net.Connected ? Palette.Red : Palette.Mint, () => { if (net.Connected) net.Leave(); else net.Join(); StartCoroutine(RefreshLater()); }, 32);
                UIKit.Btn(body, net.MicMuted ? "MIC: MUTED" : "MIC: ON (PROXIMITY VOICE)", 610, 190, 560, 100, net.MicMuted ? Palette.Muted : Palette.Teal, () => { net.ToggleMic(); Show(4); }, 30);
            }
            UIKit.Label(body, "Voice chat is proximity based: people near you hear you louder. Without the online floor the casino is filled with simulated players.", 36, 320, 1130, 80, 22, Palette.Muted, Palette.Body, TextAlignmentOptions.TopLeft);
        }

        IEnumerator RefreshLater() { yield return new WaitForSeconds(1.5f); if (open && tab == 4) Show(4); }

        static OnlineSession Online_I() { return FindFirstObjectByType<OnlineSession>(); }
    }
}
