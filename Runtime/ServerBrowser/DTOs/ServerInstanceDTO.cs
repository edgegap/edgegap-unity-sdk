using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Edgegap.ServerBrowser
{
    public class InstanceDTO<InstanceMetadata, SlotMetadata>
        where InstanceMetadata : MetadataDTO, new()
        where SlotMetadata : MetadataDTO, new()
    {
        [JsonProperty("request_id")]
        public string RequestID;

        [JsonProperty("created_at")]
        public DateTime CreatedAt;

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt;

        [JsonProperty("metadata")]
        public InstanceMetadata Metadata;

        [JsonProperty("server")]
        public DeploymentDTO Server;

        [JsonProperty("slots")]
        public List<SlotDTO<SlotMetadata>> Slots;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public class InstanceUpdateDTO<InstanceMetadata>
        where InstanceMetadata : MetadataDTO, new()
    {
        [JsonProperty("metadata")]
        public InstanceMetadata Metadata;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public class InstanceListResponseDTO<InstanceMetadata, SlotMetadata>
        where InstanceMetadata : MetadataDTO, new()
        where SlotMetadata : MetadataDTO, new()
    {
        [JsonProperty("items")]
        public List<InstanceDTO<InstanceMetadata, SlotMetadata>> ServerInstances;

        [JsonProperty("pagination")]
        public PaginationDTO Pagination;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
