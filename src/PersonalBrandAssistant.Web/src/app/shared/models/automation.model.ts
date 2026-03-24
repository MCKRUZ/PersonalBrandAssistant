export type AutomationRunStatus = 'Running' | 'Completed' | 'PartialFailure' | 'Failed';

export interface AutomationRun {
  readonly id: string;
  readonly triggeredAt: string;
  readonly status: AutomationRunStatus;
  readonly primaryContentId?: string;
  readonly imageFileId?: string;
  readonly imagePrompt?: string;
  readonly selectionReasoning?: string;
  readonly errorDetails?: string;
  readonly completedAt?: string;
  readonly durationMs: number;
  readonly platformVersionCount: number;
}

export interface AutomationConfig {
  readonly cronExpression: string;
  readonly timeZone: string;
  readonly enabled: boolean;
  readonly autonomyLevel: string;
  readonly topTrendsToConsider: number;
  readonly targetPlatforms: readonly string[];
  readonly imageGeneration: {
    readonly enabled: boolean;
    readonly comfyUiBaseUrl: string;
    readonly timeoutSeconds: number;
    readonly defaultWidth: number;
    readonly defaultHeight: number;
    readonly modelCheckpoint: string;
    readonly circuitBreakerThreshold: number;
  };
}

export interface TriggerResult {
  readonly runId: string;
  readonly success: boolean;
}
