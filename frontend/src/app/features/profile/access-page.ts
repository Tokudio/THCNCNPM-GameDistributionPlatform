import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';

@Component({
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="account-card" aria-labelledby="page-title">
      @if (granted) {
        <h1 id="page-title">Administrator access</h1>
        <p>Your account has the Admin role.</p>
      } @else if (query()?.get('reason') === 'unavailable') {
        <h1 id="page-title">Couldn’t verify access</h1>
        <p>Try again once your connection is available.</p>
      } @else {
        <h1 id="page-title">Access denied</h1>
        <p>The Admin role is required to view this page.</p>
      }
      <a class="button button-secondary" routerLink="/profile">Back to profile</a>
    </section>
  `,
})
export class AccessPage {
  private readonly route = inject(ActivatedRoute);
  protected readonly granted = this.route.snapshot.data['granted'] === true;
  protected readonly query = toSignal(this.route.queryParamMap);
}
