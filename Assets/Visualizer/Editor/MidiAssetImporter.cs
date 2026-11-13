using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Visualizer.Editor
{
    /// <summary>Lets Unity understand .mid / .midi files dropped into the project.</summary>
    [ScriptedImporter(1, new[] { "mid", "midi" })]
    public class MidiAssetImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<MidiAsset>();
            asset.SetData(File.ReadAllBytes(ctx.assetPath));
            ctx.AddObjectToAsset("midi", asset);
            ctx.SetMainObject(asset);
        }
    }
}
