import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { SocialService } from '../services/social.service';
import {
  EngagementTask, EngagementExecution, SocialInboxItem,
  SocialPlatformType, DiscoveredOpportunity, EngageSingleRequest,
  SocialStats, SafetyStatus,
} from '../models/social.model';

interface SocialState {
  readonly tasks: readonly EngagementTask[];
  readonly selectedTaskHistory: readonly EngagementExecution[];
  readonly inboxItems: readonly SocialInboxItem[];
  readonly selectedInboxItem: SocialInboxItem | undefined;
  readonly opportunities: readonly DiscoveredOpportunity[];
  readonly savedOpportunities: readonly DiscoveredOpportunity[];
  readonly stats: SocialStats | undefined;
  readonly safetyStatus: SafetyStatus | undefined;
  readonly loading: boolean;
  readonly executing: boolean;
  readonly discovering: boolean;
  readonly engaging: boolean;
  readonly hasDiscovered: boolean;
  readonly activeTab: 'automation' | 'opportunities' | 'inbox';
  readonly inboxFilter: { platform?: SocialPlatformType; isRead?: boolean };
}

const initialState: SocialState = {
  tasks: [],
  selectedTaskHistory: [],
  inboxItems: [],
  selectedInboxItem: undefined,
  opportunities: [],
  savedOpportunities: [],
  stats: undefined,
  safetyStatus: undefined,
  loading: false,
  executing: false,
  discovering: false,
  engaging: false,
  hasDiscovered: false,
  activeTab: 'automation',
  inboxFilter: {},
};

export const SocialStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed(store => ({
    hasTasks: computed(() => store.tasks().length > 0),
    enabledTasks: computed(() => store.tasks().filter(t => t.isEnabled)),
    unreadCount: computed(() => store.inboxItems().filter(i => !i.isRead).length),
    hasInboxItems: computed(() => store.inboxItems().length > 0),
    hasOpportunities: computed(() => store.opportunities().length > 0),
    opportunitiesByPlatform: computed(() => {
      const grouped = new Map<string, DiscoveredOpportunity[]>();
      for (const opp of store.opportunities()) {
        const list = grouped.get(opp.platform) ?? [];
        list.push(opp);
        grouped.set(opp.platform, list);
      }
      return grouped;
    }),
    opportunitySummary: computed(() => ({
      count: store.opportunities().length,
      platformCount: new Set(store.opportunities().map(o => o.platform)).size,
    })),
  })),
  withMethods((store, service = inject(SocialService)) => ({
    loadStats: rxMethod<void>(
      pipe(
        switchMap(() =>
          service.getStats().pipe(
            tapResponse({
              next: stats => patchState(store, { stats }),
              error: () => {},
            }),
          ),
        ),
      ),
    ),

    loadSafetyStatus: rxMethod<void>(
      pipe(
        switchMap(() =>
          service.getSafetyStatus().pipe(
            tapResponse({
              next: safetyStatus => patchState(store, { safetyStatus }),
              error: () => {},
            }),
          ),
        ),
      ),
    ),

    loadTasks: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          service.getTasks().pipe(
            tapResponse({
              next: tasks => patchState(store, { tasks, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    loadHistory: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { loading: true, selectedTaskHistory: [] })),
        switchMap(taskId =>
          service.getHistory(taskId).pipe(
            tapResponse({
              next: history => patchState(store, { selectedTaskHistory: history, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    executeTask: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { executing: true })),
        switchMap(taskId =>
          service.executeTask(taskId).pipe(
            tapResponse({
              next: () => patchState(store, { executing: false }),
              error: () => patchState(store, { executing: false }),
            }),
          ),
        ),
      ),
    ),

    loadInbox: rxMethod<{ platform?: SocialPlatformType; isRead?: boolean }>(
      pipe(
        tap(filter => patchState(store, { loading: true, inboxFilter: filter })),
        switchMap(filter =>
          service.getInboxItems(filter).pipe(
            tapResponse({
              next: items => patchState(store, { inboxItems: items, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    discoverOpportunities: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { discovering: true })),
        switchMap(() =>
          service.discoverOpportunities().pipe(
            tapResponse({
              next: opportunities => patchState(store, { opportunities, discovering: false, hasDiscovered: true }),
              error: () => patchState(store, { discovering: false, hasDiscovered: true }),
            }),
          ),
        ),
      ),
    ),

    engageSingle: rxMethod<EngageSingleRequest>(
      pipe(
        tap(() => patchState(store, { engaging: true })),
        switchMap(request =>
          service.engageSingle(request).pipe(
            tapResponse({
              next: () => {
                // Remove engaged opportunity from list
                const updated = store.opportunities().filter(o => o.postUrl !== request.postUrl);
                patchState(store, { opportunities: updated, engaging: false });
              },
              error: () => patchState(store, { engaging: false }),
            }),
          ),
        ),
      ),
    ),

    dismissOpportunity(opp: DiscoveredOpportunity) {
      service.dismissOpportunity(opp.postUrl, opp.platform).subscribe();
      const updated = store.opportunities().filter(o => o.postUrl !== opp.postUrl);
      patchState(store, { opportunities: updated });
    },

    saveOpportunity(opp: DiscoveredOpportunity) {
      service.saveOpportunity(opp.postUrl, opp.platform).subscribe();
      const updated = store.opportunities().filter(o => o.postUrl !== opp.postUrl);
      patchState(store, { opportunities: updated, savedOpportunities: [...store.savedOpportunities(), opp] });
    },

    loadSaved: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          service.getSavedOpportunities().pipe(
            tapResponse({
              next: saved => patchState(store, { savedOpportunities: saved, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    setActiveTab(tab: 'automation' | 'opportunities' | 'inbox') {
      patchState(store, { activeTab: tab });
    },

    selectInboxItem(item: SocialInboxItem | undefined) {
      patchState(store, { selectedInboxItem: item });
    },

    clearHistory() {
      patchState(store, { selectedTaskHistory: [] });
    },
  })),
);
