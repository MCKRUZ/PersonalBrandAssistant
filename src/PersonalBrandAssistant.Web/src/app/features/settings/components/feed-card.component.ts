import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { ButtonModule } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { NewsSource } from '../../news/models/news.model';

@Component({
  selector: 'app-feed-card',
  standalone: true,
  imports: [FormsModule, ToggleSwitch, ButtonModule, Tag],
  template: `
    <div class="feed-card">
      <div class="feed-card__info">
        <div class="feed-card__name">{{ feed().name }}</div>
        @if (feed().feedUrl) {
          <div class="feed-card__url">{{ feed().feedUrl }}</div>
        }
        @if (feed().category) {
          <p-tag [value]="feed().category!" [style]="{ fontSize: '0.7rem' }" />
        }
      </div>
      <div class="feed-card__actions">
        <p-toggleswitch
          [ngModel]="feed().isEnabled"
          (ngModelChange)="toggle.emit(feed().id)"
        />
        <button
          pButton
          icon="pi pi-trash"
          severity="danger"
          [outlined]="true"
          size="small"
          [rounded]="true"
          (click)="delete.emit(feed().id)"
        ></button>
      </div>
    </div>
  `,
  styles: [`
    .feed-card {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      padding: 0.75rem 1rem;
      border-radius: 8px;
      background: rgba(255, 255, 255, 0.02);
      border: 1px solid rgba(255, 255, 255, 0.06);
    }
    .feed-card__info {
      flex: 1;
      min-width: 0;
    }
    .feed-card__name {
      font-weight: 600;
      font-size: 0.88rem;
      color: #f4f4f5;
    }
    .feed-card__url {
      font-size: 0.75rem;
      color: #71717a;
      margin: 0.15rem 0 0.4rem;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .feed-card__actions {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      flex-shrink: 0;
    }
  `],
})
export class FeedCardComponent {
  feed = input.required<NewsSource>();
  toggle = output<string>();
  delete = output<string>();
}
