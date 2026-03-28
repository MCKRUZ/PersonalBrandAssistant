# Section 10: Substack Prep UI

## Overview

Angular component showing Substack-formatted fields with per-field copy-to-clipboard buttons, copy confirmation indicators, and "Mark as Published" functionality. Integrated as a tab in the content detail view.

**Depends on:** Section 03 (Substack Prep API endpoints)
**Blocks:** Section 13 (Pipeline Integration)

---

## Tests (Write First)

File: `src/PersonalBrandAssistant.Web/src/app/features/content/components/substack-prep/substack-prep.component.spec.ts`

```typescript
// Test: renders all Substack fields (title, subtitle, body, SEO desc, tags, section, preview)
// Test: copy button copies correct field to clipboard
// Test: shows checkmark indicator after successful copy
// Test: "Mark as Published" button calls substack-published endpoint
// Test: "Mark as Published" with optional URL input
// Test: status badge shows Draft/Ready/Published correctly
// Test: disables "Mark as Published" when already published
// Test: handles API errors gracefully
```

---

## Implementation Details

### Component
File: `src/PersonalBrandAssistant.Web/src/app/features/content/components/substack-prep/substack-prep.component.ts`

- **Input**: `contentId: string`
- **On init**: Call `GET /api/content/{id}/substack-prep` to load prepared fields
- **Field cards**: Each Substack field displayed as a card with label, formatted content preview, and "Copy" button
- **Copy functionality**: Use `navigator.clipboard.writeText()`. On success, show checkmark icon for 2 seconds then revert to copy icon. Track copied state per field.
- **Mark as Published**: Button with optional text input for Substack URL. Calls `POST /api/content/{id}/substack-published`. On success, updates status badge to "Published".
- **Status badge**: `Draft` (content not finalized), `Ready to Copy` (fields prepared), `Published` (confirmed via manual or RSS)

### Service
File: `src/PersonalBrandAssistant.Web/src/app/features/content/services/substack-prep.service.ts`

```typescript
@Injectable({ providedIn: 'root' })
export class SubstackPrepService {
    getPrep(contentId: string): Observable<SubstackPreparedContent>;
    markPublished(contentId: string, substackUrl?: string): Observable<SubstackPublishConfirmation>;
}
```

### Models
File: `src/PersonalBrandAssistant.Web/src/app/features/content/models/substack-prep.models.ts`

```typescript
export interface SubstackPreparedContent {
    title: string; subtitle: string; body: string; seoDescription: string;
    tags: string[]; sectionName: string | null; previewText: string; canonicalUrl: string | null;
}
export interface SubstackPublishConfirmation { contentId: string; substackPostUrl: string | null; wasAlreadyPublished: boolean; }
```

---

## Files
| File | Action |
|------|--------|
| `Web/src/app/features/content/components/substack-prep/substack-prep.component.ts` | Create |
| `Web/src/app/features/content/components/substack-prep/substack-prep.component.html` | Create |
| `Web/src/app/features/content/components/substack-prep/substack-prep.component.scss` | Create |
| `Web/src/app/features/content/services/substack-prep.service.ts` | Create |
| `Web/src/app/features/content/models/substack-prep.models.ts` | Create |
