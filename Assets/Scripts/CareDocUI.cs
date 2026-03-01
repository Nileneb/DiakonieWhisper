using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// UI controller for streaming speech documentation.
    /// Shows live transcript during recording, speaker labels after stop.
    /// Webhook export sends the protocol to a Langdock workflow.
    /// </summary>
    public class CareDocUI : MonoBehaviour
    {
        public CareDocumentationManager manager;

        [Header("Recording")]
        public Button btnRecord;
        public Button btnStop;
        public TMP_Text txtStatus;
        public TMP_Text txtTranscript;
        public Slider progressBar;

        [Header("Streaming Display")]
        [Tooltip("Scroll rect containing the transcript text (optional)")]
        public ScrollRect scrollRect;

        [Header("Webhook Export")]
        public TMP_InputField inputWebhookUrl;
        public TMP_Text txtCurrentWebhook;
        public Button btnExport;

        readonly Queue<System.Action> _q = new Queue<System.Action>();

        void Start()
        {
            // ── Recording buttons ───────────────────────────────
            // Disabled until init completes
            if (btnRecord) btnRecord.interactable = false;
            if (btnRecord) btnRecord.onClick.AddListener(() =>
            {
                try
                {
                    manager.StartRecording();
                    // Nur umschalten wenn StartRecording nicht geworfen hat
                    if (manager.audioCapture != null && manager.audioCapture.IsRecording)
                    {
                        btnRecord.interactable = false;
                        if (btnStop) btnStop.interactable = true;
                        if (btnExport) btnExport.interactable = false;
                        if (txtTranscript) txtTranscript.text = "";
                    }
                    else
                    {
                        if (txtStatus) txtStatus.text = "Mikrofon konnte nicht gestartet werden.";
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CareDocUI] Rec failed: {e.Message}");
                    if (txtStatus) txtStatus.text = $"Fehler: {e.Message}";
                    btnRecord.interactable = true;
                    if (btnStop) btnStop.interactable = false;
                }
            });
            if (btnStop)
            {
                btnStop.interactable = false;
                btnStop.onClick.AddListener(() =>
                {
                    manager.StopRecording();
                    btnRecord.interactable = true;
                    btnStop.interactable = false;
                });
            }

            // ── Webhook UI ──────────────────────────────────────
            SetupWebhookUI();

            // ── Manager events ──────────────────────────────────
            manager.OnStatusMessage += m => _q.Enqueue(() => { if (txtStatus) txtStatus.text = m; });

            manager.OnTranscriptUpdate += t => _q.Enqueue(() =>
            {
                if (!txtTranscript) return;
                txtTranscript.text = t;
                if (scrollRect) scrollRect.verticalNormalizedPosition = 0f;
            });

            manager.OnDiarizationProgress += p => _q.Enqueue(() => { if (progressBar) progressBar.value = p; });

            manager.OnSpeakerLine += (s, t) => _q.Enqueue(() =>
            {
                if (txtTranscript) txtTranscript.text += $"\n<b>{s}:</b> {t}";
            });

            manager.OnProtocolSaved += p => _q.Enqueue(() =>
            {
                if (txtStatus) txtStatus.text = $"Gespeichert: {System.IO.Path.GetFileName(p)}";
                if (btnExport) btnExport.interactable = true;
            });

            // Enable Record button once init is done
            manager.OnInitialized += () => _q.Enqueue(() =>
            {
                if (btnRecord) btnRecord.interactable = true;
            });

            // Webhook completion feedback
            manager.Webhook.OnStatusMessage += m =>
                _q.Enqueue(() => { if (txtStatus) txtStatus.text = m; });
            manager.Webhook.OnComplete += (ok, msg) =>
                _q.Enqueue(() => { if (txtStatus) txtStatus.text = msg; });
        }

        // ── Webhook setup ───────────────────────────────────────

        void SetupWebhookUI()
        {
            var wh = manager.Webhook;

            // Show current URL
            RefreshWebhookDisplay();

            // URL input: apply on end edit
            if (inputWebhookUrl)
            {
                inputWebhookUrl.text = wh.Url;
                inputWebhookUrl.onEndEdit.AddListener(url =>
                {
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        wh.Url = url.Trim();
                        RefreshWebhookDisplay();
                    }
                });
            }

            // Export button
            if (btnExport)
            {
                btnExport.interactable = false; // enabled after first protocol
                btnExport.onClick.AddListener(() => manager.ExportToWebhook());
            }
        }

        void RefreshWebhookDisplay()
        {
            if (txtCurrentWebhook)
            {
                string url = manager.Webhook.Url;
                // Truncate for display
                if (url.Length > 60)
                    txtCurrentWebhook.text = url.Substring(0, 57) + "...";
                else
                    txtCurrentWebhook.text = url;
            }
        }

        void Update()
        {
            while (_q.Count > 0) _q.Dequeue()?.Invoke();
        }
    }
}
