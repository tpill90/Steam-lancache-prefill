﻿namespace SteamPrefill.Models
{
    public class DepotInfo
    {
        public uint DepotId { get; init; }
        public string Name { get; }

        public ulong? ManifestId { get; set; }

        public uint ContainingAppId { get; }

        /// <summary>
        /// Determines if a depot is a "linked" depot.  If the current depot is linked, it won't actually have a manifest to download under the current app.
        /// Instead, the depot will need to be downloaded from the linked app.
        /// </summary>
        public uint? DepotFromApp { get; }
        public uint? DlcAppId { get; }

        // If there is no manifest we can't download this depot, and if there is no shared depot then we can't look up a related manifest we could use
        public bool IsInvalidDepot => ManifestId == null && DepotFromApp == null;
        
        public List<OperatingSystem> SupportedOperatingSystems { get; init; } = new List<OperatingSystem>();
        public Architecture Architecture { get; init; }
        public List<Language> Languages { get; init; } = new List<Language>();
        public bool? LowViolence { get; init; }

        public string ManifestFileName => $"{AppConfig.CacheDir}/{ContainingAppId}_{DepotId}_{ManifestId}.bin";

        [UsedImplicitly]
        public DepotInfo()
        {
            // Parameter-less constructor for deserialization
        }

        public DepotInfo(KeyValue rootKey, uint appId)
        {
            DepotId = uint.Parse(rootKey.Name);
            Name = rootKey["name"].Value;

            ManifestId = rootKey["manifests"]["public"].AsUnsignedLongNullable();
            DepotFromApp = rootKey["depotfromapp"].AsUnsignedIntNullable();
            DlcAppId = rootKey["dlcappid"].AsUnsignedIntNullable();

            // Config Section
            if (rootKey["config"]["oslist"] != KeyValue.Invalid)
            {
                SupportedOperatingSystems = rootKey["config"]["oslist"].Value
                                                                         .Split(',')
                                                                         .Select(e => OperatingSystem.Parse(e))
                                                                         .ToList();
            }
            Architecture = rootKey["config"]["osarch"].AsEnum<Architecture>();
            
            Languages = rootKey["config"]["language"].SplitCommaDelimited()
                                                    .Select(e => Language.Parse(e))
                                                    .ToList();

            if (rootKey["config"]["lowviolence"].Value is "1")
            {
                LowViolence = true;
            }

            // Determines what app actually owns the depot, by default it is the current app.
            // However in the case of a linked/DLC app, the depot will need to be downloaded using the referenced app's id
            
            ContainingAppId = appId;
            if (DlcAppId != null)
            {
                ContainingAppId = DlcAppId.Value;
            }
            if (DepotFromApp != null)
            {
                ContainingAppId = DepotFromApp.Value;
            }
        }

        public override string ToString()
        {
            return $"{DepotId} - {Name}";
        }
    }
}