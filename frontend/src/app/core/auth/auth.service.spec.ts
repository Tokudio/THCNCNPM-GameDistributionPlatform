import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AUTH_API, REQUIRES_AUTH } from './auth-api';
import { authInterceptor } from './auth.interceptor';
import { AuthResponse } from './auth.models';
import { AuthService } from './auth.service';

const storageKey = 'gamevault.auth';
const token = 'test.header.signature';
const login = { email: 'player@example.com', password: 'Example!123' };
const user = { id: 'test-user', email: login.email, displayName: 'Test Player', roles: ['User'] };

function credentials(expiresIn = 60_000, accessToken = token): AuthResponse {
  return { accessToken, tokenType: 'Bearer', expiresAt: new Date(Date.now() + expiresIn).toISOString() };
}

describe('AuthService and authInterceptor', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
    sessionStorage.clear();
  });

  async function authenticate(expiresIn?: number, accessToken = token): Promise<void> {
    const result = firstValueFrom(auth.login(login));
    const signIn = http.expectOne({ method: 'POST', url: AUTH_API.login });
    expect(signIn.request.headers.has('Authorization')).toBe(false);
    expect(signIn.request.body).toEqual(login);
    signIn.flush(credentials(expiresIn, accessToken));
    expect(auth.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem(storageKey)).toBeNull();

    const me = http.expectOne({ method: 'GET', url: AUTH_API.me });
    expect(me.request.headers.get('Authorization')).toBe(`Bearer ${accessToken}`);
    me.flush(user);
    expect(await result).toEqual(user);
  }

  it('persists only the verified login response and current user state', async () => {
    await authenticate();
    expect(auth.currentUser()).toEqual(user);
    expect(auth.isAuthenticated()).toBe(true);
    const saved = JSON.parse(sessionStorage.getItem(storageKey)!);
    expect(Object.keys(saved).sort()).toEqual(['accessToken', 'expiresAt', 'tokenType']);
    expect(localStorage.getItem(storageKey)).toBeNull();
  });

  it('keeps the user signed in after a refresh only after validating /me', async () => {
    sessionStorage.setItem(storageKey, JSON.stringify(credentials()));
    const initialization = auth.initialize();
    expect(auth.isAuthenticated()).toBe(false);
    const me = http.expectOne(AUTH_API.me);
    expect(me.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
    me.flush(user);
    await initialization;
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('removes a saved session if /me rejects it during initialization', async () => {
    sessionStorage.setItem(storageKey, JSON.stringify(credentials()));
    const initialization = auth.initialize();
    http.expectOne(AUTH_API.me).flush(null, { status: 401, statusText: 'Unauthorized' });
    await initialization;
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.sessionNotice()).toContain('Sign in again');
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('discards an expired saved token without making a request', async () => {
    sessionStorage.setItem(storageKey, JSON.stringify(credentials(-100)));
    await auth.initialize();
    http.expectNone(AUTH_API.me);
    expect(auth.currentUser()).toBeNull();
    expect(auth.sessionNotice()).toContain('expired');
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('clears an active session when expiresAt is reached', async () => {
    vi.useFakeTimers();
    await authenticate(1_000);
    vi.advanceTimersByTime(1_000);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentUser()).toBeNull();
    expect(auth.getAccessToken()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('sends JWT only on opt-in requests to the relative API and excludes credential endpoints', async () => {
    await authenticate();
    const client = TestBed.inject(HttpClient);
    const protectedContext = new HttpContext().set(REQUIRES_AUTH, true);

    for (const [url, context, expected] of [
      ['/api/v1/library', protectedContext, `Bearer ${token}`],
      ['/api/v1/catalog', new HttpContext(), null],
      [AUTH_API.register, protectedContext, null],
      ['/api/v1/auth/LOGIN/?test=1', protectedContext, null],
      ['https://external.example/api/v1/auth/me', protectedContext, null],
      ['//external.example/api/v1/auth/me', protectedContext, null],
      ['/api/v1/../../public', protectedContext, null],
    ] as const) {
      const response = firstValueFrom(client.get(url, { context }));
      const request = http.expectOne(url);
      expect(request.request.headers.get('Authorization')).toBe(expected);
      request.flush({});
      await response;
    }
  });

  it('clears the current token on a protected 401 without retrying', async () => {
    await authenticate();
    const result = firstValueFrom(auth.getCurrentUser());
    http.expectOne(AUTH_API.me).flush(null, { status: 401, statusText: 'Unauthorized' });
    await expect(result).rejects.toMatchObject({ status: 401 });
    expect(auth.getAccessToken()).toBeNull();
    expect(auth.currentUser()).toBeNull();
    expect(auth.sessionNotice()).toContain('no longer valid');
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    http.expectNone(AUTH_API.me);
  });

  it('preserves the current session if an anonymous Login request returns 401', async () => {
    await authenticate();
    const result = firstValueFrom(auth.login(login));
    http.expectOne(AUTH_API.login).flush(null, { status: 401, statusText: 'Unauthorized' });
    await expect(result).rejects.toMatchObject({ status: 401 });
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('preserves a new session when an earlier bearer request returns 401', async () => {
    await authenticate();
    const result = firstValueFrom(auth.getCurrentUser());
    const pending = http.expectOne(AUTH_API.me);
    expect(pending.request.headers.get('Authorization')).toBe(`Bearer ${token}`);

    const replacementToken = 'new.header.signature';
    await authenticate(undefined, replacementToken);
    pending.flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(result).rejects.toMatchObject({ status: 401 });
    expect(auth.getAccessToken()).toBe(replacementToken);
    expect(auth.currentUser()).toEqual(user);
    expect(auth.isAuthenticated()).toBe(true);
    expect(JSON.parse(sessionStorage.getItem(storageKey)!).accessToken).toBe(replacementToken);
  });

  it('never saves a token if login succeeds but /me fails', async () => {
    const result = firstValueFrom(auth.login(login));
    http.expectOne(AUTH_API.login).flush(credentials());
    http.expectOne(AUTH_API.me).flush(null, { status: 500, statusText: 'Server error' });
    await expect(result).rejects.toThrow('verify');
    expect(auth.getAccessToken()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('keeps logout effective if an earlier /me response arrives later', async () => {
    await authenticate();
    const result = firstValueFrom(auth.getCurrentUser());
    const pending = http.expectOne(AUTH_API.me);
    auth.logout();
    pending.flush(user);
    await expect(result).rejects.toThrow('verify');
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentUser()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });
});
