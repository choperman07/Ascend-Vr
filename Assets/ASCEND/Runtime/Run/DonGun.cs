using UnityEngine;

namespace Ascend
{
    /// <summary>The last thing you see when the quota is not met: the Don draws a snub-nosed revolver, levels it at
    /// you and fires. Cartoon style: one flash, one bang, cut to black. Draw() raises the arm; Fire() is the shot.</summary>
    public class DonGun : MonoBehaviour
    {
        AvatarBody don; Transform target, gun, flash, handBone, midBone;
        static readonly HumanBodyBones[] Fingers = { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal,
            HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightLittleIntermediate }; float t0, fired = -1f; Light fl;
        public bool Aimed { get { return Time.time - t0 > 0.75f; } }
        public Vector3 Muzzle { get { return gun != null ? gun.TransformPoint(new Vector3(0, 0.02f, 0.17f)) : transform.position; } }

        public static DonGun Draw(AvatarBody don, Transform target)
        {
            if (don == null || target == null) return null;
            var g = new GameObject("Don Gun").AddComponent<DonGun>();
            g.don = don; g.target = target; g.t0 = Time.time;
            g.Build();
            Sfx.Play("kn_weapon_swap", don.transform.position + Vector3.up * 1.3f, 0.7f);
            return g;
        }

        void Build()
        {
            gun = new GameObject("Revolver").transform; gun.SetParent(transform, false);
            var k = new MeshKit(); Color steel = Palette.Hex("2B2B33"), dark = Palette.Hex("14141A"), wood = Palette.Hex("5A3420");
            k.Box(new Vector3(0, 0.02f, 0.06f), new Vector3(0.03f, 0.036f, 0.2f), steel, 0f);                       // frame + barrel
            k.Cylinder(new Vector3(0, 0.02f, 0.02f), 0.027f, 0.027f, 0.055f, 10, Quaternion.Euler(90, 0, 0), dark, 0f);   // cylinder
            k.Box(new Vector3(0, 0.045f, 0.15f), new Vector3(0.008f, 0.012f, 0.012f), dark, 0f);                    // front sight
            k.Box(new Vector3(0, -0.045f, -0.035f), new Vector3(0.028f, 0.1f, 0.04f), Quaternion.Euler(-18, 0, 0), wood, 0f);   // grip
            k.Box(new Vector3(0, -0.012f, 0.015f), new Vector3(0.008f, 0.03f, 0.05f), dark, 0f);                    // trigger guard
            k.Spawn("Mesh", gun, Palette.Glossy, false);
            flash = new GameObject("Flash").transform; flash.SetParent(gun, false); flash.localPosition = new Vector3(0, 0.02f, 0.24f);
            var f = new MeshKit(); Color hot = Palette.Hex("FFE9A8");
            f.Sphere(Vector3.zero, new Vector3(0.09f, 0.09f, 0.14f), 10, hot, 3f);
            for (int i = 0; i < 6; i++) f.Box(Vector3.zero, new Vector3(0.016f, 0.3f, 0.016f), Quaternion.Euler(0, 0, i * 30f), hot, 3f);
            f.Spawn("Star", flash, Palette.Neon, false);
            fl = flash.gameObject.AddComponent<Light>(); fl.type = LightType.Point; fl.range = 6f; fl.intensity = 6f; fl.color = new Color(1f, 0.85f, 0.55f); fl.shadows = LightShadows.None;
            flash.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (don == null || target == null) { Destroy(gameObject); return; }
            var bt = don.transform; var head = don.HeadBone != null ? don.HeadBone.position : bt.position + Vector3.up * 1.6f;
            var sho = head + Vector3.down * 0.24f + bt.right * 0.2f;
            var dir = (target.position + Vector3.down * 0.05f - sho).normalized;
            float k = Mathf.Clamp01((Time.time - t0) / 0.65f); k = k * k * (3f - 2f * k);
            float kick = fired > 0f ? Mathf.Clamp01(1f - (Time.time - fired) / 0.25f) : 0f;
            var low = sho + Vector3.down * 0.5f + bt.forward * 0.12f;
            var hand = Vector3.Lerp(low, sho + dir * 0.56f + Vector3.up * kick * 0.07f, k);
            don.Reach(true, hand, Mathf.Max(0.2f, k));
            var aim = Vector3.Slerp((bt.forward + Vector3.down * 2f).normalized, (dir + Vector3.up * kick * 0.35f).normalized, k);
            // the grip sits in the real fist (the IK hand trails its target a little while the arm comes up)
            if (handBone == null) { var an = don.GetComponentInChildren<Animator>(); if (an != null && an.isHuman) handBone = an.GetBoneTransform(HumanBodyBones.RightHand); }
            var grip = handBone != null ? handBone.position : hand;
            if (handBone != null && k > 0.05f)
            {
                // the wrist follows the barrel instead of hanging limp: the hand lies along the gun and the fingers close on the grip
                if (midBone == null) { var an2 = don.GetComponentInChildren<Animator>(); if (an2 != null) midBone = an2.GetBoneTransform(HumanBodyBones.RightMiddleProximal); }
                if (midBone != null)
                {
                    var fd = midBone.position - handBone.position;
                    if (fd.sqrMagnitude > 1e-6f) handBone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(fd.normalized, aim), k) * handBone.rotation;
                    var an3 = don.GetComponentInChildren<Animator>();
                    foreach (var fb in Fingers)
                    {
                        var ft = an3 != null ? an3.GetBoneTransform(fb) : null;
                        if (ft != null) ft.rotation = Quaternion.AngleAxis(-55f * k, Vector3.Cross(aim, Vector3.up).normalized) * ft.rotation;   // curl down around the grip
                    }
                    grip = (handBone.position + midBone.position) * 0.5f;
                }
            }
            gun.position = grip + aim * 0.03f - Vector3.up * 0.005f;   // the grip runs down through the closed fist
            gun.rotation = Quaternion.LookRotation(aim, Vector3.up);
            if (fired > 0f && Time.time - fired > 0.09f && flash.gameObject.activeSelf) flash.gameObject.SetActive(false);
        }

        public void Fire()
        {
            if (fired > 0f) return;
            fired = Time.time;
            flash.gameObject.SetActive(true); flash.localRotation = Quaternion.Euler(0, 0, Random.value * 60f);
            Sfx.Play("kn_blaster", Muzzle, 1f, 0.7f);
            Sfx.UI("crash_boom", 0.8f);
        }
    }
}
