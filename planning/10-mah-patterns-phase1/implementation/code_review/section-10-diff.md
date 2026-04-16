diff --git a/src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj b/src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj
index 1550298..97d6c73 100644
--- a/src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj
+++ b/src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj
@@ -13,9 +13,11 @@
   <ItemGroup>
     <PackageReference Include="MediatR" Version="14.1.0" />
     <PackageReference Include="ModelContextProtocol" Version="1.1.0" />
+    <PackageReference Include="OpenTelemetry.Exporter.Console" Version="1.11.2" />
     <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.11.2" />
-    <!-- Explicit: AddOpenTelemetry() registered in Program.cs requires this in the Api project -->
+    <!-- Explicit: extension methods in Program.cs require direct package refs in the Api project -->
     <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.11.2" />
+    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.11.1" />
     <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.5">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
diff --git a/src/PersonalBrandAssistant.Api/Program.cs b/src/PersonalBrandAssistant.Api/Program.cs
index f0e1d72..e5e0e0c 100644
--- a/src/PersonalBrandAssistant.Api/Program.cs
+++ b/src/PersonalBrandAssistant.Api/Program.cs
@@ -4,7 +4,9 @@ using PersonalBrandAssistant.Api.Endpoints;
 using PersonalBrandAssistant.Api.Handlers;
 using PersonalBrandAssistant.Api.Middleware;
 using PersonalBrandAssistant.Application;
+using OpenTelemetry.Trace;
 using PersonalBrandAssistant.Infrastructure;
+using PersonalBrandAssistant.Infrastructure.Agents;
 using Serilog;
 
 var isMcpMode = args.Contains("--mcp");
@@ -51,6 +53,17 @@ else
     builder.Services.AddApplication();
     builder.Services.AddInfrastructure(builder.Configuration);
 
+    builder.Services.AddOpenTelemetry()
+        .WithTracing(tracing =>
+        {
+            tracing.AddAspNetCoreInstrumentation();
+            tracing.AddSource(AgentTelemetry.SourceName); // REQUIRED — without this, custom spans are silently dropped
+            if (builder.Configuration.GetValue<bool>("Telemetry:ConsoleExporter"))
+                tracing.AddConsoleExporter();
+            if (builder.Configuration["Telemetry:OtlpEndpoint"] is { } endpoint)
+                tracing.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint));
+        });
+
     builder.Services.AddSignalR();
 
     builder.Services.AddCors(options =>
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index 801c1c1..fbc932c 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -10,8 +10,10 @@ using Microsoft.Extensions.Options;
 using Polly;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Common.Models.Skills;
 using PersonalBrandAssistant.Infrastructure.Agents;
 using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+using PersonalBrandAssistant.Infrastructure.Skills;
 using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
 using PersonalBrandAssistant.Infrastructure.Data;
 using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
@@ -63,7 +65,13 @@ public static class DependencyInjection
         // Agent orchestration
         services.Configure<AgentOrchestrationOptions>(
             configuration.GetSection(AgentOrchestrationOptions.SectionName));
-        services.AddSingleton<ISidecarClient, SidecarClient>();
+        services.Configure<SkillOptions>(configuration.GetSection(SkillOptions.SectionName));
+        services.Configure<ContextBudgetOptions>(configuration.GetSection(ContextBudgetOptions.SectionName));
+        services.AddSingleton<ISkillRegistry, SkillRegistry>();
+        services.AddSingleton<SidecarClient>();
+        services.AddSingleton<ISidecarClient>(sp =>
+            new ObservabilityMiddleware(sp.GetRequiredService<SidecarClient>()));
+        services.AddScoped<IContextBudgetTracker, ContextBudgetTracker>();
         services.AddSingleton<IPromptTemplateService>(sp =>
         {
             var opts = sp.GetRequiredService<IOptions<AgentOrchestrationOptions>>().Value;
diff --git a/src/PersonalBrandAssistant.Infrastructure/Skills/SkillRegistry.cs b/src/PersonalBrandAssistant.Infrastructure/Skills/SkillRegistry.cs
index ff2bc5e..f875ed9 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Skills/SkillRegistry.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Skills/SkillRegistry.cs
@@ -27,12 +27,17 @@ public sealed class SkillRegistry : ISkillRegistry
         _logger = logger;
         var opts = options.Value;
 
-        if (!Directory.Exists(opts.SkillsPath))
+        // Empty string means "use default" — config can override without knowing the runtime base directory
+        var skillsPath = string.IsNullOrEmpty(opts.SkillsPath)
+            ? Path.Combine(AppContext.BaseDirectory, "skills")
+            : opts.SkillsPath;
+
+        if (!Directory.Exists(skillsPath))
             throw new DirectoryNotFoundException(
-                $"Skills directory not found: '{opts.SkillsPath}'. " +
+                $"Skills directory not found: '{skillsPath}'. " +
                 "Ensure the skills/ directory is published with the application.");
 
-        _skills = Discover(opts.SkillsPath, logger);
+        _skills = Discover(skillsPath, logger);
         _allSkills = _skills.Values.Select(e => e.Definition).ToList().AsReadOnly();
 
         ValidateRequired(opts.RequiredSkillIds, environment, logger);
