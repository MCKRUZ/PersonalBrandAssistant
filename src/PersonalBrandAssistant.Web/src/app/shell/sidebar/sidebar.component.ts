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
      width: 220px;
      min-width: 220px;
      background: #161b22;
      border-right: 1px solid #30363d;
      padding: 16px 0;
    }
    .brand {
      padding: 8px 20px 24px;
      font-size: 18px;
      font-weight: 700;
      color: #f0f6fc;
      letter-spacing: -0.5px;
    }
    .brand span {
      color: #58a6ff;
    }
    nav {
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: 0 8px;
    }
    a {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 12px;
      border-radius: 6px;
      color: #8b949e;
      text-decoration: none;
      font-size: 14px;
      transition: all 0.15s ease;
    }
    a:hover {
      color: #e1e4e8;
      background: #1c2128;
    }
    a.active {
      color: #f0f6fc;
      background: #1f6feb22;
    }
    .icon {
      font-size: 18px;
      width: 20px;
      text-align: center;
    }
    .label { white-space: nowrap; }

    @media (max-width: 768px) {
      :host {
        position: fixed;
        bottom: 0;
        left: 0;
        right: 0;
        width: 100%;
        min-width: 0;
        flex-direction: row;
        background: #161b22;
        border-right: none;
        border-top: 1px solid #30363d;
        padding: 0;
        z-index: 1000;
      }
      .brand { display: none; }
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
      a.active { background: rgba(31,111,235,0.12); }
      .icon { font-size: 18px; }
    }
  `]
})
export class SidebarComponent {
  navItems: NavItem[] = [
    { label: 'Feed', route: '/feed', icon: '⌂' },
    { label: 'Discover', route: '/discover', icon: '◎' },
    { label: 'Ideas', route: '/ideas', icon: '◈' },
    { label: 'Create', route: '/content', icon: '✎' },
    { label: 'Calendar', route: '/calendar', icon: '▦' },
    { label: 'Analytics', route: '/analytics', icon: '◧' },
    { label: 'Listening', route: '/listening', icon: '◉' },
    { label: 'Settings', route: '/settings', icon: '⚙' },
  ];
}
