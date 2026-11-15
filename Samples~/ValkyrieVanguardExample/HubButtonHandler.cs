using System.Collections.Generic;
using UnityEngine;
using Blsqui.SDK;

public class HubButtonHandler : MonoBehaviour
{
    [Header("App Config")]
    [SerializeField] private string appId = "valkyrie_vanguard";
    [SerializeField] private string publisherAddress = "0x2f13c512df0b0ad7";
    [SerializeField] private string network = "testnet";

    private void Start()
    {
        // Ensure BlsquiCurrentUser manager exists in scene
        if (BlsquiCurrentUser.Instance == null)
        {
            var userGo = new GameObject("BlsquiCurrentUser");
            var user = userGo.AddComponent<BlsquiCurrentUser>();
            user.Configure(appId, publisherAddress, network);
        }
        else
        {
            BlsquiCurrentUser.Instance.Configure(appId, publisherAddress, network);
        }

        // Subscribe to real-time user profile & equipped slot updates from the dashboard
        BlsquiCurrentUser.Instance.OnUserUpdated += HandleUserUpdated;
        BlsquiCurrentUser.Instance.OnSlotsUpdated += HandleSlotsUpdated;
    }

    private void OnDestroy()
    {
        if (BlsquiCurrentUser.Instance != null)
        {
            BlsquiCurrentUser.Instance.OnUserUpdated -= HandleUserUpdated;
            BlsquiCurrentUser.Instance.OnSlotsUpdated -= HandleSlotsUpdated;
        }
    }

    public void OnHubClicked()
    {
        Debug.Log("[Hub] Opening Blsqui Dashboard...");
        BlsquiCurrentUser.Instance.OpenDashboard();
    }

    private void HandleUserUpdated(CurrentUserState user)
    {
        if (user.loggedIn)
        {
            Debug.Log($"[Hub] Dashboard Connected | Player: {user.addr} | Name: {user.userName}");
        }
        else
        {
            Debug.Log("[Hub] Player logged out from Dashboard.");
        }
    }

    private void HandleSlotsUpdated(List<StageSlotItem> slots)
    {
        Debug.Log($"[Hub] Synced {slots.Count} equipped slot(s) from Dashboard:");
        foreach (var slot in slots)
        {
            Debug.Log($"  - Slot {slot.slotIndex}: {slot.itemId}");
        }
    }
}
