using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlipSkid.MapKit.Editor
{
    // The game renders with URP. Without a URP asset assigned, URP shaders built into a map
    // bundle are stripped and anything using your own materials shows up invisible.
    [InitializeOnLoad]
    public static class URPSetup
    {
        private const string Folder = "Assets/SlipSkid/Rendering";

        static URPSetup()
        {
            EditorApplication.delayCall += Ensure;
        }

        public static void Ensure()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset) return;

            string assetPath = $"{Folder}/SlipSkidURP.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                System.IO.Directory.CreateDirectory(Folder);
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, $"{Folder}/SlipSkidURP_Renderer.asset");
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;
            AssetDatabase.SaveAssets();
            Debug.Log("[Slip & Skid] URP asset assigned: " + assetPath);
        }
    }
}
