export interface HeatmapCell {
  readonly day: number;
  readonly hour: number;
  readonly engagement: number;
}

export interface BestTimesHeatmap {
  readonly cells: readonly HeatmapCell[];
  readonly maxEngagement: number;
}
