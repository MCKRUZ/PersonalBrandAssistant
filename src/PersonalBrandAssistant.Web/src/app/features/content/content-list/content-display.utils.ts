export function formatContentType(contentType: string): string {
  return contentType.replace(/([A-Z])/g, ' $1').trim();
}

export function voiceScoreClass(score: number | null): string {
  if (score === null) return 'voice-dot voice-none';
  if (score > 80) return 'voice-dot voice-green';
  if (score >= 60) return 'voice-dot voice-amber';
  return 'voice-dot voice-red';
}

export function platformIconClass(platform: string): string {
  const icons: Record<string, string> = {
    Blog: 'pi pi-globe',
    LinkedIn: 'pi pi-linkedin',
    Twitter: 'pi pi-twitter',
    Substack: 'pi pi-envelope',
    Reddit: 'pi pi-comments',
    YouTube: 'pi pi-youtube',
  };
  return icons[platform] ?? 'pi pi-globe';
}

export function truncateText(text: string, maxLength: number): string {
  return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
}
