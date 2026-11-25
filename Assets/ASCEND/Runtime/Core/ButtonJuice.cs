using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ascend
{
    /// <summary>Button feel of the skin: on hover/selection a solid colour block fades in under the caption and the
    /// caption turns dark; press squashes it a little; disabled buttons fade out. It never touches the button's own
    /// fill colour, so code that tints targetGraphic (locked floors, chosen amounts…) keeps working.</summary>
    public class ButtonJuice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Image highlight;
        public TMP_Text caption;
        public Color captionColor = Color.white;
        public bool Selected;
        Button button; CanvasGroup group;
        bool hover, down;
        float h, s = 1f;

        void Awake()
        {
            button = GetComponent<Button>();
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }

        void OnDisable() { hover = down = false; h = Selected ? 1f : 0f; s = 1f; transform.localScale = Vector3.one; Apply(); }

        public void OnPointerEnter(PointerEventData e) { hover = true; }
        public void OnPointerExit(PointerEventData e) { hover = false; down = false; }
        public void OnPointerDown(PointerEventData e) { down = true; }
        public void OnPointerUp(PointerEventData e) { down = false; }

        void Update()
        {
            bool live = button == null || button.IsInteractable();
            float target = (Selected || (live && hover)) ? 1f : 0f;
            h = Mathf.MoveTowards(h, target, Time.unscaledDeltaTime * 9f);
            float st = live && down ? 0.95f : live && hover ? 1.04f : 1f;
            s = Mathf.Lerp(s, st, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
            transform.localScale = new Vector3(s, s, 1f);
            group.alpha = live || Selected ? 1f : 0.42f;
            Apply();
        }

        void Apply()
        {
            if (highlight != null) { var c = highlight.color; c.a = h; highlight.color = c; }
            if (caption != null) caption.color = Color.Lerp(captionColor, UISkin.Ink, h);
        }
    }
}
