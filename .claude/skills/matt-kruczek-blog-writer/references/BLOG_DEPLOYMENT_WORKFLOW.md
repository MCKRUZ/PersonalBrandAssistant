# Blog Deployment Workflow for matthewkruczek.ai

## Overview
This document defines the automated workflow for deploying blog posts to matthewkruczek.ai when Matt approves a blog for publishing.

## Approval Trigger
When Matt says any of the following phrases, initiate the full deployment workflow:
- "Approve this blog for publishing"
- "This is approved - publish it"
- "Go ahead and deploy this"
- "Publish this blog post"
- "This looks good - push it live"

## Automated Deployment Steps

### Step 1: Verify Blog Post Exists
- Confirm the blog HTML file exists at: `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\matthewkruczek-ai\blog\[article-slug].html`
- Extract metadata from the article:
  - Title
  - Description/excerpt (first paragraph or meta description)
  - Category
  - Publication date
  - Read time estimate (word count Ã· 200 words/min)

### Step 2: Handle Header Image
**If image was generated/provided:**
- Copy image to: `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\matthewkruczek-ai\assets\blog-images\[article-slug].png`
- Use `present_files` tool to make it downloadable for Matt if Filesystem API fails

**Image naming convention:**
- Use article slug (lowercase, hyphens)
- Example: `multi-agent-patterns-microsoft.png`

### Step 3: Update blog.html
**File location:** `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\matthewkruczek-ai\blog.html`

**Update featured post section:**
```html
<section class="featured-post">
    <div class="section-label">Featured Article</div>
    <div class="featured-post-inner">
        <div class="featured-post-content">
            <span class="blog-card-category">[CATEGORY]</span>
            <h2><a href="blog/[SLUG].html">[TITLE]</a></h2>
            <p>[EXCERPT - 2-3 sentences]</p>
            <div class="blog-card-meta">
                <span class="blog-card-date">[DATE]</span>
                <span>Â·</span>
                <span>[READ_TIME] min read</span>
            </div>
            <a href="blog/[SLUG].html" class="blog-card-link">
                Read Full Article
                <svg>...</svg>
            </a>
        </div>
        <div class="featured-post-image">
            <img src="assets/blog-images/[SLUG].png" alt="[ALT_TEXT]">
        </div>
    </div>
</section>
```

**Add new blog card at TOP of blog-list-grid:**
```html
<article class="blog-card" data-category="[category-slug]">
    <div class="blog-card-image">
        <img src="assets/blog-images/[SLUG].png" alt="[ALT_TEXT]">
    </div>
    <div class="blog-card-content">
        <div class="blog-card-meta">
            <span class="blog-card-category">[CATEGORY]</span>
            <span class="blog-card-date">[DATE]</span>
        </div>
        <h3><a href="blog/[SLUG].html">[TITLE]</a></h3>
        <p>[DESCRIPTION]</p>
        <a href="blog/[SLUG].html" class="blog-card-link">
            Read Article
            <svg>...</svg>
        </a>
    </div>
</article>
```

**CRITICAL:** Ensure ALL existing articles remain in the file. Do NOT truncate or use placeholders like "<!-- Remaining articles... -->". Write the complete file every time.

### Step 4: Update index.html Homepage
**File location:** `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\matthewkruczek-ai\index.html`

**Update the FIRST blog preview card in the blog section:**
- Replace the first `<article class="blog-card">` in the blog preview section
- Use same format as blog.html cards
- Update article count in the "View All X Articles" button

### Step 5: Update Article Count
- Count total number of `<article class="blog-card">` elements in blog.html
- Update homepage button text: "View All [COUNT] Articles"

### Step 6: Verify All Files Updated
**Checklist:**
- âœ… Blog article HTML exists: `blog/[slug].html`
- âœ… Header image copied: `assets/blog-images/[slug].png`
- âœ… blog.html updated (featured post + new card + all existing articles intact)
- âœ… index.html updated (first preview card + article count)

### Step 7: Provide Git Commands
Generate the complete git workflow for Matt to execute:

```bash
cd "C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\matthewkruczek-ai"

# Check status
git status

# Stage all changes
git add blog/[article-slug].html
git add assets/blog-images/[article-slug].png
git add blog.html
git add index.html

# Commit with descriptive message
git commit -m "Add new blog post: [Article Title]

- New article: [Full Title]
- Updated blog.html featured post and article grid
- Updated index.html homepage preview
- Added header image for article
- Total articles: [COUNT]"

# Push to main
git push origin main
```

### Step 8: Provide Deployment Summary
Create a summary report:

```
âœ… BLOG POST DEPLOYMENT COMPLETE

ðŸ“ Article Details:
   - Title: [Title]
   - URL: https://matthewkruczek.ai/blog/[slug].html
   - Category: [Category]
   - Published: [Date]
   - Read Time: [X] minutes

ðŸ“ Files Updated:
   - blog/[slug].html (created)
   - assets/blog-images/[slug].png (created)
   - blog.html (featured post + new card)
   - index.html (homepage preview)
   - Total articles now: [COUNT]

ðŸš€ Next Steps:
   1. Review the git commands above
   2. Execute the commands to push changes
   3. Verify at https://matthewkruczek.ai/blog
   4. Article will be live after git push completes

ðŸ’¡ Header Image Note:
   [If using present_files: Download the image file above and ensure it's copied to assets/blog-images/]
```

## Category Mappings
Map user-friendly categories to data-category attributes:

- "Agentic AI" â†’ `agentic-ai`
- "Enterprise AI" â†’ `enterprise-ai`
- "Microsoft Copilot" â†’ `microsoft-copilot`
- "SDLC" â†’ `sdlc`
- "Strategy" â†’ `strategy`
- "MCP" â†’ `mcp`

## File Path Reference
**Repository Root:**
`C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\matthewkruczek-ai`

**Blog Articles:**
`[root]/blog/[article-slug].html`

**Blog Images:**
`[root]/assets/blog-images/[article-slug].png`

**Blog Listing:**
`[root]/blog.html`

**Homepage:**
`[root]/index.html`

## Error Handling

### If Filesystem API Fails
1. Use bash commands with proper path escaping
2. For images, use `present_files` to make downloadable
3. Provide manual copy instructions as backup

### If blog.html Gets Truncated
1. ALWAYS write complete file with all articles
2. Never use placeholders like "<!-- Remaining articles... -->"
3. If file is too large, write to /tmp first then copy with bash

### If User Needs Manual Steps
Clearly separate:
- âœ… **Automated Steps Completed** (what I did)
- ðŸ“‹ **Manual Steps Required** (what Matt needs to do)

## Quality Checks Before Reporting Complete
- [ ] All existing articles still visible in blog.html
- [ ] Featured post updated to new article
- [ ] New blog card added to grid (first position)
- [ ] Homepage preview updated
- [ ] Article count accurate
- [ ] Image file referenced correctly in HTML
- [ ] Git commands provided with correct paths
- [ ] Category slug matches filter buttons

## Version History
- v1.0 (2026-01-13): Initial workflow documentation
