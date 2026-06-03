// ===== PBAv2 Content Studio — sample data + domain helpers =====
// Mirrors PersonalBrandAssistant.Web content.model.ts

const STATUSES = ['Idea', 'Draft', 'Review', 'Approved', 'Scheduled', 'Published', 'Archived'];

// status -> accent color (from _variables.scss + harmonious extensions)
const STATUS_META = {
  Idea:      { color: '#8a7df0', label: 'Idea' },      // soft violet
  Draft:     { color: '#8a8a96', label: 'Draft' },     // muted
  Review:    { color: '#c87156', label: 'Review' },    // terracotta (brand)
  Approved:  { color: '#4ade80', label: 'Approved' },  // green
  Scheduled: { color: '#60a5fa', label: 'Scheduled' }, // blue
  Published: { color: '#34d399', label: 'Published' }, // emerald
  Archived:  { color: '#5a5a66', label: 'Archived' },  // grey
};

// Platforms — restrained, desaturated accents + 2-letter codes (no brand logos)
// delivery: 'auto' = connected API, deploys on click | 'manual' = we format, you post
// connected: is the account linked? charLimit: hard cap (null = none) | fmt: format note
const PLATFORM_META = {
  Blog:     { code: 'BL', color: '#c87156', label: 'Blog',        delivery: 'auto',   connected: true,  charLimit: null, fmt: 'Markdown + HTML, images, full formatting' },
  Medium:   { code: 'ME', color: '#cfcfd6', label: 'Medium',      delivery: 'manual', connected: true,  charLimit: null, fmt: 'No publish API — paste into Medium' },
  Substack: { code: 'SU', color: '#e08a4b', label: 'Substack',    delivery: 'auto',   connected: true,  charLimit: null, fmt: 'Newsletter — sends to subscribers automatically' },
  LinkedIn: { code: 'IN', color: '#5b9bd5', label: 'LinkedIn',    delivery: 'auto',   connected: true,  charLimit: 3000, fmt: 'Posts automatically to your profile' },
  Twitter:  { code: 'TW', color: '#9aa7b3', label: 'Twitter / X',  delivery: 'auto',   connected: false, charLimit: 280,  fmt: 'Splits into a numbered thread' },
  Reddit:   { code: 'RE', color: '#d97551', label: 'Reddit',      delivery: 'manual', connected: false, charLimit: null, fmt: 'Pick a subreddit, post manually' },
  YouTube:  { code: 'YT', color: '#d96a6a', label: 'YouTube',     delivery: 'manual', connected: false, charLimit: null, fmt: 'Community post / description' },
};

// Platforms we can target when publishing prose
const PUBLISHABLE = ['Blog', 'Medium', 'Substack', 'LinkedIn', 'Twitter'];

const TYPE_META = {
  Blog:               { glyph: '¶', label: 'Blog post' },
  LinkedInPost:       { glyph: '▤', label: 'LinkedIn post' },
  Tweet:              { glyph: '·', label: 'Tweet' },
  ThreadedTweet:      { glyph: '⋮', label: 'Thread' },
  SubstackNewsletter: { glyph: '✉', label: 'Newsletter' },
  RedditPost:         { glyph: '◇', label: 'Reddit post' },
  YouTubeVideo:       { glyph: '▷', label: 'Video' },
  YouTubeShort:       { glyph: '▹', label: 'Short' },
};

function scoreColor(s) {
  if (s == null) return '#5a5a66';
  if (s >= 80) return '#4ade80';
  if (s >= 60) return '#fbbf24';
  return '#f87171';
}

function daysAgo(n) {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return d.toISOString();
}
function hoursAgo(n) {
  const d = new Date();
  d.setHours(d.getHours() - n);
  return d.toISOString();
}
function daysAhead(n) {
  const d = new Date();
  d.setDate(d.getDate() + n);
  return d.toISOString();
}

function relTime(iso) {
  const then = new Date(iso).getTime();
  const now = Date.now();
  const diff = now - then;
  const ahead = diff < 0;
  const a = Math.abs(diff);
  const min = Math.round(a / 60000);
  const hr = Math.round(a / 3600000);
  const day = Math.round(a / 86400000);
  let s;
  if (min < 60) s = `${min}m`;
  else if (hr < 24) s = `${hr}h`;
  else if (day < 14) s = `${day}d`;
  else s = `${Math.round(day / 7)}w`;
  return ahead ? `in ${s}` : `${s} ago`;
}

let _id = 0;
const uid = () => `c${++_id}`;

const SAMPLE_CONTENT = [
  {
    id: uid(), title: 'Why most "personal brand" advice quietly fails solo founders',
    type: 'Blog', status: 'Idea', primaryPlatform: 'Blog',
    targetPlatforms: ['Blog', 'Medium'], voiceScore: null,
    tags: ['founders', 'positioning'], updatedAt: hoursAgo(3), scheduledAt: null,
  },
  {
    id: uid(), title: 'The 3-layer system I use to turn one essay into a week of posts',
    type: 'Blog', status: 'Idea', primaryPlatform: 'Blog',
    targetPlatforms: ['Blog', 'Substack'], voiceScore: null,
    tags: ['workflow', 'repurposing'], updatedAt: hoursAgo(20), scheduledAt: null,
  },
  {
    id: uid(), title: 'Hot take: engagement bait is eating your credibility',
    type: 'Tweet', status: 'Idea', primaryPlatform: 'Twitter',
    targetPlatforms: ['Twitter'], voiceScore: null,
    tags: ['opinion'], updatedAt: daysAgo(2), scheduledAt: null,
  },

  {
    id: uid(), title: 'Building in public is a skill, not a personality trait',
    type: 'LinkedInPost', status: 'Draft', primaryPlatform: 'LinkedIn',
    targetPlatforms: ['LinkedIn'], voiceScore: 58,
    tags: ['build-in-public'], updatedAt: hoursAgo(5), scheduledAt: null,
  },
  {
    id: uid(), title: 'My 2026 content stack: every tool, what it costs, what I cut',
    type: 'SubstackNewsletter', status: 'Draft', primaryPlatform: 'Substack',
    targetPlatforms: ['Substack', 'Medium'], voiceScore: 71,
    tags: ['tools', 'transparency'], updatedAt: daysAgo(1), scheduledAt: null,
  },
  {
    id: uid(), title: 'A thread on pricing your first digital product',
    type: 'ThreadedTweet', status: 'Draft', primaryPlatform: 'Twitter',
    targetPlatforms: ['Twitter'], voiceScore: 64,
    tags: ['pricing', 'products'], updatedAt: daysAgo(3), scheduledAt: null,
  },

  {
    id: uid(), title: 'What I learned shipping 100 days of daily writing',
    type: 'Blog', status: 'Review', primaryPlatform: 'Blog',
    targetPlatforms: ['Blog', 'Medium', 'LinkedIn'], voiceScore: 82,
    tags: ['writing', 'habits'], updatedAt: hoursAgo(8), scheduledAt: null,
  },
  {
    id: uid(), title: 'The case against posting every day',
    type: 'LinkedInPost', status: 'Review', primaryPlatform: 'LinkedIn',
    targetPlatforms: ['LinkedIn'], voiceScore: 77,
    tags: ['strategy'], updatedAt: daysAgo(1), scheduledAt: null,
  },

  {
    id: uid(), title: 'How I research a topic in 25 minutes flat',
    type: 'Blog', status: 'Approved', primaryPlatform: 'Blog',
    targetPlatforms: ['Blog', 'Substack'], voiceScore: 91,
    tags: ['research', 'workflow'], updatedAt: daysAgo(1), scheduledAt: null,
  },

  {
    id: uid(), title: 'Newsletter #42 — The quiet power of a narrow audience',
    type: 'SubstackNewsletter', status: 'Scheduled', primaryPlatform: 'Substack',
    targetPlatforms: ['Substack'], voiceScore: 88,
    tags: ['newsletter', 'audience'], updatedAt: daysAgo(2), scheduledAt: daysAhead(1),
  },
  {
    id: uid(), title: '5 prompts that make AI sound like you, not a robot',
    type: 'LinkedInPost', status: 'Scheduled', primaryPlatform: 'LinkedIn',
    targetPlatforms: ['LinkedIn', 'Twitter'], voiceScore: 84,
    tags: ['ai', 'voice'], updatedAt: daysAgo(2), scheduledAt: daysAhead(3),
  },

  {
    id: uid(), title: 'Stop optimizing your bio. Optimize your last 10 posts.',
    type: 'Blog', status: 'Published', primaryPlatform: 'Blog',
    targetPlatforms: ['Blog', 'Medium'], voiceScore: 86,
    tags: ['positioning'], updatedAt: daysAgo(6), scheduledAt: null,
  },
  {
    id: uid(), title: 'The one-page content system (with template)',
    type: 'SubstackNewsletter', status: 'Published', primaryPlatform: 'Substack',
    targetPlatforms: ['Substack'], voiceScore: 93,
    tags: ['template', 'systems'], updatedAt: daysAgo(9), scheduledAt: null,
  },

  {
    id: uid(), title: 'Old draft: thoughts on the algorithm (2024)',
    type: 'Blog', status: 'Archived', primaryPlatform: 'Blog',
    targetPlatforms: ['Blog'], voiceScore: 49,
    tags: ['archive'], updatedAt: daysAgo(120), scheduledAt: null,
  },
];

// Idea suggestions for the inspiring empty state
const IDEA_SUGGESTIONS = [
  { topic: 'Repurposing', hook: 'Turn your best-performing post into a 5-part thread', type: 'ThreadedTweet' },
  { topic: 'Behind the scenes', hook: 'Share the messy first draft of something you shipped', type: 'LinkedInPost' },
  { topic: 'Contrarian take', hook: 'Argue against a piece of advice everyone repeats', type: 'Blog' },
  { topic: 'Teardown', hook: 'Break down a creator you admire — what actually works', type: 'SubstackNewsletter' },
];

// ---- Sample manuscript body (markdown-ish) used by the editor + previews ----
const SAMPLE_TITLE = 'What I learned shipping 100 days of daily writing';
const SAMPLE_SUBTITLE = 'A quieter, more honest take on consistency — and why the streak was never the point.';
const SAMPLE_BODY = [
  { type: 'p', text: "For 100 days straight I published something. A post, an essay, a half-formed thought. I expected the streak to make me a better writer. It did — but not the way the productivity blogs promised." },
  { type: 'h2', text: 'The streak was scaffolding, not the building' },
  { type: 'p', text: "What actually changed wasn't my output. It was the threshold for what felt worth saying out loud. When you have to ship daily, you stop waiting for the perfect idea and start noticing the small, true ones." },
  { type: 'p', text: "By week three the blank page stopped being a threat. By week six I had a backlog. By day 100 I had a voice I recognized — and a system I could repeat without the pressure of a streak." },
  { type: 'h2', text: 'Three things I would tell day-one me' },
  { type: 'p', text: "Write the version you'd be slightly embarrassed to publish. Cut the first paragraph — it's almost always throat-clearing. And measure the habit by whether you showed up, never by whether it performed." },
  { type: 'p', text: "The goal was never 100 days. It was becoming the kind of person who doesn't need a counter to keep going." },
];

// First N chars of the prose, for truncated-preview platforms (LinkedIn etc.)
function bodyPlainText() {
  return SAMPLE_BODY.map((b) => b.text).join('\n\n');
}

// Split prose into tweet-sized chunks (<=limit), numbered.
function splitThread(text, limit = 270) {
  const sentences = text.replace(/\n+/g, ' ').split(/(?<=[.!?]) /);
  const chunks = [];
  let cur = '';
  for (const s of sentences) {
    if ((cur + ' ' + s).trim().length > limit) {
      if (cur) chunks.push(cur.trim());
      cur = s;
    } else {
      cur = (cur + ' ' + s).trim();
    }
  }
  if (cur) chunks.push(cur.trim());
  return chunks.map((c, i) => `${c}`);
}

const TONES = ['My voice', 'Conversational', 'Punchy', 'Analytical', 'Story-driven'];

Object.assign(window, {
  STATUSES, STATUS_META, PLATFORM_META, TYPE_META, PUBLISHABLE,
  scoreColor, relTime, SAMPLE_CONTENT, IDEA_SUGGESTIONS,
  SAMPLE_TITLE, SAMPLE_SUBTITLE, SAMPLE_BODY, bodyPlainText, splitThread, TONES,
});
