import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InputTextModule } from 'primeng/inputtext';
import { AccordionModule } from 'primeng/accordion';
import { BadgeModule } from 'primeng/badge';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { FeedCardComponent } from './feed-card.component';
import { CatalogFeedCardComponent } from './catalog-feed-card.component';
import { Select } from 'primeng/select';
import { ButtonModule } from 'primeng/button';
import { FeedManagementStore } from '../store/feed-management.store';
import { CATEGORY_ORDER } from '../../news/models/news.model';

@Component({
  selector: 'app-news-feeds-panel',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    InputTextModule,
    AccordionModule,
    BadgeModule,
    IconField,
    InputIcon,
    FeedCardComponent,
    CatalogFeedCardComponent,
    Select,
    ButtonModule,
  ],
  template: `
    <div class="feeds-panel">
      <div class="feeds-panel__header">
        <h3 class="feeds-panel__title">News Feeds</h3>
        <p-badge [value]="'' + store.feeds().length" severity="info" />
      </div>

      <div class="feeds-panel__search">
        <p-iconfield>
          <p-inputicon styleClass="pi pi-search" />
          <input
            type="text"
            pInputText
            placeholder="Search feed catalog..."
            [ngModel]="store.searchQuery()"
            (ngModelChange)="store.setSearchQuery($event)"
          />
        </p-iconfield>

        @if (store.searchQuery().length >= 2 && store.searchResults().length > 0) {
          <div class="feeds-panel__results">
            @for (result of store.searchResults(); track result.feedUrl) {
              <app-catalog-feed-card
                [feed]="result"
                [subscribed]="store.subscribedUrls().has(result.feedUrl)"
                (add)="store.addFeed($event)"
              />
            }
          </div>
        }

        @if (isUrl(store.searchQuery()) && store.searchResults().length === 0) {
          <div class="feeds-panel__custom-add">
            <div class="feeds-panel__custom-row">
              <input
                type="text"
                pInputText
                placeholder="Feed name"
                [(ngModel)]="customFeedName"
                [style]="{ flex: '1' }"
              />
              <p-select
                [options]="categoryOptions"
                [(ngModel)]="customCategory"
                placeholder="Category"
                [style]="{ minWidth: '140px' }"
              />
              <button
                pButton
                label="Add"
                icon="pi pi-plus"
                severity="success"
                size="small"
                [disabled]="!customFeedName.trim()"
                (click)="addCustomFeed()"
              ></button>
            </div>
          </div>
        }
      </div>

      @if (store.feedsByCategory().length > 0) {
        <p-accordion [multiple]="true">
          @for (group of store.feedsByCategory(); track group.category) {
            <p-accordion-panel [value]="group.category">
              <p-accordion-header>
                <span>{{ group.category }}</span>
                <p-badge
                  [value]="'' + group.feeds.length"
                  severity="secondary"
                  [style]="{ marginLeft: '0.5rem' }"
                />
              </p-accordion-header>
              <p-accordion-content>
                <div class="feeds-panel__list">
                  @for (feed of group.feeds; track feed.id) {
                    <app-feed-card
                      [feed]="feed"
                      (toggle)="store.toggleFeed($event)"
                      (delete)="store.deleteFeed($event)"
                    />
                  }
                </div>
              </p-accordion-content>
            </p-accordion-panel>
          }
        </p-accordion>
      }
    </div>
  `,
  styles: [`
    .feeds-panel {
      background: rgba(255, 255, 255, 0.02);
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 12px;
      padding: 1.5rem;
    }
    .feeds-panel__header {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      margin-bottom: 1rem;
    }
    .feeds-panel__title {
      margin: 0;
      font-size: 1.1rem;
      font-weight: 600;
      color: #f4f4f5;
    }
    .feeds-panel__search {
      position: relative;
      margin-bottom: 1.25rem;
    }
    .feeds-panel__search p-iconfield {
      width: 100%;
    }
    .feeds-panel__search input {
      width: 100%;
    }
    .feeds-panel__results {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      margin-top: 0.75rem;
      max-height: 320px;
      overflow-y: auto;
      padding: 0.75rem;
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 8px;
    }
    .feeds-panel__list {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }
    .feeds-panel__custom-add {
      margin-top: 0.75rem;
      padding: 0.75rem;
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 8px;
    }
    .feeds-panel__custom-row {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
  `],
})
export class NewsFeedsPanelComponent implements OnInit {
  readonly store = inject(FeedManagementStore);
  readonly categoryOptions = [...CATEGORY_ORDER];

  customFeedName = '';
  customCategory = 'General Tech';

  ngOnInit(): void {
    this.store.loadFeeds(undefined);
  }

  isUrl(value: string): boolean {
    return /^https?:\/\/.+/i.test(value.trim());
  }

  addCustomFeed(): void {
    const url = this.store.searchQuery().trim();
    const name = this.customFeedName.trim();
    if (!url || !name) return;

    this.store.addFeed({
      name,
      feedUrl: url,
      category: this.customCategory,
      description: 'Custom feed',
      tags: [],
    });

    this.customFeedName = '';
    this.store.setSearchQuery('');
  }
}
