﻿namespace SteamPrefill.Handlers.Steam
{
    public sealed class LicenseManager
    {
        private readonly SteamApps _steamAppsApi;

        internal UserLicenses _userLicenses = new UserLicenses();

        public List<uint> AllOwnedAppIds => _userLicenses.OwnedAppIds.ToList();

        public LicenseManager(SteamApps steamAppsApi)
        {
            _steamAppsApi = steamAppsApi;
        }

        /// <summary>
        /// Checks against the list of currently owned apps to determine if the user is able to download this app.
        /// </summary>
        /// <param name="appid">Id of the application to check for access</param>
        /// <returns>True if the user has access to the app</returns>
        public bool AccountHasAppAccess(uint appid)
        {
            return _userLicenses.OwnedAppIds.Contains(appid);
        }

        /// <summary>
        /// Checks against the list of currently owned depots + apps to determine if the user is able to download this depot.
        /// There are 3 cases that a depot is considered owned :
        /// - If a user owns an App, and the Package that grants the App ownership also grants ownership to the App's depots, then the user owns the depot.
        /// - If the user owns an App with DLC, and the App owns the depot + a Package grants ownership of the depot, then the user owns the depot.
        /// - If the user owns an App with DLC but DLC App has no depots of its own. And instead the App owns a depot with the same Id as the DLC App,
        ///     then the user owns the depot.  Example DLC AppId : 1962660 - DepotId : 1962660
        ///
        /// For official documentation on how this works, see : https://partner.steamgames.com/doc/store/application/dlc
        /// </summary>
        /// <param name="depotId">Id of the depot to check for access</param>
        /// <returns>True if the user has access to the depot</returns>
        public bool AccountHasDepotAccess(uint depotId)
        {
            return _userLicenses.OwnedDepotIds.Contains(depotId) || _userLicenses.OwnedAppIds.Contains(depotId);
        }

        [SuppressMessage("Threading", "VSTHRD002:Synchronously waiting on tasks or awaiters may cause deadlocks", Justification = "Callback must be synchronous to compile")]
        public void LoadPackageInfo(IReadOnlyCollection<LicenseListCallback.License> licenseList)
        {
            _userLicenses = new UserLicenses();

            // Filters out licenses that are subscription based, and have expired, like EA Play for example.
            // The account will continue to "own" the packages, and will be unable to download their apps, so they must be filtered out here.
            var nonExpiredLicenses = licenseList.Where(e => !e.LicenseFlags.HasFlag(ELicenseFlags.Expired)).ToList();

            // Some packages require a access token in order to request their apps/depot list
            var packageRequests = nonExpiredLicenses.Select(e => new PICSRequest(e.PackageID, e.AccessToken)).ToList();

            var jobResult = _steamAppsApi.PICSGetProductInfo(new List<PICSRequest>(), packageRequests).ToTask().Result;
            var jobResults2 = jobResult.Results.SelectMany(e => e.Packages)
                                                                 .Select(e => e.Value)
                                                                 .ToList();
            var packageInfos = jobResults2
                               .Select(e => new Package(e.KeyValues))
                               .OrderBy(e => e.Id)
                               .ToList();

            foreach (var packageInfo in packageInfos)
            {
                var license = nonExpiredLicenses.First(e => e.PackageID == packageInfo.Id);
                foreach (var appId in packageInfo.AppIds)
                {
                    _userLicenses.AppIdPurchaseDateLookup.Add(new KeyValue2(appId, license.TimeCreated));
                }
            }
            _userLicenses.AppIdPurchaseDateLookup = _userLicenses.AppIdPurchaseDateLookup.OrderBy(e => e.Value).ToList();

            _userLicenses.OwnedPackageIds.AddRange(packageInfos.Select(e => e.Id).ToList());

            // Handling packages that are normally purchased or added via cd-key
            foreach (var package in packageInfos)
            {
                // Removing any free weekends that are no longer active
                if (package.IsFreeWeekend && package.FreeWeekendHasExpired)
                {
                    continue;
                }

                _userLicenses.OwnedAppIds.AddRange(package.AppIds);
                _userLicenses.OwnedDepotIds.AddRange(package.DepotIds);
            }
        }
    }

    public sealed class UserLicenses
    {
        public int LicenseCount => OwnedPackageIds.Count;

        public List<KeyValue2> AppIdPurchaseDateLookup = new List<KeyValue2>();

        public HashSet<uint> OwnedPackageIds { get; } = new HashSet<uint>();
        public HashSet<uint> OwnedAppIds { get; } = new HashSet<uint>();
        public HashSet<uint> OwnedDepotIds { get; } = new HashSet<uint>();

        public override string ToString()
        {
            return $"Packages : {OwnedPackageIds.Count} Apps : {OwnedAppIds.Count} Depots : {OwnedDepotIds.Count}";
        }
    }

    public class KeyValue2
    {
        public uint AppId { get; set; }
        public DateTime Value { get; set; }

        public KeyValue2(uint appId, DateTime val)
        {
            AppId = appId;
            Value = val;
        }

        public override string ToString()
        {
            return $"Id : {AppId} Purchased : {Value}";
        }
    }
}