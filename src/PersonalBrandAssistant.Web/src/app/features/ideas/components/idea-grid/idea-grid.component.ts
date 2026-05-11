import { Component, input, output } from '@angular/core';
import { IdeaCardComponent } from '../idea-card/idea-card.component';
import { Idea } from '../../../../models/idea.model';

@Component({
  selector: 'app-idea-grid',
  standalone: true,
  imports: [IdeaCardComponent],
  template: `
    <div class="idea-grid" data-testid="idea-grid">
      @for (idea of ideas(); track idea.id) {
        <app-idea-card
          [idea]="idea"
          (save)="save.emit($event)"
          (dismiss)="dismiss.emit($event)"
          (createContent)="createContent.emit($event)" />
      } @empty {
        <div class="empty-state">
          <i class="pi pi-lightbulb"></i>
          <p>No ideas found</p>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .idea-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
        gap: 16px;
      }
      .empty-state {
        grid-column: 1 / -1;
        text-align: center;
        padding: 48px 16px;
        color: #8b949e;
      }
      .empty-state i {
        font-size: 32px;
        margin-bottom: 8px;
      }
    `,
  ],
})
export class IdeaGridComponent {
  readonly ideas = input.required<Idea[]>();
  readonly save = output<string>();
  readonly dismiss = output<string>();
  readonly createContent = output<string>();
}
