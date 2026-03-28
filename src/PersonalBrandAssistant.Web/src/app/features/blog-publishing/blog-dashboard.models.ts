export interface BlogPipelineItem {
  id: string;
  title: string;
  status: string;
  createdAt: string;
  substackPostUrl: string | null;
  blogPostUrl: string | null;
  blogDeployCommitSha: string | null;
  blogSkipped: boolean;
  blogDelayDays: number | null;
  substack: { status: string; publishedAt: string | null; postUrl: string | null } | null;
  personalBlog: { status: string; scheduledAt: string | null; publishedAt: string | null; postUrl: string | null } | null;
}

export interface DashboardFilter {
  status?: string;
  from?: string;
  to?: string;
}

export interface DashboardStats {
  inPipeline: number;
  scheduled: number;
  published: number;
}
