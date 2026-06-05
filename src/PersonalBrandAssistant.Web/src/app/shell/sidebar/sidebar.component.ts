import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

interface NavItem {
  label: string;
  route: string;
  icon: string;
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './sidebar.component.html',
  styles: [`
    :host {
      display: flex;
      flex-direction: column;
      width: 212px;
      min-width: 212px;
      background: var(--surface-sidebar);
      border-right: 1px solid var(--surface-border);
      padding: 18px 0 14px;
    }
    .brand {
      padding: 6px 22px 22px;
      font-family: var(--font-display);
      font-size: 24px;
      line-height: 1;
      letter-spacing: 0.3px;
      color: var(--text-primary);
    }
    .brand span {
      color: var(--brand-primary);
      font-style: italic;
    }
    nav {
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: 0 12px;
      flex: 1;
    }
    a {
      display: flex;
      align-items: center;
      gap: 13px;
      padding: 9px 12px;
      border-radius: 8px;
      color: var(--text-secondary);
      text-decoration: none;
      font-size: 14px;
      font-weight: 500;
      transition: all 0.15s ease;
    }
    a:hover {
      color: var(--text-primary);
      background: var(--surface-hover);
    }
    a.active {
      color: var(--text-primary);
      background: var(--accent-soft);
    }
    a.active .icon {
      color: var(--brand-primary);
    }
    .icon {
      font-size: 18px;
      width: 20px;
      text-align: center;
    }
    .label { white-space: nowrap; }

    .user-footer {
      display: flex;
      align-items: center;
      gap: 10px;
      margin: 12px 12px 0;
      padding: 12px;
      border-top: 1px solid var(--surface-border);
    }
    .avatar {
      width: 32px;
      height: 32px;
      flex-shrink: 0;
      border-radius: 50%;
      background: linear-gradient(135deg, var(--brand-primary), #9c5440);
      display: flex;
      align-items: center;
      justify-content: center;
      color: #fff;
      font-size: 12px;
      font-weight: 700;
    }
    .user-meta { display: flex; flex-direction: column; min-width: 0; }
    .user-name {
      font-size: 13px;
      font-weight: 600;
      color: var(--text-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .user-sub { font-size: 11px; color: var(--text-muted); }

    @media (max-width: 768px) {
      :host {
        position: fixed;
        bottom: 0;
        left: 0;
        right: 0;
        width: 100%;
        min-width: 0;
        flex-direction: row;
        background: var(--surface-sidebar);
        border-right: none;
        border-top: 1px solid var(--surface-border);
        padding: 0;
        z-index: 1000;
      }
      .brand { display: none; }
      .user-footer { display: none; }
      nav {
        flex-direction: row;
        width: 100%;
        padding: 0;
        gap: 0;
        justify-content: space-around;
      }
      a {
        flex-direction: column;
        gap: 2px;
        padding: 8px 4px 6px;
        border-radius: 0;
        font-size: 10px;
        flex: 1;
        justify-content: center;
        align-items: center;
      }
      a:hover { background: transparent; }
      a.active { background: var(--accent-soft); }
      .icon { font-size: 18px; }
    }
  `]
})
export class SidebarComponent {
  readonly userName = 'Matthew Kruczek';
  readonly userInitials = 'MK';

  navItems: NavItem[] = [
    { label: 'Feed', route: '/feed', icon: '⌂' },
    { label: 'Discover', route: '/discover', icon: '◎' },
    { label: 'Ideas', route: '/ideas', icon: '◈' },
    { label: 'Daily Brief', route: '/daily-brief', icon: '◑' },
    { label: 'Create', route: '/content', icon: '✎' },
    { label: 'Calendar', route: '/calendar', icon: '▦' },
    { label: 'Analytics', route: '/analytics', icon: '◧' },
    { label: 'Listening', route: '/listening', icon: '◉' },
    { label: 'Settings', route: '/settings', icon: '⚙' },
  ];
}
