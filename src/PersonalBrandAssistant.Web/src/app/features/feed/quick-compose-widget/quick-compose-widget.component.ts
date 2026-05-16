import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { IdeaService } from '../../../core/services/idea.service';
import { ContentService } from '../../content/services/content.service';
import { ContentType, Platform } from '../../content/models/content.model';

type ComposeMode = 'idea' | 'content';

@Component({
  selector: 'app-quick-compose-widget',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="widget">
      <div class="mode-tabs">
        <button type="button"
                class="mode-tab"
                [class.active]="mode() === 'idea'"
                data-testid="mode-idea"
                (click)="setMode('idea')">
          <i class="pi pi-lightbulb"></i> Quick Idea
        </button>
        <button type="button"
                class="mode-tab"
                [class.active]="mode() === 'content'"
                data-testid="mode-content"
                (click)="setMode('content')">
          <i class="pi pi-pencil"></i> New Content
        </button>
      </div>

      <form class="compose-form" (ngSubmit)="onSubmit()">
        <input type="text"
               class="form-input"
               placeholder="Title"
               data-testid="title-field"
               [(ngModel)]="title"
               name="title"
               required />

        @if (mode() === 'idea') {
          <textarea class="form-input form-textarea"
                    placeholder="Notes (optional)"
                    data-testid="note-field"
                    [(ngModel)]="note"
                    name="note"
                    rows="3"></textarea>
        } @else {
          <select class="form-input"
                  data-testid="content-type-field"
                  [(ngModel)]="contentType"
                  name="contentType">
            @for (ct of contentTypes; track ct.value) {
              <option [value]="ct.value">{{ ct.label }}</option>
            }
          </select>
        }

        @if (errorMessage()) {
          <p class="error-message" data-testid="error-message">{{ errorMessage() }}</p>
        }

        <button type="submit"
                class="submit-btn"
                data-testid="submit-btn"
                [disabled]="submitting() || !title.trim()">
          @if (submitting()) {
            <i class="pi pi-spinner pi-spin"></i>
          } @else {
            {{ mode() === 'idea' ? 'Save Idea' : 'Create' }}
          }
        </button>
      </form>
    </div>
  `,
  styles: [`
    .widget {
      background: #161b22;
      border: 1px solid #30363d;
      border-radius: 8px;
      padding: 16px;
    }

    .mode-tabs {
      display: flex;
      gap: 4px;
      margin-bottom: 12px;
    }

    .mode-tab {
      flex: 1;
      padding: 8px;
      font-size: 12px;
      font-weight: 500;
      color: #8b949e;
      background: transparent;
      border: 1px solid #30363d;
      border-radius: 6px;
      cursor: pointer;
      transition: all 0.15s;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
    }

    .mode-tab:hover { color: #c9d1d9; border-color: #8b949e; }

    .mode-tab.active {
      color: #f0f6fc;
      background: #21262d;
      border-color: #58a6ff;
    }

    .compose-form {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .form-input {
      width: 100%;
      padding: 8px 10px;
      font-size: 13px;
      color: #f0f6fc;
      background: #0d1117;
      border: 1px solid #30363d;
      border-radius: 6px;
      outline: none;
      transition: border-color 0.15s;
      box-sizing: border-box;
      font-family: inherit;
    }

    .form-input:focus { border-color: #58a6ff; }
    .form-input::placeholder { color: #484f58; }

    .form-textarea { resize: vertical; min-height: 60px; }

    select.form-input {
      cursor: pointer;
      appearance: auto;
    }

    .submit-btn {
      padding: 8px 16px;
      font-size: 13px;
      font-weight: 500;
      color: #fff;
      background: #238636;
      border: 1px solid #2ea043;
      border-radius: 6px;
      cursor: pointer;
      transition: background 0.15s;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
    }

    .submit-btn:hover:not(:disabled) { background: #2ea043; }
    .submit-btn:disabled { opacity: 0.5; cursor: not-allowed; }

    .error-message {
      font-size: 12px;
      color: #f85149;
      margin: 0;
    }
  `]
})
export class QuickComposeWidgetComponent {
  private readonly ideaService = inject(IdeaService);
  private readonly contentService = inject(ContentService);
  private readonly router = inject(Router);

  protected readonly mode = signal<ComposeMode>('idea');
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected title = '';
  protected note = '';
  protected contentType = ContentType.BlogPost;

  protected readonly contentTypes = [
    { value: ContentType.BlogPost, label: 'Blog Post' },
    { value: ContentType.LinkedInPost, label: 'LinkedIn Post' },
    { value: ContentType.Tweet, label: 'Tweet' },
    { value: ContentType.ThreadedTweet, label: 'Threaded Tweet' },
    { value: ContentType.SubstackNewsletter, label: 'Newsletter' },
    { value: ContentType.YouTubeVideo, label: 'YouTube Video' },
  ];

  private static readonly PLATFORM_MAP: Record<string, Platform> = {
    [ContentType.BlogPost]: Platform.Blog,
    [ContentType.LinkedInPost]: Platform.LinkedIn,
    [ContentType.Tweet]: Platform.Twitter,
    [ContentType.ThreadedTweet]: Platform.Twitter,
    [ContentType.SubstackNewsletter]: Platform.Substack,
    [ContentType.RedditPost]: Platform.Reddit,
    [ContentType.YouTubeVideo]: Platform.YouTube,
    [ContentType.YouTubeShort]: Platform.YouTube,
  };

  protected setMode(m: ComposeMode): void {
    this.mode.set(m);
  }

  protected onSubmit(): void {
    if (!this.title.trim() || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    if (this.mode() === 'idea') {
      this.ideaService.create({ title: this.title.trim(), description: this.note.trim() || undefined }).subscribe({
        next: () => {
          this.title = '';
          this.note = '';
          this.submitting.set(false);
        },
        error: () => {
          this.submitting.set(false);
          this.errorMessage.set('Failed to save idea. Please try again.');
        },
      });
    } else {
      const platform = QuickComposeWidgetComponent.PLATFORM_MAP[this.contentType] ?? Platform.Blog;
      this.contentService.create({
        title: this.title.trim(),
        contentType: this.contentType,
        primaryPlatform: platform,
        tags: [],
      }).subscribe({
        next: (id) => {
          this.submitting.set(false);
          this.title = '';
          this.contentType = ContentType.BlogPost;
          this.router.navigate(['/content', id]);
        },
        error: () => {
          this.submitting.set(false);
          this.errorMessage.set('Failed to create content. Please try again.');
        },
      });
    }
  }
}
