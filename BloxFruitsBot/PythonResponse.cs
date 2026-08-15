using System; 
using System.Text.Json.Serialization; 
 
namespace BloxFruitsBot 
{ 
    public class PythonResponse 
    { 
        [JsonPropertyName("HpRatio")] 
        public double HpRatio { get; set; } 
 
        [JsonPropertyName("EnergyRatio")] 
        public double EnergyRatio { get; set; } 
 
        [JsonPropertyName("TargetVisible")] 
        public bool TargetVisible { get; set; } 
 
        [JsonPropertyName("TargetId")]
        public string TargetId { get; set; } = string.Empty;

        [JsonPropertyName("DecisionIntent")]
        public string DecisionIntent { get; set; } = string.Empty;

        [JsonPropertyName("ActionKeys")]
        public System.Collections.Generic.List<string> ActionKeys { get; set; } = new System.Collections.Generic.List<string>();

        [JsonPropertyName("HoldKeys")]
        public System.Collections.Generic.List<string> HoldKeys { get; set; } = new System.Collections.Generic.List<string>();

        [JsonPropertyName("ActionDuration")]
        public double ActionDuration { get; set; } = 1.0;
    } 
}
