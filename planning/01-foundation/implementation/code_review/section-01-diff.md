diff --git a/.gitignore b/.gitignore
index 1b8794b..ad19f30 100644
--- a/.gitignore
+++ b/.gitignore
@@ -17,11 +17,14 @@ dist/
 .angular/
 
 # Secrets
-*.env
-.env.*
+.env
+!.env.example
 appsettings.Development.json
 appsettings.Local.json
 
+# Data Protection
+data-protection-keys/
+
 # Claude Code
 CLAUDE.local.md
 .claude/todo.md
diff --git a/Directory.Build.props b/Directory.Build.props
new file mode 100644
index 0000000..a7782a4
--- /dev/null
+++ b/Directory.Build.props
@@ -0,0 +1,8 @@
+<Project>
+  <PropertyGroup>
+    <TargetFramework>net10.0</TargetFramework>
+    <Nullable>enable</Nullable>
+    <ImplicitUsings>enable</ImplicitUsings>
+    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
+  </PropertyGroup>
+</Project>
diff --git a/PersonalBrandAssistant.slnx b/PersonalBrandAssistant.slnx
new file mode 100644
index 0000000..d2466a8
--- /dev/null
+++ b/PersonalBrandAssistant.slnx
@@ -0,0 +1,13 @@
+<Solution>
+  <Folder Name="/src/">
+    <Project Path="src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj" />
+    <Project Path="src/PersonalBrandAssistant.Application/PersonalBrandAssistant.Application.csproj" />
+    <Project Path="src/PersonalBrandAssistant.Domain/PersonalBrandAssistant.Domain.csproj" />
+    <Project Path="src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj" />
+  </Folder>
+  <Folder Name="/tests/">
+    <Project Path="tests/PersonalBrandAssistant.Application.Tests/PersonalBrandAssistant.Application.Tests.csproj" />
+    <Project Path="tests/PersonalBrandAssistant.Domain.Tests/PersonalBrandAssistant.Domain.Tests.csproj" />
+    <Project Path="tests/PersonalBrandAssistant.Infrastructure.Tests/PersonalBrandAssistant.Infrastructure.Tests.csproj" />
+  </Folder>
+</Solution>
diff --git a/src/PersonalBrandAssistant.Api/Endpoints/.gitkeep b/src/PersonalBrandAssistant.Api/Endpoints/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Api/Middleware/.gitkeep b/src/PersonalBrandAssistant.Api/Middleware/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj b/src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj
new file mode 100644
index 0000000..3e5cc22
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj
@@ -0,0 +1,14 @@
+<Project Sdk="Microsoft.NET.Sdk.Web">
+
+  <ItemGroup>
+    <ProjectReference Include="..\PersonalBrandAssistant.Application\PersonalBrandAssistant.Application.csproj" />
+    <ProjectReference Include="..\PersonalBrandAssistant.Infrastructure\PersonalBrandAssistant.Infrastructure.csproj" />
+  </ItemGroup>
+
+  <ItemGroup>
+    <PackageReference Include="MediatR" Version="14.1.0" />
+    <PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
+    <PackageReference Include="Swashbuckle.AspNetCore" Version="10.1.5" />
+  </ItemGroup>
+
+</Project>
diff --git a/src/PersonalBrandAssistant.Api/Program.cs b/src/PersonalBrandAssistant.Api/Program.cs
new file mode 100644
index 0000000..1760df1
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Program.cs
@@ -0,0 +1,6 @@
+var builder = WebApplication.CreateBuilder(args);
+var app = builder.Build();
+
+app.MapGet("/", () => "Hello World!");
+
+app.Run();
diff --git a/src/PersonalBrandAssistant.Api/Properties/launchSettings.json b/src/PersonalBrandAssistant.Api/Properties/launchSettings.json
new file mode 100644
index 0000000..9e9d53c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Properties/launchSettings.json
@@ -0,0 +1,23 @@
+{
+  "$schema": "https://json.schemastore.org/launchsettings.json",
+  "profiles": {
+    "http": {
+      "commandName": "Project",
+      "dotnetRunMessages": true,
+      "launchBrowser": true,
+      "applicationUrl": "http://localhost:5105",
+      "environmentVariables": {
+        "ASPNETCORE_ENVIRONMENT": "Development"
+      }
+    },
+    "https": {
+      "commandName": "Project",
+      "dotnetRunMessages": true,
+      "launchBrowser": true,
+      "applicationUrl": "https://localhost:7003;http://localhost:5105",
+      "environmentVariables": {
+        "ASPNETCORE_ENVIRONMENT": "Development"
+      }
+    }
+  }
+}
diff --git a/src/PersonalBrandAssistant.Api/appsettings.json b/src/PersonalBrandAssistant.Api/appsettings.json
new file mode 100644
index 0000000..10f68b8
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/appsettings.json
@@ -0,0 +1,9 @@
+{
+  "Logging": {
+    "LogLevel": {
+      "Default": "Information",
+      "Microsoft.AspNetCore": "Warning"
+    }
+  },
+  "AllowedHosts": "*"
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Behaviors/.gitkeep b/src/PersonalBrandAssistant.Application/Common/Behaviors/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/.gitkeep b/src/PersonalBrandAssistant.Application/Common/Interfaces/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/.gitkeep b/src/PersonalBrandAssistant.Application/Common/Models/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/.gitkeep b/src/PersonalBrandAssistant.Application/Features/Content/Commands/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Queries/.gitkeep b/src/PersonalBrandAssistant.Application/Features/Content/Queries/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Application/PersonalBrandAssistant.Application.csproj b/src/PersonalBrandAssistant.Application/PersonalBrandAssistant.Application.csproj
new file mode 100644
index 0000000..cb906a3
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/PersonalBrandAssistant.Application.csproj
@@ -0,0 +1,13 @@
+<Project Sdk="Microsoft.NET.Sdk">
+
+  <ItemGroup>
+    <ProjectReference Include="..\PersonalBrandAssistant.Domain\PersonalBrandAssistant.Domain.csproj" />
+  </ItemGroup>
+
+  <ItemGroup>
+    <PackageReference Include="FluentValidation" Version="12.1.1" />
+    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
+    <PackageReference Include="MediatR" Version="14.1.0" />
+  </ItemGroup>
+
+</Project>
diff --git a/src/PersonalBrandAssistant.Domain/Common/.gitkeep b/src/PersonalBrandAssistant.Domain/Common/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Domain/Entities/.gitkeep b/src/PersonalBrandAssistant.Domain/Entities/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Domain/Enums/.gitkeep b/src/PersonalBrandAssistant.Domain/Enums/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Domain/Events/.gitkeep b/src/PersonalBrandAssistant.Domain/Events/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Domain/PersonalBrandAssistant.Domain.csproj b/src/PersonalBrandAssistant.Domain/PersonalBrandAssistant.Domain.csproj
new file mode 100644
index 0000000..1650ed3
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/PersonalBrandAssistant.Domain.csproj
@@ -0,0 +1,7 @@
+<Project Sdk="Microsoft.NET.Sdk">
+
+  <ItemGroup>
+    <PackageReference Include="MediatR.Contracts" Version="2.0.1" />
+  </ItemGroup>
+
+</Project>
diff --git a/src/PersonalBrandAssistant.Domain/ValueObjects/.gitkeep b/src/PersonalBrandAssistant.Domain/ValueObjects/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/.gitkeep b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Interceptors/.gitkeep b/src/PersonalBrandAssistant.Infrastructure/Data/Interceptors/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Migrations/.gitkeep b/src/PersonalBrandAssistant.Infrastructure/Data/Migrations/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
new file mode 100644
index 0000000..d3c0975
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
@@ -0,0 +1,22 @@
+<Project Sdk="Microsoft.NET.Sdk">
+
+  <ItemGroup>
+    <ProjectReference Include="..\PersonalBrandAssistant.Application\PersonalBrandAssistant.Application.csproj" />
+  </ItemGroup>
+
+  <ItemGroup>
+    <PackageReference Include="Microsoft.AspNetCore.DataProtection" Version="10.0.5" />
+    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.5" />
+    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.5">
+      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
+      <PrivateAssets>all</PrivateAssets>
+    </PackageReference>
+    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="10.0.5" />
+    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.5" />
+    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.1" />
+    <PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
+    <PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
+    <PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
+  </ItemGroup>
+
+</Project>
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/.gitkeep b/src/PersonalBrandAssistant.Infrastructure/Services/.gitkeep
new file mode 100644
index 0000000..e69de29
diff --git a/tests/PersonalBrandAssistant.Application.Tests/PersonalBrandAssistant.Application.Tests.csproj b/tests/PersonalBrandAssistant.Application.Tests/PersonalBrandAssistant.Application.Tests.csproj
new file mode 100644
index 0000000..1a0036e
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/PersonalBrandAssistant.Application.Tests.csproj
@@ -0,0 +1,23 @@
+<Project Sdk="Microsoft.NET.Sdk">
+
+  <PropertyGroup>
+    <IsPackable>false</IsPackable>
+  </PropertyGroup>
+
+  <ItemGroup>
+    <PackageReference Include="coverlet.collector" Version="6.0.4" />
+    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
+    <PackageReference Include="Moq" Version="4.20.72" />
+    <PackageReference Include="xunit" Version="2.9.3" />
+    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
+  </ItemGroup>
+
+  <ItemGroup>
+    <Using Include="Xunit" />
+  </ItemGroup>
+
+  <ItemGroup>
+    <ProjectReference Include="..\..\src\PersonalBrandAssistant.Application\PersonalBrandAssistant.Application.csproj" />
+  </ItemGroup>
+
+</Project>
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/PersonalBrandAssistant.Domain.Tests.csproj b/tests/PersonalBrandAssistant.Domain.Tests/PersonalBrandAssistant.Domain.Tests.csproj
new file mode 100644
index 0000000..33c4d65
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/PersonalBrandAssistant.Domain.Tests.csproj
@@ -0,0 +1,23 @@
+<Project Sdk="Microsoft.NET.Sdk">
+
+  <PropertyGroup>
+    <IsPackable>false</IsPackable>
+  </PropertyGroup>
+
+  <ItemGroup>
+    <PackageReference Include="coverlet.collector" Version="6.0.4" />
+    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
+    <PackageReference Include="Moq" Version="4.20.72" />
+    <PackageReference Include="xunit" Version="2.9.3" />
+    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
+  </ItemGroup>
+
+  <ItemGroup>
+    <Using Include="Xunit" />
+  </ItemGroup>
+
+  <ItemGroup>
+    <ProjectReference Include="..\..\src\PersonalBrandAssistant.Domain\PersonalBrandAssistant.Domain.csproj" />
+  </ItemGroup>
+
+</Project>
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/PersonalBrandAssistant.Infrastructure.Tests.csproj b/tests/PersonalBrandAssistant.Infrastructure.Tests/PersonalBrandAssistant.Infrastructure.Tests.csproj
new file mode 100644
index 0000000..636ddae
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/PersonalBrandAssistant.Infrastructure.Tests.csproj
@@ -0,0 +1,26 @@
+<Project Sdk="Microsoft.NET.Sdk">
+
+  <PropertyGroup>
+    <IsPackable>false</IsPackable>
+  </PropertyGroup>
+
+  <ItemGroup>
+    <PackageReference Include="coverlet.collector" Version="6.0.4" />
+    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.5" />
+    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
+    <PackageReference Include="Moq" Version="4.20.72" />
+    <PackageReference Include="Testcontainers.PostgreSql" Version="4.11.0" />
+    <PackageReference Include="xunit" Version="2.9.3" />
+    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
+  </ItemGroup>
+
+  <ItemGroup>
+    <Using Include="Xunit" />
+  </ItemGroup>
+
+  <ItemGroup>
+    <ProjectReference Include="..\..\src\PersonalBrandAssistant.Infrastructure\PersonalBrandAssistant.Infrastructure.csproj" />
+    <ProjectReference Include="..\..\src\PersonalBrandAssistant.Api\PersonalBrandAssistant.Api.csproj" />
+  </ItemGroup>
+
+</Project>
