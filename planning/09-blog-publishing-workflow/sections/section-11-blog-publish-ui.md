# Section 11: Blog Publish UI

## Overview

Angular component for blog HTML preview and publish triggering. Shows generated HTML preview, "Publish to Blog" button (disabled until SubstackPostUrl present), deploy status display, and retry on failure. Integrated as a tab in content detail view.

**Depends on:** Section 04 (IBlogHtmlGenerator), Section 05 (Blog Publish API endpoints)
**Blocks:** Section 13 (Pipeline Integration)

---

## Tests (Write First)

File: `src/PersonalBrandAssistant.Web/src/app/features/content/components/blog-publish/blog-publish.component.spec.ts`

```typescript
// Test: renders HTML preview in iframe or sanitized container
// Test: "Publish to Blog" button disabled when SubstackPostUrl is null
// Test: "Publish to Blog" button enabled when SubstackPostUrl is present
// Test: clicking Publish calls POST /api/content/{id}/blog-publish
// Test: shows loading/progress indicator during publish
// Test: displays commit SHA and blog URL on success
// Test: displays error message and retry button on failure
// Test: retry button calls publish endpoint again
// Test: shows deploy verification status (checking, verified, failed)
```

---

## Implementation Details

### Component
File: `src/PersonalBrandAssistant.Web/src/app/features/content/components/blog-publish/blog-publish.component.ts`

- **Input**: `contentId: string`, `substackPostUrl: string | null`
- **On init**: Call `GET /api/content/{id}/blog-prep` to load HTML preview and `GET /api/content/{id}/blog-status` for current status
- **HTML preview**: Render in a sandboxed iframe (`srcdoc`) or a sanitized container. Show file path below preview.
- **Publish button**: Disabled when `substackPostUrl` is null (with tooltip explaining why). On click, call `POST /api/content/{id}/blog-publish`.
- **Progress states**: Idle → Publishing (spinner) → Verifying (progress dots) → Published (green checkmark + URL) | Failed (red X + error + retry button)
- **Status display**: Shows `commitSha`, `blogUrl`, `publishedAt` when published. Links `blogUrl` as clickable.
- **Retry**: On failure, show error message and "Retry" button that re-calls the publish endpoint.

### Service
File: `src/PersonalBrandAssistant.Web/src/app/features/content/services/blog-publish.service.ts`

```typescript
@Injectable({ providedIn: 'root' })
export class BlogPublishService {
    getPrep(contentId: string): Observable<BlogHtmlResult>;
    publish(contentId: string): Observable<BlogPublishResult>;
    getStatus(contentId: string): Observable<BlogDeployStatus>;
}
```

### Models
File: `src/PersonalBrandAssistant.Web/src/app/features/content/models/blog-publish.models.ts`

```typescript
export interface BlogHtmlResult { html: string; filePath: string; canonicalUrl: string | null; }
export interface BlogPublishResult { commitSha: string; blogUrl: string; status: string; }
export interface BlogDeployStatus { commitSha: string | null; blogUrl: string | null; status: string; publishedAt: string | null; errorMessage: string | null; }
```

---

## Files
| File | Action |
|------|--------|
| `Web/src/app/features/content/components/blog-publish/blog-publish.component.ts` | Create |
| `Web/src/app/features/content/components/blog-publish/blog-publish.component.html` | Create |
| `Web/src/app/features/content/components/blog-publish/blog-publish.component.scss` | Create |
| `Web/src/app/features/content/services/blog-publish.service.ts` | Create |
| `Web/src/app/features/content/models/blog-publish.models.ts` | Create |
