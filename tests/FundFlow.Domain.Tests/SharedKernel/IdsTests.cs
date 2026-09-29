using System.Data.SqlTypes;
using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Tests.SharedKernel;

public class IdsTests
{
    [Fact]
    public void New_generates_unique_ids()
    {
        var ids = Enumerable.Range(0, 10_000).Select(_ => Ids.New()).ToHashSet();

        ids.Should().HaveCount(10_000);
    }

    [Fact]
    public void New_embeds_the_timestamp_in_the_bytes_sql_server_sorts_by_first()
    {
        var at = new DateTimeOffset(2026, 3, 14, 15, 9, 26, TimeSpan.Zero);

        var id = Ids.New(at);

        Ids.ExtractTimestampMilliseconds(id).Should().Be(at.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Ids_created_later_sort_after_earlier_ones_in_sql_server_order()
    {
        // SqlGuid implements SQL Server's uniqueidentifier comparison, which is what a clustered index uses.
        var start = DateTimeOffset.UtcNow;
        var ids = Enumerable.Range(0, 500).Select(i => Ids.New(start.AddMilliseconds(i * 5))).ToList();

        var sqlOrdered = ids.OrderBy(id => new SqlGuid(id)).ToList();

        sqlOrdered.Should().Equal(ids, "ids generated later must never insert before earlier ones");
    }
}
