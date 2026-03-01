using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SherpaOnnx;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// Offline speaker diarization via sherpa-onnx.
    /// Pyannote segmentation + speaker embedding + clustering.
    /// </summary>
    public class DiarizationService : IDisposable
    {
        public struct SpeakerSegment
        {
            public int SpeakerLabel;
            public string SpeakerName;
            public float StartSec;
            public float EndSec;
        }

        public event Action<float> OnProgress; // 0..1

        readonly OfflineSpeakerDiarization _diarizer;
        readonly Dictionary<int, string> _speakerNames = new Dictionary<int, string>();

        public DiarizationService(string segmentationModel, string embeddingModel,
            int minSpeakers = 1, int maxSpeakers = 5, float threshold = 0.5f,
            int numThreads = 4)
        {
            var config = new OfflineSpeakerDiarizationConfig();
            config.Segmentation.Pyannote.Model = segmentationModel;
            config.Embedding.Model = embeddingModel;
            config.Clustering.NumClusters = 0; // auto-detect
            config.Clustering.Threshold = threshold;
            config.MinDurationOn = 0.3f;
            config.MinDurationOff = 0.5f;
            config.Segmentation.NumThreads = numThreads;
            config.Embedding.NumThreads = numThreads;
            config.Embedding.Debug = 0;
            config.Segmentation.Debug = 0;

            _diarizer = new OfflineSpeakerDiarization(config);
            Debug.Log($"[Diarization] Loaded - speakers:{minSpeakers}-{maxSpeakers}");
        }

        /// <summary>Set a human name for a speaker label, e.g. SetSpeakerName(0, "Pfleger Mueller")</summary>
        public void SetSpeakerName(int label, string name)
        {
            _speakerNames[label] = name;
        }

        /// <summary>Process a complete recording → speaker segments.
        /// Uses the non-callback Process overload to avoid IL2CPP marshaling issues
        /// (IL2CPP cannot marshal instance delegates to native code).</summary>
        public List<SpeakerSegment> Process(float[] samples)
        {
            var segments = new List<SpeakerSegment>();

            // _diarizer.Process (without callback) is IL2CPP-safe.
            // ProcessWithCallback crashes on IL2CPP because instance lambdas
            // cannot be marshaled as native function pointers.
            var rawSegments = _diarizer.Process(samples);

            foreach (var seg in rawSegments)
            {
                string name;
                if (!_speakerNames.TryGetValue(seg.Speaker, out name))
                    name = $"Sprecher {seg.Speaker + 1}";

                segments.Add(new SpeakerSegment
                {
                    SpeakerLabel = seg.Speaker,
                    SpeakerName = name,
                    StartSec = seg.Start,
                    EndSec = seg.End
                });
            }
            Debug.Log($"[Diarization] {segments.Count} segments found");
            return segments;
        }

        public void UpdateConfig(float threshold)
        {
            var config = new OfflineSpeakerDiarizationConfig();
            config.Clustering.Threshold = threshold;
            config.Clustering.NumClusters = 0;
            _diarizer.SetConfig(config);
        }

        public int SampleRate => _diarizer.SampleRate;
        public void Dispose() { _diarizer?.Dispose(); }
    }
}
