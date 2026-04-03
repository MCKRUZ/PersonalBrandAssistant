export enum BlogPipelineStage {
  Draft = 0,
  Image = 1,
  Substack = 2,
  Website = 3,
  Social = 4,
}

export const PIPELINE_STAGE_LABELS: Record<BlogPipelineStage, string> = {
  [BlogPipelineStage.Draft]: 'Draft',
  [BlogPipelineStage.Image]: 'Image',
  [BlogPipelineStage.Substack]: 'Substack',
  [BlogPipelineStage.Website]: 'Website',
  [BlogPipelineStage.Social]: 'Social',
};

export const PIPELINE_STAGE_ICONS: Record<BlogPipelineStage, string> = {
  [BlogPipelineStage.Draft]: 'pi pi-pencil',
  [BlogPipelineStage.Image]: 'pi pi-image',
  [BlogPipelineStage.Substack]: 'pi pi-envelope',
  [BlogPipelineStage.Website]: 'pi pi-globe',
  [BlogPipelineStage.Social]: 'pi pi-share-alt',
};

export const PIPELINE_STAGES = [
  BlogPipelineStage.Draft,
  BlogPipelineStage.Image,
  BlogPipelineStage.Substack,
  BlogPipelineStage.Website,
  BlogPipelineStage.Social,
] as const;

export interface BlogStageTransition {
  readonly fromStage: BlogPipelineStage;
  readonly toStage: BlogPipelineStage;
  readonly transitionedAt: string;
  readonly note: string | null;
}

export interface BlogPipelineItem {
  readonly id: string;
  readonly title: string | null;
  readonly status: string;
  readonly createdAt: string;
  readonly currentBlogStage: BlogPipelineStage;
  readonly blogStageHistory: readonly BlogStageTransition[];
  readonly substackPostUrl: string | null;
  readonly blogPostUrl: string | null;
  readonly blogSkipped: boolean;
}
