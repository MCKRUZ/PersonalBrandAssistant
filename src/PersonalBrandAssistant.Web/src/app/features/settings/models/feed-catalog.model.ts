export interface CatalogFeed {
  readonly name: string;
  readonly feedUrl: string;
  readonly description: string;
  readonly category: string;
  readonly tags: readonly string[];
}
