using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>EL CAMELLO · the thing that lives in the lobby lift: a tall figure in a black coat and a wide-brimmed hat,
    /// face lost in its shadow except for two ember eyes. Text only. For 100 chips he hands you ten milligrams of
    /// "mondongo" (pure parody) and three jobs; do them and the lift makes its unlisted stop: -1, EL SÓTANO (the
    /// team's hall of fame). After that first trip he just charges the 100 chips.
    /// Jobs (tracked on every floor by <see cref="CamelloJobs"/>): sell the 10 mg to five guests (stand next to them),
    /// pick up his parcel at the bar, and put one security guard to sleep (come up behind him).</summary>
    [RequireComponent(typeof(Elevator))]
    public class ElevatorDealer : MonoBehaviour
    {
        public static ElevatorDealer I { get; private set; }
        public AvatarBody Body { get { return body; } }
        public const long Price = 100;

        // cabin interior, floor-local (same numbers as Elevator)
        const float X = 1.15f, Z0 = -2.9f, Z1 = -0.8f, Hgt = 2.75f;

        Elevator lift;
        AvatarBody body;
        Transform speaker;
        DialogBox dialog;
        enum State { Idle, Pitch, Busy }
        State state;
        bool quiet;            // already talked: wait until the player has been out of the lift for a moment
        float outSince;

        const string Who = "EL CAMELLO";
        static readonly string[] PitchLines =
        {
            "Psst. Tú. No mires a la cámara.",
            "Cien fichas y te doy mondongo. Del bueno. Sin cortar.",
        };
        static readonly string[] JobLines =
        {
            "Diez miligramos. No lo pierdas.",
            "Ahora trabajas para mí. Tres encargos y este ascensor baja a donde no baja nadie.",
            "Uno: vende el mondongo a los clientes. Arrímate a ellos, que pican solos.",
            "Dos: recoge mi paquete en la barra. No lo abras.",
            "Tres: hay un guardia que hace demasiadas preguntas. Por la espalda. Que deje de hacerlas.",
        };
        static readonly string[] DoneLines = { "Limpio. Nadie te ha visto.", "Agárrate. Planta menos uno." };
        static readonly string[] AgainLines = { "¿Otra vez abajo? Ya conoces el precio.", "Cien fichas. El mondongo va aparte." };
        static readonly string[] PaidLines = { "Un placer hacer negocios.", "Yo no te he visto y tú no me has visto." };
        static readonly string[] BrokeLines = { "¿Sin fichas? Sin fichas no hay mondongo. Vuelve cuando hayas palmado menos." };
        static readonly string[] CancelLines = { "¿Te rajas? Toma tus fichas. Aquí no ha pasado nada." };
        static readonly string[] BackLines = { "¿Qué tal ahí abajo? Ni una palabra a seguridad.", "Si quieres repetir, ya sabes: cien fichas." };

        void Awake() { I = this; lift = GetComponent<Elevator>(); }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (dialog != null) Destroy(dialog.gameObject);
        }

        void Start()
        {
            // back-left corner, angled at the doors (the floor panel is on the right wall)
            var lib = AssetLibrary.I; var local = new Vector3(-0.72f, 0f, -1.2f);
            if (Avatars.UseHumanoids && lib != null && lib.HasHumanoids && lib.security != null)
                body = HumanoidBody.CreateFrom(lib.security, lib.humanoidController, lib, Who, transform, local, 160f, Rarity.Legendary, RoninRole.Player, 1.92f, HumanoidBody.Kind.Extra);
            if (body == null) body = Avatars.CreateStyled(Who, transform, local, 160f, Rarity.Rare, AvatarStyle.Android, 1.9f);
            if (body != null)
            {
                body.pose = Pose.Idle;
                speaker = body.HeadBone != null ? body.HeadBone : body.transform;
                Sinister(body);
            }
            else speaker = transform;

            // the merchandise: a battered briefcase at his feet with a gold clasp
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.17f, 0), new Vector3(0.46f, 0.32f, 0.12f), Palette.Hex("1E1410"), 0f);
            k.Box(new Vector3(0, 0.34f, 0), new Vector3(0.14f, 0.03f, 0.04f), Palette.Hex("0E0A08"), 0f);
            k.Box(new Vector3(0, 0.27f, -0.062f), new Vector3(0.06f, 0.04f, 0.01f), Palette.Gold, 0.6f);
            k.Spawn("Briefcase", transform, new Vector3(-0.95f, 0f, -1.0f), Quaternion.Euler(0, 25f, 0), Palette.Flat(Color.white, 0.05f, 0.3f, 0.4f), false);

            // the cabin light dies in his corner: one weak red bulb
            var lg = new GameObject("CamelloGlow"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(-0.75f, 2.2f, -1.6f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.16f, 0.12f); l.intensity = 0.9f; l.range = 2.2f; l.shadows = LightShadows.None;

            var sign = Txt.Glow("DealerSign", transform, new Vector3(-X + 0.03f, 2.3f, -1.35f), "NO PREGUNTES", 0.06f, Palette.Hex("FF3B3B"), 1.2f);
            sign.transform.localRotation = Quaternion.Euler(0, -90f, 0);

            dialog = DialogBox.Create();
            outSince = -10f;
            CamelloJobs.Ensure();
        }

        /// <summary>Black from hat to shoes, hat brim low over the face, the face itself a shadow with two ember eyes,
        /// collar up, a long coat. He stands with his arms folded and never waves.</summary>
        static void Sinister(AvatarBody b)
        {
            var hb = b as HumanoidBody;
            foreach (var r in b.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("Glasses")) { r.enabled = false; continue; }
                var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", new Color(0.085f, 0.07f, 0.1f)); r.SetPropertyBlock(mpb);
            }
            if (hb != null) { hb.idleSet = new[] { "IdleArms" }; }
            var head = b.HeadBone; if (head == null || head == b.transform) return;
            float sc = 1.92f / 1.75f;
            var mat = Palette.Flat(Color.white, 0.02f, 0.04f, 0.2f);
            Color black = new Color(0.03f, 0.025f, 0.04f), band = Palette.Hex("7A0F1E");
            var rig = new GameObject("Camello Hat").transform; rig.SetParent(b.transform, false);
            rig.position = head.position + b.transform.up * HumanoidBody.EyeUp * sc; rig.rotation = b.transform.rotation;
            var k = new MeshKit();
            float top = 0.13f * sc;                                                           // eye line → top of the skull
            k.Push(new Vector3(0, top - 0.035f, 0.01f), Quaternion.Euler(9f, 0, 0));          // tipped forward over the eyes
            k.Cylinder(new Vector3(0, 0, 0), 0.235f * sc, 0.235f * sc, 0.012f, 40, Quaternion.identity, black, 0f);
            k.Cylinder(new Vector3(0, 0.065f, 0), 0.125f * sc, 0.108f * sc, 0.13f, 32, Quaternion.identity, black, 0f);
            k.Cylinder(new Vector3(0, 0.022f, 0), 0.128f * sc, 0.126f * sc, 0.028f, 32, Quaternion.identity, band, 0.25f);
            k.Pop();
            // the face: a shadow, and the collar turned up round the jaw
            k.Sphere(new Vector3(0, -0.005f, HumanoidBody.EyeFwd * sc - 0.075f), new Vector3(0.118f, 0.125f, 0.11f) * sc, 20, black, 0f);
            k.Cylinder(new Vector3(0, -0.14f * sc, -0.005f), 0.105f * sc, 0.135f * sc, 0.11f * sc, 24, Quaternion.identity, black, 0f, false);
            k.Spawn("Shadow", rig, mat, false);
            var ek = new MeshKit(); var ember = new Color(1f, 0.25f, 0.08f);
            foreach (var sx in new[] { -1f, 1f }) ek.Sphere(new Vector3(sx * HumanoidBody.EyeSide * sc, 0.004f, HumanoidBody.EyeFwd * sc + 0.03f), new Vector3(0.013f, 0.007f, 0.008f), 10, ember, 3f);
            ek.Spawn("Eyes", rig, Palette.Neon, false);
            rig.SetParent(head, true);
            // the coat: a long bell from the waist to the shins
            Transform hips = null; var an = b.GetComponentInChildren<Animator>(); if (an != null && an.isHuman) hips = an.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null)
            {
                var coat = new GameObject("Camello Coat").transform; coat.SetParent(b.transform, false);
                coat.position = hips.position; coat.rotation = b.transform.rotation;
                var ck = new MeshKit();
                ck.Cylinder(new Vector3(0, -0.33f * sc, 0), 0.3f * sc, 0.19f * sc, 0.74f * sc, 28, Quaternion.identity, black, 0f, false);
                ck.Spawn("Coat", coat, mat, false);
                coat.SetParent(hips, true);
            }
        }

        bool Inside()
        {
            if (!Rig.Ready) return false;
            var l = transform.InverseTransformPoint(Rig.I.Head.position);
            return l.x > -X && l.x < X && l.z > Z0 - 0.05f && l.z < Z1 && l.y > 0f && l.y < Hgt + 0.5f;
        }

        void Update()
        {
            if (!Rig.Ready || dialog == null) return;
            bool inside = Inside();
            if (body != null)
            {
                var d = Rig.I.Head.position - speaker.position;
                body.lookAtTarget = d.sqrMagnitude < 9f ? Rig.I.Head : null;
            }

            if (!inside)
            {
                if (state == State.Pitch) { dialog.Hide(); state = State.Idle; }
                if (state != State.Busy && Time.time - outSince > 1.5f) quiet = false;
                return;
            }
            outSince = Mathf.Max(outSince, Time.time);

            // a normal ride started while he was talking: shut up until next time
            if (Elevator.Riding)
            {
                if (state == State.Pitch) { dialog.Hide(); state = State.Idle; quiet = true; }
                return;
            }

            if (state == State.Idle && !quiet && !dialog.Open && !IntroCinematic.Playing && !PlayerSeating.Seated && !Rig.I.Teleporting)
                Pitch();
        }

        void Pitch()
        {
            state = State.Pitch;
            if (body != null) body.Play(Emote.Nod, 1.6f);
            Sfx.UI("whistle", 0.25f);
            switch (CamelloJobs.Stage)
            {
                case 0: dialog.Show(speaker, Who, PitchLines, "PAGAR " + Price + " FICHAS ▸", PayFirst); break;
                case 1:
                    if (CamelloJobs.AllDone) { CamelloJobs.Stage = 2; goto case 2; }
                    dialog.Show(speaker, Who, new[] { "Todavía no has terminado.", CamelloJobs.Pending() }, "", null, true); state = State.Idle; quiet = true; break;
                case 2: dialog.Show(speaker, Who, DoneLines, "BAJAR ▸", () => Ride(false)); break;
                default: dialog.Show(speaker, Who, AgainLines, "PAGAR " + Price + " FICHAS ▸", () => Ride(true)); break;
            }
        }

        void Say(string[] lines)
        {
            if (body != null) body.Play(Emote.Talk, 1.6f);
            dialog.Show(speaker, Who, lines, "", null, true);
        }

        /// <summary>First deal: 100 chips for the mondongo, and the three jobs that come with it.</summary>
        void PayFirst()
        {
            state = State.Idle; quiet = true;
            if (!Inside()) return;
            var gs = GameState.I;
            if (gs == null || !gs.TakeChips(Price)) { Say(BrokeLines); return; }
            Sfx.Play("chips_sweep", speaker.position, 0.8f);
            CamelloJobs.Begin();
            Say(JobLines);
        }

        void Ride(bool charge)
        {
            state = State.Idle; quiet = true;
            if (!Inside()) return;
            var gs = GameState.I;
            if (charge && (gs == null || !gs.TakeChips(Price))) { Say(BrokeLines); return; }
            paidRide = charge;
            if (charge) Sfx.Play("chips_sweep", speaker.position, 0.8f);
            if (!lift.RideDown(Arrive, Cancelled)) { if (charge) gs.ReturnChips(Price); return; }
            state = State.Busy;
            if (body != null) body.Play(Emote.Nod, 1.2f);
            if (charge) Say(PaidLines);
        }
        bool paidRide;

        void Arrive()
        {
            dialog.Hide();
            state = State.Idle;
            if (CamelloJobs.Stage == 2) CamelloJobs.Stage = 3;
            if (CreditsBasement.I != null) CreditsBasement.I.EnterFromLift();
            else if (GameState.I != null) { if (paidRide) GameState.I.ReturnChips(Price); GameState.I.Notify("EL SÓTANO ESTÁ CERRADO", Palette.Red); }
        }

        void Cancelled()
        {
            state = State.Idle;
            if (paidRide && GameState.I != null) GameState.I.ReturnChips(Price);
            Say(CancelLines);
        }

        /// <summary>Back from the basement: you land in his lift; he says hi and does not pitch again until you leave.</summary>
        public void Welcome()
        {
            state = State.Idle; quiet = true;
            outSince = Time.time + 3f;
            if (dialog != null) Say(BackLines);
        }

        /// <summary>Where the basement exit drops the player: inside this cabin, facing the doors.</summary>
        public void Landing(out Vector3 feet, out float yaw)
        {
            feet = transform.TransformPoint(new Vector3(0.25f, 0f, -1.55f));
            yaw = transform.eulerAngles.y + 180f;
        }
    }

    /// <summary>EL CAMELLO's three jobs, followed wherever the player goes (it lives outside the floors).
    /// Stage: 0 = never paid · 1 = working · 2 = all done, ride pending · 3 = the basement is open for good.</summary>
    public class CamelloJobs : MonoBehaviour
    {
        static CamelloJobs me;
        const string Key = "asc2_camello_stage";
        public const int Doses = 5, MgPerDose = 2;
        public static int Stage { get { return PlayerPrefs.GetInt(Key, 0); } set { PlayerPrefs.SetInt(Key, value); } }
        static int sold; static bool parcel, guard;
        public static bool AllDone { get { return sold >= Doses && parcel && guard; } }

        public static void Ensure()
        {
            if (me != null) return;
            var go = new GameObject("ASCEND · Camello Jobs"); DontDestroyOnLoad(go); me = go.AddComponent<CamelloJobs>();
        }
        public static void Begin() { Ensure(); Stage = 1; sold = 0; parcel = guard = false; me.customers.Clear(); Toast("ENCARGOS DEL CAMELLO · VENDE 10 mg · PAQUETE EN LA BARRA · UN GUARDIA", Palette.Hex("FF3B3B")); }

        public static string Pending()
        {
            var p = new List<string>();
            if (sold < Doses) p.Add("vender " + (Doses - sold) * MgPerDose + " mg");
            if (!parcel) p.Add("mi paquete de la barra");
            if (!guard) p.Add("el guardia");
            return "Te falta: " + string.Join(" · ", p.ToArray()) + ".";
        }

        static void Toast(string s, Color c) { if (GameState.Exists) GameState.I.Notify(s, c); }

        readonly HashSet<BotAvatar> customers = new HashSet<BotAvatar>();
        BotAvatar near; float nearT, guardT, scan; GuardPost[] posts = new GuardPost[0]; NeonBar bar;

        void Update()
        {
            if (Stage != 1 || !Rig.Ready || Elevator.Riding) return;
            var head = Rig.I.Head.position; float dt = Time.deltaTime;
            if (Time.time > scan)
            {
                scan = Time.time + 2f;
                posts = FindObjectsByType<GuardPost>(FindObjectsSortMode.None);
                bar = FindFirstObjectByType<NeonBar>();
            }

            // 1. customers: stand next to a guest for a moment and the deal is done
            if (sold < Doses)
            {
                BotAvatar best = null; float bd = 1.35f * 1.35f;
                foreach (var b in BotAvatar.All)
                {
                    if (b == null || !b.isActiveAndEnabled || customers.Contains(b)) continue;
                    var d = b.transform.position - head; d.y = 0;
                    if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = b; }
                }
                if (best != near) { near = best; nearT = 0f; }
                if (near != null && (nearT += dt) > 1.4f)
                {
                    customers.Add(near); sold++; nearT = 0f;
                    if (near.Anim != null) near.Anim.Play(Emote.Nod, 1.2f);
                    if (GameState.Exists) GameState.I.ReturnChips(30);
                    Sfx.Play("chips_sweep", near.transform.position + Vector3.up, 0.6f, 1.3f);
                    Toast("MONDONGO VENDIDO · " + sold * MgPerDose + "/" + Doses * MgPerDose + " mg · +30 FICHAS", Palette.Mint);
                    near = null;
                }
            }

            // 2. the parcel waits at the bar
            if (!parcel && bar != null && bar.isActiveAndEnabled)
            {
                var d = bar.transform.position - head; d.y = 0;
                if (d.sqrMagnitude < 2.4f * 2.4f) { parcel = true; Sfx.UI("ui_click", 0.6f); Toast("PAQUETE RECOGIDO · NO LO ABRAS", Palette.Gold); }
            }

            // 3. the guard: from behind, close, and he never sees it coming
            if (!guard)
            {
                bool behindOne = false;
                foreach (var p in posts)
                {
                    if (p == null || !p.isActiveAndEnabled || p.Down || p.Body == null) continue;
                    var gt = p.Body.transform; var d = head - gt.position; d.y = 0;
                    if (d.sqrMagnitude > 1.25f * 1.25f || Vector3.Dot(gt.forward, d.normalized) > -0.25f) continue;
                    behindOne = true;
                    if ((guardT += dt) > 0.9f)
                    {
                        guard = true; p.TakeDown(); XRIn.HapticBoth(0.8f, 0.2f);
                        Toast("GUARDIA FUERA DE SERVICIO · YA NO PREGUNTA", Palette.Hex("FF3B3B"));
                    }
                    break;
                }
                if (!behindOne) guardT = 0f;
            }

            if (AllDone) { Stage = 2; Toast("ENCARGOS HECHOS · VUELVE AL ASCENSOR", Palette.Gold); }
        }
    }
}
