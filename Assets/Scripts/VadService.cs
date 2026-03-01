using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SherpaOnnx;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// Voice Activity Detection via Silero VAD (sherpa-onnx).
    /// Streaming-friendly: feed audio with Process(), retrieve completed
    /// speech segments with DrainSegments().
    /// </summary>
    public class VadService : IDisposable
    {
        public struct SpeechChunk
        {
            public float[] Samples;
            public float StartTimeSec;
            public float EndTimeSec;
        }

        public event Action<SpeechChunk> OnSpeechSegment;

        readonly VoiceActivityDetector _vad;
        readonly int _sampleRate;

        public VadService(string modelPath, int sampleRate = 16000,
            float threshold = 0.5f, float minSilenceDuration = 0.5f,
            float minSpeechDuration = 0.25f, int windowSize = 512)
        {
            _sampleRate = sampleRate;

            if (!File.Exists(modelPath))
            {
                Debug.LogError($"[VAD] Model file not found: {modelPath}");
                throw new FileNotFoundException($"VAD model not found: {modelPath}");
            }

            var config = new VadModelConfig();
            config.SileroVad.Model = modelPath;
            config.SileroVad.Threshold = threshold;
            config.SileroVad.MinSilenceDuration = minSilenceDuration;
            config.SileroVad.MinSpeechDuration = minSpeechDuration;
            config.SileroVad.WindowSize = windowSize;
            config.SampleRate = sampleRate;
            config.NumThreads = 2;
            config.Debug = 0;

            _vad = new VoiceActivityDetector(config, bufferSizeInSeconds: 120f);
            Debug.Log($"[VAD] Loaded: {Path.GetFileName(modelPath)} (windowSize={windowSize})");
        }

        /// <summary>Feed audio to VAD. Also returns segments for convenience (same as DrainSegments).</summary>
        public List<SpeechChunk> Process(float[] samples)
        {
            _vad.AcceptWaveform(samples);
            return DrainSegments();
        }

        /// <summary>Drain all completed speech segments from the internal queue.</summary>
        public List<SpeechChunk> DrainSegments()
        {
            var result = new List<SpeechChunk>();
            while (!_vad.IsEmpty())
            {
                var seg = _vad.Front();
                _vad.Pop();

                float startSec = (float)seg.Start / _sampleRate;
                float endSec = startSec + (float)seg.Samples.Length / _sampleRate;

                var chunk = new SpeechChunk
                {
                    Samples = seg.Samples,
                    StartTimeSec = startSec,
                    EndTimeSec = endSec
                };
                result.Add(chunk);
                OnSpeechSegment?.Invoke(chunk);
            }
            return result;
        }

        /// <summary>
        /// Signal that no more audio will arrive. Forces the VAD to emit
        /// any speech segment still being tracked (avoids losing the tail).
        /// </summary>
        public void Flush()
        {
            _vad.Flush();
        }

        public void Reset() { _vad.Reset(); }
        public void Dispose() { _vad?.Dispose(); }
    }
}
