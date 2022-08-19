﻿namespace LancachePrefill.Common
{
    public static class UpdateChecker
    {
        //TODO update docs
        /// <summary>
        /// Compares the current application version against the newest version available on Github Releases.  If there is a newer version, displays a message
        /// to the user.
        /// </summary>
        public static async Task CheckForUpdatesAsync(Type executingAppType, string repoName, string cacheDir)
        {
            string lastUpdateCheckFile = Path.Combine(cacheDir, "lastUpdateCheck.txt");
            try
            {
                //Will only check for updates once every 3 days. 
                var fileInfo = new FileInfo(lastUpdateCheckFile);
                if (fileInfo.Exists && fileInfo.LastWriteTimeUtc.AddDays(3) > DateTime.UtcNow)
                {
                    return;
                }

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
                httpClient.DefaultRequestHeaders.Add("User-Agent", repoName);

                // Query Github for a list of all available releases
                var response = await httpClient.GetStringAsync(new Uri($"https://api.github.com/repos/{repoName}/releases"));
                GithubRelease latestRelease = JsonSerializer.Deserialize(response, LancachePrefill.Common.SerializationContext.Default.ListGithubRelease)
                                                            .OrderByDescending(e => e.PublishedAt)
                                                            .First();

                // Compare the available releases against our known releases
                var latestVersion = latestRelease.TagName.Replace("v", "");
                var assemblyVersion = executingAppType.Assembly.GetName().Version.ToString(3);
                if (latestVersion != assemblyVersion)
                {
                    WriteUpdateMessage(assemblyVersion, latestVersion, repoName);
                }

                await File.WriteAllTextAsync(lastUpdateCheckFile, DateTime.Now.ToString());
            }
            catch
            {
                // Doesn't matter if this fails.  Its non-critical to the application's function
            }
        }

        private static void WriteUpdateMessage(string currentVersion, string updateVersion, string repoName)
        {
            var table = new Table
            {
                ShowHeaders = false,
                Border = TableBorder.Rounded,
                BorderStyle = new Style(Color.Yellow4)
            };
            table.AddColumn("");

            // Add some rows
            table.AddRow("");
            table.AddRow($"A newer version is available {currentVersion} → {Olive(updateVersion)}");
            table.AddRow("");
            table.AddRow("Download at :  ");
            table.AddRow(LightBlue($"https://api.github.com/repos/{repoName}/releases"));
            table.AddRow("");

            // Render the table to the console
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(List<GithubRelease>))]
    internal partial class SerializationContext : JsonSerializerContext
    {
    }

    public class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        public override string ToString()
        {
            return $"{TagName} - {PublishedAt}";
        }
    }
}
