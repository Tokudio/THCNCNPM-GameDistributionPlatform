import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (auth.currentUser(); as user) {
      <section class="account-card" aria-labelledby="page-title">
        <div class="profile-heading">
          <div class="avatar" aria-hidden="true">{{ user.displayName.slice(0, 1).toUpperCase() }}</div>
          <h1 id="page-title">Your profile</h1>
        </div>
        <dl class="profile-details">
          <div><dt>Display name</dt><dd>{{ user.displayName }}</dd></div>
          <div><dt>Email address</dt><dd>{{ user.email }}</dd></div>
          <div>
            <dt>Roles</dt>
            <dd>
              @for (role of user.roles; track role) {
                <span class="role">{{ role }}</span>
              } @empty {
                No roles assigned.
              }
            </dd>
          </div>
        </dl>
        @if (user.roles.includes('Admin')) {
          <a class="button button-secondary" routerLink="/admin">Administrator access</a>
        }
      </section>
    }
  `,
  styles: `
    .profile-heading { display: flex; align-items: center; gap: 1rem; margin-bottom: 1.5rem; }
    .profile-heading h1 { margin: 0; }
    .avatar {
      flex: 0 0 auto;
      display: grid;
      place-items: center;
      width: 3.5rem;
      height: 3.5rem;
      border: 1px solid #ad90ff6b;
      border-radius: 1rem;
      background: #382a57;
      color: var(--accent-text);
      font-size: 1.5rem;
      font-weight: 700;
    }
    .profile-details { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 1.5rem; margin: 0 0 1.5rem; }
    .profile-details div { border-top: 1px solid var(--border); padding-block: 1rem; }
    .profile-details div:last-child { grid-column: 1 / -1; }
    dt { color: var(--muted); font-size: .875rem; margin-bottom: .25rem; }
    dd { margin: 0; overflow-wrap: anywhere; }
    .role {
      display: inline-block;
      margin: .125rem .35rem .125rem 0;
      padding: .2rem .65rem;
      border: 1px solid #ad90ff50;
      border-radius: 999px;
      background: #322649;
      color: var(--accent-text);
      font-size: .875rem;
    }
    @media (max-width: 35rem) { .profile-details { grid-template-columns: 1fr; } }
  `,
})
export class ProfilePage {
  protected readonly auth = inject(AuthService);
}
