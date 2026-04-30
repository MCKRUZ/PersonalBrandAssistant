import { Component, input, output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { NotificationBellComponent } from '../../shared/components/notification-bell/notification-bell.component';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [ButtonModule, NotificationBellComponent],
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
})
export class TopbarComponent {
  pageTitle = input('Dashboard');
  sidebarCollapsed = input(false);
  toggleSidebar = output<void>();
  toggleSidecar = output<void>();
}
