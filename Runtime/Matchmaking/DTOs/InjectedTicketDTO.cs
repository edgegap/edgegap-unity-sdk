using System;
using Newtonsoft.Json;

namespace Edgegap.Matchmaking
{
    public class InjectedTicketDTO<A>
    {
        [JsonProperty("id")]
        public string ID;

        [JsonProperty("created_at")]
        public DateTime CreatedAt;

        [JsonProperty("player_ip")]
        public string PlayerIP;

        [JsonProperty("group_id")]
        public string GroupID;

        [JsonProperty("team_id")]
        public string TeamID;

        [JsonProperty("attributes")]
        public A Attributes;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
