export interface AgentExecution {
  id: string;
  agentType: string;
  contentId?: string;
  startedAt: string;
  completedAt?: string;
  inputTokens: number;
  outputTokens: number;
  summary: string;
  status: 'Running' | 'Completed' | 'Failed';
}
