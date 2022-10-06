﻿using SteamPrefill.CliCommands.SelectAppsBeta;
using AnsiConsoleExtensions = LancachePrefill.Common.Extensions.AnsiConsoleExtensions;

// ReSharper disable MemberCanBePrivate.Global - Properties used as parameters can't be private with CliFx, otherwise they won't work.
namespace SteamPrefill.CliCommands
{
    //TODO update the readme in general.  Some of the pictures are not as crisp as they should be.  Some are too wide
    [UsedImplicitly]
    [Command("select-apps", Description = "Displays an interactive list of all owned apps.  " +
                                          "As many apps as desired can be selected, which will then be used by the 'prefill' command")]
    public class SelectAppsCommand : ICommand
    {
        public async ValueTask ExecuteAsync(IConsole console)
        {
            var ansiConsole = console.CreateAnsiConsole();
            using var steamManager = new SteamManager(ansiConsole, new DownloadArguments());

            try
            {
                await steamManager.InitializeAsync();

                var games = await steamManager.GetAllAvailableGamesAsync();

                Application.Init();
                using var tui2 = new SelectAppsTui(games, steamManager);
                tui2.Run();

                // This escape sequence is required when running on linux, otherwise will not be able to use the Spectre selection prompt
                // See : https://github.com/gui-cs/Terminal.Gui/issues/418
                await Console.Out.WriteAsync("\x1b[?1h");
                await Console.Out.FlushAsync();

                var runPrefill = ansiConsole.Prompt(new SelectionPrompt<bool>()
                                                    .Title(LightYellow("Run prefill now?"))
                                                    .AddChoices(true, false)
                                                    .UseConverter(e => e == false ? "No" : "Yes"));

                if (runPrefill)
                {
                    await steamManager.DownloadMultipleAppsAsync(false, false, null, new List<uint>());
                }
            }
            catch (TimeoutException e)
            {
                ansiConsole.MarkupLine("\n");
                if (e.StackTrace.Contains(nameof(UserAccountStore.GetUsernameAsync)))
                {
                    ansiConsole.MarkupLine(Red("Timed out while waiting for username entry"));
                }
                if (e.StackTrace.Contains(nameof(AnsiConsoleExtensions.ReadPassword)))
                {
                    ansiConsole.MarkupLine(Red("Timed out while waiting for password entry"));
                }
                ansiConsole.WriteException(e, ExceptionFormats.ShortenPaths);
            }
            catch (Exception e)
            {
                ansiConsole.WriteException(e, ExceptionFormats.ShortenPaths);
            }
            finally
            {
                steamManager.Shutdown();
            }
        }
    }
}