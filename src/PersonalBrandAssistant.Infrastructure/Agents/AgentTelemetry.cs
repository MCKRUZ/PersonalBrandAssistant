using System.Diagnostics;

namespace PersonalBrandAssistant.Infrastructure.Agents;

public static class AgentTelemetry
{
    public const string SourceName = "PersonalBrandAssistant.Agents";
    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}
