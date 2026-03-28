export interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
  timestamp: string;
}

export interface FinalizedDraft {
  title: string;
  subtitle: string;
  bodyMarkdown: string;
  seoDescription: string;
  tags: string[];
}
