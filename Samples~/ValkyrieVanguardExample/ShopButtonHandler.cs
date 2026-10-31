using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Blsqui.SDK;

public class ShopButtonHandler : MonoBehaviour
{
    private const string WEAPON_SHOP_FLIX_ID = "351d9d6f23ebe5ddbe9bf5b4f937a352c5caaa6a4439b5e464f3b16c1d671472";

    [Header("UI Dialog References")]
    [SerializeField] private GameObject tournamentDialog;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private GameObject cancelButton;

    public async void OnShopButtonClicked()
    {
        Debug.Log("[Shop] Opening purchase transaction...");

        // Show transaction feedback in the dialog
        if (tournamentDialog != null)
        {
            if (cancelButton != null) cancelButton.SetActive(false);
            if (titleText != null) titleText.text = "Processing Shop Item...";
            if (messageText != null) messageText.text = "Authorizing transaction on Flow...\nPlease complete payment in the browser.";
            tournamentDialog.SetActive(true);
        }

        // Configure transaction options matching your web SDK call
        var options = new TxOptions
{
    isTestnet = true,
    verbose = true,
    flixId = "351d9d6f23ebe5ddbe9bf5b4f937a352c5caaa6a4439b5e464f3b16c1d671472",
    args = new Dictionary<string, string>()
};

TxResult result = await BlsquiSDK.RequestTransaction(options);

        // Update dialog state upon completion
        if (result.status == "SEALED")
        {
            Debug.Log($"Shop purchase succeeded! TX ID: {result.txId}");
            if (titleText != null) titleText.text = "Purchase Successful!";
            if (messageText != null) messageText.text = $"Item purchase sealed !!!\n\nTX ID:\n{result.txId}";
        }
        else
        {
            string err = !string.IsNullOrEmpty(result.errorMessage) ? result.errorMessage : result.error;
            Debug.LogWarning($"[Shop] Purchase incomplete. Status: {result.status} | Error: {err}");
            if (titleText != null) titleText.text = "Transaction Cancelled";
            if (messageText != null) messageText.text = $"Status: {result.status}\n{err}";
        }
    }
}