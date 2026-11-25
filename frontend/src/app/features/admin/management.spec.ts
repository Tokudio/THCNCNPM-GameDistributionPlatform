import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { firstValueFrom } from 'rxjs';
import { routes } from '../../app.routes';
import { AUTH_API } from '../../core/auth/auth-api';
import { postLoginUrl } from '../../core/auth/auth.guards';
import { authInterceptor } from '../../core/auth/auth.interceptor';
import { AuthService } from '../../core/auth/auth.service';
import { AdminOrderSummary, AdminUserSummary, DashboardSummary } from '../../core/models/admin.models';
import { Page } from '../../core/models/game-store.models';
import { OrderStatus, PaymentStatus } from '../../core/models/order.models';

const dashboardUrl = '/api/v1/admin/dashboard/summary';
const ordersUrl = '/api/v1/admin/orders';
const usersUrl = '/api/v1/admin/users';
const id = 'c3c8526c-aa76-43e7-8f73-f57aab31e579';
const actorId = '144d1387-dc08-4c39-99c2-a2665a43b2b8';
const token = 'admin.fixture.signature';
const instant = '2026-11-23T23:00:00Z';
const empty: DashboardSummary = {
  games: { total: 0, published: 0, draft: 0, archived: 0 }, totalCategories: 0,
  users: { total: 0, active: 0, inactive: 0 }, orders: { total: 0, pending: 0, paid: 0, cancelled: 0 },
  revenue: [], revenueStartDate: '2026-11-19', dailyRevenue: [], recentOrders: [],
  unverifiedPaidOrders: 0, revenueOrdersMissingPaidAt: 0,
};
const order: AdminOrderSummary = {
  id, orderCode: 'ORD-MAU', customerName: 'Ngọc Lan', customerEmail: 'lan@example.test', currency: 'VND',
  createdAt: instant, totalAmount: '199.00', status: OrderStatus.Paid, paymentStatus: PaymentStatus.Succeeded,
};
const user: AdminUserSummary = { id, displayName: 'Minh', email: 'minh@example.test', roles: ['User'], isActive: true, createdAt: instant };

function page<T>(items: T[], pageNumber = 1, totalCount = items.length): Page<T> {
  return { pageNumber, pageSize: 12, totalCount, totalPages: Math.ceil(totalCount / 12), items };
}

describe('Phase 9 admin management', () => {
  let http: HttpTestingController;
  let router: Router;
  let harness: RouterTestingHarness;
  const view = () => harness.routeNativeElement!;
  const render = () => harness.detectChanges();
  const button = (selector: string) => view().querySelector<HTMLButtonElement>(selector)!;
  const get = (url: string) => http.expectOne(req => req.method === 'GET' && req.url === url);

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

  async function signIn(roles: string[] = ['Admin']) {
    const login = firstValueFrom(TestBed.inject(AuthService).login({ email: 'tester@example.test', password: 'Fixture!123' }));
    http.expectOne(AUTH_API.login).flush({ accessToken: token, tokenType: 'Bearer', expiresAt: new Date(Date.now() + 60_000).toISOString() });
    get(AUTH_API.me).flush({ id: actorId, displayName: 'Fixture Admin', email: 'tester@example.test', roles });
    await login;
  }

  async function refreshRoles(roles: string[] = ['Admin']) {
    const request = await vi.waitFor(() => get(AUTH_API.me));
    request.flush({ id: actorId, displayName: 'Fixture Admin', email: 'tester@example.test', roles });
  }

  async function open(path: string, roles: string[] = ['Admin']) {
    const navigation = harness.navigateByUrl(path);
    await refreshRoles(roles);
    await navigation;
    render();
  }

  function change(selector: string, value: string) {
    const input = view().querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event(input.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
  }

  function submitFilters() {
    view().querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  }

  function mockDialog() {
    const dialog = view().querySelector<HTMLDialogElement>('dialog')!;
    Object.defineProperties(dialog, {
      showModal: { value: vi.fn(() => { dialog.open = true; }) },
      close: { value: vi.fn(() => { dialog.open = false; }) },
    });
    return dialog;
  }

  it('redirects guests and preserves an allowed Admin detail return URL', async () => {
    const path = '/admin/orders/' + id;
    await harness.navigateByUrl(path);
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe(path);
    expect(postLoginUrl(router, path).toString()).toBe(path);
    expect(postLoginUrl(router, '//external.test/admin/orders').toString()).toBe('/profile');
    http.expectNone(req => req.url.startsWith('/api/v1/admin'));
  });

  it.each(['/admin/orders', '/admin/users'])('rejects a regular user with fresh roles on %s', async path => {
    await signIn();
    await open(path, ['User']);
    expect(router.url).toBe('/access-denied');
    http.expectNone(req => req.url.startsWith('/api/v1/admin'));
  });

  it('renders loading and exact large decimal revenue after an authenticated summary request', async () => {
    await signIn();
    await open('/admin');
    expect(view().querySelector('[role=status]')?.textContent).toContain('Đang tải tổng quan');
    const request = get(dashboardUrl);
    expect(request.request.headers.get('Authorization')).toBe('Bearer ' + token);
    const amount = '9007199254740993.65';
    request.flush({ ...empty, games: { total: 12, published: 7, draft: 3, archived: 2 },
      orders: { total: 1, paid: 1, pending: 0, cancelled: 0 }, revenue: [{ amount: '1', currency: 'USD' }, { amount, currency: 'VND' }],
      dailyRevenue: [{ amount, currency: 'VND', date: '2026-11-23' }], recentOrders: [order] });
    render();
    expect(view().querySelector('[data-metric=games]')?.textContent).toBe('12');
    expect(view().querySelector('.metrics')?.textContent).toContain('9.007.199.254.740.993,65');
    expect(view().querySelector<HTMLSelectElement>('.visual-heading select')?.value).toBe('VND');
    expect(view().querySelectorAll('.chart meter').length).toBe(7);
    expect(view().querySelector('time[datetime="2026-11-23"]')?.textContent).toBe('23/11');
    expect(view().querySelector(`a[href="/admin/orders/${id}"]`)?.textContent).toBe(order.orderCode);
  });

  it('offers retry on dashboard errors and renders real zero and empty states', async () => {
    await signIn();
    await open('/admin');
    get(dashboardUrl).flush({}, { status: 500, statusText: 'Unavailable' });
    render();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('Không tải được dữ liệu');
    button('.state button').click();
    get(dashboardUrl).flush(empty);
    render();
    expect(view().querySelector('[data-metric=revenue]')?.textContent).toContain('0 ₫');
    expect(view().textContent).toContain('Chưa có đơn hàng.');
  });

  it('distinguishes excluded orders from revenue that cannot be dated for the chart', async () => {
    await signIn();
    await open('/admin');
    get(dashboardUrl).flush({ ...empty, revenue: [{ currency: 'VND', amount: '50.00' }],
      unverifiedPaidOrders: 2, revenueOrdersMissingPaidAt: 1 });
    render();
    expect(view().querySelector('[data-metric=revenue]')?.textContent).toContain('50 ₫');
    expect(view().querySelector('.notice')?.textContent).toContain('2 đơn ở trạng thái đã thanh toán bị loại khỏi doanh thu');
    expect(view().querySelector('.notice')?.textContent).toContain('1 đơn đã xác minh được tính vào tổng doanh thu');
  });

  it('searches and filters orders, cancels obsolete requests, and preserves filters while paging', async () => {
    await signIn();
    await open('/admin/orders');
    const obsolete = get(ordersUrl);
    change('#order-search', '  lan  ');
    change('#order-status', '1');
    change('#payment-status', '1');
    change('#order-sort', 'true');
    submitFilters();
    const filtered = await vi.waitFor(() => get(ordersUrl));
    expect(obsolete.cancelled).toBe(true);
    expect(filtered.request.params.get('search')).toBe('lan');
    expect(filtered.request.params.get('status')).toBe('1');
    expect(filtered.request.params.get('paymentStatus')).toBe('1');
    expect(filtered.request.params.get('oldestFirst')).toBe('true');
    filtered.flush(page([order], 1, 13));
    render();
    expect(view().querySelector('tbody')?.textContent).toContain('Thành công');
    button('.controls button:last-child').click();
    const next = await vi.waitFor(() => get(ordersUrl));
    expect(next.request.params.get('search')).toBe('lan');
    expect(next.request.params.get('pageNumber')).toBe('2');
    next.flush(page([order], 2, 13));
    render();
    expect(view().querySelector('.pager')?.textContent).toContain('Trang 2/2');
  });

  it('opens the read-only order detail and renders the stored price snapshot', async () => {
    await signIn();
    await open('/admin/orders');
    get(ordersUrl).flush(page([order]));
    render();
    view().querySelector<HTMLAnchorElement>(`table a[href="/admin/orders/${id}"]`)!.click();
    await refreshRoles();
    const detail = await vi.waitFor(() => get(`${ordersUrl}/${id}`));
    detail.flush({ order, items: [{ gameId: id, gameTitle: 'Tên lưu lúc mua', unitPrice: '250', coverImageUrl: null }],
      payments: [{ attemptNumber: 1, status: 1, method: 0, amount: '199', currency: 'VND', createdAt: instant, completedAt: instant }],
      paidAt: instant, originalAmount: '250', discountAmount: '51' });
    render();
    expect(view().querySelector('h1')?.textContent).toBe('Đơn ORD-MAU');
    expect(view().textContent).toContain('Tên lưu lúc mua');
    expect(view().textContent).toContain('Giảm giá lúc đặt51 ₫');
    expect(view().textContent).toContain('Thanh toán mô phỏng');
    expect(view().querySelector('form')).toBeNull();
  });

  it('reports a missing order using a specific error', async () => {
    await signIn();
    await open('/admin/orders/' + id);
    get(`${ordersUrl}/${id}`).flush({}, { status: 404, statusText: 'Not Found' });
    render();
    expect(view().querySelector('[role=alert]')?.textContent).toBe('Không tìm thấy đơn hàng.');
  });

  it('searches users by name or email with status and role filters', async () => {
    await signIn();
    await open('/admin/users');
    get(usersUrl).flush(page([]));
    render();
    expect(view().textContent).toContain('Chưa có người dùng.');
    change('#user-search', 'minh@example.test');
    change('#user-status', 'true');
    change('#user-role', 'User');
    submitFilters();
    const result = await vi.waitFor(() => get(usersUrl));
    expect(result.request.params.get('search')).toBe('minh@example.test');
    expect(result.request.params.get('isActive')).toBe('true');
    expect(result.request.params.get('role')).toBe('User');
    result.flush(page([user]));
    render();
    expect(view().querySelector('tbody')?.textContent).toContain('Đang hoạt động');
    expect(view().querySelector(`table a[href="/admin/users/${id}"]`)).not.toBeNull();
  });

  it.each([id, id.toUpperCase()])('requires confirmation and reloads the current user detail at %s', async routeId => {
    await signIn();
    await open('/admin/users/' + routeId);
    const detail = { user, totalOrders: 2, ownedGames: 1 };
    get(`${usersUrl}/${routeId}`).flush(detail);
    render();
    const dialog = mockDialog();
    button('.change').click();
    expect(dialog.open).toBe(true);
    button('.dialog-actions .button-secondary').click();
    expect(dialog.open).toBe(false);
    http.expectNone(`${usersUrl}/${id}/status`);
    button('.change').click();
    button('.dialog-actions .button-primary').click();
    const update = http.expectOne({ url: `${usersUrl}/${id}/status`, method: 'PATCH' });
    expect(update.request.body).toEqual({ isActive: false });
    update.flush(null, { status: 204, statusText: 'No Content' });
    get(`${usersUrl}/${routeId}`).flush({ ...detail, user: { ...user, isActive: false } });
    render();
    expect(view().querySelector('.notice-success')?.textContent).toBe('Đã tạm khóa tài khoản.');
    expect(button('.change').textContent).toContain('Kích hoạt tài khoản');
  });

  it('shows a conflict from the API without changing the displayed account status', async () => {
    await signIn();
    await open('/admin/users/' + id);
    get(`${usersUrl}/${id}`).flush({ user, totalOrders: 0, ownedGames: 0 });
    render();
    mockDialog();
    button('.change').click();
    button('.dialog-actions .button-primary').click();
    http.expectOne({ url: `${usersUrl}/${id}/status`, method: 'PATCH' }).flush({ detail: 'Không thể tạm khóa Admin cuối cùng.' }, { status: 409, statusText: 'Conflict' });
    render();
    expect(view().querySelector('[role=alert]')?.textContent).toBe('Không thể tạm khóa Admin cuối cùng.');
    expect(view().querySelector('.identity .status')?.textContent).toBe('Đang hoạt động');
  });

  it('includes existing catalog routes, restores mobile menu focus, and logs out', async () => {
    await signIn();
    await open('/admin');
    get(dashboardUrl).flush(empty);
    render();
    expect(view().querySelector('nav a[href="/admin/games"]')).not.toBeNull();
    expect(view().querySelector('nav a[href="/admin/categories"]')).not.toBeNull();
    const menu = button('.menu');
    menu.click();
    render();
    expect(menu.getAttribute('aria-expanded')).toBe('true');
    view().querySelector('.sidebar a')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    render();
    expect(menu.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(menu);
    button('.account-links button').click();
    await vi.waitFor(() => expect(router.url).toBe('/login'));
    expect(TestBed.inject(AuthService).getAccessToken()).toBeNull();
  });

  it('removes Admin data as soon as the session ends even before router navigation', async () => {
    await signIn();
    await open('/admin/orders');
    get(ordersUrl).flush(page([order]));
    render();
    expect(view().textContent).toContain(order.customerEmail);

    TestBed.inject(AuthService).logout();
    render();
    expect(view().querySelector('table')).toBeNull();
    expect(view().textContent).not.toContain(order.customerEmail);
  });
});
