import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { firstValueFrom } from 'rxjs';
import { routes } from '../../app.routes';
import { AUTH_API } from '../../core/auth/auth-api';
import { authInterceptor } from '../../core/auth/auth.interceptor';
import { AuthService } from '../../core/auth/auth.service';

const login = { email: 'new-player@example.com', password: 'StrongPass9!' };
const registration = { ...login, confirmPassword: login.password, displayName: 'New Player' };
const user = { id: 'test-account', email: login.email, displayName: registration.displayName, roles: ['User'] };
const credentials = () => ({
  accessToken: 'test.header.signature',
  tokenType: 'Bearer',
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
});

describe('Authentication routes and forms', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  const page = () => harness.routeNativeElement!;
  const submitButton = () => page().querySelector<HTMLButtonElement>('button[type=submit]')!;

  function enter(fields: Record<string, string>): void {
    for (const [field, value] of Object.entries(fields)) {
      const input = page().querySelector<HTMLInputElement>(`input[id="${field}"]`)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      input.dispatchEvent(new Event('blur'));
    }
    harness.detectChanges();
  }

  function submit(): void {
    page().querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    harness.detectChanges();
  }

  async function authenticate(): Promise<void> {
    const result = firstValueFrom(TestBed.inject(AuthService).login(login));
    http.expectOne(AUTH_API.login).flush(credentials());
    http.expectOne(AUTH_API.me).flush(user);
    await result;
  }

  async function waitForUrl(url: string): Promise<void> {
    await vi.waitFor(() => expect(router.url).toBe(url));
    harness.detectChanges();
  }

  it('registers without authenticating and signs in through /me to the protected returnUrl', async () => {
    await harness.navigateByUrl('/register?returnUrl=%2Fprofile%3Ffrom%3Dlogin');
    enter(registration);
    expect(submitButton().disabled).toBe(false);
    submit();
    const request = http.expectOne(AUTH_API.register);
    expect(submitButton().disabled).toBe(true);
    expect(submitButton().textContent).toContain('Creating account');
    expect(page().querySelector('input#email')?.matches(':disabled')).toBe(true);
    expect(request.request.body).toEqual(registration);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(user, { status: 201, statusText: 'Created' });

    await waitForUrl('/login?returnUrl=%2Fprofile%3Ffrom%3Dlogin');
    expect(page().textContent).toContain('Your account is ready');
    http.expectNone(AUTH_API.me);
    expect(sessionStorage.length).toBe(0);

    enter(login);
    submit();
    const loginRequest = http.expectOne(AUTH_API.login);
    expect(submitButton().textContent).toContain('Signing in');
    expect(page().querySelector('input#password')?.matches(':disabled')).toBe(true);
    loginRequest.flush(credentials());
    http.expectOne(AUTH_API.me).flush(user);
    await waitForUrl('/profile?from=login');
    expect(page().textContent).toContain('New Player');
    expect(page().textContent).toContain(login.email);
    expect(page().querySelector('.role')?.textContent).toContain('User');
  });

  it('validates required fields, email, Identity password, display name and confirmation', async () => {
    await harness.navigateByUrl('/register');
    enter({ email: 'invalid', password: 'weak', confirmPassword: 'different', displayName: ' ' });
    expect(submitButton().disabled).toBe(true);
    expect(page().textContent).toContain('Enter a display name.');
    expect(page().textContent).toContain('Enter a valid email address.');
    expect(page().textContent).toContain('At least 8 characters');
    expect(page().textContent).toContain('Passwords must match.');
    enter({ displayName: 'a'.repeat(101) });
    expect(page().textContent).toContain('Use 100 characters or fewer.');
    submit();
    http.expectNone(AUTH_API.register);
  });

  it('shows safe per-field messages for ValidationProblemDetails and duplicate email', async () => {
    await harness.navigateByUrl('/register');
    enter(registration);
    submit();
    http.expectOne(AUTH_API.register).flush(
      { errors: { DisplayName: ['Private detail'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    harness.detectChanges();
    expect(page().querySelector('#displayName-message')?.textContent).toContain('Check your display name');
    expect(page().textContent).not.toContain('Private detail');
    enter({ displayName: 'Another Player' });
    submit();
    http.expectOne(AUTH_API.register).flush(null, { status: 409, statusText: 'Conflict' });
    harness.detectChanges();
    expect(page().querySelector('#email-message')?.textContent).toContain('already uses this email');
    expect(submitButton().disabled).toBe(true);
  });

  it.each(['wrong password', 'unknown account', 'inactive account'])(
    'displays the same login message for %s',
    async () => {
      await harness.navigateByUrl('/login');
      enter(login);
      submit();
      http.expectOne(AUTH_API.login).flush(
        { detail: 'Internal account information' },
        { status: 401, statusText: 'Unauthorized' },
      );
      harness.detectChanges();
      expect(page().querySelector('[role=alert]')?.textContent).toContain('email address or password is incorrect');
      expect(page().textContent).not.toContain('Internal account information');
      expect(router.url).toBe('/login');
      http.expectNone(AUTH_API.me);
    },
  );

  it('shows login validation, generic server and generic network errors', async () => {
    await harness.navigateByUrl('/login');
    enter(login);
    submit();
    http.expectOne(AUTH_API.login).flush(
      { errors: { Email: ['Validation message'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    harness.detectChanges();
    expect(page().querySelector('#email-message')?.textContent).toContain('Check your email address');

    enter({ email: 'updated@example.com' });
    submit();
    http.expectOne(AUTH_API.login).flush(
      { detail: 'Database host information' },
      { status: 500, statusText: 'Server Error' },
    );
    harness.detectChanges();
    expect(page().querySelector('[role=alert]')?.textContent).toContain('couldn’t sign you in');
    expect(page().textContent).not.toContain('Database host information');

    submit();
    http.expectOne(AUTH_API.login).error(new ProgressEvent('error'));
    harness.detectChanges();
    expect(page().querySelector('[role=alert]')?.textContent).toContain('couldn’t sign you in');
  });

  it('rejects a whitespace-only login password before sending a request', async () => {
    await harness.navigateByUrl('/login');
    enter({ email: login.email, password: '   ' });
    expect(submitButton().disabled).toBe(true);
    expect(page().querySelector('#password-message')?.textContent).toContain('Enter your password.');
    submit();
    http.expectNone(AUTH_API.login);
  });

  it('redirects a guest from a protected route and redirects a signed-in user away from Login', async () => {
    await harness.navigateByUrl('/profile');
    expect(router.url).toBe('/login?returnUrl=%2Fprofile');
    await authenticate();
    await harness.navigateByUrl('/register');
    expect(router.url).toBe('/profile');
  });

  it('blocks an expired session when navigating before the expiration timer runs', async () => {
    await authenticate();
    const now = vi.spyOn(Date, 'now').mockReturnValue(Date.now() + 90_000);
    try {
      await harness.navigateByUrl('/profile');
      expect(router.url).toBe('/login?returnUrl=%2Fprofile');
      expect(page().textContent).toContain('Your session expired');
      expect(sessionStorage.length).toBe(0);
    } finally {
      now.mockRestore();
    }
  });

  it('ignores an external returnUrl after login', async () => {
    await harness.navigateByUrl('/login?returnUrl=%2F%2Foutside.example%2F');
    enter(login);
    submit();
    http.expectOne(AUTH_API.login).flush(credentials());
    http.expectOne(AUTH_API.me).flush(user);
    await waitForUrl('/profile');
  });

  it.each([
    { roles: ['User'], destination: '/access-denied', heading: 'Access denied' },
    { roles: ['Admin'], destination: '/admin', heading: 'Administrator access' },
  ])('uses freshly fetched roles to navigate to $destination', async ({ roles, destination, heading }) => {
    await authenticate();
    const navigation = harness.navigateByUrl('/admin');
    const me = await vi.waitFor(() => http.expectOne(AUTH_API.me));
    me.flush({ ...user, roles });
    await navigation;
    expect(router.url).toBe(destination);
    expect(page().querySelector('h1')?.textContent).toBe(heading);
  });

  it('changes password visibility without changing form data', async () => {
    await harness.navigateByUrl('/login');
    enter(login);
    const input = page().querySelector<HTMLInputElement>('#password')!;
    const toggle = page().querySelector<HTMLButtonElement>('[aria-controls=password]')!;
    expect(page().querySelector<HTMLLabelElement>('label[for=password]')?.control).toBe(input);
    expect(input.type).toBe('password');
    toggle.click();
    harness.detectChanges();
    expect(input.type).toBe('text');
    expect(input.value).toBe(login.password);
    expect(toggle.getAttribute('aria-label')).toBe('Hide password');
  });
});
