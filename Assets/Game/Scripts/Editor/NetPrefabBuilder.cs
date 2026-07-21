using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates the two NetworkObject prefabs the runtime needs. NGO can only spawn
/// registered prefab assets (both peers must agree on a GlobalObjectIdHash, which
/// comes from the asset GUID), so this is the one place the "everything is built in
/// code" rule bends: the prefabs are bare shells — the blocky visual body is still
/// attached at runtime per peer. Auto-runs on editor load if the assets are missing.
/// </summary>
[InitializeOnLoad]
public static class NetPrefabBuilder
{
    const string Dir = "Assets/Game/Resources/Net";
    const string CharacterPath = Dir + "/NetCharacter.prefab";
    const string MatchPath = Dir + "/NetMatch.prefab";

    static NetPrefabBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath) == null
                || AssetDatabase.LoadAssetAtPath<GameObject>(MatchPath) == null)
                Rebuild();
        };
    }

    [MenuItem("Tools/Net/Rebuild Net Prefabs")]
    public static void Rebuild()
    {
        if (!AssetDatabase.IsValidFolder(Dir))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Resources"))
                AssetDatabase.CreateFolder("Assets/Game", "Resources");
            AssetDatabase.CreateFolder("Assets/Game/Resources", "Net");
        }

        // --- NetCharacter: root shell only; Character.AttachVisual adds the body ---
        var ch = new GameObject("NetCharacter");
        try
        {
            var cc = ch.AddComponent<CharacterController>();
            cc.radius = 0.24f;
            cc.height = 1.35f;
            cc.center = new Vector3(0f, 0.675f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.32f;

            ch.AddComponent<CharacterMotor>();
            ch.AddComponent<Character>();
            ch.AddComponent<NetworkObject>();
            var nt = ch.AddComponent<OwnerNetworkTransform>();
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false; // poses scale the body child, never the root
            ch.AddComponent<CharacterNetSync>();
            PrefabUtility.SaveAsPrefabAsset(ch, CharacterPath);
        }
        finally { Object.DestroyImmediate(ch); }

        // --- NetMatch: replicated match state ---
        var m = new GameObject("NetMatch");
        try
        {
            m.AddComponent<NetworkObject>();
            m.AddComponent<MatchNet>();
            PrefabUtility.SaveAsPrefabAsset(m, MatchPath);
        }
        finally { Object.DestroyImmediate(m); }

        AssetDatabase.SaveAssets();
        Debug.Log("NetPrefabBuilder: rebuilt " + CharacterPath + " and " + MatchPath);
    }
}
