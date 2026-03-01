using System;
using System.IO;
using UnityEngine;
using SherpaOnnx;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// Offline ASR using Whisper ONNX via sherpa-onnx.
    /// </summary>
    public class AsrService : IDisposable
    {
        public struct TranscriptResult
        {
            public string Text;
            public float StartSec;
            public float DurationSec;
        }

        readonly OfflineRecognizer _recognizer;
        readonly int _sampleRate = 16000;

        /// <param name="encoder">Path to whisper encoder.onnx</param>
        /// <param name="decoder">Path to whisper decoder.onnx</param>
        /// <param name="tokens">Path to tokens.txt</param>
        /// <param name="language">"de" for German</param>
        public AsrService(string encoder, string decoder, string tokens,
            string language = "de", string task = "transcribe",
            int numThreads = 4)
        {
            var config = new OfflineRecognizerConfig();
            config.ModelConfig.Whisper.Encoder = encoder;
            config.ModelConfig.Whisper.Decoder = decoder;
            config.ModelConfig.Whisper.Language = language;
            config.ModelConfig.Whisper.Task = task;
            config.ModelConfig.Tokens = tokens;
            config.ModelConfig.NumThreads = numThreads;
            config.ModelConfig.Debug = 0;

            _recognizer = new OfflineRecognizer(config);
            Debug.Log($"[ASR] Whisper loaded - lang={language}, threads={numThreads}");
        }

        /// <summary>Transcribe 16 kHz mono float[].</summary>
        public TranscriptResult Transcribe(float[] samples, float offsetSec = 0f)
        {
            var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(_sampleRate, samples);
            _recognizer.Decode(stream);
            var raw = stream.Result;

            return new TranscriptResult
            {
                Text = raw.Text.Trim(),
                StartSec = offsetSec,
                DurationSec = (float)samples.Length / _sampleRate
            };
        }

        public void Dispose() { _recognizer?.Dispose(); }
    }
}
