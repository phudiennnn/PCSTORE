using PCStore.API.Services;

namespace PCStore.Tests;

public class SupportTicketWorkflowTests
{
    [Theory]
    [InlineData("Open", "Open")]
    [InlineData("inprogress", "InProgress")]
    [InlineData(" Resolved ", "Resolved")]
    [InlineData("Closed", "Closed")]
    public void NormalizeStatus_AcceptsKnownStatusValues(string input, string expected)
    {
        Assert.Equal(expected, SupportTicketWorkflow.NormalizeStatus(input));
    }

    [Fact]
    public void NormalizeStatus_RejectsUnknownOrMissingStatus()
    {
        Assert.Null(SupportTicketWorkflow.NormalizeStatus("Pending"));
        Assert.Null(SupportTicketWorkflow.NormalizeStatus(null));
        Assert.Null(SupportTicketWorkflow.NormalizeStatus(" "));
    }
}
