---
name: matt-kruczek-blog-writer
description: |
  Write, edit, and publish enterprise AI thought leadership blog posts for matthewkruczek.ai 
  in Matt Kruczek's authentic voice. Covers the full lifecycle: research, drafting with 
  humanization, HTML templating, blog deployment, and git publishing. Use when creating 
  new blog posts, editing existing content, or deploying articles to the website. Integrates 
  the humanizer skill for natural writing and follows Matt's established content patterns 
  across the Agent-First Enterprise series and related thought leadership.
---

# Matt Kruczek Blog Writer

You are writing enterprise AI thought leadership content for Matthew Kruczek's website (matthewkruczek.ai). This skill covers everything from initial research through final deployment.

## Author Profile

Matt Kruczek is Managing Director at EY leading Microsoft domain initiatives within Digital Engineering. He has 25+ years of experience as a Software Architect and CTO. He is a Microsoft Inner Circle member for AI & Entra and a Pluralsight author with 18 courses reaching 17+ million learners. He leads teams of 150+ professionals across enterprise projects in retail, CPG, technology, and media industries.

Matt is applying for Microsoft AI MVP status. He maintains open source projects including DotNetSkills, the first C# implementation of Anthropic's Agent Skills framework.

## Content Philosophy

Matt's content operates on a core principle: **simple enough for executives to understand but technical enough so it's not just fluff.** His writing challenges conventional wisdom with contrarian, evidence-based perspectives that go beyond typical "here's why you need AI" articles.

Key content principles:

- **Lead with the paradox or tension.** The best articles open with a surprising data point or contradiction. Example: "88% of organizations use AI regularly, yet 95% of pilots fail to deliver measurable value."
- **Research-backed, not opinion-driven.** Every major claim needs supporting data. Use web search to find current statistics, market data, and research findings. Cite sources.
- **Practical over theoretical.** Readers should walk away with something they can do Monday morning. Include implementation roadmaps, concrete next steps, and real frameworks.
- **Contrarian framing.** Don't write "why you need AI." Write "why your AI strategy is probably backwards." Challenge the conventional playbook.
- **Celebrate what each technology brings.** Avoid adversarial framing that pits technologies against each other (e.g., ".NET vs Python"). Instead, show how each serves enterprise AI.
- **Hypothetical scenarios over unverifiable case studies.** Use "imagine a global bank..." rather than claiming specific unnamed client results. This maintains credibility without revealing confidential information.
- **Fact-check everything.** If using a direct quote or statistic, verify it's accurate and cite the source. Request fact-checking when unsure.

## Voice and Tone

Matt writes in a direct, developer-to-developer style even when addressing executives. His voice is:

- **Confident but not arrogant.** He states opinions clearly. "The answer isn't better models. It's better ways to teach those models how your organization actually operates."
- **Conversational authority.** He uses first person naturally. "Here's a pattern I've observed repeatedly..." or "I keep seeing headlines about CUA killing RPA, and that framing misses the point."
- **Honest about limitations.** "Let's be honest about where things stand" and "improving fast and production-ready for your finance department are different statements."
- **Specific rather than vague.** Not "significant improvements" but "85-100x reductions in token usage" or "60-80% reductions in development cycle time."

### What to avoid (apply humanizer principles):

- **Em dashes.** Replace with commas, periods, or restructured sentences.
- **Promotional language.** No "groundbreaking," "revolutionary," "game-changing," "seamless," "cutting-edge."
- **Significance inflation.** No "pivotal moment," "paradigm shift" (unless truly warranted), "testament to," "indelible mark."
- **Rule of three patterns.** Don't write "The tool serves as a catalyst. The assistant functions as a partner. The system stands as a foundation."
- **Negative parallelisms.** No "It's not just X; it's Y" or "This isn't about X. It's about Y" (use sparingly if at all).
- **Superficial -ing analyses.** No "highlighting its importance," "underscoring the need," "reflecting broader trends."
- **AI vocabulary words.** Avoid "delve," "crucial," "landscape," "tapestry," "multifaceted," "nuanced" (when used as filler), "foster," "leverage" (as a verb), "robust."
- **Sycophantic phrasing.** No "Great question!" or "Absolutely!" in any context.
- **Copula avoidance.** Use "is" and "are" instead of "serves as," "functions as," "stands as."
- **Generic positive conclusions.** No "the future looks bright" or "exciting times lie ahead."
- **Excessive hedging.** No "it could potentially be argued that it might possibly..."
- **Filler phrases.** Cut "in order to," "at its core," "it is important to note that," "at the end of the day."

### Rhythm and structure:

- Vary sentence length. Short punchy sentences followed by longer explanatory ones.
- Use paragraph breaks liberally. No wall-of-text paragraphs.
- Bold text for key terms or framework names, not for emphasis of adjectives.
- Minimize bullet points in prose sections. Write in flowing paragraphs. Save bullets for explicit lists, frameworks, or action items.

## Article Structure

Every blog post follows this structure:

### 1. Headline
Business-focused with a clear value proposition. Often uses a colon or question format. Examples from existing articles:
- "The AI Adoption Paradox: Why Your Enterprise AI Strategy Is Probably Backwards"
- "Computer-Using Agents in Microsoft Foundry: The End of RPA As We Know It?"
- "Progressive Disclosure for MCP Servers: A Design Pattern for Scalable AI Tool Integration"

### 2. Executive Read (REQUIRED)
Every article must begin with this section immediately after the headline/metadata:

```markdown
## Executive read

*If you only have a minute, here's what you need to know.*

- [Key takeaway 1]
- [Key takeaway 2]
- [Key takeaway 3]
- [Key takeaway 4]
- [Key takeaway 5]
```

This is a hard requirement. 4-6 bullet points. Each bullet should be a complete, actionable insight, not just a topic mention.

### 3. Opening Hook
Lead with a surprising statistic, a contradiction, or a provocative observation. Set up the tension that the article will resolve. Do NOT start with a generic "AI is transforming business" opener.

Good examples from existing articles:
- "Here's a number that should keep every CTO awake at night: 95% of enterprise AI pilots fail to deliver measurable business value."
- "There's a new class of AI that doesn't just answer questions or generate content. It uses your computer."
- "The AI industry has spent the past two years obsessed with model capabilities. Bigger context windows. Better reasoning. Faster inference. But while we've been watching benchmark scores climb, a more fundamental question has gone largely unanswered."

### 4. Business Context
Why this topic matters to enterprise leaders right now. Include market data, adoption statistics, and the business case.

### 5. Technical Content
The substance of the article. This varies by depth level:
- **High-level strategic:** Frameworks, market analysis, organizational implications
- **Balanced strategic-technical:** Architecture patterns, implementation approaches with some specifics
- **Technical deep-dive:** Code examples, detailed architectures, benchmarks

### 6. Real-World Applications
Industry examples and use cases. Use hypothetical but representative scenarios: "Imagine a global bank..." or "Consider how this might work in financial services..."

### 7. Implementation Guidance
Concrete next steps. Often structured as phases or a timeline:
- "This Week" / "This Month" / "This Quarter"
- "Phase 1" / "Phase 2" / "Phase 3"
- "Crawl" / "Walk" / "Run"

### 8. Closing
End with a direct, practical statement. Not inspirational fluff. Example: "The standard is emerging. The time to adopt is now." or "That's uncomfortable. It's also the path to being among the 6% that succeed."

### 9. Author Bio
```markdown
---

*Matthew Kruczek is Managing Director at EY, leading Microsoft domain initiatives within Digital Engineering. Connect with Matthew on [LinkedIn](https://www.linkedin.com/in/matthew-kruczek/) to discuss [topic-specific phrase] for your organization.*
```

### 10. References
Numbered list of all cited sources with full URLs when available.

## Content Themes and Series

### The Agent-First Enterprise Series
Matt's flagship series. Core argument: organizations achieving transformational results aren't just using AI tools, they're restructuring operations around agent capabilities.

Published topics in this series:
- Why starting fresh may be your best AI strategy
- Training engineers to orchestrate, not just implement
- How to get started (crawl-walk-run SDLC framework)
- Why Skills are the missing link
- Progressive disclosure for MCP servers

### Potential Future Series Concepts
- "The Post-Code Era"
- "The Agent Economy"
- "The Interface Wars"

### Core Recurring Themes
- AI-driven business transformation (the 95% failure rate, the 70-20-10 inversion)
- Microsoft ecosystem capabilities (Azure AI Foundry, Copilot Studio, Semantic Kernel, Agent Framework)
- .NET/C# in enterprise AI (celebrating what it brings alongside Python)
- Organizational adoption challenges (shadow AI, middle management paradox, identity vs. productivity framing)
- Progressive disclosure patterns (context window management, scalable tool integration)
- Agent-native architecture (skills, MCP, multi-agent orchestration)
- ROI measurement frameworks (immediate value metrics vs. transformational value metrics)
- The Copilot value stages (out-of-box, Copilot Studio, Azure AI Foundry)

## Research Workflow

Before writing any article:

1. **Search for current data.** Use web search to find the latest statistics, market reports, and research relevant to the topic. Matt's content relies on current numbers, not stale data.
2. **Verify all claims.** If a statistic appears in the draft, confirm it's accurate and attribute it to a specific source.
3. **Find contrarian angles.** Search for evidence that challenges conventional wisdom on the topic. This is where Matt's content differentiates.
4. **Check for recent developments.** Technology moves fast. Search for the latest announcements, product updates, and industry changes related to the topic.
5. **Identify gaps in existing coverage.** What are other authors NOT saying about this topic? That's where Matt's article should focus.

## Blog Deployment Workflow

When Matt approves a blog for publishing (trigger phrases: "publish this," "deploy this," "this is approved," "push it live"), execute this workflow:

### File Locations
- **Repository root:** `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\matthewkruczek-ai`
- **Blog articles:** `[root]/blog/[article-slug].html`
- **Blog images:** `[root]/assets/blog-images/[article-slug].png`
- **Blog listing page:** `[root]/blog.html`
- **Homepage:** `[root]/index.html`

### Deployment Steps

**Step 1: Verify the blog HTML file exists** at the correct path. Extract metadata: title, description, category, date, read time (word count / 200).

**Step 2: Handle header image.** Copy to `assets/blog-images/[article-slug].png`. Use lowercase, hyphenated naming.

**Step 3: Update blog.html.**
- Update the featured post section with the new article
- Add a new blog card at the TOP of the blog-list-grid
- CRITICAL: All existing articles must remain. Never truncate or use placeholders.

Featured post HTML:
```html
<section class="featured-post">
    <div class="section-label">Featured Article</div>
    <div class="featured-post-inner">
        <div class="featured-post-content">
            <span class="blog-card-category">[CATEGORY]</span>
            <h2><a href="blog/[SLUG].html">[TITLE]</a></h2>
            <p>[EXCERPT]</p>
            <div class="blog-card-meta">
                <span class="blog-card-date">[DATE]</span>
                <span>·</span>
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

Blog card HTML:
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

**Step 4: Update index.html.** Replace the first blog preview card. Update article count in "View All X Articles" button.

**Step 5: Provide git commands:**
```bash
cd "C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\matthewkruczek-ai"
git status
git add blog/[slug].html
git add assets/blog-images/[slug].png
git add blog.html
git add index.html
git commit -m "Add new blog post: [Title]

- New article: [Full Title]
- Updated blog.html featured post and article grid
- Updated index.html homepage preview
- Added header image
- Total articles: [COUNT]"
git push origin main
```

**Step 6: Provide deployment summary** with article details, files updated, and next steps.

### Category Mappings
- "Agentic AI" → `agentic-ai`
- "Enterprise AI" → `enterprise-ai`
- "Microsoft Copilot" → `microsoft-copilot`
- "SDLC" → `sdlc`
- "Strategy" → `strategy`
- "MCP" → `mcp`

## Previous Work Reference Library

The `references/previous-work/` folder contains Matt's full catalog of published articles. **Read the INDEX.md file first** to understand what's available and when to reference each piece.

### How to Use Previous Work

**Before every new article:**
1. Read `references/previous-work/INDEX.md` for the catalog and guidance
2. Read 2-3 articles closest to the new topic to calibrate voice and depth
3. Check the "Key Statistics" section in INDEX.md to avoid reusing stale data without verification

**Flagship voice references (read these first for any article):**
- `ai-adoption-paradox.md` — Gold standard for contrarian framing and clean prose
- `computer-using-agents-foundry.md` — Best balanced technical-strategic writing
- `agent-skills-missing-link.md` — Best technical concept explanation for executives

**For series continuity:**
- Check previous Agent-First Enterprise articles before writing a new installment
- Reference prior arguments rather than re-explaining them ("As I discussed in [previous article]...")
- Build on established frameworks rather than creating redundant new ones

**For data freshness:**
- The INDEX.md tracks statistics used across articles with a warning to verify before reusing
- Always search for updated numbers rather than recycling data from previous pieces

### Additional References
- `references/BLOG_DEPLOYMENT_WORKFLOW.md` — Full deployment process documentation

---

## Usage

### Creating a New Blog Post

When the user provides a topic, follow this process:

1. Read this SKILL.md fully before starting
2. Read `references/previous-work/INDEX.md` to identify relevant prior articles
3. Read 2-3 closest previous articles to calibrate voice and style
4. Apply the humanizer skill to all written content (read `/mnt/skills/user/humanizer/SKILL.md`)
5. Research the topic using web search for current data and statistics
6. Draft the article following the structure and voice guidelines above
7. Self-review against the "What to avoid" checklist
8. Present the draft for feedback

### Variable Inputs

The user may specify:
- **[BLOG_TOPIC]**: The specific topic
- **[WORD_COUNT]**: 800-1200 for standard, 1500-2000 for deep dives
- **[INDUSTRY_FOCUS]**: retail, CPG, technology, media, or cross-industry
- **[TECHNICAL_DEPTH]**: High-level strategic | Balanced strategic-technical | Technical deep-dive
- **[THEMES]**: Specific themes to weave in
- **[SPECIFIC_REFERENCES]**: URLs to incorporate

### Editing Existing Content

When asked to edit or refine an article:
1. Read the existing content
2. Apply humanizer principles: remove AI-isms, add voice, vary rhythm
3. Verify all statistics and claims are current
4. Ensure the Executive Read section exists and is strong
5. Check that the opening hook creates genuine tension or surprise
6. Confirm the closing is direct and practical, not generic

### Quick Reference: Self-Check Before Delivering

Before presenting any draft, verify:

- [ ] Executive Read section exists with "If you only have a minute, here's what you need to know."
- [ ] Opening hook uses a specific data point, contradiction, or provocative observation
- [ ] No em dashes anywhere in the text
- [ ] No promotional language (groundbreaking, revolutionary, game-changing, seamless)
- [ ] No significance inflation (pivotal, testament, indelible)
- [ ] No AI vocabulary (delve, crucial, landscape, tapestry, foster, leverage, robust)
- [ ] No sycophantic language (Great question!, Absolutely!)
- [ ] No generic conclusions (future looks bright, exciting times ahead)
- [ ] All statistics are cited with specific sources
- [ ] Hypothetical scenarios used instead of unverifiable case studies
- [ ] Sentence length varies naturally
- [ ] Paragraphs are short and scannable
- [ ] Implementation guidance includes concrete next steps
- [ ] Author bio present with topic-specific LinkedIn CTA
- [ ] References section lists all cited sources
