import { Component, input, output } from '@angular/core';
import { ContentCardComponent } from '../content-card/content-card.component';
import { Content } from '../../models/content.model';

@Component({
  selector: 'app-content-grid',
  standalone: true,
  imports: [ContentCardComponent],
  template: `
    <div class="content-grid" data-testid="content-grid">
      @for (content of contents(); track content.id) {
        <app-content-card
          [content]="content"
          (edit)="edit.emit($event)"
          (onDelete)="onDelete.emit($event)"
          (duplicate)="duplicate.emit($event)" />
      } @empty {
        <div class="empty-state" data-testid="empty-state">
          <i class="pi pi-file-edit"></i>
          <p>No content found</p>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .content-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
        gap: 16px;
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
    `,
  ],
})
export class ContentGridComponent {
  readonly contents = input.required<Content[]>();
  readonly edit = output<string>();
  readonly onDelete = output<string>();
  readonly duplicate = output<string>();
}
