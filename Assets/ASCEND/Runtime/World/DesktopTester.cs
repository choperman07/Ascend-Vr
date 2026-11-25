using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ascend
{
    /// <summary>Desktop testing without a headset: WASD move, Q/E or arrows turn and look, hold right mouse to look, left click presses buttons /
    /// tiles / felt, scroll = quick bet on the nearest table, T wrist menu, Tab menu. Disabled automatically in VR.</summary>
    public class DesktopTester : MonoBehaviour
    {
        float yaw, pitch, vy;
        void OnEnable() { PlayerSeating.Changed += OnSeat; }
        void OnDisable() { PlayerSeating.Changed -= OnSeat; }
        /// <summary>Sitting down at a game tilts the desktop view onto the felt (in VR you just look down).</summary>
        void OnSeat(Seat s)
        {
            if (XRIn.HasHeadset || !Rig.Ready) return;
            pitch = s != null && s.table != null ? 24f : 0f;
            Rig.I.Head.localRotation = Quaternion.Euler(pitch, Rig.I.Head.localEulerAngles.y, 0);
        }
        void Update()
        {
            if (XRIn.HasHeadset || !Rig.Ready) return;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; var m = Mouse.current;
            if (k == null) return;
            var head = Rig.I.Head;
            var origin = Rig.I.Origin != null ? Rig.I.Origin.transform : transform;
            var hp = head.position; hp.y = origin.position.y + (PlayerSeating.Seated ? PlayerSeating.SeatedEye : (XRIn.DesktopCrouch ? 0.95f : 1.65f)); head.position = hp;   // Left Ctrl = crouch (duck the raid laser)   // eye height on desktop
            if (IntroCinematic.Playing) return;   // the cinematic owns the camera: no walking, floor keys or clicks
            if (m != null && m.rightButton.isPressed)
            {
                var d = m.delta.ReadValue() * 0.12f;
                yaw += d.x; pitch = Mathf.Clamp(pitch - d.y, -80f, 80f);
                origin.Rotate(0, d.x, 0, Space.World);
                head.localRotation = Quaternion.Euler(pitch, head.localEulerAngles.y, 0);
            }
            // arrow keys / Q E turn and look without the mouse
            float turn = (k.rightArrowKey.isPressed || k.eKey.isPressed ? 1f : 0f) - (k.leftArrowKey.isPressed || k.qKey.isPressed ? 1f : 0f);
            float look = (k.upArrowKey.isPressed ? 1f : 0f) - (k.downArrowKey.isPressed ? 1f : 0f);
            if (turn != 0f) origin.Rotate(0, turn * 90f * Time.deltaTime, 0, Space.World);
            if (look != 0f) { pitch = Mathf.Clamp(pitch - look * 60f * Time.deltaTime, -80f, 80f); head.localRotation = Quaternion.Euler(pitch, head.localEulerAngles.y, 0); }
            var f = head.forward; f.y = 0; f.Normalize();
            var r = head.right; r.y = 0; r.Normalize();
            Vector3 mv = Vector3.zero;
            if (k.wKey.isPressed) mv += f; if (k.sKey.isPressed) mv -= f;
            if (k.dKey.isPressed) mv += r; if (k.aKey.isPressed) mv -= r;
            float speed = (k.leftShiftKey.isPressed ? 6f : 2.5f) * HomeFun.SpeedMul;   // the caravan park soda
            if (PlayerSeating.Seated || Rig.I.Frozen) mv = Vector3.zero;   // WASD stands you up (PlayerSeating) instead of sliding the chair
            // walk with the rig's CharacterController so tables, walls, machines and the glass stop you
            var cc = origin.GetComponent<CharacterController>();
            if (PlayerSeating.Seated) { }
            else if (cc != null && cc.enabled)
            {
                cc.center = new Vector3(head.localPosition.x, 0.9f, head.localPosition.z);
                cc.height = 1.8f; cc.radius = 0.22f;
                vy = cc.isGrounded ? -0.5f : vy - 9.81f * Time.deltaTime;
                cc.Move(mv * speed * Time.deltaTime + Vector3.up * vy * Time.deltaTime);
            }
            else origin.position += mv * speed * Time.deltaTime;
            // keys 1–9 (top row or numpad) = floors 1–9 (one GoTo per press: 1 used to fire twice)
            for (int i = 0; i < Tower.Count && i < 9; i++)
                if (k[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame || k[(Key)((int)Key.Numpad1 + i)].wasPressedThisFrame) { Go(i); break; }
            if (k.nKey.wasPressedThisFrame) JumpTable(1);
            if (k.bKey.wasPressedThisFrame) JumpTable(-1);
            if (m != null && m.leftButton.wasPressedThisFrame && Rig.I.Cam != null)
            {
                var ray = Rig.I.Cam.ScreenPointToRay(m.position.ReadValue());
                var hits = Physics.RaycastAll(ray, 40f, ~0, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                var rigRoot = Rig.I.transform;
                // UI behind a wall / guest / table must not steal the click: only canvases in front of the first solid hit
                float solid = 40f;
                foreach (var hit in hits) if (!hit.collider.isTrigger && !hit.collider.transform.IsChildOf(rigRoot) && hit.collider.GetComponentInParent<Canvas>() == null) { solid = hit.distance; break; }
                if (ClickUI(m.position.ReadValue(), solid + 0.05f)) return;
                if (PlayerSeating.I != null && !PlayerSeating.Seated)
                {
                    Vector3 sp; var seat = PlayerSeating.Pick(ray, out sp);
                    // only when nothing interactive is closer (buttons and felt take priority)
                    RaycastHit first;
                    bool blocked = Physics.Raycast(ray, out first, 40f, ~0, QueryTriggerInteraction.Ignore) && first.distance + 0.05f < Vector3.Distance(ray.origin, sp) &&
                                   (first.collider.GetComponentInParent<PokeButton>() != null || first.collider.GetComponentInParent<TileTouch>() != null);
                    if (seat != null && !blocked && Vector3.Distance(ray.origin, sp) < 4f && PlayerSeating.I.Sit(seat)) return;
                }
                foreach (var hit in hits)
                {
                    if (hit.collider.transform.IsChildOf(rigRoot)) continue;
                    var pb = hit.collider.GetComponentInParent<PokeButton>();
                    var tile = hit.collider.GetComponentInParent<TileTouch>();
                    var tap = hit.collider.GetComponentInParent<TapZone>();
                    var spot = hit.collider.GetComponentInParent<BetSpot>();
                    var guest = hit.collider.GetComponentInParent<BotAvatar>();
                    if (pb != null) { pb.Press(); break; }
                    var mt = hit.collider.GetComponentInParent<PlayerMocktail>();
                    if (mt != null && hit.distance < 3f) { mt.Sip(); break; }
                    var gh = hit.collider.GetComponent<GuardHitbox>();
                    if (gh != null && hit.distance < 4f) { gh.Poke(); break; }
                    // easter eggs that are hand-only in VR: pet Lucky, pick up the golden chip
                    var cat = hit.collider.GetComponentInParent<LuckyCat>();
                    if (cat != null && hit.distance < 3f) { cat.Pet(); break; }
                    var gold = hit.collider.GetComponentInParent<GoldenChip>();
                    if (gold != null && hit.distance < 3f) { gold.Found(); break; }
                    if (guest != null && guest.job != BotJob.Waiter && guest.job != BotJob.Patrol && hit.distance < 5f) { NpcChat.Open(guest); break; }
                    if (tile != null && tile.onTouch != null) { tile.onTouch(); break; }
                    if (tap != null && tap.table != null) { tap.table.Tapped(); break; }
                    if (spot != null)
                    {
                        // exact cell: intersect the click ray with the felt plane, then pick the cell containing that point
                        var target = spot;
                        if (spot.game != null) { var exact = spot.game.SpotAt(ray); if (exact != null) target = exact; }   // each cell's own plane (tilted boards)
                        target.AddValue(ChipKit.Values[0] * 5, target.transform.position + Vector3.up * 0.2f);
                        break;
                    }
                    if (!hit.collider.isTrigger) break;
                }
            }
#endif
        }
        // without a headset the controllers are untracked and sit just under the camera, so the hand meshes fill the
        // view from inside. Hold them in front of the camera instead, like a first-person view.
        void LateUpdate()
        {
            if (XRIn.HasHeadset || !Rig.Ready) return;
            var head = Rig.I.Head;
            if (Rig.I.LeftCtrl != null) Rig.I.LeftCtrl.SetPositionAndRotation(head.TransformPoint(new Vector3(-0.22f, -0.3f, 0.45f)), head.rotation * Quaternion.Euler(15f, 10f, 0));
            if (Rig.I.RightCtrl != null) Rig.I.RightCtrl.SetPositionAndRotation(head.TransformPoint(new Vector3(0.22f, -0.3f, 0.45f)), head.rotation * Quaternion.Euler(15f, -10f, 0));
        }

        static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> uiHits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        /// <summary>World-space canvas buttons for mouse testing (the XR UI module only listens to controllers here).</summary>
        static bool ClickUI(Vector2 screen, float maxDist)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screen };
            uiHits.Clear();
            es.RaycastAll(ped, uiHits);
            foreach (var h in uiHits)
            {
                if (h.gameObject == null || h.distance > maxDist) continue;
                var b = h.gameObject.GetComponentInParent<UnityEngine.UI.Button>();
                if (b != null && b.IsActive() && b.interactable) { b.onClick.Invoke(); return true; }
                var tg = h.gameObject.GetComponentInParent<UnityEngine.UI.Toggle>();
                if (tg != null && tg.IsActive() && tg.interactable) { tg.isOn = !tg.isOn; return true; }
            }
            return false;
        }

        void Go(int f) { if (FloorManager.I != null) FloorManager.I.GoTo(f); }

        int tableIdx = -1;
        /// <summary>Desktop shortcut: N / B jump to the next / previous game on this floor and take its seat.</summary>
        void JumpTable(int dir)
        {
            if (FloorManager.I == null || FloorManager.I.Current < 0) return;
            var floor = FloorManager.I.floors[FloorManager.I.Current];
            var games = new List<TableGame>(floor.GetComponentsInChildren<TableGame>());
            games.RemoveAll(x => x.PlayerSeat == null);
            if (games.Count == 0) return;
            games.Sort((a, b) => Mathf.Atan2(a.transform.position.x, a.transform.position.z).CompareTo(Mathf.Atan2(b.transform.position.x, b.transform.position.z)));
            tableIdx = ((tableIdx + dir) % games.Count + games.Count) % games.Count;
            var g = games[tableIdx];
            if (PlayerSeating.Seated) PlayerSeating.I.StandUp(false);
            if (PlayerSeating.I != null && PlayerSeating.I.Sit(g.PlayerSeat)) GameState.I.Notify(g.title + " · " + (tableIdx + 1) + "/" + games.Count, Palette.Teal);
        }
    }
}
