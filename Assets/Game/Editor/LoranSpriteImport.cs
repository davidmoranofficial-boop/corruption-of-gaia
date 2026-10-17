#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Loran.EditorTools
{
    /// <summary>
    /// Pixel art has to stay sharp. Every picture under Assets/Game/Resources/Sprites is imported
    /// with point filtering, no compression, no mipmaps and its exact size, so nobody has to set it by hand.
    /// </summary>
    public class LoranSpriteImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith("Assets/Game/Resources/Sprites/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.filterMode = FilterMode.Point;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.mipmapEnabled = false;
            t.npotScale = TextureImporterNPOTScale.None;
            t.alphaIsTransparency = true;
            t.wrapMode = TextureWrapMode.Clamp;
            t.isReadable = false;
        }
    }

    /// <summary>
    /// Sprites can arrive in the same update as the import rules above, before those rules are compiled.
    /// So once per version of the rules, every sprite is imported again with them.
    /// </summary>
    [InitializeOnLoad]
    static class LoranSpriteReimport
    {
        const int Version = 1;
        const string Key = "CorruptionOfGaia.SpriteImportVersion";
        const string Folder = "Assets/Game/Resources/Sprites";

        static LoranSpriteReimport()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetInt(Key, 0) >= Version || !AssetDatabase.IsValidFolder(Folder)) return;
                EditorPrefs.SetInt(Key, Version);
                AssetDatabase.ImportAsset(Folder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            };
        }
    }
}
#endif
