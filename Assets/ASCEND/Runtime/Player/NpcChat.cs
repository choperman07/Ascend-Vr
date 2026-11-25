using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Talk to the guests. VR: aim the right controller at someone ("A · TALK") and press A. Desktop: click them.
    /// They stop, turn to you and a dialogue card opens next to them with a few replies to pick. What they say depends on
    /// who they are (high roller, regular, on a budget), what they are doing and where you are. Text only.</summary>
    public class NpcChat : MonoBehaviour
    {
        public static NpcChat I { get; private set; }
        BotAvatar npc;
        RectTransform card;
        TextMeshProUGUI lineT;
        readonly List<GameObject> options = new List<GameObject>();
        readonly HashSet<string> used = new HashSet<string>();
        static readonly HashSet<BotAvatar> treated = new HashSet<BotAvatar>();
        Transform hint; TextMeshPro hintT; LineRenderer ray;
        BotAvatar hover;
        int tipIdx;

        void Awake() { I = this; }

        void Start()
        {
            hint = new GameObject("TalkHint").transform; hint.SetParent(transform, false);
            hintT = Txt.Glow("Label", hint, Vector3.zero, "A · TALK", 0.07f, Palette.Gold);
            hintT.gameObject.AddComponent<Billboard>();
            hint.gameObject.SetActive(false);
            var go = new GameObject("TalkRay"); go.transform.SetParent(transform, false);
            ray = go.AddComponent<LineRenderer>(); ray.sharedMaterial = Palette.FlatGlow; ray.widthMultiplier = 0.004f; ray.positionCount = 2;
            ray.startColor = Palette.A(Palette.Gold, 0.8f); ray.endColor = Palette.A(Palette.Gold, 0f); ray.enabled = false;
        }

        static readonly RaycastHit[] hits = new RaycastHit[16];
        /// <summary>Closest talkable guest along a ray (used by the desktop click too).</summary>
        public static BotAvatar Pick(Ray r, float maxDist, out Vector3 point)
        {
            point = r.origin;
            int n = Physics.RaycastNonAlloc(r, hits, maxDist, ~0, QueryTriggerInteraction.Ignore);
            BotAvatar best = null; float bd = float.MaxValue; float wall = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var b = hits[i].collider.GetComponentInParent<BotAvatar>();
                if (b == null) { if (Rig.I != null && hits[i].collider.transform.IsChildOf(Rig.I.transform)) continue; wall = Mathf.Min(wall, hits[i].distance); continue; }
                if (b.job == BotJob.Waiter || b.job == BotJob.Patrol) continue;
                if (hits[i].distance < bd) { bd = hits[i].distance; best = b; point = hits[i].point; }
            }
            return bd < wall ? best : null;
        }

        void Update()
        {
            var rig = Rig.I; if (rig == null) return;
            // the guest was destroyed (floor change repopulates the guests): Unity's == null hides that from the check below
            if (!ReferenceEquals(npc, null) && npc == null) Close();
            if (npc != null)
            {
                // walk away or the guest disappears: the conversation ends
                if (!npc.isActiveAndEnabled || (rig.Head.position - npc.transform.position).sqrMagnitude > 16f) Close();
                hint.gameObject.SetActive(false); ray.enabled = false;
                return;
            }
            hover = null;
            if (XRIn.HasHeadset && rig.RightCtrl != null && !IntroCinematic.Playing)
            {
                bool seatHover = PlayerSeating.I != null && PlayerSeating.I.Hovering;
                var r = new Ray(rig.RightCtrl.position, rig.RightCtrl.forward);
                Vector3 p = r.origin;
                var b = seatHover ? null : Pick(r, 5f, out p);
                if (b != null)
                {
                    hover = b;
                    ray.enabled = true; ray.SetPosition(0, r.origin); ray.SetPosition(1, p);
                    hint.gameObject.SetActive(true);
                    hint.position = b.Anim.HeadBone.position + Vector3.up * 0.35f;
                    if (XRIn.Down(Hand.Right, Btn.Primary)) Open(b);
                }
            }
            if (hover == null) { if (hint.gameObject.activeSelf) hint.gameObject.SetActive(false); ray.enabled = false; }
        }

        // ------------------------------------------------------------ dialogue
        public static void Open(BotAvatar b)
        {
            if (I == null || b == null || !Rig.Ready) return;
            if (I.npc == b) return;
            I.Close();
            treated.RemoveWhere(x => x == null);
            I.npc = b; I.used.Clear();
            b.TalkStart(Rig.I.Head);
            I.BuildCard();
            I.Say(I.Greeting());
            I.Root();
            Sfx.UI("ui_open", 0.4f);
        }

        public void Close()
        {
            CancelInvoke("Close");   // a pending "see you around" close must not end the NEXT conversation
            if (npc != null) npc.TalkEnd();
            npc = null;
            if (card != null) Destroy(card.gameObject);
            card = null; options.Clear();
        }

        void BuildCard()
        {
            var head = Rig.I.Head;
            var npcHead = npc.Anim.HeadBone.position;
            var toMe = head.position - npcHead; toMe.y = 0; toMe.Normalize();
            var right = Vector3.Cross(Vector3.up, toMe);
            var pos = npcHead + toMe * 0.55f - right * 0.55f + Vector3.down * 0.1f;
            pos.y = Mathf.Max(pos.y, head.position.y - 0.25f);
            var look = pos - head.position; look.y = 0;
            var prof = npc.GetComponent<AvatarProfile>();
            var rc = prof != null ? Palette.RarityColor(prof.rarity) : Palette.Teal;
            card = UIKit.Panel("ChatCard", null, pos, Quaternion.LookRotation(look.normalized, Vector3.up), new Vector2(900, 600), 0.72f, rc);
            UIKit.Label(card, prof != null ? prof.displayName : "GUEST", 30, 18, 640, 56, 44, rc, Palette.Display);
            string tag = npc.wealth == Wealth.HighRoller ? "HIGH ROLLER" : npc.wealth == Wealth.Budget ? "ON A BUDGET" : "REGULAR";
            UIKit.Label(card, tag + " · " + npc.Activity, 30, 74, 840, 32, 22, Palette.Muted, Palette.Mono);
            UIKit.Btn(card, "X", 820, 16, 60, 56, Palette.Red, Close, 30);
            lineT = UIKit.Label(card, "", 30, 118, 840, 190, 34, Palette.Text, Palette.Body, TextAlignmentOptions.TopLeft);
        }

        void Say(string line)
        {
            if (lineT != null) lineT.text = "\"" + line + "\"";
            if (npc != null) npc.Say(line.Length > 46 ? line.Substring(0, 43) + "..." : line);
        }

        void Options(params KeyValuePair<string, System.Action>[] opts)
        {
            foreach (var o in options) if (o != null) Destroy(o);
            options.Clear();
            for (int i = 0; i < opts.Length && i < 4; i++)
            {
                var o = opts[i];
                var b = UIKit.Btn(card, o.Key, 30, 330 + i * 64, 840, 56, i == opts.Length - 1 ? Palette.Violet : Palette.Teal, () => { Sfx.UI("ui_click", 0.4f); o.Value(); }, 26);
                options.Add(b.gameObject);
            }
        }

        static KeyValuePair<string, System.Action> Opt(string label, System.Action a) { return new KeyValuePair<string, System.Action>(label, a); }

        void Root()
        {
            var list = new List<KeyValuePair<string, System.Action>>();
            if (!used.Contains("night")) list.Add(Opt("HOW'S YOUR NIGHT GOING?", () => { used.Add("night"); Say(Night()); Root(); }));
            if (!used.Contains("tip")) list.Add(Opt("GOT ANY TIPS FOR ME?", () => { used.Add("tip"); Say(Tip()); Root(); }));
            if (!used.Contains("play")) list.Add(Opt("WHAT SHOULD I PLAY?", () => { used.Add("play"); Say(Recommend()); Root(); }));
            if (!npc.HasDrink && !treated.Contains(npc) && list.Count < 3)
                list.Add(Opt("CAN I GET YOU A DRINK?", () => { treated.Add(npc); npc.GiveDrink(Random.Range(0, 3)); Say(npc.wealth == Wealth.HighRoller ? "Champagne, of course. You've got style." : "Oh, nice! Cheers, friend!"); GameState.I.AddXP(50); Root(); }));
            if (npc.Dancing && !used.Contains("dance") && list.Count < 3)
                list.Add(Opt("NICE MOVES!", () => { used.Add("dance"); Say(DanceLine()); npc.Anim.Play(Emote.Cheer, 1.2f); Root(); }));
            list.Add(Opt("SEE YOU AROUND", () => { Say(Bye()); Invoke("Close", 1.6f); Options(); }));
            Options(list.ToArray());
        }

        // ------------------------------------------------------------ lines
        static string Pick(params string[] s) { return s[Random.Range(0, s.Length)]; }
        string FloorName { get { return FloorManager.I != null ? Tower.Floors[FloorManager.I.Current].name : "THE LOBBY"; } }

        string Greeting()
        {
            if (npc.Dancing) return Pick("Hey! Great track, right?", "Whoa, hi! Come dance!", "You here for the DJ too?");
            if (npc.Seated) return Pick("Hey, pull up a stool!", "Hi! Don't jinx my streak, okay?", "Oh hey, want to play the next round?");
            switch (npc.wealth)
            {
                case Wealth.HighRoller: return Pick("Evening. Nice NFT you've got there.", "Ah, a new face. Welcome to the good life.", "Hello there. Enjoying the view?");
                case Wealth.Budget: return Pick("Hey! First time here too?", "Hi! Just watching for now, saving my chips.", "Hey hey! This place is wild, huh?");
                default: return Pick("Hey! How's it going?", "Hi there! Having fun?", "Oh, hey! Nice to meet you.");
            }
        }

        string Night()
        {
            if (npc.Dancing) return Pick("Best night ever. This DJ never stops!", "I came for the slots and stayed for the dance floor.");
            if (npc.Seated) return Pick("Up a little, down a little. That's the game.", "Just won two hands in a row, so... good!", "The dealer is on fire tonight. Not in my favour.");
            switch (npc.wealth)
            {
                case Wealth.HighRoller: return Pick("Up forty thousand at the VIP tables. The Sky Lounge is where the action is.", "I lost a little, won a little more. Champagne either way.", "Just cleared a raid on the roof. The House didn't stand a chance.");
                case Wealth.Budget: return Pick("Making my chips last. Slow and steady.", "I set a small budget and I'm sticking to it. Still having fun!", "Lost a bit on roulette, so now I'm just enjoying the music.");
                default: return Pick("Pretty good! Hit 3x on Crash earlier.", "Not bad. Blackjack's been kind to me.", "Better now that the DJ switched tracks.");
            }
        }

        static readonly string[] Tips =
        {
            "Pick a table, set a budget and play for fun.",
            "Roulette here has a single green zero. Red or black?",
            "Blackjack pays 6:5 on a natural at these tables. Check the felt before you sit.",
            "If you get carried away, set a loss limit in Settings. I do.",
            "The raid on the roof pays the House Jackpot one time in a hundred. Bring friends.",
            "Point at anyone to see their NFT card. Legendaries get the Sky Lounge.",
            "Daily missions give free crates and XP. Don't skip them.",
            "Sit down at a machine and the betting panel comes right to you.",
            "The DJ booth has a secret. Look closely at the edge of the decks.",
        };

        string Tip() { tipIdx = (tipIdx + Random.Range(1, Tips.Length)) % Tips.Length; return Tips[tipIdx]; }

        string Recommend()
        {
            switch (npc.wealth)
            {
                case Wealth.HighRoller: return Pick("High-Roller 21 in the Sky Lounge. Big tables, better company.", "Try the raid on the roof. Nothing beats beating The House.", "Baccarat. Simple, elegant, and the banker bet has the lowest edge.");
                case Wealth.Budget: return Pick("Mines with three mines. Cash out early and you'll last all night.", "Plinko on low risk. It's relaxing to watch the ball.", "Coinflip. Fifty-fifty, almost. Quick and easy.");
                default: return Pick("Crash! Just cash out before it pops.", "The slots on Luck & Loot. Pull the lever, it feels great.", "Craps on Dice & Wheels. The stickman is hilarious.");
            }
        }

        string DanceLine() { return Pick("You should try the griddy, it's easy!", "Floss is harder than it looks.", "I learned this one from a robot. Literally.", "The trick is to never stop!"); }
        string Bye() { return Pick("See you around!", "Good luck out there!", "Catch you on the leaderboard!", "Have fun, and know when to stop!"); }
    }
}
