export interface SubstackPreparedContent {
  title: string;
  subtitle: string;
  body: string;
  seoDescription: string;
  tags: string[];
  sectionName: string | null;
  previewText: string;
  canonicalUrl: string | null;
}

export interface SubstackPublishConfirmation {
  contentId: string;
  substackPostUrl: string | null;
  wasAlreadyPublished: boolean;
}
