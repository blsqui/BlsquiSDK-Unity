using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Blsqui.SDK
{
    [Serializable]
    public class StageSlotOptions
    {
        public string targetAddress;
        public string publisherAddress;
        public string appId = "";
        public bool isTestnet = true;
        public bool verbose = false;
    }

    [Serializable]
    public class StageSlotItem
    {
        public int slotIndex;
        public string itemId;
    }

    public static class BlsquiStageSlot
    {
        private const string TESTNET_ACCESS_NODE = "https://rest-testnet.onflow.org/v1/scripts";
        private const string MAINNET_ACCESS_NODE = "https://rest-mainnet.onflow.org/v1/scripts";

        private const string SESAME_CONTRACT_TESTNET = "0x975b04756864e9ea";
        private const string SESAME_CONTRACT_MAINNET = "0xb576a3926d239682";

        private const string CADENCE_SCRIPT_TEMPLATE = @"
import SesamePass from {0}

access(all) fun main(publisher: Address, user: Address, appId: String): [String]? {{
    return SesamePass.getUserItemSlots(publisher: publisher, user: user, appId: appId)
}}";

        [Serializable]
        private class CadenceScriptPayload
        {
            public string script;
            public List<string> arguments;
        }

        public static async Task<List<StageSlotItem>> GetStageSlots(StageSlotOptions options)
        {
            var results = new List<StageSlotItem>();

            if (options == null || string.IsNullOrWhiteSpace(options.targetAddress) || string.IsNullOrWhiteSpace(options.publisherAddress))
            {
                Debug.LogError("[BlsquiSDK] targetAddress and publisherAddress are required.");
                return results;
            }

            string cleanTarget = options.targetAddress.Trim();
            if (!cleanTarget.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                cleanTarget = "0x" + cleanTarget;

            string cleanPublisher = options.publisherAddress.Trim();
            if (!cleanPublisher.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                cleanPublisher = "0x" + cleanPublisher;

            string contractAddr = options.isTestnet ? SESAME_CONTRACT_TESTNET : SESAME_CONTRACT_MAINNET;
            string cadenceScript = string.Format(CADENCE_SCRIPT_TEMPLATE, contractAddr);
            string scriptB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(cadenceScript));

            string argPubJson = $"{{\"type\":\"Address\",\"value\":\"{cleanPublisher}\"}}";
            string argUserJson = $"{{\"type\":\"Address\",\"value\":\"{cleanTarget}\"}}";
            string argAppIdJson = $"{{\"type\":\"String\",\"value\":\"{options.appId ?? ""}\"}}";

            var payloadObj = new CadenceScriptPayload
            {
                script = scriptB64,
                arguments = new List<string>
                {
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(argPubJson)),
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(argUserJson)),
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(argAppIdJson))
                }
            };

            string requestJson = JsonUtility.ToJson(payloadObj);
            string endpoint = options.isTestnet ? TESTNET_ACCESS_NODE : MAINNET_ACCESS_NODE;

            using (var req = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (options.verbose)
                        Debug.LogWarning($"[BlsquiSDK] Failed to query target slots ({req.responseCode}): {req.downloadHandler.text}");
                    return results;
                }

                string rawBody = req.downloadHandler.text;

                // Decode base64 payload if returned as a quoted string
                string decodedJson = rawBody.Trim();
                if (decodedJson.StartsWith("\"") && decodedJson.EndsWith("\""))
                {
                    string base64Str = decodedJson.Substring(1, decodedJson.Length - 2);
                    try
                    {
                        byte[] decodedBytes = Convert.FromBase64String(base64Str);
                        decodedJson = Encoding.UTF8.GetString(decodedBytes);
                    }
                    catch
                    {
                        // Not base64, leave as-is
                    }
                }

                if (options.verbose)
                {
                    Debug.Log($"[BlsquiSDK] Decoded Cadence Object: {decodedJson}");
                }

                // Extract all string values inside the Cadence array: {"type":"String","value":"..."}
                var matches = Regex.Matches(decodedJson, @"\{""(?:value""\s*:\s*""(?<val>[^""]*)""\s*,\s*""type""\s*:\s*""String""|type""\s*:\s*""String""\s*,\s*""value""\s*:\s*""(?<val>[^""]*)"")\}");

                int index = 1;
                foreach (Match m in matches)
                {
                    string val = m.Groups["val"].Value;
                    results.Add(new StageSlotItem
                    {
                        slotIndex = index++,
                        itemId = val
                    });
                }

                if (options.verbose)
                {
                    string summary = "[";
                    for (int i = 0; i < results.Count; i++)
                    {
                        summary += $"{{slotIndex: {results[i].slotIndex}, itemId: \"{results[i].itemId}\"}}";
                        if (i < results.Count - 1) summary += ", ";
                    }
                    summary += "]";
                    Debug.Log($"[BlsquiSDK] getStageSlots resolved: {summary}");
                }

                return results;
            }
        }
    }
}
