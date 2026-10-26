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
            string path = assetPath.Replace('\\', '/');
            if (path.StartsWith("Assets/Game/Resources/Sprites/HD/"))
            {
                // pictures rendered in 3D: smooth, mipmapped, big, high-quality compression
                var h = (TextureImporter)assetImporter;
                h.textureType = TextureImporterType.Default;
                h.filterMode = FilterMode.Trilinear;
                h.mipmapEnabled = true;
                h.textureCompression = TextureImporterCompression.CompressedHQ;
                h.npotScale = TextureImporterNPOTScale.None;
                h.alphaIsTransparency = true;
                h.wrapMode = TextureWrapMode.Clamp;
                h.maxTextureSize = 8192;
                h.isReadable = false;
                return;
            }
            if (!path.StartsWith("Assets/Game/Resources/Sprites/")) return;
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
        const int Version = 2;
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
