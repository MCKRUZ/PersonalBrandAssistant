namespace PersonalBrandAssistant.Application.Common.Models;

public class BlogPublishOptions
{
    public const string SectionName = "BlogPublish";

    public string RepoOwner { get; set; } = string.Empty;
    public string RepoName { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public string ContentPath { get; set; } = "content/blog";
    public string FilePattern { get; set; } = "{slug}.html";
    public string TemplatePath { get; set; } = "templates/blog-post.html";
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public string DeployVerificationUrlPattern { get; set; } = string.Empty;
    public int DeployVerificationInitialDelaySeconds { get; set; } = 30;
    public int DeployVerificationMaxRetries { get; set; } = 5;
}
