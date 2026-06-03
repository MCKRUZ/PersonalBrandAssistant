import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ProseBlock } from '../markdown-blocks';

@Component({
  selector: 'app-substack-preview',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="substack-card">
      <div class="masthead">
        <span class="pub-name">Publication name</span>
        <span class="subscribe">Subscribe</span>
      </div>
      <h1 class="headline">{{ title() }}</h1>
      <p class="byline">{{ byline() }} · to 1,200 subscribers</p>
      <div class="prose">
        @for (block of blocks(); track $index) {
          @switch (block.type) {
            @case ('h2') { <h2 class="subhead">{{ block.text }}</h2> }
            @case ('h3') { <h3 class="subhead subhead--sm">{{ block.text }}</h3> }
            @default { <p class="para">{{ block.text }}</p> }
          }
        }
      </div>
      <p class="unsub">
        You're receiving this because you subscribed. Unsubscribe at any time.
      </p>
    </article>
  `,
  styles: [`
    .substack-card {
      background: #fff;
      color: #1a1a1a;
      border-radius: 10px;
      padding: 24px 28px 20px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
      font-family: -apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
    }
    .masthead {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding-bottom: 16px;
      border-bottom: 1px solid #ececec;
      margin-bottom: 20px;
    }
    .pub-name {
      font-weight: 700;
      font-size: 16px;
      color: #1a1a1a;
    }
    .subscribe {
      background: #ff6719;
      color: #fff;
      font-size: 13px;
      font-weight: 600;
      padding: 6px 14px;
      border-radius: 6px;
    }
    .headline {
      font-size: 28px;
      font-weight: 800;
      line-height: 1.2;
      margin: 0 0 8px;
      color: #1a1a1a;
    }
    .byline {
      font-size: 14px;
      color: #6b6b6b;
      margin: 0 0 20px;
    }
    .subhead {
      font-size: 22px;
      font-weight: 700;
      margin: 22px 0 8px;
      color: #1a1a1a;
    }
    .subhead--sm { font-size: 18px; }
    .para {
      font-size: 17px;
      line-height: 1.6;
      margin: 0 0 15px;
      color: #2e2e2e;
    }
    .unsub {
      margin: 22px 0 0;
      padding-top: 16px;
      border-top: 1px solid #ececec;
      font-size: 12px;
      color: #999;
    }
  `],
})
export class SubstackPreviewComponent {
  readonly blocks = input.required<ProseBlock[]>();
  readonly title = input('');
  readonly subtitle = input('');
  readonly byline = input('You');
}
