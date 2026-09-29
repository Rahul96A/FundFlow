using System.Text.Json.Serialization;

namespace FundFlow.Contracts.Common;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortDirection
{
    Asc = 1,
    Desc = 2,
}
