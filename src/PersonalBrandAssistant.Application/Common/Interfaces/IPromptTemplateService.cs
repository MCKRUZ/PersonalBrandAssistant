namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IPromptTemplateService
{
    Task<string> RenderAsync(string agentName, string templateName, Dictionary<string, object> variables);
    string[] ListTemplates(string agentName);
}
