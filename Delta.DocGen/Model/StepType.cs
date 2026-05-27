using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepType
{
    Given,
    When,
    Then,
    /// <summary>SpecFlow/Reqnroll [StepDefinition] — universal attribute matching any Given/When/Then context.</summary>
    StepDefinition,
}
