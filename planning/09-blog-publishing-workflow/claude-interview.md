# Interview Transcript: Blog Publishing Workflow

## Q1: Current Blog Writing Process
**Q:** Walk me through your current blog writing process: do you write in Substack's editor directly, or do you draft elsewhere first?
**A:** Draft in PBA/external, paste into Substack editor. Content is written outside Substack, then manually pasted into their editor for formatting and publishing.

## Q2: matthewkruczek.ai Stack
**Q:** What's the site built with and how is it deployed?
**A:** Static HTML with GitHub Pages. Hand-crafted or templated HTML, deployed via GitHub Pages.

## Q3: Substack Content Preparation Level
**Q:** When PBA prepares the Substack-formatted content, what level of 'ready' do you expect?
**A:** Full package - every Substack field pre-filled, just paste each one. Title, subtitle, body, SEO description, tags, section, preview text - all formatted and ready to copy.

## Q4: Blog Auto-Publish Level
**Q:** Should PBA invoke the blog-writer skill directly as part of the automated pipeline, or should you trigger the blog publish separately?
**A:** Semi-auto - PBA prepares everything, I click 'publish'. PBA generates the HTML and stages it, but user manually approves the final push/deploy.

## Q5: RSS Detection Automation
**Q:** When PBA detects your Substack post is live via RSS, should it auto-create and schedule the blog version, or notify and wait?
**A:** Notify me, I confirm before scheduling. PBA detects the Substack publish, shows the blog-ready version, user approves the schedule.

## Q6: UI Approach
**Q:** Should this be a new dedicated view or integrated into the existing content pipeline?
**A:** Both - pipeline integration + a summary dashboard. Publish prep integrated in the pipeline flow, plus a dedicated view showing all blog posts and their cross-platform status.

## Q7: Content Adaptation Details
**Q:** What additional changes are needed when content moves from Substack to the blog?
**A:** Same content, user will manually add any differences. No automated adaptation beyond template wrapping and canonical URL.

## Q8: Blog-Writer Skill Integration (Critical Clarification)
**Q:** How does the blog-writer skill output connect to PBA?
**A:** User wants to invoke the matt-kruczek-blog-writer skill **inside PBA** as part of the workflow. Write, iterate, and publish all within PBA. Not as a separate Claude session. PBA needs a chat interface where the user can interact with Claude using blog-writer skill logic to draft and iterate on the content.

## Q9: Chat Interface for Skill Interaction
**Q:** How do you envision interacting with the blog-writer skill inside PBA?
**A:** Chat interface in PBA for iterating with Claude/skill. An embedded conversational UI where user can prompt, see drafts, give feedback, and refine - all within PBA's content page.

## Q10: Version Preparation Timing
**Q:** When the blog post is finalized, should PBA prepare both versions upfront or Substack first?
**A:** Prepare both versions upfront when finalized. Generate Substack-formatted + blog HTML at the same time. Blog version sits ready, waiting for scheduled deploy date.

## Q11: Delay Configuration
**Q:** Is the 7-day delay global or per-post?
**A:** Global default, override per-post when needed. 7 days is the default, but can change for specific posts. Some posts might skip the blog entirely.

---

## Confirmed Workflow Summary

1. User opens PBA content page, starts a new blog post
2. PBA provides a chat interface where user interacts with Claude (using blog-writer skill logic) to draft and iterate
3. When finalized, PBA prepares both versions: Substack-formatted (all fields ready to copy) + blog HTML (matthewkruczek.ai template + canonical URL)
4. User manually pastes into Substack and publishes
5. PBA's RSS poller detects the Substack publication
6. PBA notifies user, user confirms -> schedules blog deploy for +7 days (configurable per-post, skippable)
7. When the scheduled date arrives, PBA stages the blog HTML - user clicks "publish" to trigger git commit/deploy
8. Blog publishing dashboard shows all posts and their two-stage status
