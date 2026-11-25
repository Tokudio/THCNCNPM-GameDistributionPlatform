import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { accountInitial } from '../admin-presentation';

@Component({
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './admin-layout.html',
  styleUrl: './admin-layout.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminLayout {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly canViewAdmin = computed(() => this.auth.currentUser()?.roles.includes('Admin') === true);
  protected readonly open = signal(false);
  protected readonly initial = accountInitial;
  protected readonly links = [
    { path: '/admin', label: 'Tổng quan', icon: '▦' },
    { path: '/admin/games', label: 'Quản lý game', icon: '▤' },
    { path: '/admin/categories', label: 'Danh mục', icon: '◇' },
    { path: '/admin/orders', label: 'Đơn hàng', icon: '☰' },
    { path: '/admin/users', label: 'Người dùng', icon: '◎' },
  ];

  protected closeMenu(button: HTMLButtonElement): void {
    if (!this.open()) return;
    this.open.set(false);
    button.focus();
  }

  protected logout(): void {
    this.auth.logout();
    void this.router.navigate(['/login'], { replaceUrl: true });
  }
}
