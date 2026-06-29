using System.Collections.Generic;
using Newtonsoft.Json;

namespace Edgegap.Matchmaking
{
    public class BackfillResponseDTO<A>
    {
        [JsonProperty("id")]
        public string ID;

        [JsonProperty("profile")]
        public string Profile;

        [JsonProperty("tickets")]
        public Dictionary<string, BackfillTicketMemberDTO<A>> Tickets;

        [JsonProperty("status")]
        public string Status;

#nullable enable
        [JsonProperty("assigned_ticket")]
        public BackfillTicketMemberDTO<A>? AssignedTicket;

#nullable disable

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
