import { Component, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

interface NavItem {
  label: string;
  icon: string;
  route: string;
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {
  collapsed = input(false);
  toggleCollapse = output<void>();

  readonly navGroups: NavGroup[] = [
    {
      label: 'Create',
      items: [
        { label: 'Content', icon: 'pi pi-file', route: '/content' },
        { label: 'Blog', icon: 'pi pi-book', route: '/blog' },
      ],
    },
    {
      label: 'Distribute',
      items: [
        { label: 'Calendar', icon: 'pi pi-calendar', route: '/calendar' },
        { label: 'Approval Queue', icon: 'pi pi-check-square', route: '/approval-queue' },
        { label: 'Social', icon: 'pi pi-users', route: '/social' },
        { label: 'Platforms', icon: 'pi pi-share-alt', route: '/platforms' },
      ],
    },
    {
      label: 'Analyze',
      items: [
        { label: 'Dashboard', icon: 'pi pi-chart-bar', route: '/dashboard' },
        { label: 'Analytics', icon: 'pi pi-chart-line', route: '/analytics' },
        { label: 'News', icon: 'pi pi-globe', route: '/news' },
      ],
    },
    {
      label: 'System',
      items: [
        { label: 'Automation', icon: 'pi pi-bolt', route: '/automation' },
        { label: 'Settings', icon: 'pi pi-cog', route: '/settings' },
      ],
    },
  ];
}
