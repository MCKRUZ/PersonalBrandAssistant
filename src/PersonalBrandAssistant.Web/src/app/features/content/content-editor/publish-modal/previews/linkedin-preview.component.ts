import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ProseBlock } from '../markdown-blocks';

const LINKEDIN_TRUNCATE = 210;
const LINKEDIN_MAX = 3000;

@Component({
  selector: 'app-linkedin-preview',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="li-card">
      <div class="head">
        <span class="avatar"></span>
        <div class="who">
          <span class="name">{{ byline() }}</span>
          <span class="time">Now</span>
        </div>
      </div>
      <p class="text">
        {{ display() }}@if (truncated()) {<span class="more">…more</span>}
      </p>
      @if (overLimit()) {
        <p class="warn">Over LinkedIn's 3000-char limit</p>
      }
      <div class="bar">
        <span>👍 ❤️</span>
        <span>·</span>
        <span>Comment</span>
        <span>·</span>
        <span>Repost</span>
        <span>·</span>
        <span>Send</span>
      </div>
    </article>
  `,
  styles: [`
    .li-card {
      background: #fff;
      color: #1a1a1a;
      border-radius: 10px;
      padding: 16px 20px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
      font-family: -apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
    }
    .head {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 12px;
    }
    .avatar {
      width: 44px;
      height: 44px;
      border-radius: 50%;
      background: #cfd8e3;
      flex: 0 0 auto;
    }
    .who { display: flex; flex-direction: column; }
    .name { font-weight: 600; font-size: 15px; color: #1a1a1a; }
    .time { font-size: 12px; color: #6b6b6b; }
    .text {
      font-size: 15px;
      line-height: 1.5;
      white-space: pre-wrap;
      margin: 0 0 12px;
      color: #1a1a1a;
    }
    .more { color: #6b6b6b; font-weight: 600; margin-left: 2px; }
    .warn {
      font-size: 12px;
      color: #b54708;
      margin: 0 0 12px;
    }
    .bar {
      display: flex;
      align-items: center;
      gap: 8px;
      padding-top: 10px;
      border-top: 1px solid #ececec;
      font-size: 13px;
      color: #6b6b6b;
    }
  `],
})
export class LinkedinPreviewComponent {
  readonly blocks = input.required<ProseBlock[]>();
  readonly title = input('');
  readonly subtitle = input('');
  readonly byline = input('You');
  readonly body = input('');

  readonly truncated = computed(() => this.body().length > LINKEDIN_TRUNCATE);

  readonly display = computed(() => {
    const text = this.body();
    return text.length > LINKEDIN_TRUNCATE ? text.slice(0, LINKEDIN_TRUNCATE) : text;
  });

  readonly overLimit = computed(() => this.body().length > LINKEDIN_MAX);
}
