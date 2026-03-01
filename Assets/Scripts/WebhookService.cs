using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// Sends transcript text to a Langdock workflow webhook.
    /// Automatically splits into batches if the payload exceeds the 200 000 char limit.
    /// Persists the webhook URL and secret across sessions via PlayerPrefs.
    /// </summary>
    public class WebhookService
    {
        const int MaxCharsPerBatch = 195_000; // leave headroom under 200k limit
        const string PrefKeyUrl = "Webhook_URL";

        /// <summary>Current webhook URL.</summary>
        public string Url
        {
            get => PlayerPrefs.GetString(PrefKeyUrl, DefaultUrl);
            set { PlayerPrefs.SetString(PrefKeyUrl, value); PlayerPrefs.Save(); }
        }

        public const string DefaultUrl =
            "https://app.langdock.com/api/hooks/workflows/4a7edfd8-ef26-4070-9205-c33df60c2020";

        // ── Events ──────────────────────────────────────────────
        public event Action<string> OnStatusMessage;
        public event Action<int, int> OnBatchProgress; // (done, total)
        public event Action<bool, string> OnComplete;  // (success, message)

        /// <summary>
        /// Send the transcript to the configured webhook.
        /// Must be called from a MonoBehaviour via StartCoroutine.
        /// Splits into multiple requests if the text exceeds the batch limit.
        /// </summary>
        public IEnumerator Send(string transcript)
        {
            if (string.IsNullOrEmpty(Url))
            {
                OnComplete?.Invoke(false, "Webhook-URL ist leer.");
                yield break;
            }
            if (string.IsNullOrWhiteSpace(transcript))
            {
                OnComplete?.Invoke(false, "Kein Transkript vorhanden.");
                yield break;
            }

            var batches = SplitIntoBatches(transcript, MaxCharsPerBatch);
            int total = batches.Count;
            int done = 0;
            bool allOk = true;
            string lastError = null;

            foreach (var batch in batches)
            {
                done++;
                string label = total > 1 ? $" (Teil {done}/{total})" : "";
                OnStatusMessage?.Invoke($"Sende an Webhook{label}...");
                OnBatchProgress?.Invoke(done, total);

                // Build JSON payload: {"prompt": "..."}
                string promptValue = EscapeJsonString(batch);
                string json = $"{{\"prompt\": \"{promptValue}\"}}";

                using (var req = new UnityWebRequest(Url, "POST"))
                {
                    byte[] body = Encoding.UTF8.GetBytes(json);
                    req.uploadHandler = new UploadHandlerRaw(body);
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");

                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        allOk = false;
                        lastError = $"HTTP {req.responseCode}: {req.error}";
                        Debug.LogError($"[Webhook] Batch {done}/{total} failed: {lastError}");
                        // continue with remaining batches anyway
                    }
                    else
                    {
                        Debug.Log($"[Webhook] Batch {done}/{total} sent OK ({batch.Length} chars).");
                    }
                }
            }

            if (allOk)
                OnComplete?.Invoke(true, $"Erfolgreich gesendet ({total} Teil(e)).");
            else
                OnComplete?.Invoke(false, $"Fehler: {lastError}");
        }

        // ── Helpers ─────────────────────────────────────────────

        /// <summary>Split text into chunks of at most maxChars, breaking at line boundaries when possible.</summary>
        static List<string> SplitIntoBatches(string text, int maxChars)
        {
            var result = new List<string>();
            if (text.Length <= maxChars)
            {
                result.Add(text);
                return result;
            }

            int pos = 0;
            while (pos < text.Length)
            {
                int remaining = text.Length - pos;
                int take = Math.Min(remaining, maxChars);

                // Try to break at last newline within the chunk
                if (take < remaining)
                {
                    int lastNl = text.LastIndexOf('\n', pos + take - 1, take);
                    if (lastNl > pos)
                        take = lastNl - pos + 1; // include the newline
                }

                result.Add(text.Substring(pos, take));
                pos += take;
            }
            return result;
        }

        /// <summary>Escape a string for safe embedding inside a JSON string value.</summary>
        static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 64);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
