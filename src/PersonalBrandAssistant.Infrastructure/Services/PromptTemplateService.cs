using System.Collections.Concurrent;
using Fluid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services;

public sealed class PromptTemplateService : IPromptTemplateService, IDisposable
{
    private readonly string _promptsPath;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PromptTemplateService> _logger;
    private readonly ConcurrentDictionary<string, Lazy<IFluidTemplate>> _cache = new();
    private readonly TemplateOptions _templateOptions;
    private readonly FileSystemWatcher? _watcher;

    public PromptTemplateService(
        string promptsPath,
        IHostEnvironment environment,
        ILogger<PromptTemplateService> logger)
    {
        _promptsPath = Path.GetFullPath(promptsPath);
        _environment = environment;
        _logger = logger;

        _templateOptions = new TemplateOptions();
        _templateOptions.MemberAccessStrategy.Register<BrandProfilePromptModel>();
        _templateOptions.MemberAccessStrategy.Register<ContentPromptModel>();

        if (_environment.IsDevelopment() && Directory.Exists(_promptsPath))
        {
            _watcher = new FileSystemWatcher(_promptsPath, "*.liquid")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
            };
            _watcher.Changed += OnTemplateFileChanged;
            _watcher.Created += OnTemplateFileChanged;
            _watcher.Deleted += OnTemplateFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public async Task<string> RenderAsync(
        string agentName,
        string templateName,
        Dictionary<string, object> variables)
    {
        ValidatePathSegment(agentName, nameof(agentName));
        ValidatePathSegment(templateName, nameof(templateName));

        var cacheKey = $"{agentName}/{templateName}";
        var template = GetOrParseTemplate(cacheKey);

        var context = new TemplateContext(_templateOptions);

        // Inject brand voice block if shared template exists (check cache first to avoid filesystem hit)
        var brandVoiceKey = "shared/brand-voice";
        if (_cache.ContainsKey(brandVoiceKey) || File.Exists(Path.Combine(_promptsPath, "shared", "brand-voice.liquid")))
        {
            var brandVoiceTemplate = GetOrParseTemplate(brandVoiceKey);
            var brandVoiceContext = new TemplateContext(_templateOptions);
            foreach (var (key, value) in variables)
            {
                brandVoiceContext.SetValue(key, value);
            }
            var brandVoiceBlock = await brandVoiceTemplate.RenderAsync(brandVoiceContext);
            context.SetValue("brand_voice_block", brandVoiceBlock);
        }

        foreach (var (key, value) in variables)
        {
            context.SetValue(key, value);
        }

        var result = await template.RenderAsync(context);
        return result;
    }

    public string[] ListTemplates(string agentName)
    {
        ValidatePathSegment(agentName, nameof(agentName));

        var agentDir = Path.Combine(_promptsPath, agentName);
        if (!Directory.Exists(agentDir))
            return [];

        return Directory.GetFiles(agentDir, "*.liquid")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Order()
            .ToArray();
    }

    private IFluidTemplate GetOrParseTemplate(string cacheKey)
    {
        var lazy = _cache.GetOrAdd(cacheKey, key => new Lazy<IFluidTemplate>(() =>
        {
            var filePath = Path.Combine(_promptsPath, $"{key}.liquid");
            var fullPath = Path.GetFullPath(filePath);

            if (!fullPath.StartsWith(_promptsPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Template path escapes prompts directory: {key}");

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Prompt template not found: {fullPath}", fullPath);

            var content = File.ReadAllText(fullPath);
            var parser = new FluidParser();
            if (!parser.TryParse(content, out var template, out var error))
                throw new InvalidOperationException($"Failed to parse template '{key}': {error}");

            _logger.LogDebug("Template parsed and cached: {CacheKey}", key);
            return template;
        }));

        return lazy.Value;
    }

    private static void ValidatePathSegment(string segment, string paramName)
    {
        if (segment.Contains("..") || segment.Contains('/') || segment.Contains('\\'))
            throw new ArgumentException($"Invalid path segment: {segment}", paramName);
    }

    private void OnTemplateFileChanged(object sender, FileSystemEventArgs e)
    {
        var relativePath = Path.GetRelativePath(_promptsPath, e.FullPath);
        var cacheKey = relativePath
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(".liquid", "");

        if (_cache.TryRemove(cacheKey, out _))
        {
            _logger.LogInformation("Template cache evicted: {CacheKey}", cacheKey);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
