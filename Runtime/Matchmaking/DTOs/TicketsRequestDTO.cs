using System.Collections.Generic;
using Newtonsoft.Json;

namespace Edgegap.Matchmaking
{
    public abstract class TicketsRequestDTO<A>
    {
        [JsonProperty("profile")]
        public string Profile;

        [JsonProperty("attributes")]
        public A Attributes;

#nullable enable
        [JsonProperty("player_ip")]
        public string? PlayerIP;

#nullable disable

        public TicketsRequestDTO(string profile)
        {
            Profile = profile;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public class SimpleTicketsRequestDTO : TicketsRequestDTO<LatenciesAttributesDTO>
    {
        public SimpleTicketsRequestDTO(Dictionary<string, float> latencyBeacons)
            : base("simple-example")
        {
            Attributes = new LatenciesAttributesDTO(latencyBeacons);
        }
    }

    public class AdvancedTicketsRequestDTO : TicketsRequestDTO<AdvancedTicketsAttributesDTO>
    {
        public AdvancedTicketsRequestDTO(
            Dictionary<string, float> latencyBeacons,
            int eloRating,
            string selectedGameMode,
            string[] selectedMap,
            string[] backfillGroupSize
        )
            : base("advanced-example")
        {
            Attributes = new AdvancedTicketsAttributesDTO(
                latencyBeacons,
                eloRating,
                selectedGameMode,
                selectedMap,
                backfillGroupSize
            );
        }
    }

    public class AdvancedTicketsAttributesDTO : LatenciesAttributesDTO
    {
        [JsonProperty("elo_rating")]
        public int EloRating;

        [JsonProperty("selected_game_mode")]
        public string SelectedGameMode;

        [JsonProperty("selected_map")]
        public string[] SelectedMap;

        [JsonProperty("backfill_group_size")]
        public string[] BackfillGroupSize;

        public AdvancedTicketsAttributesDTO(
            Dictionary<string, float> beacons,
            int eloRating,
            string selectedGameMode,
            string[] selectedMap,
            string[] backfillGroupSize
        )
            : base(beacons)
        {
            EloRating = eloRating;
            SelectedGameMode = selectedGameMode;
            SelectedMap = selectedMap;
            BackfillGroupSize = backfillGroupSize;
        }
    }

    public class LatenciesAttributesDTO
    {
        [JsonProperty("beacons")]
        public Dictionary<string, float> Beacons;

        public LatenciesAttributesDTO(Dictionary<string, float> beacons)
        {
            Beacons = beacons;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
    
    public abstract class GroupUpRequestDTO<A> : TicketsRequestDTO<A>
    {
        [JsonProperty("is_ready")]
        public bool IsReady;

        public GroupUpRequestDTO(string profile, bool isReady = false)
            : base(profile)
        {
            IsReady = isReady;
        }
    }

    public class SimpleGroupUpRequestDTO : GroupUpRequestDTO<LatenciesAttributesDTO>
    {
        public SimpleGroupUpRequestDTO(Dictionary<string, float> latencyBeacons)
            : base("simple-example")
        {
            Attributes = new LatenciesAttributesDTO(latencyBeacons);
        }
    }
}
