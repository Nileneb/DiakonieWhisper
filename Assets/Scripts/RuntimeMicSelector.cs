// RuntimeMicSelector.cs
// Runtime UI-Dropdown für Mikrofon-Auswahl durch Endnutzer (z.B. Pflegekräfte).
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using BergischeDiakonie.Speech;

public class RuntimeMicSelector : MonoBehaviour
{
    [SerializeField] private Dropdown micDropdown;
    [SerializeField] private AudioCaptureService captureService;

    void Start()
    {
        if (!captureService)
            captureService = FindFirstObjectByType<AudioCaptureService>();

        RefreshMicList();
    }

    /// <summary>
    /// Füllt das Dropdown mit allen verfügbaren Mikrofonen und
    /// synchronisiert die Auswahl mit dem AudioCaptureService.
    /// </summary>
    public void RefreshMicList()
    {
        micDropdown.onValueChanged.RemoveListener(OnMicSelected);
        micDropdown.ClearOptions();

        string[] devices = captureService != null
            ? captureService.GetAvailableMicrophones()
            : Microphone.devices;

        var mics = new List<string>(devices);

        if (mics.Count == 0)
        {
            mics.Add("-- Kein Mikrofon gefunden --");
            micDropdown.interactable = false;
            micDropdown.AddOptions(mics);
            return;
        }

        micDropdown.interactable = true;
        micDropdown.AddOptions(mics);

        // Aktuell gewähltes Mic im Dropdown vorselektieren
        if (captureService != null && !string.IsNullOrEmpty(captureService.micDeviceName))
        {
            int idx = mics.IndexOf(captureService.micDeviceName);
            if (idx >= 0) micDropdown.value = idx;
        }

        micDropdown.onValueChanged.AddListener(OnMicSelected);

        // Erstes Mic automatisch auswählen, falls noch keines gesetzt
        if (captureService != null && string.IsNullOrEmpty(captureService.micDeviceName))
        {
            OnMicSelected(0);
        }
    }

    private void OnMicSelected(int index)
    {
        if (captureService != null)
        {
            captureService.SelectMicrophone(index);
        }
    }
}
