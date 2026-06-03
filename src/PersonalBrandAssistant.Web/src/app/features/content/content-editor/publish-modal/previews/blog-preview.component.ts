import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ProseBlock } from '../markdown-blocks';

@Component({
  selector: 'app-blog-preview',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="blog-card">
      <div class="hero"></div>
      <div class="body">
        <span class="kicker">Article</span>
        <h1 class="headline">{{ title() }}</h1>
        @if (subtitle()) {
          <p class="lede">{{ subtitle() }}</p>
        }
        <div class="byline">
          <span class="avatar"></span>
          <span class="by-name">{{ byline() }}</span>
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
      </div>
    </article>
  `,
  styles: [`
    .blog-card {
      background: #fff;
      color: #222;
      border-radius: 10px;
      overflow: hidden;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
      font-family: Georgia, 'Times New Roman', serif;
    }
    .hero {
      height: 120px;
      background: repeating-linear-gradient(45deg, #e9e3d5, #e9e3d5 12px, #ddd4c0 12px, #ddd4c0 24px);
    }
    .body { padding: 24px 28px 28px; }
    .kicker {
      display: inline-block;
      font-family: Arial, Helvetica, sans-serif;
      font-size: 11px;
      letter-spacing: 0.12em;
      text-transform: uppercase;
      color: #8a7f6b;
      margin-bottom: 10px;
    }
    .headline {
      font-size: 30px;
      line-height: 1.2;
      margin: 0 0 12px;
      color: #1a1a1a;
    }
    .lede {
      font-style: italic;
      color: #6b6b6b;
      font-size: 18px;
      line-height: 1.4;
      margin: 0 0 16px;
    }
    .byline {
      display: flex;
      align-items: center;
      gap: 10px;
      padding-bottom: 16px;
      border-bottom: 1px solid #ececec;
      margin-bottom: 18px;
    }
    .avatar {
      width: 32px;
      height: 32px;
      border-radius: 50%;
      background: #d8cdb6;
      flex: 0 0 auto;
    }
    .by-name {
      font-family: Arial, Helvetica, sans-serif;
      font-size: 14px;
      color: #333;
    }
    .subhead {
      font-size: 22px;
      margin: 22px 0 8px;
      color: #1a1a1a;
    }
    .subhead--sm { font-size: 18px; }
    .para {
      font-size: 17px;
      line-height: 1.6;
      margin: 0 0 14px;
      color: #2e2e2e;
    }
  `],
})
export class BlogPreviewComponent {
  readonly blocks = input.required<ProseBlock[]>();
  readonly title = input('');
  readonly subtitle = input('');
  readonly byline = input('You');
}
