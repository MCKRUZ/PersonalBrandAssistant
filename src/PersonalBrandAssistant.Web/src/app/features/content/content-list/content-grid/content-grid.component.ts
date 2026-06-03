import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ContentCardComponent } from '../content-card/content-card.component';
import { Content } from '../../models/content.model';

/**
 * Grid view: the board card variant laid out in a responsive grid. Clicking a card opens the
 * detail drawer via `openCard`.
 */
@Component({
  selector: 'app-content-grid',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ContentCardComponent],
  template: `
    <div class="content-grid" data-testid="content-grid">
      @for (content of contents(); track content.id) {
        <app-content-card
          [content]="content"
          variant="board"
          (click)="openCard.emit(content.id)" />
      } @empty {
        <div class="empty-state" data-testid="empty-state">
          <i class="pi pi-file-edit"></i>
          <p>No content found</p>
        </div>
      }
    </div>
  `,
  styles: [`
    .content-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(286px, 1fr));
      gap: 16px;
      padding: 4px 28px 28px;
    }
    .empty-state {
      grid-column: 1 / -1;
      text-align: center;
      padding: 48px 16px;
      color: var(--text-secondary);
    }
    .empty-state i {
      font-size: 32px;
      margin-bottom: 8px;
    }
  `],
})
export class ContentGridComponent {
  readonly contents = input.required<Content[]>();
  readonly openCard = output<string>();
}
