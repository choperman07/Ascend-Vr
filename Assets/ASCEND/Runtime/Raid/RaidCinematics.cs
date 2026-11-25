using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>THE HOUSE's entrance (alarm, red search beams, a meteor-fall descent onto the Vault, shockwave and
    /// title card), its death (convulsions and bursts of light, crown knocked off, a slow collapse into a final
    /// blast) and its gloat when the squad loses.</summary>
    public partial class RaidArena
    {
        Vector3 crownHome; Quaternion crownHomeRot; Vector3 bossScale0;

        Transform CineRoot { get { return vault != null ? vault : transform; } }
        Vector3 CineLocal(Vector3 world) { return CineRoot.InverseTransformPoint(world); }

        IEnumerator BossEntrance()
        {
            var bob = boss.GetComponent<Bob>(); if (bob != null) bob.enabled = false;
            if (bossScale0 == Vector3.zero) bossScale0 = boss.localScale;
            var land = boss.localPosition;
            var landW = boss.position;
            var ground = new Vector3(landW.x, CineRoot.position.y, landW.z);
            boss.localPosition = land + Vector3.up * 40f;
            boss.localScale = bossScale0 * 0.7f;
            // alarm + four red search beams sweeping the sky over the landing point
            Sfx.UI("security_alert", 0.8f);
            GameState.I.Notify("WARNING · THE HOUSE IS COMING", Palette.Red);
            var beams = new List<LineRenderer>();
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("EntranceBeam" + i); go.transform.SetParent(CineRoot, false);
                var lr = go.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.widthMultiplier = 0.35f; lr.sharedMaterial = Palette.FlatGlow;
                lr.startColor = Palette.A(Palette.Red, 0.9f); lr.endColor = Palette.A(Palette.Red, 0f);
                beams.Add(lr); temp.Add(go);   // temp: destroyed by ClearAttacks if the cinematic is cut (floor switched)
            }
            for (float t = 0; t < 1.8f; t += Time.deltaTime)
            {
                for (int i = 0; i < 4; i++)
                {
                    float a = (i * 90f + t * 140f) * Mathf.Deg2Rad;
                    beams[i].SetPosition(0, ground + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 7f);
                    beams[i].SetPosition(1, landW + Vector3.up * 30f + new Vector3(Mathf.Sin(t * 3f + i), 0, Mathf.Cos(t * 2f + i)) * 6f);
                    beams[i].widthMultiplier = 0.3f + 0.15f * Mathf.Sin(t * 30f + i);
                }
                yield return null;
            }
            // the fall
            Sfx.Play("orb_whoosh_loop", landW + Vector3.up * 15f, 1f, 0.6f);
            Sfx.Play("boss_phase", landW, 1f, 0.8f);
            var from = boss.localPosition;
            for (float t = 0; t < 1.4f; t += Time.deltaTime)
            {
                float k = t / 1.4f;
                boss.localPosition = Vector3.Lerp(from, land, k * k);
                boss.localScale = Vector3.Lerp(bossScale0 * 0.7f, bossScale0, k);
                boss.localRotation = Quaternion.Euler(0, (1f - k) * 220f, 0);
                for (int i = 0; i < 4; i++) { beams[i].SetPosition(1, boss.position); beams[i].widthMultiplier = 0.4f * (1f - k) + 0.05f; }
                yield return null;
            }
            boss.localPosition = land; boss.localRotation = Quaternion.identity; boss.localScale = bossScale0;
            foreach (var b in beams) Destroy(b.gameObject);
            // impact
            Sfx.Play("explosion_small", landW, 1f, 0.5f);
            Sfx.Play("boss_roar", landW, 1f);
            XRIn.HapticBoth(1f, 0.4f);
            ShakeView(0.03f, 0.6f);
            StartCoroutine(Shockwave(ground, Palette.Red, 16f, 1.1f));
            Juice.Sparks(boss.position, Palette.Red, 90);
            Juice.Sparks(ground, Palette.Gold, 60);
            // title card
            var card = Txt.Glow("BossTitle", CineRoot, CineLocal(boss.position) + new Vector3(0, 4.2f, -1f), "THE HOUSE", 1.1f, Palette.Red, 14f);
            card.gameObject.AddComponent<Billboard>(); temp.Add(card.gameObject);
            var subT = Txt.Make("BossSub", card.transform, new Vector3(0, -0.95f, 0), "RAID BOSS · " + BossMaxHP.ToString("N0") + " HP · NO COVER", 0.36f, Palette.Gold, Palette.Display, TextAlignmentOptions.Center, 14f);
            Voice.Say("boss_rise", boss, 3.2f);
            for (float t = 0; t < 2.6f; t += Time.deltaTime)
            {
                float a = Mathf.Clamp01(Mathf.Min(t / 0.3f, (2.6f - t) / 0.5f));
                card.alpha = a; subT.alpha = a;
                card.transform.localScale = Vector3.one * (1f + 0.25f * (1f - Mathf.Clamp01(t / 0.3f)));
                yield return null;
            }
            Destroy(card.gameObject);
            if (bob != null) { bob.enabled = true; bob.Rebase(); }
        }

        IEnumerator BossDeath()
        {
            var bob = boss.GetComponent<Bob>(); if (bob != null) bob.enabled = false;
            if (bossScale0 == Vector3.zero) bossScale0 = boss.localScale;
            if (crown != null && crownHome == Vector3.zero) { crownHome = crown.localPosition; crownHomeRot = crown.localRotation; }
            var p0 = boss.localPosition; var r0 = boss.localRotation;
            Sfx.Play("boss_death", boss.position, 1f);
            Voice.Say("boss_fall", boss, 3.2f);
            if (shieldRing != null) shieldRing.gameObject.SetActive(false);
            // 1) convulsing, bursts of light out of the body
            for (float t = 0; t < 2.2f; t += Time.deltaTime)
            {
                float k = t / 2.2f;
                boss.localPosition = p0 + Random.insideUnitSphere * 0.25f * (0.4f + k);
                boss.localRotation = r0 * Quaternion.Euler(Random.Range(-6f, 6f) * k, Random.Range(-10f, 10f) * k, Random.Range(-6f, 6f) * k);
                if (Random.value < Time.deltaTime * 7f)
                {
                    var at = boss.position + Random.insideUnitSphere * 2f;
                    Juice.Sparks(at, Random.value < 0.5f ? Palette.Gold : Palette.Red, 40);
                    Sfx.Play("explosion_small", at, 0.8f, Random.Range(0.8f, 1.2f));
                }
                if (coreR != null) Fx.SetColor(coreR, Color.Lerp(Palette.Red, Color.white, Mathf.PingPong(t * 6f, 1f)), 2f);
                yield return null;
            }
            // 2) the crown is knocked off and spins away
            if (crown != null)
            {
                Sfx.Play("coin_land", crown.position, 1f, 0.6f);
                crown.SetParent(CineRoot, true);
                StartCoroutine(Fx.Arc(crown, crown.position + boss.right * 4f + Vector3.down * 5f, 3f, 1.4f));
            }
            // 3) collapse: tilt forward and sink, then the final blast
            var tilt = r0 * Quaternion.Euler(35f, 0, 18f);
            for (float t = 0; t < 1.6f; t += Time.deltaTime)
            {
                float k = Fx.EaseInOut(t / 1.6f);
                boss.localPosition = Vector3.Lerp(p0, p0 + Vector3.down * 3.5f, k) + Random.insideUnitSphere * 0.1f;
                boss.localRotation = Quaternion.Slerp(r0, tilt, k);
                boss.localScale = Vector3.Lerp(bossScale0, bossScale0 * 0.85f, k);
                yield return null;
            }
            var blast = boss.position;
            Sfx.Play("crash_boom", blast, 1f, 0.8f);
            XRIn.HapticBoth(1f, 0.5f);
            ShakeView(0.04f, 0.8f);
            Juice.Sparks(blast, Palette.Gold, 160); Juice.Sparks(blast, Palette.Red, 120); Juice.Sparks(blast, Color.white, 80);
            StartCoroutine(Shockwave(new Vector3(blast.x, CineRoot.position.y + 0.1f, blast.z), Palette.Gold, 22f, 1.4f));
            boss.gameObject.SetActive(false);
            if (crown != null) crown.gameObject.SetActive(false);
            var t2 = Txt.Glow("Fallen", CineRoot, CineLocal(blast) + new Vector3(0, 2.5f, 0), "THE HOUSE HAS FALLEN", 0.9f, Palette.Gold, 16f);
            t2.gameObject.AddComponent<Billboard>();
            Destroy(t2.gameObject, 3.2f);
            foreach (var b in squad) b.Play(Emote.Cheer, 2.5f);
            yield return new WaitForSeconds(2f);
        }

        IEnumerator BossGloat()
        {
            var bob = boss.GetComponent<Bob>(); if (bob != null) bob.enabled = false;
            if (bossScale0 == Vector3.zero) bossScale0 = boss.localScale;
            var p0 = boss.localPosition; var r0 = boss.localRotation;
            Sfx.Play("boss_roar", boss.position, 1f, 0.8f);
            var t1 = Txt.Glow("Gloat", CineRoot, CineLocal(boss.position) + new Vector3(0, 4.5f, -1f), "THE HOUSE ALWAYS WINS", 0.8f, Palette.Red, 16f);
            t1.gameObject.AddComponent<Billboard>(); temp.Add(t1.gameObject);
            for (float t = 0; t < 1.8f; t += Time.deltaTime)
            {
                float k = Mathf.Sin(Mathf.Clamp01(t / 1.8f) * Mathf.PI);
                boss.localPosition = p0 + Vector3.up * (1.2f * k);
                boss.localRotation = r0 * Quaternion.Euler(0, t * 400f, 0);
                boss.localScale = bossScale0 * (1f + 0.25f * k);
                yield return null;
            }
            boss.localPosition = p0; boss.localRotation = r0; boss.localScale = bossScale0;
            Destroy(t1.gameObject);
            if (bob != null) { bob.enabled = true; bob.Rebase(); }
        }

        /// <summary>After a win the boss is rebuilt for the next squad.</summary>
        void ResetBoss()
        {
            if (bossScale0 != Vector3.zero) boss.localScale = bossScale0;
            boss.localRotation = Quaternion.identity;
            if (crown != null && crownHome != Vector3.zero) { crown.gameObject.SetActive(true); crown.SetParent(boss, false); crown.localPosition = crownHome; crown.localRotation = crownHomeRot; }
            if (shieldRing != null) shieldRing.gameObject.SetActive(true);
            if (coreR != null) Fx.ClearColor(coreR);
            var bob = boss.GetComponent<Bob>(); if (bob != null) { bob.enabled = true; bob.Rebase(); }
        }

        IEnumerator Shockwave(Vector3 at, Color c, float radius, float dur)
        {
            // shared mesh per colour (vertex coloured); lives at the scene root, so a cut coroutine must not leave it behind
            var ring = SpawnShared("Shockwave" + ColorUtility.ToHtmlStringRGB(c), null, Vector3.zero, Quaternion.identity, Palette.Neon, () => ShockMesh(c)).transform;
            ring.position = at + Vector3.up * 0.15f;
            ring.localScale = Vector3.zero;
            Destroy(ring.gameObject, dur + 0.1f);
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float e = Fx.EaseOut(t / dur);
                if (ring == null) yield break;
                ring.localScale = new Vector3(radius * e, 1f + 4f * (1f - e), radius * e);
                yield return null;
            }
            if (ring != null) Destroy(ring.gameObject);
        }

        static MeshKit ShockMesh(Color c) { var k = new MeshKit(); k.Torus(Vector3.zero, 1f, 0.04f, 64, 6, Quaternion.identity, c, 1.5f); return k; }
    }
}
