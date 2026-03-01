// AudioCaptureServiceEditor.cs
// MUSS im Ordner "Editor" liegen!
using UnityEngine;
using UnityEditor;
using BergischeDiakonie.Speech;

[CustomEditor(typeof(AudioCaptureService))]
public class AudioCaptureServiceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        AudioCaptureService service = (AudioCaptureService)target;
        string[] mics = Microphone.devices;

        // ── Microphone Dropdown ─────────────────────────────
        EditorGUILayout.LabelField("Microphone", EditorStyles.boldLabel);

        if (mics.Length == 0)
        {
            EditorGUILayout.HelpBox("Keine Mikrofone gefunden!", MessageType.Warning);
            service.micDeviceName = "";
            service.selectedMicIndex = 0;
        }
        else
        {
            // Aktuellen Index finden
            int currentIndex = System.Array.IndexOf(mics, service.micDeviceName);
            if (currentIndex < 0) currentIndex = 0;

            int newIndex = EditorGUILayout.Popup("Mic Device", currentIndex, mics);

            if (newIndex != currentIndex || string.IsNullOrEmpty(service.micDeviceName))
            {
                service.selectedMicIndex = newIndex;
                service.micDeviceName = mics[newIndex];
                EditorUtility.SetDirty(service);
            }
        }

        EditorGUILayout.Space();

        // ── Settings ────────────────────────────────────────
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        service.targetSampleRate = EditorGUILayout.IntField("Target Sample Rate", service.targetSampleRate);
        service.chunkSeconds = EditorGUILayout.IntSlider("Chunk Seconds", service.chunkSeconds, 1, 10);
        service.clipLengthSeconds = EditorGUILayout.IntField("Clip Length Seconds", service.clipLengthSeconds);

        if (GUI.changed)
        {
            EditorUtility.SetDirty(service);
        }
    }
}
