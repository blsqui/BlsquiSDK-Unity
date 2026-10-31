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
    }

    public void OnHubClicked()
    {
        Debug.Log("[Hub] Opening Blsqui Dashboard...");
        BlsquiCurrentUser.Instance.OpenDashboard();
    }
}