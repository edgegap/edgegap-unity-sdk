using System.Collections.Generic;
using Newtonsoft.Json;

namespace Edgegap.Matchmaking
{
    public class BackfillRequestDTO<A>
    {
        [JsonProperty("profile")]
        public string Profile;

        [JsonProperty("attributes")]
        public BackfillAttributes Attributes;

        [JsonProperty("tickets")]
        public Dictionary<string, BackfillTicketMemberDTO<A>> Tickets;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public class BackfillAttributes
    {
        [JsonProperty("assignment")]
        public DeploymentDTO Assignment;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
