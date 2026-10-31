using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;
using Blsqui.SDK;

public class ActionButtonsController : MonoBehaviour
{
    private const string WEAPON_ITEM_ID = "wpn_dragon_piercer_b";
    private const string SHIELD_ITEM_ID = "shd_valkyrie_aegis_b";

    [Header("DEF Button (Button2)")]
    [SerializeField] private SpriteRenderer defRenderer;
    [SerializeField] private Collider2D defCollider;

    [Header("ATK Button (Button5)")]
    [SerializeField] private SpriteRenderer atkRenderer;
    [SerializeField] private Collider2D atkCollider;

    [Header("UI Dialog References")]
    [SerializeField] private GameObject tournamentDialog;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private GameObject cancelButton;

    private const float DISABLED_ALPHA = 0.2f;
    private const float ENABLED_ALPHA = 1.0f;
    private bool isSubscribed = false;

    private void Awake()
    {
        // Force initial disabled visual
        SetButtonVisualState(defRenderer, defCollider, false);
        SetButtonVisualState(atkRenderer, atkCollider, false);
    }

    private void Start()
    {
        TrySubscribe();
    }

    private void Update()
    {
        // Fallback: If BlsquiCurrentUser spawned after Start(), catch it here
        if (!isSubscribed)
        {
            TrySubscribe();
        }
    }

    private void TrySubscribe()
    {
        if (BlsquiCurrentUser.Instance != null && !isSubscribed)
        {
            BlsquiCurrentUser.Instance.OnSlotsUpdated += UpdateEquipmentButtons;
            BlsquiCurrentUser.Instance.OnItemUseRequested += HandleItemUseRequested;
            isSubscribed = true;
            Debug.Log(" ActionButtonsController successfully subscribed to BlsquiCurrentUser.OnSlotsUpdated!");
        }
    }

    private void OnDestroy()
    {
        if (BlsquiCurrentUser.Instance != null && isSubscribed)
        {
            BlsquiCurrentUser.Instance.OnSlotsUpdated -= UpdateEquipmentButtons;
            isSubscribed = false;
        }
    }

    public void UpdateEquipmentButtons(List<StageSlotItem> equippedSlots)
    {
        int slotCount = equippedSlots?.Count ?? 0;
        Debug.Log($"[ActionButtons] Received slots update! Count: {slotCount}");

        bool hasShield = false;
        bool hasWeapon = false;

        if (equippedSlots != null)
        {
            foreach (var slot in equippedSlots)
            {
                if (slot == null) continue;

                Debug.Log($"[ActionButtons] Slot {slot.slotIndex}: {slot.itemId}");

                if (string.IsNullOrEmpty(slot.itemId)) continue;

                if (slot.itemId == SHIELD_ITEM_ID)
                {
                    hasShield = true;
                }
                else if (slot.itemId == WEAPON_ITEM_ID)
                {
                    hasWeapon = true;
                }
            }
        }

        Debug.Log($"[ActionButtons] Applying state -> Shield: {hasShield}, Weapon: {hasWeapon}");
        SetButtonVisualState(defRenderer, defCollider, hasShield);
        SetButtonVisualState(atkRenderer, atkCollider, hasWeapon);
    }

    private void SetButtonVisualState(SpriteRenderer sr, Collider2D col, bool isEnabled)
    {
        if (sr != null)
        {
            Color c = sr.color;
            c.a = isEnabled ? ENABLED_ALPHA : DISABLED_ALPHA;
            sr.color = c;
        }

        if (col != null)
        {
            col.enabled = isEnabled;
        }
    }

    public void OnDefButtonClicked()
    {
        Debug.Log("[ActionButtons] DEF Button clicked -> Requesting shd_valkyrie_aegis_b");
        if (BlsquiCurrentUser.Instance != null)
        {
            BlsquiCurrentUser.Instance.RequestUseItem(SHIELD_ITEM_ID);
        }
    }

    public void OnAtkButtonClicked()
    {
        Debug.Log("[ActionButtons] ATK Button clicked -> Requesting wpn_dragon_piercer_b");
        if (BlsquiCurrentUser.Instance != null)
        {
            BlsquiCurrentUser.Instance.RequestUseItem(WEAPON_ITEM_ID);
        }
    }

    private async void HandleItemUseRequested(ItemUseRequestPayload payload)
    {
        Debug.Log($"[ActionButtons] Received execution request for {payload.itemId} #{payload.serialNumber} (FLIX: {payload.flixId})");

        // Show immediate progress dialog
        if (tournamentDialog != null)
        {
            if (cancelButton != null) cancelButton.SetActive(false);
            if (titleText != null) titleText.text = "Executing Action...";
            if (messageText != null) messageText.text = $"Authorizing transaction on Flow...\nItem: {payload.itemId} (Serial #{payload.serialNumber})";
            tournamentDialog.SetActive(true);
        }

        // Prepare transaction arguments matching FLIX template parameter requirements
        var txArgs = new Dictionary<string, string>
        {
            { "itemId", payload.itemId },
            { "serialNumber", payload.serialNumber.ToString() }
        };

        var options = new TxOptions
        {
            isTestnet = true,
            verbose = true,
            flixId = payload.flixId,
            args = txArgs
        };

        // 3. Request signed transaction session via Blsqui SDK gateway
        TxResult result = await BlsquiSDK.RequestTransaction(options);

        // 4. Update the dialog with final state
        if (result.status == "SEALED")
        {
            Debug.Log($"🎉 Item action sealed! TX ID: {result.txId}");
            if (titleText != null) titleText.text = "Action Complete!";
            if (messageText != null) messageText.text = $"[GAME] ITEM USED SUCCESSFULLY !!!\n{payload.itemId} (Serial #{payload.serialNumber})\n\nTX: {result.txId}";
        }
        else
        {
            string err = !string.IsNullOrEmpty(result.errorMessage) ? result.errorMessage : result.error;
            Debug.LogWarning($"Action failed: {result.status} | {err}");
            if (titleText != null) titleText.text = "Action Cancelled";
            if (messageText != null) messageText.text = $"Status: {result.status}\n{err}";
        }
    }

}