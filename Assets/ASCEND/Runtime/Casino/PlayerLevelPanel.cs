using UnityEngine;

namespace Ascend
{
    /// <summary>A table's betting panel only exists for the player while they are SEATED at that table or machine.
    /// It floats to your RIGHT SIDE (not in front of the game), turned towards you like a tablet on an arm, so the
    /// felt, the wheel and the dealer stay in full view. It can be minimised to a small tab (the "—" button), and on
    /// spin/deal games it tucks itself away while the round plays and comes back when bets open again.</summary>
    public class PlayerLevelPanel : MonoBehaviour
    {
        public Transform table;
        public Vector3 standLocal = new Vector3(0, 0, -1.05f);
        public float parkHeight = 1.2f;
        [Tooltip("Extra height for games with tall grids (mines, keno)")] public float lift;
        Vector3 parkLocal;
        float blend, shown;
        Canvas canvas; CanvasGroup group;
        Vector3 baseScale;
        TableGame game;
        bool minimised;
        RectTransform tab; CanvasGroup tabGroup; TMPro.TextMeshProUGUI tabT;

        public void Init(Transform t, Vector3 stand, Vector3 parkPos)
        {
            table = t; standLocal = stand;
            parkLocal = new Vector3(parkPos.x, parkHeight, parkPos.z);
        }

        public void Minimise() { minimised = true; Sfx.UI("ui_close", 0.35f); }
        public void Restore() { minimised = false; Sfx.UI("ui_open", 0.35f); }

        void Start()
        {
            canvas = GetComponent<Canvas>();
            // (not "??": in the editor a missing component is a fake-null object, so ?? kept it and the fade never applied)
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            baseScale = transform.localScale;
            game = table != null ? table.GetComponent<TableGame>() : null;
            // the small tab that stays when the panel is tucked away
            tab = UIKit.Panel("BetTab", table, Vector3.zero, Quaternion.identity, new Vector2(300, 96), 0.15f, Palette.Gold);
            tabGroup = tab.gameObject.AddComponent<CanvasGroup>();
            var b = UIKit.Btn(tab, "SHOW BETS", 10, 10, 280, 76, Palette.Gold, Restore, 32);
            tabT = b.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            Apply(0f, 0f);
        }

        bool SeatedHere()
        {
            var ps = PlayerSeating.I;
            return ps != null && ps.Current != null && ps.Current.table != null && ps.Current.table.transform == table;
        }

        void Apply(float b, float tabVis)
        {
            if (group != null) { group.alpha = b; group.interactable = b > 0.6f; group.blocksRaycasts = b > 0.6f; }
            if (canvas != null) canvas.enabled = b > 0.01f;
            transform.localScale = baseScale * Mathf.Lerp(0.35f, 1f, Fx.EaseOut(b));
            if (tab != null)
            {
                tabGroup.alpha = tabVis; tabGroup.interactable = tabGroup.blocksRaycasts = tabVis > 0.6f;
                var c = tab.GetComponent<Canvas>(); if (c != null) c.enabled = tabVis > 0.01f;
            }
        }

        void LateUpdate()
        {
            if (table == null || baseScale == Vector3.zero) return;
            bool here = SeatedHere();
            if (!here) minimised = false;
            // spin / deal games tuck the panel away while the round plays, it returns when bets open again
            bool roundHide = game != null && game.AutoHidePanel && !game.AcceptingBets;
            bool open = here && !minimised && !roundHide;
            float prev = blend;
            blend = Mathf.MoveTowards(blend, open ? 1f : 0f, Time.deltaTime * 4f);
            shown = Mathf.MoveTowards(shown, here && !open ? 1f : 0f, Time.deltaTime * 4f);
            if (prev == 0f && blend > 0f) Sfx.Play("ui_open", transform.position, 0.3f);
            if (tabT != null) tabT.text = roundHide && !minimised ? "ROUND IN PLAY" : "SHOW BETS";
            Apply(blend, shown);
            if (!Rig.Ready || (blend <= 0f && shown <= 0f)) { if (blend <= 0f) transform.position = table.TransformPoint(parkLocal); return; }

            // to the RIGHT of the seated player, at forearm height, angled towards the eyes
            var head = Rig.I.Head;
            var seat = PlayerSeating.I != null ? PlayerSeating.I.Current : null;
            var fwd = seat != null ? seat.transform.forward : table.forward;
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            var target = head.position + fwd * 0.38f + right * 0.4f + Vector3.down * (0.2f - lift);
            var look = target - (head.position + Vector3.down * 0.08f);
            var targetRot = Quaternion.LookRotation(look, Vector3.up);
            var from = table.TransformPoint(parkLocal);
            var pos = Vector3.Lerp(from, target, Fx.EaseInOut(Mathf.Max(blend, 0.001f)));
            float k = blend < 1f ? 1f : 1f - Mathf.Exp(-10f * Time.deltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, pos, k), Quaternion.Slerp(transform.rotation, targetRot, k));
            if (tab != null)
            {
                var tp = head.position + fwd * 0.42f + right * 0.36f + Vector3.down * 0.36f;
                tab.SetPositionAndRotation(tp, Quaternion.LookRotation(tp - head.position, Vector3.up));
            }
        }
    }
}
