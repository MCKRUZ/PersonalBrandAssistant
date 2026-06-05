export interface DigestItem {
  ideaId: string;
  rank: number;
  score: number;
  whyItMatters: string;
  title: string;
  url: string | null;
}

export interface Digest {
  id: string;
  date: string;
  title: string;
  intro: string;
  itemCount: number;
  createdAt: string;
  items: DigestItem[];
}

export interface DigestSummary {
  id: string;
  date: string;
  title: string;
  itemCount: number;
  createdAt: string;
}
