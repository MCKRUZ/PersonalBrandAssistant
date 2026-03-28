import { Component, input, output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { CatalogFeed } from '../models/feed-catalog.model';

@Component({
  selector: 'app-catalog-feed-card',
  standalone: true,
  imports: [ButtonModule, Tag],
  template: `
    <div class="catalog-card">
      <div class="catalog-card__info">
        <div class="catalog-card__name">{{ feed().name }}</div>
        <div class="catalog-card__desc">{{ feed().description }}</div>
        <p-tag [value]="feed().category" [style]="{ fontSize: '0.7rem' }" />
      </div>
      <button
        pButton
        [label]="subscribed() ? 'Subscribed' : 'Add'"
        [icon]="subscribed() ? 'pi pi-check' : 'pi pi-plus'"
        [disabled]="subscribed()"
        [severity]="subscribed() ? 'secondary' : 'success'"
        size="small"
        [outlined]="!subscribed()"
        (click)="add.emit(feed())"
      ></button>
    </div>
  `,
  styles: [`
    .catalog-card {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      padding: 0.75rem 1rem;
      border-radius: 8px;
      background: rgba(255,255,255,0.02);
      border: 1px solid rgba(255,255,255,0.06);
    }
    .catalog-card__info { flex: 1; min-width: 0; }
    .catalog-card__name { font-weight: 600; font-size: 0.88rem; color: #f4f4f5; }
    .catalog-card__desc { font-size: 0.78rem; color: #71717a; margin: 0.2rem 0 0.4rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  `],
})
export class CatalogFeedCardComponent {
  feed = input.required<CatalogFeed>();
  subscribed = input(false);
  add = output<CatalogFeed>();
}
