﻿namespace SteamPrefill.Handlers
{
    /// <summary>
    /// Responsible for retrieving application metadata from Steam
    /// </summary>
    public class AppInfoHandler
    {
        private readonly IAnsiConsole _ansiConsole;
        private readonly Steam3Session _steam3Session;

        private ConcurrentDictionary<uint, AppInfo> LoadedAppInfos { get; } = new ConcurrentDictionary<uint, AppInfo>();

        public AppInfoHandler(IAnsiConsole ansiConsole, Steam3Session steam3Session)
        {
            _ansiConsole = ansiConsole;
            _steam3Session = steam3Session;
        }

        /// <summary>
        /// Gets the latest app metadata from steam, for the specified apps, as well as their related DLC apps
        /// </summary>
        public async Task RetrieveAppMetadataAsync(List<uint> appIds)
        {
            await _ansiConsole.StatusSpinner().StartAsync("Retrieving latest App info...", async _ =>
            {
                // Breaking the request into smaller batches that complete faster
                var batchJobs = appIds.Chunk(100)
                                      .Select(e => BulkLoadAppInfosAsync(e.ToList()));
                await Task.WhenAll(batchJobs);

                // Once we have loaded all the apps, we can also load information for related DLC
                await BulkLoadDlcAppInfoAsync();
            });
        }

        /// <summary>
        /// Will return an AppInfo for the specified AppId, that contains various metadata about the app.
        /// If the information for the specified app hasn't already been retrieved, then a request to the Steam network will be made.
        /// </summary>
        public async Task<AppInfo> GetAppInfoAsync(uint appId)
        {
            if (LoadedAppInfos.ContainsKey(appId))
            {
                return LoadedAppInfos[appId];
            }

            await BulkLoadAppInfosAsync(new List<uint> { appId });
            return LoadedAppInfos[appId];
        }

        /// <summary>
        /// Retrieves the latest AppInfo for multiple apps at the same time.  One large request containing multiple apps is significantly faster
        /// than multiple individual requests, as it seems that there is a minimum threshold for how quickly steam will return results.
        /// </summary>
        /// <param name="appIds">The list of App Ids to retrieve info for</param>
        private async Task BulkLoadAppInfosAsync(List<uint> appIds)
        {
            //TODO need to always load every app, but need to move this filtering elsewhere so it doesn't break things
            var noAccess = appIds.Where(e => !_steam3Session.AccountHasAppAccess(e)).ToList();

            var appIdsToLoad = appIds.Where(e => !LoadedAppInfos.ContainsKey(e)).ToList();
            if (!appIdsToLoad.Any())
            {
                return;
            }

            // Some apps will require an additional "access token" in order to retrieve their app metadata
            var accessTokensResponse = await _steam3Session.SteamAppsApi.PICSGetAccessTokens(appIds, new List<uint>()).ToTask();
            var appTokens = accessTokensResponse.AppTokens;

            // Build out the requests
            var requests = new List<SteamApps.PICSRequest>();
            foreach (var appId in appIdsToLoad)
            {
                var request = new SteamApps.PICSRequest(appId);
                if (appTokens.ContainsKey(appId))
                {
                    request.AccessToken = appTokens[appId];
                }
                requests.Add(request);
            }

            // Finally request the metadata from steam
            var resultSet = await _steam3Session.SteamAppsApi.PICSGetProductInfo(requests, new List<SteamApps.PICSRequest>()).ToTask();

            List<PicsProductInfo> appInfos = resultSet.Results.SelectMany(e => e.Apps).Select(e => e.Value).ToList();
            foreach (var app in appInfos)
            {
                LoadedAppInfos.TryAdd(app.ID, new AppInfo(_steam3Session, app.ID, app.KeyValues));
            }
        }

        /// <summary>
        /// Steam stores all DLCs for a game as separate "apps", so they must be loaded after the game's AppInfo has been retrieved,
        /// and the list of DLC AppIds are known.
        ///
        /// Once the DLC apps are loaded, the final combined depot list (both the app + dlc apps) will be built.
        /// </summary>
        private async Task BulkLoadDlcAppInfoAsync()
        {
            var dlcAppIds = LoadedAppInfos.Values.SelectMany(e => e.DlcAppIds).ToList();
            await BulkLoadAppInfosAsync(dlcAppIds);

            // Builds out the list of all depots for each game, including depots from all related DLCs
            // DLCs are stored as separate "apps", so their info comes back separately.
            foreach (var app in LoadedAppInfos.Values.Where(e => e.Type == AppType.Game))
            {
                foreach (var dlcApp in app.DlcAppIds)
                {
                    app.Depots.AddRange((await GetAppInfoAsync(dlcApp)).Depots);
                }

                var distinctDepots = app.Depots.DistinctBy(e => e.DepotId).ToList();
                app.Depots.Clear();
                app.Depots.AddRange(distinctDepots);
            }
        }

        /// <summary>
        /// Gets a list of available games, filtering out any unavailable, non-Windows games.
        /// </summary>
        public async Task<List<AppInfo>> GetAvailableGamesAsync(List<uint> filteredAppIds)
        {
            var appInfos = new List<AppInfo>();
            foreach (var appId in filteredAppIds)
            {
                appInfos.Add(await GetAppInfoAsync(appId));
            }
            
            return FilterGames(appInfos);
        }

        /// <summary>
        /// Gets a list of all available games, filtering out any unavailable, non-Windows games.
        /// </summary>
        /// <returns>All currently available games for the current user</returns>
        public List<AppInfo> GetAllAvailableGames()
        {
            return FilterGames(LoadedAppInfos.Values.ToList());
        }

        private List<AppInfo> FilterGames(List<AppInfo> appInfos)
        {
            var excludedAppIds = Enum.GetValues(typeof(ExcludedAppId)).Cast<uint>().ToList();

            return appInfos.Where(e => e.Type == AppType.Game
                                       && e.ReleaseState != ReleaseState.Unavailable
                                       && e.SupportsWindows)
                           .Where(e => !excludedAppIds.Contains(e.AppId))
                           .Where(e => !e.Categories.Contains(Category.Mods) && !e.Categories.Contains(Category.ModsHL2))
                           .Where(e => !e.Name.Contains("AMD Driver Updater"))
                           .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                           .ToList();
        }
    }

    /// <summary>
    /// A subset of <see cref="AppInfo"/> that is only ever used for caching known AppTypes for previously loaded AppInfos
    /// These types will be used to filter out apps that aren't games or DLC, which will help dramatically in app startup time.
    /// </summary>
    public class CachedAppInfo
    {
        public CachedAppInfo()
        {

        }

        public CachedAppInfo(AppInfo appInfo)
        {
            this.AppId = appInfo.AppId;
            this.Name = appInfo.Name;
            this.Type = appInfo.Type;
        }

        public uint AppId { get; set; }
        public string Name { get; set; }
        public AppType Type { get; set; }
    }
}