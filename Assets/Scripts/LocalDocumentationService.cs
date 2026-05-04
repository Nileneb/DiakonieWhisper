using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// Processes raw speech protocols with a local LLM and optional RAG knowledge base.
    /// Rewrites raw transcripts into structured Pflegedokumentation.
    /// Fires streaming events so the UI can display progress in real time.
    /// </summary>
    public class LocalDocumentationService : MonoBehaviour
    {
        [Header("LLM References")]
        [Tooltip("The LLMAgent component responsible for text generation.")]
        public LLMAgent llmAgent;

        [Tooltip("Optional RAG component for retrieving relevant care guidelines.")]
        public RAG rag;

        [Header("RAG Settings")]
        [Tooltip("File name inside StreamingAssets that contains the care guidelines text.")]
        public string guidelinesFileName = "guidelines_pflege.txt";

        [Tooltip("File name used to cache the RAG embeddings (saved to persistentDataPath).")]
        public string ragStorePath = "PflegeRAG.zip";

        [Header("Output")]
        [Tooltip("Sub-folder inside Application.persistentDataPath where formatted protocols are saved.")]
        public string outputFolder = "Protokolle";

        // ── Public events ────────────────────────────────────────
        /// <summary>Fired with informational / progress messages meant for the status bar.</summary>
        public event Action<string> OnStatusMessage;

        /// <summary>Fired for each streamed partial LLM token so the UI can display live output.</summary>
        public event Action<string> OnPartialResult;

        /// <summary>Fired once with the absolute path to the saved formatted documentation file.</summary>
        public event Action<string> OnProcessingComplete;

        /// <summary>Fired when processing fails, with a human-readable error description.</summary>
        public event Action<string> OnProcessingError;

        // ── State ────────────────────────────────────────────────
        /// <summary>True when the LLMAgent component is present, enabled and its GameObject is active.</summary>
        public bool IsReady =>
            llmAgent != null && llmAgent.enabled && llmAgent.gameObject.activeInHierarchy;

        bool _ragLoaded;
        bool _processing;

        // ── Unity lifecycle ──────────────────────────────────────

        async void Start()
        {
            if (rag != null)
                await InitRAGAsync();
        }

        // ── RAG initialisation ───────────────────────────────────

        /// <summary>
        /// Loads existing RAG embeddings from disk or builds them from the StreamingAssets
        /// guidelines file.  Either way the service is ready for Search() after this returns.
        /// </summary>
        async Task InitRAGAsync()
        {
            if (!IsEmbeddingsModelReady())
            {
                Debug.LogWarning("[LocalDocService] Kein Embeddings-Modell konfiguriert → RAG deaktiviert.");
                OnStatusMessage?.Invoke("RAG deaktiviert (kein Embeddings-Modell).");
                return;
            }

            try
            {
                OnStatusMessage?.Invoke("Lade Pflege-Wissensdatenbank...");

                bool loaded = await rag.Load(ragStorePath);
                if (loaded)
                {
                    _ragLoaded = true;
                    OnStatusMessage?.Invoke($"Wissensdatenbank geladen ({rag.Count()} Einträge).");
                    return;
                }

                string guidelinesPath = Path.Combine(Application.streamingAssetsPath, guidelinesFileName);
                if (!File.Exists(guidelinesPath))
                {
                    Debug.LogWarning($"[LocalDocService] Keine Richtlinien-Datei gefunden: {guidelinesPath}");
                    OnStatusMessage?.Invoke("Keine Richtlinien-Datei gefunden — RAG deaktiviert.");
                    return;
                }

                OnStatusMessage?.Invoke("Erstelle Wissensdatenbank (einmalig, bitte warten)...");
                string text = File.ReadAllText(guidelinesPath, Encoding.UTF8);
                string[] paragraphs = text.Split(
                    new[] { "\r\n\r\n", "\n\n" },
                    StringSplitOptions.RemoveEmptyEntries);

                int count = 0;
                foreach (string para in paragraphs)
                {
                    string trimmed = para.Trim();
                    if (trimmed.Length >= 20)
                    {
                        await rag.Add(trimmed);
                        count++;
                    }
                }

                rag.Save(ragStorePath);
                _ragLoaded = true;
                OnStatusMessage?.Invoke($"Wissensdatenbank erstellt ({count} Abschnitte).");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalDocService] RAG-Initialisierung fehlgeschlagen: {e}");
                OnStatusMessage?.Invoke($"Wissensdatenbank-Fehler: {e.Message}");
            }
        }

        // ── Protocol processing ──────────────────────────────────

        /// <summary>
        /// Rewrites a raw speech protocol into structured Pflegedokumentation.
        /// Fires <see cref="OnPartialResult"/> for each streamed token and
        /// <see cref="OnProcessingComplete"/> with the saved file path when finished.
        /// </summary>
        /// <param name="rawProtocol">Raw, unstructured transcript text produced by the ASR pipeline.</param>
        public async Task ProcessProtocol(string rawProtocol)
        {
            if (_processing)
            {
                OnProcessingError?.Invoke("Verarbeitung läuft bereits.");
                return;
            }

            if (string.IsNullOrWhiteSpace(rawProtocol))
            {
                OnProcessingError?.Invoke("Kein Protokolltext vorhanden.");
                return;
            }

            if (!IsReady)
            {
                OnProcessingError?.Invoke("LLM nicht bereit. Bitte Modell im Inspector konfigurieren.");
                return;
            }

            _processing = true;
            try
            {
                OnStatusMessage?.Invoke("LLM strukturiert das Protokoll...");

                string ragContext = await BuildRAGContextAsync(rawProtocol);
                string prompt = BuildPrompt(rawProtocol, ragContext);

                // Start each protocol with a clean context so previous sessions
                // do not bleed into the current documentation.
                await llmAgent.ClearHistory();

                string fullResult = await llmAgent.Chat(
                    prompt,
                    partial => OnPartialResult?.Invoke(partial),
                    completionCallback: null,
                    addToHistory: false
                );

                if (string.IsNullOrWhiteSpace(fullResult))
                {
                    OnProcessingError?.Invoke("LLM hat kein Ergebnis geliefert.");
                    return;
                }

                string filePath = SaveFormattedProtocol(fullResult);
                OnProcessingComplete?.Invoke(filePath);
                OnStatusMessage?.Invoke($"Strukturierte Dokumentation gespeichert: {Path.GetFileName(filePath)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalDocService] Verarbeitungsfehler: {e}");
                OnProcessingError?.Invoke($"Fehler: {e.Message}");
                OnStatusMessage?.Invoke($"LLM-Fehler: {e.Message}");
            }
            finally
            {
                _processing = false;
            }
        }

        /// <summary>Cancels an in-progress LLM request, if any.</summary>
        public void CancelProcessing()
        {
            if (_processing && llmAgent != null)
                llmAgent.CancelRequests();
        }

        // ── Private helpers ──────────────────────────────────────

        async Task<string> BuildRAGContextAsync(string query)
        {
            if (rag == null || !_ragLoaded || rag.Count() == 0)
                return string.Empty;

            // Cached RAG store may have loaded even when embeddings are broken —
            // re-check here to prevent LLMUnity from logging its internal error
            if (!IsEmbeddingsModelReady())
            {
                _ragLoaded = false;
                Debug.LogWarning("[LocalDocService] Embeddings-Modell nicht bereit — RAG deaktiviert.");
                return string.Empty;
            }

            try
            {
                (string[] chunks, float[] _) = await rag.Search(query, 3);
                if (chunks == null || chunks.Length == 0)
                    return string.Empty;

                return "\n\nRelevante Pflegerichtlinien:\n" + string.Join("\n---\n", chunks);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalDocService] RAG-Suche fehlgeschlagen: {e.Message}");
                _ragLoaded = false;
                return string.Empty;
            }
        }

        bool IsEmbeddingsModelReady()
        {
            if (rag == null) return false;
            var search = rag.GetComponent<SearchMethod>();
            if (search == null) search = rag.GetComponentInChildren<SearchMethod>();
            var embeddingsLlm = search?.llmEmbedder?.llm;
            if (embeddingsLlm == null || !embeddingsLlm.embeddingsOnly) return false;
            // A chat LLM set to embeddingsOnly=true still can't produce embeddings —
            // require a SEPARATE LLM instance dedicated to embeddings
            if (llmAgent != null && embeddingsLlm == llmAgent.llm) return false;
            return true;
        }

        static string BuildPrompt(string rawProtocol, string ragContext)
        {
            const int maxProtocolChars = 24000;
            if (rawProtocol.Length > maxProtocolChars)
                rawProtocol = rawProtocol.Substring(0, maxProtocolChars) + "\n[Protokoll gekürzt]";

            var sb = new StringBuilder();
            sb.AppendLine("Du bist ein Assistent für die Bergische Diakonie.");
            sb.AppendLine("Schreibe das folgende Pflegeprotokoll in eine strukturierte, professionelle Pflegedokumentation um.");
            sb.AppendLine("Halte dich an die Standards der Pflegedokumentation: Datum/Zeit, Pflegemaßnahmen, Beobachtungen, Besonderheiten.");
            sb.AppendLine("Schreibe in klarer, sachlicher Sprache auf Deutsch. Erfinde keine Informationen.");
            if (!string.IsNullOrEmpty(ragContext))
                sb.Append(ragContext);
            sb.AppendLine();
            sb.AppendLine("Rohprotokoll:");
            sb.AppendLine(rawProtocol);
            return sb.ToString();
        }

        string SaveFormattedProtocol(string content)
        {
            string dir = Path.Combine(Application.persistentDataPath, outputFolder);
            Directory.CreateDirectory(dir);
            string fn = $"Pflegedoku_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string path = Path.Combine(dir, fn);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }
    }
}
