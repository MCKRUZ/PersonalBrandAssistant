import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ProseBlock } from '../markdown-blocks';

@Component({
  selector: 'app-medium-preview',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="medium-card">
      <h1 class="headline">{{ title() }}</h1>
      @if (subtitle()) {
        <p class="subtitle">{{ subtitle() }}</p>
      }
      <div class="author-row">
        <span class="avatar"></span>
        <span class="meta">{{ byline() }} · 5 min read</span>
        <span class="follow">Follow</span>
      </div>
      <div class="actions">
        <span class="action">👏 0</span>
        <span class="action">🔖</span>
      </div>
      <div class="prose">
        @for (block of blocks(); track $index) {
          @switch (block.type) {
            @case ('h2') { <h2 class="subhead">{{ block.text }}</h2> }
            @case ('h3') { <h3 class="subhead subhead--sm">{{ block.text }}</h3> }
            @default { <p class="para">{{ block.text }}</p> }
          }
        }
      </div>
    </article>
  `,
  styles: [`
    .medium-card {
      background: #fff;
      color: #292929;
      border-radius: 10px;
      padding: 28px 32px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
      font-family: -apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
    }
    .headline {
      font-size: 32px;
      font-weight: 800;
      line-height: 1.18;
      margin: 0 0 10px;
      color: #242424;
    }
    .subtitle {
      font-size: 20px;
      color: #6b6b6b;
      line-height: 1.4;
      margin: 0 0 20px;
    }
    .author-row {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 16px;
    }
    .avatar {
      width: 40px;
      height: 40px;
      border-radius: 50%;
      background: #cfcfcf;
      flex: 0 0 auto;
    }
    .meta {
      font-size: 14px;
      color: #292929;
    }
    .follow {
      margin-left: auto;
      font-size: 13px;
      color: #1a8917;
      border: 1px solid #1a8917;
      border-radius: 999px;
      padding: 4px 12px;
    }
    .actions {
      display: flex;
      gap: 18px;
      padding: 10px 0;
      border-top: 1px solid #ececec;
      border-bottom: 1px solid #ececec;
      margin-bottom: 18px;
      font-size: 14px;
      color: #6b6b6b;
    }
    .subhead {
      font-size: 24px;
      font-weight: 700;
      margin: 22px 0 8px;
      color: #242424;
    }
    .subhead--sm { font-size: 20px; }
    .para {
      font-size: 18px;
      line-height: 1.6;
      margin: 0 0 16px;
      color: #292929;
    }
  `],
})
export class MediumPreviewComponent {
  readonly blocks = input.required<ProseBlock[]>();
  readonly title = input('');
  readonly subtitle = input('');
  readonly byline = input('You');
}
