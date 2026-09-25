using System;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace ExtendedBosses
{
    // Live config without ConfigurationManager - for a dedicated server above all: saving
    // j1ga.extendedbosses.cfg re-reads it within a second, and the server sends the new values to
    // every client with the mod (08 Sync). The watcher fires on a thread-pool thread, so it only
    // raises a flag; the reload runs on the main thread, debounced (editors write in several
    // steps). Auto-save is off during the reload, so re-read values are never written back and
    // cannot trigger the watcher again.
    public partial class ExtendedBossesPlugin
    {
        private ConfigEntry<bool> _cfgWatchFile;
        private FileSystemWatcher _watcher;
        private volatile bool _fileChanged;
        private float _reloadAt;
        private const float ReloadDebounce = 1f;
        private float _lastSettingChange = -100f;   // realtime of the last in-game change (settings window, sync) - its own save is not an edit
        private bool _reloading;                    // SettingChanged raised by our own Reload is not an in-game change

        private void BindWatchConfig()
        {
            _cfgWatchFile = B("08 Sync", "WatchConfigFile", true,
                "Re-read this file as soon as it is saved (no restart; meant for a dedicated server, where there is no settings window). On the server the new values are sent to every client with the mod.",
                "Перечитывать этот файл сразу после сохранения (без перезапуска; для выделенного сервера, где нет окна настроек). На сервере новые значения сразу рассылаются всем клиентам с модом.");
        }

        private void StartWatcher()
        {
            try
            {
                string path = Config.ConfigFilePath;
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                _watcher = new FileSystemWatcher(dir, Path.GetFileName(path));
                _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime;
                _watcher.Changed += OnConfigFileEvent;
                _watcher.Created += OnConfigFileEvent;
                _watcher.Renamed += OnConfigFileEvent;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Logger.LogWarning("Config file watcher not available (" + e.Message + "); config changes need a restart.");
                _watcher = null;
            }
        }

        private void StopWatcher()
        {
            if (_watcher == null) return;
            try { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); }
            catch { }
            _watcher = null;
        }

        // thread-pool thread: only a flag
        private void OnConfigFileEvent(object sender, FileSystemEventArgs e)
        {
            _fileChanged = true;
        }

        // main thread, from Update
        private void TickConfigWatch(float now)
        {
            if (_fileChanged)
            {
                _fileChanged = false;
                _reloadAt = now + ReloadDebounce;
            }
            if (_reloadAt <= 0f || now < _reloadAt) return;
            _reloadAt = 0f;
            if (!_cfgWatchFile.Value) return;
            if (Time.realtimeSinceStartup - _lastSettingChange < ReloadDebounce + 2f) return;   // the mod saved it itself

            bool save = Config.SaveOnConfigSet;
            try
            {
                Config.SaveOnConfigSet = false;
                _reloading = true;
                Config.Reload();
            }
            catch (Exception e) { Logger.LogWarning("Config reload failed: " + e.Message); return; }
            finally { Config.SaveOnConfigSet = save; _reloading = false; }

            _syncDirty = true;          // the server sends the new values to clients with the mod
            ZNet znet = ZNet.instance;
            Logger.LogInfo("Config reloaded from file" + (znet != null && znet.IsServer() && _cfgSync.Value ? "; sending to clients with the mod." : "."));
        }
    }
}
