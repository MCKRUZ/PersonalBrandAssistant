import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'relativeTime', standalone: true })
export class RelativeTimePipe implements PipeTransform {
  transform(value: string | null): string {
    if (!value) return '';
    const diffMs = Date.now() - new Date(value).getTime();
    if (diffMs < 0) return 'Just now';
    const minutes = Math.floor(diffMs / 60_000);
    if (minutes < 1) return 'Just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(diffMs / 3_600_000);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(diffMs / 86_400_000);
    if (days < 30) return `${days}d ago`;
    return `${Math.floor(days / 30)}mo ago`;
  }
}
