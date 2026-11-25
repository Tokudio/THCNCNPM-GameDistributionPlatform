import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { firstValueFrom } from 'rxjs';
import { routes } from '../../app.routes';
import { AUTH_API } from '../../core/auth/auth-api';
import { authInterceptor } from '../../core/auth/auth.interceptor';
import { AuthService } from '../../core/auth/auth.service';
import { Category, GameDetails, GameStatus, GameSummary, Page } from './catalog.models';

const gamesUrl = '/api/v1/admin/games';
const categoryUrl = '/api/v1/categories';
const adminCategoryUrl = '/api/v1/admin/categories';
const id = 'c3c8526c-aa76-43e7-8f73-f57aab31e579';
const category: Category = {
  id: '144d1387-dc08-4c39-99c2-a2665a43b2b8', name: 'Puzzle', slug: 'puzzle', description: null,
  isActive: true, isDefault: false,
};
const record: GameDetails = {
  id, title: 'Signal Tower', slug: 'signal-tower', description: 'A puzzle game.', price: 14.5, currency: 'VND',
  discountPercent: 0, status: GameStatus.Draft, coverImageUrl: null, bannerImageUrl: null, trailerUrl: null,
  publisher: null, developer: null, releaseDate: null, minimumRequirements: null, recommendedRequirements: null,
  createdAt: '2026-11-21T00:00:00Z', updatedAt: null, categories: [category], media: [],
};
const gamePage = (items: GameSummary[] = [record], pageNumber = 1, totalPages = 1): Page<GameSummary> => ({
  items, pageNumber, pageSize: 12, totalPages, totalCount: totalPages === 1 ? items.length : 15,
});

describe('Admin catalog routes and forms', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [
      provideRouter(routes), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(),
    ] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  async function signIn(roles = ['Admin']): Promise<void> {
    const login = firstValueFrom(TestBed.inject(AuthService).login({ email: 'test@example.com', password: 'Fixture!123' }));
    http.expectOne(AUTH_API.login).flush({
      accessToken: 'catalog.fake.signature', tokenType: 'Bearer',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });
    refreshRoles(roles, http.expectOne(AUTH_API.me));
    await login;
  }

  function refreshRoles(roles: string[], request: TestRequest): void {
    request.flush({ id: 'fixture-user', displayName: 'Fixture', email: 'test@example.com', roles });
  }

  async function open(path: string, roles = ['Admin']): Promise<void> {
    const navigation = harness.navigateByUrl(path);
    const request = await vi.waitFor(() => http.expectOne(AUTH_API.me));
    refreshRoles(roles, request);
    await navigation;
  }

  const view = () => harness.routeNativeElement!;
  const gameListRequest = () => http.expectOne(req => req.method === 'GET' && req.url === gamesUrl);

  function enter(selector: string, value: string): void {
    const input = view().querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('blur'));
    harness.detectChanges();
  }

  function submit(): void {
    view().querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    harness.detectChanges();
  }

  function click(label: string): void {
    const button = [...view().querySelectorAll<HTMLButtonElement>('button')]
      .find(item => item.getAttribute('aria-label') === label || item.textContent?.trim() === label);
    expect(button).toBeDefined();
    button!.click();
    harness.detectChanges();
  }

  function enterDraftDetails(price = '14.50'): void {
    enter('#game-title', record.title);
    enter('#game-slug', record.slug);
    enter('#game-description', record.description);
    enter('#game-price', price);
  }

  const createDraftButton = () => view().querySelector<HTMLButtonElement>('button[type=submit]')!;

  it('shows game list loading, attaches the real interceptor context and renders the result', async () => {
    await signIn();
    await open('/admin/games');
    const list = gameListRequest();
    http.expectOne(adminCategoryUrl).flush([category]);
    expect(view().textContent).toContain('Loading games');
    expect(list.request.headers.get('Authorization')).toBe('Bearer catalog.fake.signature');
    list.flush(gamePage());
    harness.detectChanges();
    expect(view().textContent).toContain(record.title);
    expect(view().textContent).toContain('Draft');
  });

  it('applies filters, requests the next page and displays the empty state', async () => {
    await signIn();
    await open('/admin/games');
    gameListRequest().flush(gamePage());
    http.expectOne(adminCategoryUrl).flush([category]);
    enter('#game-search', '   signal   ');
    submit();
    const filtered = gameListRequest();
    expect(filtered.request.params.get('search')).toBe('signal');
    expect(filtered.request.params.get('pageNumber')).toBe('1');
    filtered.flush(gamePage([record], 1, 2));
    harness.detectChanges();

    click('Next');
    const next = gameListRequest();
    expect(next.request.params.get('search')).toBe('signal');
    expect(next.request.params.get('pageNumber')).toBe('2');
    next.flush(gamePage([], 2, 2));
    harness.detectChanges();
    expect(view().textContent).toContain('No games match these filters.');
  });

  it.each([
    [401, 'Phiên đăng nhập quản trị đã kết thúc'], [403, 'The Admin role is required'],
  ])('handles API status %s on game list', async (status, message) => {
    await signIn();
    await open('/admin/games');
    const list = gameListRequest();
    http.expectOne(adminCategoryUrl).flush([category]);
    list.flush({}, { status, statusText: 'Catalog rejected request' });
    harness.detectChanges();
    expect(view().querySelector('[role=alert]')?.textContent).toContain(message);
    if (status === 401) {
      expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
      expect(view().querySelector('table')).toBeNull();
    }
  });

  it('redirects guests and rejects a user whose refreshed roles lack Admin', async () => {
    await harness.navigateByUrl('/admin/games/new');
    expect(router.url).toBe('/login?returnUrl=%2Fadmin%2Fgames%2Fnew');
    await signIn();
    await open('/admin/categories', ['User']);
    expect(router.url).toBe('/access-denied');
    http.expectNone(adminCategoryUrl);
    http.expectNone(req => req.url === gamesUrl);
  });

  it('creates a draft with checked categories and media, then opens edit', async () => {
    await signIn();
    await open('/admin/games/new');
    http.expectOne(adminCategoryUrl).flush([category]);
    harness.detectChanges();
    enter('#game-title', record.title);
    enter('#game-slug', record.slug);
    enter('#game-description', record.description);
    enter('#game-price', '14.50');
    view().querySelector<HTMLInputElement>('.category-options input')!.click();
    harness.detectChanges();
    click('Add media URL');
    enter('#media-url-0', 'https://example.com/screenshot.webp');
    expect(createDraftButton().disabled).toBe(false);
    click('Create draft');

    const create = http.expectOne({ method: 'POST', url: gamesUrl });
    expect(create.request.body).toMatchObject({
      title: record.title, price: 14.5, categoryIds: [category.id],
      media: [{ mediaUrl: 'https://example.com/screenshot.webp', type: 0, displayOrder: 0 }],
    });
    expect(create.request.body).not.toHaveProperty('status');
    create.flush(record, { status: 201, statusText: 'Created' });
    const refresh = await vi.waitFor(() => http.expectOne(AUTH_API.me));
    refreshRoles(['Admin'], refresh);
    await vi.waitFor(() => expect(router.url).toBe('/admin/games/' + id + '/edit'));
    http.expectOne(gamesUrl + '/' + id).flush(record);
    http.expectOne(adminCategoryUrl).flush([category]);
    harness.detectChanges();
    expect(view().textContent).toContain('Edit game');
  });

  it('loads and updates an existing game, then confirms and patches its status', async () => {
    await signIn();
    await open('/admin/games/' + id + '/edit');
    http.expectOne(gamesUrl + '/' + id).flush(record);
    http.expectOne(adminCategoryUrl).flush([category]);
    harness.detectChanges();
    expect(view().querySelector<HTMLInputElement>('#game-title')?.value).toBe(record.title);
    expect(view().querySelector<HTMLInputElement>('.category-options input')?.checked).toBe(true);
    enter('#game-title', 'Signal Tower II');
    submit();
    const update = http.expectOne({ method: 'PUT', url: gamesUrl + '/' + id });
    expect(update.request.body.title).toBe('Signal Tower II');
    expect(update.request.body.categoryIds).toEqual([category.id]);
    const updated = { ...record, title: 'Signal Tower II' };
    update.flush(updated);
    harness.detectChanges();
    expect(view().textContent).toContain('Game updated.');

    const target = view().querySelector<HTMLSelectElement>('#next-status')!;
    expect(target.selectedOptions[0]?.text).toBe('Draft');
    target.selectedIndex = Array.from(target.options).findIndex(option => option.text === 'Published');
    target.dispatchEvent(new Event('change', { bubbles: true }));
    harness.detectChanges();
    click('Review change');
    http.expectNone(gamesUrl + '/' + id + '/status');
    click('Confirm status change');
    const status = http.expectOne({ method: 'PATCH', url: gamesUrl + '/' + id + '/status' });
    expect(status.request.body).toEqual({ status: GameStatus.Published });
    status.flush({ ...updated, status: GameStatus.Published });
    harness.detectChanges();
    expect(view().textContent).toContain('Current status Published');
    expect(view().querySelector<HTMLSelectElement>('#next-status')?.selectedOptions[0]?.text).toBe('Published');
  });

  it('disables save when the game price can lose cents in the browser', async () => {
    await signIn();
    await open('/admin/games/new');
    http.expectOne(adminCategoryUrl).flush([]);
    harness.detectChanges();
    enter('#game-title', record.title);
    enter('#game-slug', record.slug);
    enter('#game-description', record.description);
    enter('#game-price', '70368744177664.01');

    expect(view().querySelector<HTMLButtonElement>('button[type=submit]')?.disabled).toBe(true);
    expect(view().querySelector('#price-error')?.textContent).toContain('preserve cent precision');
    submit();
    http.expectNone({ method: 'POST', url: gamesUrl });
  });

  it('explains why Create draft is disabled for missing or invalid game details', async () => {
    await signIn();
    await open('/admin/games/new');
    http.expectOne(adminCategoryUrl).flush([]);
    harness.detectChanges();

    for (const field of ['title', 'slug', 'description', 'price']) {
      expect(view().querySelector(`#${field}-error`)?.textContent).toContain('is required');
    }
    expect(createDraftButton().disabled).toBe(true);

    enter('#game-title', '   ');
    enter('#game-description', '   ');
    enter('#game-slug', 'Signal--Tower');
    enter('#game-price', '1.234');
    enter('#game-currency', 'V1');
    enter('#game-discount', '1.5');

    expect(view().querySelector('#title-error')?.textContent).toContain('is required');
    expect(view().querySelector('#description-error')?.textContent).toContain('is required');
    expect(view().querySelector('#slug-error')?.textContent).toContain('single hyphens');
    expect(view().querySelector('#price-error')?.textContent).toContain('two decimal places');
    expect(view().querySelector('#game-price')?.getAttribute('aria-describedby')).toBe('price-error');
    expect(view().querySelector('#currency-error')?.textContent).toContain('three-letter');
    expect(view().querySelector('#discount-error')?.textContent).toContain('whole number');
    submit();
    http.expectNone({ method: 'POST', url: gamesUrl });
  });

  it('shows errors on invalid URLs, empty media rows, fractional media orders and duplicate media', async () => {
    await signIn();
    await open('/admin/games/new');
    http.expectOne(adminCategoryUrl).flush([]);
    harness.detectChanges();
    enterDraftDetails();
    enter('#cover-url', 'file:///cover.png');
    enter('#banner-url', 'https://user:pass@example.com/banner.png');
    enter('#trailer-url', '/trailer');
    for (const field of ['cover', 'banner', 'trailer']) {
      expect(view().querySelector(`#${field}-error`)?.textContent).toContain('absolute HTTP or HTTPS URL');
      enter(`#${field}-url`, '');
    }

    click('Add media URL');
    expect(view().querySelector('#media-url-error-0')?.textContent).toContain('Media URL is required');
    expect(createDraftButton().disabled).toBe(true);
    enter('#media-url-0', 'https://EXAMPLE.com/screenshot.webp');
    enter('#media-order-0', '1.5');
    expect(view().querySelector('#media-order-error-0')?.textContent).toContain('whole number');
    enter('#media-order-0', '1');
    expect(createDraftButton().disabled).toBe(false);

    click('Add media URL');
    enter('#media-url-1', 'https://example.com/screenshot.webp');
    expect(view().querySelector('#media-url-error-1')?.textContent).toContain('already listed');
    expect(createDraftButton().disabled).toBe(true);
    submit();
    http.expectNone({ method: 'POST', url: gamesUrl });
    click('Remove media item 2');
    expect(createDraftButton().disabled).toBe(false);
  });

  it('explains an invalid selected category ID before sending a draft', async () => {
    await signIn();
    await open('/admin/games/new');
    http.expectOne(adminCategoryUrl).flush([{ ...category, id: '00000000-0000-0000-0000-000000000000' }]);
    harness.detectChanges();
    enterDraftDetails();
    const checkbox = view().querySelector<HTMLInputElement>('.category-options input')!;
    checkbox.click();
    harness.detectChanges();
    expect(view().querySelector('#category-error')?.textContent).toContain('valid categories');
    expect(createDraftButton().disabled).toBe(true);
    submit();
    http.expectNone({ method: 'POST', url: gamesUrl });
    checkbox.click();
    harness.detectChanges();
    expect(createDraftButton().disabled).toBe(false);
  });

  it('maps API validation to price, category selection and the affected media row', async () => {
    await signIn();
    await open('/admin/games/new');
    http.expectOne(adminCategoryUrl).flush([category]);
    harness.detectChanges();
    enterDraftDetails();
    const checkbox = view().querySelector<HTMLInputElement>('.category-options input')!;
    checkbox.click();
    click('Add media URL');
    enter('#media-url-0', 'https://example.com/screenshot.webp');
    click('Create draft');
    http.expectOne({ method: 'POST', url: gamesUrl }).flush({
      errors: {
        Price: ['Enter a valid price.'],
        '$.categoryIds[0]': ['This category ID is invalid.'],
        'Media[0].MediaUrl': ['This media URL is invalid.'],
      },
    }, { status: 400, statusText: 'Bad Request' });
    harness.detectChanges();

    expect(view().querySelector('#price-error')?.textContent).toContain('valid price');
    expect(view().querySelector('#category-error')?.textContent).toContain('category ID is invalid');
    expect(view().querySelector('#media-url-error-0')?.textContent).toContain('media URL is invalid');
    expect(createDraftButton().disabled).toBe(true);

    enter('#game-price', '15.50');
    enter('#media-url-0', 'https://example.com/updated.webp');
    checkbox.click();
    harness.detectChanges();
    expect(createDraftButton().disabled).toBe(false);

    checkbox.click();
    click('Create draft');
    http.expectOne({ method: 'POST', url: gamesUrl }).flush(
      { detail: 'One or more selected categories do not exist.' }, { status: 400, statusText: 'Bad Request' },
    );
    harness.detectChanges();
    expect(view().querySelector('#category-error')?.textContent).toContain('categories do not exist');
  });

  it('submits a valid free draft and distinguishes a duplicate slug from other conflicts', async () => {
    await signIn();
    await open('/admin/games/new');
    http.expectOne(adminCategoryUrl).flush([]);
    harness.detectChanges();
    enterDraftDetails('0');
    enter('#game-currency', 'vnd');
    expect(createDraftButton().disabled).toBe(false);
    click('Create draft');

    const request = http.expectOne({ method: 'POST', url: gamesUrl });
    expect(request.request.body).toMatchObject({
      slug: record.slug, price: 0, currency: 'VND', categoryIds: [], media: [],
      releaseDate: null, coverImageUrl: null,
    });
    request.flush({ detail: 'This game slug is already in use.' }, { status: 409, statusText: 'Conflict' });
    harness.detectChanges();
    expect(view().querySelector('#slug-error')?.textContent).toContain('slug is already in use');
    expect(createDraftButton().disabled).toBe(true);
    enter('#game-slug', 'different-slug');
    expect(createDraftButton().disabled).toBe(false);

    click('Create draft');
    http.expectOne({ method: 'POST', url: gamesUrl }).flush(
      { detail: 'The catalog changed during this operation.' }, { status: 409, statusText: 'Conflict' },
    );
    harness.detectChanges();
    expect(view().querySelector('#slug-error')).toBeNull();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('The catalog changed');

    click('Create draft');
    http.expectOne({ method: 'POST', url: gamesUrl }).flush(
      { detail: 'Internal persistence details.' }, { status: 500, statusText: 'Server Error' },
    );
    harness.detectChanges();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('The server could not save the game');
    expect(view().textContent).not.toContain('Internal persistence details');
  });

  it('ignores a failed save from a previously opened game when the edit route is reused', async () => {
    await signIn();
    await open('/admin/games/' + id + '/edit');
    http.expectOne(gamesUrl + '/' + id).flush(record);
    http.expectOne(adminCategoryUrl).flush([category]);
    harness.detectChanges();
    enter('#game-title', 'Previous game edit');
    submit();
    const previousSave = http.expectOne({ method: 'PUT', url: gamesUrl + '/' + id });

    const nextId = 'a0d31fd9-3921-47ac-99c6-2da31828528f';
    await open('/admin/games/' + nextId + '/edit');
    http.expectOne(gamesUrl + '/' + nextId).flush({ ...record, id: nextId, title: 'Current game' });
    http.expectOne(adminCategoryUrl).flush([category]);
    previousSave.flush({ detail: 'Old edit conflict.' }, { status: 409, statusText: 'Conflict' });
    harness.detectChanges();

    expect(view().querySelector<HTMLInputElement>('#game-title')?.value).toBe('Current game');
    expect(view().querySelector('[role=alert]')).toBeNull();
  });

  it('loads categories, handles a stale edit and shows a conflict on confirmed deletion', async () => {
    await signIn();
    await open('/admin/categories');
    const list = http.expectOne(adminCategoryUrl);
    expect(view().textContent).toContain('Loading categories');
    list.flush([category]);
    harness.detectChanges();
    expect(view().textContent).toContain(category.name);

    click('Edit Puzzle');
    expect(document.activeElement?.id).toBe('category-name');
    enter('#category-name', 'New name');
    submit();
    const update = http.expectOne({ method: 'PUT', url: categoryUrl + '/' + category.id });
    expect(update.request.body.name).toBe('New name');
    update.flush({}, { status: 404, statusText: 'Not found' });
    harness.detectChanges();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('could not be found');

    click('Delete Puzzle');
    http.expectNone(req => req.method === 'DELETE');
    click('Confirm delete');
    const deletion = http.expectOne({ method: 'DELETE', url: categoryUrl + '/' + category.id });
    deletion.flush({ detail: 'This category is assigned to a game.' }, { status: 409, statusText: 'Conflict' });
    harness.detectChanges();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('assigned to a game');
  });

  it('shows server validation errors for a category and reloads after a successful create', async () => {
    await signIn();
    await open('/admin/categories');
    http.expectOne(adminCategoryUrl).flush([]);
    harness.detectChanges();
    enter('#category-name', category.name);
    enter('#category-slug', category.slug);
    submit();
    http.expectOne({ method: 'POST', url: categoryUrl }).flush(
      { errors: { Name: ['Name was rejected.'] } }, { status: 400, statusText: 'Bad Request' },
    );
    harness.detectChanges();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('Name was rejected.');
    submit();
    http.expectOne({ method: 'POST', url: categoryUrl }).flush(category);
    http.expectOne(adminCategoryUrl).flush([category]);
    harness.detectChanges();
    expect(view().textContent).toContain('Category created.');
  });
});
