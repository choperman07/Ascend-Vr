using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>THE VAULT · the raid's fighting ground. Starting a raid teleports you (and your squad) to a flat, open
    /// platform floating above the tower: no crates, no walls to hide behind — you dodge by moving. THE HOUSE gets a full
    /// attack deck that grows with each phase:
    ///   ORB VOLLEY  homing chips-of-light (shoot them or sidestep)
    ///   CARD FAN    a spread of razor cards thrown straight — strafe through the gaps
    ///   CHIP SLAM   red circles under you, then giant chips crash down — get out of the circle
    ///   ROULETTE    the floor turns into a wheel; red sectors explode — stand on green
    ///   LASER       the sweep you have to duck (phase 3: two sweeps back to back)
    ///   DRONES      phase 3 escorts that strafe and shoot
    /// Under 12% HP THE HOUSE goes ALL IN (enrage): shorter gaps and combined attacks.</summary>
    public partial class RaidArena
    {
        public static readonly Vector3 VaultOffset = new Vector3(0, 70f, 0);
        public const float VaultR = 17.5f;
        static readonly Vector3 VaultBoss = new Vector3(0, 6.5f, 9.5f);
        static readonly Vector3 VaultStart = new Vector3(0, 0, -9f);
        const int WedgeCount = 8;

        enum Atk { Orbs, Cards, Slam, Roulette, Laser, Combo }

        class Card { public Transform t; public Vector3 v; public float die; }

        Transform vault, hudT;
        bool inVault, enraged;
        Renderer[] wedges;
        Transform wedgeRoot;
        readonly List<Card> cards = new List<Card>();
        readonly List<GameObject> temp = new List<GameObject>();
        int attacking; float nextAttack, playerInvuln; Atk lastAtk = Atk.Combo;
        Vector3 bossHomePos; Quaternion bossHomeRot; Vector3 hudHomePos;
        readonly List<Vector3> squadHome = new List<Vector3>();

        Transform Field { get { return inVault && vault != null ? vault : transform; } }
        Vector3 BossLocal { get { return inVault ? VaultBoss : bossLocal; } }

        // ------------------------------------------------------------ build
        void BuildVault()
        {
            vault = Build.Empty("RaidVault", transform, VaultOffset).transform;
            var k = new MeshKit();
            // the slab: dark glossy disc with a gold rim and a glowing underside cone
            k.Cylinder(new Vector3(0, -0.25f, 0), VaultR + 0.8f, VaultR + 0.8f, 0.5f, 96, Quaternion.identity, Palette.Hex("140E20"), 0.04f, true, Palette.Hex("1A1228"), 0.06f);
            k.Cylinder(new Vector3(0, -3.0f, 0), 4f, VaultR + 0.6f, 5f, 64, Quaternion.identity, Palette.Hex("0E0A16"), 0.05f, false);
            k.Torus(new Vector3(0, 0.02f, 0), VaultR + 0.6f, 0.12f, 96, 6, Quaternion.identity, Palette.Gold, 1f);
            // casino floor markings: rings, spokes and a giant chip in the middle
            for (int r = 1; r <= 4; r++) k.Ring(new Vector3(0, 0.012f, 0), r * 4.2f - 0.04f, r * 4.2f + 0.04f, 96, Quaternion.identity, Palette.Violet, 0.8f);
            for (int i = 0; i < WedgeCount; i++)
            {
                float a = i * Mathf.PI * 2f / WedgeCount;
                k.Bar(new Vector3(Mathf.Sin(a) * 1.6f, 0.014f, Mathf.Cos(a) * 1.6f), new Vector3(Mathf.Sin(a) * VaultR, 0.014f, Mathf.Cos(a) * VaultR), 0.05f, Palette.Violet, 0.9f);
            }
            k.Disc(new Vector3(0, 0.016f, 0), 1.6f, 48, Quaternion.identity, Palette.Red, 0.6f);
            k.Ring(new Vector3(0, 0.018f, 0), 1.25f, 1.45f, 48, Quaternion.identity, Color.white, 1f);
            k.Ring(new Vector3(VaultStart.x, 0.02f, VaultStart.z), 0.45f, 0.52f, 40, Quaternion.identity, Palette.Mint, 1.2f);
            k.Spawn("VaultFloor", vault, Palette.Glossy, false);
            var floorCol = vault.gameObject.AddComponent<BoxCollider>();
            floorCol.center = new Vector3(0, -0.25f, 0); floorCol.size = new Vector3((VaultR + 1f) * 2f, 0.5f, (VaultR + 1f) * 2f);
            // energy barrier: glowing posts + two rings; invisible colliders keep everyone on the platform
            var bk = new MeshKit();
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI * 2f / 32f;
                var p = new Vector3(Mathf.Sin(a) * (VaultR + 0.6f), 0, Mathf.Cos(a) * (VaultR + 0.6f));
                bk.Bar(p, p + Vector3.up * 2.6f, 0.06f, i % 2 == 0 ? Palette.Magenta : Palette.Teal, 1.2f);
                var wall = Build.Empty("Barrier" + i, vault, p + Vector3.up * 1.5f, Quaternion.Euler(0, a * Mathf.Rad2Deg, 0));
                Build.BoxCol(wall, Vector3.zero, new Vector3(3.6f, 3f, 0.4f));
            }
            bk.Torus(new Vector3(0, 1.2f, 0), VaultR + 0.6f, 0.03f, 96, 4, Quaternion.identity, Palette.Teal, 1.4f);
            bk.Torus(new Vector3(0, 2.6f, 0), VaultR + 0.6f, 0.04f, 96, 4, Quaternion.identity, Palette.Magenta, 1.4f);
            bk.Spawn("Barrier", vault, Palette.Neon, false);
            Txt.Glow("VaultTitle", vault, new Vector3(0, 0.03f, -13.5f), "THE VAULT", 0.9f, Palette.Gold).transform.localRotation = Quaternion.Euler(90, 0, 0);
            // roulette sectors (hidden until THE HOUSE spins the floor)
            wedgeRoot = Build.Empty("Roulette", vault, new Vector3(0, 0.03f, 0)).transform;
            wedges = new Renderer[WedgeCount];
            for (int i = 0; i < WedgeCount; i++)
            {
                var wk = new MeshKit();
                var outline = new List<Vector2> { Vector2.zero };
                float a0 = i * 360f / WedgeCount, a1 = (i + 1) * 360f / WedgeCount;
                for (int s = 0; s <= 8; s++)
                {
                    float a = Mathf.Lerp(a0, a1, s / 8f) * Mathf.Deg2Rad;
                    outline.Add(new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (VaultR - 0.1f));
                }
                wk.Prism(outline, 0f, 0.02f, Color.white, 0.9f);
                var go = wk.Spawn("Sector" + i, wedgeRoot, Palette.FlatGlow, false);
                wedges[i] = go.GetComponent<Renderer>();
                wedges[i].enabled = false;
            }
            // floating lights over the platform
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var lamp = Build.GlowSprite(vault, new Vector3(Mathf.Sin(a) * 12f, 9f, Mathf.Cos(a) * 12f), 3f, i % 2 == 0 ? Palette.Magenta : Palette.Teal, 0.6f);
                lamp.gameObject.AddComponent<Bob>().amplitude = 0.5f;
            }
            var light = new GameObject("VaultLight").AddComponent<Light>();
            light.transform.SetParent(vault, false); light.transform.localPosition = new Vector3(0, 12f, 0);
            light.type = LightType.Point; light.range = 40f; light.intensity = 2.2f; light.color = Palette.Hex("FFD9F0");
            light.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------ travel
        IEnumerator EnterVault()
        {
            bossHomePos = boss.localPosition; bossHomeRot = boss.localRotation;
            hudHomePos = hudT != null ? hudT.localPosition : Vector3.zero;
            inVault = true;
            boss.SetParent(vault, false);
            boss.localPosition = VaultBoss; boss.localRotation = Quaternion.Euler(0, 180, 0); { var bb = boss.GetComponent<Bob>(); if (bb != null) bb.Rebase(); }
            if (hudT != null) { hudT.SetParent(vault, false); hudT.localPosition = VaultBoss + new Vector3(0, 3.6f, 0); }
            squadHome.Clear();
            for (int i = 0; i < squad.Count; i++)
            {
                squadHome.Add(squad[i].transform.localPosition);
                squad[i].transform.SetParent(vault, false);
                squad[i].transform.localPosition = VaultStart + new Vector3(i == 0 ? -2.6f : i == 1 ? 2.6f : 0f, 0, i == 2 ? -2.2f : -0.6f);
                squad[i].transform.localRotation = Quaternion.identity;
            }
            if (Rig.Ready)
            {
                Rig.I.Teleport(vault.TransformPoint(VaultStart), vault.eulerAngles.y, -1, "teleport");
                yield return new WaitForSeconds(0.65f);
                // Rig.Teleport is ignored while another teleport is still fading: never start the fight with the
                // player left at the console (PlayerLeftVault would end it at once and the entry would be lost)
                if (PlayerLeftVault()) Rig.I.Place(vault.TransformPoint(VaultStart), vault.eulerAngles.y);
            }
            Voice.Announce("ann_vault");
            GameState.I.Notify("THE VAULT · NO COVER · MOVE TO DODGE", Palette.Red);
        }

        IEnumerator LeaveVault()
        {
            if (!inVault) yield break;
            if (Rig.Ready && Rig.I.Head != null && (Rig.I.Head.position - vault.position).sqrMagnitude < 60f * 60f)
            {
                Rig.I.Teleport(Start0, transform.eulerAngles.y, -1, "teleport");
                yield return new WaitForSeconds(0.65f);
                // same guard on the way back: a dropped teleport would strand the player 70 m up behind the barrier
                if (Rig.Ready && (Rig.I.Head.position - vault.position).sqrMagnitude < 60f * 60f) Rig.I.Place(Start0, transform.eulerAngles.y);
            }
            ReturnHome();
        }

        void ReturnHome()
        {
            if (!inVault) return;
            inVault = false;
            boss.SetParent(transform, false); boss.localPosition = bossHomePos; boss.localRotation = bossHomeRot; { var bb = boss.GetComponent<Bob>(); if (bb != null) bb.Rebase(); }
            if (hudT != null) { hudT.SetParent(transform, false); hudT.localPosition = hudHomePos; }
            for (int i = 0; i < squad.Count && i < squadHome.Count; i++)
            {
                squad[i].transform.SetParent(transform, false);
                squad[i].transform.localPosition = squadHome[i]; squad[i].transform.localRotation = Quaternion.identity;
            }
        }

        bool PlayerLeftVault()
        {
            if (!inVault || !Rig.Ready || Rig.I.Head == null) return false;
            var d = Rig.I.Head.position - vault.position;
            return d.y < -8f || new Vector2(d.x, d.z).magnitude > VaultR + 6f;
        }

        /// <summary>Raid interrupted (player took the elevator / wrist teleport mid-fight): the entry is lost, everything resets.</summary>
        bool needReturn;
        void OnEnable()
        {
            if (!needReturn) return;
            needReturn = false;
            // a cinematic may have been cut anywhere: boss in the vault, scaled, tilted, crown flying, Bob disabled
            ReturnHome();
            ResetBoss();
            ResetTracers();
            boss.gameObject.SetActive(true);
            if (lootT != null) lootT.color = Palette.Gold;
            Idle();
        }

        void OnDisable()
        {
            if (!fighting) return;
            StopAllCoroutines();
            live = false;
            ClearAttacks();
            DestroyPlayerHud();
            EquipBlasters(false);
            if (heartbeat != null) { Destroy(heartbeat.gameObject); heartbeat = null; }
            hurt = 0; if (hurtR != null) hurtR.enabled = false;
            // reparenting / re-activating children is not allowed while the floor is being deactivated: do it on re-enable
            needReturn = true;
            if (GameState.Exists) GameState.I.Pay(0, stake, transform.position);
            fighting = false; if (startBtn != null) startBtn.SetInteractable(true);
            foreach (var b in squad) if (b != null) b.pose = Pose.Guard;
        }

        // ------------------------------------------------------------ attack deck
        float AttackGap()
        {
            float g = phase == 1 ? 1.7f : phase == 2 ? 1.2f : 0.9f;
            return enraged ? g * 0.55f : g;
        }

        Atk PickAttack()
        {
            var pool = new List<Atk> { Atk.Orbs, Atk.Cards, Atk.Slam };
            if (phase >= 2) { pool.Add(Atk.Roulette); pool.Add(Atk.Laser); pool.Add(Atk.Cards); }
            if (phase >= 3) { pool.Add(Atk.Combo); pool.Add(Atk.Slam); }
            if (enraged) { pool.Add(Atk.Combo); pool.Add(Atk.Combo); }
            Atk a; int guard = 0;
            do a = pool[Random.Range(0, pool.Count)]; while (a == lastAtk && ++guard < 6);
            lastAtk = a;
            return a;
        }

        IEnumerator RunAttack()
        {
            attacking++;
            var a = PickAttack();
            switch (a)
            {
                case Atk.Orbs: yield return StartCoroutine(OrbVolley(phase == 1 ? 3 : phase == 2 ? 4 : 6)); break;
                case Atk.Cards: yield return StartCoroutine(CardFan(phase == 1 ? 1 : 2)); break;
                case Atk.Slam: yield return StartCoroutine(ChipSlam(phase == 1 ? 3 : phase == 2 ? 4 : 6)); break;
                case Atk.Roulette: yield return StartCoroutine(Roulette()); break;
                case Atk.Laser:
                    yield return StartCoroutine(Laser());
                    if (phase >= 3) { yield return new WaitForSeconds(0.4f); yield return StartCoroutine(Laser()); }
                    break;
                case Atk.Combo:
                    StartCoroutine(OrbVolley(3));
                    if (Random.value < 0.5f) yield return StartCoroutine(ChipSlam(4)); else { StartCoroutine(CardFan(2)); yield return StartCoroutine(ChipSlam(3)); }
                    break;
            }
            attacking--;
            nextAttack = Time.time + AttackGap();
        }

        void Enrage()
        {
            enraged = true;
            Voice.Say("boss_enrage", boss, 3.2f);
            Sfx.Play("boss_roar", boss.position, 1f, 0.7f);
            GameState.I.Notify("THE HOUSE GOES ALL IN!", Palette.Red);
            StartCoroutine(Fx.Shake(boss, 0.2f, 1.5f));
            Juice.Sparks(core.position, Palette.Gold, 120);
            XRIn.HapticBoth(0.8f, 0.5f);
        }

        IEnumerator OrbVolley(int n)
        {
            // live, not fighting: fighting stays true through the victory/defeat cinematics, and a volley still running
            // when the boss died kept spawning orbs that nothing moved or cleared any more
            for (int i = 0; i < n && live; i++) { SpawnOrb(); yield return new WaitForSeconds(enraged ? 0.18f : 0.28f); }
        }

        float OrbSpeed { get { return (phase == 1 ? 3.6f : phase == 2 ? 4.4f : 5.2f) + (enraged ? 0.8f : 0f); } }

        // ---- CARD FAN: straight, fast, spread out; the gaps are where you survive
        IEnumerator CardFan(int waves)
        {
            if (Random.value < 0.5f) Voice.Say(Random.value < 0.5f ? "boss_cards" : "boss_deal", boss, 3.2f);
            Sfx.Play("card_shuffle", boss.position, 1f, 0.8f);
            Fx.SetColor(bossR, Palette.Gold, 1.2f);
            yield return new WaitForSeconds(0.55f);
            Fx.ClearColor(bossR);
            int count = phase == 1 ? 5 : 7;
            float spread = phase == 1 ? 50f : 64f;
            for (int w = 0; w < waves && live; w++)
            {
                var from = core.position + Vector3.up * 0.3f;
                var aim = PlayerHead + Vector3.down * 0.45f;
                var baseDir = aim - from;
                float offset = (w % 2 == 1) ? spread / (count - 1) * 0.5f : 0f;
                for (int i = 0; i < count; i++)
                {
                    float yaw = -spread * 0.5f + spread * i / (count - 1) + offset;
                    var dir = (Quaternion.AngleAxis(yaw, Vector3.up) * baseDir).normalized;
                    SpawnCard(from, dir * (enraged ? 12f : 10f));
                }
                Sfx.Play("card_slide", from, 1f, 0.9f);
                Sfx.Play("card_deal", from, 1f, 1.1f);
                yield return new WaitForSeconds(0.55f);
            }
        }

        void SpawnCard(Vector3 at, Vector3 vel)
        {
            var go = SpawnShared("Card", Field, Vector3.zero, Quaternion.identity, Palette.Neon, CardMesh);
            go.transform.position = at;
            Build.GlowSprite(go.transform, Vector3.zero, 0.9f, Palette.Red, 0.5f);
            cards.Add(new Card { t = go.transform, v = vel, die = Time.time + 4f });
        }

        void MoveCards()
        {
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                var c = cards[i];
                if (c.t == null || Time.time > c.die) { if (c.t != null) Destroy(c.t.gameObject); cards.RemoveAt(i); continue; }
                c.t.position += c.v * Time.deltaTime;
                c.t.Rotate(0, 900f * Time.deltaTime, 0, UnityEngine.Space.Self);
                if (HitsBody(c.t.position, 0.32f))
                {
                    Damage(9f, c.t.position);
                    Juice.Sparks(c.t.position, Palette.Red, 12);
                    Destroy(c.t.gameObject); cards.RemoveAt(i);
                }
            }
        }

        /// <summary>True when a point is inside the player's body (a capsule from the feet to the head).</summary>
        bool HitsBody(Vector3 p, float r)
        {
            if (!Rig.Ready || Rig.I.Head == null) return false;
            var head = Rig.I.Head.position; float feet = Rig.I.transform.position.y;
            if (p.y < feet - r || p.y > head.y + 0.15f + r) return false;
            var d = new Vector2(p.x - head.x, p.z - head.z);
            return d.magnitude < 0.25f + r;
        }

        // ---- CHIP SLAM: telegraphed circles, then giant chips crash down
        IEnumerator ChipSlam(int n)
        {
            if (Random.value < 0.5f) Voice.Say("boss_slam", boss, 3.2f);
            var spots = new List<Vector3>();
            var me = Field.InverseTransformPoint(PlayerHead); me.y = 0;
            spots.Add(me);
            for (int i = 1; i < n; i++)
            {
                var p = me + new Vector3(Random.Range(-4.5f, 4.5f), 0, Random.Range(-4.5f, 4.5f));
                if (p.magnitude > VaultR - 2f) p = p.normalized * (VaultR - 2f);
                spots.Add(p);
            }
            float tele = phase == 1 ? 1.4f : phase == 2 ? 1.15f : 0.95f;
            if (enraged) tele *= 0.85f;
            const float R = SlamR;
            var marks = new List<Transform>();
            foreach (var s in spots)
            {
                var ring = SpawnShared("SlamRing", Field, s + Vector3.up * 0.04f, Quaternion.identity, Palette.FlatGlow, SlamRingMesh);
                Fx.SetColor(ring.GetComponent<Renderer>(), Palette.Red, 1.5f);
                var fill = SpawnShared("SlamFill", ring.transform, new Vector3(0, -0.005f, 0), Quaternion.identity, Palette.FlatGlow, SlamFillMesh);
                Fx.SetColor(fill.GetComponent<Renderer>(), Palette.A(Palette.Red, 0.5f), 0.6f);
                fill.transform.localScale = new Vector3(0.05f, 1, 0.05f);
                marks.Add(ring.transform); temp.Add(ring);
            }
            Sfx.Play("security_alert", PlayerHead, 0.5f, 1.2f);
            for (float t = 0; t < tele && live; t += Time.deltaTime)
            {
                float f = t / tele;
                foreach (var m in marks) if (m != null && m.childCount > 0) m.GetChild(0).localScale = new Vector3(f, 1, f);
                yield return null;
            }
            if (!live) { foreach (var m in marks) if (m != null) Destroy(m.gameObject); yield break; }
            // chips fall
            var chips = new List<Transform>();
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i] == null) continue;
                int ci = i % TableGame.Accents.Length;
                var chip = SpawnShared("SlamChip" + ci, Field, spots[i] + Vector3.up * 12f, Quaternion.identity, Palette.Neon, () => SlamChipMesh(ci, R));
                chips.Add(chip.transform); temp.Add(chip);
            }
            for (float t = 0; t < 0.28f; t += Time.deltaTime)
            {
                float f = Fx.EaseIn(t / 0.28f);
                for (int i = 0; i < chips.Count; i++) if (chips[i] != null) chips[i].localPosition = spots[i] + Vector3.up * Mathf.Lerp(12f, 0.18f, f);
                yield return null;
            }
            bool hit = false;
            for (int i = 0; i < spots.Count; i++)
            {
                var world = Field.TransformPoint(spots[i]);
                Juice.Sparks(world + Vector3.up * 0.3f, Palette.Gold, 30);
                if (i < 2) Sfx.Play("crash_boom", world, 0.9f, Random.Range(0.85f, 1.1f));
                if (!hit && Rig.Ready)
                {
                    var h = Rig.I.Head.position; var d = new Vector2(h.x - world.x, h.z - world.z);
                    if (d.magnitude < R + 0.15f) { hit = true; Damage(22f, world); }
                }
            }
            XRIn.HapticBoth(0.5f, 0.15f);
            if (Rig.Ready) StartCoroutine(Fx.Shake(Rig.I.Head.parent, 0.012f, 0.25f));
            foreach (var m in marks) if (m != null) Destroy(m.gameObject);
            yield return new WaitForSeconds(0.5f);
            for (float t = 0; t < 0.4f; t += Time.deltaTime)
            {
                foreach (var c in chips) if (c != null) c.localPosition += Vector3.down * Time.deltaTime * 1.2f;
                yield return null;
            }
            foreach (var c in chips) if (c != null) Destroy(c.gameObject);
        }

        // ---- ROULETTE: the floor becomes a wheel; red sectors explode
        IEnumerator Roulette()
        {
            Voice.Say("boss_roulette", boss, 3.2f);
            GameState.I.Notify("ROULETTE · STAND ON GREEN!", Palette.Mint);
            int mine = SectorOf(PlayerHead);
            int danger = phase >= 3 || enraged ? 5 : 4;
            var red = new bool[WedgeCount];
            red[mine] = true;   // your sector is always red: you have to move
            int safeNext = (mine + (Random.value < 0.5f ? 1 : WedgeCount - 1)) % WedgeCount;   // a neighbour is always safe
            var order = new List<int>(); for (int i = 0; i < WedgeCount; i++) if (i != mine && i != safeNext) order.Add(i);
            for (int i = 0; i < order.Count; i++) { int j = Random.Range(i, order.Count); int tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
            for (int i = 0; i < danger - 1 && i < order.Count; i++) red[order[i]] = true;
            spinLoop = Sfx.Loop("wheel_spin_loop", boss, 0.8f, 0f);
            // flicker like a spinning wheel, then settle on the real colours
            float tele = phase >= 3 ? 2.0f : 2.4f; if (enraged) tele *= 0.85f;
            float t = 0, next = 0; int tick = 0;
            foreach (var w in wedges) w.enabled = true;
            while (t < tele && live)
            {
                t += Time.deltaTime;
                if (t >= next)
                {
                    tick++;
                    // the real colours show for the last 65% of the spin (was 55%: at phase 3 + ALL IN that left
                    // under a second to cross up to ~3.5 m into the safe sector)
                    bool settle = t > tele * 0.35f;
                    for (int i = 0; i < WedgeCount; i++)
                    {
                        bool r = settle ? red[i] : ((i + tick) % 2 == 0);
                        float pulse = settle && r ? 0.55f + Mathf.Sin(t * 18f) * 0.25f : 0.35f;
                        Fx.SetColor(wedges[i], r ? Palette.A(Palette.Red, 1f) * pulse : Palette.Mint * 0.3f, 1f);
                    }
                    Sfx.UI("ball_tick", 0.35f, settle ? 1.3f : 1f);
                    next = t + (settle ? 0.08f : Mathf.Lerp(0.06f, 0.16f, t / tele));
                }
                yield return null;
            }
            if (spinLoop != null) Destroy(spinLoop.gameObject);
            spinLoop = null;
            if (!live) { foreach (var w in wedges) w.enabled = false; yield break; }
            // boom
            int at = SectorOf(PlayerHead);
            for (int i = 0; i < WedgeCount; i++)
            {
                if (!red[i]) continue;
                float a = (i + 0.5f) * Mathf.PI * 2f / WedgeCount;
                for (int s = 0; s < 3; s++)
                    Juice.Sparks(Field.TransformPoint(new Vector3(Mathf.Sin(a), 0.1f, Mathf.Cos(a)) * (3f + s * 5f)), Palette.Red, 18);
                Fx.SetColor(wedges[i], Color.white, 1.5f);
            }
            Sfx.Play("crash_boom", Field.TransformPoint(Vector3.zero), 1f, 0.8f);
            Sfx.UI("mine_boom", 0.8f);
            XRIn.HapticBoth(0.7f, 0.25f);
            if (red[at] && Rig.Ready && Rig.I.Head.position.y - Field.position.y < 3f) Damage(30f, PlayerHead);
            else Sfx.UI("whistle", 0.4f);
            yield return new WaitForSeconds(0.25f);
            foreach (var w in wedges) w.enabled = false;
        }

        // ------------------------------------------------------------ shared meshes
        // Orbs, cards, slam rings/chips, drones and shockwaves used to bake a new runtime Mesh per spawn; destroying the
        // GameObject does not free a runtime mesh, so every raid leaked hundreds of them. Build each shape once.
        readonly Dictionary<string, Mesh> sharedMeshes = new Dictionary<string, Mesh>();

        GameObject SpawnShared(string key, Transform parent, Vector3 localPos, Quaternion localRot, Material mat, System.Func<MeshKit> build)
        {
            Mesh m;
            if (!sharedMeshes.TryGetValue(key, out m) || m == null)
            {
                var first = build().Spawn(key, parent, localPos, localRot, mat, false);
                sharedMeshes[key] = first.GetComponent<MeshFilter>().sharedMesh;
                return first;
            }
            var go = new GameObject(key);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localRotation = localRot;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat != null ? mat : Palette.Neon;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            return go;
        }

        static MeshKit OrbMesh() { var k = new MeshKit(); k.Sphere(Vector3.zero, 0.22f, 12, Palette.Violet, 1.4f); return k; }
        static MeshKit CardMesh()
        {
            var k = new MeshKit();
            k.Box(Vector3.zero, new Vector3(0.34f, 0.02f, 0.48f), Color.white, 0.9f);
            k.Box(new Vector3(0, 0.012f, 0), new Vector3(0.28f, 0.005f, 0.42f), Palette.Red, 1.2f);
            return k;
        }
        static MeshKit DroneMesh()
        {
            var k = new MeshKit();
            // chunky toy drone in the Casino FREE colours: white shell, red eye, four rotor arms with red rotors
            k.Sphere(Vector3.zero, new Vector3(0.24f, 0.14f, 0.24f), 12, Palette.Hex("EEF0F5"), 0f);
            k.Cylinder(new Vector3(0, -0.02f, 0), 0.25f, 0.25f, 0.05f, 20, Quaternion.identity, Palette.Hex("D9404E"), 0f);
            k.Sphere(new Vector3(0, 0, -0.2f), 0.07f, 10, Palette.Hex("FFD54A"), 1.5f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.785f;
                var tip = new Vector3(Mathf.Cos(a) * 0.3f, 0.06f, Mathf.Sin(a) * 0.3f);
                k.Bar(new Vector3(0, 0.04f, 0), tip, 0.025f, Palette.Hex("3A3D48"), 0f);
                k.Cylinder(tip + Vector3.up * 0.02f, 0.13f, 0.13f, 0.02f, 16, Quaternion.identity, Palette.Hex("D9404E"), 0.3f);
            }
            return k;
        }
        const float SlamR = 1.9f;
        static MeshKit SlamRingMesh() { var k = new MeshKit(); k.Ring(Vector3.zero, SlamR - 0.08f, SlamR, 48, Quaternion.identity, Color.white, 1.4f); return k; }
        static MeshKit SlamFillMesh() { var k = new MeshKit(); k.Disc(Vector3.zero, SlamR, 48, Quaternion.identity, Color.white, 0.8f); return k; }
        static MeshKit SlamChipMesh(int accent, float r)
        {
            var ck = new MeshKit();
            ck.Cylinder(Vector3.zero, r * 0.92f, r * 0.92f, 0.35f, 40, Quaternion.identity, Palette.Hex("1A1426"), 0.15f, true, TableGame.Accents[accent], 1f);
            ck.Torus(new Vector3(0, 0.18f, 0), r * 0.7f, 0.06f, 40, 4, Quaternion.identity, Color.white, 1f);
            return ck;
        }

        int SectorOf(Vector3 world)
        {
            var l = Field.InverseTransformPoint(world);
            float a = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg; if (a < 0) a += 360f;
            return Mathf.Clamp(Mathf.FloorToInt(a / (360f / WedgeCount)), 0, WedgeCount - 1);
        }

        // ------------------------------------------------------------ player HUD, sights, hit feedback
        Transform phud, phpFill, pbossFill, reticle;
        Renderer phpFillR, reticleR;
        TMPro.TextMeshPro phpT, pbossT, ptimeT;
        int hudHP = -1, hudTime = -1, hudPhase = -1; bool hudEnraged;
        readonly LineRenderer[] sights = new LineRenderer[2];
        readonly Transform[] dots = new Transform[2];

        /// <summary>Head-locked combat HUD: your HP bar (bottom), THE HOUSE's HP and phase (top), timer; a reticle for
        /// desktop aiming and a laser sight + dot on each blaster in VR.</summary>
        void BuildPlayerHud()
        {
            DestroyPlayerHud();
            if (!Rig.Ready) return;
            phud = Build.Empty("RaidHUD", Rig.I.Head, Vector3.zero).transform;
            // bottom: player HP
            var bg = new MeshKit(); bg.Quad(Vector3.zero, new Vector2(0.26f, 0.022f), Quaternion.identity, Palette.Hex("1A0E14"), 0.3f);
            bg.Spawn("HPBack", phud, new Vector3(0, -0.19f, 0.6f), Quaternion.identity, Palette.FlatGlow, false);
            var fill = new MeshKit(); fill.Quad(new Vector3(0.13f, 0, 0), new Vector2(0.26f, 0.016f), Quaternion.identity, Color.white, 1f);
            phpFill = fill.Spawn("HPFill", phud, new Vector3(-0.13f, -0.19f, 0.598f), Quaternion.identity, Palette.FlatGlow, false).transform;
            phpFillR = phpFill.GetComponent<Renderer>();
            hudHP = hudTime = hudPhase = -1;
            phpT = Txt.Glow("HPText", phud, new Vector3(0, -0.165f, 0.6f), "HP 100", 0.018f, Palette.Mint);
            // top: boss HP + phase
            var bb = new MeshKit(); bb.Quad(Vector3.zero, new Vector2(0.34f, 0.016f), Quaternion.identity, Palette.Hex("1A0E14"), 0.3f);
            bb.Spawn("BossBack", phud, new Vector3(0, 0.19f, 0.6f), Quaternion.identity, Palette.FlatGlow, false);
            var bf = new MeshKit(); bf.Quad(new Vector3(0.17f, 0, 0), new Vector2(0.34f, 0.012f), Quaternion.identity, Palette.Red, 1.2f);
            pbossFill = bf.Spawn("BossFill", phud, new Vector3(-0.17f, 0.19f, 0.598f), Quaternion.identity, Palette.FlatGlow, false).transform;
            pbossT = Txt.Glow("BossText", phud, new Vector3(0, 0.212f, 0.6f), "THE HOUSE", 0.017f, Palette.Red);
            ptimeT = Txt.Make("Time", phud, new Vector3(0.2f, 0.19f, 0.6f), "", 0.014f, Palette.Text, Palette.Display);
            foreach (var r in phud.GetComponentsInChildren<Renderer>()) { r.sortingOrder = 50; }
            if (!XRIn.HasHeadset)
            {
                var rk = new MeshKit(); rk.Ring(Vector3.zero, 0.006f, 0.008f, 24, Quaternion.Euler(-90, 0, 0), Color.white, 1.5f);
                rk.Sphere(Vector3.zero, 0.0015f, 6, Color.white, 1.5f);
                reticle = rk.Spawn("Reticle", phud, new Vector3(0, 0, 0.6f), Quaternion.identity, Palette.FlatGlow, false).transform;
                reticleR = reticle.GetComponent<Renderer>();
            }
            else
            {
                for (int h = 0; h < 2; h++)
                {
                    var g = new GameObject("Sight" + h); g.transform.SetParent(transform, false);
                    var lr = g.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.sharedMaterial = Palette.FlatGlow; lr.widthMultiplier = 0.003f;
                    lr.startColor = Palette.A(Palette.Red, 0.7f); lr.endColor = Palette.A(Palette.Red, 0.05f);
                    sights[h] = lr;
                    dots[h] = Build.GlowSprite(transform, Vector3.zero, 0.08f, Palette.Red, 1f);
                }
            }
        }

        void UpdatePlayerHud()
        {
            if (phud == null) return;
            float hp = Mathf.Clamp01(playerHP / 100f);
            phpFill.localScale = new Vector3(Mathf.Max(0.001f, hp), 1, 1);
            var col = hp > 0.5f ? Palette.Mint : hp > 0.25f ? Palette.Gold : Palette.Red;
            // texts only change when the number does (string building + TMP mesh rebuilds every frame add up on Quest)
            int hpi = Mathf.CeilToInt(playerHP);
            if (hpi != hudHP) { hudHP = hpi; Fx.SetColor(phpFillR, col, 1.2f); phpT.text = "HP " + hpi; phpT.color = col; }
            float bk = Mathf.Max(0, bossHP) / (float)BossMaxHP;
            pbossFill.localScale = new Vector3(Mathf.Max(0.001f, bk), 1, 1);
            if (phase != hudPhase || enraged != hudEnraged) { hudPhase = phase; hudEnraged = enraged; pbossT.text = "THE HOUSE · PHASE " + phase + (enraged ? " · ALL IN!" : ""); }
            int ti = Mathf.CeilToInt(Mathf.Max(0, timeLeft));
            if (ti != hudTime) { hudTime = ti; ptimeT.text = ti + "s"; }
            if (reticle != null)
            {
                RaidTarget rt; Vector3 rp;
                bool onTarget = Rig.Ready && ShotHit(Rig.I.Head.position, 0.14f, Rig.I.Head.forward, out rt, out rp) && rt != null;
                Fx.SetColor(reticleR, onTarget ? Palette.Red : Color.white, 1.5f);
            }
            for (int h = 0; h < 2; h++)
            {
                if (sights[h] == null || blasters[h] == null) continue;
                var m = blasters[h].GetChild(0);
                RaycastHit hit; var end = m.position + m.forward * 30f;
                if (Physics.Raycast(m.position, m.forward, out hit, 60f, ~0, QueryTriggerInteraction.Collide)) end = hit.point;
                sights[h].SetPosition(0, m.position); sights[h].SetPosition(1, end);
                dots[h].position = end;
            }
        }

        void DestroyPlayerHud()
        {
            if (phud != null) Destroy(phud.gameObject);
            phud = null; reticle = null; phpFillR = null; reticleR = null;
            for (int h = 0; h < 2; h++) { if (sights[h] != null) Destroy(sights[h].gameObject); if (dots[h] != null) Destroy(dots[h].gameObject); sights[h] = null; dots[h] = null; }
        }

        IEnumerator MuzzleFlash(Transform muzzle)
        {
            var f = Build.GlowSprite(muzzle, Vector3.zero, 0.18f, Palette.Teal, 1.2f);
            yield return new WaitForSeconds(0.05f);
            if (f != null) Destroy(f.gameObject);
        }

        void DamageNumber(Vector3 at, string text, Color c)
        {
            var t = Txt.Glow("Dmg", null, at, text, text.Length > 4 ? 0.22f : 0.3f, c);
            t.gameObject.AddComponent<Billboard>();
            Destroy(t.gameObject, 0.85f);   // also goes away if FloatUp is stopped (it lives at the scene root)
            StartCoroutine(FloatUp(t));
        }

        IEnumerator FloatUp(TMPro.TextMeshPro t)
        {
            var p0 = t.transform.position + Random.insideUnitSphere * 0.2f;
            for (float e = 0; e < 0.8f; e += Time.deltaTime)
            {
                if (t == null) yield break;
                t.transform.position = p0 + Vector3.up * e * 1.2f;
                t.alpha = 1f - e / 0.8f;
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        void ClearAttacks()
        {
            foreach (var c in cards) if (c.t != null) Destroy(c.t.gameObject); cards.Clear();
            foreach (var g in temp) if (g != null) Destroy(g); temp.Clear();
            foreach (var o in orbs) if (o != null) Destroy(o.gameObject); orbs.Clear();
            foreach (var d in drones) if (d != null) Destroy(d.gameObject); drones.Clear();
            if (wedges != null) foreach (var w in wedges) if (w != null) w.enabled = false;
            laserOn = false; if (laser != null) laser.enabled = false;
            if (laserLoop != null) Destroy(laserLoop.gameObject); laserLoop = null;
            if (spinLoop != null) Destroy(spinLoop.gameObject); spinLoop = null;
            attacking = 0; enraged = false;
        }
    }
}
