import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../../../core/auth/auth.service';

@Component({
  selector: 'app-store-header',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './store-header.html',
  styleUrl: './store-header.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StoreHeader {
  protected readonly auth = inject(AuthService);
  protected readonly router = inject(Router);
  protected readonly open = signal(false);
  protected readonly onCheckout = computed(() =>
    this.router.lastSuccessfulNavigation()?.finalUrl?.root.children['primary']?.segments[0]?.path === 'checkout');
  readonly signOut = output<void>();

  protected closeMenu(trigger: HTMLButtonElement): void {
    if (!this.open()) return;
    this.open.set(false);
    trigger.focus();
  }

  protected search(event: SubmitEvent, value: string): void {
    event.preventDefault();
    this.open.set(false);
    void this.router.navigate(['/store/games'], { queryParams: { search: value.trim() || null } });
  }
}
