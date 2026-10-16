using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Import settings for everything under Art/Audio: audio data is preloaded with the scene, so no clip hitches the
    /// first time it plays, and WAVs shorter than 0.5 s (transients, ticks, clicks) stay uncompressed PCM, which keeps
    /// their attack exact; longer clips keep the default Vorbis.
    /// </summary>
    internal sealed class AudioImportPostprocessor : AssetPostprocessor
    {
        private const float ShortClipSeconds = 0.5f;

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(GunshotAudioBuilder.AudioFolder + "/")) return;
            var importer = (AudioImporter)assetImporter;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.preloadAudioData = true;
            if (WavSeconds(assetPath) < ShortClipSeconds) settings.compressionFormat = AudioCompressionFormat.PCM;
            importer.defaultSampleSettings = settings;
        }

        /// <summary>Duration of a WAV from its size and the byte rate in its fmt chunk (first after the RIFF header); other formats count as long.</summary>
        private static float WavSeconds(string path)
        {
            if (!path.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase)) return float.MaxValue;
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.BaseStream.Length < 44) return float.MaxValue;
                reader.BaseStream.Position = 28;
                int byteRate = reader.ReadInt32();
                return byteRate > 0 ? (reader.BaseStream.Length - 44f) / byteRate : float.MaxValue;
            }
        }
    }
}
