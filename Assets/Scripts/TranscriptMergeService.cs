using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// Merges ASR transcript segments with diarization speaker segments
    /// → speaker-labeled protocol text.
    /// </summary>
    public static class TranscriptMergeService
    {
        public struct LabeledLine
        {
            public string SpeakerName;
            public int SpeakerLabel;
            public float StartSec;
            public float EndSec;
            public string Text;
        }

        /// <summary>
        /// Assign each ASR segment to the speaker with the most temporal overlap.
        /// </summary>
        public static List<LabeledLine> Merge(
            List<AsrService.TranscriptResult> transcripts,
            List<DiarizationService.SpeakerSegment> speakers)
        {
            var result = new List<LabeledLine>();

            foreach (var t in transcripts)
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;
                float tStart = t.StartSec;
                float tEnd = t.StartSec + t.DurationSec;

                float bestOverlap = 0f;
                var bestSpeaker = speakers.Count > 0
                    ? speakers[0]
                    : new DiarizationService.SpeakerSegment
                    { SpeakerLabel = -1, SpeakerName = "Unbekannt" };

                foreach (var s in speakers)
                {
                    float overlap = Mathf.Max(0,
                        Mathf.Min(tEnd, s.EndSec) - Mathf.Max(tStart, s.StartSec));
                    if (overlap > bestOverlap)
                    {
                        bestOverlap = overlap;
                        bestSpeaker = s;
                    }
                }

                result.Add(new LabeledLine
                {
                    SpeakerName = bestSpeaker.SpeakerName,
                    SpeakerLabel = bestSpeaker.SpeakerLabel,
                    StartSec = tStart,
                    EndSec = tEnd,
                    Text = t.Text
                });
            }
            return MergeConsecutive(result);
        }

        static List<LabeledLine> MergeConsecutive(List<LabeledLine> lines)
        {
            if (lines.Count == 0) return lines;
            var merged = new List<LabeledLine>();
            var cur = lines[0];
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].SpeakerLabel == cur.SpeakerLabel)
                {
                    cur.EndSec = lines[i].EndSec;
                    cur.Text += " " + lines[i].Text;
                }
                else { merged.Add(cur); cur = lines[i]; }
            }
            merged.Add(cur);
            return merged;
        }

        /// <summary>Human-readable protocol.</summary>
        public static string FormatAsProtocol(List<LabeledLine> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Pflegedokumentation - Sprachprotokoll ===");
            sb.AppendLine($"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}");
            sb.AppendLine();
            foreach (var l in lines)
            {
                sb.AppendLine($"[{Fmt(l.StartSec)} - {Fmt(l.EndSec)}] {l.SpeakerName}:");
                sb.AppendLine($"  {l.Text}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>RTTM format for evaluation/analysis.</summary>
        public static string FormatAsRTTM(List<LabeledLine> lines, string fileId = "recording")
        {
            var sb = new StringBuilder();
            foreach (var l in lines)
                sb.AppendLine($"SPEAKER {fileId} 1 {l.StartSec:F3} {l.EndSec - l.StartSec:F3} <NA> <NA> speaker_{l.SpeakerLabel} <NA> <NA>");
            return sb.ToString();
        }

        static string Fmt(float s) { int m = (int)(s / 60); return $"{m:D2}:{s - m * 60:00.0}"; }
    }
}
