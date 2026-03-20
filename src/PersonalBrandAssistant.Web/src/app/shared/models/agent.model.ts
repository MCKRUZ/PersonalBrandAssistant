import { AgentCapabilityType, AgentExecutionStatus, ModelTier } from './enums';

export interface AgentExecuteRequest {
  readonly type: AgentCapabilityType;
  readonly contentId?: string;
  readonly parameters?: Readonly<Record<string, string>>;
}

export interface AgentExecution {
  readonly id: string;
  readonly contentId?: string;
  readonly agentType: AgentCapabilityType;
  readonly status: AgentExecutionStatus;
  readonly modelUsed: ModelTier;
  readonly modelId?: string;
  readonly inputTokens: number;
  readonly outputTokens: number;
  readonly cacheReadTokens: number;
  readonly cacheCreationTokens: number;
  readonly cost: number;
  readonly startedAt: string;
  readonly completedAt?: string;
  readonly duration?: string;
  readonly error?: string;
  readonly outputSummary?: string;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface SseEvent {
  readonly event: string;
  readonly data: string;
}

export interface AgentUsage {
  readonly from: string;
  readonly to: string;
  readonly totalCost: number;
}

export interface AgentBudget {
  readonly budgetRemaining: number;
  readonly isOverBudget: boolean;
}
