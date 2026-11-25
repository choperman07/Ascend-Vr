using System;
using System.Threading.Tasks;
using UnityEngine;
#if ASCEND_NGO && ASCEND_MPS
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
#endif
#if ASCEND_VIVOX && !UNITY_WEBGL
using Unity.Services.Vivox;
#endif

namespace Ascend
{
    /// <summary>Online floor: everyone who joins shares the same casino (avatars, head and hands, floor) and talks with
    /// proximity voice chat. Uses Unity Multiplayer Sessions (relay, no server to host) + Netcode + Vivox.
    /// Needs the project linked to a Unity Cloud project (Edit → Project Settings → Services). Free tier is enough.</summary>
    public class OnlineSession : MonoBehaviour
    {
        public string sessionId = "ascend-main-floor";
        public int maxPlayers = 16;
        public string StatusText { get; private set; } = "OFFLINE · SIMULATED PLAYERS ON THE FLOOR";
        public bool Connected { get; private set; }
        public bool MicMuted { get; private set; }
        public string VoiceChannel { get; private set; }
        bool busy;
        float nextPos;
        int shownCount = -1; string voiceError = "";
#if ASCEND_NGO && ASCEND_MPS
        ISession session;
#endif

        public async void Join()
        {
            if (busy || Connected) return;
            busy = true;
            try
            {
#if ASCEND_NGO && ASCEND_MPS
                StatusText = "CONNECTING TO UNITY SERVICES…";
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
                StatusText = "JOINING THE ONLINE FLOOR…";
                // Secure WebSockets on every platform: browsers can only use WSS, and using it everywhere
                // lets the web build, Windows, Mac and Quest players share the same floor.
                var nm = NetworkManager.Singleton;
                var utp = nm != null ? nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>() : null;
                if (utp != null) utp.UseWebSockets = true;
                var options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork(new RelayNetworkOptions(RelayProtocol.WSS));
                session = await MultiplayerService.Instance.CreateOrJoinSessionAsync(sessionId, options);
                Connected = true;
                StatusText = "ONLINE · " + session.PlayerCount + " PLAYER(S) ON THE FLOOR · SESSION " + session.Id;
                GameState.I.Notify("ONLINE · WELCOME TO THE SHARED FLOOR", Palette.Mint);
                await StartVoice(session.Id);
#else
                StatusText = "ONLINE PACKAGES MISSING (NETCODE + MULTIPLAYER SERVICES)";
                await Task.Yield();
#endif
            }
            catch (Exception e)
            {
                Connected = false;
                StatusText = "COULD NOT GO ONLINE: " + e.Message + "\nLINK THE PROJECT IN PROJECT SETTINGS > SERVICES, THEN TRY AGAIN.";
                Debug.LogWarning("[ASCEND] Online join failed: " + e);
            }
            busy = false;
        }

        public async void Leave()
        {
            if (busy) return;
            busy = true;
            try
            {
#if ASCEND_VIVOX && !UNITY_WEBGL
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn) await VivoxService.Instance.LeaveAllChannelsAsync();
#endif
#if ASCEND_NGO && ASCEND_MPS
                if (session != null) await session.LeaveAsync();
                session = null;
#else
                await Task.Yield();
#endif
            }
            catch (Exception e) { Debug.LogWarning("[ASCEND] Leave: " + e.Message); }
            Connected = false; VoiceChannel = null; shownCount = -1; voiceError = "";
            StatusText = "OFFLINE · SIMULATED PLAYERS ON THE FLOOR";
            busy = false;
        }

        async Task StartVoice(string id)
        {
#if ASCEND_VIVOX && !UNITY_WEBGL
            if (!GameState.I.voiceChat) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
#endif
            // VoiceChannel is only set once the channel is really joined: before, a failed init/login left it set and the
            // 3D-position updates, quick-chat and the status line all acted as if voice was on
            string channel = "ascend-" + id;
            try
            {
                if (VivoxService.Instance == null) throw new Exception("VIVOX SERVICE NOT AVAILABLE");
                VivoxService.Instance.ChannelJoined -= OnChannelJoined;
                VivoxService.Instance.ChannelJoined += OnChannelJoined;
                VivoxService.Instance.ChannelMessageReceived -= OnMessage;
                VivoxService.Instance.ChannelMessageReceived += OnMessage;
                try { await VivoxService.Instance.InitializeAsync(); } catch (Exception) { /* already initialised */ }
                if (!VivoxService.Instance.IsLoggedIn)
                    await VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = GameState.I.playerName.Replace(" ", "_").Replace("#", "") });
                var props = new Channel3DProperties(32, 2, 1.0f, AudioFadeModel.InverseByDistance);
                await VivoxService.Instance.JoinPositionalChannelAsync(channel, ChatCapability.TextAndAudio, props);
                VoiceChannel = channel;
                if (MicMuted) VivoxService.Instance.MuteInputDevice();   // muted from the menu before voice came up
                StatusText += "\nVOICE CHAT ON · PROXIMITY";
            }
            catch (Exception e)
            {
                VoiceChannel = null;
                voiceError = "\nVOICE UNAVAILABLE: " + e.Message;
                StatusText += voiceError;
                Debug.LogWarning("[ASCEND] Vivox: " + e);
            }
#else
            await Task.Yield();
#endif
        }

#if ASCEND_VIVOX && !UNITY_WEBGL
        void OnChannelJoined(string channel) { GameState.I.Notify("VOICE CHAT CONNECTED · TALK TO PEOPLE NEAR YOU", Palette.Teal); }
        void OnMessage(VivoxMessage m) { if (!m.FromSelf) ChatWall.Post(m.SenderDisplayName, m.MessageText, Palette.Teal, false); }
#endif

        /// <summary>Sends a quick-chat line to everybody online.</summary>
        public async void SendText(string text)
        {
#if ASCEND_VIVOX && !UNITY_WEBGL
            if (!Connected || string.IsNullOrEmpty(VoiceChannel)) return;
            try { await VivoxService.Instance.SendChannelTextMessageAsync(VoiceChannel, text); }
            catch (Exception e) { Debug.LogWarning("[ASCEND] text: " + e.Message); }
#else
            await Task.Yield();
#endif
        }

        public void ToggleMic()
        {
            MicMuted = !MicMuted;
#if ASCEND_VIVOX && !UNITY_WEBGL
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
                {
                    if (MicMuted) VivoxService.Instance.MuteInputDevice(); else VivoxService.Instance.UnmuteInputDevice();
                }
            }
            catch (Exception e) { Debug.LogWarning(e.Message); }
#endif
            GameState.I.Notify(MicMuted ? "MIC MUTED" : "MIC ON", Palette.Teal);
        }

        void Update()
        {
#if ASCEND_VIVOX && !UNITY_WEBGL
            if (Connected && !string.IsNullOrEmpty(VoiceChannel) && Time.time > nextPos && Rig.I != null)
            {
                nextPos = Time.time + 0.2f;
                try { VivoxService.Instance.Set3DPosition(Rig.I.Head.gameObject, VoiceChannel); } catch { }
            }
#endif
#if ASCEND_NGO && ASCEND_MPS
            // rebuild the status line only when something changed (it used to allocate a new string every frame)
            if (Connected && session != null)
            {
                int n = session.PlayerCount * 2 + (string.IsNullOrEmpty(VoiceChannel) ? 0 : 1);
                if (n != shownCount)
                {
                    shownCount = n;
                    StatusText = "ONLINE · " + session.PlayerCount + " PLAYER(S) ON THE FLOOR" + (string.IsNullOrEmpty(VoiceChannel) ? voiceError : "\nVOICE CHAT ON · PROXIMITY");
                }
            }
#endif
        }

        async void OnApplicationQuit()
        {
            try
            {
#if ASCEND_NGO && ASCEND_MPS
                if (session != null) await session.LeaveAsync();
#else
                await Task.Yield();
#endif
            }
            catch { }
        }
    }
}
