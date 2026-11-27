import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { loginUrl } from './core/auth/auth.guards';
import { AuthService } from './core/auth/auth.service';
import { StoreHeader } from './features/store/components/store-header/store-header';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet, StoreHeader],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  host: {
    '(window:pagehide)': 'hideCachedView($event)',
    '(window:pageshow)': 'restoreCachedView($event)',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly document = inject(DOCUMENT);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly router = inject(Router);
  protected readonly storePage = computed(() => {
    this.router.lastSuccessfulNavigation();
    return this.router.routerState.snapshot.root.firstChild?.data['store'] === true;
  });
  protected readonly adminPage = computed(() => {
    this.router.lastSuccessfulNavigation();
    return this.router.routerState.snapshot.root.firstChild?.data['admin'] === true;
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

  protected focusMain(content: HTMLElement): void {
    (content.querySelector<HTMLElement>('#admin-content') ?? content).focus();
  }

  protected hideCachedView(event: PageTransitionEvent): void {
    const protectedRoute = this.router.routerState.snapshot.root.firstChild?.data['protected'] === true;
    // Ẩn trước khi browser giữ lại DOM của phiên này trong back-forward cache.
    if (event.persisted && (protectedRoute || this.auth.isAuthenticated())) this.host.hidden = true;
  }

  protected restoreCachedView(event: PageTransitionEvent): void {
    // Tải lại ứng dụng để xác thực phiên hiện tại trước khi hiển thị nội dung riêng tư.
    if (event.persisted && this.host.hidden) this.document.defaultView?.location.reload();
  }

  protected storeLogout(): void {
    if (this.router.routerState.snapshot.root.firstChild?.data['protected']) this.logout();
    else this.auth.logout();
  }
}
