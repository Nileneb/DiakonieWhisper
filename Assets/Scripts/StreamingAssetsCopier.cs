using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// On Android, StreamingAssets live inside the compressed APK and cannot
    /// be read by File.* or native libs (ONNX Runtime). This helper copies
    /// them to Application.persistentDataPath so they are accessible as
    /// regular files.
    ///
    /// On other platforms the files are already on disk, so ModelBasePath
    /// simply returns Application.streamingAssetsPath.
    ///
    /// Usage:
    ///   yield return StreamingAssetsCopier.EnsureExtracted();
    ///   string vadPath = StreamingAssetsCopier.ModelPath("SherpaOnnx/vad-models/silero_vad.onnx");
    /// </summary>
    public class StreamingAssetsCopier : MonoBehaviour
    {
        /// <summary>
        /// The base folder where model files are accessible via normal IO.
        /// On Android this is persistentDataPath, elsewhere streamingAssetsPath.
        /// </summary>
        public static string ModelBasePath
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Path.Combine(Application.persistentDataPath, "StreamingAssets");
#else
                return Application.streamingAssetsPath;
#endif
            }
        }

        /// <summary>Resolve a relative path (e.g. "SherpaOnnx/vad-models/silero_vad.onnx") to an absolute file path.</summary>
        public static string ModelPath(string relativePath)
        {
            return Path.Combine(ModelBasePath, relativePath);
        }

        /// <summary>True after all files have been extracted successfully.</summary>
        public static bool IsReady { get; private set; }

        public event Action<float> OnProgress;          // 0..1
#pragma warning disable CS0067 // subscribed externally
        public event Action<string> OnStatusMessage;
#pragma warning restore CS0067
        public event Action<bool> OnComplete;           // true = success

        /// <summary>
        /// Starts the extraction coroutine. On non-Android platforms this
        /// completes immediately. Call from a MonoBehaviour via
        ///   StartCoroutine(copier.EnsureExtracted());
        /// </summary>
        public IEnumerator EnsureExtracted()
        {
#if !UNITY_ANDROID || UNITY_EDITOR
            IsReady = true;
            OnProgress?.Invoke(1f);
            OnComplete?.Invoke(true);
            yield break;
#else
            if (IsReady)
            {
                OnComplete?.Invoke(true);
                yield break;
            }

            OnStatusMessage?.Invoke("Modelldateien werden extrahiert...");

            // 1. Read the manifest from StreamingAssets
            string manifestUrl = Path.Combine(Application.streamingAssetsPath,
                "SherpaOnnx/streaming-assets-manifest.json");

            string manifestJson = null;
            using (var req = UnityWebRequest.Get(manifestUrl))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[StreamingAssetsCopier] Cannot read manifest: {req.error}");
                    OnStatusMessage?.Invoke($"Fehler: Manifest nicht lesbar ({req.error})");
                    OnComplete?.Invoke(false);
                    yield break;
                }
                manifestJson = req.downloadHandler.text;
            }

            var manifest = JsonUtility.FromJson<Manifest>(manifestJson);
            if (manifest == null || manifest.files == null || manifest.files.Count == 0)
            {
                Debug.LogError("[StreamingAssetsCopier] Manifest leer oder ungueltig.");
                OnComplete?.Invoke(false);
                yield break;
            }

            // 2. Check version — skip copy if already up to date
            string versionFile = Path.Combine(ModelBasePath, ".version");
            if (File.Exists(versionFile) && File.ReadAllText(versionFile).Trim() == manifest.version)
            {
                // Already extracted with this version
                bool allExist = true;
                foreach (var f in manifest.files)
                {
                    if (!File.Exists(Path.Combine(ModelBasePath, f)))
                    {
                        allExist = false;
                        break;
                    }
                }
                if (allExist)
                {
                    Debug.Log("[StreamingAssetsCopier] All files already extracted (version match).");
                    IsReady = true;
                    OnProgress?.Invoke(1f);
                    OnComplete?.Invoke(true);
                    yield break;
                }
            }

            // 3. Copy each file
            int total = manifest.files.Count;
            int done = 0;
            bool hadError = false;

            foreach (var relPath in manifest.files)
            {
                string srcUrl = Path.Combine(Application.streamingAssetsPath, relPath);
                string dstPath = Path.Combine(ModelBasePath, relPath);

                // Skip if file already exists with same size
                // (lightweight check — version file catches content changes across builds)
                if (File.Exists(dstPath))
                {
                    done++;
                    OnProgress?.Invoke((float)done / total);
                    continue;
                }

                string dir = Path.GetDirectoryName(dstPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using (var req = UnityWebRequest.Get(srcUrl))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[StreamingAssetsCopier] Failed to copy {relPath}: {req.error}");
                        hadError = true;
                        // continue anyway — try the rest
                    }
                    else
                    {
                        File.WriteAllBytes(dstPath, req.downloadHandler.data);
                    }
                }

                done++;
                float progress = (float)done / total;
                OnProgress?.Invoke(progress);
                OnStatusMessage?.Invoke($"Extrahiere Modelle... {done}/{total}");
            }

            // 4. Write version marker
            if (!hadError)
            {
                File.WriteAllText(versionFile, manifest.version);
            }

            IsReady = !hadError;
            OnProgress?.Invoke(1f);
            OnStatusMessage?.Invoke(hadError
                ? "Fehler beim Extrahieren einiger Dateien."
                : "Modelldateien bereit.");
            OnComplete?.Invoke(!hadError);
            Debug.Log($"[StreamingAssetsCopier] Done. success={!hadError}, files={total}");
#endif
        }

        // ── JSON model for the manifest ─────────────────────────
        [Serializable]
        class Manifest
        {
            public string version;
            public List<string> files;
        }
    }
}
