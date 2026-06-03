import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ProseBlock } from '../markdown-blocks';
import { splitThread } from '../thread-splitter';

@Component({
  selector: 'app-twitter-preview',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="thread">
      @for (tweet of tweets(); track $index) {
        <div class="tweet">
          <div class="gutter">
            <span class="avatar"></span>
            @if (!$last) { <span class="rail"></span> }
          </div>
          <div class="bubble">
            <div class="who">
              <span class="name">{{ byline() }}</span>
              <span class="handle">&#64;handle</span>
            </div>
            <p class="text">{{ tweet }}</p>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .thread {
      background: #fff;
      color: #0f1419;
      border-radius: 10px;
      padding: 16px 20px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
      font-family: -apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
    }
    .tweet {
      display: flex;
      gap: 12px;
    }
    .gutter {
      display: flex;
      flex-direction: column;
      align-items: center;
      flex: 0 0 auto;
    }
    .avatar {
      width: 40px;
      height: 40px;
      border-radius: 50%;
      background: #cfd9de;
      flex: 0 0 auto;
    }
    .rail {
      flex: 1 1 auto;
      width: 2px;
      background: #cfd9de;
      margin: 4px 0;
    }
    .bubble { padding-bottom: 16px; }
    .who {
      display: flex;
      gap: 6px;
      align-items: baseline;
      margin-bottom: 2px;
    }
    .name { font-weight: 700; font-size: 15px; color: #0f1419; }
    .handle { font-size: 14px; color: #536471; }
    .text {
      font-size: 15px;
      line-height: 1.45;
      white-space: pre-wrap;
      margin: 0;
      color: #0f1419;
    }
  `],
})
export class TwitterPreviewComponent {
  readonly blocks = input.required<ProseBlock[]>();
  readonly title = input('');
  readonly subtitle = input('');
  readonly byline = input('You');
  readonly body = input('');

  readonly tweets = computed(() => splitThread(this.body(), 280));
}
