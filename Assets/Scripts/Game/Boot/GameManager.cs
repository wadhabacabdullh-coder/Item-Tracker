using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShadowContract.Core;
using UnityEngine;

namespace ShadowContract.Game
{
    /// <summary>
    /// Creates the game at startup (no scene setup required) — see <see cref="GameManager"/>.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (GameManager.I != null) return;
            var go = new GameObject("ShadowContract");
            go.AddComponent<GameManager>();
        }
    }

    /// <summary>
    /// Top-level game flow: Main Menu -> Contracts -> (Loadout / Shop) -> Mission -> Results -> Shop -> next contract.
    /// Owns the save data, settings, persistent services and all menu screens.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        public ProgressData Progress { get; private set; }
        public SettingsData Settings { get; private set; }
        public MissionRunner Runner { get; private set; }
        public string PendingMissionId { get; private set; }
        public string LastMissionId { get; private set; }
        /// <summary>Frame of the last pause/resume/inventory toggle, so one key press is never handled twice.</summary>
        public int UiToggleFrame { get; private set; } = -1;

        private string _progressPath, _settingsPath;
        private RectTransform _uiRoot;
        private HUD _hud;
        private readonly List<UIScreen> _screens = new List<UIScreen>();
        private MainMenuScreen _main;
        private MissionSelectScreen _select;
        private LoadoutScreen _loadout;
        private ShopScreen _shop;
        private SettingsScreen _settings;
        private PauseScreen _pause;
        private InventoryScreen _inventory;
        private MissionCompleteScreen _complete;
        private GameOverScreen _gameOver;
        private UIScreen _beforeShop;
        private Transform _missionRoot;

        private void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 144;
            SpriteLibrary.KeepFontsCrisp();

            _progressPath = Path.Combine(Application.persistentDataPath, "progress.json");
            _settingsPath = Path.Combine(Application.persistentDataPath, "settings.json");
            Progress = SaveStore.Load<ProgressData>(_progressPath, out bool recovered);
            Progress.Sanitize();
            if (recovered) Debug.LogWarning("Progress file was damaged; restored from backup.");
            Settings = SaveStore.Load<SettingsData>(_settingsPath, out _);
            Settings.Sanitize();

            // Services
            var cam = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera));
            cam.tag = "MainCamera";
            DontDestroyOnLoad(cam);
            if (cam.GetComponent<CameraRig>() == null) cam.AddComponent<CameraRig>();
            var al = cam.GetComponent<AudioListener>();
            if (al == null) cam.AddComponent<AudioListener>();
            gameObject.AddComponent<AudioManager>();
            gameObject.AddComponent<MusicDirector>();
            gameObject.AddComponent<FxManager>();
            _missionRoot = new GameObject("Mission").transform;
            _missionRoot.SetParent(transform, false);

            // UI
            UI.EnsureEventSystem();
            _hud = new GameObject("HUDRoot").AddComponent<HUD>();
            _hud.transform.SetParent(transform, false);
            _hud.Build();
            var canvas = UI.CreateCanvas("Menus", 20, transform);
            _uiRoot = (RectTransform)canvas.transform;
            _main = AddScreen<MainMenuScreen>();
            _select = AddScreen<MissionSelectScreen>();
            _loadout = AddScreen<LoadoutScreen>();
            _shop = AddScreen<ShopScreen>();
            _settings = AddScreen<SettingsScreen>();
            _pause = AddScreen<PauseScreen>();
            _inventory = AddScreen<InventoryScreen>();
            _complete = AddScreen<MissionCompleteScreen>();
            _gameOver = AddScreen<GameOverScreen>();

            ApplySettings();
            ShowMainMenu();
        }

        private T AddScreen<T>() where T : UIScreen
        {
            var s = gameObject.AddComponent<T>();
            s.Init(_uiRoot);
            _screens.Add(s);
            return s;
        }

        private void ShowOnly(UIScreen screen)
        {
            foreach (var s in _screens) if (s != screen) s.Hide();
            screen?.Show();
            Cursor.visible = true;
        }

        private UIScreen Visible => _screens.LastOrDefault(s => s.Visible);

        private void Update()
        {
            var v = Visible;
            if (v == null) return;
            if (v == _settings && _settings.Capturing) return;
            if (UiToggleFrame == Time.frameCount) return;
            if (v == _inventory && GameInput.Pressed(GameAction.Inventory)) { ToggleInventory(); return; }
            if (GameInput.Pressed(GameAction.Pause)) v.Back();
        }

        private void OnApplicationQuit()
        {
            SaveProgress();
            SaveSettings();
        }

        // ------------------------------------------------------------------ persistence

        public void SaveProgress()
        {
            try { SaveStore.Save(_progressPath, Progress); }
            catch (Exception e) { Debug.LogError("Could not save progress: " + e.Message); }
        }

        public void SaveSettings()
        {
            try { SaveStore.Save(_settingsPath, Settings); }
            catch (Exception e) { Debug.LogError("Could not save settings: " + e.Message); }
        }

        public void ResetProgress()
        {
            Progress = new ProgressData();
            Progress.Sanitize();
            SaveProgress();
        }

        public void ApplySettings()
        {
            var s = Settings;
            GameInput.Load(s.Bindings);
            AudioManager.I.MasterVolume = s.MasterVolume;
            AudioManager.I.SfxVolume = s.SfxVolume;
            MusicDirector.I.MasterVolume = s.MasterVolume;
            MusicDirector.I.MusicVolume = s.MusicVolume;
            CameraRig.I.ShakeEnabled = s.ScreenShake;
            var mode = s.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.fullScreenMode != mode)
            {
                if (s.Fullscreen) Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, mode);
                else Screen.SetResolution(1600, 900, mode);
            }
            if (Runner != null && Runner.View != null) Runner.View.Cones.Visible = s.ShowVisionCones;
        }

        // ------------------------------------------------------------------ navigation

        private void EndMissionWorld()
        {
            if (Runner != null)
            {
                Runner.enabled = false; // Destroy is deferred to the end of the frame
                Destroy(Runner.gameObject);
                Runner = null;
            }
            Time.timeScale = 1f;
            _hud.Show(false);
            FxManager.I.Clear();
            AudioManager.I.StopAll();
            CameraRig.I.Snap(Vector2.zero);
        }

        public void ShowMainMenu()
        {
            EndMissionWorld();
            MusicDirector.I.PlayMenu();
            ShowOnly(_main);
        }

        public void ShowMissionSelect()
        {
            EndMissionWorld();
            MusicDirector.I.PlayMenu();
            PendingMissionId = null;
            ShowOnly(_select);
        }

        public void ShowLoadout(string missionId)
        {
            EndMissionWorld();
            PendingMissionId = missionId;
            _loadout.SetMission(missionId);
            ShowOnly(_loadout);
        }

        public void ShowShop()
        {
            var from = Visible;
            if (from != _shop) _beforeShop = from;
            EndMissionWorld();
            MusicDirector.I.PlayMenu();
            ShowOnly(_shop);
        }

        public void ShowPreviousFromShop()
        {
            if (_beforeShop == _loadout) ShowLoadout(PendingMissionId);
            else if (_beforeShop == _select || _beforeShop == _complete) ShowMissionSelect();
            else ShowMainMenu();
        }

        public void ShowSettings(Action onBack = null)
        {
            _settings.SetReturn(onBack);
            ShowOnly(_settings);
        }

        public void Quit()
        {
            SaveProgress();
            SaveSettings();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ missions

        public void StartMission(string missionId)
        {
            var mission = MissionCatalog.Get(missionId);
            if (mission == null || !Progress.IsUnlocked(missionId)) return;
            EndMissionWorld();
            var text = Resources.Load<TextAsset>("Maps/" + mission.MapId);
            if (text == null) { Debug.LogError("Missing map " + mission.MapId); return; }
            MapData map;
            try { map = MapData.Parse(text.text); }
            catch (Exception e) { Debug.LogError("Map failed to load: " + e.Message); return; }

            LastMissionId = missionId;
            PendingMissionId = missionId;
            foreach (var s in _screens) s.Hide();
            Runner = new GameObject("Mission_" + missionId).AddComponent<MissionRunner>();
            Runner.transform.SetParent(_missionRoot, false);
            _hud.Show(true);
            Runner.Begin(mission, map, Progress.BuildLoadout(), Settings, _hud);
            Cursor.visible = false;
        }

        public void PauseMission()
        {
            UiToggleFrame = Time.frameCount;
            if (Runner == null || Runner.Ended) return;
            Runner.Paused = true;
            Time.timeScale = 0f;
            ShowOnly(_pause);
        }

        public void ResumeMission()
        {
            UiToggleFrame = Time.frameCount;
            if (Runner == null) { ShowMainMenu(); return; }
            foreach (var s in _screens) s.Hide();
            Runner.Paused = false;
            Time.timeScale = 1f;
            Cursor.visible = false;
            _hud.Show(true);
        }

        public void ToggleInventory()
        {
            UiToggleFrame = Time.frameCount;
            if (Runner == null) return;
            if (_inventory.Visible) { ResumeMission(); return; }
            Runner.Paused = true;
            Time.timeScale = 0f;
            ShowOnly(_inventory);
        }

        public void RestartMission()
        {
            if (LastMissionId != null) StartMission(LastMissionId);
        }

        public void AbandonMission()
        {
            ShowMissionSelect();
        }

        public void OnMissionEnded(MissionRunner runner)
        {
            var s = runner.Session;
            var result = s.BuildResult();
            var unlockedBefore = new HashSet<string>(Progress.UnlockedMissions);
            var shopBefore = ShopService.Catalog.Where(i => ShopService.IsUnlocked(Progress, i)).Select(i => i.Id).ToList();
            Progress.ApplyResult(result);
            SaveProgress();
            _hud.Show(false);
            Cursor.visible = true;
            if (result.Success)
            {
                var news = new List<string>();
                foreach (var m in Progress.UnlockedMissions)
                    if (!unlockedBefore.Contains(m)) news.Add("contract \"" + MissionCatalog.Get(m).Name + "\"");
                foreach (var i in ShopService.Catalog)
                    if (ShopService.IsUnlocked(Progress, i) && !shopBefore.Contains(i.Id) && i.Kind == ShopItemKind.Weapon) news.Add(i.Name);
                foreach (var sc in _screens) sc.Hide();
                _complete.Show(result, s.Mission, news.ToArray());
            }
            else
            {
                foreach (var sc in _screens) sc.Hide();
                _gameOver.Show(result, s.State == MissionState.Dead);
            }
        }
    }
}
