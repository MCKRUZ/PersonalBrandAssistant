import { Component, computed, inject, OnInit, output, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { IdeaService } from '../../../../core/services/idea.service';
import { IdeaStore } from '../../store/idea.store';
import { IdeaConnection } from '../../../../models/idea.model';

@Component({
  selector: 'app-smart-suggestions',
  standalone: true,
  imports: [ButtonModule],
  template: `
    @if (connections().length > 0) {
      <div class="suggestions-panel" [class.collapsed]="isCollapsed()" data-testid="suggestions-panel">
        <header class="suggestions-header">
          <h3>Smart Suggestions</h3>
          <button class="collapse-btn" (click)="toggleCollapse()" data-testid="collapse-btn">
            <i [class]="isCollapsed() ? 'pi pi-chevron-left' : 'pi pi-chevron-right'"></i>
          </button>
        </header>

        @if (!isCollapsed()) {
          @if (isLoading()) {
            <div class="loading-text">Analyzing connections...</div>
          } @else {
            @for (group of sortedConnections(); track group.theme) {
              <div class="suggestion-group" data-testid="suggestion-group">
                <div class="theme-label">{{ group.theme }}</div>

                <div class="confidence-bar">
                  <div class="confidence-fill" [style.width.%]="group.confidence * 100"></div>
                </div>

                <ul class="related-ideas">
                  @for (ideaId of group.relatedIdeaIds; track ideaId) {
                    <li class="idea-link">{{ getIdeaTitle(ideaId) }}</li>
                  }
                </ul>

                <p class="suggested-angle">{{ group.suggestedAngle }}</p>

                <p-button label="Draft It" icon="pi pi-pencil" size="small"
                  (onClick)="onDraftClick(group)" data-testid="draft-btn" />
              </div>
            }
          }
        }
      </div>
    }
  `,
  styles: [
    `
      .suggestions-panel {
        height: 100%;
        overflow-y: auto;
      }
      .suggestions-panel.collapsed {
        width: 40px;
        padding: 8px;
      }
      .suggestions-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 12px;
      }
      .suggestions-header h3 {
        font-size: 14px;
        font-weight: 600;
        color: #f0f6fc;
        margin: 0;
      }
      .collapse-btn {
        background: none;
        border: none;
        color: #8b949e;
        cursor: pointer;
        padding: 4px;
        font-size: 14px;
      }
      .collapse-btn:hover {
        color: #f0f6fc;
      }
      .suggestion-group {
        padding: 12px;
        margin-bottom: 12px;
        background: #161b22;
        border-radius: 8px;
        border: 1px solid #30363d;
      }
      .theme-label {
        font-weight: 600;
        font-size: 13px;
        margin-bottom: 8px;
        color: #e6edf3;
      }
      .confidence-bar {
        height: 4px;
        background: #21262d;
        border-radius: 2px;
        margin-bottom: 8px;
      }
      .confidence-fill {
        height: 100%;
        background: #238636;
        border-radius: 2px;
      }
      .related-ideas {
        list-style: none;
        padding: 0;
        margin: 0 0 8px;
      }
      .idea-link {
        color: #58a6ff;
        font-size: 12px;
        padding: 2px 0;
      }
      .suggested-angle {
        font-size: 12px;
        color: #8b949e;
        font-style: italic;
        margin: 0 0 8px;
      }
      .loading-text {
        font-size: 13px;
        color: #8b949e;
        text-align: center;
        padding: 16px 0;
      }
    `,
  ],
})
export class SmartSuggestionsComponent implements OnInit {
  private readonly ideaService = inject(IdeaService);
  private readonly store = inject(IdeaStore);

  readonly createContent = output<string>();

  readonly connections = signal<IdeaConnection[]>([]);
  readonly isCollapsed = signal(false);
  readonly isLoading = signal(false);

  readonly sortedConnections = computed(() =>
    [...this.connections()].sort((a, b) => b.confidence - a.confidence)
  );

  ngOnInit(): void {
    this.loadConnections();
  }

  loadConnections(): void {
    this.isLoading.set(true);
    this.ideaService.getConnections().subscribe({
      next: (connections) => {
        this.connections.set(connections);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      },
    });
  }

  toggleCollapse(): void {
    this.isCollapsed.update((v) => !v);
  }

  getIdeaTitle(ideaId: string): string {
    const idea = this.store.ideas().find((i) => i.id === ideaId);
    return idea?.title ?? 'Unknown idea';
  }

  onDraftClick(group: IdeaConnection): void {
    const primaryIdeaId = group.relatedIdeaIds[0];
    if (primaryIdeaId) {
      this.createContent.emit(primaryIdeaId);
    }
  }
}
