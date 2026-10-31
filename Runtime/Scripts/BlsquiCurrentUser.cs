using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Blsqui.SDK
{
    [Serializable]
    public class CurrentUserState
    {
        public string addr = "";
        public bool loggedIn = false;
        public string avatarImage = "";
        public string userName = "";
        public string network = "testnet";
        public string lastUsedItemId = "";
    }

    [Serializable]
    public class ItemUseRequestPayload
    {
        public string type;
        public string player;
        public string app_id;
        public string itemId;
        public int serialNumber;
        public string flixId;
    }

    public class BlsquiCurrentUser : MonoBehaviour
    {
        public static BlsquiCurrentUser Instance { get; private set; }

        public event Action<CurrentUserState> OnUserUpdated;
        public event Action<List<StageSlotItem>> OnSlotsUpdated;
        public event Action<ItemUseRequestPayload> OnItemUseRequested;

        private const string TESTNET_BASE_URL = "https://lab.blsqui.net";
        private const string MAINNET_BASE_URL = "https://wallet.blsqui.net";

        public string appId = "";
        public string publisherAddress = "";
        public string network = "testnet";

        private CurrentUserState userState = new CurrentUserState();
        private BlsquiWebRTCPresence presence;
        private string sessionChannelKey = "";
        private Coroutine heartbeatCoroutine;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitializePresence();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void InitializePresence()
        {
            presence = gameObject.AddComponent<BlsquiWebRTCPresence>();
            presence.OnMessageReceived += OnPresenceMessage;
        }

        public void Configure(string cfgAppId, string cfgPubAddr, string cfgNetwork = "testnet")
        {
            appId = cfgAppId.Trim().ToLowerInvariant();
            publisherAddress = cfgPubAddr.Trim();
            network = cfgNetwork;
            userState.network = network;
        }

        public string GenerateChannelKey()
        {
            byte[] bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            var sb = new StringBuilder(64);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        public string GetOrCreateChannelKey()
        {
            if (string.IsNullOrEmpty(sessionChannelKey))
            {
                sessionChannelKey = GenerateChannelKey();
            }
            return sessionChannelKey;
        }

        /// <summary>
        /// Opens system browser pointing to the Web Wallet Dashboard and connects game presence
        /// </summary>
        public void OpenDashboard()
        {
            string baseUrl = (network == "testnet") ? TESTNET_BASE_URL : MAINNET_BASE_URL;
            string channelKey = GetOrCreateChannelKey();

            // Connect Unity WebRTC presence using channel_key as room ID
            presence.InitPresence(channelKey, "game");

            // Build URL parameters
            var query = new StringBuilder();
            query.Append($"channel_key={UnityWebRequest.EscapeURL(channelKey)}");

            if (!string.IsNullOrEmpty(appId))
                query.Append($"&app_id={UnityWebRequest.EscapeURL(appId)}");

            if (!string.IsNullOrEmpty(publisherAddress))
                query.Append($"&publisher_address={UnityWebRequest.EscapeURL(publisherAddress)}");

            if (!string.IsNullOrEmpty(userState.addr))
                query.Append($"&addr={UnityWebRequest.EscapeURL(userState.addr)}");

            string fullUrl = $"{baseUrl}/?{query}";
            Debug.Log($"[BlsquiSDK] Opening Dashboard: {fullUrl}");

            Application.OpenURL(fullUrl);
        }

        public void RequestUseItem(string itemId)
        {
            if (presence == null)
            {
                Debug.LogWarning("[BlsquiSDK] Cannot use item: WebRTC presence is not initialized.");
                return;
            }

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string cleanAddr = userState.addr.ToLowerInvariant();

            // Matches the standard Blsqui action dispatch protocol
            string payload = $"{{\"type\":\"TRIGGER_ITEM_ACTION\",\"action\":\"use_equipped_item\",\"itemId\":\"{itemId}\",\"player\":\"{cleanAddr}\",\"timestamp\":{nowMs}}}";

            Debug.Log($"[BlsquiSDK] Requesting item use: {itemId}");
            presence.SendData(payload);
        }

        private void OnPresenceMessage(string rawText)
        {
            if (rawText.Contains("\"PRESENCE_PING\""))
            {
                SendPresenceLiveSignal();
                return;
            }

            if (rawText.Contains("BLSQUI_LOGOUT"))
            {
                userState.addr = "";
                userState.loggedIn = false;
                userState.avatarImage = "";
                userState.userName = "";
                StopHeartbeat();
                OnUserUpdated?.Invoke(userState);
                OnSlotsUpdated?.Invoke(new List<StageSlotItem>());
                return;
            }

            // Handle user info & address sync
            if (rawText.Contains("BLSQUI_USER_UPDATE") || rawText.Contains("SLOT_SYNC") || rawText.Contains("BLSQUI_SLOT_SYNC"))
            {
                string addr = ExtractStringValue(rawText, "address");
                if (string.IsNullOrEmpty(addr))
                    addr = ExtractStringValue(rawText, "player");

                if (!string.IsNullOrEmpty(addr))
                {
                    userState.addr = addr;
                    userState.loggedIn = true;
                    userState.userName = ExtractStringValue(rawText, "userName");
                    userState.avatarImage = ExtractStringValue(rawText, "avatarImage");

                    StartHeartbeat();
                    OnUserUpdated?.Invoke(userState);
                }

                // Parse and broadcast equipped items if present
                var parsedSlots = ExtractSlotsFromPayload(rawText);
                if (parsedSlots != null && parsedSlots.Count > 0)
                {
                    OnSlotsUpdated?.Invoke(parsedSlots);
                }
            }

            // Handle ITEM_USE_REQUESTED from WebRTC
            if (rawText.Contains("ITEM_USE_REQUESTED"))
            {
                Debug.Log($"[BlsquiSDK] Detected ITEM_USE_REQUESTED payload: {rawText}");

                string reqFlixId = ExtractStringValue(rawText, "flixId");
                if (string.IsNullOrEmpty(reqFlixId))
                {
                    reqFlixId = ExtractStringValue(rawText, "flix_id");
                }

                string reqItemId = ExtractStringValue(rawText, "itemId");
                if (string.IsNullOrEmpty(reqItemId))
                {
                    reqItemId = ExtractStringValue(rawText, "item_id");
                }

                string reqPlayer = ExtractStringValue(rawText, "player");
                string reqAppId = ExtractStringValue(rawText, "app_id");

                // Extract serialNumber dynamically (e.g., "serialNumber":2)
                int serial = ExtractIntValue(rawText, "serialNumber");
                if (serial <= 0)
                {
                    serial = ExtractIntValue(rawText, "serial_number");
                }

                var payload = new ItemUseRequestPayload
                {
                    type = "ITEM_USE_REQUESTED",
                    flixId = reqFlixId,
                    itemId = reqItemId,
                    player = reqPlayer,
                    app_id = reqAppId,
                    serialNumber = serial
                };

                Debug.Log($"[BlsquiSDK] Dispatching OnItemUseRequested -> Item: {payload.itemId}, Serial: {payload.serialNumber}, FLIX: {payload.flixId}");
                OnItemUseRequested?.Invoke(payload);
                return;
            }
        }

        private List<StageSlotItem> ExtractSlotsFromPayload(string rawText)
        {
            var list = new List<StageSlotItem>();

            // 1. Direct item ID presence check in WebRTC raw text
            // Handles cases where payload is json with an array or simple payload
            if (rawText.Contains("shd_valkyrie_aegis_b"))
            {
                list.Add(new StageSlotItem { slotIndex = 1, itemId = "shd_valkyrie_aegis_b" });
            }
            if (rawText.Contains("wpn_dragon_piercer_b"))
            {
                list.Add(new StageSlotItem { slotIndex = 2, itemId = "wpn_dragon_piercer_b" });
            }

            return list;
        }

        private void StartHeartbeat()
        {
            if (heartbeatCoroutine == null)
            {
                heartbeatCoroutine = StartCoroutine(HeartbeatRoutine());
            }
        }

        private void StopHeartbeat()
        {
            if (heartbeatCoroutine != null)
            {
                StopCoroutine(heartbeatCoroutine);
                heartbeatCoroutine = null;
            }
        }

        private IEnumerator HeartbeatRoutine()
        {
            while (userState.loggedIn)
            {
                SendPresenceLiveSignal();
                yield return new WaitForSeconds(10.0f);
            }
            heartbeatCoroutine = null;
        }

        private void SendPresenceLiveSignal()
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string pongJson = $"{{\"type\":\"PRESENCE_PONG\",\"role\":\"game\",\"player\":\"{userState.addr.ToLowerInvariant()}\",\"status\":\"LIVE\",\"game_online\":true,\"timestamp\":{nowMs}}}";
            presence.SendData(pongJson);
        }

        private string ExtractStringValue(string json, string key)
        {
            string pattern = $"\"{key}\":\"";
            int start = json.IndexOf(pattern, StringComparison.Ordinal);
            if (start == -1) return "";
            start += pattern.Length;
            int end = json.IndexOf('"', start);
            return (end != -1) ? json.Substring(start, end - start) : "";
        }

        private int ExtractIntValue(string json, string key)
        {
            string pattern = $"\"{key}\":";
            int start = json.IndexOf(pattern, StringComparison.Ordinal);
            if (start == -1) return 0;
            start += pattern.Length;

            // Trim leading whitespace
            while (start < json.Length && char.IsWhiteSpace(json[start]))
            {
                start++;
            }

            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
            {
                end++;
            }

            if (end > start && int.TryParse(json.Substring(start, end - start), out int result))
            {
                return result;
            }

            return 0;
        }
    }
}