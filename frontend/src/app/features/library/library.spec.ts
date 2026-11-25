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
import { GameStatus, Page } from '../../core/models/game-store.models';
import { Order, OrderStatus, PaymentStatus } from '../../core/models/order.models';
import { LibraryGame, LibraryGameDetail } from '../../core/models/user-library.models';
import { StoreHeader } from '../store/components/store-header/store-header';

const base = '/api/v1/library/my';
const gameId = '144d1387-dc08-4c39-99c2-a2665a43b2b8';
const otherGameId = '63f33321-d205-44cf-a421-0efadf61f72a';
const orderId = 'c3c8526c-aa76-43e7-8f73-f57aab31e579';
const libraryGameMissing = { code: 'library_game_not_found' };
const detail: LibraryGameDetail = {
  gameId, title: 'Signal Relay', slug: 'signal-relay', isOwned: true, status: GameStatus.Published,
  coverImageUrl: 'https://assets.example.test/cover.webp', bannerImageUrl: null, categories: ['Puzzle'],
  acquiredAt: '2026-11-25T00:15:00Z', description: 'Restore a quiet radio station.',
  developer: 'Fixture Studio', publisher: null, releaseDate: '2025-04-06T00:00:00',
  minimumRequirements: '8 GB RAM', recommendedRequirements: null,
};

function page(items: LibraryGame[] = [], pageNumber = 1, totalCount = items.length): Page<LibraryGame> {
  return { items, pageNumber, pageSize: 12, totalCount, totalPages: Math.ceil(totalCount / 12) };
}

describe('Phase 8 User Library', () => {
  let http: HttpTestingController;
  let router: Router;
  let harness: RouterTestingHarness;
  const view = () => harness.routeNativeElement!;
  const render = () => harness.detectChanges();
  const list = () => http.expectOne(req => req.method === 'GET' && req.url === base);
  const button = (selector: string) => view().querySelector<HTMLButtonElement>(selector)!;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [
      provideRouter(routes), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(),
    ] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  async function signIn(id = 'fixture-owner') {
    const login = firstValueFrom(TestBed.inject(AuthService).login({ email: 'player@example.test', password: 'Fixture!123' }));
    http.expectOne(AUTH_API.login).flush({
      accessToken: 'library.fixture.signature', tokenType: 'Bearer', expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });
    http.expectOne(AUTH_API.me).flush({ id, email: 'player@example.test', displayName: 'Player One', roles: ['User'] });
    await login;
  }

  async function open(path: string, user = true) {
    if (user) await signIn();
    harness = await RouterTestingHarness.create(path);
  }

  function enterSearch(value: string) {
    const search = view().querySelector<HTMLInputElement>('input[type=search]')!;
    search.value = value;
    search.dispatchEvent(new Event('input', { bubbles: true }));
  }

  async function loadStoreGame(id = gameId) {
    const request = http.expectOne('/api/v1/games/' + id);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({
      id, title: detail.title, status: GameStatus.Published, currency: 'VND', price: '100.00',
      discountPercent: 0, categories: [], media: [], description: '', mockPaymentAvailable: true,
    });
    render();
    return vi.waitFor(() => http.expectOne(base + '/' + id));
  }

  it('redirects a guest to login with the exact protected Library return URL', async () => {
    await open('/library/' + gameId, false);
    expect(router.url).toContain('/login?');
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe('/library/' + gameId);
    http.expectNone(base + '/' + gameId);
  });

  it('requests an authenticated page and renders owned cards, acquisition dates and fallback art', async () => {
    await open('/library');
    expect(view().querySelector('[role=status]')?.textContent).toContain('Loading');
    const request = list();
    expect(request.request.headers.get('Authorization')).toBe('Bearer library.fixture.signature');
    expect(request.request.params.get('pageSize')).toBe('12');
    request.flush(page([detail]));
    render();
    const card = view().querySelector('.card')!;
    expect(card.getAttribute('href')).toBe('/library/' + gameId);
    expect(card.textContent).toContain('Owned');
    expect(card.textContent).toContain('25 Nov 2026');
    expect(card.querySelector('img')?.getAttribute('alt')).toContain(detail.title);
    card.querySelector('img')!.dispatchEvent(new Event('error'));
    render();
    expect(card.querySelector('img')?.getAttribute('src')).toBe('store/artwork-1.svg');
    http.expectNone(base + '/' + gameId);
  });

  it('distinguishes an empty library from a search with no matches', async () => {
    await open('/library');
    list().flush(page());
    render();
    expect(view().textContent).toContain('Your library is waiting');
    expect(view().querySelector('.empty a')?.getAttribute('href')).toBe('/store/games');

    enterSearch('  quiet  ');
    const request = await vi.waitFor(list);
    expect(request.request.params.get('search')).toBe('quiet');
    expect(request.request.params.get('pageNumber')).toBe('1');
    request.flush(page());
    render();
    expect(view().textContent).toContain('No games match your search');
    expect(view().textContent).not.toContain('Your library is waiting');

    button('.empty button').click();
    const cleared = await vi.waitFor(list);
    expect(cleared.request.params.get('search')).toBeNull();
    cleared.flush(page([detail]));
    render();
    expect(router.url).toBe('/library');
    expect(view().querySelector('.card')).not.toBeNull();
  });

  it('cancels stale requests, preserves search while paging and resets the page on search changes', async () => {
    await open('/library?search=relay');
    const stale = list();
    await harness.navigateByUrl('/library?search=signal&page=2');
    expect(stale.cancelled).toBe(true);
    const next = list();
    expect(next.request.params.get('search')).toBe('signal');
    expect(next.request.params.get('pageNumber')).toBe('2');
    next.flush(page([detail], 2, 30));
    render();
    button('.paging button:last-child').click();
    const third = await vi.waitFor(list);
    expect(third.request.params.get('pageNumber')).toBe('3');
    expect(third.request.params.get('search')).toBe('signal');
    third.flush(page([detail], 3, 30));
    render();

    enterSearch('s');
    enterSearch('station');
    const changed = await vi.waitFor(list);
    expect(changed.request.params.get('search')).toBe('station');
    expect(changed.request.params.get('pageNumber')).toBe('1');
    changed.flush(page());
  });

  it('shows a recoverable error and cancels the retry when navigating away', async () => {
    await open('/library');
    list().flush('', { status: 503, statusText: 'Unavailable' });
    render();
    expect(view().querySelector('[role=alert]')).not.toBeNull();
    expect(view().textContent).not.toContain('Your library is waiting');
    button('.state button').click();
    const pending = list();
    await harness.navigateByUrl('/profile');
    expect(pending.cancelled).toBe(true);
  });

  it('keeps a newly typed search when pagination is clicked before debounce completes', async () => {
    await open('/library?search=relay&page=2');
    list().flush(page([detail], 2, 36));
    render();
    enterSearch('station');
    button('.paging button:last-child').click();
    const request = await vi.waitFor(list);
    expect(request.request.params.get('search')).toBe('station');
    expect(request.request.params.get('pageNumber')).toBe('1');
    request.flush(page());
    expect(router.url).toBe('/library?search=station');
  });

  it.each([
    ['2026-11-25T00:15:00', '25 Nov 2026'],
    ['2026-11-25T00:15:00Z', '25 Nov 2026'],
    ['2026-11-25T00:15:00+07:00', '24 Nov 2026'],
  ])('renders the actual acquired instant in UTC for %s', async (timestamp, expected) => {
    await open('/library/' + gameId);
    http.expectOne(base + '/' + gameId).flush({ ...detail, acquiredAt: timestamp });
    render();
    expect(view().querySelector('time')?.textContent).toBe(expected);
    expect(view().querySelector('a[href="/store/games/' + gameId + '"]')).not.toBeNull();
  });

  it('shows an archived game to its owner without a Store or download action', async () => {
    await open('/library/' + gameId);
    http.expectOne(base + '/' + gameId).flush({ ...detail, status: GameStatus.Archived });
    render();
    expect(view().querySelector('h1')?.textContent).toBe(detail.title);
    expect(view().textContent).toContain('No longer available on the Store');
    expect(view().textContent).toContain('8 GB RAM');
    expect(view().querySelector('a[href^="/store/games"]')).toBeNull();
    expect(view().textContent).not.toContain('Download');
  });

  it('keeps a denied owned-game route private and does not fall back to public or admin APIs', async () => {
    await open('/library/' + gameId);
    http.expectOne(base + '/' + gameId).flush(libraryGameMissing, { status: 404, statusText: 'Not Found' });
    render();
    expect(view().querySelector('h1')?.textContent).toContain('Game not found in your library');
    http.expectNone(req => req.url.startsWith('/api/v1/games') || req.url.startsWith('/api/v1/admin'));
  });

  it('offers retry when the Library Detail endpoint itself is unavailable', async () => {
    await open('/library/' + gameId);
    http.expectOne(base + '/' + gameId).flush(null, { status: 404, statusText: 'Not Found' });
    render();
    expect(view().textContent).not.toContain('Game not found in your library');
    expect(view().querySelector('[role=alert]')).not.toBeNull();

    button('.state button').click();
    http.expectOne(base + '/' + gameId).flush(detail);
    render();
    expect(view().querySelector('h1')?.textContent).toBe(detail.title);
  });

  it('reloads a direct Library Detail URL after verifying the restored session', async () => {
    const token = 'restored.library.signature';
    sessionStorage.setItem('gamevault.auth', JSON.stringify({
      accessToken: token, tokenType: 'Bearer', expiresAt: new Date(Date.now() + 60_000).toISOString(),
    }));
    const initialize = TestBed.inject(AuthService).initialize();
    const me = http.expectOne(AUTH_API.me);
    expect(me.request.headers.get('Authorization')).toBe('Bearer ' + token);
    me.flush({ id: 'fixture-owner', email: 'player@example.test', displayName: 'Player One', roles: ['User'] });
    await initialize;

    await open('/library/' + gameId, false);
    const request = http.expectOne(base + '/' + gameId);
    expect(request.request.headers.get('Authorization')).toBe('Bearer ' + token);
    request.flush(detail);
    render();
    expect(view().querySelector('h1')?.textContent).toBe(detail.title);
  });

  it('uses the verified profile, exposes Library navigation and logs out through the current auth service', async () => {
    await open('/profile');
    expect(view().textContent).toContain('Player One');
    expect(view().textContent).toContain('player@example.test');
    expect(view().textContent).toContain('Account settings are read-only');
    expect(view().querySelector('a[href="/library"]')).not.toBeNull();
    button('.profile-actions button').click();
    await vi.waitFor(() => expect(router.url).toBe('/login'));
    expect(TestBed.inject(AuthService).currentUser()).toBeNull();
    expect(sessionStorage.getItem('gamevault.auth')).toBeNull();
  });

  it('shows My Library in the shared Store header only after authentication', async () => {
    TestBed.inject(Router);
    const header = TestBed.createComponent(StoreHeader);
    header.detectChanges();
    const node: HTMLElement = header.nativeElement;
    expect(node.querySelector('a[href="/library"]')).toBeNull();
    await signIn();
    header.detectChanges();
    expect(node.querySelector('a[href="/library"]')).not.toBeNull();
    TestBed.inject(AuthService).logout();
    header.detectChanges();
    expect(node.querySelector('a[href="/library"]')).toBeNull();
  });

  it('opens the confirmed owned game from the Store link using its GameId', async () => {
    await open('/store/games/' + gameId);
    const publicRequest = http.expectOne('/api/v1/games/' + gameId);
    expect(publicRequest.request.headers.has('Authorization')).toBe(false);
    publicRequest.flush({
      id: gameId, slug: detail.slug, title: detail.title, status: GameStatus.Published, currency: 'VND', price: 100,
      discountPercent: 0, categories: [], media: [], description: '', mockPaymentAvailable: true,
    });
    render();
    expect(button('.buy').disabled).toBe(true);
    const ownership = await vi.waitFor(() => http.expectOne(base + '/' + gameId));
    ownership.flush(detail);
    render();
    expect(button('.buy').textContent).toContain('Owned');
    expect(button('.buy').disabled).toBe(true);
    button('.buy').click();
    http.expectNone('/api/v1/orders');

    const link = view().querySelector<HTMLAnchorElement>('.price-card a[href^="/library/"]')!;
    expect(link.getAttribute('href')).toBe('/library/' + gameId);
    link.click();
    const request = await vi.waitFor(() => http.expectOne(base + '/' + gameId));
    expect(request.request.headers.get('Authorization')).toBe('Bearer library.fixture.signature');
    request.flush(detail);
    render();
    expect(router.url).toBe('/library/' + gameId);
    expect(view().querySelector('h1')?.textContent).toBe(detail.title);
  });

  it('requires sign-in after the ownership request rejects the current JWT without offering Buy Now', async () => {
    await open('/store/games/' + gameId);
    const ownership = await loadStoreGame();
    ownership.flush('', { status: 401, statusText: 'Unauthorized' });
    render();
    expect(TestBed.inject(AuthService).currentUser()).toBeNull();
    expect(button('.buy').disabled).toBe(true);
    expect(view().textContent).not.toContain('Buy game');
    const signIn = view().querySelector<HTMLAnchorElement>('a[href^="/login?"]');
    expect(signIn?.textContent).toContain('Sign in');
    expect(router.parseUrl(signIn!.getAttribute('href')!).queryParams['returnUrl']).toBe('/store/games/' + gameId);
    http.expectNone('/api/v1/orders');
  });

  it('does not link to the library when the ownership response identifies another game', async () => {
    await open('/store/games/' + gameId);
    const ownership = await loadStoreGame();
    ownership.flush({ ...detail, gameId: otherGameId });
    render();
    expect(button('.buy').disabled).toBe(true);
    expect(view().querySelector('.price-card a[href^="/library/"]')).toBeNull();
    http.expectNone('/api/v1/orders');
  });

  it.each([500, 0, 404])('keeps checkout blocked on an unconfirmed ownership response %s and retries', async status => {
    await open('/store/games/' + gameId);
    const ownership = await loadStoreGame();
    if (status === 0) ownership.error(new ProgressEvent('error'));
    else ownership.flush('', { status, statusText: 'Unavailable' });
    render();
    expect(button('.buy').disabled).toBe(true);
    expect(button('.buy').textContent).toContain('Ownership unavailable');
    button('.buy').click();
    http.expectNone('/api/v1/orders');
    button('.price-card button.button-secondary').click();
    http.expectOne(base + '/' + gameId).flush(libraryGameMissing, { status: 404, statusText: 'Not Found' });
    render();
    expect(button('.buy').disabled).toBe(false);
    expect(button('.buy').textContent).toContain('Buy game');
  });

  it('cancels the previous game ownership request when the Store Detail route changes', async () => {
    await open('/store/games/' + gameId);
    const previous = await loadStoreGame();
    await harness.navigateByUrl('/store/games/' + otherGameId);
    const current = await loadStoreGame(otherGameId);
    expect(previous.cancelled).toBe(true);
    current.flush({ ...detail, gameId: otherGameId });
    render();
    expect(view().querySelector('a[href="/library/' + otherGameId + '"]')).not.toBeNull();
  });

  it.each([
    ['paid', OrderStatus.Paid, PaymentStatus.Succeeded, '10.00'],
    ['free', OrderStatus.Paid, null, '0.00'],
    ['failed', OrderStatus.Cancelled, PaymentStatus.Failed, '10.00'],
  ] as const)('links to the live Library only for the verified %s checkout result', async (outcome, status, paymentStatus, totalAmount) => {
    await open('/checkout/' + orderId + '/result');
    const order: Order = {
      id: orderId, orderCode: 'ORD-test', status, paymentStatus, totalAmount, ownershipGranted: outcome !== 'failed',
      currency: 'VND', createdAt: detail.acquiredAt, paidAt: null, originalAmount: totalAmount, discountAmount: '0',
      items: [{ gameId, gameTitle: detail.title, coverImageUrl: null, categories: [], unitPrice: totalAmount }],
      mockPaymentAvailable: true, paymentMessage: null,
    };
    http.expectOne('/api/v1/orders/' + orderId).flush(order);
    render();
    const cta = view().querySelector<HTMLAnchorElement>('a[href="/library"]');
    if (outcome === 'failed') {
      expect(cta).toBeNull();
      expect(view().querySelector('h1')?.textContent).toBe('Payment failed');
      return;
    }
    cta!.click();
    const fresh = await vi.waitFor(list);
    fresh.flush(page([detail]));
    render();
    expect(view().querySelector('.card')?.textContent).toContain(detail.title);
  });
});
