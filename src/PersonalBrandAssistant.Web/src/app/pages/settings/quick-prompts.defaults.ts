export const QUICK_PROMPTS: Record<string, string[]> = {
  dashboard: ['Summarize today\'s schedule', 'What should I post next?', 'Review pending items'],
  'content-editor': ['Tighten this draft', 'Repurpose for LinkedIn', 'Check brand voice', 'Suggest a hook'],
  'approval-queue': ['Review all pending', 'Score this batch', 'Draft rejection notes'],
  calendar: ['Find gaps this week', 'Suggest content for empty slots', 'Optimize posting times'],
  analytics: ['Summarize this week\'s performance', 'Compare platforms', 'Suggest improvements'],
  settings: ['Review brand voice config', 'Suggest tone adjustments'],
};

export const QUICK_PROMPTS_STORAGE_KEY = 'pba-quick-prompts';
