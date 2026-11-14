# Blsqui SDK for Unity Engine

The official Unity Engine SDK for **Blsqui** — enabling seamless Flow blockchain payments, Passkey wallet authentication, real-time WebRTC dashboard sync, and microtransactions in Unity games.

## Features
- **Passkey & WebAuthn Ready:** Launches the system browser directly to the Blsqui Gateway for one-touch biometric signing.
- **Real-Time WebRTC Presence & Session Sync:** Live bidirectional DataChannel communication between the Unity client and the player's web dashboard for inventory sync and item activation.
- **Dynamic Slot & Item Actions:** Built-in protocol support for `TRIGGER_ITEM_ACTION`, `SLOT_SYNC`, and auto-dispatched `ITEM_USE_REQUESTED` events with publisher- and app-scoped Cadence arguments.
- **On-Chain Scouter Queries:** Query any player's equipped NFT item slots directly from Flow Access Nodes via `BlsquiStageSlot.GetStageSlots()` without wallet popups or gas.
- **Edge Polling Engine:** Asynchronous, non-blocking HTTP polling against Blsqui edge nodes until on-chain finality without freezing Unity's main thread.
- **FLIX Native:** Built-in support for Flow Interaction Templates with arbitrary Cadence script parameters.

## 📦 Installation & Setup

1. In the Unity Editor, open **Window -> Package Manager**.
2. Click the **+** icon in the upper-left corner and select **Add package from git URL...**.
3. Enter the repository URL:
   ```text
   [https://github.com/blsqui/BlsquiSDK-Unity.git](https://github.com/blsqui/BlsquiSDK-Unity.git)
   ```
4. Click Add. (Unity Package Manager will automatically resolve the com.unity.webrtc dependency declared in package.json.)


## Quick Start Example
1. Direct Payment (`BlsquiSDK.RequestTransaction`)
Call `BlsquiSDK.RequestTransaction()` from any button or event handler:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Blsqui.SDK;

public class PaymentButton : MonoBehaviour
{
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Text buttonText;

    private void Start()
    {
        purchaseButton.onClick.AddListener(OnPurchaseClicked);
    }

    private async void OnPurchaseClicked()
    {
        purchaseButton.interactable = false;
        buttonText.text = "Waiting for wallet...";

        var options = new TxOptions
        {
            isTestnet = true,
            verbose = true,
            flixId = "7d9d4b154547d7f6ec95e8b95741ed84663592d8c0016dbc4b28b6f9bf435ba5",
            args = new Dictionary<string, string>
            {
                { "to", "0xa090f900023d6d34" } // Merchant / game deposit address
            }
        };

        TxResult result = await BlsquiSDK.RequestTransaction(options);

        if (result.status == "SEALED")
        {
            Debug.Log($"🎉 Payment succeeded! TX ID: {result.txId}");
            Debug.Log($"Payer: {result.payer}");
            Debug.Log($"Amount Paid: {result.amount} {result.token}");
            // Unlock in-game item or grant access here
        }
        else
        {
            string errorDetail = !string.IsNullOrEmpty(result.errorMessage) ? result.errorMessage : result.error;
            Debug.LogWarning($"Payment unfinished. Status: {result.status} | Error: {errorDetail}");
        }

        purchaseButton.interactable = true;
        buttonText.text = "Purchase Item";
    }
}
```

2. Real-Time WebRTC Dashboard & Item Usage (`BlsquiCurrentUser`)

```csharp
using System.Collections.Generic;
using UnityEngine;
using Blsqui.SDK;

public class BlsquiSessionExample : MonoBehaviour
{
    private void Start()
    {
        if (BlsquiCurrentUser.Instance == null)
        {
            var go = new GameObject("BlsquiCurrentUser");
            go.AddComponent<BlsquiCurrentUser>();
        }

        BlsquiCurrentUser.Instance.Configure("valkyrie_vanguard", "0x2f13c512df0b0ad7", "testnet");
        BlsquiCurrentUser.Instance.OnItemUseRequested += HandleItemUseRequested;
    }

    public void OpenWebDashboard()
    {
        BlsquiCurrentUser.Instance.OpenDashboard();
    }

    public void TriggerInGameItem(string itemId)
    {
        BlsquiCurrentUser.Instance.RequestUseItem(itemId);
    }

    private async void HandleItemUseRequested(ItemUseRequestPayload payload)
    {
        var options = new TxOptions
        {
            isTestnet = true,
            flixId = payload.flixId,
            args = new Dictionary<string, string>
            {
                { "publisherAddress", payload.publisherAddress },
                { "appId", payload.appId },
                { "itemId", payload.itemId },
                { "serialNumber", payload.serialNumber.ToString() }
            }
        };

        TxResult result = await BlsquiSDK.RequestTransaction(options);
        if (result.status == "SEALED")
        {
            Debug.Log($"🎉 Item consumed on-chain! TX ID: {result.txId}");
        }
    }
}
```

## Configuration Reference
### `TxOptions` Class
Pass this configuration object into `BlsquiSDK.RequestTransaction(options)`:
| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `isTestnet` | `bool` | `true` | When `true`, connects to Flow Testnet (`lab.blsqui.net`). When `false`, connects to Flow Mainnet (`wallet.blsqui.net`). |
| `flixId` | `string` | Default Fee Template | The FLIX (Flow Interaction Template) identifier. Defaults to the standard 10 FLOW entry template. |
| `args` | `Dictionary<string, string>` | `new()` | Key-value arguments passed into the Cadence interaction. (e.g. `{"to": "0x..."}`). |
| `verbose` | `bool` | `false` | When `true`, prints detailed polling requests and HTTP status logs to the Unity Console. |

---

### `TxResult` Object
Returned asynchronously by `await BlsquiSDK.RequestTransaction(...)`:

```csharp
public class TxResult
{
    public string status;        // "SEALED" | "FAILED" | "EXPIRED" | "TIMEOUT" | "CANCELED"
    public string txId;          // Flow blockchain transaction ID (SEALED only)
    public string nonce;         // Cryptographic tracking nonce
    public string payer;         // Flow account address that signed the transaction
    public string to;            // Recipient account address (extracted from TokensDeposited)
    public string amount;        // Executed payment amount (Cadence UFix64 string)
    public string token;         // Token identifier (e.g., "FlowToken")
    public string txFee;         // Gas/execution fee deducted on-chain (Cadence UFix64 string, or null)
    public string error;         // Client-side error or failure description
    public string errorMessage;  // Detailed Cadence runtime error message from the node, or null
}
```

### Transaction Status Definitions

 - `SEALED`: The transaction was executed and finalized on the Flow blockchain.

 - `FAILED`: The transaction failed during on-chain execution (see errorMessage).

 - `EXPIRED`: The transaction expired on-chain before execution.

 - `TIMEOUT`: Polling exceeded the maximum duration (default: 300 seconds).

 - `CANCELED`: Polling was stopped manually via BlsquiSDK.CancelTransaction().


## Requirements
- Unity **2021.3 LTS** or higher
- com.unity.webrtc (3.0.0-pre.7 or compatible)
- Active internet connection

## License
MIT License - see [LICENSE](LICENSE) for details.
