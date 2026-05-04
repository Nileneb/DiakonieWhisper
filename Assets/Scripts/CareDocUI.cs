using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_STANDALONE
using SFB;
#endif

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
        public TMP_InputField inputTranscript;
        public Slider progressBar;

        [Header("Streaming Display")]
        [Tooltip("Scroll rect containing the transcript InputField (optional)")]
        public ScrollRect scrollRect;

        [Header("Audio Upload")]
        public Button btnUpload;
        [Tooltip("Android fallback: panel with a path InputField + load button")]
        public GameObject panelFilePath;
        public TMP_InputField inputFilePath;

        [Header("Webhook Export")]
        public TMP_InputField inputWebhookUrl;
        public TMP_Text txtCurrentWebhook;
        public Button btnExport;

        [Header("Local Documentation")]
        [Tooltip("Optional: LocalDocumentationService for local LLM rewriting. Leave null to disable.")]
        public LocalDocumentationService localDocService;

        readonly Queue<System.Action> _q = new Queue<System.Action>();

        // Accumulated LLM streaming output for the current session
        string _llmOutput = "";
        bool _llmStarted;

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
                    if (manager.audioCapture != null && manager.audioCapture.IsRecording)
                    {
                        btnRecord.interactable = false;
                        if (btnStop) btnStop.interactable = true;
                        if (btnExport) btnExport.interactable = false;
                        if (inputTranscript)
                        {
                            inputTranscript.SetTextWithoutNotify("");
                            inputTranscript.interactable = false;
                        }
                        _llmOutput = "";
                        _llmStarted = false;
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

            // ── Audio Upload ────────────────────────────────────
            SetupUploadUI();

            // ── Webhook UI ──────────────────────────────────────
            SetupWebhookUI();

            // ── Manager events ──────────────────────────────────
            manager.OnStatusMessage += m => _q.Enqueue(() => { if (txtStatus) txtStatus.text = m; });

            manager.OnTranscriptUpdate += t => _q.Enqueue(() =>
            {
                if (!inputTranscript) return;
                inputTranscript.SetTextWithoutNotify(t);
                if (scrollRect) scrollRect.verticalNormalizedPosition = 0f;
            });

            manager.OnDiarizationProgress += p => _q.Enqueue(() => { if (progressBar) progressBar.value = p; });

            manager.OnSpeakerLine += (s, t) => _q.Enqueue(() =>
            {
                if (!inputTranscript) return;
                inputTranscript.SetTextWithoutNotify(inputTranscript.text + $"\n{s}: {t}");
                if (scrollRect) scrollRect.verticalNormalizedPosition = 0f;
            });

            manager.OnProtocolSaved += p => _q.Enqueue(() =>
            {
                if (txtStatus) txtStatus.text = $"Gespeichert: {System.IO.Path.GetFileName(p)}";
                if (btnExport) btnExport.interactable = true;
                if (inputTranscript) inputTranscript.interactable = true;
                _llmOutput = "";
                _llmStarted = false;
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

            // ── Local LLM events ─────────────────────────────────
            if (localDocService != null)
            {
                localDocService.OnStatusMessage += m =>
                    _q.Enqueue(() => { if (txtStatus) txtStatus.text = m; });

                localDocService.OnPartialResult += token => _q.Enqueue(() =>
                {
                    if (!inputTranscript) return;
                    if (!_llmStarted)
                    {
                        _llmStarted = true;
                        _llmOutput = "── Strukturierte Dokumentation ──\n";
                    }
                    _llmOutput += token;
                    inputTranscript.SetTextWithoutNotify(_llmOutput);
                    if (scrollRect) scrollRect.verticalNormalizedPosition = 0f;
                });

                localDocService.OnProcessingComplete += path => _q.Enqueue(() =>
                {
                    if (txtStatus)
                        txtStatus.text = $"Dokumentation gespeichert: {System.IO.Path.GetFileName(path)}";
                });

                localDocService.OnProcessingError += err => _q.Enqueue(() =>
                {
                    if (txtStatus) txtStatus.text = $"LLM-Fehler: {err}";
                });
            }
        }

        // ── Upload setup ────────────────────────────────────────

        void SetupUploadUI()
        {
            if (!btnUpload) return;

            btnUpload.onClick.AddListener(() =>
            {
#if UNITY_STANDALONE
                StandaloneFileBrowser.OpenFilePanelAsync(
                    "Audio-Datei wählen", "",
                    new[] { new ExtensionFilter("Audio", "wav") },
                    false,
                    paths =>
                    {
                        if (paths != null && paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
                            manager.ProcessFile(paths[0]);
                    });
#else
                // Android: zeige Fallback-Panel mit Pfad-Eingabe
                if (panelFilePath) panelFilePath.SetActive(!panelFilePath.activeSelf);
#endif
            });

            // Android fallback: "Laden"-Button im panelFilePath (optional)
            if (inputFilePath)
            {
                // Ein zweiter Button im panelFilePath kann ProcessFile aufrufen.
                // Verdrahtung im Inspector: Button.onClick → CareDocUI.LoadFromInputPath()
            }
        }

        public void LoadFromInputPath()
        {
            if (!inputFilePath) return;
            string path = inputFilePath.text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                if (txtStatus) txtStatus.text = "Bitte Dateipfad eingeben.";
                return;
            }
            if (panelFilePath) panelFilePath.SetActive(false);
            manager.ProcessFile(path);
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

            // Export button — sends the currently displayed (possibly edited) text
            if (btnExport)
            {
                btnExport.interactable = false;
                btnExport.onClick.AddListener(() =>
                {
                    string text = inputTranscript ? inputTranscript.text : manager.LastProtocol;
                    manager.ExportText(text);
                });
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
