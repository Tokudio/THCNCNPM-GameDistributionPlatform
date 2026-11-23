import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { loginUrl } from './core/auth/auth.guards';
import { AuthService } from './core/auth/auth.service';
import { StoreHeader } from './features/store/components/store-header/store-header';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet, StoreHeader],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly storePage = computed(() => {
    this.router.lastSuccessfulNavigation();
    return this.router.routerState.snapshot.root.firstChild?.data['store'] === true;
  });
  protected readonly onRegister = computed(() => {
    const url = this.router.lastSuccessfulNavigation()?.finalUrl;
    return url?.root.children['primary']?.segments[0]?.path === 'register';
  });

  constructor() {
    effect(() => {
      const navigation = this.router.lastSuccessfulNavigation();
      if (
        this.auth.sessionNotice() && navigation &&
        this.router.routerState.snapshot.root.firstChild?.data['protected']
      ) {
        void this.router.navigateByUrl(loginUrl(this.router, this.router.url), { replaceUrl: true });
      }
    });
  }

  protected logout(): void {
    this.auth.logout();
    void this.router.navigate(['/login'], { replaceUrl: true });
  }
}
