export interface BlogHtmlResult {
  html: string;
  filePath: string;
  canonicalUrl: string | null;
}

export interface BlogPublishResult {
  commitSha: string;
  commitUrl: string;
  blogUrl: string;
  status: string;
  deployed: boolean;
}

export interface BlogDeployStatus {
  commitSha: string | null;
  blogUrl: string | null;
  status: string;
  publishedAt: string | null;
  errorMessage: string | null;
}
