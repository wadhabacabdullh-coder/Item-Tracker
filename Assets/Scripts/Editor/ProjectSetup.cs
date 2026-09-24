using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShadowContract.EditorTools
{
    /// <summary>
    /// One-click project setup (runs automatically the first time the project is opened):
    /// creates the (empty) main scene, adds it to the build, and sets player settings.
    /// The game builds itself from code at runtime, so the scene needs no objects.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string DoneKey = "ShadowContract.SetupDone.v1";

        static ProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(DoneKey + Application.dataPath, false) && File.Exists(ScenePath)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                Setup(false);
            };
        }

        [MenuItem("Shadow Contract/Setup Project", priority = 0)]
        public static void SetupMenu() => Setup(true);

        public static void Setup(bool verbose)
        {
            // Generated art/audio may have been imported before this editor code compiled; re-import so the
            // pixel-art settings from ShadowContractImport apply (point filtering, no compression, 16 PPU).
            AssetDatabase.ImportAsset("Assets/Resources/Sprites", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/Resources/Audio", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            else if (EditorSceneManager.GetActiveScene().path != ScenePath && !EditorSceneManager.GetActiveScene().isDirty)
            {
                EditorSceneManager.OpenScene(ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.productName = "Shadow Contract";
            PlayerSettings.companyName = "Shadow Contract";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.colorSpace = ColorSpace.Gamma; // pixel art colours exactly as authored

            EditorPrefs.SetBool(DoneKey + Application.dataPath, true);
            AssetDatabase.SaveAssets();
            if (verbose)
                EditorUtility.DisplayDialog("Shadow Contract", "Project is set up.\n\nPress Play to start the game, or use Shadow Contract > Build Windows to make an .exe.", "OK");
            else
                Debug.Log("[Shadow Contract] Project set up: open Assets/Scenes/Main.unity and press Play.");
        }

        [MenuItem("Shadow Contract/Build Windows (.exe)", priority = 20)]
        public static void BuildWindows()
        {
            Setup(false);
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "Windows");
            Directory.CreateDirectory(dir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(dir, "ShadowContract.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorUtility.RevealInFinder(options.locationPathName);
                Debug.Log("[Shadow Contract] Build succeeded: " + options.locationPathName);
            }
            else Debug.LogError("[Shadow Contract] Build failed: " + report.summary.result);
        }

        [MenuItem("Shadow Contract/Reimport Generated Assets", priority = 30)]
        public static void Reimport()
        {
            AssetDatabase.ImportAsset("Assets/Resources", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }

        [MenuItem("Shadow Contract/Open Save Folder", priority = 40)]
        public static void OpenSaves() => EditorUtility.RevealInFinder(Application.persistentDataPath);

        [MenuItem("Shadow Contract/Delete Save Data", priority = 41)]
        public static void DeleteSaves()
        {
            if (!EditorUtility.DisplayDialog("Delete save data", "Erase all progress and settings?", "Delete", "Cancel")) return;
            foreach (var f in new[] { "progress.json", "progress.json.bak", "settings.json", "settings.json.bak" })
            {
                string p = Path.Combine(Application.persistentDataPath, f);
                if (File.Exists(p)) File.Delete(p);
            }
        }

        [MenuItem("Shadow Contract/Unlock Everything (debug)", priority = 42)]
        public static void UnlockAll()
        {
            string path = Path.Combine(Application.persistentDataPath, "progress.json");
            var p = ShadowContract.Core.SaveStore.Load<ShadowContract.Core.ProgressData>(path, out _);
            p.Money += 50000;
            foreach (var m in ShadowContract.Core.MissionCatalog.All)
            {
                if (!p.UnlockedMissions.Contains(m.Id)) p.UnlockedMissions.Add(m.Id);
                if (!p.CompletedMissions.Contains(m.Id)) p.CompletedMissions.Add(m.Id);
            }
            ShadowContract.Core.SaveStore.Save(path, p);
            Debug.Log("[Shadow Contract] All contracts unlocked, +$50,000 (restart Play mode to see it).");
        }
    }

    /// <summary>Import settings for generated assets: crisp pixel art and sensible audio compression.</summary>
    public sealed class ShadowContractImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/Sprites/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 16;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaIsTransparency = true;
            ti.maxTextureSize = 4096;
            ti.isReadable = false;
        }

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Resources/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            bool longClip = assetPath.Contains("/Music/") || assetPath.Contains("/Ambience/");
            var s = ai.defaultSampleSettings;
            s.loadType = longClip ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = longClip ? 0.6f : 0.8f;
            ai.defaultSampleSettings = s;
            ai.forceToMono = true;
        }
    }
}
