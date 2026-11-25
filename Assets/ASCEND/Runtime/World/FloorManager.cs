using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Keeps only the floor you are on active (big performance win on Quest), handles floor changes
    /// (tier checks, fade-teleport, announcer, music) and owns the list of teleport destinations.</summary>
    public class FloorManager : MonoBehaviour
    {
        public static FloorManager I { get; private set; }
        public Transform[] floors;
        public int startFloor = 0;
        public int Current { get; private set; } = -1;
        public readonly List<TeleportPoint> points = new List<TeleportPoint>();
        public event System.Action<int> FloorActivated;
        AudioSource ambience;

        void Awake()
        {
            I = this;
            foreach (var f in floors) if (f != null) points.AddRange(f.GetComponentsInChildren<TeleportPoint>(true));
        }

        void Start()
        {
            Activate(startFloor, false);
            if (startFloor != 0 && Rig.I != null) Rig.I.Place(Tower.Arrival(startFloor), Tower.ArrivalYaw);   // the rig is built in the lobby
            gameObject.AddComponent<CreditsBasement>();   // EL SÓTANO: the team's hall of fame under the lobby
            // MAFIA MODE (the main mode) starts at home, not in the lobby; the trailer recorder keeps the free casino
            if (Run.Enabled && PlayerPrefs.GetInt("asc_trailer_rec", 0) == 0) { gameObject.AddComponent<RunDirector>(); return; }
            if (IntroCinematic.ShouldPlay) gameObject.AddComponent<IntroCinematic>();
            else Voice.Announce("ann_welcome", 0.8f);
        }

        public bool Allowed(int floor, bool notify = true)
        {
            if (Run.Active)
            {
                string why = null;
                if (RunDirector.I == null || !RunDirector.I.InCasino) why = "PRIMERO HABLA CON DON TUCHI";
                else if (!Run.FloorOpen(floor))
                    why = Run.FinalDay ? "HOY SOLO IMPORTA LA AZOTEA" : floor == Tower.Count - 1 ? "LA AZOTEA ABRE EL ÚLTIMO DÍA" : Tower.Floors[floor].name + " ABRE EL DÍA " + Run.OpensOn(floor);
                if (why == null) return true;
                if (notify) { GameState.I.Notify("CERRADO · " + why, Palette.Red); Sfx.UI("ui_error"); }
                return false;
            }
            var tier = Tower.Floors[floor].tier;
            if (GameState.I.CanAccess(tier)) return true;
            if (notify)
            {
                GameState.I.Notify("LOCKED · " + Tower.Floors[floor].name + " OPENS AT " + GameState.TierNames[(int)tier] + " (" + GameState.TierXP[(int)tier].ToString("N0") + " XP)", Palette.TierColor(tier));
                Sfx.UI("ui_error");
                Voice.Announce("security_locked");
            }
            return false;
        }

        /// <summary>Elevator / wrist-menu travel to a floor (arrival point) or to a named spot.</summary>
        public void GoTo(int floor, TeleportPoint spot = null)
        {
            // mid-ride / mid-switch / during the intro a second jump would land the rig on the wrong floor
            if (Elevator.Riding || switching || IntroCinematic.Playing) { Sfx.UI("ui_error", 0.4f); return; }
            if (!Allowed(floor)) return;
            var rig = Rig.I; if (rig == null) return;
            // seated players stand up first: otherwise the seat (on the old floor) stays taken, the rig stays frozen
            // and standing up later teleports you back to a floor that is switched off
            if (PlayerSeating.Seated) PlayerSeating.I.StandUp(false);
            Vector3 pos = spot != null ? spot.transform.position : Tower.Arrival(floor);
            float yaw = spot != null ? spot.transform.eulerAngles.y : Tower.ArrivalYaw;
            bool changing = floor != Current;
            if (changing) StartCoroutine(Switch(floor, pos, yaw));
            else rig.Teleport(pos, yaw, -1);
        }

        bool switching;

        System.Collections.IEnumerator Switch(int floor, Vector3 pos, float yaw)
        {
            switching = true;
            var rig = Rig.I;
            Sfx.UI("elevator_doors", 0.7f);
            // a teleport already fading would swallow this one and strand the player on a switched-off floor
            for (float w = 0; rig.Teleporting && w < 2f; w += Time.deltaTime) yield return null;
            rig.Teleport(pos, yaw, floor, "");
            yield return new WaitForSeconds(0.25f);
            Activate(floor, true);
            yield return null;
            // safety: if the move still didn't happen, place the player directly
            if (!rig.Teleporting && Mathf.Abs(rig.Feet.y - pos.y) > 2f) rig.Place(pos, yaw);
            switching = false;
            yield return new WaitForSeconds(0.4f);
            Sfx.UI("elevator_ding", 0.8f);
            yield return new WaitForSeconds(0.5f);
            Voice.Announce(Tower.Floors[floor].voice);
        }

        /// <summary>Mafia mode: nobody is in the tower (home, the quiet places, the limbo).</summary>
        public void HideAll()
        {
            for (int i = 0; i < floors.Length; i++) if (floors[i] != null) floors[i].gameObject.SetActive(false);
            Current = -1;
            var bd = FindFirstObjectByType<BotDirector>();
            if (bd != null) bd.ClearAll();
        }

        public void Activate(int floor, bool announce)
        {
            for (int i = 0; i < floors.Length; i++) if (floors[i] != null) floors[i].gameObject.SetActive(i == floor);
            Current = floor;
            if (Rig.I != null) Rig.I.Floor = floor;
            var info = Tower.Floors[floor];
            if (string.IsNullOrEmpty(info.music)) Sfx.StopMusic(); else Sfx.Music(info.music, 0.35f);
            if (ambience != null) Destroy(ambience.gameObject);
            var cam = Rig.I != null ? Rig.I.Head : null;
            // no constant background murmur / wind: the floor music is the only bed (players found the ambience loops annoying)
            GameState.I.Record("floor_" + floor);
            if (FloorActivated != null) FloorActivated(floor);
        }
    }
}
