namespace PersonalBrandAssistant.Infrastructure.Services;

public static class AgentExecutionContext
{
    private static readonly AsyncLocal<Guid?> _currentExecutionId = new();

    public static Guid? CurrentExecutionId
    {
        get => _currentExecutionId.Value;
        set => _currentExecutionId.Value = value;
    }
}
