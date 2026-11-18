using SupernovaBench.Core.Games;

namespace SupernovaBench.Core.Tests;

public class VdfAndSteamTests
{
    private const string LibraryFolders = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"apps"
        		{
        			"228980"		"0"
        			"570"		"123"
        		}
        	}
        	// segunda biblioteca
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        	}
        }
        """;

    [Fact]
    public void Vdf_parses_nested_objects_escapes_comments_and_ignores_case()
    {
        var root = Vdf.Parse(LibraryFolders + "\n\"extra\" \"say \\\"hi\\\"\" [$WIN32]\n");
        Assert.Equal(@"C:\Program Files (x86)\Steam", root.At("LibraryFolders", "0", "PATH")?.Value);
        Assert.Equal("123", root.At("libraryfolders", "0", "apps", "570")?.Value);
        Assert.Equal("say \"hi\"", root.Get("extra"));
        Assert.Null(root.At("libraryfolders", "9"));
        Assert.Equal(2, root["libraryfolders"]!.Children.Count);
    }

    [Fact]
    public void Library_folders_new_and_old_formats()
    {
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, SteamLibrary.ParseLibraryFolders(LibraryFolders));
        const string old = "\"LibraryFolders\" { \"TimeNextStatsReport\" \"123\" \"ContentStatsID\" \"-1\" \"1\" \"E:\\\\Games\\\\Steam\" }";
        Assert.Equal(new[] { @"E:\Games\Steam" }, SteamLibrary.ParseLibraryFolders(old));
    }

    [Fact]
    public void App_manifest_and_tool_filter()
    {
        var m = SteamLibrary.ParseAppManifest("""
            "AppState"
            {
            	"appid"		"1091500"
            	"name"		"Cyberpunk 2077"
            	"StateFlags"		"4"
            	"installdir"		"Cyberpunk 2077"
            	"LastUpdated"		"1700000000"
            	"SizeOnDisk"		"70000000000"
            }
            """)!;
        Assert.Equal(1091500, m.AppId);
        Assert.Equal("Cyberpunk 2077", m.Name);
        Assert.Equal(70_000_000_000, m.SizeOnDisk);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), m.LastUpdated);
        Assert.False(SteamLibrary.IsTool(m.AppId, m.Name));
        Assert.True(SteamLibrary.IsTool(228980, "Steamworks Common Redistributables"));
        Assert.True(SteamLibrary.IsTool(1, "Proton 9.0"));
        Assert.True(SteamLibrary.IsTool(2, "Counter-Strike 2 Dedicated Server"));
        Assert.Null(SteamLibrary.ParseAppManifest("\"AppState\" { \"appid\" \"x\" }"));
    }

    [Fact]
    public void Playtime_comes_from_localconfig()
    {
        var pt = SteamLibrary.ParsePlaytime("""
            "UserLocalConfigStore"
            {
            	"Software"
            	{
            		"Valve"
            		{
            			"steam"
            			{
            				"apps"
            				{
            					"570"
            					{
            						"LastPlayed"		"1731900000"
            						"Playtime"		"5412"
            					}
            					"10"
            					{
            						"cloud" { "quota" "1" }
            					}
            				}
            			}
            		}
            	}
            }
            """);
        Assert.Single(pt);
        Assert.Equal(5412, pt[570].Minutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731900000), pt[570].LastPlayed);
    }

    [Fact]
    public void Scan_folder_finds_games_in_every_library_with_playtime()
    {
        var root = Directory.CreateTempSubdirectory("supernova-steam-").FullName;
        var lib2 = Directory.CreateTempSubdirectory("supernova-steamlib-").FullName;
        try
        {
            void App(string lib, int id, string name, string dir, bool createDir = true)
            {
                Directory.CreateDirectory(Path.Combine(lib, "steamapps"));
                File.WriteAllText(Path.Combine(lib, "steamapps", $"appmanifest_{id}.acf"),
                    $"\"AppState\" {{ \"appid\" \"{id}\" \"name\" \"{name}\" \"installdir\" \"{dir}\" \"SizeOnDisk\" \"1000\" }}");
                if (createDir) Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common", dir));
            }
            App(root, 570, "Dota 2", "dota 2 beta");
            App(root, 228980, "Steamworks Common Redistributables", "Steamworks Shared");
            App(root, 999, "Desinstalado", "gone", createDir: false);
            App(lib2, 1091500, "Cyberpunk 2077", "Cyberpunk 2077");
            File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{root.Replace("\\", "\\\\")}\" }} \"1\" {{ \"path\" \"{lib2.Replace("\\", "\\\\")}\" }} }}");
            var cfg = Path.Combine(root, "userdata", "12345", "config");
            Directory.CreateDirectory(cfg);
            File.WriteAllText(Path.Combine(cfg, "localconfig.vdf"),
                "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"570\" { \"Playtime\" \"90\" } } } } } }");

            var games = SteamLibrary.ScanFolder(root).OrderBy(g => g.Name).ToList();
            Assert.Equal(new[] { "Cyberpunk 2077", "Dota 2" }, games.Select(g => g.Name));
            var dota = games[1];
            Assert.Equal("steam:570", dota.Id);
            Assert.Equal("steam://rungameid/570", dota.LaunchUri);
            Assert.Equal(570, dota.SteamAppId);
            Assert.Equal(90, dota.StorePlaytimeMinutes);
            Assert.EndsWith("dota 2 beta", dota.InstallDir);
            Assert.Null(games[0].StorePlaytimeMinutes);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(lib2, true);
        }
    }
}

public class StoreManifestTests
{
    [Fact]
    public void Epic_manifest_game_dlc_and_incomplete()
    {
        const string game = """
            { "FormatVersion": 0, "bIsIncompleteInstall": false, "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteLauncher.exe",
              "DisplayName": "Fortnite", "InstallLocation": "C:\\Program Files\\Epic Games\\Fortnite", "InstallSize": 30000000000,
              "AppName": "Fortnite", "CatalogNamespace": "fn", "CatalogItemId": "4fe75bbc5a674f4f9b356b5c90567da5",
              "AppCategories": ["public", "games", "applications"] }
            """;
        var g = EpicManifest.Parse(game)!;
        Assert.Equal("epic:Fortnite", g.Id);
        Assert.Equal("Fortnite", g.Name);
        Assert.Equal(30_000_000_000, g.SizeBytes);
        Assert.Equal("com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true", g.LaunchUri);
        Assert.EndsWith("FortniteLauncher.exe", g.Executable);

        Assert.Null(EpicManifest.Parse(game.Replace("\"bIsIncompleteInstall\": false", "\"bIsIncompleteInstall\": true")));
        Assert.Null(EpicManifest.Parse(game.Replace("[\"public\", \"games\", \"applications\"]", "[\"public\", \"addons\"]")));
        Assert.Null(EpicManifest.Parse(game.Replace("\"AppName\": \"Fortnite\"", "\"AppName\": \"FortniteDLC\", \"MainGameAppName\": \"Fortnite\"")));
    }

    [Fact]
    public void Gog_info_picks_the_primary_task()
    {
        var info = GogInfo.Parse("""
            { "gameId": "1207658924", "name": "The Witcher 3: Wild Hunt", "playTasks": [
              { "category": "launcher", "isPrimary": false, "type": "FileTask", "path": "REDprelauncher.exe" },
              { "category": "game", "isPrimary": true, "type": "FileTask", "path": "bin\\x64\\witcher3.exe", "arguments": "-dx12" },
              { "type": "URLTask", "link": "https://example.com" } ] }
            """)!;
        Assert.Equal("1207658924", info.GameId);
        Assert.Equal("The Witcher 3: Wild Hunt", info.Name);
        Assert.Equal(@"bin\x64\witcher3.exe", info.ExePath);
        Assert.Equal("-dx12", info.Arguments);
    }

    [Fact]
    public void Xbox_config_and_launch_uri()
    {
        var c = XboxGameConfig.Parse("""
            <?xml version="1.0" encoding="utf-8"?>
            <Game configVersion="1">
              <Identity Name="Microsoft.624F8B84B80" Publisher="CN=..." Version="1.0.0.0" />
              <ExecutableList>
                <Executable Name="Forza\ForzaHorizon5.exe" Id="Game" TargetDeviceFamily="PC" />
              </ExecutableList>
              <ShellVisuals DefaultDisplayName="Forza Horizon 5" PublisherDisplayName="Microsoft Studios" />
            </Game>
            """)!;
        Assert.Equal("Microsoft.624F8B84B80", c.IdentityName);
        Assert.Equal("Forza Horizon 5", c.DisplayName);
        Assert.Equal(@"Forza\ForzaHorizon5.exe", c.Executable);
        Assert.Equal(@"shell:AppsFolder\Microsoft.624F8B84B80_8wekyb3d8bbwe!Game", XboxGameConfig.LaunchUri("Microsoft.624F8B84B80_8wekyb3d8bbwe", c.AppId));
        Assert.Null(XboxGameConfig.Parse("<Game><ShellVisuals DefaultDisplayName=\"x\"/></Game>"));
        Assert.Null(XboxGameConfig.Parse("<Game><Identity Name=\"A\"/><ShellVisuals DefaultDisplayName=\"ms-resource:Name\"/></Game>")!.DisplayName);
    }

    [Fact]
    public void Battle_net_uid_to_launch_code()
    {
        const string uninstall = "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=ptBR --uid=prometheus --displayname=\"Overwatch\"";
        Assert.Equal("prometheus", BattleNetCodes.ParseUid(uninstall));
        Assert.Equal("Pro", BattleNetCodes.LaunchCode("prometheus"));
        Assert.Equal("Fen", BattleNetCodes.LaunchCode("FENRIS"));
        Assert.Null(BattleNetCodes.LaunchCode("unknown_game"));
        Assert.Null(BattleNetCodes.ParseUid("C:\\x\\uninstall.exe"));
        Assert.True(BattleNetCodes.IsLauncher("battle.net"));
    }
}

public class CoverSearchTests
{
    [Theory]
    [InlineData("DOOM Eternal™", "doom eternal")]
    [InlineData("The Witcher® 3: Wild Hunt – Game of the Year Edition", "the witcher 3 wild hunt")]
    [InlineData("Pokémon Café", "pokemon cafe")]
    [InlineData("Red Dead Redemption 2: Ultimate Edition", "red dead redemption 2")]
    public void Names_are_normalized(string name, string expected) => Assert.Equal(expected, CoverSearch.Normalize(name));

    [Fact]
    public void Picks_exact_match_and_refuses_unrelated_results()
    {
        const string json = """
            { "total": 3, "items": [
              { "type": "app", "name": "Cyberpunk 2077: Phantom Liberty", "id": 2138330 },
              { "type": "app", "name": "Cyberpunk 2077", "id": 1091500 },
              { "type": "sub", "name": "Cyberpunk 2077", "id": 5 } ] }
            """;
        Assert.Equal(1091500, CoverSearch.PickBestMatch(json, "Cyberpunk 2077™"));
        Assert.Null(CoverSearch.PickBestMatch(json, "Minecraft"));
        Assert.Null(CoverSearch.PickBestMatch("{\"total\":0,\"items\":[]}", "Cyberpunk 2077"));
        Assert.Contains("term=cyberpunk%202077", CoverSearch.StoreSearchUrl("Cyberpunk 2077™"));
        Assert.EndsWith("/1091500/library_600x900_2x.jpg", CoverSearch.SteamCoverUrls(1091500)[0]);
    }
}

public class PlaytimeTrackerTests
{
    private static readonly GameEntry GameA = new() { Id = "steam:1", Name = "A", Store = GameStore.Steam, InstallDir = @"C:\Jogos\A" };
    private static readonly GameEntry GameAB = new() { Id = "steam:2", Name = "AB", Store = GameStore.Steam, InstallDir = @"C:\Jogos\AB" };
    private static readonly GameEntry GameB = new() { Id = "epic:B", Name = "B", Store = GameStore.Epic, InstallDir = @"D:\Epic\B\" };
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 20, 0, 0, TimeSpan.Zero);

    private static PlaytimeTracker NewTracker()
    {
        var t = new PlaytimeTracker(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        t.SetGames(new[] { GameA, GameAB, GameB });
        return t;
    }

    [Fact]
    public void Session_starts_progresses_and_ends_with_the_right_minutes()
    {
        var t = NewTracker();
        var store = new GameLibraryStore(Path.Combine(Path.GetTempPath(), $"games-{Guid.NewGuid():N}.json"));

        var e1 = Assert.Single(t.Update(new[] { @"C:\Windows\explorer.exe", @"c:\jogos\a\bin\CrashReporter.exe", @"C:\Jogos\A\bin\game.exe" }, T0));
        Assert.Equal(TrackerEventKind.Started, e1.Kind);
        Assert.Equal(@"C:\Jogos\A\bin\game.exe", e1.Exe);
        store.Apply(e1, T0);

        Assert.Empty(t.Update(new[] { @"C:\Jogos\A\bin\game.exe" }, T0.AddMinutes(3)));
        var p = Assert.Single(t.Update(new[] { @"C:\Jogos\A\bin\game.exe" }, T0.AddMinutes(6)));
        Assert.Equal(TrackerEventKind.Progress, p.Kind);
        Assert.Equal(6, p.Minutes, 3);
        Assert.True(p.NewSession);
        store.Apply(p, T0.AddMinutes(6));

        var end = Assert.Single(t.Update(Array.Empty<string>(), T0.AddMinutes(10)));
        Assert.Equal(TrackerEventKind.Ended, end.Kind);
        Assert.Equal(4, end.Minutes, 3);
        Assert.False(end.NewSession);
        Assert.Equal(TimeSpan.FromMinutes(10), end.SessionLength);
        store.Apply(end, T0.AddMinutes(10));

        var stats = store.Stats("steam:1")!;
        Assert.Equal(10, stats.Minutes, 3);
        Assert.Equal(1, stats.Sessions);
        Assert.Equal(T0.AddMinutes(10), stats.LastPlayed);
        Assert.Equal(@"C:\Jogos\A\bin\game.exe", stats.Exe);
    }

    [Fact]
    public void Short_sessions_are_not_counted()
    {
        var t = NewTracker();
        t.Update(new[] { @"D:\Epic\B\B.exe" }, T0);
        var end = Assert.Single(t.Update(Array.Empty<string>(), T0.AddSeconds(40)));
        Assert.Equal(0, end.Minutes);
        Assert.False(end.NewSession);

        t.Update(new[] { @"D:\Epic\B\B.exe" }, T0);
        var end2 = Assert.Single(t.StopAll(T0.AddMinutes(2)));
        Assert.Equal(2, end2.Minutes, 3);
        Assert.True(end2.NewSession);
    }

    [Fact]
    public void Folder_prefix_does_not_match_a_sibling_and_two_games_run_together()
    {
        var t = NewTracker();
        var events = t.Update(new[] { @"C:\Jogos\AB\ab.exe", @"D:/Epic/B/Binaries/B.exe" }, T0);
        Assert.Equal(new[] { "epic:B", "steam:2" }, events.Select(e => e.Game.Id).OrderBy(x => x));
        Assert.Equal(2, t.Running.Count);
        Assert.DoesNotContain(t.Running, r => r.Game.Id == "steam:1");
    }

    [Fact]
    public void Library_store_round_trip_and_corrupt_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"games-{Guid.NewGuid():N}.json");
        try
        {
            var s = new GameLibraryStore(path);
            var manual = s.AddManual("", @"C:\Jogos\Indie\indie.exe", "-windowed");
            Assert.Equal("indie", manual.Name);
            Assert.StartsWith("manual:", manual.Id);
            Assert.Equal(@"C:\Jogos\Indie", manual.InstallDir.Replace('/', '\\'));
            s.SetFavorite("steam:570", true);
            s.SetHidden("epic:x", true);
            s.SetProfile("steam:570", new GameProfile { PowerPlan = PowerPlanChoice.HighPerformance, Priority = PriorityChoice.High, Gpu = GpuChoice.HighPerformance });
            s.SetProfile("steam:1", new GameProfile());
            s.Save();

            var back = new GameLibraryStore(path);
            Assert.Single(back.ManualGames());
            Assert.True(back.IsFavorite("steam:570"));
            Assert.True(back.IsHidden("epic:x"));
            Assert.Equal(PriorityChoice.High, back.Profile("steam:570").Priority);
            Assert.True(back.Profile("steam:1").IsDefault);
            Assert.True(back.RemoveManual(manual.Id));

            File.WriteAllText(path, "{ isto não é json");
            var fresh = new GameLibraryStore(path);
            Assert.Empty(fresh.ManualGames());
        }
        finally
        {
            File.Delete(path);
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }
}
