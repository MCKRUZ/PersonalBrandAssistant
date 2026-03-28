import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'relativeTime', standalone: true })
export class RelativeTimePipe implements PipeTransform {
  transform(value: string | Date | undefined): string {
    if (!value) return '';
    const date = typeof value === 'string' ? new Date(value) : value;
    const now = Date.now();
    const diff = now - date.getTime();

    if (diff < 0) {
      const absDiff = Math.abs(diff);
      if (absDiff < 60_000) return 'in a few seconds';
      if (absDiff < 3_600_000) return `in ${Math.floor(absDiff / 60_000)}m`;
      if (absDiff < 86_400_000) return `in ${Math.floor(absDiff / 3_600_000)}h`;
      return `in ${Math.floor(absDiff / 86_400_000)}d`;
    }

    if (diff < 60_000) return 'just now';
    if (diff < 3_600_000) return `${Math.floor(diff / 60_000)}m ago`;
    if (diff < 86_400_000) return `${Math.floor(diff / 3_600_000)}h ago`;
    if (diff < 604_800_000) return `${Math.floor(diff / 86_400_000)}d ago`;
    return date.toLocaleDateString();
  }
}
