// Compile-check stubs for the UnityEditor API used by Assets/Scripts/Editor.
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
        public MenuItem(string itemName, bool isValidateFunction) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }
        public int priority;
    }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute { }

    public static class EditorApplication
    {
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall;
        public static bool isPlayingOrWillChangePlaymode => false;
        public static bool isPlaying { get; set; }
    }
    public static class EditorPrefs
    {
        public static bool GetBool(string key, bool defaultValue) => defaultValue;
        public static void SetBool(string key, bool value) { }
    }
    public static class EditorUtility
    {
        public static bool DisplayDialog(string title, string message, string ok) => true;
        public static bool DisplayDialog(string title, string message, string ok, string cancel) => true;
        public static void RevealInFinder(string path) { }
    }
    [Flags] public enum ImportAssetOptions { Default = 0, ForceUpdate = 1, ForceSynchronousImport = 8, ImportRecursive = 256 }
    public static class AssetDatabase
    {
        public static void SaveAssets() { }
        public static void ImportAsset(string path, ImportAssetOptions options) { }
    }
    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { }
    }
    public static class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes { get; set; } }
    public static class PlayerSettings
    {
        public static string productName { get; set; }
        public static string companyName { get; set; }
        public static int defaultScreenWidth { get; set; }
        public static int defaultScreenHeight { get; set; }
        public static FullScreenMode fullScreenMode { get; set; }
        public static bool resizableWindow { get; set; }
        public static bool runInBackground { get; set; }
        public static ColorSpace colorSpace { get; set; }
    }
    public enum BuildTarget { StandaloneWindows64 = 19 }
    [Flags] public enum BuildOptions { None = 0 }
    public struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public BuildTarget target { get; set; }
        public BuildOptions options { get; set; }
    }
    public static class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) => null;
    }
    public class AssetImporter : UnityEngine.Object { }
    public enum TextureImporterType { Default = 0, Sprite = 8 }
    public enum SpriteImportMode { None, Single, Multiple, Polygon }
    public enum TextureImporterCompression { Uncompressed, Compressed, CompressedHQ, CompressedLQ }
    public class TextureImporter : AssetImporter
    {
        public TextureImporterType textureType { get; set; }
        public SpriteImportMode spriteImportMode { get; set; }
        public float spritePixelsPerUnit { get; set; }
        public FilterMode filterMode { get; set; }
        public TextureImporterCompression textureCompression { get; set; }
        public bool mipmapEnabled { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public bool alphaIsTransparency { get; set; }
        public int maxTextureSize { get; set; }
        public bool isReadable { get; set; }
    }
    public struct AudioImporterSampleSettings
    {
        public AudioClipLoadType loadType;
        public AudioCompressionFormat compressionFormat;
        public float quality;
    }
    public class AudioImporter : AssetImporter
    {
        public AudioImporterSampleSettings defaultSampleSettings { get; set; }
        public bool forceToMono { get; set; }
    }
    public class AssetPostprocessor
    {
        public string assetPath { get; }
        public AssetImporter assetImporter { get; }
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }
    public struct BuildSummary { public BuildResult result { get; } }
    public class BuildReport { public BuildSummary summary { get; } }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }
    public sealed class EditorSceneManager : SceneManager
    {
        public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
        public static bool SaveScene(Scene scene, string dstScenePath) => true;
        public static Scene OpenScene(string scenePath) => default;
    }
}
