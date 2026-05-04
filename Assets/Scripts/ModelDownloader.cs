using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine.UI;
using BergischeDiakonie.Speech;
using Unity.SharpZipLib.BZip2;
using Unity.SharpZipLib.Tar;

/// <summary>
/// Lädt sherpa-onnx Modelle beim ersten App-Start von öffentlichen URLs.
/// Auf ein GameObject in der Start-Scene legen.
/// </summary>
public class ModelDownloader : MonoBehaviour
{
    [Header("UI References")]
    public GameObject downloadPanel;
    public Slider progressBar;
    public TMP_Text statusText;
    public TMP_Text totalSizeText;

    [Header("References")]
    public CareDocumentationManager careDocManager;

    /// <summary>Fired once when all models are present (downloaded or already on disk).</summary>
    public event Action OnAllModelsReady;

    [Serializable]
    public struct ModelFile
    {
        public string fileName;
        public string url;
        public long expectedSizeMB;
        /// <summary>Wenn true, ist die URL ein tar.bz2-Archiv.</summary>
        public bool isTarBz2;
        /// <summary>Relativer Pfad der gewünschten Datei im Archiv, z.B. "folder/model.onnx".</summary>
        public string archiveEntryPath;
    }

    // ═══════════════════════════════════════════════
    //  PERMANENTE DOWNLOAD-URLS (NilEneb/DiakonieWhisper-models auf HuggingFace)
    //  Generiert via tools/export_and_upload_models.py
    // ═══════════════════════════════════════════════
    private const string HF_BASE = "https://huggingface.co/NilEneb/DiakonieWhisper-models/resolve/main/";

    private readonly ModelFile[] models = new ModelFile[]
    {
        // ── Whisper Medium (multilingual, deutsch-optimiert, ~1.5 GB gesamt) ──
        new ModelFile
        {
            fileName = "medium-encoder.onnx",
            url = HF_BASE + "medium-encoder.onnx",
            expectedSizeMB = 764
        },
        new ModelFile
        {
            fileName = "medium-decoder.onnx",
            url = HF_BASE + "medium-decoder.onnx",
            expectedSizeMB = 736
        },
        new ModelFile
        {
            fileName = "medium-tokens.txt",
            url = HF_BASE + "medium-tokens.txt",
            expectedSizeMB = 1
        },

        // ── Silero VAD (~2 MB) ──
        new ModelFile
        {
            fileName = "silero_vad.onnx",
            url = HF_BASE + "silero_vad.onnx",
            expectedSizeMB = 2
        },

        // ── Speaker Segmentation – Pyannote 3.0 (~5 MB) ──
        new ModelFile
        {
            fileName = "sherpa-onnx-pyannote-segmentation-3-0.onnx",
            url = HF_BASE + "sherpa-onnx-pyannote-segmentation-3-0.onnx",
            expectedSizeMB = 5
        },

        // ── Speaker Embedding – 3D-Speaker (~25 MB) ──
        new ModelFile
        {
            fileName = "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
            url = HF_BASE + "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
            expectedSizeMB = 25
        },
    };

    // ═══════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════

    /// <summary>Modell-Verzeichnis auf dem Gerät.</summary>
    public static string ModelDirectory =>
        Path.Combine(Application.persistentDataPath, "sherpa-onnx-models");

    /// <summary>Vollständiger Pfad für eine Modell-Datei.</summary>
    public static string GetModelPath(string fileName) =>
        Path.Combine(ModelDirectory, fileName);

    /// <summary>True wenn alle Modelle heruntergeladen sind.</summary>
    public bool AllModelsPresent()
    {
        foreach (var model in models)
        {
            if (!File.Exists(GetModelPath(model.fileName)))
                return false;
        }
        return true;
    }

    // ═══════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════

    private void Start()
    {
        if (AllModelsPresent())
        {
            Debug.Log("[ModelDownloader] Alle Modelle vorhanden.");
            HideDownloadUI();
            OnModelsReady();
        }
        else
        {
            ShowDownloadUI();
            StartCoroutine(DownloadAllModels());
        }
    }

    // ═══════════════════════════════════════════════
    //  DOWNLOAD LOGIC
    // ═══════════════════════════════════════════════

    private IEnumerator DownloadAllModels()
    {
        Directory.CreateDirectory(ModelDirectory);

        long totalMB = 0;
        foreach (var m in models) totalMB += m.expectedSizeMB;
        if (totalSizeText != null)
            totalSizeText.text = $"Gesamtgröße: ~{totalMB} MB";

        int completed = 0;

        for (int i = 0; i < models.Length; i++)
        {
            var model = models[i];
            string savePath = GetModelPath(model.fileName);

            // Bereits heruntergeladen? Überspringen.
            if (File.Exists(savePath))
            {
                Debug.Log($"[ModelDownloader] {model.fileName} vorhanden, überspringe.");
                completed++;
                UpdateProgress(completed, models.Length, 1f);
                continue;
            }

            // Temp-Datei für atomares Schreiben
            string tempPath = savePath + ".tmp";

            SetStatus($"Lade {model.fileName}\n({i + 1}/{models.Length}) ~{model.expectedSizeMB} MB");

            using (var request = UnityWebRequest.Get(model.url))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 600; // 10 Min pro Datei

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    UpdateProgress(completed, models.Length, request.downloadProgress);
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = $"Fehler bei {model.fileName}:\n{request.error}";
                    Debug.LogError($"[ModelDownloader] {error}");
                    SetStatus(error + "\n\nBitte Internetverbindung prüfen und App neu starten.");
                    yield break;
                }

                try
                {
                    if (model.isTarBz2)
                    {
                        // tar.bz2-Archiv entpacken → gewünschte Datei extrahieren
                        SetStatus($"Entpacke {model.fileName}…");
                        Debug.Log($"[ModelDownloader] tar.bz2 Archiv: {request.downloadedBytes} bytes, entry={model.archiveEntryPath}, savePath={savePath}");
                        ExtractFileFromTarBz2(
                            request.downloadHandler.data,
                            model.archiveEntryPath,
                            savePath);
                        Debug.Log($"[ModelDownloader] {model.fileName} aus Archiv extrahiert. exists={File.Exists(savePath)}, size={new FileInfo(savePath).Length}");
                    }
                    else
                    {
                        File.WriteAllBytes(tempPath, request.downloadHandler.data);
                        if (File.Exists(savePath)) File.Delete(savePath);
                        File.Move(tempPath, savePath);
                    }
                    Debug.Log($"[ModelDownloader] {model.fileName} OK ({request.downloadedBytes / 1024 / 1024} MB)");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModelDownloader] Schreibfehler: {ex.Message}\n{ex.StackTrace}");
                    SetStatus($"Speicherfehler bei {model.fileName}:\n{ex.Message}");
                    yield break;
                }
            }

            completed++;
            UpdateProgress(completed, models.Length, 1f);
        }

        // Verifizieren
        if (!AllModelsPresent())
        {
            SetStatus("Fehler: Nicht alle Modelle geladen.\nBitte App neu starten.");
            yield break;
        }

        SetStatus("Alle Modelle geladen! App startet...");
        if (progressBar != null) progressBar.value = 1f;
        yield return new WaitForSeconds(1.5f);
        HideDownloadUI();
        OnModelsReady();
    }

    // ═══════════════════════════════════════════════
    //  TAR.BZ2 EXTRACTION
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Extrahiert eine einzelne Datei aus einem tar.bz2-Byte-Array
    /// und speichert sie unter <paramref name="outputPath"/>.
    /// </summary>
    private static void ExtractFileFromTarBz2(byte[] archiveData, string entryPath, string outputPath)
    {
        Debug.Log($"[ModelDownloader] ExtractFileFromTarBz2: archiveSize={archiveData.Length}, looking for=\"{entryPath}\"");

        using (var rawStream = new MemoryStream(archiveData))
        using (var bz2Stream = new BZip2InputStream(rawStream))
        using (var tarStream = new TarInputStream(bz2Stream, null))
        {
            TarEntry entry;
            while ((entry = tarStream.GetNextEntry()) != null)
            {
                // Vergleich mit normalisiertem Pfad (forward slashes)
                string name = entry.Name.Replace('\\', '/').TrimEnd('/');
                string target = entryPath.Replace('\\', '/').TrimEnd('/');

                Debug.Log($"[ModelDownloader] tar entry: \"{name}\" (size={entry.Size}, isDir={entry.IsDirectory})");

                if (!string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                    continue;

                Debug.Log($"[ModelDownloader] MATCH found! Extracting {entry.Size} bytes → {outputPath}");

                // Temp-Datei für atomares Schreiben
                string tempPath = outputPath + ".tmp";
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    tarStream.CopyEntryContents(fs);
                }
                if (File.Exists(outputPath)) File.Delete(outputPath);
                File.Move(tempPath, outputPath);
                return;
            }
        }

        throw new FileNotFoundException(
            $"Eintrag \"{entryPath}\" nicht im tar.bz2-Archiv gefunden.");
    }

    // ═══════════════════════════════════════════════
    //  UI HELPERS
    // ═══════════════════════════════════════════════

    private void UpdateProgress(int completedFiles, int totalFiles, float currentFileProgress)
    {
        if (progressBar != null)
            progressBar.value = (completedFiles + currentFileProgress) / totalFiles;
    }

    private void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    private void ShowDownloadUI()
    {
        if (downloadPanel != null) downloadPanel.SetActive(true);
        if (progressBar != null) progressBar.value = 0f;
    }

    private void HideDownloadUI()
    {
        if (downloadPanel != null) downloadPanel.SetActive(false);
    }

    // ═══════════════════════════════════════════════
    //  CALLBACK
    // ═══════════════════════════════════════════════

    private void OnModelsReady()
    {
        Debug.Log($"[ModelDownloader] Modelle bereit in: {ModelDirectory}");
        OnAllModelsReady?.Invoke();
    }

    // ═══════════════════════════════════════════════
    //  UTILITY: Modelle löschen (zum Testen)
    // ═══════════════════════════════════════════════

    [ContextMenu("Delete All Downloaded Models")]
    public void DeleteAllModels()
    {
        if (Directory.Exists(ModelDirectory))
        {
            Directory.Delete(ModelDirectory, true);
            Debug.Log("[ModelDownloader] Alle Modelle gelöscht.");
        }
    }
}
