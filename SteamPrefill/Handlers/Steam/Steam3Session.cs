using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using Spectre.Console;
using SteamKit2;
using SteamKit2.CDN;
using SteamPrefill.Models.Exceptions;
using SteamPrefill.Settings;
using SteamPrefill.Utils;
using static SteamPrefill.Utils.SpectreColors;
using JsonSerializer = Utf8Json.JsonSerializer;

namespace SteamPrefill.Handlers.Steam
{
    //TODO document this class
    public class Steam3Session
    {
        private readonly string _packageCountPath = $"{AppConfig.CacheDir}/packageCount.txt";
        private readonly string _ownedAppIdsPath = $"{AppConfig.CacheDir}/OwnedAppIds.json";
        private readonly string _ownedDepotIdsPath = $"{AppConfig.CacheDir}/OwnedDepotIds.json";
        // TODO make this private again
        public HashSet<uint> OwnedAppIds { get; set; } = new HashSet<uint>();
        private HashSet<uint> OwnedDepotIds { get; set; } = new HashSet<uint>();
        
        private readonly SteamClient _steamClient;
        private readonly SteamUser _steamUser;
        public readonly SteamContent steamContent;
        public readonly SteamApps SteamAppsApi;
        //TODO not sure if this should be public or not
        public readonly Client CdnClient;

        private readonly CallbackManager _callbackManager;
        
        private SteamUser.LogOnDetails _logonDetails;
        private readonly IAnsiConsole _ansiConsole;

        public Steam3Session(IAnsiConsole ansiConsole)
        {
            _ansiConsole = ansiConsole;
            
            _steamClient = new SteamClient();
            _steamUser = _steamClient.GetHandler<SteamUser>();
            SteamAppsApi = _steamClient.GetHandler<SteamApps>();
            steamContent = _steamClient.GetHandler<SteamContent>();

            _callbackManager = new CallbackManager(_steamClient);
            _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(loggedOn => _loggedOnCallbackResult = loggedOn);
            _callbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(UpdateMachineAuthCallback);
            _callbackManager.Subscribe<SteamUser.LoginKeyCallback>(LoginKeyCallback);
            _callbackManager.Subscribe<SteamApps.LicenseListCallback>(LicenseListCallback);
            _callbackManager.RunCallbacks();

            CdnClient = new Client(_steamClient);
        }
        
        // TODO document
        public void LoginToSteam()
        {
            ConfigureLoginDetails();

            int retryCount = 0;
            bool logonSuccess = false;
            while (!logonSuccess)
            {
                SteamUser.LoggedOnCallback logonResult = null;
                _ansiConsole.StatusSpinner().Start("Connecting to Steam...", ctx =>
                {
                    ConnectToSteam();

                    ctx.Status = "Logging into Steam...";
                    logonResult = AttemptSteamLogin();
                });

                logonSuccess = HandleLogonResult(logonResult);

                retryCount++;
                if (retryCount == 5)
                {
                    throw new SteamLoginException("Unable to login to Steam!  Try again in a few moments...");
                }
            }

            _ansiConsole.StatusSpinner().Start("Saving Steam session...", _ =>
            {
                TryWaitForLoginKey();
            });
        }

        #region Logging into Steam

        private void ConnectToSteam()
        {
            _steamClient.Connect();
            while (!_steamClient.IsConnected)
            {
                _callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
        }
        
        //TODO document
        public void ConfigureLoginDetails()
        {
            var username = UserAccountStore.Instance.GetUsername(_ansiConsole);

            string loginKey;
            UserAccountStore.Instance.LoginKeys.TryGetValue(username, out loginKey);
            
            _logonDetails = new SteamUser.LogOnDetails
            {
                Username = username,
                Password = loginKey == null ? _ansiConsole.ReadPassword() : null,
                ShouldRememberPassword = true,
                LoginKey = loginKey,
                LoginID = 0x534DD2
            };
            // Sentry file is required when using Steam Guard w\ email
            if (UserAccountStore.Instance.SentryData.TryGetValue(_logonDetails.Username, out var bytes))
            {
                _logonDetails.SentryFileHash = bytes.ToSha1();
            }
        }

        //TODO document
        private SteamUser.LoggedOnCallback _loggedOnCallbackResult;
        private SteamUser.LoggedOnCallback AttemptSteamLogin()
        {
            var loginTimeoutAfter = DateTime.Now.AddSeconds(30);

            _loggedOnCallbackResult = null;
            _steamUser.LogOn(_logonDetails);

            // Busy waiting for the callback to complete, then we can return the callback value synchronously
            while (_loggedOnCallbackResult == null)
            {
                _callbackManager.RunWaitCallbacks(timeout: TimeSpan.FromSeconds(3));
                if (DateTime.Now > loginTimeoutAfter)
                {
                    throw new SteamLoginException("Timeout connecting to Steam3.");
                }
            }
            return _loggedOnCallbackResult;
        }

        //TODO document
        private int _failedLogonAttempts;
        private bool HandleLogonResult(SteamUser.LoggedOnCallback logonResult)
        {
            var loggedOn = logonResult;

            // If the account has 2-Factor login enabled, then we will need to re-login with the supplied code
            if (loggedOn.Result == EResult.AccountLoginDeniedNeedTwoFactor)
            {
                _logonDetails.TwoFactorCode = _ansiConsole.Prompt(new TextPrompt<string>(Yellow("2FA required for login.") +
                                                                                         $"  Please enter your {Cyan("Steam Guard code")} from your authenticator app : "));
                return false;
            }
            if (loggedOn.Result == EResult.TwoFactorCodeMismatch)
            {
                _logonDetails.TwoFactorCode = _ansiConsole.Prompt(new TextPrompt<string>(Red("Login failed. Incorrect Steam Guard code") +
                                                                                         "  Please try again : "));
                return false;
            }

            var loginKeyExpired = _logonDetails.LoginKey != null && loggedOn.Result == EResult.InvalidPassword;
            if (loginKeyExpired)
            {
                UserAccountStore.Instance.LoginKeys.Remove(_logonDetails.Username);
                _logonDetails.LoginKey = null;
                _logonDetails.Password = _ansiConsole.ReadPassword("Steam session expired!  Password re-entry required!");
                return false;
            }

            if (loggedOn.Result == EResult.InvalidPassword)
            {
                _failedLogonAttempts++;
                if (_failedLogonAttempts == 3)
                {
                    _ansiConsole.LogMarkupLine(Red("Invalid username/password combination!  Check your login credential validity, and try again.."));
                    throw new AuthenticationException("Invalid username/password");
                }

                _logonDetails.Password = _ansiConsole.ReadPassword($"{Red("Invalid password!  Please re-enter your password!")}");
                return false;
            }

            // SteamGuard code required
            if (loggedOn.Result == EResult.AccountLogonDenied)
            {
                _logonDetails.AuthCode = _ansiConsole.Prompt(new TextPrompt<string>(Yellow("This account is protected by Steam Guard.") +
                                                                                    "  Please enter the code sent to your email address:  "));
                return false;
            }
            if (loggedOn.Result == EResult.ServiceUnavailable)
            {
                throw new SteamLoginException($"{Red("Unable to login to Steam")} : Service is unavailable");
            }
            if (loggedOn.Result != EResult.OK)
            {
                throw new SteamLoginException($"Unable to login to Steam.  An unknown error occurred : {loggedOn.Result}");
            }

            _ansiConsole.LogMarkupLine($"Logged '{Cyan(_logonDetails.Username)}' into Steam");

            // Forcing a garbage collect to remove stored password from memory
            if (_logonDetails.Password != null)
            {
                _logonDetails.Password = null;
                GC.Collect(3, GCCollectionMode.Forced);
            }
            
            return true;
        }
        
        bool _receivedLoginKey;
        //TODO cleanup + comment
        //TODO this takes a long time. ~5s
        public void TryWaitForLoginKey()
        {
            if (_logonDetails.LoginKey != null)
            {
                return;
            }

            var totalWaitPeriod = DateTime.Now.AddSeconds(3);
            while (true)
            {
                if (DateTime.Now >= totalWaitPeriod)
                {
                    _ansiConsole.LogMarkupLine(Red("Failed to save Steam session key.  Steam account will not stay logged in..."));
                    return;
                }
                if (_receivedLoginKey)
                {
                    return;
                }
                _callbackManager.RunWaitAllCallbacks(TimeSpan.FromSeconds(3));
            }
        }

        private void LoginKeyCallback(SteamUser.LoginKeyCallback loginKey)
        {
            UserAccountStore.Instance.LoginKeys[_logonDetails.Username] = loginKey.LoginKey;
            UserAccountStore.Save();

            _steamUser.AcceptNewLoginKey(loginKey);
            _receivedLoginKey = true;
        }
        
        #endregion

        #region Other Auth Methods
        
        /// <summary>
        /// The UpdateMachineAuth event will be triggered once the user has logged in with either Steam Guard or 2FA enabled.
        /// This callback handler will save a "sentry file" for future logins, that will allow an existing Steam session to be reused,
        /// without requiring a password.
        ///
        /// Despite the fact that this will be triggered for both Steam Guard + 2FA, the sentry file is only required for re-login when using an
        /// account with Steam Guard enabled.
        /// </summary>
        private void UpdateMachineAuthCallback(SteamUser.UpdateMachineAuthCallback machineAuth)
        {
            UserAccountStore.Instance.SentryData[_logonDetails.Username] = machineAuth.Data;
            UserAccountStore.Save();

            var authResponse = new SteamUser.MachineAuthDetails
            {
                BytesWritten = machineAuth.BytesToWrite,
                FileName = machineAuth.FileName,
                FileSize = machineAuth.BytesToWrite,
                Offset = machineAuth.Offset,
                // should be the sha1 hash of the sentry file we just received
                SentryFileHash = machineAuth.Data.ToSha1(),
                OneTimePassword = machineAuth.OneTimePassword,
                LastError = 0,
                Result = EResult.OK,
                JobID = machineAuth.JobID
            };
            _steamUser.SendMachineAuthResponse(authResponse);
        }
        
        #endregion

        #region LoadAccountLicenses

        private bool _loadAccountLicensesIsRunning = true;
        //TODO rename to WaitForLicenses or something similar
        //TODO document when this runs
        public void LoadAccountLicenses()
        {
            _ansiConsole.StatusSpinner().Start("Retreiving owned apps...", _ =>
            {
                while (_loadAccountLicensesIsRunning)
                {
                    _callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                }
            });
        }

        private void LicenseListCallback(SteamApps.LicenseListCallback licenseList)
        {
            _loadAccountLicensesIsRunning = false;
            if (licenseList.Result != EResult.OK)
            {
                //TODO handle
                _ansiConsole.WriteLine($"Unable to get license list: {licenseList.Result}");
                throw new Exception("aborted");
            }
            LoadPackageInfo(licenseList.LicenseList);
        }

        //TODO document
        private void LoadPackageInfo(IReadOnlyCollection<SteamApps.LicenseListCallback.License> licenseList)
        {
            // If we haven't bought any new games (or free-to-play) since the last run, we can reload our owned Apps/Depots
            if (File.Exists(_packageCountPath) && File.ReadAllText(_packageCountPath) == licenseList.Count.ToString())
            {
                if (File.Exists(_ownedAppIdsPath) && File.Exists(_ownedDepotIdsPath))
                {
                    OwnedAppIds = JsonSerializer.Deserialize<HashSet<uint>>(File.ReadAllText(_ownedAppIdsPath));
                    OwnedDepotIds = JsonSerializer.Deserialize<HashSet<uint>>(File.ReadAllText(_ownedDepotIdsPath));
                    return;
                }
            }
            
            var packageRequests = licenseList.Select(package => new SteamApps.PICSRequest(package.PackageID)).ToList();
            //TODO async
            var jobResult = SteamAppsApi.PICSGetProductInfo(new List<SteamApps.PICSRequest>(), packageRequests).ToTask().Result;
            var packages = jobResult.Results.SelectMany(e => e.Packages)
                                    .Select(e => e.Value)
                                    .ToList();
            foreach (var package in packages)
            {
                // Removing any free weekends that are no longer active
                //TODO turn this into a property on some class
                var expiryTimeKey = package.KeyValues["extended"].Children.FirstOrDefault(e => e.Name == "expirytime");
                if (expiryTimeKey != null)
                {
                    var expiryTimeUtc = DateTimeOffset.FromUnixTimeSeconds(1630256400).DateTime;
                    if (DateTime.Now > expiryTimeUtc)
                    {
                        continue;
                    }
                }
                foreach (KeyValue appId in package.KeyValues["appids"].Children)
                {
                    OwnedAppIds.Add(UInt32.Parse(appId.Value));
                }
                foreach (KeyValue appId in package.KeyValues["depotids"].Children)
                {
                    OwnedDepotIds.Add(UInt32.Parse(appId.Value));
                }
            }

            // Serializing this data to speedup subsequent runs
            File.WriteAllText(_ownedAppIdsPath, JsonSerializer.ToJsonString(OwnedAppIds));
            File.WriteAllText(_ownedDepotIdsPath, JsonSerializer.ToJsonString(OwnedDepotIds));
            File.WriteAllText(_packageCountPath, packageRequests.Count.ToString());
        }
        #endregion

        //TODO move this into app handler?
        /// <summary>
        /// Checks against the list of currently owned apps to determine if the user is able to download this app.
        /// </summary>
        /// <param name="appid">Id of the application to check for access</param>
        /// <returns>True if the user has access to the app</returns>
        public bool AccountHasAppAccess(uint appid)
        {
            return OwnedAppIds.Contains(appid);
        }

        //TODO move this into depot handler?
        /// <summary>
        /// Checks against the list of currently owned apps to determine if the user is able to download this depot.
        /// </summary>
        /// <param name="depotId">Id of the depot to check for access</param>
        /// <returns>True if the user has access to the depot</returns>
        public bool AccountHasDepotAccess(uint depotId)
        {
            return OwnedDepotIds.Contains(depotId);
        }
    }
}