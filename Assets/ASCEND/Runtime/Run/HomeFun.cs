using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ascend
{
    /// <summary>Things to do at the caravan park before getting in Don Tuchi's car (ported from Roberto's own street
    /// in "Gamble With Your Friends", HomeActivities.cs): shoot hoops, bounce on the trampoline, buy a soda that makes
    /// you walk faster for 30 s, and throw a chip in the wishing fountain for a fortune. Built in the Casino FREE flat
    /// style with MeshKit; no files from the original game.</summary>
    public class HomeFun : MonoBehaviour
    {
        public const long SodaPrice = 25, WishPrice = 10;
        public const float SodaBoost = 1.25f, SodaSeconds = 30f, ThrowSpeed = 8.5f;
        /// <summary>Desktop walking speed multiplier (DesktopTester reads it).</summary>
        public static float SpeedMul { get { return Time.time < boostUntil ? SodaBoost : 1f; } }
        static float boostUntil;

        Transform home, rim, hoopRoot;
        Rigidbody ball;
        XRGrabInteractable grab;
        Vector3 ballHome;
        TextMeshPro scoreT;
        int score;
        float lastScore, lastBounce, idleSince;
        bool inHoop, enteredAbove, bouncing;
        Bounds trampoline;

        static Material _mat;
        static Material Mat { get { if (_mat == null) _mat = Palette.Flat(new Color(0.6f, 0.58f, 0.6f), 0.02f, 0.03f, 0.25f); return _mat; } }

        static readonly string[] Fortunes =
        {
            "LA PRÓXIMA TIRADA HUELE A SUERTE", "HOY LA CUOTA PARPADEA PRIMERO", "UN PAR DE ASES VIENE DE CAMINO", "APUESTA POCO, SUEÑA MUCHO",
            "THE HOUSE SE VA A LLEVAR UNA SORPRESA", "EL ASCENSOR SABE TU NOMBRE", "TU CARTERA SUSURRA: QUIZÁS", "PARA EN LO ALTO. O NO.",
            "DON TUCHI SONRÍE. MALA SEÑAL.", "LA FUENTE SE QUEDA TUS 10 FICHAS. COMO TODOS."
        };

        public void Build(Transform root)
        {
            home = root;
            BuildHoop(new Vector3(14f, 0, -4f), -90f);
            BuildTrampoline(new Vector3(-8.5f, 0, -1.5f));
            BuildSoda(new Vector3(5.4f, 0, 2.6f), 0f);
            BuildFountain(new Vector3(-6.5f, 0, 13.5f));
        }

        static GameObject Spawn(MeshKit k, string name, Transform parent) { return k.Spawn(name, parent, Mat, false); }

        // ------------------------------------------------------------------ basketball
        void BuildHoop(Vector3 p, float yaw)
        {
            hoopRoot = global::Ascend.Build.Empty("Hoop", home, p, Quaternion.Euler(0, yaw, 0)).transform;
            var k = new MeshKit();
            k.Cylinder(new Vector3(0, 1.6f, -0.3f), 0.07f, 0.07f, 3.2f, 10, Quaternion.identity, Palette.Hex("3A3F48"));
            k.Box(new Vector3(0, 3.35f, -0.15f), new Vector3(0.1f, 0.1f, 0.35f), Palette.Hex("3A3F48"), 0f);
            k.Box(new Vector3(0, 3.45f, 0), new Vector3(1.6f, 1.05f, 0.06f), Palette.Hex("F2F2F2"), 0f);
            k.Box(new Vector3(0, 3.25f, 0.035f), new Vector3(0.55f, 0.42f, 0.01f), Palette.Hex("D9331F"), 0.2f);
            k.Box(new Vector3(0, 3.25f, 0.04f), new Vector3(0.45f, 0.34f, 0.012f), Palette.Hex("F2F2F2"), 0f);
            // court painted on the dirt
            k.Box(new Vector3(0, 0.02f, 2.6f), new Vector3(4.4f, 0.04f, 5.4f), Palette.Hex("5A5560"), 0f);
            k.Box(new Vector3(0, 0.045f, 2.2f), new Vector3(1.8f, 0.01f, 0.06f), Palette.Text, 0.3f);
            k.Torus(new Vector3(0, 0.045f, 0.3f), 1.9f, 0.03f, 40, 4, Quaternion.Euler(0, 180f, 0), Palette.Text, 0.3f, 180f);
            Spawn(k, "HoopMesh", hoopRoot);
            global::Ascend.Build.BoxCol(global::Ascend.Build.Empty("Board", hoopRoot, new Vector3(0, 3.45f, 0)), Vector3.zero, new Vector3(1.6f, 1.05f, 0.06f));
            global::Ascend.Build.BoxCol(global::Ascend.Build.Empty("Pole", hoopRoot, new Vector3(0, 1.6f, -0.3f)), Vector3.zero, new Vector3(0.14f, 3.2f, 0.14f));

            // rim: orange ring with colliders, and a net
            rim = global::Ascend.Build.Empty("Rim", hoopRoot, new Vector3(0, 3.05f, 0.3f)).transform;
            var rk = new MeshKit();
            rk.Torus(Vector3.zero, 0.23f, 0.012f, 24, 6, Quaternion.identity, Palette.Hex("FF6A1A"), 0.4f);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                rk.Bar(new Vector3(Mathf.Cos(a) * 0.23f, 0, Mathf.Sin(a) * 0.23f), new Vector3(Mathf.Cos(a) * 0.15f, -0.38f, Mathf.Sin(a) * 0.15f), 0.006f, Palette.Text, 0.3f);
            }
            rk.Torus(new Vector3(0, -0.38f, 0), 0.15f, 0.006f, 16, 4, Quaternion.identity, Palette.Text, 0.3f);
            Spawn(rk, "RimMesh", rim);
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                var seg = global::Ascend.Build.Empty("RimCol", rim, new Vector3(Mathf.Cos(a) * 0.23f, 0, Mathf.Sin(a) * 0.23f), Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f, 0));
                global::Ascend.Build.BoxCol(seg, Vector3.zero, new Vector3(0.1f, 0.025f, 0.025f));
            }
            scoreT = Txt.Make("Score", hoopRoot, new Vector3(0, 4.25f, 0.05f), "¡ENCESTA!", 0.24f, Palette.Gold, Palette.Display);
            scoreT.transform.localRotation = Quaternion.Euler(0, 180f, 0);   // readable from the court (+Z)
            var hint = Txt.Make("Hint", hoopRoot, new Vector3(1.3f, 0.9f, 3.4f), XRIn.HasHeadset ? "COGE EL BALÓN Y LÁNZALO" : "PULSA F CERCA DEL BALÓN\n(O HAZ CLIC EN ÉL)", 0.09f, Palette.Text, Palette.Display);
            hint.gameObject.AddComponent<Billboard>();

            // the ball (grab and throw in VR; F / click on desktop)
            ballHome = hoopRoot.TransformPoint(new Vector3(0.8f, 0.35f, 3.4f));
            var bk = new MeshKit();
            bk.Sphere(Vector3.zero, 0.12f, 14, Palette.Hex("E0731F"), 0.1f);
            bk.Torus(Vector3.zero, 0.121f, 0.004f, 24, 4, Quaternion.Euler(90, 0, 0), Palette.Hex("1A1206"), 0f);
            bk.Torus(Vector3.zero, 0.121f, 0.004f, 24, 4, Quaternion.identity, Palette.Hex("1A1206"), 0f);
            var b = Spawn(bk, "Basketball", home);
            b.transform.position = ballHome;
            var sc = b.AddComponent<SphereCollider>(); sc.radius = 0.12f;
            sc.material = new PhysicsMaterial("Ball") { bounciness = 0.75f, dynamicFriction = 0.4f, bounceCombine = PhysicsMaterialCombine.Maximum };
            ball = b.AddComponent<Rigidbody>();
            ball.mass = 0.6f; ball.interpolation = RigidbodyInterpolation.Interpolate; ball.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            grab = b.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach = true;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
        }

        void Throw()
        {
            if (!Rig.Ready || ball == null) return;
            var cam = Rig.I.Head;
            ball.position = cam.position + cam.forward * 0.6f;
            ball.linearVelocity = (cam.forward + Vector3.up * 0.35f).normalized * ThrowSpeed;
            ball.angularVelocity = cam.right * -8f;
            idleSince = Time.time;
            Sfx.Play("dice_land", ball.position, 0.4f, 0.7f);
        }

        void ResetBall()
        {
            if (ball == null) return;
            if (ball.transform.parent != home) ball.transform.SetParent(home, true);
            ball.linearVelocity = Vector3.zero; ball.angularVelocity = Vector3.zero;
            ball.position = ballHome; ball.transform.position = ballHome;
        }

        // ------------------------------------------------------------------ trampoline
        void BuildTrampoline(Vector3 p)
        {
            var t = global::Ascend.Build.Empty("Trampoline", home, p).transform;
            var k = new MeshKit();
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                k.Cylinder(new Vector3(Mathf.Cos(a) * 1.35f, 0.25f, Mathf.Sin(a) * 1.35f), 0.04f, 0.04f, 0.5f, 6, Quaternion.identity, Palette.Hex("2E2E36"));
            }
            k.Torus(new Vector3(0, 0.5f, 0), 1.45f, 0.08f, 36, 6, Quaternion.identity, Palette.Hex("2A5CB8"), 0.15f);
            k.Disc(new Vector3(0, 0.52f, 0), 1.38f, 36, Quaternion.identity, Palette.Hex("15151C"), 0f);
            Spawn(k, "TrampolineMesh", t);
            global::Ascend.Build.BoxCol(global::Ascend.Build.Empty("TrampCol", t, new Vector3(0, 0.26f, 0)), Vector3.zero, new Vector3(2.5f, 0.52f, 2.5f));
            trampoline = new Bounds(home.TransformPoint(p + new Vector3(0, 0.75f, 0)), new Vector3(2.5f, 0.8f, 2.5f));
            var sign = Txt.Make("Sign", t, new Vector3(0, 1.7f, 0), "¡A BOTAR!", 0.26f, Palette.Teal, Palette.Display);
            sign.gameObject.AddComponent<Billboard>();
        }

        IEnumerator Bounce()
        {
            bouncing = true;
            var rig = Rig.I.transform;
            var cc = rig.GetComponent<CharacterController>();
            bool ccOn = cc != null && cc.enabled; if (ccOn) cc.enabled = false;
            float y0 = rig.position.y;
            float v = XRIn.HasHeadset ? 4.5f : 8f;   // gentler in the headset
            Sfx.Play("boing", rig.position, 0.5f);
            Sfx.Play("dice_land", rig.position, 0.5f, 0.6f);
            while (true)
            {
                v -= 9.81f * Time.deltaTime;
                var pos = rig.position + Vector3.up * v * Time.deltaTime;
                if (v < 0f && pos.y <= y0) { pos.y = y0; rig.position = pos; break; }
                rig.position = pos;
                yield return null;
            }
            Physics.SyncTransforms();
            if (ccOn && cc != null) cc.enabled = true;
            lastBounce = Time.time;
            bouncing = false;
        }

        // ------------------------------------------------------------------ soda machine
        void BuildSoda(Vector3 p, float yaw)
        {
            var v = global::Ascend.Build.Empty("SodaMachine", home, p, Quaternion.Euler(0, yaw, 0)).transform;
            var k = new MeshKit();
            var red = Palette.Hex("C21E26");
            k.Box(new Vector3(0, 1.0f, 0), new Vector3(1.0f, 2.0f, 0.8f), red, 0f);
            k.Box(new Vector3(-0.12f, 1.2f, 0.41f), new Vector3(0.6f, 1.3f, 0.02f), Palette.Hex("2A3A4A"), 0.25f);
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 3; c++)
                    k.Cylinder(new Vector3(-0.3f + c * 0.18f, 0.7f + r * 0.3f, 0.45f), 0.05f, 0.05f, 0.16f, 8, Quaternion.identity,
                        (r + c) % 3 == 0 ? Palette.Hex("3399F2") : (r + c) % 3 == 1 ? Palette.Hex("F29926") : Palette.Hex("4DD959"), 0.3f);
            k.Box(new Vector3(0, 0.25f, 0.41f), new Vector3(0.6f, 0.2f, 0.02f), Palette.Hex("1A1A1A"), 0f);
            Spawn(k, "SodaMesh", v);
            global::Ascend.Build.BoxCol(global::Ascend.Build.Empty("SodaCol", v, new Vector3(0, 1f, 0)), Vector3.zero, new Vector3(1f, 2f, 0.8f));
            Txt.Make("Title", v, new Vector3(0, 1.88f, 0.42f), "REFRESCOS", 0.14f, Palette.Text, Palette.Display).transform.localRotation = Quaternion.Euler(0, 180f, 0);
            Txt.Make("Price", v, new Vector3(0.33f, 1.45f, 0.42f), SodaPrice + " FICHAS\nCORRES MÁS\n30 S", 0.045f, Palette.Gold, Palette.Display, TextAlignmentOptions.Center, 0.3f).transform.localRotation = Quaternion.Euler(0, 180f, 0);
            PokeButton.Create("Buy", v, new Vector3(0.33f, 1.15f, 0.44f), Quaternion.Euler(90, 0, 0), 0.05f, Palette.Mint, "COMPRAR", BuySoda, 0.03f);
        }

        void BuySoda()
        {
            var gs = GameState.I;
            if (gs == null || !gs.TakeChips(SodaPrice)) return;
            boostUntil = Time.time + SodaSeconds;
            Sfx.UI("ui_open", 0.7f);
            gs.Notify(XRIn.HasHeadset ? "¡REFRESCANTE! (SABE A PROMESAS ROTAS)" : "¡REFRESCANTE! CORRES MÁS DURANTE " + (int)SodaSeconds + " S", Palette.Mint);
        }

        // ------------------------------------------------------------------ wishing fountain
        void BuildFountain(Vector3 p)
        {
            var f = global::Ascend.Build.Empty("Fountain", home, p).transform;
            var k = new MeshKit();
            k.Cylinder(new Vector3(0, 0.3f, 0), 1.1f, 1.1f, 0.6f, 24, Quaternion.identity, Palette.Hex("C4BEB4"));
            k.Disc(new Vector3(0, 0.58f, 0), 0.95f, 24, Quaternion.identity, Palette.Hex("3C8CC0"), 0.35f);
            k.Cylinder(new Vector3(0, 0.95f, 0), 0.12f, 0.12f, 0.8f, 10, Quaternion.identity, Palette.Hex("CCC6BC"));
            k.Sphere(new Vector3(0, 1.4f, 0), new Vector3(0.25f, 0.1f, 0.25f), 12, Palette.Hex("CCC6BC"), 0f);
            k.Sphere(new Vector3(0, 1.52f, 0), new Vector3(0.12f, 0.18f, 0.12f), 10, Palette.Hex("99D9FF"), 0.8f);
            for (int i = 0; i < 9; i++) { float a = i * 1.7f; k.Cylinder(new Vector3(Mathf.Cos(a) * 0.5f * (0.4f + i * 0.07f), 0.592f, Mathf.Sin(a) * 0.5f), 0.035f, 0.035f, 0.006f, 10, Quaternion.identity, Palette.Gold, 0.7f); }
            Spawn(k, "FountainMesh", f);
            global::Ascend.Build.BoxCol(global::Ascend.Build.Empty("FountainCol", f, new Vector3(0, 0.3f, 0)), Vector3.zero, new Vector3(2.1f, 0.6f, 2.1f));
            var sign = Txt.Make("Sign", f, new Vector3(0, 2.0f, 0), "FUENTE DE LOS DESEOS\n<size=60%>" + WishPrice + " FICHAS</size>", 0.16f, Palette.Teal, Palette.Display);
            sign.gameObject.AddComponent<Billboard>();
            PokeButton.Create("Wish", f, new Vector3(0, 0.75f, -1.16f), Quaternion.Euler(-90, 0, 0), 0.06f, Palette.Gold, "DESEO", Wish, 0.035f);
        }

        void Wish()
        {
            var gs = GameState.I;
            if (gs == null || !gs.TakeChips(WishPrice)) return;
            Sfx.Play("coin_land", transform.position, 0.6f);
            gs.Notify(Fortunes[Random.Range(0, Fortunes.Length)], Palette.Teal);
        }

        // ------------------------------------------------------------------ per frame
        void OnDisable() { ResetBall(); if (bouncing) bouncing = false; }

        void Update()
        {
            if (!Rig.Ready || home == null) return;
            var feet = Rig.I.Feet;

            // trampoline: stepping onto it throws you up (not while one bounce is still going)
            if (!bouncing && !Rig.I.Teleporting && !Rig.I.Frozen && trampoline.Contains(feet + Vector3.up * 0.6f) && Time.time - lastBounce > 0.2f)
                StartCoroutine(Bounce());

            if (ball == null) return;
            // desktop: F near the ball, or a click on it
            if (!XRIn.HasHeadset)
            {
                bool near = (ball.position - Rig.I.Head.position).magnitude < 3f;
                if (near && XRIn.KeyDown("f")) Throw();
#if ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                if (m != null && m.leftButton.wasPressedThisFrame && Rig.I.Cam != null)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(Rig.I.Cam.ScreenPointToRay(m.position.ReadValue()), out hit, 6f) && hit.rigidbody == ball) Throw();
                }
#endif
            }

            // a basket counts when the ball enters the hoop above the rim and leaves it below (as in the original)
            Vector3 rel = ball.position - rim.position;
            bool inside = Mathf.Abs(rel.y) < 0.15f && new Vector2(rel.x, rel.z).magnitude < 0.23f;
            if (inside && !inHoop)
            {
                inHoop = true;
                enteredAbove = rel.y > 0f;
                if (!enteredAbove) { ball.linearVelocity *= 0.3f; ball.AddForce(Vector3.down * 2f, ForceMode.VelocityChange); }
            }
            else if (!inside && inHoop)
            {
                inHoop = false;
                if (enteredAbove && rel.y < 0f && Time.time - lastScore > 0.5f)
                {
                    lastScore = Time.time; score++;
                    scoreText(score);
                    Sfx.Play("win_small", rim.position, 0.7f);
                    Juice.Celebrate(rim.position, 100, 1f);
                }
            }

            // lost balls come home
            bool held = grab != null && grab.isSelected;
            if (held) idleSince = Time.time;
            float far = (ball.position - ballHome).magnitude;
            if (!held && (ball.position.y < home.position.y - 3f || far > 30f || (far > 1f && ball.linearVelocity.sqrMagnitude < 0.01f && Time.time - idleSince > 8f)))
            { ResetBall(); idleSince = Time.time; }
        }

        void scoreText(int n) { if (scoreT != null) scoreT.text = n == 1 ? "¡CANASTA!" : "CANASTAS: " + n; }
    }
}
