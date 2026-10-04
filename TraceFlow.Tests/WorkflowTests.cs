using TraceFlow.Domain;
using Xunit;

public class WorkflowTests
{
    [Theory]
    [InlineData(Station.Assembly, Station.Test)]
    [InlineData(Station.Test, Station.Packing)]
    [InlineData(Station.Packing, Station.Shipped)]
    public void Next_moves_forward(Station from, Station expected) =>
        Assert.Equal(expected, Workflow.Next(from));

    [Fact]
    public void Shipped_has_no_next() => Assert.Null(Workflow.Next(Station.Shipped));
}
