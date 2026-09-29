using FundFlow.Application.Common.Paging;
using FundFlow.Contracts.Common;
using FundFlow.Domain.SharedKernel;

namespace FundFlow.Application.Tests.Paging;

public class PagingTests
{
    private sealed class Row : Entity
    {
        public Row(string name, int rank)
        {
            Name = name;
            Rank = rank;
        }

        public string Name { get; }

        public int Rank { get; }
    }

    private sealed record TestQuery : PagedQuery;

    private static readonly SortMap<Row> Sorts = new SortMap<Row>()
        .Add("name", r => r.Name)
        .Add("rank", r => r.Rank)
        .Default("rank", descending: true);

    private static IQueryable<Row> Rows() =>
        new[] { new Row("carol", 2), new Row("alice", 3), new Row("bob", 1), new Row("dave", 3) }.AsQueryable();

    [Fact]
    public void Whitelisted_columns_sort_in_the_requested_direction()
    {
        Sorts.Apply(Rows(), "name", SortDirection.Asc).Select(r => r.Name).Should().Equal("alice", "bob", "carol", "dave");
        Sorts.Apply(Rows(), "NAME", SortDirection.Desc).Select(r => r.Name).Should().Equal("dave", "carol", "bob", "alice");
    }

    [Fact]
    public void Unknown_sort_columns_fall_back_to_the_default_instead_of_reaching_the_query()
    {
        var sorted = Sorts.Apply(Rows(), "passwordHash; DROP TABLE Users", SortDirection.Asc).ToList();

        sorted.Select(r => r.Rank).Should().BeInDescendingOrder("the default sort is rank descending, whatever direction was asked for");
    }

    [Fact]
    public void Missing_sort_uses_the_default()
    {
        Sorts.Apply(Rows(), null, null).Select(r => r.Rank).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Equal_sort_keys_are_broken_by_id_so_pages_never_overlap()
    {
        var rows = Rows().ToList();
        var page1 = Sorts.Apply(rows.AsQueryable(), "rank", SortDirection.Asc).Skip(0).Take(2).ToList();
        var page2 = Sorts.Apply(rows.AsQueryable(), "rank", SortDirection.Asc).Skip(2).Take(2).ToList();

        page1.Concat(page2).Select(r => r.Id).Should().OnlyHaveUniqueItems().And.HaveCount(4);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void Page_numbers_are_clamped(int requested, int expected)
    {
        new TestQuery { Page = requested }.SafePage.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(25, 25)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(int.MaxValue, 100)]
    public void Page_sizes_are_capped_so_no_endpoint_returns_an_unbounded_collection(int requested, int expected)
    {
        new TestQuery { PageSize = requested }.SafePageSize.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  ada ", "ada")]
    public void Search_terms_are_trimmed(string? input, string? expected)
    {
        new TestQuery { Search = input }.SearchTerm.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 25, 0)]
    [InlineData(1, 25, 1)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    [InlineData(1000, 25, 40)]
    public void Total_pages_are_computed_from_the_count(int total, int pageSize, int expectedPages)
    {
        var response = PagedResponse<string>.Create([], 1, pageSize, total);

        response.TotalPages.Should().Be(expectedPages);
        response.TotalCount.Should().Be(total);
    }

    [Theory]
    [InlineData("100%", "100\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("[x]", "\\[x]")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("plain", "plain")]
    public void Like_wildcards_in_user_input_are_escaped(string input, string expected)
    {
        PagingExtensions.EscapeLike(input).Should().Be(expected);
    }
}
