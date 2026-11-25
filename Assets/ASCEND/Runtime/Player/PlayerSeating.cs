using UnityEngine;

namespace Ascend
{
    /// <summary>Sit on any free seat: table stools, bar stools, lounge chairs and sofas.
    /// VR: aim the right controller at a seat (it lights up with "A · SIT") and press A. Desktop: click a seat.
    /// While seated your view drops to seated eye height, locomotion is paused and the table's betting panel comes
    /// up to you. Stand up with A again, the left stick, WASD or Space.</summary>
    public class PlayerSeating : MonoBehaviour
    {
        public static PlayerSeating I { get; private set; }
        public Seat Current { get; private set; }
        public static bool Seated { get { return I != null && I.Current != null; } }
        /// <summary>Upright seated eye height above the cushion (an adult sits ~0.75-0.8 m eye-over-seat).</summary>
        public const float EyeOverSeat = 0.78f;
        /// <summary>Seated eye height above the floor for the current seat (desktop camera; VR lowers the rig to match).</summary>
        public static float SeatedEye { get { return I != null && I.Current != null ? I.Current.height + EyeOverSeat : 1.4f; } }
        /// <summary>Raised whenever the player sits down or stands up (tables show their betting panel only while seated).</summary>
        public static event System.Action<Seat> Changed;

        Seat hover; Transform hint;
        public bool Hovering { get { return hover != null; } } TMPro.TextMeshPro hintT;
        float vrDrop, sitTime;
        bool dropApplied;
        LineRenderer ray;

        void Awake() { I = this; }

        void Start()
        {
            hint = new GameObject("SitHint").transform;
            hint.SetParent(transform, false);
            var k = new MeshKit(); k.Ring(Vector3.zero, 0.26f, 0.3f, 40, Quaternion.identity, Palette.Mint, 1.2f);
            k.Spawn("Ring", hint, Palette.Neon, false);
            hintT = Txt.Glow("Label", hint, new Vector3(0, 0.35f, 0), "", 0.07f, Palette.Mint);
            hintT.gameObject.AddComponent<Billboard>();
            hint.gameObject.SetActive(false);
            var go = new GameObject("SitRay"); go.transform.SetParent(transform, false);
            ray = go.AddComponent<LineRenderer>(); ray.sharedMaterial = Palette.FlatGlow; ray.widthMultiplier = 0.004f; ray.positionCount = 2;
            ray.startColor = Palette.A(Palette.Mint, 0.8f); ray.endColor = Palette.A(Palette.Mint, 0f); ray.enabled = false;
        }

        void Update()
        {
            var rig = Rig.I; if (rig == null) return;
            if (Current != null) { SeatedUpdate(rig); return; }
            // VR: aim at a seat with the right controller
            hover = null;
            if (XRIn.HasHeadset && rig.RightCtrl != null)
            {
                var r = new Ray(rig.RightCtrl.position, rig.RightCtrl.forward);
                var s = Pick(r, out var point);
                if (s != null && CanUse(s)) hover = s;
                ray.enabled = hover != null;
                if (hover != null) { ray.SetPosition(0, r.origin); ray.SetPosition(1, point); }
                if (hover != null && XRIn.Down(Hand.Right, Btn.Primary)) Sit(hover);
            }
            ShowHint(hover);
        }

        static readonly RaycastHit[] hits = new RaycastHit[24];
        /// <summary>Nearest seat along the ray (seat pick volumes are triggers inside the furniture colliders).</summary>
        public static Seat Pick(Ray r, out Vector3 point)
        {
            point = r.origin;
            int n = Physics.RaycastNonAlloc(r, hits, 4f, ~0, QueryTriggerInteraction.Collide);
            Seat best = null; float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.name != "SitPick") continue;
                var s = hits[i].collider.GetComponentInParent<Seat>();
                if (s != null && hits[i].distance < bd) { bd = hits[i].distance; best = s; point = hits[i].point; }
            }
            return best;
        }

        bool CanUse(Seat s) { return s != null && s.occupant == null && !s.playerOn && s.enabled; }

        void ShowHint(Seat s)
        {
            if (s == null) { if (hint.gameObject.activeSelf) hint.gameObject.SetActive(false); return; }
            hint.gameObject.SetActive(true);
            hint.position = s.transform.position + Vector3.up * (s.height + 0.02f);
            hintT.text = XRIn.HasHeadset ? "A · SIT" : "CLICK · SIT";
        }

        /// <summary>Desktop click or any other code (teleport spots) asks the player to sit.</summary>
        public bool Sit(Seat s)
        {
            // a guest playing "your" machine politely gets up when you want it
            if (s != null && s.occupant != null && s.table != null && s == s.table.PlayerSeat)
            {
                var bot = s.occupant.GetComponent<BotAvatar>();
                if (bot != null) { bot.Say("all yours, good luck!"); bot.Leave(); }
            }
            if (!CanUse(s)) { if (s != null && s.occupant != null) GameState.I.Notify("THAT SEAT IS TAKEN", Palette.Gold); return false; }
            var rig = Rig.I; if (rig == null) return false;
            if (Current != null) StandUp(false);
            Current = s; s.playerOn = true; sitTime = Time.time;
            vrDrop = 0f; dropApplied = false;
            hint.gameObject.SetActive(false); ray.enabled = false;
            // feet go just behind the seat centre, facing where the seat faces (table, bar, room)
            var feet = s.transform.position + s.transform.forward * (s.tableEdge ? 0.08f : 0.02f);
            rig.Teleport(feet, s.transform.eulerAngles.y, -1, "chip_place");
            rig.Freeze(true);
            if (XRIn.HasHeadset)
            {
                // lower the world view by the difference between standing and seated eye height
                float standing = Mathf.Max(1.3f, rig.Head.position.y - rig.transform.position.y);
                vrDrop = Mathf.Clamp(standing - (s.height + EyeOverSeat), 0f, 0.6f);
                StartCoroutine(DropLater(s));
            }
            Sfx.UI("ui_open", 0.5f);
            if (Changed != null) Changed(s);
            if (s.table != null) s.table.PlayerSat();
            GameState.I.Notify((s.table != null ? "SEATED AT " + s.table.title : "SEATED") + " · " + (XRIn.HasHeadset ? "EXIT OR LEFT STICK TO STAND" : "WASD OR SPACE TO STAND"), Palette.Mint);
            return true;
        }

        System.Collections.IEnumerator DropLater(Seat s)
        {
            yield return new WaitForSeconds(0.3f);   // after the teleport fade
            // stood up (EXIT) or moved to another seat inside the fade: lowering now would sink you into the floor
            if (Current != s || dropApplied || vrDrop <= 0f) yield break;
            var rig = Rig.I; if (rig == null) yield break;
            var cc = rig.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            rig.transform.position += Vector3.up * -vrDrop;
            dropApplied = true;
        }

        void SeatedUpdate(Rig rig)
        {
            // the seat's floor was switched off (elevator / wrist menu while seated): you have already been moved away,
            // so let go of the seat without teleporting back to it or re-adding the seated height drop
            if (!Current.isActiveAndEnabled) { Release(false, false); return; }
            if (Time.time - sitTime < 0.6f) return;
            // stand up: EXIT on the betting panel, a firm push of the left stick, or (desktop) WASD / Space
            bool stand = XRIn.Stick(Hand.Left).magnitude > 0.85f
                         || XRIn.KeyDown("space") || XRIn.KeyDown("w") || XRIn.KeyDown("a") || XRIn.KeyDown("s") || XRIn.KeyDown("d");
            if (stand) StandUp(true);
        }

        public void StandUp(bool place) { Release(place, true); }

        void Release(bool place, bool restoreHeight)
        {
            var s = Current; if (s == null) return;
            Current = null; s.playerOn = false;
            var rig = Rig.I; if (rig == null) { vrDrop = 0f; dropApplied = false; return; }
            var cc = rig.GetComponent<CharacterController>();
            if (dropApplied)
            {
                if (cc != null) cc.enabled = false;
                if (restoreHeight) rig.transform.position += Vector3.up * vrDrop;
                if (cc != null) cc.enabled = true;
            }
            vrDrop = 0f; dropApplied = false;
            rig.Freeze(false);
            if (Changed != null) Changed(null);
            if (place) rig.Teleport(s.transform.position - s.transform.forward * 0.75f, s.transform.eulerAngles.y, -1, "");
            Sfx.UI("ui_close", 0.4f);
        }
    }
}
