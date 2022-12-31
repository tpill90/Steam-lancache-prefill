﻿namespace SteamPrefill
{
    //TODO Possibly change this to metadata
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(List<uint>))]
    [JsonSerializable(typeof(Dictionary<uint, HashSet<ulong>>))]
    [JsonSerializable(typeof(GetMostPlayedGamesResponse))]
    [JsonSerializable(typeof(UserLicenses))]
    internal sealed partial class SerializationContext : JsonSerializerContext
    {
    }
}