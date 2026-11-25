using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace Ascend
{
    public enum BotJob { Guest, Seated, Waiter, Patrol }
    /// <summary>How deep a guest's pockets are: changes their look (gold chain + shades / cap), their chip stacks, what
    /// they talk about and the bets they place.</summary>
    public enum Wealth { Budget, Regular, HighRoller }

    /// <summary>A simulated guest. Seated guests really play: at tables they keep a tidy chip stack on the felt, carry
    /// chips to a bet in front of them while bets are open, follow the table's real rounds, get paid (chips pushed to
    /// them, sparkles, a cheer, neighbours clapping for big wins) or watch the dealer sweep their chips away (head shake,
    /// facepalm). At slots they press SPIN, watch the reels and react; at arcade stations they bet on the counter and play.
    /// They also dance, drink at the bar, watch games and chat — with glances, weight shifts and talking hands — and
    /// you can talk to them. Waiter robots walk the floor and hand drinks to guests; guards patrol. Honest odds: every
    /// guest bet has a house edge, so guests lose a little more than they win.</summary>
    [DefaultExecutionOrder(200)]
    public class BotAvatar : MonoBehaviour
    {
        public static readonly List<BotAvatar> All = new List<BotAvatar>();

        public int rosterId;
        public BotJob job = BotJob.Guest;
        public Wealth wealth = Wealth.Regular;
        BotDirector dir;
        List<BotSpot> spots;
        BotSpot spot;
        Seat seat;
        NavMeshAgent agent;
        AvatarBody anim;
        TextMeshPro bubble;
        float until, bubbleUntil, nextEmote, nextSip, holdUntil, nextBet;
        bool walking, standingUp;
        Transform tray, drink;
        int trayDrinks = 3;
        BotAvatar serving;          // waiter: guest we are bringing a drink to
        Transform faceTarget;
        bool danceBeforeTalk;                    // a dancer keeps "dancing" while chatting and resumes afterwards
        public bool Talking { get; private set; }
        public AvatarBody Anim { get { return anim; } }
        public bool Seated { get { return seat != null; } }
        public bool HasDrink { get { return drink != null; } }
        public bool Dancing { get { return anim != null && (anim.pose == Pose.Dance || (Talking && danceBeforeTalk)); } }
        public string Activity
        {
            get
            {
                if (job == BotJob.Waiter) return "SERVING DRINKS";
                if (job == BotJob.Patrol) return "ON PATROL";
                if (Seated) return seat.table != null ? "PLAYING " + seat.table.title : seat.tableEdge ? "AT THE BAR" : "RELAXING IN THE LOUNGE";
                if (Dancing) return "DANCING";
                if (spot != null && spot.kind == SpotKind.Bar) return "AT THE BAR";
                if (spot != null && spot.kind == SpotKind.Watch) return "WATCHING A GAME";
                if (spot != null && spot.kind == SpotKind.Window) return "ENJOYING THE VIEW";
                return walking ? "WALKING THE FLOOR" : "HANGING OUT";
            }
        }

        void OnEnable() { All.Add(this); if (drink != null) drink.gameObject.SetActive(true); }
        void OnDisable() { All.Remove(this); if (drink != null) drink.gameObject.SetActive(false); }

        public void Init(BotDirector d, int id, List<BotSpot> s, int floor, BotJob j = BotJob.Guest, Seat takeSeat = null, Wealth w = Wealth.Regular)
        {
            dir = d; rosterId = id; spots = s; job = j; wealth = w;
            anim = GetComponent<AvatarBody>();
            anim.danceStyle = Random.Range(0, 6);
            // waiter robots are service machines, not players: no player card
            var prof = job == BotJob.Waiter ? null : gameObject.AddComponent<AvatarProfile>();
            if (prof == null) { }
            else if (job == BotJob.Patrol) { prof.displayName = "SECURITY · UNIT " + (7 + id); prof.rarity = Rarity.Rare; prof.xp = 0; prof.status = "ASCEND SECURITY · KEEPING THE FLOOR SAFE"; }
            else
            {
                prof.displayName = Roster.Name(id); prof.rarity = Roster.Face(id); prof.xp = Roster.XP(id); prof.squad = Roster.Squad(id);
                prof.status = (wealth == Wealth.HighRoller ? "HIGH ROLLER · " : wealth == Wealth.Budget ? "ON A BUDGET · " : "") + "ON " + Tower.Floors[floor].name;
            }
            if (job == BotJob.Guest || job == BotJob.Seated) GuestLook.Apply(anim, wealth);
            bubble = Txt.Make("Bubble", transform, new Vector3(0, 2.15f, 0), "", 0.1f, Palette.Mint, Palette.Body, TextAlignmentOptions.Center, 2.4f);
            bubble.gameObject.AddComponent<Billboard>();
            until = Time.time + Random.Range(0f, 5f);
            nextEmote = Time.time + Random.Range(5f, 15f);
            nextBet = Time.time + Random.Range(1.5f, 6f);
            nextGlance = Time.time + Random.Range(1f, 4f);
            if (job == BotJob.Waiter) BuildTray();
            if (job == BotJob.Patrol && anim != null) anim.pose = Pose.Guard;
            if (takeSeat != null && takeSeat.Take(anim))
            {
                seat = takeSeat; job = BotJob.Seated;
                until = Time.time + Random.Range(60f, 160f);
                if (prof != null) prof.status = (wealth == Wealth.HighRoller ? "HIGH ROLLER · " : "") + Activity;
                SetupVenue();
                return;
            }
            AddAgent();
            Choose();
        }

        /// <summary>Starts the guest dancing on the spot (used for the dance-floor crowd).</summary>
        public void StartDancing(int style)
        {
            anim.danceStyle = style; anim.pose = Pose.Dance;
            // drop the errand Init() picked: otherwise the dancer walks off (still in the dance pose) as soon as a
            // waiter or a chat un-stops the agent, and keeps a bar / window spot reserved for nothing
            if (agent != null && agent.isOnNavMesh) agent.ResetPath();
            if (spot != null) { spot.taken = false; spot = null; }
            walking = false; until = Time.time + Random.Range(20f, 45f);
        }

        /// <summary>True when nobody else is dancing within 1.5 m and nothing solid is within arm's reach.</summary>
        bool RoomToDance()
        {
            foreach (var b in All)
                if (b != null && b != this && b.Dancing && (b.transform.position - transform.position).sqrMagnitude < 1.5f * 1.5f) return false;
            var hits = Physics.OverlapSphere(transform.position + Vector3.up * 1.1f, 0.55f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (h.GetComponentInParent<AvatarBody>() == null && h.GetComponentInParent<Rig>() == null) return false;
            return true;
        }

        void AddAgent()
        {
            if (agent != null)
            {
                agent.enabled = true;
                NavMeshHit h;
                if (NavMesh.SamplePosition(transform.position, out h, 3f, NavMesh.AllAreas)) agent.Warp(h.position);
                return;
            }
            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = 0.28f; agent.height = 1.8f;
            agent.speed = job == BotJob.Waiter ? 1.4f : job == BotJob.Patrol ? 1.1f : Random.Range(0.9f, 1.3f);
            agent.angularSpeed = 300f; agent.acceleration = 6f;
            agent.stoppingDistance = 0.25f; agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            agent.avoidancePriority = job == BotJob.Waiter ? 30 : job == BotJob.Patrol ? 40 : Random.Range(45, 70);
            agent.autoBraking = true;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }

        // ------------------------------------------------------------ props: waiter tray, drinks
        void BuildTray()
        {
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.2f, 0.2f, 0.015f, 24, Quaternion.identity, Palette.Silver, 0f);
            k.Torus(new Vector3(0, 0.01f, 0), 0.2f, 0.008f, 24, 4, Quaternion.identity, Palette.Gold, 0.8f);
            var hb = anim as HumanoidBody;
            // a person holds it at shoulder height on the right hand (the pack's waiter clips); a robot at its chest
            var trayPos = hb != null && hb.waiter ? new Vector3(0.26f, 1.36f, 0.18f) : new Vector3(0.3f, 1.05f, 0.25f);
            var g = k.Spawn("Tray", transform, trayPos, Quaternion.identity, Palette.Glossy, false);
            tray = g.transform;
            RefillTray();
        }

        void RefillTray()
        {
            trayDrinks = 3;
            for (int i = tray.childCount - 1; i >= 0; i--) Destroy(tray.GetChild(i).gameObject);
            for (int i = 0; i < 3; i++)
            {
                var p = Quaternion.Euler(0, i * 120f + 20f, 0) * new Vector3(0, 0, 0.1f);
                var cup = DrinkMesh(i).transform; cup.SetParent(tray, false); cup.localPosition = p + new Vector3(0, 0.01f, 0);
            }
        }

        public static GameObject DrinkMesh(int kind)
        {
            var k = new MeshKit();
            var c = kind % 3 == 0 ? Palette.Magenta : kind % 3 == 1 ? Palette.Teal : Palette.Gold;
            if (kind % 3 == 2)
            {
                // champagne flute
                k.Cylinder(new Vector3(0, 0.004f, 0), 0.03f, 0.03f, 0.008f, 12, Quaternion.identity, Palette.Text, 0.3f);
                k.Cylinder(new Vector3(0, 0.05f, 0), 0.004f, 0.004f, 0.09f, 6, Quaternion.identity, Palette.Text, 0.3f);
                k.Cylinder(new Vector3(0, 0.13f, 0), 0.016f, 0.024f, 0.09f, 12, Quaternion.identity, c, 0.8f);
            }
            else
            {
                k.Cylinder(new Vector3(0, 0.06f, 0), 0.028f, 0.035f, 0.12f, 12, Quaternion.identity, c, 0.7f);
                k.Cylinder(new Vector3(0, 0.14f, 0), 0.03f, 0.006f, 0.05f, 10, Quaternion.identity, Palette.Text, 0.3f);
                k.Bar(new Vector3(0.012f, 0.1f, 0), new Vector3(0.03f, 0.2f, 0), 0.004f, Palette.Mint, 1f);
            }
            return k.Spawn("Drink", null, Vector3.zero, Quaternion.identity, Palette.Glossy, false);
        }

        /// <summary>Hands this guest a drink (from a waiter, the bar, or the player).</summary>
        public void GiveDrink(int kind)
        {
            if (drink != null || anim == null || job == BotJob.Waiter || job == BotJob.Patrol) return;
            // held upright in the LEFT hand (the right stays free for chips and buttons); it follows the hand every
            // frame instead of being parented to it, so the glass never tilts with the wrist and never spills
            drink = DrinkMesh(kind).transform;
            drink.SetParent(transform.parent, true);
            drink.position = anim.LeftHand.position;
            nextSip = Time.time + Random.Range(3f, 8f);
            holdW = 0f; sipT = -1f;
            Destroy(drink.gameObject, Random.Range(80f, 160f));
        }

        float holdW, sipT = -1f;

        /// <summary>Glass in hand: arm bent with the glass at chest height; a sip lifts it to the lips, tips it and the
        /// head goes back a little. Chips carried by the right hand ride on the palm.</summary>
        void LateUpdate()
        {
            if (anim == null) return;
            if (carry.Count > 0)
            {
                var h = anim.RightHand;
                var basePos = h.position + Vector3.down * 0.035f + transform.forward * 0.03f;
                for (int i = 0; i < carry.Count; i++) if (carry[i] != null) carry[i].position = basePos + Vector3.up * (ChipKit.Thick * i);
            }
            if (drink == null) return;
            var hand = anim.LeftHand;
            float sip = sipT < 0f ? 0f : Mathf.Sin(Mathf.Clamp01(sipT / 1.8f) * Mathf.PI);
            drink.position = hand.position + transform.forward * 0.035f + Vector3.up * (0.02f + 0.02f * sip);
            drink.rotation = Quaternion.AngleAxis(-55f * sip, transform.right) * Quaternion.LookRotation(transform.forward, Vector3.up);
        }

        /// <summary>All arm motion of this guest goes through here (marked as the guest's own, see AvatarBody.driven).</summary>
        void Hand(bool right, Vector3 p, float w)
        {
            if (anim == null) return;
            anim.scripted = true; anim.Reach(right, p, w); anim.scripted = false;
            if (right) { handR = p; handRW = w; } else { handL = p; handLW = w; }
        }
        Vector3 handR, handL; float handRW, handLW;

        void HoldDrink()
        {
            if (anim == null) return;
            if (drink == null)
            {
                if (holdW > 0f) { holdW = Mathf.MoveTowards(holdW, 0f, Time.deltaTime * 2.5f); Hand(false, handL, holdW); if (sipT >= 0f) { sipT = -1f; anim.lookAt = null; } }
                return;
            }
            bool busyHand = Dancing;
            holdW = Mathf.MoveTowards(holdW, busyHand ? 0f : 0.75f, Time.deltaTime * 2.5f);
            var head = anim.HeadBone.position;
            var chest = head + Vector3.down * 0.42f + transform.forward * 0.26f - transform.right * 0.1f;
            var mouth = head + Vector3.down * 0.07f + transform.forward * 0.11f - transform.right * 0.02f;
            if (sipT >= 0f)
            {
                sipT += Time.deltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(sipT / 1.8f) * Mathf.PI);
                Hand(false, Vector3.Lerp(chest, mouth, Mathf.SmoothStep(0, 1, Mathf.Clamp01(k * 1.4f))), Mathf.Max(holdW, 0.9f * k));
                anim.lookAt = head + transform.forward * 0.6f + Vector3.up * (0.5f * k);
                if (sipT > 1.8f) { sipT = -1f; anim.lookAt = null; }
                return;
            }
            Hand(false, chest, holdW);
        }

        // ------------------------------------------------------------ talking
        public void Say(string msg)
        {
            bubble.text = msg; bubbleUntil = Time.time + 5f;
            // talking hands, and the people around look at whoever speaks
            anim.Play(Talking || Random.value < 0.25f ? Emote.Nod : Emote.Talk, Random.Range(1.6f, 2.6f));
            var head = anim.HeadBone;
            foreach (var b in All)
            {
                if (b == null || b == this || b.anim == null || b.job == BotJob.Waiter || b.Talking) continue;
                if ((b.transform.position - transform.position).sqrMagnitude < 6.25f) b.GlanceAt(head, Random.Range(1.5f, 3f));
            }
        }

        public void Turn(Vector3 target)
        {
            if (Seated) { anim.lookAt = target; return; }
            var d = target - transform.position; d.y = 0;
            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
            anim.lookAt = target;
        }

        /// <summary>The player started a conversation: stop, face them, keep eye contact.</summary>
        public void TalkStart(Transform who)
        {
            danceBeforeTalk = anim.pose == Pose.Dance;
            Talking = true; faceTarget = who;
            if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
            if (danceBeforeTalk) anim.pose = Pose.Idle;
            anim.lookAtTarget = who;
            anim.Play(Seated ? Emote.Nod : Emote.Wave, 1.2f);
        }

        public void TalkEnd()
        {
            Talking = false; faceTarget = null;
            if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
            if (danceBeforeTalk && anim != null) { anim.pose = Pose.Dance; anim.lookAtTarget = null; }
            danceBeforeTalk = false;
            // seated guests use 'until' as their stay-at-the-table timer: a chat must not make them get up
            if (!Seated) until = Time.time + Random.Range(2f, 6f);
        }

        /// <summary>Stop for a moment and face someone (a waiter bringing a drink).</summary>
        public void Hold(Transform who, float seconds)
        {
            holdUntil = Time.time + seconds; faceTarget = who;
            if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        }

        // ------------------------------------------------------------ glances (eyes and head never freeze on one point)
        float nextGlance, glanceUntil;
        Transform glanceT; Vector3 glanceP; bool glanceIsT;

        /// <summary>Look at someone for a moment (a neighbour talking, a big win at the table).</summary>
        public void GlanceAt(Transform t, float seconds)
        {
            if (t == null) return;
            glanceT = t; glanceIsT = true; glanceUntil = Time.time + seconds; nextGlance = glanceUntil + Random.Range(1f, 3f);
        }
        void GlanceAt(Vector3 p, float seconds) { glanceP = p; glanceIsT = false; glanceT = null; glanceUntil = Time.time + seconds; nextGlance = glanceUntil + Random.Range(0.5f, 2.5f); }

        /// <summary>Applies the current glance; true when one is active.</summary>
        bool ApplyGlance()
        {
            if (Time.time > glanceUntil || (glanceIsT && glanceT == null)) return false;
            if (glanceIsT) { anim.lookAtTarget = glanceT; }
            else { anim.lookAtTarget = null; anim.lookAt = glanceP; }
            return true;
        }

        AvatarBody Neighbour(float maxDist)
        {
            AvatarBody best = null; float bd = maxDist * maxDist;
            if (seat != null && seat.table != null)
            {
                foreach (var st in seat.table.seats)
                {
                    if (st == null || st == seat || st.occupant == null) continue;
                    float d = (st.transform.position - transform.position).sqrMagnitude;
                    if (d < bd && Random.value < 0.8f) { bd = d; best = st.occupant; }
                }
                if (best != null) return best;
            }
            foreach (var b in All)
            {
                if (b == null || b == this || b.anim == null || b.job == BotJob.Waiter) continue;
                float d = (b.transform.position - transform.position).sqrMagnitude;
                if (d < bd && Random.value < 0.7f) { bd = d; best = b.anim; }
            }
            return best;
        }

        // ------------------------------------------------------------ getting up
        /// <summary>Gets up from the seat (the player wants the machine, or the guest is done playing): the seated pose
        /// blends out while the guest steps back off the stool, then walks away.</summary>
        public void Leave()
        {
            if (seat == null) return;
            // a round in progress would touch released chips after its waits
            StopAllCoroutines();
            busy = false;
            ReleaseAllChips();
            Hand(true, handR, 0f); Hand(false, handL, 0f);
            anim.driven = false;
            seat.Leave();
            seat = null;
            venue = Venue.None;
            job = BotJob.Guest;
            StartCoroutine(StandUp());
        }

        IEnumerator StandUp()
        {
            standingUp = true;
            var from = transform.position; var to = from - transform.forward * 0.55f;
            for (float t = 0; t < 1f; t += Time.deltaTime)
            {
                // hips rise first, then the step back
                transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.2f) / 0.8f)));
                yield return null;
            }
            transform.position = to;
            standingUp = false;
            AddAgent();
            if (Random.value < 0.5f) anim.Play(Emote.Wave, 1.2f);
            Choose();
        }

        // ------------------------------------------------------------ choosing what to do
        void Choose()
        {
            if (spot != null) spot.taken = false;
            spot = null; seatTarget = null;
            if (job == BotJob.Waiter && ChooseServe()) return;
            if (job == BotJob.Guest && agent != null && Random.value < 0.3f && ChooseSeat()) return;
            if (spots == null || spots.Count == 0 || agent == null) return;
            for (int i = 0; i < 10; i++)
            {
                var s = spots[Odds.Range(0, spots.Count)];
                if (job == BotJob.Waiter && s.kind != SpotKind.Bar && s.kind != SpotKind.Wander && s.kind != SpotKind.Watch) continue;
                if (job == BotJob.Patrol && s.kind == SpotKind.Dance) continue;
                if (s.taken && s.kind != SpotKind.Dance && s.kind != SpotKind.Wander) continue;
                spot = s; break;
            }
            if (spot == null) spot = spots[Odds.Range(0, spots.Count)];
            spot.taken = true;
            var p = spot.transform.position + (spot.kind == SpotKind.Dance || spot.kind == SpotKind.Wander ? new Vector3(Random.Range(-1.2f, 1.2f), 0, Random.Range(-1.2f, 1.2f)) : Vector3.zero);
            if (spot.kind == SpotKind.Dance && Random.value < 0.5f) { spot.taken = false; spot = null; p = transform.position; walking = false; until = Time.time + 1f; return; }   // fewer people heading to the floor
            if (agent.isOnNavMesh) { agent.isStopped = false; agent.SetDestination(p); }
            walking = true;
            if (job != BotJob.Patrol) anim.pose = Pose.Idle;
            anim.lookAt = null;
        }

        // ------------------------------------------------------------ taking a seat on the way
        Seat seatTarget;

        /// <summary>A free stool / chair / sofa place nearby on this floor (not a poker seat: those are the regulars').</summary>
        bool ChooseSeat()
        {
            Seat best = null; float bd = 14f * 14f;
            foreach (var st in Seat.All)
            {
                if (st == null || !st.Free || !st.isActiveAndEnabled || st.table is PokerTable) continue;
                if (Mathf.Abs(st.transform.position.y - transform.position.y) > 1f) continue;
                bool taken = false;
                foreach (var b in All) if (b != null && b != this && b.seatTarget == st) { taken = true; break; }
                if (taken) continue;
                float d = (st.transform.position - transform.position).sqrMagnitude;
                if (d < bd && Random.value < 0.6f) { bd = d; best = st; }
            }
            if (best == null || !agent.isOnNavMesh) return false;
            seatTarget = best;
            var approach = best.transform.position - best.transform.forward * 0.6f;
            NavMeshHit h;
            if (NavMesh.SamplePosition(approach, out h, 0.8f, NavMesh.AllAreas)) approach = h.position;
            agent.isStopped = false; agent.SetDestination(approach);
            walking = true; anim.pose = Pose.Idle; anim.lookAt = null;
            return true;
        }

        /// <summary>Step in front of the seat, turn, and sit down (the body blends into the seated pose).</summary>
        IEnumerator SitDown(Seat st)
        {
            standingUp = true;   // the regular update keeps its hands off while we move
            if (agent != null) agent.enabled = false;
            var from = transform.position; var fromRot = transform.rotation;
            var to = st.transform.position; var toRot = Quaternion.Euler(0, st.transform.eulerAngles.y, 0);
            to.y = from.y;
            for (float t = 0; t < 0.9f; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / 0.9f);
                transform.rotation = Quaternion.Slerp(fromRot, toRot, Mathf.Clamp01(u * 1.6f));
                transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.25f) / 0.65f)));
                yield return null;
            }
            standingUp = false;
            seatTarget = null;
            if (st == null || !st.isActiveAndEnabled || !st.Take(anim)) { AddAgent(); Choose(); yield break; }
            seat = st; job = BotJob.Seated;
            until = Time.time + Random.Range(45f, 140f);
            nextBet = Time.time + Random.Range(2f, 5f);
            var prof = GetComponent<AvatarProfile>();
            if (prof != null) prof.status = (wealth == Wealth.HighRoller ? "HIGH ROLLER · " : "") + Activity;
            SetupVenue();
        }

        /// <summary>Waiter: pick a nearby guest without a drink (walking, watching, at the bar or seated) and bring one.</summary>
        bool ChooseServe()
        {
            if (trayDrinks <= 0 || agent == null || Random.value < 0.25f) return false;
            BotAvatar best = null; float bd = 14f * 14f;
            foreach (var b in All)
            {
                if (b == null || b == this || b.job == BotJob.Waiter || b.job == BotJob.Patrol || b.HasDrink || b.Talking) continue;
                float d = (b.transform.position - transform.position).sqrMagnitude;
                if (d < bd && Random.value < 0.7f) { bd = d; best = b; }
            }
            if (best == null) return false;
            serving = best;
            until = Time.time;   // the 25 s give-up timer in UpdateServing counts from now, not from the last errand
            if (agent.isOnNavMesh) { agent.isStopped = false; agent.SetDestination(best.transform.position + (transform.position - best.transform.position).normalized * 0.9f); }
            walking = true;
            return true;
        }

        void UpdateServing()
        {
            // the guest left the floor, already got a drink elsewhere or is busy talking to the player: give up
            if (serving == null || !serving.isActiveAndEnabled || serving.HasDrink || serving.Talking) { serving = null; walking = false; until = 0; return; }
            var d = serving.transform.position - transform.position; d.y = 0;
            if (d.magnitude > 1.25f)
            {
                if (Time.frameCount % 20 == 0 && agent.isOnNavMesh) agent.SetDestination(serving.transform.position - d.normalized * 0.9f);
                if (Time.time > until + 25f) { serving = null; walking = false; }
                return;
            }
            // arrived: both stop and face each other, hand the drink over
            if (agent.isOnNavMesh) agent.ResetPath();
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d.normalized);
            serving.Hold(transform, 2.2f);
            anim.Play(Emote.Nod, 1.2f);
            if (tray != null && tray.childCount > 0) Destroy(tray.GetChild(tray.childCount - 1).gameObject);
            trayDrinks--;
            serving.GiveDrink(Random.Range(0, 3));
            serving.Say(serving.wealth == Wealth.HighRoller ? "keep the change" : Random.value < 0.5f ? "thanks!" : "cheers!");
            Sfx.Play("chip_clack1", transform.position + Vector3.up, 0.3f, 1.4f);
            serving = null; walking = false;
            until = Time.time + Random.Range(1.5f, 3f);
            if (trayDrinks <= 0) RefillTrayLater();
        }

        void RefillTrayLater() { Invoke("RefillTray", 18f); }

        // ------------------------------------------------------------ update
        void Update()
        {
            if (bubble.text.Length > 0 && Time.time > bubbleUntil) bubble.text = "";
            if (standingUp) { HoldDrink(); return; }
            if (faceTarget != null && (Talking || Time.time < holdUntil))
            {
                var d = faceTarget.position - transform.position; d.y = 0;
                if (d.sqrMagnitude > 0.01f && !Seated) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), Time.deltaTime * 6f);
                anim.lookAtTarget = faceTarget;
                HoldDrink();   // a drink handed over mid-conversation comes up to the chest instead of dangling
                return;
            }
            if (faceTarget != null && Time.time >= holdUntil && !Talking)
            {
                faceTarget = null;
                if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
            }
            Sip();
            if (Seated) { SeatedUpdate(); return; }
            if (agent == null || !agent.isOnNavMesh) return;
            if (job == BotJob.Waiter && serving != null) { UpdateServing(); return; }
            if (walking)
            {
                if (seatTarget != null && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.15f)
                {
                    walking = false;
                    StartCoroutine(SitDown(seatTarget));
                    return;
                }
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.05f)
                {
                    walking = false;
                    until = Time.time + (job == BotJob.Waiter ? Random.Range(3f, 6f) : job == BotJob.Patrol ? Random.Range(4f, 9f) : spot != null && spot.kind == SpotKind.Dance ? Random.Range(12f, 25f) : Random.Range(10f, 35f));
                    if (spot != null)
                    {
                        transform.rotation = spot.transform.rotation;
                        // dance only with room around (no arms through a neighbour); otherwise watch and clap
                        if (job == BotJob.Guest) anim.pose = spot.kind == SpotKind.Dance && RoomToDance() ? Pose.Dance : Pose.Idle;
                        if (job == BotJob.Guest && spot.kind == SpotKind.Dance && anim.pose != Pose.Dance) anim.Play(Emote.Clap, 2f);
                        if (job == BotJob.Guest && spot.kind == SpotKind.Bar && !HasDrink) GiveDrink(Random.Range(0, 3));
                        if (job == BotJob.Waiter) { anim.Play(Emote.Wave, 1.2f); if (spot.kind == SpotKind.Bar && trayDrinks < 3) RefillTray(); }
                    }
                }
                return;
            }
            if (Time.time > nextEmote)
            {
                nextEmote = Time.time + Random.Range(6f, 18f);
                if (job == BotJob.Patrol) { if (Random.value < 0.3f) anim.Play(Emote.Nod, 1.0f); }
                else if (spot != null && spot.kind == SpotKind.Watch) anim.Play(Random.value < 0.5f ? Emote.Clap : Emote.Point, 1.6f);
                else if (!Dancing && Random.value < 0.25f) anim.Play(Emote.Wave, 1.4f);
                else if (!Dancing && Random.value < 0.4f) anim.Play(Emote.Talk, 2f);
            }
            if (Rig.I != null && (Rig.I.Head.position - transform.position).sqrMagnitude < 6f && anim.pose != Pose.Dance) anim.lookAtTarget = Rig.I.Head;
            else if (anim.pose != Pose.Dance && job != BotJob.Waiter && StandingGlance()) { }
            else { anim.lookAtTarget = null; if (anim.pose == Pose.Dance) anim.lookAt = null; }
            if (Time.time > until) Choose();
        }

        /// <summary>Standing guests glance at the people around them and at the games, now and then.</summary>
        bool StandingGlance()
        {
            if (ApplyGlance()) return true;
            if (Time.time < nextGlance) { anim.lookAtTarget = null; anim.lookAt = null; return true; }
            nextGlance = Time.time + Random.Range(3f, 8f);
            if (Random.value < 0.55f)
            {
                var n = Neighbour(3.5f);
                if (n != null) { GlanceAt(n.HeadBone, Random.Range(1.2f, 2.8f)); return ApplyGlance(); }
            }
            GlanceAt(anim.HeadBone.position + transform.forward * 3f + transform.right * Random.Range(-2f, 2f) + Vector3.up * Random.Range(-0.6f, 0.3f), Random.Range(1f, 2.5f));
            return ApplyGlance();
        }

        void Sip()
        {
            HoldDrink();
            if (drink == null || Time.time < nextSip || Dancing || sipT >= 0f || busy) return;
            nextSip = Time.time + Random.Range(6f, 14f);
            sipT = 0f;
        }

        void OnDestroy()
        {
            if (drink != null) Destroy(drink.gameObject);
            if (spot != null) spot.taken = false;
            if (seat != null) seat.occupant = null;
            ReleaseAllChips();
        }

        // ============================================================ seated play
        enum Venue { None, Table, Slots, Station, Bar, Lounge }
        Venue venue;
        bool busy;                     // a round / gesture coroutine owns the right arm
        long bank, unit;               // chips in front of the guest, and their usual bet
        Vector3 stackPos, betPos, dealerPos, intakePos, screenPos, buttonPos;   // world
        Transform chipParent, spinButton;
        readonly List<Transform> stack = new List<Transform>();   // chips in the guest's own stack (tidy columns)
        readonly List<Transform> pile = new List<Transform>();    // chips out on the felt (bet + winnings)
        readonly List<Transform> carry = new List<Transform>();   // chips in the right hand
        readonly List<Transform> owned = new List<Transform>();   // every chip this guest has out (for clean-up)
        float lastForeign, slotSpinStart = -1f, slotWait;
        static readonly List<long> split = new List<long>(48);

        void SetupVenue()
        {
            venue = Venue.Lounge;
            if (seat == null) return;
            anim.driven = true;
            var t = seat.table;
            if (t == null)
            {
                venue = seat.tableEdge ? Venue.Bar : Venue.Lounge;
                if (venue == Venue.Bar ? Random.value < 0.7f : Random.value < 0.3f) GiveDrink(Random.Range(0, 3));
                return;
            }
            venue = t is Slots ? Venue.Slots : t is ArcadeStation ? Venue.Station : Venue.Table;
            chipParent = t.transform;
            // everything is laid out in front of this guest, on the real surface, inside the table outline
            float edge = seat.EdgeZ; if (edge < 0f) edge = 0.42f;
            stackPos = OnFelt(new Vector3(venue == Venue.Slots ? -0.22f : 0.2f, 0, edge + (venue == Venue.Table ? 0.1f : 0.09f)), 0.09f);
            betPos = OnFelt(new Vector3(-0.03f, 0, edge + (venue == Venue.Table ? 0.3f : 0.2f)), 0.13f);
            var dl = t.OnTable(new Vector3(0, 0, 0.5f), 0.15f);
            dealerPos = t.transform.TransformPoint(new Vector3(dl.x, t.TableY + 0.01f, dl.z));
            intakePos = t.transform.TransformPoint(new Vector3(0, t.TableY + 0.3f, 0.3f));
            screenPos = venue == Venue.Slots ? t.transform.TransformPoint(new Vector3(0, 1.62f, 0.42f)) : t.transform.TransformPoint(new Vector3(0, 1.65f, 0.35f));
            spinButton = venue == Venue.Slots ? t.transform.Find("Btn_Spin") : null;
            buttonPos = spinButton != null ? spinButton.position + Vector3.up * 0.025f : OnFelt(new Vector3(0.12f, 0, edge + 0.24f), 0.1f) + Vector3.up * 0.01f;
            // chips: budget players bet hundreds, regulars five hundreds, high rollers the big plaques
            unit = wealth == Wealth.HighRoller ? 5000 : wealth == Wealth.Budget ? 100 : 500;
            bank = unit * (venue == Venue.Slots ? Random.Range(6, 12) : wealth == Wealth.HighRoller ? Random.Range(14, 26) : wealth == Wealth.Budget ? Random.Range(8, 16) : Random.Range(10, 22));
            LayoutStack();
            lastForeign = anim.ForeignReachTime;
        }

        /// <summary>Seat-space point (x, z) → world point on the playing surface, clamped inside the table outline.</summary>
        Vector3 OnFelt(Vector3 seatLocal, float margin)
        {
            var t = seat.table;
            var w = seat.W(seatLocal);
            var l = t.OnTable(t.transform.InverseTransformPoint(w), margin);
            w = t.transform.TransformPoint(l);
            // surface height measured at that distance in front of the seat
            var back = Quaternion.Inverse(Quaternion.Euler(0, seat.transform.eulerAngles.y, 0)) * (w - seat.transform.position);
            w.y = seat.transform.position.y + seat.SurfaceAt(back.z);
            return w;
        }

        // ---- chips (pooled visuals; at most 10 per column, never floating)
        static Transform poolRoot;
        static readonly Stack<Transform> pool = new Stack<Transform>();

        Transform GetChip(long value, Vector3 pos)
        {
            Transform c = null;
            while (pool.Count > 0 && c == null) c = pool.Pop();
            if (c == null) c = ChipKit.Visual(value, null, pos).transform;
            else c.GetComponent<MeshFilter>().sharedMesh = ChipKit.MeshFor(ChipKit.Index(value));
            c.SetParent(chipParent, true);
            c.position = pos; c.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            c.gameObject.SetActive(true);
            owned.Add(c);
            return c;
        }

        void PutBack(Transform c)
        {
            if (c == null) return;
            owned.Remove(c);
            Pool(c);
        }

        static void Pool(Transform c)
        {
            if (poolRoot == null) { var g = new GameObject("GuestChipPool"); g.SetActive(false); poolRoot = g.transform; pool.Clear(); }
            c.SetParent(poolRoot, false);
            if (pool.Count < 160) pool.Push(c); else Destroy(c.gameObject);
        }

        void ReleaseAllChips()
        {
            // includes chips caught mid-flight by a guest leaving or the floor switching off
            for (int i = 0; i < owned.Count; i++) if (owned[i] != null) Pool(owned[i]);
            owned.Clear(); stack.Clear(); pile.Clear(); carry.Clear();
        }

        /// <summary>Chips worth amount: mostly in the guest's own denomination, bigger plaques only to keep it tidy.</summary>
        void Split(long amount, int maxChips)
        {
            split.Clear();
            var v = ChipKit.Values;
            int top = ChipKit.Index(unit);
            while (top < v.Length - 1 && amount / v[top] > maxChips) top++;
            for (int i = top; i >= 0 && amount > 0 && split.Count < maxChips; i--)
                while (amount >= v[i] && split.Count < maxChips) { split.Add(v[i]); amount -= v[i]; }
        }

        /// <summary>The guest's own stack: columns of up to 10 chips side by side (a second row behind when it grows).</summary>
        Vector3 StackSlot(int i)
        {
            int col = i / 10, lvl = i % 10;
            var right = seat.transform.right; var fwd = seat.transform.forward;
            float d = ChipKit.Radius * 2f + 0.004f;
            return stackPos + right * ((col % 4) - 1.5f) * d * (venue == Venue.Slots ? -1f : 1f) + fwd * (col / 4) * d + Vector3.up * (ChipKit.Thick * (lvl + 0.5f));
        }

        void LayoutStack()
        {
            Split(bank, 40);
            // reuse the transforms already there, top up or put back the difference
            while (stack.Count > split.Count) { PutBack(stack[stack.Count - 1]); stack.RemoveAt(stack.Count - 1); }
            for (int i = 0; i < split.Count; i++)
            {
                var p = StackSlot(i);
                if (i < stack.Count) { stack[i].GetComponent<MeshFilter>().sharedMesh = ChipKit.MeshFor(ChipKit.Index(split[i])); stack[i].position = p; }
                else stack.Add(GetChip(split[i], p));
            }
        }

        Vector3 StackTop { get { return stack.Count > 0 ? stack[stack.Count - 1].position + Vector3.up * ChipKit.Thick : stackPos; } }

        /// <summary>Pile slot next to the bet: tidy columns of 10 growing sideways (bet, then winnings beside it).</summary>
        Vector3 PileSlot(int i)
        {
            int col = i / 10, lvl = i % 10;
            float d = ChipKit.Radius * 2f + 0.004f;
            return betPos + seat.transform.right * (col - 0.0f) * d + Vector3.up * (ChipKit.Thick * (lvl + 0.5f));
        }

        // ---- arm motion: eased arcs with a lift, never a straight snap
        IEnumerator Move(bool right, Vector3 to, float dur, float lift, float w1)
        {
            Vector3 from = right ? (handRW > 0.01f ? handR : anim.RestHand(true)) : (handLW > 0.01f ? handL : anim.RestHand(false));
            float w0 = right ? handRW : handLW;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / dur);
                var p = Vector3.Lerp(from, to, u) + Vector3.up * lift * Mathf.Sin(u * Mathf.PI);
                Hand(right, p, Mathf.Lerp(w0, w1, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / (dur * 0.7f)))));
                yield return null;
            }
            Hand(right, to, w1);
        }

        IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.deltaTime) yield return null; }

        /// <summary>Slides / arcs a set of chips to their slots (dealer pushes, sweeps, collecting the winnings).</summary>
        IEnumerator Fly(List<Transform> chips, Vector3 from, float dur, float lift, float stagger, bool toStack, Vector3 target, int slot0)
        {
            int n = chips.Count;
            float total = dur + stagger * n;
            var start = new Vector3[n];
            for (int i = 0; i < n; i++) start[i] = chips[i] != null ? chips[i].position : from;
            for (float t = 0; t < total; t += Time.deltaTime)
            {
                for (int i = 0; i < n; i++)
                {
                    if (chips[i] == null) continue;
                    float u = Mathf.Clamp01((t - i * stagger) / dur);
                    // toStack: a column at target (sweeps, the coin slot) · slot0 >= 0: the pile beside the bet ·
                    // slot0 == -2: the guest's own stack, into its next free column slots
                    var end = slot0 == -2 ? StackSlot(stack.Count + i) : toStack ? target + Vector3.up * (ChipKit.Thick * i) : (slot0 >= 0 ? PileSlot(slot0 + i) : target);
                    float e = Mathf.SmoothStep(0f, 1f, u);
                    chips[i].position = Vector3.Lerp(start[i], end, e) + Vector3.up * lift * Mathf.Sin(e * Mathf.PI);
                }
                yield return null;
            }
        }

        // ---- the seated update
        void SeatedUpdate()
        {
            anim.driven = true;
            if (!busy && Time.time > nextBet && (venue == Venue.Table || venue == Venue.Station))
            {
                busy = true;
                StartCoroutine(venue == Venue.Table ? TableRound() : StationRound());
            }
            if (venue == Venue.Slots) SlotsUpdate();
            if (!busy && Time.time > nextEmote)
            {
                nextEmote = Time.time + Random.Range(8f, 20f);
                float r = Random.value;
                if ((venue == Venue.Table || venue == Venue.Station) && stack.Count > 2 && r < 0.35f) { busy = true; StartCoroutine(Fiddle()); }
                else if (r < 0.6f && venue != Venue.Slots)
                {
                    // small talk with a neighbour
                    var n = Neighbour(1.9f);
                    if (n != null) { GlanceAt(n.HeadBone, 2.5f); anim.Play(Emote.Talk, 2.2f); }
                }
            }
            // eyes: the player when close, else glances at the dealer / reels / neighbours / own chips
            if (Rig.I != null && (Rig.I.Head.position - transform.position).sqrMagnitude < 5f && !busy) anim.lookAtTarget = Rig.I.Head;
            else SeatedGlance();
            if (Time.time > until && !busy && (seat.table == null || seat != seat.table.PlayerSeat)) { if (Random.value < 0.35f) Leave(); else until = Time.time + Random.Range(40f, 100f); }
        }

        void SeatedGlance()
        {
            var t = seat.table;
            if (ApplyGlance()) return;
            // between glances: eyes on the game (or straight ahead in the lounge / at the bar)
            anim.lookAtTarget = null;
            anim.lookAt = t != null ? (venue == Venue.Table ? t.transform.TransformPoint(new Vector3(0, t.TableY + 0.35f, 0.4f)) : screenPos)
                                    : anim.HeadBone.position + transform.forward * 3f + Vector3.down * 0.2f;
            if (Time.time < nextGlance) return;
            nextGlance = Time.time + Random.Range(2.5f, 6f);
            float r = Random.value;
            if (t != null && r < 0.35f)
            {
                var focus = venue == Venue.Table ? t.transform.TransformPoint(new Vector3(0, t.TableY + 0.45f, 0.5f)) : screenPos;   // the dealer / the screen
                GlanceAt(focus + Random.insideUnitSphere * 0.15f, Random.Range(1.5f, 3.5f));
            }
            else if (r < 0.7f)
            {
                var n = Neighbour(2.2f);
                if (n != null) GlanceAt(n.HeadBone, Random.Range(1f, 2.2f));
                else GlanceAt(anim.HeadBone.position + transform.forward * 2f + transform.right * Random.Range(-1.5f, 1.5f), 1.5f);
            }
            else if (t != null && stack.Count > 0) GlanceAt(stackPos, Random.Range(0.8f, 1.6f));
            else GlanceAt(anim.HeadBone.position + transform.forward * 3f + transform.right * Random.Range(-2.5f, 2.5f) + Vector3.down * 0.3f, Random.Range(1.5f, 3f));
        }

        /// <summary>Idle hands: riffle the top chips of the stack and put the hand back on the rail.</summary>
        IEnumerator Fiddle()
        {
            GlanceAt(stackPos, 2f);
            yield return Move(true, StackTop + Vector3.up * 0.025f, 0.5f, 0.03f, 0.9f);
            for (int i = 0; i < 3; i++)
            {
                yield return Move(true, StackTop + Vector3.up * 0.045f, 0.18f, 0f, 0.9f);
                Sfx.Chip(stackPos, 0.12f);
                yield return Move(true, StackTop + Vector3.up * 0.02f, 0.18f, 0f, 0.9f);
            }
            yield return Move(true, anim.RestHand(true), 0.5f, 0.03f, 0f);
            busy = false;
        }

        // ---- table rounds, in step with the table's real rounds
        IEnumerator TableRound()
        {
            var t = seat.table;
            // bets go down only while the table takes bets
            // (a table locked to a higher tier never opens: after a while the guest plays at their own pace)
            for (float g = 0; g < 10f && !t.AcceptingBets; g += Time.deltaTime) yield return null;
            if (bank < unit) Rebuy();
            int units = wealth == Wealth.HighRoller ? Random.Range(1, 4) : wealth == Wealth.Budget ? 1 : Random.Range(1, 3);
            long bet = System.Math.Min(bank, unit * units);
            // the kind of bet decides the odds: even money most of the time, sometimes 2:1, now and then a long shot
            float r = Random.value; float pay; double p;
            if (r < 0.72f) { pay = 2f; p = 0.47; } else if (r < 0.94f) { pay = 3f; p = 0.315; } else { pay = 9f; p = 0.1; }
            yield return PlaceBet(bet);
            // watch the game: the dealer, the felt
            GlanceAt(t.transform.TransformPoint(new Vector3(0, t.TableY + 0.3f, 0.35f)), 3f);
            bool openAtBet = t.AcceptingBets, sawRound = false; float waited = 0f, virt = Random.Range(5f, 9f);
            while (true)
            {
                waited += Time.deltaTime;
                if (openAtBet && !t.AcceptingBets) sawRound = true;                                         // the table's round started
                if (sawRound && t.AcceptingBets) { yield return Wait(Random.Range(0.25f, 0.9f)); break; }   // settled with the real round
                if (!sawRound && waited > virt) break;                                                       // nobody else playing: own pace
                if (waited > 60f) break;
                yield return null;
            }
            bool win = Odds.R() < p;
            if (win) yield return Win(bet, (long)(bet * (double)(pay - 1f)), pay >= 9f);
            else yield return Lose();
            nextBet = Time.time + Random.Range(2f, 6f);
            busy = false;
        }

        IEnumerator PlaceBet(long bet)
        {
            GlanceAt(stackPos, 1.2f);
            yield return Move(true, StackTop + Vector3.up * 0.02f, 0.5f, 0.04f, 1f);
            // pick the chips off the top of the stack (the stack re-tidies into columns)
            bank -= bet;
            Split(bet, 10);
            var top = StackTop;
            for (int i = 0; i < split.Count; i++) carry.Add(GetChip(split[i], top + Vector3.up * (ChipKit.Thick * i)));
            LayoutStack();
            Sfx.Chip(top, 0.25f);
            GlanceAt(betPos, 1.4f);
            yield return Move(true, betPos + Vector3.up * (0.035f + ChipKit.Thick * carry.Count), 0.6f, 0.09f, 1f);
            // set them down in a neat column on the felt
            for (int i = 0; i < carry.Count; i++) { carry[i].position = PileSlot(pile.Count); pile.Add(carry[i]); }
            carry.Clear();
            Sfx.Chip(betPos, 0.35f);
            yield return Move(true, anim.RestHand(true), 0.5f, 0.04f, 0f);
        }

        IEnumerator Win(long bet, long won, bool longShot)
        {
            var t = seat.table;
            bool big = longShot || won >= unit * 8;
            // the dealer pushes the winnings next to the bet, a chip at a time
            Split(won, 20);
            var paid = new List<Transform>(split.Count);
            for (int i = 0; i < split.Count; i++) paid.Add(GetChip(split[i], dealerPos + Vector3.up * (ChipKit.Thick * i)));
            int slot0 = ((pile.Count + 9) / 10) * 10;   // next empty column
            Sfx.Play("chips_payout", betPos, 0.22f);
            yield return Fly(paid, dealerPos, 0.45f, 0.06f, 0.05f, false, betPos, slot0);
            pile.AddRange(paid);
            Celebrate(betPos, big, won);
            if (big && t != null)
                foreach (var st in t.seats) if (st != null && st != seat && st.occupant != null && Random.value < 0.75f) st.occupant.Play(Random.value < 0.6f ? Emote.Clap : Emote.Cheer, 1.6f);
            yield return Wait(big ? 1.2f : 0.6f);
            // rake it all back into the stack
            GlanceAt(betPos, 1.5f);
            yield return Move(true, betPos + Vector3.up * 0.03f + seat.transform.forward * 0.03f, 0.45f, 0.04f, 1f);
            var all = new List<Transform>(pile); pile.Clear();
            var to = StackTop;
            for (float u = 0; u < 0.5f; u += Time.deltaTime)
            {
                float e = Mathf.SmoothStep(0f, 1f, u / 0.5f);
                Hand(true, Vector3.Lerp(betPos, to, e) + Vector3.up * 0.03f, 1f);
                yield return null;
            }
            StartCoroutine(Fly(all, betPos, 0.35f, 0.02f, 0.01f, true, to, -2));
            yield return Wait(0.4f + 0.01f * all.Count);
            for (int i = 0; i < all.Count; i++) PutBack(all[i]);
            bank += bet + won;
            LayoutStack();
            Sfx.Chip(stackPos, 0.3f);
            yield return Move(true, anim.RestHand(true), 0.45f, 0.03f, 0f);
        }

        IEnumerator Lose()
        {
            // the dealer sweeps the bet away
            var lost = new List<Transform>(pile); pile.Clear();
            yield return Fly(lost, betPos, 0.55f, 0.015f, 0.02f, true, dealerPos, -1);
            for (int i = 0; i < lost.Count; i++) PutBack(lost[i]);
            Sfx.Chip(dealerPos, 0.2f);
            yield return Wait(0.15f);
            float r = Random.value;
            if (r < 0.4f) anim.Play(Emote.Facepalm, 1.4f);
            else if (r < 0.65f) anim.Play(Emote.Nod, 1.0f);      // "ok, next one"
            if (bank < unit) Rebuy();
        }

        void Rebuy()
        {
            bank += unit * Random.Range(6, 12);
            LayoutStack();
            Sfx.Play("chip_stack", stackPos, 0.3f);
        }

        void Celebrate(Vector3 at, bool big, long amount)
        {
            anim.Play(big ? Emote.Cheer : (Random.value < 0.5f ? Emote.Clap : Emote.Nod), big ? 1.8f : 1.3f);
            Juice.Sparks(at + Vector3.up * 0.05f, big ? Palette.Gold : Palette.Mint, big ? 45 : 14);
            WinRing.Show(new Vector3(at.x, at.y + 0.004f, at.z), big ? Palette.Gold : Palette.Mint, big ? 0.55f : 0.25f);
            if (big)
            {
                Juice.Burst("confetti", at + Vector3.up * 0.6f, 35);
                Sfx.Play("win_small", at, 0.45f);
                bubble.text = "+" + amount.ToString("N0"); bubbleUntil = Time.time + 3f;
                // the people around turn to look
                foreach (var b in All)
                    if (b != null && b != this && b.job != BotJob.Waiter && (b.transform.position - transform.position).sqrMagnitude < 16f) b.GlanceAt(anim.HeadBone, Random.Range(1.5f, 3f));
            }
            else Sfx.Play("chips_payout", at, 0.18f);
        }

        // ---- arcade stations: chips on the counter, press play, watch the screen
        IEnumerator StationRound()
        {
            if (bank < unit) Rebuy();
            long bet = System.Math.Min(bank, unit);
            yield return PlaceBet(bet);
            // press the play button on the console
            GlanceAt(buttonPos, 1f);
            yield return Move(true, buttonPos + Vector3.up * 0.05f, 0.45f, 0.04f, 1f);
            yield return Move(true, buttonPos, 0.12f, 0f, 1f);
            Sfx.Play("ui_click", buttonPos, 0.25f);
            yield return Move(true, buttonPos + Vector3.up * 0.05f, 0.15f, 0f, 1f);
            // the stake disappears into the machine
            var gone = new List<Transform>(pile); pile.Clear();
            StartCoroutine(Fly(gone, betPos, 0.35f, 0.12f, 0.04f, true, intakePos, -1));
            yield return Move(true, anim.RestHand(true), 0.5f, 0.03f, 0f);
            for (int i = 0; i < gone.Count; i++) PutBack(gone[i]);
            GlanceAt(screenPos, 5f);
            yield return Wait(Random.Range(2.5f, 5f));
            // honest machine maths: ~94 % return
            double r = Odds.R(); float mult = r < 0.004 ? 15f : r < 0.034 ? 4f : r < 0.414 ? 2f : 0f;
            if (mult > 0f)
            {
                long won = (long)(bet * (double)mult);
                Split(won, 16);
                var paid = new List<Transform>(split.Count);
                for (int i = 0; i < split.Count; i++) paid.Add(GetChip(split[i], intakePos));
                yield return Fly(paid, intakePos, 0.4f, 0.1f, 0.05f, false, betPos, 0);
                pile.AddRange(paid);
                Celebrate(betPos, mult >= 4f, won - bet);
                yield return Wait(0.8f);
                yield return Move(true, betPos + Vector3.up * 0.03f, 0.4f, 0.04f, 1f);
                var all = new List<Transform>(pile); pile.Clear();
                StartCoroutine(Fly(all, betPos, 0.35f, 0.02f, 0.01f, true, StackTop, -2));
                yield return Move(true, StackTop + Vector3.up * 0.03f, 0.4f, 0f, 1f);
                for (int i = 0; i < all.Count; i++) PutBack(all[i]);
                bank += won; LayoutStack();
                yield return Move(true, anim.RestHand(true), 0.45f, 0.03f, 0f);
            }
            else if (Random.value < 0.45f) anim.Play(Emote.Facepalm, 1.3f);
            nextBet = Time.time + Random.Range(2.5f, 7f);
            busy = false;
        }

        // ---- slots: the machine runs the guest's spins (Slots.Spin(guest)); the guest's hand presses SPIN when the
        // spin starts, a credit chip goes into the coin slot, eyes on the reels, then the reaction and the payout
        void SlotsUpdate()
        {
            float fr = anim.ForeignReachTime;
            bool started = fr != lastForeign && anim.ForeignReachWeight > 0f;
            bool ended = fr != lastForeign && anim.ForeignReachWeight <= 0f;
            lastForeign = fr;
            if (started)
            {
                slotSpinStart = Time.time;
                if (!busy) { busy = true; StartCoroutine(SlotPress()); }
            }
            else if (ended && slotSpinStart >= 0f)
            {
                slotSpinStart = -1f; slotWait = Time.time;
                StartCoroutine(SlotResult());
            }
            else if (!busy && slotSpinStart < 0f && Time.time - slotWait > 1.6f)
            {
                // between spins the finger hovers by the SPIN button
                Hand(true, Vector3.Lerp(handRW > 0.01f ? handR : anim.RestHand(true), buttonPos + Vector3.up * 0.06f, 1f - Mathf.Exp(-3f * Time.deltaTime)), Mathf.MoveTowards(handRW, 0.85f, Time.deltaTime));
            }
            if (slotSpinStart >= 0f) GlanceAt(screenPos + Vector3.up * Mathf.Sin(Time.time * 2f) * 0.03f, 0.5f);
        }

        IEnumerator SlotPress()
        {
            yield return Move(true, buttonPos + Vector3.up * 0.04f, 0.12f, 0f, 1f);
            yield return Move(true, buttonPos, 0.09f, 0f, 1f);
            yield return Move(true, buttonPos + Vector3.up * 0.06f, 0.15f, 0f, 0.9f);
            // a credit chip into the coin slot
            if (bank < unit) Rebuy();
            bank -= unit;
            var top = StackTop;
            LayoutStack();
            var coin = new List<Transform> { GetChip(unit, top) };
            yield return Fly(coin, top, 0.35f, 0.12f, 0f, true, intakePos, -1);
            PutBack(coin[0]);
            yield return Move(true, anim.RestHand(true), 0.5f, 0.03f, 0.2f);
            busy = false;
        }

        IEnumerator SlotResult()
        {
            yield return null;   // the machine plays its reaction emote right after letting go of the arm
            busy = true;
            bool fresh = Time.time - anim.LastEmoteTime < 0.2f;
            bool big = fresh && anim.LastEmote == Emote.Cheer, win = fresh && (big || anim.LastEmote == Emote.Clap);
            if (win)
            {
                // the machine pays into the tray in front of the guest; they scoop it onto their stack
                long won = unit * (big ? Random.Range(15, 40) : Random.Range(2, 5));
                Split(won, big ? 20 : 6);
                var paid = new List<Transform>(split.Count);
                for (int i = 0; i < split.Count; i++) paid.Add(GetChip(split[i], intakePos));
                Juice.Sparks(screenPos, big ? Palette.Gold : Palette.Mint, big ? 40 : 12);
                WinRing.Show(seat.transform.position + Vector3.up * 0.02f, big ? Palette.Gold : Palette.Mint, big ? 0.9f : 0.5f);
                yield return Fly(paid, intakePos, 0.4f, 0.12f, 0.05f, false, betPos, 0);
                pile.AddRange(paid);
                if (big)
                {
                    bubble.text = "+" + won.ToString("N0"); bubbleUntil = Time.time + 3f;
                    Juice.Burst("confetti", screenPos + Vector3.up * 0.4f, 35);
                    foreach (var b in All)
                        if (b != null && b != this && b.job != BotJob.Waiter && (b.transform.position - transform.position).sqrMagnitude < 16f)
                        { b.GlanceAt(anim.HeadBone, 2f); if (Random.value < 0.5f) b.anim.Play(Emote.Clap, 1.4f); }
                }
                yield return Wait(0.6f);
                yield return Move(true, betPos + Vector3.up * 0.03f, 0.4f, 0.04f, 1f);
                var all = new List<Transform>(pile); pile.Clear();
                StartCoroutine(Fly(all, betPos, 0.35f, 0.02f, 0.01f, true, StackTop, -2));
                yield return Move(true, StackTop + Vector3.up * 0.03f, 0.4f, 0f, 1f);
                for (int i = 0; i < all.Count; i++) PutBack(all[i]);
                bank += won; LayoutStack();
                yield return Move(true, anim.RestHand(true), 0.4f, 0.03f, 0.3f);
            }
            else if (!fresh && Random.value < 0.3f) anim.Play(Emote.Nod, 1f);
            slotWait = Time.time;
            busy = false;
        }
    }

    /// <summary>A light ring that spreads over the felt / floor under a win (shared mesh, pooled objects, no lights).</summary>
    public static class WinRing
    {
        static Mesh mesh;
        static readonly Stack<GameObject> free = new Stack<GameObject>();

        public static void Show(Vector3 pos, Color c, float radius)
        {
            var host = Juice.I; if (host == null) return;
            host.StartCoroutine(Run(pos, c, radius));
        }

        static IEnumerator Run(Vector3 pos, Color c, float radius)
        {
            GameObject go = null;
            while (free.Count > 0 && go == null) go = free.Pop();
            if (go == null)
            {
                if (mesh == null) { var k = new MeshKit(); k.Ring(Vector3.zero, 0.86f, 1f, 40, Quaternion.identity, Color.white, 1.5f); mesh = k.ToMesh("GuestWinRing"); }
                go = new GameObject("GuestWinRing");
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Palette.FlatGlow;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
            go.SetActive(true);
            go.transform.position = pos;
            var r = go.GetComponent<Renderer>();
            for (float t = 0; t < 0.8f; t += Time.deltaTime)
            {
                if (go == null) yield break;
                float f = t / 0.8f;
                go.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, radius, Fx.EaseOut(f));
                Fx.SetColor(r, c * (1f - f), 1.6f * (1f - f));
                yield return null;
            }
            if (go == null) yield break;
            go.SetActive(false);
            if (free.Count < 8) free.Push(go); else Object.Destroy(go);
        }
    }

    /// <summary>Accessories that show a guest's wealth: high rollers get a gold chain, shades and a gold watch; budget
    /// players a cap. Built at spawn in the bind pose and pinned to the head / chest bones.</summary>
    public static class GuestLook
    {
        public static void Apply(AvatarBody body, Wealth w)
        {
            if (body == null || w == Wealth.Regular) return;
            var head = body.HeadBone; var root = body.transform;
            if (head == null || head == root) return;
            float k = Mathf.Max(0.6f, (head.position.y - root.position.y) / 1.5f);
            var hb = body as HumanoidBody;
            bool helmet = hb != null && hb.style == AvatarStyle.NeonRonin;
            var fwd = root.forward; var up = Vector3.up;
            var hc = head.position + up * 0.1f * k;
            if (w == Wealth.HighRoller)
            {
                // gold chain round the neck with a chip medallion
                var ck = new MeshKit();
                ck.Torus(Vector3.zero, 0.1f * k, 0.008f * k, 24, 5, Quaternion.Euler(-20, 0, 0), Palette.Gold, 1f);
                ck.Cylinder(new Vector3(0, -0.07f, 0.1f) * k, 0.022f * k, 0.022f * k, 0.006f * k, 14, Quaternion.Euler(80, 0, 0), Palette.Gold, 1f);
                Pin(ck.Spawn("GoldChain", null, Vector3.zero, Quaternion.identity, Palette.Neon, false).transform, root, head.position - up * 0.02f * k + fwd * 0.01f, body.HeadBone.parent ?? head);
                if (!helmet)
                {
                    var gk = new MeshKit();
                    gk.Box(new Vector3(-0.034f, 0, 0) * k, new Vector3(0.05f, 0.026f, 0.008f) * k, Palette.Hex("08060C"), 0.05f);
                    gk.Box(new Vector3(0.034f, 0, 0) * k, new Vector3(0.05f, 0.026f, 0.008f) * k, Palette.Hex("08060C"), 0.05f);
                    gk.Bar(new Vector3(-0.07f, 0.01f, 0) * k, new Vector3(0.07f, 0.01f, 0) * k, 0.004f * k, Palette.Gold, 1f);
                    Pin(gk.Spawn("Shades", null, Vector3.zero, Quaternion.identity, Palette.Glossy, false).transform, root, hc + fwd * 0.1f * k + up * 0.01f * k, head);
                }
                var wk = new MeshKit(); wk.Torus(Vector3.zero, 0.035f * k, 0.008f * k, 16, 4, Quaternion.Euler(0, 0, 90), Palette.Gold, 1f);
                var lh = body.LeftHand;
                if (lh != null && lh != root) Pin(wk.Spawn("Watch", null, Vector3.zero, Quaternion.identity, Palette.Neon, false).transform, root, lh.position, lh);
            }
            else if (w == Wealth.Budget && !helmet)
            {
                var col = Color.HSVToRGB(Mathf.Repeat(root.name.GetHashCode() * 0.37f, 1f), 0.6f, 0.7f);
                var bk = new MeshKit();
                bk.Sphere(Vector3.zero, new Vector3(0.105f, 0.07f, 0.115f) * k, 14, col, 0.05f);
                bk.Box(new Vector3(0, -0.035f, 0.12f) * k, new Vector3(0.15f, 0.01f, 0.09f) * k, col * 0.8f, 0.05f);
                Pin(bk.Spawn("Cap", null, Vector3.zero, Quaternion.identity, Palette.Glossy, false).transform, root, hc + up * 0.045f * k, head);
            }
        }

        static void Pin(Transform t, Transform root, Vector3 world, Transform bone)
        {
            t.SetPositionAndRotation(world, root.rotation);
            t.SetParent(bone, true);
        }
    }
}
