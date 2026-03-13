diff --git a/planning/01-foundation/implementation/deep_implement_config.json b/planning/01-foundation/implementation/deep_implement_config.json
index a2c6d57..900bb58 100644
--- a/planning/01-foundation/implementation/deep_implement_config.json
+++ b/planning/01-foundation/implementation/deep_implement_config.json
@@ -17,7 +17,16 @@
     "section-08-testing",
     "section-09-verification"
   ],
-  "sections_state": {},
+  "sections_state": {
+    "section-01-scaffolding": {
+      "status": "complete",
+      "commit_hash": "07721d33a26c36836af1b38fdd1db919c8506274"
+    },
+    "section-02-domain": {
+      "status": "complete",
+      "commit_hash": "925a434d590e303a61f0a7e122cae25e7c951dcd"
+    }
+  },
   "pre_commit": {
     "present": false,
     "type": "none",
diff --git a/src/PersonalBrandAssistant.Application/Common/Behaviors/LoggingBehavior.cs b/src/PersonalBrandAssistant.Application/Common/Behaviors/LoggingBehavior.cs
new file mode 100644
index 0000000..e7922a3
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Behaviors/LoggingBehavior.cs
@@ -0,0 +1,70 @@
+using System.Diagnostics;
+using MediatR;
+using Microsoft.Extensions.Logging;
+
+namespace PersonalBrandAssistant.Application.Common.Behaviors;
+
+public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
+    where TRequest : notnull
+{
+    private static readonly HashSet<string> SensitivePatterns = ["Token", "Password", "Secret", "Key"];
+    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
+
+    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
+    {
+        _logger = logger;
+    }
+
+    public async Task<TResponse> Handle(
+        TRequest request,
+        RequestHandlerDelegate<TResponse> next,
+        CancellationToken cancellationToken)
+    {
+        var requestName = typeof(TRequest).Name;
+
+        _logger.LogInformation("Handling {RequestName} with {RequestData}",
+            requestName, SanitizeRequest(request));
+
+        var stopwatch = Stopwatch.StartNew();
+
+        try
+        {
+            var response = await next(cancellationToken);
+            stopwatch.Stop();
+
+            _logger.LogInformation("Handled {RequestName} in {ElapsedMs}ms",
+                requestName, stopwatch.ElapsedMilliseconds);
+
+            return response;
+        }
+        catch (Exception ex)
+        {
+            stopwatch.Stop();
+
+            _logger.LogError(ex, "Failed handling {RequestName} in {ElapsedMs}ms",
+                requestName, stopwatch.ElapsedMilliseconds);
+
+            throw;
+        }
+    }
+
+    private static Dictionary<string, object?> SanitizeRequest(TRequest request)
+    {
+        var properties = typeof(TRequest).GetProperties();
+        var sanitized = new Dictionary<string, object?>();
+
+        foreach (var prop in properties)
+        {
+            if (SensitivePatterns.Any(p => prop.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
+            {
+                sanitized[prop.Name] = "[REDACTED]";
+            }
+            else
+            {
+                sanitized[prop.Name] = prop.GetValue(request);
+            }
+        }
+
+        return sanitized;
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Behaviors/ValidationBehavior.cs b/src/PersonalBrandAssistant.Application/Common/Behaviors/ValidationBehavior.cs
new file mode 100644
index 0000000..f227eca
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Behaviors/ValidationBehavior.cs
@@ -0,0 +1,54 @@
+using FluentValidation;
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Common.Behaviors;
+
+public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
+    where TRequest : notnull
+    where TResponse : class
+{
+    private readonly IEnumerable<IValidator<TRequest>> _validators;
+
+    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
+    {
+        _validators = validators;
+    }
+
+    public async Task<TResponse> Handle(
+        TRequest request,
+        RequestHandlerDelegate<TResponse> next,
+        CancellationToken cancellationToken)
+    {
+        if (!_validators.Any())
+        {
+            return await next(cancellationToken);
+        }
+
+        var context = new ValidationContext<TRequest>(request);
+        var results = await Task.WhenAll(
+            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));
+
+        var failures = results
+            .SelectMany(r => r.Errors)
+            .Where(f => f is not null)
+            .ToList();
+
+        if (failures.Count == 0)
+        {
+            return await next(cancellationToken);
+        }
+
+        var errors = failures.Select(f => f.ErrorMessage).ToList();
+
+        // Use reflection to create Result<T>.ValidationFailure
+        var responseType = typeof(TResponse);
+        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
+        {
+            var method = responseType.GetMethod(nameof(Result<object>.ValidationFailure))!;
+            return (TResponse)method.Invoke(null, [errors.AsEnumerable()])!;
+        }
+
+        throw new ValidationException(failures);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Errors/ErrorCode.cs b/src/PersonalBrandAssistant.Application/Common/Errors/ErrorCode.cs
new file mode 100644
index 0000000..633b9f0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Errors/ErrorCode.cs
@@ -0,0 +1,11 @@
+namespace PersonalBrandAssistant.Application.Common.Errors;
+
+public enum ErrorCode
+{
+    None,
+    ValidationFailed,
+    NotFound,
+    Conflict,
+    Unauthorized,
+    InternalError,
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs
new file mode 100644
index 0000000..faed1c0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs
@@ -0,0 +1,16 @@
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IApplicationDbContext
+{
+    DbSet<Content> Contents { get; }
+    DbSet<Platform> Platforms { get; }
+    DbSet<BrandProfile> BrandProfiles { get; }
+    DbSet<ContentCalendarSlot> ContentCalendarSlots { get; }
+    DbSet<AuditLogEntry> AuditLogEntries { get; }
+    DbSet<User> Users { get; }
+
+    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IDateTimeProvider.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IDateTimeProvider.cs
new file mode 100644
index 0000000..ac00fc7
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IDateTimeProvider.cs
@@ -0,0 +1,6 @@
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IDateTimeProvider
+{
+    DateTimeOffset UtcNow { get; }
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IEncryptionService.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IEncryptionService.cs
new file mode 100644
index 0000000..3632bde
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IEncryptionService.cs
@@ -0,0 +1,7 @@
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IEncryptionService
+{
+    byte[] Encrypt(string plaintext);
+    string Decrypt(byte[] ciphertext);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/PagedResult.cs b/src/PersonalBrandAssistant.Application/Common/Models/PagedResult.cs
new file mode 100644
index 0000000..f7bee32
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/PagedResult.cs
@@ -0,0 +1,30 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public class PagedResult<T>
+{
+    public PagedResult(IReadOnlyList<T> items, string? cursor, bool hasMore)
+    {
+        Items = items;
+        Cursor = cursor;
+        HasMore = hasMore;
+    }
+
+    public IReadOnlyList<T> Items { get; }
+    public string? Cursor { get; }
+    public bool HasMore { get; }
+
+    public static string EncodeCursor(DateTimeOffset createdAt, Guid id) =>
+        Convert.ToBase64String(
+            System.Text.Encoding.UTF8.GetBytes($"{createdAt.Ticks}_{id}"));
+
+    public static (DateTimeOffset CreatedAt, Guid Id)? DecodeCursor(string? cursor)
+    {
+        if (string.IsNullOrWhiteSpace(cursor)) return null;
+
+        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
+        var parts = decoded.Split('_', 2);
+        if (parts.Length != 2) return null;
+
+        return (new DateTimeOffset(long.Parse(parts[0]), TimeSpan.Zero), Guid.Parse(parts[1]));
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/Result.cs b/src/PersonalBrandAssistant.Application/Common/Models/Result.cs
new file mode 100644
index 0000000..9b72b0e
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/Result.cs
@@ -0,0 +1,49 @@
+using PersonalBrandAssistant.Application.Common.Errors;
+
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public class Result<T>
+{
+    private Result(T? value, bool isSuccess, ErrorCode errorCode, IReadOnlyList<string> errors)
+    {
+        Value = value;
+        IsSuccess = isSuccess;
+        ErrorCode = errorCode;
+        Errors = errors;
+    }
+
+    public T? Value { get; }
+    public bool IsSuccess { get; }
+    public ErrorCode ErrorCode { get; }
+    public IReadOnlyList<string> Errors { get; }
+
+    public static Result<T> Success(T value) =>
+        new(value, true, ErrorCode.None, []);
+
+    public static Result<T> Failure(ErrorCode errorCode, params string[] errors) =>
+        new(default, false, errorCode, errors);
+
+    public static Result<T> NotFound(string message) =>
+        Failure(ErrorCode.NotFound, message);
+
+    public static Result<T> ValidationFailure(IEnumerable<string> errors) =>
+        new(default, false, ErrorCode.ValidationFailed, errors.ToList().AsReadOnly());
+
+    public static Result<T> Conflict(string message) =>
+        Failure(ErrorCode.Conflict, message);
+}
+
+public static class Result
+{
+    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
+
+    public static Result<T> Failure<T>(ErrorCode errorCode, params string[] errors) =>
+        Result<T>.Failure(errorCode, errors);
+
+    public static Result<T> NotFound<T>(string message) => Result<T>.NotFound(message);
+
+    public static Result<T> ValidationFailure<T>(IEnumerable<string> errors) =>
+        Result<T>.ValidationFailure(errors);
+
+    public static Result<T> Conflict<T>(string message) => Result<T>.Conflict(message);
+}
diff --git a/src/PersonalBrandAssistant.Application/DependencyInjection.cs b/src/PersonalBrandAssistant.Application/DependencyInjection.cs
new file mode 100644
index 0000000..14fccb9
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/DependencyInjection.cs
@@ -0,0 +1,22 @@
+using FluentValidation;
+using MediatR;
+using Microsoft.Extensions.DependencyInjection;
+using PersonalBrandAssistant.Application.Common.Behaviors;
+
+namespace PersonalBrandAssistant.Application;
+
+public static class DependencyInjection
+{
+    public static IServiceCollection AddApplication(this IServiceCollection services)
+    {
+        var assembly = typeof(DependencyInjection).Assembly;
+
+        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
+        services.AddValidatorsFromAssembly(assembly);
+
+        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
+        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
+
+        return services;
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommand.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommand.cs
new file mode 100644
index 0000000..76c4e2b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommand.cs
@@ -0,0 +1,13 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.ValueObjects;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;
+
+public sealed record CreateContentCommand(
+    ContentType ContentType,
+    string Body,
+    string? Title = null,
+    PlatformType[]? TargetPlatforms = null,
+    ContentMetadata? Metadata = null) : IRequest<Result<Guid>>;
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommandHandler.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommandHandler.cs
new file mode 100644
index 0000000..135103b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommandHandler.cs
@@ -0,0 +1,35 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;
+
+public sealed class CreateContentCommandHandler : IRequestHandler<CreateContentCommand, Result<Guid>>
+{
+    private readonly IApplicationDbContext _dbContext;
+
+    public CreateContentCommandHandler(IApplicationDbContext dbContext)
+    {
+        _dbContext = dbContext;
+    }
+
+    public async Task<Result<Guid>> Handle(CreateContentCommand request, CancellationToken cancellationToken)
+    {
+        var content = ContentEntity.Create(
+            request.ContentType,
+            request.Body,
+            request.Title,
+            request.TargetPlatforms);
+
+        if (request.Metadata is not null)
+        {
+            content.Metadata = request.Metadata;
+        }
+
+        _dbContext.Contents.Add(content);
+        await _dbContext.SaveChangesAsync(cancellationToken);
+
+        return Result<Guid>.Success(content.Id);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommandValidator.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommandValidator.cs
new file mode 100644
index 0000000..445abfb
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommandValidator.cs
@@ -0,0 +1,12 @@
+using FluentValidation;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;
+
+public sealed class CreateContentCommandValidator : AbstractValidator<CreateContentCommand>
+{
+    public CreateContentCommandValidator()
+    {
+        RuleFor(x => x.Body).NotEmpty();
+        RuleFor(x => x.ContentType).IsInEnum();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/DeleteContent/DeleteContentCommand.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/DeleteContent/DeleteContentCommand.cs
new file mode 100644
index 0000000..4015c19
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/DeleteContent/DeleteContentCommand.cs
@@ -0,0 +1,6 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.DeleteContent;
+
+public sealed record DeleteContentCommand(Guid Id) : IRequest<Result<Unit>>;
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/DeleteContent/DeleteContentCommandHandler.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/DeleteContent/DeleteContentCommandHandler.cs
new file mode 100644
index 0000000..d7f84f8
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/DeleteContent/DeleteContentCommandHandler.cs
@@ -0,0 +1,45 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.DeleteContent;
+
+public sealed class DeleteContentCommandHandler : IRequestHandler<DeleteContentCommand, Result<Unit>>
+{
+    private readonly IApplicationDbContext _dbContext;
+
+    public DeleteContentCommandHandler(IApplicationDbContext dbContext)
+    {
+        _dbContext = dbContext;
+    }
+
+    public async Task<Result<Unit>> Handle(DeleteContentCommand request, CancellationToken cancellationToken)
+    {
+        var content = await _dbContext.Contents.FirstOrDefaultAsync(
+            c => c.Id == request.Id, cancellationToken);
+
+        if (content is null)
+        {
+            return Result<Unit>.NotFound($"Content with ID {request.Id} not found.");
+        }
+
+        if (content.Status == ContentStatus.Archived)
+        {
+            return Result<Unit>.Success(Unit.Value);
+        }
+
+        try
+        {
+            content.TransitionTo(ContentStatus.Archived);
+        }
+        catch (InvalidOperationException ex)
+        {
+            return Result<Unit>.Failure(Common.Errors.ErrorCode.ValidationFailed, ex.Message);
+        }
+
+        await _dbContext.SaveChangesAsync(cancellationToken);
+        return Result<Unit>.Success(Unit.Value);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/DeleteContent/DeleteContentCommandValidator.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/DeleteContent/DeleteContentCommandValidator.cs
new file mode 100644
index 0000000..06cba4a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/DeleteContent/DeleteContentCommandValidator.cs
@@ -0,0 +1,11 @@
+using FluentValidation;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.DeleteContent;
+
+public sealed class DeleteContentCommandValidator : AbstractValidator<DeleteContentCommand>
+{
+    public DeleteContentCommandValidator()
+    {
+        RuleFor(x => x.Id).NotEmpty();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommand.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommand.cs
new file mode 100644
index 0000000..c1a2c7a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommand.cs
@@ -0,0 +1,14 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.ValueObjects;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;
+
+public sealed record UpdateContentCommand(
+    Guid Id,
+    string? Title = null,
+    string? Body = null,
+    PlatformType[]? TargetPlatforms = null,
+    ContentMetadata? Metadata = null,
+    uint Version = 0) : IRequest<Result<Unit>>;
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommandHandler.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommandHandler.cs
new file mode 100644
index 0000000..cdca56f
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommandHandler.cs
@@ -0,0 +1,52 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;
+
+public sealed class UpdateContentCommandHandler : IRequestHandler<UpdateContentCommand, Result<Unit>>
+{
+    private readonly IApplicationDbContext _dbContext;
+
+    public UpdateContentCommandHandler(IApplicationDbContext dbContext)
+    {
+        _dbContext = dbContext;
+    }
+
+    public async Task<Result<Unit>> Handle(UpdateContentCommand request, CancellationToken cancellationToken)
+    {
+        var content = await _dbContext.Contents.FirstOrDefaultAsync(
+            c => c.Id == request.Id, cancellationToken);
+
+        if (content is null)
+        {
+            return Result<Unit>.NotFound($"Content with ID {request.Id} not found.");
+        }
+
+        if (content.Status is not (ContentStatus.Draft or ContentStatus.Review))
+        {
+            return Result<Unit>.Failure(ErrorCode.ValidationFailed, "Content is not in an editable state.");
+        }
+
+        if (request.Title is not null) content.Title = request.Title;
+        if (request.Body is not null) content.Body = request.Body;
+        if (request.TargetPlatforms is not null) content.TargetPlatforms = request.TargetPlatforms;
+        if (request.Metadata is not null) content.Metadata = request.Metadata;
+
+        content.Version = request.Version;
+
+        try
+        {
+            await _dbContext.SaveChangesAsync(cancellationToken);
+        }
+        catch (DbUpdateConcurrencyException)
+        {
+            return Result<Unit>.Conflict("Content was modified by another process.");
+        }
+
+        return Result<Unit>.Success(Unit.Value);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommandValidator.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommandValidator.cs
new file mode 100644
index 0000000..dac2b7c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommandValidator.cs
@@ -0,0 +1,15 @@
+using FluentValidation;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;
+
+public sealed class UpdateContentCommandValidator : AbstractValidator<UpdateContentCommand>
+{
+    public UpdateContentCommandValidator()
+    {
+        RuleFor(x => x.Id).NotEmpty();
+        RuleFor(x => x)
+            .Must(x => x.Title is not null || x.Body is not null ||
+                        x.TargetPlatforms is not null || x.Metadata is not null)
+            .WithMessage("At least one field must be provided for update.");
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Queries/GetContent/GetContentQuery.cs b/src/PersonalBrandAssistant.Application/Features/Content/Queries/GetContent/GetContentQuery.cs
new file mode 100644
index 0000000..8332535
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Queries/GetContent/GetContentQuery.cs
@@ -0,0 +1,7 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Queries.GetContent;
+
+public sealed record GetContentQuery(Guid Id) : IRequest<Result<ContentEntity>>;
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Queries/GetContent/GetContentQueryHandler.cs b/src/PersonalBrandAssistant.Application/Features/Content/Queries/GetContent/GetContentQueryHandler.cs
new file mode 100644
index 0000000..e226571
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Queries/GetContent/GetContentQueryHandler.cs
@@ -0,0 +1,27 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Queries.GetContent;
+
+public sealed class GetContentQueryHandler : IRequestHandler<GetContentQuery, Result<ContentEntity>>
+{
+    private readonly IApplicationDbContext _dbContext;
+
+    public GetContentQueryHandler(IApplicationDbContext dbContext)
+    {
+        _dbContext = dbContext;
+    }
+
+    public async Task<Result<ContentEntity>> Handle(GetContentQuery request, CancellationToken cancellationToken)
+    {
+        var content = await _dbContext.Contents
+            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
+
+        return content is null
+            ? Result<ContentEntity>.NotFound($"Content with ID {request.Id} not found.")
+            : Result<ContentEntity>.Success(content);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Queries/ListContent/ListContentQuery.cs b/src/PersonalBrandAssistant.Application/Features/Content/Queries/ListContent/ListContentQuery.cs
new file mode 100644
index 0000000..8f255cf
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Queries/ListContent/ListContentQuery.cs
@@ -0,0 +1,12 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;
+
+public sealed record ListContentQuery(
+    ContentType? ContentType = null,
+    ContentStatus? Status = null,
+    int PageSize = 20,
+    string? Cursor = null) : IRequest<Result<PagedResult<ContentEntity>>>;
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Queries/ListContent/ListContentQueryHandler.cs b/src/PersonalBrandAssistant.Application/Features/Content/Queries/ListContent/ListContentQueryHandler.cs
new file mode 100644
index 0000000..5970068
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Queries/ListContent/ListContentQueryHandler.cs
@@ -0,0 +1,62 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;
+
+public sealed class ListContentQueryHandler
+    : IRequestHandler<ListContentQuery, Result<PagedResult<ContentEntity>>>
+{
+    private readonly IApplicationDbContext _dbContext;
+
+    public ListContentQueryHandler(IApplicationDbContext dbContext)
+    {
+        _dbContext = dbContext;
+    }
+
+    public async Task<Result<PagedResult<ContentEntity>>> Handle(
+        ListContentQuery request, CancellationToken cancellationToken)
+    {
+        var pageSize = Math.Min(request.PageSize, 50);
+        var query = _dbContext.Contents.AsQueryable();
+
+        if (request.ContentType.HasValue)
+            query = query.Where(c => c.ContentType == request.ContentType.Value);
+
+        if (request.Status.HasValue)
+            query = query.Where(c => c.Status == request.Status.Value);
+
+        var cursorData = PagedResult<ContentEntity>.DecodeCursor(request.Cursor);
+        if (cursorData.HasValue)
+        {
+            var (cursorCreatedAt, cursorId) = cursorData.Value;
+            query = query.Where(c =>
+                c.CreatedAt < cursorCreatedAt ||
+                (c.CreatedAt == cursorCreatedAt && c.Id.CompareTo(cursorId) < 0));
+        }
+
+        query = query
+            .OrderByDescending(c => c.CreatedAt)
+            .ThenByDescending(c => c.Id);
+
+        var items = await query.Take(pageSize + 1).ToListAsync(cancellationToken);
+        var hasMore = items.Count > pageSize;
+
+        if (hasMore)
+        {
+            items = items.Take(pageSize).ToList();
+        }
+
+        string? nextCursor = null;
+        if (hasMore && items.Count > 0)
+        {
+            var last = items[^1];
+            nextCursor = PagedResult<ContentEntity>.EncodeCursor(last.CreatedAt, last.Id);
+        }
+
+        return Result<PagedResult<ContentEntity>>.Success(
+            new PagedResult<ContentEntity>(items.AsReadOnly(), nextCursor, hasMore));
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Queries/ListContent/ListContentQueryValidator.cs b/src/PersonalBrandAssistant.Application/Features/Content/Queries/ListContent/ListContentQueryValidator.cs
new file mode 100644
index 0000000..6c16811
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Queries/ListContent/ListContentQueryValidator.cs
@@ -0,0 +1,11 @@
+using FluentValidation;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;
+
+public sealed class ListContentQueryValidator : AbstractValidator<ListContentQuery>
+{
+    public ListContentQueryValidator()
+    {
+        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/PersonalBrandAssistant.Application.csproj b/src/PersonalBrandAssistant.Application/PersonalBrandAssistant.Application.csproj
index cb906a3..61fa69c 100644
--- a/src/PersonalBrandAssistant.Application/PersonalBrandAssistant.Application.csproj
+++ b/src/PersonalBrandAssistant.Application/PersonalBrandAssistant.Application.csproj
@@ -8,6 +8,8 @@
     <PackageReference Include="FluentValidation" Version="12.1.1" />
     <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
     <PackageReference Include="MediatR" Version="14.1.0" />
+    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.5" />
+    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.5" />
   </ItemGroup>
 
 </Project>
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Behaviors/LoggingBehaviorTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Behaviors/LoggingBehaviorTests.cs
new file mode 100644
index 0000000..17271f7
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Behaviors/LoggingBehaviorTests.cs
@@ -0,0 +1,78 @@
+using MediatR;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Behaviors;
+
+namespace PersonalBrandAssistant.Application.Tests.Behaviors;
+
+public class LoggingBehaviorTests
+{
+    public sealed record TestRequest(string Name, string ApiKey) : IRequest<string>;
+
+    private readonly Mock<ILogger<LoggingBehavior<TestRequest, string>>> _logger = new();
+
+    [Fact]
+    public async Task Handle_LogsRequestNameAndResponse()
+    {
+        var behavior = new LoggingBehavior<TestRequest, string>(_logger.Object);
+
+        var result = await behavior.Handle(
+            new TestRequest("test", "secret"),
+            ct => Task.FromResult("done"),
+            CancellationToken.None);
+
+        Assert.Equal("done", result);
+
+        _logger.Verify(
+            l => l.Log(
+                LogLevel.Information,
+                It.IsAny<EventId>(),
+                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TestRequest")),
+                null,
+                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
+            Times.AtLeast(1));
+    }
+
+    [Fact]
+    public async Task Handle_RedactsSensitiveFields()
+    {
+        var behavior = new LoggingBehavior<TestRequest, string>(_logger.Object);
+
+        await behavior.Handle(
+            new TestRequest("visible", "my-secret-key"),
+            ct => Task.FromResult("done"),
+            CancellationToken.None);
+
+        // Verify that logging was called and the ApiKey field would be redacted
+        // The SanitizeRequest method redacts fields containing "Key"
+        _logger.Verify(
+            l => l.Log(
+                LogLevel.Information,
+                It.IsAny<EventId>(),
+                It.Is<It.IsAnyType>((o, t) => !o.ToString()!.Contains("my-secret-key")),
+                null,
+                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
+            Times.AtLeast(1));
+    }
+
+    [Fact]
+    public async Task Handle_OnException_LogsErrorAndRethrows()
+    {
+        var behavior = new LoggingBehavior<TestRequest, string>(_logger.Object);
+
+        await Assert.ThrowsAsync<InvalidOperationException>(() =>
+            behavior.Handle(
+                new TestRequest("test", "key"),
+                ct => throw new InvalidOperationException("boom"),
+                CancellationToken.None));
+
+        _logger.Verify(
+            l => l.Log(
+                LogLevel.Error,
+                It.IsAny<EventId>(),
+                It.IsAny<It.IsAnyType>(),
+                It.IsAny<InvalidOperationException>(),
+                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
+            Times.Once);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Behaviors/ValidationBehaviorTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Behaviors/ValidationBehaviorTests.cs
new file mode 100644
index 0000000..0a2fb50
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Behaviors/ValidationBehaviorTests.cs
@@ -0,0 +1,69 @@
+using FluentValidation;
+using FluentValidation.Results;
+using MediatR;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Behaviors;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Tests.Behaviors;
+
+public class ValidationBehaviorTests
+{
+    public sealed record TestRequest(string Name) : IRequest<Result<string>>;
+
+    [Fact]
+    public async Task Handle_NoValidators_CallsNext()
+    {
+        var behavior = new ValidationBehavior<TestRequest, Result<string>>(
+            Enumerable.Empty<IValidator<TestRequest>>());
+
+        var called = false;
+        var result = await behavior.Handle(
+            new TestRequest("test"),
+            ct => { called = true; return Task.FromResult(Result<string>.Success("ok")); },
+            CancellationToken.None);
+
+        Assert.True(called);
+        Assert.True(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task Handle_ValidationPasses_CallsNext()
+    {
+        var validator = new Mock<IValidator<TestRequest>>();
+        validator.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new ValidationResult());
+
+        var behavior = new ValidationBehavior<TestRequest, Result<string>>(new[] { validator.Object });
+
+        var result = await behavior.Handle(
+            new TestRequest("test"),
+            ct => Task.FromResult(Result<string>.Success("ok")),
+            CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task Handle_ValidationFails_ReturnsValidationFailure()
+    {
+        var validator = new Mock<IValidator<TestRequest>>();
+        validator.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new ValidationResult(new[]
+            {
+                new ValidationFailure("Name", "Name is required")
+            }));
+
+        var behavior = new ValidationBehavior<TestRequest, Result<string>>(new[] { validator.Object });
+
+        var result = await behavior.Handle(
+            new TestRequest(""),
+            ct => Task.FromResult(Result<string>.Success("should not reach")),
+            CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+        Assert.Contains("Name is required", result.Errors);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Common/PagedResultTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Common/PagedResultTests.cs
new file mode 100644
index 0000000..45acf03
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Common/PagedResultTests.cs
@@ -0,0 +1,41 @@
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Tests.Common;
+
+public class PagedResultTests
+{
+    [Fact]
+    public void EncodeCursor_DecodeCursor_Roundtrip()
+    {
+        var createdAt = new DateTimeOffset(2026, 3, 13, 10, 0, 0, TimeSpan.Zero);
+        var id = Guid.NewGuid();
+
+        var cursor = PagedResult<object>.EncodeCursor(createdAt, id);
+        var decoded = PagedResult<object>.DecodeCursor(cursor);
+
+        Assert.NotNull(decoded);
+        Assert.Equal(createdAt, decoded.Value.CreatedAt);
+        Assert.Equal(id, decoded.Value.Id);
+    }
+
+    [Theory]
+    [InlineData(null)]
+    [InlineData("")]
+    [InlineData("   ")]
+    public void DecodeCursor_NullOrWhitespace_ReturnsNull(string? cursor)
+    {
+        var result = PagedResult<object>.DecodeCursor(cursor);
+        Assert.Null(result);
+    }
+
+    [Fact]
+    public void Constructor_SetsProperties()
+    {
+        var items = new List<string> { "a", "b" }.AsReadOnly();
+        var paged = new PagedResult<string>(items, "cursor123", true);
+
+        Assert.Equal(2, paged.Items.Count);
+        Assert.Equal("cursor123", paged.Cursor);
+        Assert.True(paged.HasMore);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Common/ResultTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Common/ResultTests.cs
new file mode 100644
index 0000000..bf66d06
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Common/ResultTests.cs
@@ -0,0 +1,60 @@
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Tests.Common;
+
+public class ResultTests
+{
+    [Fact]
+    public void Success_SetsValueAndIsSuccess()
+    {
+        var result = Result<int>.Success(42);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(42, result.Value);
+        Assert.Equal(ErrorCode.None, result.ErrorCode);
+        Assert.Empty(result.Errors);
+    }
+
+    [Fact]
+    public void Failure_SetsErrorCodeAndMessages()
+    {
+        var result = Result<int>.Failure(ErrorCode.InternalError, "Something went wrong");
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
+        Assert.Single(result.Errors);
+        Assert.Equal("Something went wrong", result.Errors[0]);
+    }
+
+    [Fact]
+    public void NotFound_SetsNotFoundErrorCode()
+    {
+        var result = Result<string>.NotFound("Item missing");
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+        Assert.Contains("Item missing", result.Errors);
+    }
+
+    [Fact]
+    public void ValidationFailure_SetsValidationFailedCodeAndAllErrors()
+    {
+        var errors = new[] { "Field A required", "Field B invalid" };
+        var result = Result<string>.ValidationFailure(errors);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+        Assert.Equal(2, result.Errors.Count);
+    }
+
+    [Fact]
+    public void Conflict_SetsConflictErrorCode()
+    {
+        var result = Result<string>.Conflict("Already exists");
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
+        Assert.Contains("Already exists", result.Errors);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/CreateContentCommandHandlerTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/CreateContentCommandHandlerTests.cs
new file mode 100644
index 0000000..5006b29
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/CreateContentCommandHandlerTests.cs
@@ -0,0 +1,79 @@
+using Microsoft.EntityFrameworkCore;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.ValueObjects;
+using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;
+
+public class CreateContentCommandHandlerTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<DbSet<ContentEntity>> _contentsDbSet;
+    private readonly CreateContentCommandHandler _handler;
+
+    public CreateContentCommandHandlerTests()
+    {
+        _contentsDbSet = new List<ContentEntity>().AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(x => x.Contents).Returns(_contentsDbSet.Object);
+        _handler = new CreateContentCommandHandler(_dbContext.Object);
+    }
+
+    [Fact]
+    public async Task Handle_ValidCommand_ReturnsSuccessWithId()
+    {
+        var command = new CreateContentCommand(ContentType.BlogPost, "Test body", "Test title");
+
+        var result = await _handler.Handle(command, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.NotEqual(Guid.Empty, result.Value);
+        _contentsDbSet.Verify(s => s.Add(It.IsAny<ContentEntity>()), Times.Once);
+        _dbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task Handle_WithMetadata_SetsMetadataOnContent()
+    {
+        var metadata = new ContentMetadata { Tags = ["tag1", "tag2"] };
+        var command = new CreateContentCommand(ContentType.SocialPost, "Body", Metadata: metadata);
+
+        ContentEntity? captured = null;
+        _contentsDbSet.Setup(s => s.Add(It.IsAny<ContentEntity>()))
+            .Callback<ContentEntity>(c => captured = c);
+
+        await _handler.Handle(command, CancellationToken.None);
+
+        Assert.NotNull(captured);
+        Assert.Equal(metadata, captured!.Metadata);
+    }
+
+    [Fact]
+    public async Task Handle_WithTargetPlatforms_SetsTargetPlatforms()
+    {
+        var platforms = new[] { PlatformType.TwitterX, PlatformType.LinkedIn };
+        var command = new CreateContentCommand(ContentType.BlogPost, "Body", TargetPlatforms: platforms);
+
+        ContentEntity? captured = null;
+        _contentsDbSet.Setup(s => s.Add(It.IsAny<ContentEntity>()))
+            .Callback<ContentEntity>(c => captured = c);
+
+        await _handler.Handle(command, CancellationToken.None);
+
+        Assert.NotNull(captured);
+        Assert.Equal(platforms, captured!.TargetPlatforms);
+    }
+
+    [Fact]
+    public async Task Handle_CallsSaveChanges()
+    {
+        var command = new CreateContentCommand(ContentType.BlogPost, "Body");
+
+        await _handler.Handle(command, CancellationToken.None);
+
+        _dbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/DeleteContentCommandHandlerTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/DeleteContentCommandHandlerTests.cs
new file mode 100644
index 0000000..d6512a9
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/DeleteContentCommandHandlerTests.cs
@@ -0,0 +1,56 @@
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Features.Content.Commands.DeleteContent;
+using PersonalBrandAssistant.Domain.Enums;
+using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;
+
+public class DeleteContentCommandHandlerTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+
+    private DeleteContentCommandHandler CreateHandler(List<ContentEntity> data)
+    {
+        var mockDbSet = data.AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
+        return new DeleteContentCommandHandler(_dbContext.Object);
+    }
+
+    [Fact]
+    public async Task Handle_ExistingContent_ArchivesSuccessfully()
+    {
+        var content = ContentEntity.Create(ContentType.BlogPost, "Body");
+        var handler = CreateHandler([content]);
+
+        var result = await handler.Handle(new DeleteContentCommand(content.Id), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(ContentStatus.Archived, content.Status);
+    }
+
+    [Fact]
+    public async Task Handle_ContentNotFound_ReturnsNotFound()
+    {
+        var handler = CreateHandler([]);
+
+        var result = await handler.Handle(new DeleteContentCommand(Guid.NewGuid()), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task Handle_AlreadyArchived_ReturnsSuccessIdempotently()
+    {
+        var content = ContentEntity.Create(ContentType.BlogPost, "Body");
+        content.TransitionTo(ContentStatus.Archived);
+        var handler = CreateHandler([content]);
+
+        var result = await handler.Handle(new DeleteContentCommand(content.Id), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/UpdateContentCommandHandlerTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/UpdateContentCommandHandlerTests.cs
new file mode 100644
index 0000000..bce31a1
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/UpdateContentCommandHandlerTests.cs
@@ -0,0 +1,92 @@
+using Microsoft.EntityFrameworkCore;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;
+using PersonalBrandAssistant.Domain.Enums;
+using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;
+
+public class UpdateContentCommandHandlerTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+
+    private UpdateContentCommandHandler CreateHandler(List<ContentEntity> data)
+    {
+        var mockDbSet = data.AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
+        return new UpdateContentCommandHandler(_dbContext.Object);
+    }
+
+    [Fact]
+    public async Task Handle_ExistingDraftContent_UpdatesSuccessfully()
+    {
+        var content = ContentEntity.Create(ContentType.BlogPost, "Old body", "Old title");
+        var handler = CreateHandler([content]);
+
+        var command = new UpdateContentCommand(content.Id, Title: "New title", Body: "New body");
+        var result = await handler.Handle(command, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("New title", content.Title);
+        Assert.Equal("New body", content.Body);
+    }
+
+    [Fact]
+    public async Task Handle_ContentNotFound_ReturnsNotFound()
+    {
+        var handler = CreateHandler([]);
+
+        var command = new UpdateContentCommand(Guid.NewGuid(), Title: "New title");
+        var result = await handler.Handle(command, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task Handle_ContentNotEditable_ReturnsValidationFailed()
+    {
+        var content = ContentEntity.Create(ContentType.BlogPost, "Body");
+        content.TransitionTo(ContentStatus.Review);
+        content.TransitionTo(ContentStatus.Approved);
+        var handler = CreateHandler([content]);
+
+        var command = new UpdateContentCommand(content.Id, Body: "Updated");
+        var result = await handler.Handle(command, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task Handle_ReviewContent_IsEditable()
+    {
+        var content = ContentEntity.Create(ContentType.BlogPost, "Body");
+        content.TransitionTo(ContentStatus.Review);
+        var handler = CreateHandler([content]);
+
+        var command = new UpdateContentCommand(content.Id, Body: "Updated in review");
+        var result = await handler.Handle(command, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("Updated in review", content.Body);
+    }
+
+    [Fact]
+    public async Task Handle_ConcurrencyConflict_ReturnsConflict()
+    {
+        var content = ContentEntity.Create(ContentType.BlogPost, "Body");
+        var handler = CreateHandler([content]);
+        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new DbUpdateConcurrencyException());
+
+        var command = new UpdateContentCommand(content.Id, Body: "Updated");
+        var result = await handler.Handle(command, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Queries/GetContentQueryHandlerTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Queries/GetContentQueryHandlerTests.cs
new file mode 100644
index 0000000..04e97c0
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Queries/GetContentQueryHandlerTests.cs
@@ -0,0 +1,57 @@
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Features.Content.Queries.GetContent;
+using PersonalBrandAssistant.Domain.Enums;
+using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Queries;
+
+public class GetContentQueryHandlerTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+
+    private GetContentQueryHandler CreateHandler(List<ContentEntity> data)
+    {
+        var mockDbSet = data.AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
+        return new GetContentQueryHandler(_dbContext.Object);
+    }
+
+    [Fact]
+    public async Task Handle_ContentExists_ReturnsSuccess()
+    {
+        var content = ContentEntity.Create(ContentType.BlogPost, "Body", "Title");
+        var handler = CreateHandler([content]);
+
+        var result = await handler.Handle(new GetContentQuery(content.Id), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(content.Id, result.Value!.Id);
+    }
+
+    [Fact]
+    public async Task Handle_ContentNotFound_ReturnsNotFound()
+    {
+        var handler = CreateHandler([]);
+
+        var result = await handler.Handle(new GetContentQuery(Guid.NewGuid()), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task Handle_MultipleContents_ReturnsCorrectOne()
+    {
+        var content1 = ContentEntity.Create(ContentType.BlogPost, "Body1");
+        var content2 = ContentEntity.Create(ContentType.SocialPost, "Body2");
+        var handler = CreateHandler([content1, content2]);
+
+        var result = await handler.Handle(new GetContentQuery(content2.Id), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(ContentType.SocialPost, result.Value!.ContentType);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Queries/ListContentQueryHandlerTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Queries/ListContentQueryHandlerTests.cs
new file mode 100644
index 0000000..bbd4865
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Queries/ListContentQueryHandlerTests.cs
@@ -0,0 +1,130 @@
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;
+using PersonalBrandAssistant.Domain.Enums;
+using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Queries;
+
+public class ListContentQueryHandlerTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+
+    private ListContentQueryHandler CreateHandler(List<ContentEntity> data)
+    {
+        var mockDbSet = data.AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
+        return new ListContentQueryHandler(_dbContext.Object);
+    }
+
+    [Fact]
+    public async Task Handle_EmptyDatabase_ReturnsEmptyResult()
+    {
+        var handler = CreateHandler([]);
+
+        var result = await handler.Handle(new ListContentQuery(), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Empty(result.Value!.Items);
+        Assert.False(result.Value.HasMore);
+    }
+
+    [Fact]
+    public async Task Handle_WithItems_ReturnsPagedResult()
+    {
+        var items = Enumerable.Range(0, 5)
+            .Select(i => ContentEntity.Create(ContentType.BlogPost, $"Body {i}"))
+            .ToList();
+        var handler = CreateHandler(items);
+
+        var result = await handler.Handle(new ListContentQuery(PageSize: 20), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(5, result.Value!.Items.Count);
+        Assert.False(result.Value.HasMore);
+    }
+
+    [Fact]
+    public async Task Handle_PageSizeExceeded_TruncatesAndSetsHasMore()
+    {
+        var items = Enumerable.Range(0, 5)
+            .Select(i => ContentEntity.Create(ContentType.BlogPost, $"Body {i}"))
+            .ToList();
+        var handler = CreateHandler(items);
+
+        var result = await handler.Handle(new ListContentQuery(PageSize: 3), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(3, result.Value!.Items.Count);
+        Assert.True(result.Value.HasMore);
+        Assert.NotNull(result.Value.Cursor);
+    }
+
+    [Fact]
+    public async Task Handle_FilterByContentType_FiltersCorrectly()
+    {
+        var items = new List<ContentEntity>
+        {
+            ContentEntity.Create(ContentType.BlogPost, "Blog"),
+            ContentEntity.Create(ContentType.SocialPost, "Tweet"),
+            ContentEntity.Create(ContentType.BlogPost, "Blog2"),
+        };
+        var handler = CreateHandler(items);
+
+        var result = await handler.Handle(
+            new ListContentQuery(ContentType: ContentType.SocialPost), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!.Items);
+        Assert.Equal(ContentType.SocialPost, result.Value.Items[0].ContentType);
+    }
+
+    [Fact]
+    public async Task Handle_FilterByStatus_FiltersCorrectly()
+    {
+        var draft = ContentEntity.Create(ContentType.BlogPost, "Draft");
+        var review = ContentEntity.Create(ContentType.BlogPost, "Review");
+        review.TransitionTo(ContentStatus.Review);
+        var handler = CreateHandler([draft, review]);
+
+        var result = await handler.Handle(
+            new ListContentQuery(Status: ContentStatus.Review), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!.Items);
+        Assert.Equal(ContentStatus.Review, result.Value.Items[0].Status);
+    }
+
+    [Fact]
+    public async Task Handle_PageSizeCappedAt50()
+    {
+        var items = Enumerable.Range(0, 55)
+            .Select(i => ContentEntity.Create(ContentType.BlogPost, $"Body {i}"))
+            .ToList();
+        var handler = CreateHandler(items);
+
+        var result = await handler.Handle(new ListContentQuery(PageSize: 100), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(50, result.Value!.Items.Count);
+        Assert.True(result.Value.HasMore);
+    }
+
+    [Fact]
+    public async Task Handle_NoCursor_ReturnsNull()
+    {
+        var items = new List<ContentEntity>
+        {
+            ContentEntity.Create(ContentType.BlogPost, "Body"),
+        };
+        var handler = CreateHandler(items);
+
+        var result = await handler.Handle(new ListContentQuery(PageSize: 20), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Null(result.Value!.Cursor);
+        Assert.False(result.Value.HasMore);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/CreateContentCommandValidatorTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/CreateContentCommandValidatorTests.cs
new file mode 100644
index 0000000..6783a0b
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/CreateContentCommandValidatorTests.cs
@@ -0,0 +1,26 @@
+using FluentValidation.TestHelper;
+using PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Validators;
+
+public class CreateContentCommandValidatorTests
+{
+    private readonly CreateContentCommandValidator _validator = new();
+
+    [Fact]
+    public void Validate_EmptyBody_HasError()
+    {
+        var command = new CreateContentCommand(ContentType.BlogPost, "");
+        var result = _validator.TestValidate(command);
+        result.ShouldHaveValidationErrorFor(x => x.Body);
+    }
+
+    [Fact]
+    public void Validate_ValidCommand_NoErrors()
+    {
+        var command = new CreateContentCommand(ContentType.BlogPost, "Valid body");
+        var result = _validator.TestValidate(command);
+        result.ShouldNotHaveAnyValidationErrors();
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/ListContentQueryValidatorTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/ListContentQueryValidatorTests.cs
new file mode 100644
index 0000000..db3045f
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/ListContentQueryValidatorTests.cs
@@ -0,0 +1,31 @@
+using FluentValidation.TestHelper;
+using PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Validators;
+
+public class ListContentQueryValidatorTests
+{
+    private readonly ListContentQueryValidator _validator = new();
+
+    [Theory]
+    [InlineData(0)]
+    [InlineData(-1)]
+    [InlineData(51)]
+    public void Validate_InvalidPageSize_HasError(int pageSize)
+    {
+        var query = new ListContentQuery(PageSize: pageSize);
+        var result = _validator.TestValidate(query);
+        result.ShouldHaveValidationErrorFor(x => x.PageSize);
+    }
+
+    [Theory]
+    [InlineData(1)]
+    [InlineData(25)]
+    [InlineData(50)]
+    public void Validate_ValidPageSize_NoErrors(int pageSize)
+    {
+        var query = new ListContentQuery(PageSize: pageSize);
+        var result = _validator.TestValidate(query);
+        result.ShouldNotHaveAnyValidationErrors();
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/UpdateContentCommandValidatorTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/UpdateContentCommandValidatorTests.cs
new file mode 100644
index 0000000..68a6051
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/UpdateContentCommandValidatorTests.cs
@@ -0,0 +1,25 @@
+using FluentValidation.TestHelper;
+using PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Validators;
+
+public class UpdateContentCommandValidatorTests
+{
+    private readonly UpdateContentCommandValidator _validator = new();
+
+    [Fact]
+    public void Validate_EmptyId_HasError()
+    {
+        var command = new UpdateContentCommand(Guid.Empty, Title: "Title");
+        var result = _validator.TestValidate(command);
+        result.ShouldHaveValidationErrorFor(x => x.Id);
+    }
+
+    [Fact]
+    public void Validate_NoFieldsProvided_HasError()
+    {
+        var command = new UpdateContentCommand(Guid.NewGuid());
+        var result = _validator.TestValidate(command);
+        Assert.False(result.IsValid);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/PersonalBrandAssistant.Application.Tests.csproj b/tests/PersonalBrandAssistant.Application.Tests/PersonalBrandAssistant.Application.Tests.csproj
index 1a0036e..e6d7ace 100644
--- a/tests/PersonalBrandAssistant.Application.Tests/PersonalBrandAssistant.Application.Tests.csproj
+++ b/tests/PersonalBrandAssistant.Application.Tests/PersonalBrandAssistant.Application.Tests.csproj
@@ -7,6 +7,8 @@
   <ItemGroup>
     <PackageReference Include="coverlet.collector" Version="6.0.4" />
     <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
+    <PackageReference Include="FluentValidation" Version="12.1.1" />
+    <PackageReference Include="MockQueryable.Moq" Version="7.0.3" />
     <PackageReference Include="Moq" Version="4.20.72" />
     <PackageReference Include="xunit" Version="2.9.3" />
     <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
