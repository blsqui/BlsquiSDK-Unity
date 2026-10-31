using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Unity.WebRTC;

namespace Blsqui.SDK
{
    [Serializable]
    public class PresenceState
    {
        public bool connected = false;
        public bool gameOnline = false;
        public bool canUseItem = false;
        public long lastPingMs = 0;
    }

    [Serializable]
    internal class SdpOfferData
    {
        public string type;
        public string sdp;
    }

    [Serializable]
    internal class SignalingPayload
    {
        public string role;
        public string player;
        public SdpOfferData sdp;
    }

    public class BlsquiWebRTCPresence : MonoBehaviour
    {
        public event Action<string> OnMessageReceived;
        public event Action<PresenceState> OnStateChanged;

        private const string SIGNALING_API = "https://api.blsqui-signer.net/api/rtc/session";

        private PresenceState _state = new PresenceState();
        private RTCPeerConnection pc = null;
        private RTCDataChannel dc = null;
        private string activeAddress = "";
        private bool isConnecting = false;

        private void Start()
        {
            // Required for Unity.WebRTC: pumps internal events each frame
            StartCoroutine(WebRTC.Update());
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        public PresenceState GetState() => _state;

        private void UpdateState(Action<PresenceState> modifier)
        {
            modifier(_state);
            OnStateChanged?.Invoke(_state);
        }

        public void InitPresence(string playerAddress, string role = "game")
        {
            string cleanAddr = playerAddress.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(cleanAddr))
            {
                Debug.LogWarning("[WebRTC] Missing player address. Aborting handshake.");
                return;
            }

            if (isConnecting || (pc != null && activeAddress == cleanAddr && _state.connected))
            {
                return;
            }

            isConnecting = true;
            activeAddress = cleanAddr;
            Cleanup();

            var config = new RTCConfiguration
            {
                iceServers = new[]
                {
                    new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } },
                    new RTCIceServer { urls = new[] { "stun:stun.cloudflare.com:3478" } }
                }
            };

            pc = new RTCPeerConnection(ref config);

            // Create DataChannel
            var dcInit = new RTCDataChannelInit { negotiated = false };
            dc = pc.CreateDataChannel("blsqui-presence", dcInit);
            BindDataChannelEvents(dc);

            StartCoroutine(CreateAndSendOffer(role, cleanAddr));
        }

        private void BindDataChannelEvents(RTCDataChannel channel)
        {
            channel.OnOpen = () =>
            {
                Debug.Log("✅ [Unity WebRTC DataChannel OPEN]");
                UpdateState(s => s.connected = true);
            };

            channel.OnClose = () =>
            {
                Debug.Log("🛑 [Unity WebRTC DataChannel CLOSED]");
                UpdateState(s =>
                {
                    s.connected = false;
                    s.gameOnline = false;
                    s.canUseItem = false;
                });
            };

            channel.OnMessage = bytes =>
            {
                string rawText = Encoding.UTF8.GetString(bytes);
                Debug.Log($"📩 [Unity WebRTC RECV]: {rawText}");
                HandleIncomingRaw(rawText);
            };
        }

        private IEnumerator CreateAndSendOffer(string role, string cleanAddr)
        {
            var offerOp = pc.CreateOffer();
            yield return offerOp;

            if (offerOp.IsError)
            {
                Debug.LogError($"[WebRTC] Failed to create offer: {offerOp.Error.message}");
                isConnecting = false;
                yield break;
            }

            var desc = offerOp.Desc;
            var localDescOp = pc.SetLocalDescription(ref desc);
            yield return localDescOp;

            if (localDescOp.IsError)
            {
                Debug.LogError($"[WebRTC] Failed to set local description: {localDescOp.Error.message}");
                isConnecting = false;
                yield break;
            }

            // Allow brief gathering for candidates to bake into the SDP
            yield return new WaitForSeconds(0.3f);

            var payload = new SignalingPayload
            {
                role = role,
                player = cleanAddr,
                sdp = new SdpOfferData
                {
                    type = "offer",
                    sdp = pc.LocalDescription.sdp
                }
            };

            string body = JsonUtility.ToJson(payload);
            using (var req = new UnityWebRequest(SIGNALING_API, UnityWebRequest.kHttpVerbPOST))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");

                Debug.Log($"[WebRTC] Posting offer to Pion Hub for room: {cleanAddr}");
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WebRTC] Handshake failed [HTTP {req.responseCode}]: {req.downloadHandler.text}");
                    UpdateState(s => s.connected = false);
                    isConnecting = false;
                    yield break;
                }

                string resText = req.downloadHandler.text;
                string answerSdp = ExtractAnswerSdp(resText);

                if (!string.IsNullOrEmpty(answerSdp))
                {
                    var remoteDesc = new RTCSessionDescription
                    {
                        type = RTCSdpType.Answer,
                        sdp = answerSdp
                    };
                    var remoteDescOp = pc.SetRemoteDescription(ref remoteDesc);
                    yield return remoteDescOp;

                    if (!remoteDescOp.IsError)
                    {
                        Debug.Log("[WebRTC] Remote description (answer) set successfully.");
                    }
                    else
                    {
                        Debug.LogError($"[WebRTC] Failed to set remote description: {remoteDescOp.Error.message}");
                    }
                }
                else
                {
                    Debug.LogError($"[WebRTC] Answer missing SDP body: {resText}");
                }
            }

            isConnecting = false;
        }

        private string ExtractAnswerSdp(string json)
        {
            // Parses {"sdp":"..."} or nested {"sdp":{"sdp":"...","type":"answer"}}
            int sdpKey = json.IndexOf("\"sdp\"", StringComparison.Ordinal);
            if (sdpKey == -1) return null;

            int secondSdp = json.IndexOf("\"sdp\"", sdpKey + 5, StringComparison.Ordinal);
            int targetIdx = secondSdp != -1 ? secondSdp : sdpKey;

            int colon = json.IndexOf(':', targetIdx);
            int startQuote = json.IndexOf('"', colon);
            if (startQuote == -1) return null;

            var sb = new StringBuilder();
            for (int i = startQuote + 1; i < json.Length; i++)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    if (next == 'n') { sb.Append('\n'); i++; continue; }
                    if (next == 'r') { sb.Append('\r'); i++; continue; }
                    if (next == '"') { sb.Append('"'); i++; continue; }
                    if (next == '\\') { sb.Append('\\'); i++; continue; }
                }
                if (json[i] == '"') break;
                sb.Append(json[i]);
            }

            return sb.ToString();
        }

        public void SendData(string payload)
        {
            if (dc != null && dc.ReadyState == RTCDataChannelState.Open)
            {
                Debug.Log($"📤 [Unity WebRTC SEND]: {payload}");
                dc.Send(payload);
            }
            else
            {
                Debug.LogWarning($"[WebRTC] Send dropped: DataChannel state is {(dc != null ? dc.ReadyState.ToString() : "null")}");
            }
        }

        private void HandleIncomingRaw(string rawText)
        {
            if (rawText.Contains("PRESENCE_UPDATE"))
            {
                bool isGameConnected = rawText.Contains("GAME_CONNECTED");
                bool canUse = rawText.Contains("\"can_use_item\":true") || rawText.Contains("\"can_use_item\": true");
                UpdateState(s =>
                {
                    s.gameOnline = isGameConnected;
                    s.canUseItem = canUse;
                });
            }

            OnMessageReceived?.Invoke(rawText);
        }

        public void Cleanup()
        {
            if (dc != null)
            {
                dc.Close();
                dc.Dispose();
                dc = null;
            }
            if (pc != null)
            {
                pc.Close();
                pc.Dispose();
                pc = null;
            }
            activeAddress = "";
            isConnecting = false;
            UpdateState(s =>
            {
                s.connected = false;
                s.gameOnline = false;
                s.canUseItem = false;
            });
        }
    }
}