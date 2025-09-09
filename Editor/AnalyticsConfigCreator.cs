using UnityEditor;
using UnityEngine;
using System.IO;

public static class AnalyticsConfigCreator
{
    private const string ResourcesDir = "Assets/Resources/TrueSoft";
    private const string AssetPath = ResourcesDir + "/AnalyticsSdkConfig.asset";

    [MenuItem("TrueSoft/Analytics/Select Config Asset")]
    public static void CreateOrSelect()
    {
        Directory.CreateDirectory(ResourcesDir);

        var asset = AssetDatabase.LoadAssetAtPath<AnalyticsSdkConfig>(AssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<AnalyticsSdkConfig>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Analytics] Created config at: {AssetPath}");
        }

        Selection.activeObject = asset;
    }

    // 선택) 에디터 로드시 자동으로 없으면 생성
    [InitializeOnLoadMethod]
    private static void EnsureExistsOnLoad()
    {
        if (!File.Exists(AssetPath))
        {
            // 자동 생성이 싫으면 주석 처리하세요.
            CreateOrSelect();
        }
    }
}