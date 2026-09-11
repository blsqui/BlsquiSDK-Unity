using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Blsqui.SDK
{
    [Serializable]
    public class TxOptions
    {
        public bool isTestnet = true;
        public string flixId = BlsquiSDK.DEFAULT_FLIX_ID;
        public Dictionary<string, string> args = new Dictionary<string, string>();
        public bool verbose = false;
    }

    [Serializable]
    public class TxResult
    {
        public string status;       // "SEALED" | "FAILED" | "EXPIRED" | "TIMEOUT" | "CANCELED"
        public string txId;
        public string nonce;
        public string payer;
        public string to;
        public string amount;
        public string token;
        public string error;
        public string errorMessage;
    }

    [Serializable]
    internal class ApiStatusResponse
    {
        public string status;
        public string txId;
        public string error;
        public string errorMessage;
        public string payer;
        public string to;
        public string amount;
        public string token;
    }

    public static class BlsquiSDK
    {
        // --- Gateway & Polling Endpoints ---
        public const string MAINNET_GATEWAY_URL = "https://wallet.blsqui.net/transaction";
        public const string TESTNET_GATEWAY_URL = "https://lab.blsqui.net/transaction";
        public const string MAINNET_POLL_API = "https://wallet.blsqui.net/api/status";
        public const string TESTNET_POLL_API = "https://lab.blsqui.net/api/status";

        // Default FLIX Template (10 FLOW entry fee)
        public const string DEFAULT_FLIX_ID = "7d9d4b154547d7f6ec95e8b95741ed84663592d8c0016dbc4b28b6f9bf435ba5";
        public const float POLL_INTERVAL_SECONDS = 1.5f;
        public const float TIMEOUT_SECONDS = 300.0f;

        // SDK Internal State
        private static bool _isCanceled = false;

        /// <summary>
        /// Cancels any ongoing transaction polling loop.
        /// </summary>
        public static void CancelTransaction()
        {
            _isCanceled = true;
        }

        /// <summary>
        /// Primary Entry Point: Launches browser and polls backend server for on-chain completion.
        /// </summary>
        public static async Task<TxResult> RequestTransaction(TxOptions options = null)
        {
            if (options == null)
            {
                options = new TxOptions();
            }

            _isCanceled = false;

            bool isTestnet = options.isTestnet;
            string flixId = string.IsNullOrEmpty(options.flixId) ? DEFAULT_FLIX_ID : options.flixId;
            Dictionary<string, string> args = options.args ?? new Dictionary<string, string>();
            bool verbose = options.verbose;

            string baseUrl = isTestnet ? TESTNET_GATEWAY_URL : MAINNET_GATEWAY_URL;
            string currentNonce = GenerateClientNonce();
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Build Query Parameters
            List<string> queryParts = new List<string>
            {
                $"flix={UnityWebRequest.EscapeURL(flixId)}",
                $"issued_time={currentTime}",
                $"nonce={UnityWebRequest.EscapeURL(currentNonce)}"
            };

            foreach (var kvp in args)
            {
                string val = kvp.Value?.Trim() ?? "";
                if (!string.IsNullOrEmpty(val))
                {
                    queryParts.Add($"{UnityWebRequest.EscapeURL(kvp.Key)}={UnityWebRequest.EscapeURL(val)}");
                }
            }

            string fullUrl = $"{baseUrl}?{string.Join("&", queryParts)}";

            if (verbose)
            {
                Debug.Log($"<color=#3B82F6>[BlsquiSDK]</color> Mode: Unity Native Browser [{(isTestnet ? "TESTNET" : "MAINNET")}]");
                Debug.Log($"<color=#3B82F6>[BlsquiSDK]</color> Wallet Gateway URL: {fullUrl}");
                Debug.Log($"<color=#3B82F6>[BlsquiSDK]</color> Nonce: {currentNonce}");
            }

            // Launch system browser
            try
            {
                Application.OpenURL(fullUrl);
            }
            catch (Exception ex)
            {
                Debug.LogError($"<color=#EF4444>[BlsquiSDK]</color> Failed to launch system browser: {ex.Message}");
                return new TxResult
                {
                    status = "FAILED",
                    nonce = currentNonce,
                    error = "Failed to launch system browser: " + ex.Message
                };
            }

            // Poll backend server for transaction status
            return await PollTransactionStatusAsync(isTestnet, currentNonce, verbose);
        }

        private static async Task<TxResult> PollTransactionStatusAsync(bool isTestnet, string nonce, bool verbose)
        {
            string pollBase = isTestnet ? TESTNET_POLL_API : MAINNET_POLL_API;
            string pollUrl = $"{pollBase}?nonce={UnityWebRequest.EscapeURL(nonce)}";
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < TIMEOUT_SECONDS)
            {
                if (_isCanceled)
                {
                    if (verbose)
                    {
                        Debug.Log("<color=#F59E0B>[BlsquiSDK]</color> Transaction polling canceled by caller.");
                    }
                    return new TxResult
                    {
                        status = "CANCELED",
                        nonce = nonce,
                        error = "Transaction canceled by user."
                    };
                }

                using (UnityWebRequest webRequest = UnityWebRequest.Get(pollUrl))
                {
                    webRequest.SetRequestHeader("Accept", "application/json");

                    var asyncOp = webRequest.SendWebRequest();
                    while (!asyncOp.isDone)
                    {
                        if (_isCanceled) break;
                        await Task.Delay(100);
                    }

                    if (_isCanceled)
                    {
                        return new TxResult
                        {
                            status = "CANCELED",
                            nonce = nonce,
                            error = "Transaction canceled by user."
                        };
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success && webRequest.responseCode == 200)
                    {
                        string jsonText = webRequest.downloadHandler.text;
                        ApiStatusResponse responseData = JsonUtility.FromJson<ApiStatusResponse>(jsonText);
                        string status = !string.IsNullOrEmpty(responseData?.status) ? responseData.status.ToUpper() : "PENDING";

                        if (verbose)
                        {
                            Debug.Log($"<color=#10B981>[BlsquiSDK Poll]</color> HTTP 200 | Status: '{status}' | JSON: {jsonText}");
                        }

                        if (status == "SEALED")
                        {
                            // Check for on-chain script/execution error
                            if (!string.IsNullOrEmpty(responseData.errorMessage))
                            {
                                return new TxResult
                                {
                                    status = "FAILED",
                                    txId = responseData.txId,
                                    nonce = nonce,
                                    payer = responseData.payer,
                                    error = "On-chain execution failed",
                                    errorMessage = responseData.errorMessage
                                };
                            }

                            if (verbose)
                            {
                                Debug.Log($"<color=#10B981>[BlsquiSDK]</color> 🎉 Transaction Sealed! TX ID: {responseData.txId}");
                            }

                            return new TxResult
                            {
                                status = "SEALED",
                                txId = responseData.txId,
                                nonce = nonce,
                                errorMessage = null,
                                payer = responseData.payer,
                                to = responseData.to,
                                amount = responseData.amount,
                                token = responseData.token
                            };
                        }
                        else if (status == "EXPIRED")
                        {
                            if (verbose)
                            {
                                Debug.LogWarning("<color=#F59E0B>[BlsquiSDK]</color> Transaction EXPIRED on-chain.");
                            }
                            return new TxResult
                            {
                                status = "EXPIRED",
                                nonce = nonce,
                                error = "Transaction expired on-chain"
                            };
                        }
                    }
                    else if (verbose)
                    {
                        Debug.LogWarning($"<color=#F59E0B>[BlsquiSDK Poll]</color> Non-200 or Network Error: {webRequest.responseCode} {webRequest.error}");
                    }
                }

                // Wait for POLL_INTERVAL_SECONDS before next request
                await Task.Delay((int)(POLL_INTERVAL_SECONDS * 1000));
            }

            if (verbose)
            {
                Debug.LogError($"<color=#EF4444>[BlsquiSDK]</color> Transaction poll timed out after {TIMEOUT_SECONDS} seconds.");
            }

            return new TxResult
            {
                status = "TIMEOUT",
                nonce = nonce,
                error = $"Transaction poll timed out after {TIMEOUT_SECONDS} seconds."
            };
        }

        /// <summary>
        /// Generates a 256-bit cryptographically secure hex nonce (lowercase 64-char SHA-256).
        /// Matches the GDScript hex_encode().sha256_text() implementation.
        /// </summary>
        public static string GenerateClientNonce()
        {
            byte[] randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            // Match Godot: convert 32 random bytes to 64 hex characters, then SHA-256 hash that string
            StringBuilder hexBuilder = new StringBuilder(64);
            foreach (byte b in randomBytes)
            {
                hexBuilder.Append(b.ToString("x2"));
            }
            string hexString = hexBuilder.ToString();

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(hexString));
                StringBuilder sb = new StringBuilder(64);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}