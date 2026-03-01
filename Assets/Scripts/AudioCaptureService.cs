using System;
using System.Collections;
using UnityEngine;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// Captures microphone audio and delivers 16 kHz mono float[] chunks.
    /// Includes built-in microphone selection (Inspector dropdown + runtime API).
    /// </summary>
    public class AudioCaptureService : MonoBehaviour
    {
        [Header("Microphone")]
        [HideInInspector] public int selectedMicIndex = 0;
        public string micDeviceName = "";

        [Header("Settings")]
        public int targetSampleRate = 16000;
        [Range(1, 10)] public int chunkSeconds = 1;
        public int clipLengthSeconds = 60;

        public event Action<float[]> OnAudioChunkReady;
        public event Action<bool> OnRecordingStateChanged;
        public event Action<string> OnMicChanged;
        public bool IsRecording { get; private set; }

        AudioClip _clip; int _lastPos; int _micRate;
        Coroutine _loop;

        // ── Microphone helpers ──────────────────────────────────

        /// <summary>Returns all available microphone device names.</summary>
        public string[] GetAvailableMicrophones() => Microphone.devices;

        /// <summary>Select a mic by index (runtime). Restarts capture if recording.</summary>
        public void SelectMicrophone(int index)
        {
            string[] mics = Microphone.devices;
            if (mics.Length == 0) return;
            index = Mathf.Clamp(index, 0, mics.Length - 1);
            selectedMicIndex = index;
            micDeviceName = mics[index];
            OnMicChanged?.Invoke(micDeviceName);
            Debug.Log($"[AudioCapture] Mikrofon gewählt: {micDeviceName}");

            if (IsRecording)
            {
                StopCapture();
                StartCapture();
            }
        }

        /// <summary>Select a mic by device name (runtime).</summary>
        public void SelectMicrophone(string deviceName)
        {
            int idx = Array.IndexOf(Microphone.devices, deviceName);
            if (idx >= 0) SelectMicrophone(idx);
        }

        void Awake()
        {
            // Immer das erste verfügbare Mikrofon wählen,
            // da der serialisierte Gerätename plattformspezifisch ist
            // (PC-Mic existiert nicht auf Android und umgekehrt).
            if (Microphone.devices.Length > 0)
            {
                // Prüfe ob der gespeicherte Name auf diesem Gerät existiert
                bool savedDeviceExists = !string.IsNullOrEmpty(micDeviceName)
                    && Array.IndexOf(Microphone.devices, micDeviceName) >= 0;

                if (!savedDeviceExists)
                {
                    micDeviceName = Microphone.devices[0];
                    selectedMicIndex = 0;
                    Debug.Log($"[AudioCapture] Mic auto-selected: {micDeviceName}");
                }
            }
            else
            {
                Debug.LogWarning("[AudioCapture] Kein Mikrofon verfügbar!");
            }
        }

        // ── Recording ───────────────────────────────────────────

        string DevOrNull => string.IsNullOrEmpty(micDeviceName) ? null : micDeviceName;

        public void StartCapture()
        {
            if (IsRecording) return;

            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[AudioCapture] Kein Mikrofon gefunden!");
                return;
            }

            string dev = DevOrNull;
            int lo, hi;
            Microphone.GetDeviceCaps(dev, out lo, out hi);
            _micRate = hi == 0 ? 44100 : Mathf.Clamp(targetSampleRate, lo, hi);
            _clip = Microphone.Start(dev, true, clipLengthSeconds, _micRate);

            if (_clip == null)
            {
                Debug.LogError("[AudioCapture] Microphone.Start fehlgeschlagen – Berechtigung erteilt?");
                return;
            }

            _lastPos = 0;
            IsRecording = true;
            _loop = StartCoroutine(ReadLoop());
            OnRecordingStateChanged?.Invoke(true);
            Debug.Log($"[AudioCapture] mic={_micRate}Hz target={targetSampleRate}Hz dev={micDeviceName}");
        }

        public void StopCapture()
        {
            if (!IsRecording) return;
            Microphone.End(DevOrNull);
            if (_loop != null) StopCoroutine(_loop);
            IsRecording = false;
            OnRecordingStateChanged?.Invoke(false);
        }

        IEnumerator ReadLoop()
        {
            int chunk = Mathf.RoundToInt(chunkSeconds * _micRate);
            while (IsRecording)
            {
                int pos = Microphone.GetPosition(DevOrNull);
                if (pos < 0) { yield return null; continue; }
                int avail = pos >= _lastPos
                    ? pos - _lastPos
                    : (_clip.samples - _lastPos) + pos;
                if (avail >= chunk)
                {
                    float[] raw = new float[chunk];
                    _clip.GetData(raw, _lastPos);
                    _lastPos = (_lastPos + chunk) % _clip.samples;
                    OnAudioChunkReady?.Invoke(
                        _micRate != targetSampleRate
                            ? Resample(raw, _micRate, targetSampleRate) : raw);
                }
                yield return null;
            }
        }

        // ── WAV loading ─────────────────────────────────────────

        /// <summary>Load a 16-bit PCM WAV → 16 kHz mono float[].</summary>
        public float[] LoadWavFile(string path)
        {
            byte[] b = System.IO.File.ReadAllBytes(path);
            int ch = BitConverter.ToInt16(b, 22);
            int sr = BitConverter.ToInt32(b, 24);
            int bps = BitConverter.ToInt16(b, 34);
            int off = 44; int bpS = bps / 8;
            int n = (b.Length - off) / bpS / ch;
            float[] m = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = 0;
                for (int c = 0; c < ch; c++)
                    s += BitConverter.ToInt16(b, off + (i * ch + c) * bpS) / 32768f;
                m[i] = s / ch;
            }
            return sr != targetSampleRate ? Resample(m, sr, targetSampleRate) : m;
        }

        public static float[] Resample(float[] src, int from, int to)
        {
            if (from == to) return src;
            double r = (double)from / to;
            int len = (int)(src.Length / r);
            float[] o = new float[len];
            for (int i = 0; i < len; i++)
            {
                double si = i * r; int a = (int)si;
                int b2 = Math.Min(a + 1, src.Length - 1);
                double f = si - a;
                o[i] = (float)(src[a] * (1.0 - f) + src[b2] * f);
            }
            return o;
        }

        void OnDestroy() => StopCapture();
    }
}
