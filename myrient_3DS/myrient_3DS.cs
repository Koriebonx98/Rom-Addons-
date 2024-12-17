using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;

namespace myrient.Nintendo3DS
{
    public class MyrientNintendo3DSStore : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public override Guid Id { get; } = Guid.Parse("293e9fb0-84ed-45ca-a706-321a173b0a12");
        public override string Name => "myrient.Nintendo3DS";

        public MyrientNintendo3DSStore(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
        }

        private List<string> LoadGames()
        {
            var gamesFilePath = Path.Combine(GetPluginUserDataPath(), "Nintendo3DS.Games.txt");
            if (!File.Exists(gamesFilePath))
            {
                File.WriteAllText(gamesFilePath, string.Empty);
                logger.Error("Nintendo3DS.Games.txt not found. A blank file has been created.");
                PlayniteApi.Dialogs.ShowErrorMessage("Nintendo3DS.Games.txt not found. A blank file has been created.", "Error");
            }
            return File.ReadAllLines(gamesFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        private string CleanGameName(string name)
        {
            return name.Replace("�", "'").Trim();
        }

        private string MapPlatformName(string platform)
        {
            switch (platform.ToLower())
            {
                case "nintendo - switch":
                    return "Nintendo Switch";
                case "ps3":
                    return "Sony PlayStation 3";
                default:
                    return "Nintendo 3DS";
            }
        }

        private List<string> FindInstallDirs(string gameName)
        {
            string[] drives = Environment.GetLogicalDrives();
            List<string> installDirs = new List<string>();

            foreach (string drive in drives)
            {
                string romsPath = Path.Combine(drive, "Roms", "Nintendo - 3DS", "Games");
                if (Directory.Exists(romsPath))
                {
                    var gameFiles = Directory.GetFiles(romsPath, "*.3ds", SearchOption.AllDirectories)
                                             .Where(file => file.IndexOf(gameName, StringComparison.OrdinalIgnoreCase) >= 0)
                                             .ToList();

                    installDirs.AddRange(gameFiles);
                }
            }

            return installDirs;
        }

        private List<string> FindAllRoms()
        {
            string[] drives = Environment.GetLogicalDrives();
            List<string> allRoms = new List<string>();

            foreach (string drive in drives)
            {
                string romsPath = Path.Combine(drive, "Roms", "Nintendo - 3DS", "Games");
                if (Directory.Exists(romsPath))
                {
                    var gameFiles = Directory.GetFiles(romsPath, "*.3ds", SearchOption.AllDirectories).ToList();
                    allRoms.AddRange(gameFiles);
                }
            }

            return allRoms;
        }

        private string GetBaseGameName(string gameName)
        {
            return Regex.Replace(gameName, @" \(Rev \d+\)$", "").Trim();
        }

        private GameMetadata CreateGameMetadata(string baseName, string name, string url, string platform, string version, string installDir)
        {
            var iconPath = installDir != null ? Path.Combine(installDir, "icon.png") : null;
            var backgroundPath = installDir != null ? Path.Combine(installDir, "background.png") : null;
            var descriptionPath = installDir != null ? Path.Combine(installDir, "description.txt") : null;
            var description = descriptionPath != null && File.Exists(descriptionPath) ? File.ReadAllText(descriptionPath) : "";

            var gameMetadata = new GameMetadata
            {
                Name = baseName,
                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platform) },
                GameActions = new List<GameAction>(),
                Version = version,
                IsInstalled = !string.IsNullOrEmpty(installDir),
                InstallDirectory = installDir,
                Roms = new List<GameRom>(),
                Icon = iconPath != null && File.Exists(iconPath) ? new MetadataFile(iconPath) : null,
                BackgroundImage = backgroundPath != null && File.Exists(backgroundPath) ? new MetadataFile(backgroundPath) : null,
                Description = description
            };

            return gameMetadata;
        }

        private void AddOrUpdateGame(Dictionary<string, GameMetadata> gamesDict, string baseName, string name, string url, string platform, string version, string installDir)
        {
            var gameMetadata = CreateGameMetadata(baseName, name, url, platform, version, installDir);

            if (!gamesDict.ContainsKey(baseName))
            {
                gamesDict[baseName] = gameMetadata;
                logger.Info($"Added new game entry: {baseName}");
            }
            else
            {
                var existingMetadata = gamesDict[baseName];
                existingMetadata.Roms.AddRange(gameMetadata.Roms.Where(r => !existingMetadata.Roms.Any(er => er.Path == r.Path)));
                existingMetadata.GameActions.AddRange(gameMetadata.GameActions.Where(a => !existingMetadata.GameActions.Any(ea => ea.Path == a.Path)));
                existingMetadata.Icon = existingMetadata.Icon ?? gameMetadata.Icon;
                existingMetadata.BackgroundImage = existingMetadata.BackgroundImage ?? gameMetadata.BackgroundImage;
                existingMetadata.Description = existingMetadata.Description ?? gameMetadata.Description;
                if (!existingMetadata.IsInstalled && gameMetadata.IsInstalled)
                {
                    existingMetadata.InstallDirectory = gameMetadata.InstallDirectory;
                    existingMetadata.IsInstalled = true;
                }
            }

            AddOrUpdateGameAction(gamesDict[baseName], name, url);
        }

        private void AddOrUpdateGameAction(GameMetadata game, string actionName, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                logger.Warn($"Skipping action with empty URL for game: {game.Name}");
                return;
            }

            var revisionMatch = Regex.Match(actionName, @"\(Rev \d+\)");
            var revision = revisionMatch.Success ? revisionMatch.Value : "";
            var cleanName = Regex.Replace(actionName, @"\(Rev \d+\)\s*\(Rev \d+\)", m => m.Groups[1].Value);

            var downloadActionName = string.IsNullOrEmpty(revision) ? $"Download {cleanName}" : $"Download {cleanName} {revision}";

            var existingAction = game.GameActions.FirstOrDefault(a => a.Path.Equals(url, StringComparison.OrdinalIgnoreCase));
            if (existingAction == null)
            {
                game.GameActions.Add(new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = url,
                    IsPlayAction = false
                });
                logger.Info($"Added new action: {downloadActionName} for game: {game.Name}");
            }
            else
            {
                if (!existingAction.Path.Equals(url, StringComparison.OrdinalIgnoreCase))
                {
                    existingAction.Path = url;
                    existingAction.Name = downloadActionName;
                    logger.Info($"Updated action: {downloadActionName} for game: {game.Name}");
                }
                else
                {
                    logger.Info($"Action already exists for game: {game.Name}, URL: {url}");
                }
            }
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var gamesDict = new Dictionary<string, GameMetadata>();
            var existingGames = PlayniteApi.Database.Games.Where(g => g.PluginId == Id).ToList();
            var gameEntries = LoadGames();
            logger.Info($"Total game entries from text file: {gameEntries.Count}");

            foreach (var line in gameEntries)
            {
                try
                {
                    string name, url, version, platform;
                    int nameStart = line.IndexOf("Name: \"") + 7;
                    int nameEnd = line.IndexOf("\", URL: \"");
                    int urlStart = line.IndexOf("URL: \"") + 6;
                    int urlEnd = line.IndexOf("\", Version: \"");
                    int versionStart = line.IndexOf("Version: \"") + 10;
                    int versionEnd = line.IndexOf("\", Platform: \"");
                    int platformStart = line.IndexOf("Platform: \"") + 11;
                    int platformEnd = line.LastIndexOf("\"");

                    if (nameStart < nameEnd && urlStart < urlEnd && platformStart < platformEnd)
                    {
                        name = line.Substring(nameStart, nameEnd - nameStart).Trim();
                        url = line.Substring(urlStart, urlEnd - urlStart).Trim();
                        version = (versionStart < versionEnd) ? line.Substring(versionStart, versionEnd - versionStart).Trim() : "";
                        platform = line.Substring(platformStart, platformEnd - platformStart).Trim();

                        name = CleanGameName(name);
                        platform = MapPlatformName(platform);
                        string baseName = GetBaseGameName(name);

                        var installDirs = FindInstallDirs(name);
                        var validInstallDirs = installDirs.Where(Directory.Exists).Select(Path.GetDirectoryName).ToList();
                        var installDir = validInstallDirs.FirstOrDefault();

                        AddOrUpdateGame(gamesDict, baseName, name, url, platform, version, installDir);

                        // Fetch metadata for the newly added game
                        if (!string.IsNullOrEmpty(installDir))
                        {
                            var game = PlayniteApi.Database.Games.FirstOrDefault(g => g.Name == baseName && g.PluginId == Id);
                            if (game != null)
                            {
                                var options = new MetadataRequestOptions(game, false);
                                var metadata = GetLibraryMetadata(game);

                                // Apply the fetched metadata
                                game.Name = metadata.Name;
                                game.Version = metadata.Version;
                                game.IsInstalled = metadata.IsInstalled;
                                game.InstallDirectory = metadata.InstallDirectory;
                                game.Icon = metadata.Icon?.Path;
                                game.BackgroundImage = metadata.BackgroundImage?.Path;
                                game.Description = metadata.Description;
                                PlayniteApi.Database.Games.Update(game);
                            }
                        }
                    }
                    else
                    {
                        logger.Error($"Invalid entry in Nintendo3DS.Games.txt: {line}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Failed to parse line in Nintendo3DS.Games.txt: {line}");
                }
            }

            // Step B: Always scan the ROMs folder for any ROMs and match them to existing games or add new ones
            var allRoms = FindAllRoms();
            foreach (var romPath in allRoms)
            {
                var romName = Path.GetFileNameWithoutExtension(romPath);
                var platform = "Nintendo 3DS";
                var baseName = GetBaseGameName(romName);

                GameMetadata existingGame;
                if (!gamesDict.TryGetValue(baseName, out existingGame))
                {
                    var installDir = Path.GetDirectoryName(romPath);
                    if (Directory.Exists(installDir))
                    {
                        var gameMetadata = CreateGameMetadata(baseName, romName, "", platform, "0", installDir);
                        gameMetadata.Roms.Add(new GameRom(romName, romPath)); // Add ROM to metadata
                        gamesDict[baseName] = gameMetadata;
                    }
                }
                else
                {
                    if (!existingGame.IsInstalled)
                    {
                        existingGame.InstallDirectory = Path.GetDirectoryName(romPath);
                        existingGame.IsInstalled = true;
                    }
                    if (!existingGame.Roms.Any(r => r.Path == romPath)) // Avoid duplicate ROM entries
                    {
                        existingGame.Roms.Add(new GameRom(romName, romPath));
                    }
                }
            }

            // Add or update games in Playnite database
            foreach (var game in gamesDict.Values)
            {
                var platformName = game.Platforms.First().ToString();
                var platformId = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase))?.Id;

                if (platformId != null)
                {
                    var existingGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.Name == game.Name && g.PlatformIds.Contains(platformId.Value));
                    if (existingGame == null)
                    {
                        var gameToAdd = new Game
                        {
                            Name = game.Name,
                            GameActions = new ObservableCollection<GameAction>(game.GameActions),
                            Version = game.Version,
                            IsInstalled = game.IsInstalled,
                            InstallDirectory = game.InstallDirectory,
                            PluginId = Id,
                            PlatformIds = new List<Guid> { platformId.Value },
                            Roms = new ObservableCollection<GameRom>(game.Roms),
                            Icon = game.Icon?.Path,
                            BackgroundImage = game.BackgroundImage?.Path,
                            Description = game.Description
                        };

                        // Set Play Action for Lime 3DS Emulator
                        var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name == "Lime 3DS");
                        if (emulator != null)
                        {
                            var profile = emulator.AllProfiles.FirstOrDefault(p => p.Name == "Default");
                            if (profile != null)
                            {
                                gameToAdd.GameActions.Add(new GameAction
                                {
                                    Name = "Play with Emulator",
                                    Type = GameActionType.Emulator,
                                    EmulatorId = emulator.Id,
                                    EmulatorProfileId = profile.Id,
                                    Path = gameToAdd.Roms.FirstOrDefault()?.Path,
                                    IsPlayAction = true
                                });
                            }
                        }
                        PlayniteApi.Database.Games.Add(gameToAdd);
                        logger.Info($"Added game to Playnite library: {game.Name}, Platform ID: {platformId}");
                    }
                    else
                    {
                        var installDirs = FindInstallDirs(existingGame.Name);
                        UpdateExistingGame(existingGame, game, installDirs, platformId.Value);
                    }
                }
                else
                {
                    logger.Error($"Platform not found for game: {game.Name}, Platform: {platformName}");
                }
            }

            return gamesDict.Values;
        }

        private GameMetadata GetLibraryMetadata(Game game)
        {
            var provider = new MyrientNintendo3DSMetadataProvider();
            return provider.GetMetadata(game);
        }

        private void UpdateExistingGame(Game existingGame, GameMetadata newGameMetadata, List<string> installDirs, Guid platformId)
        {
            existingGame.Version = newGameMetadata.Version ?? existingGame.Version;

            if (newGameMetadata.GameActions != null)
            {
                foreach (var action in newGameMetadata.GameActions)
                {
                    var existingAction = existingGame.GameActions.FirstOrDefault(a => a.Path == action.Path);
                    if (existingAction == null)
                    {
                        existingGame.GameActions.Add(action);
                    }
                    else if (existingAction.Path != action.Path)
                    {
                        existingAction.Path = action.Path;
                        existingAction.Name = action.Name;
                    }
                }
            }

            var currentRoms = new ObservableCollection<GameRom>(installDirs.Select(dir => new GameRom(Path.GetFileName(dir), dir)));

            existingGame.Roms.Clear();
            foreach (var rom in currentRoms)
            {
                if (!existingGame.Roms.Any(r => r.Path == rom.Path))
                {
                    existingGame.Roms.Add(rom);
                }
            }

            if (!existingGame.GameActions.Any(a => a.Name == "Play with Emulator"))
            {
                var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name == "Lime 3DS");
                if (emulator != null)
                {
                    var profile = emulator.AllProfiles.FirstOrDefault(p => p.Name == "Default");

                    if (profile != null)
                    {
                        existingGame.GameActions.Add(new GameAction
                        {
                            Name = "Play with Emulator",
                            Type = GameActionType.Emulator,
                            EmulatorId = emulator.Id,
                            EmulatorProfileId = profile.Id,
                            Path = currentRoms.FirstOrDefault()?.Path,
                            IsPlayAction = true
                        });
                    }
                }
            }

            existingGame.InstallDirectory = Path.GetDirectoryName(currentRoms.FirstOrDefault()?.Path);
            existingGame.IsInstalled = currentRoms.Any();

            PlayniteApi.Database.Games.Update(existingGame);
            logger.Info($"Updated existing game in Playnite library: {existingGame.Name}, Platform ID: {platformId}");
        }

        public class MyrientNintendo3DSMetadataProvider : LibraryMetadataProvider
        {
            public override GameMetadata GetMetadata(Game game)
            {
                // Your implementation for fetching and returning metadata for the specified game
                var metadata = new GameMetadata
                {
                    Name = game.Name,
                    // Fill in other metadata properties as needed
                };
                return metadata;
            }

            public override void Dispose()
            {
                // Dispose resources if necessary
            }
        }
    }
}
