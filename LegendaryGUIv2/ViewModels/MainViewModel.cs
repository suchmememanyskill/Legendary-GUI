﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LegendaryMapperV2.Service;
using System.Threading.Tasks;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Threading;
using LegendaryGUIv2.Services;
using LegendaryGUI.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using System.Diagnostics;
using Avalonia.Threading;
using System.Reactive;
using System.Reactive.Linq;

namespace LegendaryGUIv2.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private LegendaryAuth auth;
        private LegendaryGameManager manager;
        private MainWindowViewModel window;
        public LegendaryGameManager Manager { get => manager; }
        private Thread? imageDownloadThread;
        private bool stopImageDownloadThread = false;

        public MainViewModel(LegendaryAuth auth, MainWindowViewModel window)
        {
            this.auth = auth;
            this.window = window;
            manager = new(auth, x => OnLibraryRefresh());
            manager.GetGames();
            SetDownloadLocationText();

            this.WhenAnyValue(x => x.SearchBoxText)
                .Throttle(TimeSpan.FromMilliseconds(200))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(HandleSearchEvent!);
        }

        public void OnPathChange() => SetViewOnWindow(new ChangeGameFolderViewModel(this));
        public void SetViewOnWindow(ViewModelBase view) => window.SetViewModel(view);

        public void RefreshLibrary() => manager.GetGames();

        public void OnLibraryRefresh()
        {

            if (imageDownloadThread != null && imageDownloadThread.IsAlive)
            {
                stopImageDownloadThread = true;
                imageDownloadThread.Join();
            }

            GameCountText = $"Found {manager.Games.Count} games, {manager.InstalledGames.Count} installed";
            allInstalled = new(manager.InstalledGames.Select(x => new GameViewModel(x)));
            allNotInstalled = new(manager.NotInstalledGames.Select(x => new GameViewModel(x)));

            List<GameViewModel> transferList = new();
            manager.Downloads.ForEach(x =>
            {
                if (allInstalled.Any(y => x.Game.AppName == y.Game.AppName))
                    allInstalled.First(y => x.Game.AppName == y.Game.AppName).ApplyDownload(x);
                else if (allNotInstalled.Any(y => x.Game.AppName == y.Game.AppName))
                {
                    GameViewModel model = allNotInstalled.First(y => x.Game.AppName == y.Game.AppName);
                    transferList.Add(model);
                    model.ApplyDownload(x);
                }
            });

            int i = 0;
            transferList.ForEach(x =>
            {
                allInstalled.Insert(i++, x);
                allNotInstalled.Remove(x);
            });

            Installed = allInstalled;
            NotInstalled = allNotInstalled;

            stopImageDownloadThread = false;
            imageDownloadThread = new(DownloadAllImages);
            imageDownloadThread.IsBackground = true;
            imageDownloadThread.Start();
        }

        public void BtnUpdateSteamGames()
        {
            SteamManager m = new SteamManager();
            if (!m.Read())
            {
                Utils.CreateMessageBox("Fatal error!", $"Failure reading {m.VdfPath}").Show();
                return;
            }
            Tuple<int, int> res = m.UpdateWithLegendaryGameList(manager.InstalledGames);
            m.Write();

            Utils.CreateMessageBox("Steam games updated", $"Removed {res.Item1}, Added {res.Item2} on Steam with '(Epic)' in name\nPlease restart steam for changes to take effect").Show();
        }

        public void BtnRemoveSteamGames()
        {
            SteamManager m = new SteamManager();
            if (!m.Read())
            {
                Utils.CreateMessageBox("Fatal error!", $"Failure reading {m.VdfPath}").Show();
                return;
            }
            int count = m.RemoveAllGamesWithTag();

            if (count != 0)
                m.Write();

            Utils.CreateMessageBox("Steam games removed", $"Removed {count} on Steam with '(Epic)' in name\nPlease restart steam for changes to take effect").Show();
        }

        private Thread searcher;

        public async void HandleSearchEvent(string search)
        {
            if (string.IsNullOrWhiteSpace(SearchBoxText))
            {
                Installed = allInstalled;
                NotInstalled = allNotInstalled;
            }
                
            else
            {
                ObservableCollection < GameViewModel > a = new(allInstalled.Where(x => x.Game.AppTitle.Contains(SearchBoxText, StringComparison.OrdinalIgnoreCase)));
                ObservableCollection < GameViewModel > b = new(allNotInstalled.Where(x => x.Game.AppTitle.Contains(SearchBoxText, StringComparison.OrdinalIgnoreCase)));;
                Installed = a;
                NotInstalled = b;
            }
        }

        private void DownloadAllImages()
        {
            foreach (GameViewModel model in Installed.Concat(NotInstalled))
            {
                if (stopImageDownloadThread)
                    return;

                try
                {
                    model.DownloadImages();
                }
                catch { }
            }
        }

        public void SetDownloadLocationText()
        {
            if (manager.GameDirectory != "")
                DownloadLocation = $"Download location: {manager.GameDirectory}";
            else
                DownloadLocation = $"Download location: ~/Legendary";
        }

        private string gameCountText = "";
        public string GameCountText { get => gameCountText; set => this.RaiseAndSetIfChanged(ref gameCountText, value); }

        private string downloadLocation = "";
        public string DownloadLocation { get => downloadLocation; set => this.RaiseAndSetIfChanged(ref downloadLocation, value); }

        private ObservableCollection<GameViewModel> installed = new(), allInstalled = new();
        public ObservableCollection<GameViewModel> Installed { get => installed; set => this.RaiseAndSetIfChanged(ref installed, value); }
        private ObservableCollection<GameViewModel> notInstalled = new(), allNotInstalled = new();
        public ObservableCollection<GameViewModel> NotInstalled { get => notInstalled; set => this.RaiseAndSetIfChanged(ref notInstalled, value); }
        private GameViewModel? selectedGameInstalled, selectedGameNotInstalled;
        public GameViewModel? SelectedGameInstalled { 
            get => selectedGameInstalled; 
            set {
                selectedGameInstalled?.Unselect();
                this.RaiseAndSetIfChanged(ref selectedGameInstalled, value);
                selectedGameInstalled?.Select();
            }
        }
        public GameViewModel? SelectedGameNotInstalled
        {
            get => selectedGameNotInstalled;
            set
            {
                selectedGameNotInstalled?.Unselect();
                this.RaiseAndSetIfChanged(ref selectedGameNotInstalled, value);
                selectedGameNotInstalled?.Select();
            }
        }

        private string searchBoxText;
        public string SearchBoxText { get => searchBoxText; set
            {
                this.RaiseAndSetIfChanged(ref searchBoxText, value);
                //HandleSearchEvent();
            } }
    }
}
