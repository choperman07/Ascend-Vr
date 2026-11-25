using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>A guard standing at a post (lobby entrance, high-roller rope). Easter eggs:
    /// · poke them (hand / click) and they get annoyed — keep going and they walk you back to the lobby;
    /// · win a staring contest (look them in the eye from close up for 6 s) and they crack a smile;
    /// · press the secret DISCO button and every guard on the floor breaks into a dance;
    /// · throw a chip at them... bad idea.</summary>
    public class GuardPost : MonoBehaviour
    {
        public Rarity rarity = Rarity.Rare;
        AvatarBody g;
        int pokes; float lastPoke, stare; bool stareWon, dancing;
        static readonly string[] PokeLines = { "Sir. Please.", "Hands to yourself.", "I'm on duty.", "Last warning." };
        TMPro.TextMeshPro bubble; float bubbleUntil;

        void Start()
        {
            g = Avatars.Create("Guard", transform, Vector3.zero, 0f, rarity, RoninRole.Security);
            g.pose = Pose.Guard;
            g.danceStyle = Random.Range(1, 6);
            var ap = g.gameObject.AddComponent<AvatarProfile>(); ap.displayName = "ASCEND SECURITY"; ap.rarity = rarity; ap.xp = 26000; ap.status = "STAFF · WATCHING THE FLOOR";
            var hb = g.gameObject.AddComponent<SphereCollider>(); hb.isTrigger = true; hb.radius = 0.45f; hb.center = new Vector3(0, 1.3f, 0);
            var box = g.gameObject.AddComponent<GuardHitbox>(); box.guard = g; box.post = this;
            bubble = Txt.Make("Bubble", g.transform, new Vector3(0, 2.2f, 0), "", 0.09f, Palette.Red, Palette.Body);
            bubble.gameObject.AddComponent<Billboard>();
            if (SecurityDirector.I != null) SecurityDirector.I.Register(g);
            StartCoroutine(Watch());
        }

        // coroutines die when the floor is switched off: restart the glances when we come back
        void OnEnable() { GetUp(); if (g != null) StartCoroutine(Watch()); }

        void Say(string t) { if (bubble != null) { bubble.text = t; bubbleUntil = Time.time + 3f; } }

        public AvatarBody Body { get { return g; } }
        /// <summary>Knocked out (EL CAMELLO's third job). Gets back up, none the wiser, after a while.</summary>
        public bool Down { get; private set; }
        Quaternion upRot; Vector3 upPos;
        public void TakeDown() { if (!Down && g != null && isActiveAndEnabled) StartCoroutine(DownRoutine()); }
        IEnumerator DownRoutine()
        {
            Down = true;
            var t = g.transform; upRot = t.localRotation; upPos = t.localPosition;
            g.lookAtTarget = null; Say("¡Ugh!");
            Sfx.Play("chips_sweep", t.position + Vector3.up, 0.9f, 0.5f);
            var hb = g as HumanoidBody;
            for (float u = 0; u < 1f; u += Time.deltaTime / 0.55f)
            {
                float k = u * u;
                t.localRotation = upRot * Quaternion.Euler(-88f * k, 0, 0);
                t.localPosition = upPos + Vector3.up * 0.16f * k;
                yield return null;
            }
            t.localRotation = upRot * Quaternion.Euler(-88f, 0, 0); t.localPosition = upPos + Vector3.up * 0.16f;
            if (hb != null) hb.CloseEyes(true);
            yield return new WaitForSeconds(45f);
            GetUp();
        }
        void GetUp()
        {
            if (!Down || g == null) return;
            Down = false; g.transform.localRotation = upRot; g.transform.localPosition = upPos;
            var hb = g as HumanoidBody; if (hb != null) hb.CloseEyes(false);
        }

        public void Poked()
        {
            if (Time.time - lastPoke > 12f) pokes = 0;
            lastPoke = Time.time; pokes++;
            g.lookAtTarget = Rig.Ready ? Rig.I.Head : null;
            if (pokes <= PokeLines.Length) { Say(PokeLines[pokes - 1]); g.Play(pokes < 3 ? Emote.Nod : Emote.Facepalm, 1.2f); Sfx.Play("ui_error", g.transform.position + Vector3.up * 1.6f, 0.4f, 0.7f); }
            if (pokes == 3) Voice.Say("security_dont", g.HeadBone, 0.55f);
            if (pokes > PokeLines.Length)
            {
                pokes = 0;
                Say("That's it. Lobby. Now.");
                GameState.I.Unlock("POKE THE BEAR", "POKED SECURITY ONE TIME TOO MANY", 250);
                if (FloorManager.I != null && FloorManager.I.Current != 0) FloorManager.I.GoTo(0);
                else if (Rig.Ready)
                {
                    if (PlayerSeating.Seated) PlayerSeating.I.StandUp(false);
                    Rig.I.Teleport(Tower.Arrival(0), Tower.ArrivalYaw, -1, "security_alert");
                }
            }
        }

        void Update()
        {
            if (g == null) return;
            if (bubble != null && bubble.text.Length > 0 && Time.time > bubbleUntil) bubble.text = "";
            if (Down) return;
            // secret disco: guards dance too
            bool disco = DJStage.Disco;
            if (disco != dancing) { dancing = disco; g.pose = disco ? Pose.Dance : Pose.Guard; if (disco) Say("...don't tell my boss."); }
            // staring contest
            if (stareWon || !Rig.Ready || dancing) return;
            var head = Rig.I.Head; var eyes = g.HeadBone.position;
            var d = eyes - head.position;
            if (d.sqrMagnitude < 2.6f * 2.6f && Vector3.Dot(head.forward, d.normalized) > 0.97f)
            {
                stare += Time.deltaTime;
                g.lookAtTarget = head;
                if (stare > 3f && stare - Time.deltaTime <= 3f) Say("...");
                if (stare > 6f)
                {
                    stareWon = true;
                    Say("Ha! Okay, you win.");
                    g.Play(Emote.Cheer, 1.6f);
                    GameState.I.Unlock("STARING CONTEST", "MADE A GUARD BLINK FIRST", 300);
                    Sfx.UI("secret_found", 0.7f);
                }
            }
            else stare = Mathf.Max(0f, stare - Time.deltaTime * 2f);
        }

        IEnumerator Watch()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(2f, 5f));
                if (stare > 0.5f || dancing) continue;
                if (Rig.Ready && (Rig.I.Head.position - g.transform.position).sqrMagnitude < 16f) g.lookAtTarget = Rig.I.Head;
                else { g.lookAtTarget = null; g.lookAt = g.transform.position + Quaternion.Euler(0, Random.Range(-60f, 60f), 0) * g.transform.forward * 3f + Vector3.up * 1.6f; }
            }
        }
    }
}
