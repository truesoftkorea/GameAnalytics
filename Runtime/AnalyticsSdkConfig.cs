using UnityEngine;

[CreateAssetMenu(fileName = "AnalyticsSdkConfig", menuName = "TrueSoft/Analytics SDK Config")]
public class AnalyticsSdkConfig : ScriptableObject
{
    [Header("Android Only")]
    //public bool enableInstallReferrer = true;
    public bool debugMode = false;
}