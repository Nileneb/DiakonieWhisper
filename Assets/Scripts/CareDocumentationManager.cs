using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// Main orchestrator for Bergische Diakonie speech documentation.
    /// Streaming mode: VAD + Whisper transcribe live during recording,
    /// results are shown immediately. Diarization runs after stop.
    /// </summary>
    public class CareDocumentationManager : MonoBehaviour
    {
        [Header("References")]
        public AudioCaptureService audioCapture;
        public ModelDownloader modelDownloader;
        public LocalDocumentationService localDocService;

        [Header("Model File Names (flat, in ModelDownloader.ModelDirectory)")]
        public string vadModel = "silero_vad.onnx";
        public string whisperEncoder = "small-encoder.onnx";
        public string whisperDecoder = "small-decoder.onnx";
        public string whisperTokens = "small-tokens.txt";
        public string segmentationModel = "sherpa-onnx-pyannote-segmentation-3-0.onnx";
        public string embeddingModel = "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx";

        [Header("VAD")]
        [Range(0.1f, 0.9f)] public float vadThreshold = 0.35f;

        [Header("Diarization")]
        [Range(1, 2)] public int minSpeakers = 1;
        [Range(2, 8)] public int maxSpeakers = 5;
        [Range(0.1f, 0.9f)] public float clusteringThreshold = 0.5f;

        [Header("ASR")]
        public string language = "de";
        public int numThreads = 4;

        [Header("Output")]
        public string outputFolder = "Protokolle";

        // ── Events ──────────────────────────────────────────────
        /// <summary>Fired with the full accumulated transcript so far (live during recording, final after stop).</summary>
        public event Action<string> OnTranscriptUpdate;
        /// <summary>Fired for each speaker-labeled line after diarization.</summary>
        public event Action<string, string> OnSpeakerLine;
        public event Action<float> OnDiarizationProgress;
        public event Action<string> OnStatusMessage;
        public event Action<string> OnProtocolSaved;
        /// <summary>Fired once when all models are loaded and the system is ready to record.</summary>
        public event Action OnInitialized;

        /// <summary>The last completed protocol text (for webhook export).</summary>
        public string LastProtocol { get; private set; }

        public WebhookService Webhook { get; private set; } = new WebhookService();

        VadService _vad;
        AsrService _asr;
        DiarizationService _diarizer;
        bool _ready;

        // ── Streaming state ─────────────────────────────────────
        readonly List<float> _buffer = new List<float>();
        readonly List<AsrService.TranscriptResult> _liveTranscripts = new List<AsrService.TranscriptResult>();
        int _segmentCount;
        float _recordingStartTime;

        string MP(string fileName) => ModelDownloader.GetModelPath(fileName);

        void Start()
        {
            if (!audioCapture) audioCapture = GetComponent<AudioCaptureService>();

            StartCoroutine(WaitForModelsAndInit());
        }

        /// <summary>
        /// Waits one frame (so CareDocUI can subscribe), requests mic permission,
        /// then polls until ModelDownloader has all models ready before initializing.
        /// </summary>
        IEnumerator WaitForModelsAndInit()
        {
            // Wait one frame so all Start() methods (including CareDocUI) have run
            // and event listeners are subscribed before we fire status messages.
            yield return null;

            // 1. Request mic permission at runtime (Android 6+)
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
            {
                OnStatusMessage?.Invoke("Mikrofon-Berechtigung wird angefragt...");
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Microphone);

                // Warten bis der Benutzer reagiert hat (max 30 s)
                float timeout = 30f;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                           UnityEngine.Android.Permission.Microphone) && timeout > 0f)
                {
                    timeout -= 0.25f;
                    yield return new WaitForSeconds(0.25f);
                }

                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                        UnityEngine.Android.Permission.Microphone))
                {
                    OnStatusMessage?.Invoke("Mikrofon-Berechtigung verweigert. Aufnahme nicht möglich.");
                    Debug.LogError("[CareDoc] Microphone permission denied.");
                }
            }
#endif

            // 2. Wait until all models are downloaded
            if (modelDownloader != null)
            {
                OnStatusMessage?.Invoke("Warte auf Modell-Download...");
                while (!modelDownloader.AllModelsPresent())
                    yield return new WaitForSeconds(0.5f);
            }

            // 3. Initialize
            Initialize();

            if (_ready)
                OnInitialized?.Invoke();
        }

        /// <summary>Send an arbitrary text string to the webhook (e.g. user-edited transcript).</summary>
        public void ExportText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                OnStatusMessage?.Invoke("Kein Text zum Senden vorhanden.");
                return;
            }
            StartCoroutine(Webhook.Send(text));
        }

        /// <summary>Send the newest protocol file to the configured Langdock webhook.</summary>
        public void ExportToWebhook()
        {
            string text = LoadNewestProtocol();
            if (string.IsNullOrWhiteSpace(text))
            {
                OnStatusMessage?.Invoke("Kein Protokoll zum Senden vorhanden.");
                return;
            }
            StartCoroutine(Webhook.Send(text));
        }

        /// <summary>Read the most recently saved .txt protocol from disk.</summary>
        string LoadNewestProtocol()
        {
            string dir = Path.Combine(Application.persistentDataPath, outputFolder);
            if (!Directory.Exists(dir)) return LastProtocol;

            string newest = null;
            DateTime newestTime = DateTime.MinValue;
            foreach (var f in Directory.GetFiles(dir, "Protokoll_*.txt"))
            {
                var info = new FileInfo(f);
                if (info.LastWriteTimeUtc > newestTime)
                {
                    newestTime = info.LastWriteTimeUtc;
                    newest = f;
                }
            }

            if (newest != null)
            {
                string content = File.ReadAllText(newest);
                Debug.Log($"[CareDoc] Webhook export from: {Path.GetFileName(newest)} ({content.Length} chars)");
                return content;
            }

            // Fallback to in-memory protocol
            return LastProtocol;
        }

        // ── Init ────────────────────────────────────────────────

        public void Initialize()
        {
            if (_ready) return;
            try
            {
                // Diagnose: Alle erwarteten Modellpfade loggen
                Debug.Log($"[CareDoc] ModelDirectory: {ModelDownloader.ModelDirectory}");
                Debug.Log($"[CareDoc] vadModel path: {MP(vadModel)} exists={File.Exists(MP(vadModel))}");
                Debug.Log($"[CareDoc] encoder path: {MP(whisperEncoder)} exists={File.Exists(MP(whisperEncoder))}");
                Debug.Log($"[CareDoc] decoder path: {MP(whisperDecoder)} exists={File.Exists(MP(whisperDecoder))}");
                Debug.Log($"[CareDoc] tokens path: {MP(whisperTokens)} exists={File.Exists(MP(whisperTokens))}");
                Debug.Log($"[CareDoc] segmentation path: {MP(segmentationModel)} exists={File.Exists(MP(segmentationModel))}");
                Debug.Log($"[CareDoc] embedding path: {MP(embeddingModel)} exists={File.Exists(MP(embeddingModel))}");

                // Verzeichnis-Inhalt loggen
                string dir = ModelDownloader.ModelDirectory;
                if (Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                    Debug.Log($"[CareDoc] Dateien in {dir}: {files.Length}");
                    foreach (var f in files)
                        Debug.Log($"[CareDoc]   -> {f} ({new FileInfo(f).Length} bytes)");
                }
                else
                {
                    Debug.LogError($"[CareDoc] ModelDirectory existiert NICHT: {dir}");
                }

                OnStatusMessage?.Invoke("VAD wird geladen...");
                _vad = new VadService(MP(vadModel), threshold: vadThreshold);

                OnStatusMessage?.Invoke("Whisper ASR wird geladen...");
                _asr = new AsrService(MP(whisperEncoder), MP(whisperDecoder),
                    MP(whisperTokens), language, numThreads: numThreads);

                OnStatusMessage?.Invoke("Speaker Diarization wird geladen...");
                _diarizer = new DiarizationService(
                    MP(segmentationModel), MP(embeddingModel),
                    minSpeakers, maxSpeakers, clusteringThreshold, numThreads);


                _ready = true;
                OnStatusMessage?.Invoke("Bereit.");
            }
            catch (Exception e)
            {
                OnStatusMessage?.Invoke($"Fehler: {e.Message}");
                Debug.LogError($"[CareDoc] Init: {e}");
            }
        }

        // ── Recording (streaming) ───────────────────────────────

        public void StartRecording()
        {
            if (!_ready) Initialize();
            if (!_ready)
            {
                OnStatusMessage?.Invoke("Fehler: System nicht initialisiert. Bitte Modellpfade pruefen.");
                Debug.LogError("[CareDoc] Cannot start recording — initialization failed.");
                return;
            }
            _buffer.Clear();
            _liveTranscripts.Clear();
            _segmentCount = 0;
            _recordingStartTime = Time.realtimeSinceStartup;
            audioCapture.OnAudioChunkReady += OnChunk;
            audioCapture.StartCapture();
            OnStatusMessage?.Invoke("Aufnahme laeuft...");
        }

        public void StopRecording()
        {
            audioCapture.StopCapture();
            audioCapture.OnAudioChunkReady -= OnChunk;

            // Flush any remaining speech from VAD
            if (_vad != null && _asr != null)
            {
                _vad.Flush();
                DrainVad();
            }

            float dur = _buffer.Count / 16000f;
            int segs = _liveTranscripts.Count;
            OnStatusMessage?.Invoke($"Aufnahme beendet ({dur:F1}s, {segs} Segmente). Sprechererkennung...");

            // Diarization only — ASR already done live
            FinalizeAsync(_buffer.ToArray()).Forget();
        }

        /// <summary>
        /// Called for each audio chunk during recording.
        /// Feeds VAD, transcribes detected speech immediately, updates the live transcript.
        /// </summary>
        void OnChunk(float[] chunk)
        {
            if (_vad == null || _asr == null) return;
            _buffer.AddRange(chunk);

            // Feed to VAD
            _vad.Process(chunk); // segments are queued internally
            DrainVad();
        }

        /// <summary>Drain all completed speech segments from VAD and transcribe them.</summary>
        void DrainVad()
        {
            var segments = _vad.DrainSegments();
            foreach (var sc in segments)
            {
                _segmentCount++;
                var result = _asr.Transcribe(sc.Samples, sc.StartTimeSec);
                if (!string.IsNullOrEmpty(result.Text))
                {
                    _liveTranscripts.Add(result);
                    Debug.Log($"[Streaming] Seg#{_segmentCount} [{sc.StartTimeSec:F1}s-{sc.EndTimeSec:F1}s]: {result.Text}");

                    // Build & fire full accumulated transcript
                    OnTranscriptUpdate?.Invoke(BuildLiveTranscript());
                }

                float elapsed = Time.realtimeSinceStartup - _recordingStartTime;
                OnStatusMessage?.Invoke($"Aufnahme: {elapsed:F0}s | {_liveTranscripts.Count} Segmente erkannt");
            }
        }

        /// <summary>Build the full live transcript from all segments so far.</summary>
        string BuildLiveTranscript()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _liveTranscripts.Count; i++)
            {
                var t = _liveTranscripts[i];
                if (i > 0) sb.Append(" ");
                sb.Append(t.Text);
            }
            return sb.ToString();
        }

        // ── Post-recording (diarization only) ───────────────────

        async UniTaskVoid FinalizeAsync(float[] samples)
        {
            try
            {
                if (_liveTranscripts.Count == 0)
                {
                    if (samples.Length > 0)
                    {
                        OnStatusMessage?.Invoke("Kein VAD-Segment erkannt — Direkttranskription...");
                        var fallback = await UniTask.RunOnThreadPool(
                            () => _asr.Transcribe(samples, 0f));
                        if (!string.IsNullOrEmpty(fallback.Text))
                            _liveTranscripts.Add(fallback);
                    }
                    if (_liveTranscripts.Count == 0)
                    {
                        OnStatusMessage?.Invoke("Keine Sprache erkannt.");
                        return;
                    }
                }

                List<DiarizationService.SpeakerSegment> speakers = null;

                OnDiarizationProgress?.Invoke(0f);
                await UniTask.RunOnThreadPool(() => { speakers = _diarizer.Process(samples); });
                OnDiarizationProgress?.Invoke(1f);

                // Merge with already-transcribed segments
                var merged = TranscriptMergeService.Merge(_liveTranscripts, speakers);
                foreach (var l in merged)
                    OnSpeakerLine?.Invoke(l.SpeakerName, l.Text);

                string protocol = TranscriptMergeService.FormatAsProtocol(merged);
                LastProtocol = protocol;
                OnTranscriptUpdate?.Invoke(protocol);

                // Save
                string dir = Path.Combine(Application.persistentDataPath, outputFolder);
                Directory.CreateDirectory(dir);
                string fn = $"Protokoll_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string path = Path.Combine(dir, fn);
                File.WriteAllText(path, protocol);
                File.WriteAllText(Path.ChangeExtension(path, ".rttm"),
                    TranscriptMergeService.FormatAsRTTM(merged));

                OnProtocolSaved?.Invoke(path);
                OnStatusMessage?.Invoke($"Gespeichert: {fn}");
            }
            catch (Exception e)
            {
                OnStatusMessage?.Invoke($"Fehler: {e.Message}");
                Debug.LogError($"[CareDoc] Finalize: {e}");
            }
        }

        // ── Batch file processing ───────────────────────────────

        /// <summary>Batch-process a WAV file (full ASR + diarization).</summary>
        public void ProcessFile(string wavPath)
        {
            if (!_ready) Initialize();
            OnStatusMessage?.Invoke($"Lade: {Path.GetFileName(wavPath)}");
            BatchProcessAsync(audioCapture.LoadWavFile(wavPath)).Forget();
        }

        async UniTaskVoid BatchProcessAsync(float[] samples)
        {
            try
            {
                float dur = samples.Length / 16000f;
                OnStatusMessage?.Invoke($"Verarbeite {dur:F1}s Audio...");

                List<AsrService.TranscriptResult> transcripts = null;
                List<DiarizationService.SpeakerSegment> speakers = null;

                // Step 1: Diarization — provides both speaker labels and reliable segmentation.
                // VAD (Silero) is not used for batch because it is designed for real-time streaming
                // (512-sample chunks) and produces almost no segments on large arrays.
                OnStatusMessage?.Invoke("Sprechererkennung...");
                await UniTask.RunOnThreadPool(() => { speakers = _diarizer.Process(samples); });

                if (speakers.Count == 0)
                {
                    OnStatusMessage?.Invoke("Keine Sprecher erkannt.");
                    return;
                }

                // Step 2: ASR on each diarization segment (sub-chunk at 28s for Whisper's window)
                OnStatusMessage?.Invoke($"Transkribiere {speakers.Count} Segmente...");
                await UniTask.RunOnThreadPool(() =>
                {
                    transcripts = new List<AsrService.TranscriptResult>();
                    const int maxChunkSamples = 16000 * 28;

                    // Diagnostic: log first 3 segments
                    if (speakers.Count > 0)
                        Debug.Log($"[BatchProcess] Seg[0]: {speakers[0].StartSec:F2}s-{speakers[0].EndSec:F2}s " +
                                  $"({speakers[0].SpeakerName}), samples.Length={samples.Length}");

                    foreach (var seg in speakers)
                    {
                        int startIdx = (int)(seg.StartSec * 16000);
                        int endIdx   = (int)(seg.EndSec   * 16000);
                        startIdx = Math.Max(0, Math.Min(startIdx, samples.Length - 1));
                        endIdx   = Math.Max(0, Math.Min(endIdx,   samples.Length));
                        if (endIdx <= startIdx) continue;

                        for (int offset = startIdx; offset < endIdx; offset += maxChunkSamples)
                        {
                            int len = Math.Min(maxChunkSamples, endIdx - offset);
                            var chunk = new float[len];
                            Array.Copy(samples, offset, chunk, 0, len);
                            float chunkStartSec = (float)offset / 16000f;
                            var t = _asr.Transcribe(chunk, chunkStartSec);
                            // Log every 20th result so we can see if ASR produces anything
                            if (transcripts.Count % 20 == 0)
                                Debug.Log($"[ASR] #{transcripts.Count} t={chunkStartSec:F1}s len={len} text='{t.Text}'");
                            if (!string.IsNullOrWhiteSpace(t.Text)) transcripts.Add(t);
                        }
                    }

                    Debug.Log($"[BatchProcess] ASR fertig: {transcripts.Count} Transkripte aus {speakers.Count} Segmenten");
                });

                var merged = TranscriptMergeService.Merge(transcripts, speakers);
                foreach (var l in merged)
                    OnSpeakerLine?.Invoke(l.SpeakerName, l.Text);

                string protocol = TranscriptMergeService.FormatAsProtocol(merged);
                LastProtocol = protocol;
                OnTranscriptUpdate?.Invoke(protocol);

                string dir = Path.Combine(Application.persistentDataPath, outputFolder);
                Directory.CreateDirectory(dir);
                string fn = $"Protokoll_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string path = Path.Combine(dir, fn);
                File.WriteAllText(path, protocol);
                File.WriteAllText(Path.ChangeExtension(path, ".rttm"),
                    TranscriptMergeService.FormatAsRTTM(merged));

                OnProtocolSaved?.Invoke(path);
                OnStatusMessage?.Invoke($"Gespeichert: {fn}");
            }
            catch (Exception e)
            {
                OnStatusMessage?.Invoke($"Fehler: {e.Message}");
                Debug.LogError($"[CareDoc] BatchProcess: {e}");
            }
        }

        /// <summary>Manuell die LLM-Nachbearbeitung des zuletzt gespeicherten Protokolls starten.</summary>
        public void TriggerLLMProcessing()
        {
            if (localDocService == null) { OnStatusMessage?.Invoke("Kein LLM-Service konfiguriert."); return; }
            if (string.IsNullOrWhiteSpace(LastProtocol)) { OnStatusMessage?.Invoke("Kein Protokoll vorhanden."); return; }
            _ = localDocService.ProcessProtocol(LastProtocol);
        }

        void OnDestroy()
        {
            _vad?.Dispose(); _asr?.Dispose(); _diarizer?.Dispose();
        }
    }
}
