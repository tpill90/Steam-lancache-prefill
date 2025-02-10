﻿// ReSharper disable MemberCanBePrivate.Global - Properties used as parameters can't be private with CliFx, otherwise they won't work.
namespace SteamPrefill.CliCommands
{
    [UsedImplicitly]
    [Command("select-apps", Description = "Displays an interactive list of all owned apps.  " +
                                          "As many apps as desired can be selected, which will then be used by the 'prefill' command")]
    public class SelectAppsCommand : ICommand
    {
        [CommandOption("no-ansi",
            Description = "Application output will be in plain text.  " +
                          "Should only be used if terminal does not support Ansi Escape sequences, or when redirecting output to a file.",
            Converter = typeof(NullableBoolConverter))]
        public bool? NoAnsiEscapeSequences { get; init; }

        [CommandOption("all", 'a',
            Description = "Select all owned applications without opening the GUI.",
            Converter = typeof(NullableBoolConverter))]
        public bool? SelectAll { get; init; }

        [CommandOption("run-prefill", 'p',
            Description = "Automatically run prefill after selection (bypasses prompt)." +
                          "Defaults to true, but can be set to false to bypass prompt without running prefill.",
            Converter = typeof(NullableBoolConverter))]
        public bool? RunPrefill { get; init; }

        public async ValueTask ExecuteAsync(IConsole console)
        {
            var ansiConsole = console.CreateAnsiConsole();
            // Property must be set to false in order to disable ansi escape sequences
            ansiConsole.Profile.Capabilities.Ansi = !NoAnsiEscapeSequences ?? true;

            using var steamManager = new SteamManager(ansiConsole, new DownloadArguments());
            try
            {
                await steamManager.InitializeAsync();
                var tuiAppModels = await BuildTuiAppModelsAsync(steamManager);

                if (!(SelectAll ?? false))
                {
                    if (System.OperatingSystem.IsLinux())
                    {
                        // This is required to be enabled otherwise some Linux distros/shells won't display color correctly.
                        Application.UseSystemConsole = true;
                    }
                    if (System.OperatingSystem.IsWindows())
                    {
                        // Must be set to false on Windows otherwise navigation will not work in Windows Terminal
                        Application.UseSystemConsole = false;
                    }

                    Application.Init();
                    using var tui2 = new SelectAppsTui(tuiAppModels);
                    Key userKeyPress = tui2.Run();

                    // There is an issue with Terminal.Gui where this property is set to 'true' when the TUI is initialized, but forgets to reset it back to 'false' when the TUI closes.
                    // This causes an issue where the prefill run is not able to be cancelled with ctrl+c, but only on Linux systems.
                    Console.TreatControlCAsInput = false;


                    // Will only allow for prefill if the user has saved changes.  Escape simply exists
                    if (userKeyPress != Key.Enter)
                    {
                        return;
                    }
                    steamManager.SetAppsAsSelected(tuiAppModels);

                    // This escape sequence is required when running on linux, otherwise will not be able to use the Spectre selection prompt
                    // See : https://github.com/gui-cs/Terminal.Gui/issues/418
                    await console.Output.WriteAsync("\x1b[?1h");
                    await console.Output.FlushAsync();
                }
                else
                {
                    var newApps = SelectAllApps(tuiAppModels);
                    ansiConsole.WriteLine();
                    ansiConsole.LogMarkupLine($"{newApps.Count} new apps selected:");

                    if (newApps.Any())
                    {
                        foreach (var newApp in newApps)
                        {
                            ansiConsole.LogMarkupLine(newApp);
                        }

                        steamManager.SetAppsAsSelected(tuiAppModels);
                    }
                }

                var runPrefill = RunPrefill ?? ansiConsole.Prompt(new SelectionPrompt<bool>()
                                                    .Title(LightYellow("Run prefill now?"))
                                                    .AddChoices(true, false)
                                                    .UseConverter(e => e == false ? "No" : "Yes"));

                if (runPrefill)
                {
                    await steamManager.DownloadMultipleAppsAsync(false, false, null);
                }
            }
            finally
            {
                steamManager.Shutdown();
            }
        }

        private static async Task<List<TuiAppInfo>> BuildTuiAppModelsAsync(SteamManager steamManager)
        {
            // Listing user's owned apps, and selected apps
            var ownedApps = await steamManager.GetAllAvailableAppsAsync();
            var previouslySelectedApps = steamManager.LoadPreviouslySelectedApps();

            // Building out Tui models
            var tuiAppModels = ownedApps.Select(e =>
                                        new TuiAppInfo(e.AppId.ToString(), e.Name)
                                        {
                                            MinutesPlayed = e.MinutesPlayed2Weeks,
                                            ReleaseDate = e.ReleaseDate
                                        }).ToList();

            // Flagging previously selected apps as selected
            foreach (var appInfo in tuiAppModels)
            {
                appInfo.IsSelected = previouslySelectedApps.Contains(UInt32.Parse(appInfo.AppId));
            }

            return tuiAppModels;
        }

        private List<string> SelectAllApps(List<TuiAppInfo> tuiAppModels)
        {
            var newApps = new List<string>();

            foreach (var appInfo in tuiAppModels) 
            { 
                if (!appInfo.IsSelected)
                {
                    newApps.Add($"{appInfo.Title} ({appInfo.AppId})");
                    appInfo.IsSelected = true;
                }
            }
            return newApps;
        }
    }
}