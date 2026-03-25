import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Card } from 'primeng/card';
import { SubstackPost } from '../models/dashboard.model';

@Component({
  selector: 'app-substack-section',
  standalone: true,
  imports: [CommonModule, Card, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-card styleClass="substack-card">
      <div class="section-header">
        <i class="pi pi-at section-icon"></i>
        <span>Substack</span>
        <a href="https://matthewkruczek.substack.com" target="_blank" rel="noopener noreferrer" class="external-link" aria-label="Open Substack in new tab">
          <i class="pi pi-external-link" aria-hidden="true"></i>
        </a>
      </div>

      @if (posts().length > 0) {
        <div class="post-list">
          @for (post of posts(); track post.url; let last = $last) {
            <div class="substack-post" [class.last]="last">
              <a [href]="post.url" target="_blank" rel="noopener noreferrer" class="post-title">
                {{ post.title }}
              </a>
              <span class="post-date">{{ post.publishedAt | date:'mediumDate' }}</span>
              @if (post.summary) {
                <p class="post-summary">{{ post.summary }}</p>
              }
            </div>
          }
        </div>
      } @else {
        <div class="empty-state">
          <i class="pi pi-at empty-icon"></i>
          <span>No Substack posts found</span>
        </div>
      }
    </p-card>
  `,
  styles: `
    :host {
      display: block;
    }
    :host ::ng-deep .substack-card {
      border-left: 3px solid #ff6719;
    }
    .section-header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 1rem;
      font-weight: 700;
      margin-bottom: 1rem;
    }
    .section-icon {
      color: #ff6719;
      font-size: 1.1rem;
    }
    .external-link {
      margin-left: auto;
      color: var(--p-text-muted-color, #71717a);
      font-size: 0.8rem;
      transition: color 0.2s;
    }
    .external-link:hover {
      color: #ff6719;
    }

    .post-list {
      display: flex;
      flex-direction: column;
    }
    .substack-post {
      padding: 0.6rem 0;
      border-bottom: 1px solid var(--p-surface-700, #25252f);
    }
    .substack-post.last {
      border-bottom: none;
    }
    .post-title {
      display: block;
      font-size: 0.9rem;
      font-weight: 600;
      color: var(--p-text-color, #e4e4e7);
      text-decoration: none;
      transition: text-decoration 0.2s;
    }
    .post-title:hover {
      text-decoration: underline;
    }
    .post-date {
      display: block;
      font-size: 0.75rem;
      color: var(--p-text-muted-color, #71717a);
      margin-top: 0.15rem;
    }
    .post-summary {
      font-size: 0.8rem;
      color: var(--p-text-muted-color, #71717a);
      margin: 0.25rem 0 0;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.5rem;
      padding: 1.5rem 0;
      color: var(--p-text-muted-color, #71717a);
      font-size: 0.85rem;
    }
    .empty-icon {
      font-size: 1.5rem;
      opacity: 0.5;
    }
  `,
})
export class SubstackSectionComponent {
  readonly posts = input<readonly SubstackPost[]>([]);
}
