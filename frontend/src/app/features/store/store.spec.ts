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
import { Category, GameDetails, GameSort, GameStatus, MediaType, Page } from '../../core/models/game-store.models';
import { GameStoreService } from '../../core/services/game-store.service';
import { StoreHeader } from './components/store-header/store-header';
import { gamePrice } from './store-presentation';

const games = '/api/v1/games';
const categories = '/api/v1/categories';
const category: Category = {
  id: '144d1387-dc08-4c39-99c2-a2665a43b2b8', name: 'Puzzle', slug: 'puzzle',
  description: null, isActive: true, isDefault: false,
};
const game: GameDetails = {
  id: 'c3c8526c-aa76-43e7-8f73-f57aab31e579', title: 'Signal Tower', slug: 'signal-tower',
  price: 459000, currency: 'VND', discountPercent: 25, status: GameStatus.Published,
  coverImageUrl: 'https://assets.example.test/cover.webp', bannerImageUrl: 'https://assets.example.test/banner.webp',
  trailerUrl: 'https://assets.example.test/trailer', description: 'Explore a quiet moon.', createdAt: '2026-11-20T00:00:00Z',
  updatedAt: null, releaseDate: '2026-11-21T00:00:00', developer: 'Signal Studio', publisher: 'New Worlds',
  minimumRequirements: '8 GB RAM', recommendedRequirements: null, categories: [category], media: [
    { id: 'second', mediaUrl: 'https://assets.example.test/second.webp', type: MediaType.Image, displayOrder: 5 },
    { id: 'first', mediaUrl: 'https://assets.example.test/first.webp', type: MediaType.Image, displayOrder: 1 },
    { id: 'video', mediaUrl: 'https://assets.example.test/video', type: MediaType.Video, displayOrder: 0 },
    { id: 'unsafe', mediaUrl: 'javascript:alert(1)', type: MediaType.Video, displayOrder: 1 },
  ],
};
const page = (items: GameDetails[] = [], number = 1, total = items.length): Page<GameDetails> => ({
  items, pageNumber: number, pageSize: 6, totalCount: total, totalPages: Math.ceil(total / 6),
});

describe('Public Store', () => {
  let http: HttpTestingController;
  let router: Router;
  let harness: RouterTestingHarness;

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

  const view = () => harness.routeNativeElement!;
  const list = () => http.expectOne(req => req.method === 'GET' && req.url === games);
  const detail = (item = game) => http.expectOne(`${games}/${item.id}`);
  const render = () => harness.detectChanges();
  const open = async (url: string) => { harness = await RouterTestingHarness.create(url); };
  const button = (selector: string) => view().querySelector<HTMLButtonElement>(selector)!;

  function enter(selector: string, value: string): void {
    const input = view().querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    render();
  }

  async function signIn(roles: string[]): Promise<void> {
    const auth = TestBed.inject(AuthService);
    const completion = firstValueFrom(auth.login({ email: 'store@example.test', password: 'Fixture!123' }));
    http.expectOne(AUTH_API.login).flush({
      accessToken: 'store.fixture.signature', tokenType: 'Bearer', expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });
    http.expectOne(AUTH_API.me).flush({ id: 'test-user', email: 'store@example.test', displayName: 'Tester', roles });
    await completion;
  }

  it('uses the public contract, caps page size and preserves decimal(18,2) exactly without attaching a JWT', async () => {
    await signIn(['Admin']);
    const response = firstValueFrom(TestBed.inject(GameStoreService).list({
      page: 2, pageSize: 99, search: 'signal & tower', category: category.id,
      minPrice: '0.01', maxPrice: '9999999999999999.99', sort: GameSort.PriceDesc,
    }));
    const request = list();
    expect(request.request.headers.has('Authorization')).toBe(false);
    expect(Object.fromEntries(request.request.params.keys().map(key => [key, request.request.params.get(key)]))).toEqual({
      pageNumber: '2', pageSize: '50', search: 'signal & tower', categoryId: category.id,
      minPrice: '0.01', maxPrice: '9999999999999999.99', sort: '3',
    });
    const title = 'A quoted "price":123 stays intact';
    request.flush(JSON.stringify(page([{ ...game, title }, { ...game, status: GameStatus.Draft },
      { ...game, status: GameStatus.Archived }], 2, 24)).replace('"price":459000', '"price":9999999999999999.99'));
    expect((await response).items).toEqual([expect.objectContaining({ title, price: '9999999999999999.99' })]);
  });

  it('loads the guest homepage with the newest published hero, reusable cards and active category links', async () => {
    await open('/');
    expect(router.url).toBe('/store');
    expect(view().textContent).toContain('Loading featured games');
    expect(view().textContent).toContain('Loading categories');
    const fixtures = Array.from({ length: 6 }, (_, index) => ({ ...game, id: `${game.id.slice(0, -1)}${index}`,
      title: `Signal Tower ${index + 1}`, price: index === 1 ? 0 : game.price }));
    http.expectOne(categories).flush([category, { ...category, id: 'inactive', name: 'Inactive', isActive: false }]);
    const request = list();
    expect(request.request.params.get('pageSize')).toBe('9');
    request.flush(page(fixtures));
    http.expectNone(`${games}/${fixtures[4].id}`);
    for (const item of fixtures) detail(item).flush(item);
    render();
    expect(view().querySelector('.hero-link img')?.getAttribute('src')).toBe(game.bannerImageUrl);
    expect(view().querySelectorAll('.featured-grid app-game-card')).toHaveLength(5);
    expect(view().querySelector('.recent-grid h3')?.textContent).toBe('Signal Tower 6');
    expect(view().querySelector('.featured-grid .free')?.textContent).toBe('Free');
    expect(view().querySelector('.featured-grid .prices')?.textContent).toContain('344.250 ₫');
    expect(view().querySelector('#genres a')?.getAttribute('href')).toBe(`/store/games?category=${category.id}`);
    expect(view().textContent).not.toContain('Inactive');
    http.expectNone(AUTH_API.me);
  });

  it('uses cover art after a broken banner and lets the guest retry a failed homepage list', async () => {
    await open('/store');
    http.expectOne(categories).flush([]);
    list().error(new ProgressEvent('error'));
    render();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('Check your connection');
    button('#featured button').click();
    list().flush(page([game]));
    detail().flush(game);
    render();
    view().querySelector('.hero-link img')!.dispatchEvent(new Event('error'));
    render();
    expect(view().querySelector('.hero-link img')?.getAttribute('src')).toBe(game.coverImageUrl);
  });

  it('shows a stable empty homepage and decorative fallback with no published games', async () => {
    await open('/store');
    http.expectOne(categories).flush([]);
    list().flush(page());
    render();
    expect(view().textContent).toContain('No published games');
    expect(view().querySelector('.hero-media img')?.getAttribute('src')).toBe('store/artwork-wide.svg');
    expect(view().querySelector('.hero-link')).toBeNull();
  });

  it('restores catalog filters, sort and page from URL and paginates with the same query', async () => {
    await open(`/store/games?search=signal&category=${category.id}&minPrice=10.01&maxPrice=459000&sort=3&page=2`);
    expect(view().querySelector('.results')?.getAttribute('aria-busy')).toBe('true');
    const request = list();
    expect(request.request.params.get('categoryId')).toBe(category.id);
    expect(request.request.params.get('minPrice')).toBe('10.01');
    expect(request.request.params.get('maxPrice')).toBe('459000');
    expect(request.request.params.get('search')).toBe('signal');
    expect(request.request.params.get('sort')).toBe('3');
    expect(request.request.params.get('pageNumber')).toBe('2');
    http.expectOne(categories).flush([category]);
    request.flush(page([game], 2, 13));
    detail().flush(game);
    render();
    expect(view().querySelector<HTMLInputElement>('.catalog-search input')?.value).toBe('signal');
    expect(view().querySelector('.count')?.textContent).toContain('13 games');
    expect(view().querySelector('[aria-current=page]')?.textContent).toBe('2');
    button('[aria-label="Next page"]').click();
    const next = await vi.waitFor(list);
    expect(next.request.params.get('pageNumber')).toBe('3');
    expect(next.request.params.get('search')).toBe('signal');
    expect(router.url).toContain('page=3');
    next.flush(page([], 3, 13));
  });

  it('applies search, category and price inputs on page one, then applies sort and clears filters', async () => {
    await open('/store/games?page=2');
    http.expectOne(categories).flush([category]);
    list().flush(page());
    render();
    enter('.catalog-search input', '  signal  ');
    enter('#min-price', '100.01');
    enter('#max-price', '1');
    expect(view().querySelector('#range-error')?.textContent).toContain('Minimum price cannot exceed');
    button('.apply').click();
    http.expectNone(req => req.url === games);
    enter('#max-price', '459000');
    [...view().querySelectorAll('fieldset label')].find(label => label.textContent?.includes(category.name))!
      .querySelector('input')!.click();
    button('.apply').click();
    const filtered = await vi.waitFor(list);
    expect(filtered.request.params.get('search')).toBe('signal');
    expect(filtered.request.params.get('categoryId')).toBe(category.id);
    expect(filtered.request.params.get('minPrice')).toBe('100.01');
    expect(filtered.request.params.get('pageNumber')).toBe('1');
    expect(router.url).not.toContain('page=');
    filtered.flush(page());
    render();
    const sort = view().querySelector<HTMLSelectElement>('select')!;
    sort.selectedIndex = 2;
    sort.dispatchEvent(new Event('change', { bubbles: true }));
    const sorted = await vi.waitFor(list);
    expect(sorted.request.params.get('sort')).toBe('2');
    expect(sorted.request.params.get('categoryId')).toBe(category.id);
    sorted.flush(page());
    render();
    expect(view().textContent).toContain('No games found');
    button('.empty button').click();
    const reset = await vi.waitFor(list);
    expect(reset.request.params.get('categoryId')).toBeNull();
    expect(reset.request.params.get('minPrice')).toBeNull();
    expect(router.url).toBe('/store/games');
    reset.flush(page());
  });

  it('cancels stale catalog list and enrichment requests on query changes', async () => {
    await open('/store/games?search=before');
    http.expectOne(categories).flush([]);
    const oldest = list();
    await harness.navigateByUrl('/store/games?search=old');
    expect(oldest.cancelled).toBe(true);
    list().flush(page([game]));
    const pendingDetail = detail();
    await harness.navigateByUrl('/store/games?search=new');
    expect(pendingDetail.cancelled).toBe(true);
    const current = list();
    expect(current.request.params.get('search')).toBe('new');
    current.flush(page());
    render();
    expect(view().textContent).toContain('No games found');
    expect(view().textContent).not.toContain(game.title);
  });

  it('keeps search and sort usable while the price filter has an invalid draft', async () => {
    await open('/store/games?minPrice=10&page=2');
    http.expectOne(categories).flush([]);
    list().flush(page());
    render();

    enter('#min-price', 'invalid');
    const sort = view().querySelector<HTMLSelectElement>('select')!;
    sort.selectedIndex = 2;
    sort.dispatchEvent(new Event('change', { bubbles: true }));
    const sorted = await vi.waitFor(list);
    expect(sorted.request.params.get('sort')).toBe('2');
    expect(sorted.request.params.get('minPrice')).toBe('10');
    expect(sorted.request.params.get('pageNumber')).toBe('1');
    sorted.flush(page());
    render();

    enter('#max-price', '5');
    enter('.catalog-search input', '  signal  ');
    button('.catalog-search button').click();
    const searched = await vi.waitFor(list);
    expect(searched.request.params.get('search')).toBe('signal');
    expect(searched.request.params.get('minPrice')).toBe('10');
    expect(searched.request.params.has('maxPrice')).toBe(false);
    expect(searched.request.params.get('sort')).toBe('2');
    searched.flush(page());
  });

  it('skips duplicate requests for equivalent query parameters and cancels details when leaving the catalog', async () => {
    await open('/store/games');
    http.expectOne(categories).flush([]);
    list().flush(page([game]));
    const pending = detail();
    await harness.navigateByUrl('/store/games?page=1&sort=0');
    http.expectNone(req => req.url === games || req.url === categories || req.url === `${games}/${game.id}`);
    expect(pending.cancelled).toBe(false);

    await harness.navigateByUrl('/login');
    expect(pending.cancelled).toBe(true);
  });

  it('starts at most four metadata requests and starts one queued detail per completion', async () => {
    await open('/store/games');
    http.expectOne(categories).flush([]);
    const records = Array.from({ length: 6 }, (_, index) => ({ ...game, id: `${game.id.slice(0, -1)}${index}` }));
    list().flush(page(records));
    const firstBatch = http.match(req => req.url.startsWith(`${games}/`));
    expect(firstBatch.map(request => request.request.url)).toEqual(records.slice(0, 4).map(item => `${games}/${item.id}`));

    firstBatch[0].flush(records[0]);
    const fifth = detail(records[4]);
    http.expectNone(`${games}/${records[5].id}`);
    firstBatch[1].flush(records[1]);
    const sixth = detail(records[5]);
    await harness.navigateByUrl('/login');
    for (const request of [...firstBatch.slice(2), fifth, sixth]) expect(request.cancelled).toBe(true);
  });

  it('retains the catalog summary after a metadata error and hides a game that became unavailable', async () => {
    const unavailable = { ...game, id: 'a3c8526c-aa76-43e7-8f73-f57aab31e579', title: 'Unavailable game' };
    const healthy = { ...game, id: 'b3c8526c-aa76-43e7-8f73-f57aab31e579', title: 'Another published game' };
    await open('/store/games');
    http.expectOne(categories).flush([category]);
    list().flush(page([game, unavailable, healthy]));
    detail().flush('', { status: 500, statusText: 'Server error' });
    detail(unavailable).flush('', { status: 404, statusText: 'Not Found' });
    detail(healthy).flush(healthy);
    render();

    const titles = [...view().querySelectorAll('.game-grid h3')].map(heading => heading.textContent);
    expect(titles).toEqual([game.title, healthy.title]);
    expect(view().querySelector('[role=alert]')).toBeNull();
  });

  it('shows published details, ordered images, trailer, exact price and a disabled Phase 7 CTA', async () => {
    await open(`/store/games/${game.id}`);
    expect(view().textContent).toContain('Loading game details');
    detail().flush(game);
    render();
    expect(view().querySelector('h1')?.textContent).toBe(game.title);
    expect(view().textContent).toContain('21 Nov 2026');
    expect(view().textContent).toContain('8 GB RAM');
    expect(view().textContent).not.toContain('Recommended');
    expect(view().querySelector('.price')?.textContent).toContain('459.000 ₫');
    expect(view().querySelector('.price')?.textContent).toContain('344.250 ₫');
    expect(button('.buy').disabled).toBe(true);
    expect(view().querySelector('.video-links a')?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(view().querySelectorAll('.video-links a')).toHaveLength(2);
    const third = button('[aria-label="Show image 3"]');
    third.click();
    render();
    const active = () => view().querySelector('.main-art img')!;
    expect(active().getAttribute('src')).toContain('first.webp');
    expect(third.getAttribute('aria-pressed')).toBe('true');
    active().dispatchEvent(new Event('error'));
    render();
    expect(active().getAttribute('src')).toBe('store/artwork-wide.svg');
  });

  it('treats a missing or unpublished detail as not found, even for an Admin', async () => {
    await signIn(['Admin']);
    await open(`/store/games/${game.id}`);
    const request = detail();
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ ...game, status: GameStatus.Archived });
    render();
    expect(view().textContent).toContain('Game not found');
    await harness.navigateByUrl('/store/games/missing');
    http.expectOne(`${games}/missing`).flush('', { status: 404, statusText: 'Not Found' });
    render();
    expect(view().textContent).toContain('Game not found');
  });

  it('uses the game cover when its detail banner fails to load', async () => {
    await open(`/store/games/${game.id}`);
    detail().flush(game);
    render();
    view().querySelector('.main-art img')!.dispatchEvent(new Event('error'));
    render();
    expect(view().querySelector('.main-art img')?.getAttribute('src')).toBe(game.coverImageUrl);
  });

  it('shows placeholder art and no thumbnail controls for a game without media', async () => {
    await open(`/store/games/${game.id}`);
    detail().flush({ ...game, coverImageUrl: null, bannerImageUrl: null, media: [], trailerUrl: null });
    render();
    expect(view().querySelector('.main-art img')?.getAttribute('src')).toBe('store/artwork-wide.svg');
    expect(view().querySelector('.thumbnails')).toBeNull();
    expect(view().querySelector('.video-links')).toBeNull();
  });

  it.each([
    '2026-11-21T00:01:00',
    '2026-11-21T00:01:00Z',
    '2026-11-21T00:01:00+14:00',
    '2026-11-21T23:59:00-10:00',
  ])('preserves the calendar day in a release timestamp %s', async releaseDate => {
    await open(`/store/games/${game.id}`);
    detail().flush({ ...game, releaseDate });
    render();
    expect(view().querySelector('.price-card dl div:last-child dd')?.textContent).toBe('21 Nov 2026');
  });

  it('gives the compact navigation toggle an accessible name with its visible label hidden', () => {
    const fixture = TestBed.createComponent(StoreHeader);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.menu-button').getAttribute('aria-label')).toBe('Menu');
  });

  it('preserves guest, user and admin navigation and uses the catalog for header search', async () => {
    const fixture = TestBed.createComponent(StoreHeader);
    fixture.detectChanges();
    const header: HTMLElement = fixture.nativeElement;
    const link = (url: string) => header.querySelector(`a[href^="${url}"]`);
    expect(link('/login')).toBeTruthy();
    expect(link('/register')).toBeTruthy();
    expect(link('/admin')).toBeNull();
    await signIn(['User']);
    fixture.detectChanges();
    expect(link('/profile')).toBeTruthy();
    expect(link('/admin')).toBeNull();
    await signIn(['Admin']);
    fixture.detectChanges();
    expect(link('/admin')).toBeTruthy();
    const search = header.querySelector<HTMLInputElement>('input')!;
    search.value = '  signal  ';
    header.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(router.url).toBe('/store/games?search=signal'));
  });

  it('rounds discounted minor units precisely at the schema scale and marks free games', () => {
    expect(gamePrice({ price: '0.03', discountPercent: 50, currency: 'VND' }).final).toBe('0,02 ₫');
    expect(gamePrice({ price: '9999999999999999.99', discountPercent: 0, currency: 'VND' }).final)
      .toBe('9.999.999.999.999.999,99 ₫');
    expect(gamePrice({ price: '1', discountPercent: 100, currency: 'VND' })).toMatchObject({ free: true, final: 'Free' });
  });
});
