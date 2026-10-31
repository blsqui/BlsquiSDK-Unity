using System.Text;
using UnityEngine;
using TMPro;
using Blsqui.SDK;

public class ScoutButtonHandler : MonoBehaviour
{
    private const string TARGET_ADDRESS = "0x2f13c512df0b0ad7";
    private const string PUBLISHER_ADDRESS = "0x2f13c512df0b0ad7";
    private const string APP_ID = "valkyrie_vanguard";

    [Header("UI Dialog References")]
    [SerializeField] private GameObject tournamentDialog;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private GameObject cancelButton; // Will be hidden for Scout modal

    public async void ScoutOpponentSlots()
    {
        Debug.Log($"[Scout] Querying equipped slots for: {TARGET_ADDRESS}");

        // 1. Show immediate loading feedback on the dialog
        if (tournamentDialog != null)
        {
            if (cancelButton != null) cancelButton.SetActive(false);
            if (titleText != null) titleText.text = "Scouting Opponent...";
            if (messageText != null) messageText.text = $"Connecting to Flow Access Node...\nTarget: {TARGET_ADDRESS}";
            tournamentDialog.SetActive(true);
        }

        // 2. Fetch equipped slots from Flow Testnet
        var slots = await BlsquiStageSlot.GetStageSlots(new StageSlotOptions
        {
            targetAddress = TARGET_ADDRESS,
            publisherAddress = PUBLISHER_ADDRESS,
            appId = APP_ID,
            isTestnet = true,
            verbose = true
        });

        // 3. Format and display the scouted results
        if (tournamentDialog != null && messageText != null)
        {
            if (titleText != null) titleText.text = "Opponent Loadout";

            int maxSlotsToShow = 3;
            var sb = new StringBuilder();
            sb.AppendLine($"Target: {TARGET_ADDRESS}");
            sb.AppendLine("Equipped Item Slots:\n");

            bool hasAnyEquipped = false;
            int count = 0;

            foreach (var slot in slots)
            {
                if (count >= maxSlotsToShow)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(slot.itemId))
                {
                    hasAnyEquipped = true;
                    sb.AppendLine($"• Slot {slot.slotIndex}: {slot.itemId}");
                }
                else
                {
                    sb.AppendLine($"• Slot {slot.slotIndex}: (Empty)");
                }

                count++;
            }

            if (!hasAnyEquipped)
            {
                sb.AppendLine("No items equipped in stage slots.");
            }

            messageText.text = sb.ToString();
        }
    }

    // Hooked to ConfirmButton.onClick to dismiss the modal
    public void OnCloseScoutDialog()
    {
        if (tournamentDialog != null)
        {
            tournamentDialog.SetActive(false);
        }
    }
}