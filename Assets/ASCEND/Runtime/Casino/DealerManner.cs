using UnityEngine;

namespace Ascend
{
    /// <summary>What a croupier does between rounds: greets whoever walks up with a wave (each dealer on its own
    /// rhythm, so a row of tables never moves in unison) and keeps both hands above the felt.</summary>
    public class DealerManner : MonoBehaviour
    {
        public AvatarBody body;
        float next, range;
        bool wasNear;

        public static void Attach(AvatarBody dealer, float tableTopWorldY)
        {
            if (dealer == null) return;
            var m = dealer.gameObject.AddComponent<DealerManner>();
            m.body = dealer;
            m.range = Random.Range(5.5f, 8f);
            m.next = Time.time + Random.Range(0.5f, 6f);
            var hb = dealer as HumanoidBody;
            if (hb != null) hb.handFloor = tableTopWorldY + 0.045f;
        }

        void Update()
        {
            if (body == null) return;
            var head = TrailerView.Head; if (head == null) return;
            var d = head.position - transform.position; d.y = 0;
            bool near = d.magnitude < range;
            // a wave when you come into range, then again every now and then while you stay
            if (near && (!wasNear || Time.time >= next) && body.LastEmoteTime + 2.5f < Time.time)
            {
                body.Play(Emote.Wave, Random.Range(2.0f, 2.8f));
                next = Time.time + Random.Range(7f, 15f);
            }
            wasNear = near;
        }
    }
}
