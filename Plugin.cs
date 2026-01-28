using BepInEx;
using BepInEx.Configuration;
using GorillaGameModes;
using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;
using BananaOS;
using BananaOS.Pages;
// to lazy to make a separate file for stuff lmao
namespace LMSMusic
{
    [BepInPlugin("gull.lms.music", "Last Monke Standing", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        private AudioSource audioSource;
        private AudioClip clip;
        private Coroutine fadeRoutine;
        private string musicFolder; // dont touch will break if u do 
        private string currentTrack;
        private bool isPlaying;
        private bool hasPlayedThisRound; // dont touch will break if u do 
        private bool wasLastSurvivor;
        private string logFilePath;
        private StreamWriter logWriter;
        public ConfigEntry<float> MusicVolume; // dont touch will break if u do 
        void Awake()
        {
            Instance = this;
            SetupLogger();
            Log("Plugin Awake");
            // setup volume config
            MusicVolume = Config.Bind("Settings", "MusicVolume", 1f,
                new ConfigDescription("Volume of LMS Music (0.0 to 1.0)"));
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.volume = MusicVolume.Value; // use saved volume
            audioSource.spatialBlend = 0f;
            SetupFolder();
        }
        void Start()
        {
            GorillaTagger.OnPlayerSpawned(Init);
            // setup folder and audio
            SetupFolder();
            // audioSource already created in Awake
            SetupAudio();
            Log("Plugin started — LMSMusicPage will be auto-detected by BananaOS");
        }
        private void SetupAudio()
        {
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.playOnAwake = false;
            audioSource.volume = MusicVolume.Value;
            audioSource.spatialBlend = 0f;
            Log("AudioSource initialized");
        }
        private void Init()
        {
            Log("Player spawned, subscribing to room events");
            NetworkSystem.Instance.OnJoinedRoomEvent += OnJoinedRoom;
            NetworkSystem.Instance.OnReturnedToSinglePlayer += OnLeftRoom;
        }
        private void SetupFolder()
        {
            musicFolder = Path.Combine(Paths.PluginPath, "LMSMUSIC");
            Directory.CreateDirectory(musicFolder);
            Log($"Music folder set to: {musicFolder}");
            var files = Directory.GetFiles(musicFolder, "*.wav");
            Log($"Detected {files.Length} wav files");
        }
        private void OnJoinedRoom()
        {
            Log("Joined room");
            hasPlayedThisRound = false;
            wasLastSurvivor = false;
        }
        private void OnLeftRoom()
        {
            Log("Left room");
            StopMusicImmediate();
            hasPlayedThisRound = false;
            wasLastSurvivor = false;
        }
        void Update()
        {
            if (!PhotonNetwork.InRoom || GorillaGameManager.instance == null)
                return;

            bool localInfected = IsLocalPlayerInfected();
bool lastSurvivor = IsLastSurvivor();
if (PhotonNetwork.PlayerList.Length >= 4)
{
    if (lastSurvivor && !localInfected && !hasPlayedThisRound)
    {
        Log("Last survivor detected (network check)");
        hasPlayedThisRound = true;
        wasLastSurvivor = true;
        PlayRandomMusic();
    }
    if (wasLastSurvivor && localInfected)
    {
        Log("Player got tagged (network check), fading out music");
        wasLastSurvivor = false;
        FadeOutMusic();
    }
}
        }
        private bool IsLocalPlayerInfected()
        {
            return GetInfectedPlayers().Contains(NetworkSystem.Instance.LocalPlayer);
        }
        private bool IsLastSurvivor()
        {
            var infected = GetInfectedPlayers();
            int totalPlayers = PhotonNetwork.PlayerList.Length;
            return infected.Count == totalPlayers - 1 &&
                   !infected.Contains(NetworkSystem.Instance.LocalPlayer);
        }
        private List<NetPlayer> GetInfectedPlayers()
        {
            List<NetPlayer> infected = new List<NetPlayer>();
            if (!(GorillaGameManager.instance is GorillaTagManager tag))
                return infected;
            if (tag.isCurrentlyTag)
                infected.Add(tag.currentIt);
            else
                infected.AddRange(tag.currentInfected);
            return infected;
        }
        public string[] GetMusicFiles()
        {
            if (!Directory.Exists(musicFolder))
                return Array.Empty<string>();
            return Directory.GetFiles(musicFolder, "*.wav").Select(Path.GetFileName).ToArray();
        }
        public void TestRandomMusic()
        {
            Log("Test Random Music triggered");
            PlayRandomMusic();
        }
        private void PlayRandomMusic()
        {
            string[] tracks = Directory.GetFiles(musicFolder, "*.wav");
            if (tracks.Length == 0)
            {
                Log("No music files found");
                return;
            }
            string chosen = tracks[UnityEngine.Random.Range(0, tracks.Length)];
            currentTrack = Path.GetFileName(chosen);
            Log($"Selected track: {currentTrack}");
            StartCoroutine(LoadAndPlay(chosen));
        }
        private IEnumerator LoadAndPlay(string path)
        {
            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);
            Log($"Loading audio: {path}");
            using var uwr = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.WAV);
            yield return uwr.SendWebRequest();
            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Log($"ERROR loading audio: {uwr.error}");
                yield break;
            }
            clip = DownloadHandlerAudioClip.GetContent(uwr);
            audioSource.clip = clip;
            audioSource.volume = MusicVolume.Value;
            audioSource.Play();
            isPlaying = true;
            fadeRoutine = StartCoroutine(FadeAudio(0f, MusicVolume.Value, 3f));
        }
        private void FadeOutMusic()
        {
            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(FadeAudio(audioSource.volume, 0f, 3f, true));
            isPlaying = false;
        }
        private IEnumerator FadeAudio(float from, float to, float duration, bool stopAfter = false)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                audioSource.volume = Mathf.Lerp(from, to, t / duration);
                yield return null;
            }
            audioSource.volume = to;
            if (stopAfter)
            {
                audioSource.Stop();
                audioSource.clip = null;
                Log("Audio stopped");
            }
        }
        private void StopMusicImmediate()
        {
            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);
            audioSource.Stop();
            audioSource.volume = 0f;
            audioSource.clip = null;
            isPlaying = false;
            Log("Music stopped immediately");
        }
        public void SetVolume(float volume)
        {
            MusicVolume.Value = Mathf.Clamp01(volume);
            if (audioSource != null)
                audioSource.volume = MusicVolume.Value;
            Log($"Volume set to {MusicVolume.Value}");
        }
        public string CurrentTrack => currentTrack ?? "None";
        private void SetupLogger()
        {
            logFilePath = Path.Combine(Paths.PluginPath, "LMSMusic.log");
            logWriter = new StreamWriter(logFilePath, true) { AutoFlush = true };
        }

        internal void Log(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            logWriter.WriteLine(line);
        }
        void OnDestroy()
        {
            Log("Plugin destroyed");
            logWriter?.Close();
        }
    }
    public class LMSMusicPage : WatchPage
    {
        public override string Title => "Last Monke Standing";
        public override bool DisplayOnMainMenu => true;
        private bool HasMusic => Plugin.Instance.GetMusicFiles().Length > 0;
        private float currentVolume => Plugin.Instance.MusicVolume.Value;
        public override void OnPostModSetup()
        {
            selectionHandler.maxIndex = 2; // dont touch will break if u do 
        }
        public override string OnGetScreenContent()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<color=yellow>== Last Monke Standing ==</color>");
            sb.AppendLine();
            sb.AppendLine("Now Playing:");
            sb.AppendLine(Plugin.Instance.CurrentTrack);
            sb.AppendLine();
            sb.AppendLine($"Volume: {(int)(currentVolume * 100)}%");
            sb.AppendLine(selectionHandler.GetOriginalBananaOSSelectionText(0, "Increase Volume"));
            sb.AppendLine(selectionHandler.GetOriginalBananaOSSelectionText(1, "Decrease Volume"));
            sb.AppendLine();
            if (HasMusic)
                sb.AppendLine(selectionHandler.GetOriginalBananaOSSelectionText(2, "Test Random Music"));
            else
                sb.AppendLine(selectionHandler.GetOriginalBananaOSSelectionText(2, "Open Music Folder"));
            sb.AppendLine();
            sb.AppendLine("Music Files:");
            var files = Plugin.Instance.GetMusicFiles();
            if (files.Length == 0)
            {
                sb.AppendLine("<color=red>No .wav files found</color>");
                sb.AppendLine("<color=red>Add .wav files to:</color>");
                sb.AppendLine("<color=red>BepInEx/plugins/LMSMUSIC</color>");
            }
            else
            {
                foreach (var f in files)
                    sb.AppendLine($"- {f}");
            }

            return sb.ToString();
        }
        public override void OnButtonPressed(WatchButtonType buttonType)
        {
            switch (buttonType)
            {
                case WatchButtonType.Up:
                    selectionHandler.MoveSelectionUp();
                    break;
                case WatchButtonType.Down:
                    selectionHandler.MoveSelectionDown();
                    break;
                case WatchButtonType.Enter:
                    switch (selectionHandler.currentIndex)
                    {
                        case 0: // dont touch will break if u do 
                            Plugin.Instance.SetVolume(currentVolume + 0.1f);
                            break;
                        case 1: // dont touch will break if u do 
                            Plugin.Instance.SetVolume(currentVolume - 0.1f);
                            break;
                        case 2: // dont touch will break if u do 
                            if (HasMusic)
                            {
                                Plugin.Instance.TestRandomMusic();
                                BananaNotifications.DisplayNotification((Plugin.Instance.CurrentTrack), Color.yellow, Color.white, 2f);
                            }
                            else
                                OpenMusicFolder();
                            break;
                    }
                    break;
                case WatchButtonType.Back:
                    ReturnToMainMenu();
                    break;
            }
        }
        private void OpenMusicFolder()
        {
            try
            {
                string folder = Path.Combine(Paths.PluginPath, "LMSMUSIC");
                Process.Start("explorer.exe", folder);
                BananaNotifications.DisplayNotification(
                    "Opened LMSMUSIC folder",
                    Color.green,
                    Color.white,
                    2f
                );
            }
            catch (Exception e)
            {
                Plugin.Instance.Log("ERROR opening music folder: " + e);
                BananaNotifications.DisplayErrorNotification("Failed to open folder");
            }
        }
    }
}
