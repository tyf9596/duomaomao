// Import settings for the generated INK&PAINT UI art (Assets/Game/Resources/UI/**):
// everything is a white tintable sprite; the named nine-slice borders come from the
// design delivery spec (MecchaChameleon-UI-Spec, section 4).
using System.IO;
using UnityEditor;
using UnityEngine;

public class UiArtImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.Contains("Assets/Game/Resources/UI/")) return;

        var imp = (TextureImporter)assetImporter;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.mipmapEnabled = false;
        imp.filterMode = FilterMode.Bilinear;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.alphaIsTransparency = true;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.spritePixelsPerUnit = 100;
        imp.wrapMode = TextureWrapMode.Clamp;

        string name = Path.GetFileNameWithoutExtension(p);
        switch (name)
        {
            case "panel-round-32": imp.spriteBorder = new Vector4(48, 48, 48, 48); break;
            case "card-cream-24": imp.spriteBorder = new Vector4(36, 36, 36, 36); break;
            case "chip-pill": imp.spriteBorder = new Vector4(30, 30, 30, 30); break;
            case "tile-round-12": imp.spriteBorder = new Vector4(24, 24, 24, 24); break;
            case "stripe-warn-tile": imp.wrapMode = TextureWrapMode.Repeat; break;
        }
    }
}
