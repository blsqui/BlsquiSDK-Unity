using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Blsqui.SDK
{
    [Serializable]
    public class TxResult
    {
        public string status;
        public string txId;
        public string error;
        public string nonce;
        public string errorMessage;
        public string payer;
        public string to;
        public string amount;
        public string token;
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
        public const string MAINNET_URL = "https://signer.blsqui.net";
        public const string TESTNET_URL = "https://testnet-signer.blsqui.net";
        public const float POLL_INTERVAL_SECONDS = 1.5f;
        public const float TIMEOUT_SECONDS = 300.0f;

        /// <summary>
        /// Primary Entry Point: Opens system browser and polls backend server for completion.
        /// </summary>
        /// <param name="amount">Transaction amount (e.g., 300.0f)</param>
        /// <param name="destination">Target wallet address (e.g., 0xa090f900023d6d34)</param>
        /// <param name="isTestnet">True for Flow Testnet, False for Mainnet</param>
        /// <param name="verbose">True to enable detailed console debugging logs</param>
        public static async Task<TxResult> RequestTransaction(
            float amount, 
            string destination, 
            bool isTestnet = true, 
            bool verbose = false,
            Action<string> onStatusUpdate = null)
        {
            string baseUrl = isTestnet ? TESTNET_URL : MAINNET_URL;
            string currentNonce = GenerateClientNonce();
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // 1. Build payment signer URL
            string fullUrl = $"{baseUrl}?nonce={currentNonce}&to={destination}&price={amount}&issued_time={currentTime}";

            if (verbose)
            {
                Debug.Log($"<color=#3B82F6>[BlsquiSDK]</color> Launching system browser ({(isTestnet ? "TESTNET" : "MAINNET")}) -> {fullUrl}");
                Debug.Log($"<color=#3B82F6>[BlsquiSDK]</color> Nonce: {currentNonce}");
            }

            // 2. Open user browser
            try
            {
                Application.OpenURL(fullUrl);
            }
            catch (Exception ex)
            {
                Debug.LogError($"<color=#EF4444>[BlsquiSDK]</color> Failed to open browser: {ex.Message}");
                return new TxResult { status = "FAILED", error = "Failed to open system browser: " + ex.Message };
            }

            // 3. Poll backend server for transaction status
            if (verbose)
            {
                Debug.Log($"<color=#3B82F6>[BlsquiSDK]</color> Starting poll loop for nonce: {currentNonce}...");
            }

            return await PollTransactionStatusAsync(baseUrl, currentNonce, verbose);
        }

        private static async Task<TxResult> PollTransactionStatusAsync(string baseUrl, string nonce, bool verbose)
        {
            string pollUrl = $"{baseUrl}/api/status?nonce={nonce}";
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < TIMEOUT_SECONDS)
            {
                using (UnityWebRequest webRequest = UnityWebRequest.Get(pollUrl))
                {
                    // Await UnityWebRequest asynchronously
                    var asyncOp = webRequest.SendWebRequest();
                    while (!asyncOp.isDone)
                    {
                        await Task.Delay(100);
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        long responseCode = webRequest.responseCode;
                        string jsonText = webRequest.downloadHandler.text;

                        if (responseCode == 200)
                        {
                            ApiStatusResponse responseData = JsonUtility.FromJson<ApiStatusResponse>(jsonText);
                            string status = !string.IsNullOrEmpty(responseData?.status) ? responseData.status : "PENDING";

                            if (verbose)
                            {
                                Debug.Log($"<color=#10B981>[BlsquiSDK Poll]</color> HTTP 200 | Status: '{status}' | JSON: {jsonText}");
                            }

                            // If status is finalized on-chain
                            string upperStatus = status.ToUpper();
                            if (upperStatus == "SEALED" || upperStatus == "EXECUTED" || upperStatus == "FINALIZED" || upperStatus == "SUCCESS")
                            {
                                if (verbose)
                                {
                                    Debug.Log($"<color=#10B981>[BlsquiSDK]</color> 🎉 Transaction Sealed! TX ID: {responseData.txId}");
                                }

                                return new TxResult
                                {
                                    status = status,
                                    txId = responseData.txId,
                                    nonce = nonce,
                                    errorMessage = responseData.errorMessage,
                                    payer = responseData.payer,
                                    to = responseData.to,
                                    amount = responseData.amount,
                                    token = responseData.token
                                };
                            }
                            else if (upperStatus == "EXPIRED")
                            {
                                if (verbose)
                                {
                                    Debug.LogWarning($"<color=#F59E0B>[BlsquiSDK]</color> Transaction EXPIRED on-chain.");
                                }
                                return new TxResult { status = "EXPIRED", error = "Transaction expired on-chain" };
                            }
                        }
                        else if (verbose)
                        {
                            Debug.LogWarning($"<color=#F59E0B>[BlsquiSDK Poll]</color> Non-200 HTTP response: {responseCode}");
                        }
                    }
                    else if (verbose)
                    {
                        Debug.LogWarning($"<color=#F59E0B>[BlsquiSDK Poll Request Error]</color> {webRequest.error}");
                    }
                }

                // Wait for POLL_INTERVAL_SECONDS before next tick
                await Task.Delay((int)(POLL_INTERVAL_SECONDS * 1000));
            }

            if (verbose)
            {
                Debug.LogError($"<color=#EF4444>[BlsquiSDK]</color> Transaction poll timed out after {TIMEOUT_SECONDS} seconds.");
            }

            return new TxResult { status = "TIMEOUT", error = $"Transaction poll timed out after {TIMEOUT_SECONDS} seconds" };
        }

        private static string GenerateClientNonce()
        {
            // Use 32 random bytes (256 bits) of entropy for SHA-256
            byte[] randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            string base64 = Convert.ToBase64String(randomBytes).ToLowerInvariant();

            // Compute SHA-256 hash (64 lowercase hex characters)
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(base64));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}