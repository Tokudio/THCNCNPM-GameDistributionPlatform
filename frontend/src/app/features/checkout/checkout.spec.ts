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
import { MockPaymentOutcome, Order, OrderStatus, PaymentStatus } from '../../core/models/order.models';

const base = '/api/v1/orders';
const id = 'c3c8526c-aa76-43e7-8f73-f57aab31e579';
const gameId = '144d1387-dc08-4c39-99c2-a2665a43b2b8';
const gameRoute = '/store/games/' + gameId;
const orderRoute = '/checkout/' + id;
const orderUrl = base + '/' + id;
const gameUrl = '/api/v1/games/' + gameId;
const pending: Order = {
  id, orderCode: 'ORD-test', status: OrderStatus.Pending, currency: 'VND',
  createdAt: '2026-11-24T00:00:00Z', paidAt: null, originalAmount: '459000.00', discountAmount: '91800.00',
  totalAmount: '367200.00', items: [{ gameId, gameTitle: 'Signal Tower', categories: ['Action'], coverImageUrl: null, unitPrice: '459000.00' }],
  paymentStatus: null, paymentMessage: null, ownershipGranted: false, mockPaymentAvailable: true,
};
const paid: Order = { ...pending, status: OrderStatus.Paid, paymentStatus: PaymentStatus.Succeeded, ownershipGranted: true };
const game = {
  id: gameId, title: pending.items[0].gameTitle, status: 1, currency: 'VND', price: 500000,
  discountPercent: 20, categories: [], media: [], description: 'Explore the frontier.',
  coverImageUrl: null, bannerImageUrl: null, trailerUrl: null, mockPaymentAvailable: true,
};

describe('Phase 7 checkout', () => {
  let http: HttpTestingController;
  let router: Router;
  let harness: RouterTestingHarness;
  const view = () => harness.routeNativeElement!;
  const render = () => harness.detectChanges();
  const click = (selector: string) => view().querySelector<HTMLElement>(selector)!.click();

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

  async function signIn() {
    const login = firstValueFrom(TestBed.inject(AuthService).login({ email: 'checkout@example.test', password: 'Fixture!123' }));
    http.expectOne(AUTH_API.login).flush({
      accessToken: 'checkout.fixture.signature', tokenType: 'Bearer', expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });
    http.expectOne(AUTH_API.me).flush({ id: 'fixture-user', email: 'checkout@example.test', displayName: 'Tester', roles: ['User'] });
    await login;
  }

  async function open(path: string, user = true) {
    if (user) await signIn();
    harness = await RouterTestingHarness.create(path);
  }

  it('takes a guest from Buy Game to login with a safe return URL and never creates an order', async () => {
    await open(gameRoute, false);
    http.expectOne(gameUrl).flush(game);
    render();
    click('.buy');
    await vi.waitFor(() => expect(router.url).toContain('/login?'));
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe(gameRoute);
    expect(postLoginUrl(router, orderRoute + '/result').toString()).toBe(orderRoute + '/result');
    expect(postLoginUrl(router, '//external.test/checkout').toString()).toBe('/profile');
    http.expectNone(base);
  });

  it('guards checkout links directly and preserves the existing order in the return URL', async () => {
    await open(orderRoute + '/payment', false);
    expect(router.url).toContain('/login?');
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe(orderRoute + '/payment');
    http.expectNone(orderUrl);
  });

  it('posts only gameId with the auth interceptor and reviews the server price snapshot', async () => {
    await open(gameRoute);
    const load = http.expectOne(gameUrl);
    expect(load.request.headers.has('Authorization')).toBe(false);
    load.flush(game);
    render();
    click('.buy');
    click('.buy');
    const create = http.expectOne({ method: 'POST', url: base });
    expect(create.request.body).toEqual({ gameId });
    expect(create.request.headers.get('Authorization')).toBe('Bearer checkout.fixture.signature');
    create.flush(pending, { status: 201, statusText: 'Created' });
    const order = await vi.waitFor(() => http.expectOne({ method: 'GET', url: orderUrl }));
    order.flush(pending);
    render();
    expect(view().textContent).toContain('Review your order');
    expect(view().textContent).toContain('459.000 ₫');
    expect(view().textContent).toContain('91.800 ₫');
    expect(view().textContent).toContain('367.200 ₫');
    expect(view().textContent).not.toContain('400.000 ₫');
    expect(view().querySelector('.primary-action')?.getAttribute('href')).toBe(orderRoute + '/payment');
  });

  it('marks already-owned games and prevents resubmission', async () => {
    await open(gameRoute);
    http.expectOne(gameUrl).flush(game);
    render();
    click('.buy');
    http.expectOne(base).flush({ code: 'game_already_owned' }, { status: 409, statusText: 'Conflict' });
    render();
    expect(view().querySelector<HTMLButtonElement>('.buy')?.disabled).toBe(true);
    expect(view().querySelector('.buy')?.textContent).toContain('Owned');
    expect(view().querySelector('[role=alert]')?.textContent).toContain('You already own');
  });

  it('makes checkout unavailable when the backend disables Mock Payment', async () => {
    await open(gameRoute);
    http.expectOne(gameUrl).flush({ ...game, mockPaymentAvailable: false });
    render();
    expect(view().querySelector<HTMLButtonElement>('.buy')?.disabled).toBe(true);
    await harness.navigateByUrl(orderRoute + '/payment');
    http.expectOne(orderUrl).flush({ ...pending, mockPaymentAvailable: false });
    render();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('unavailable');
    expect(view().querySelector('.primary-action')).toBeNull();
    http.expectNone(orderUrl + '/mock-payment');
  });

  it('shows loading and handles protected review errors without creating an order on refresh', async () => {
    await open(orderRoute);
    expect(view().querySelector('[role=status]')?.textContent).toContain('Loading');
    http.expectOne(orderUrl).flush('', { status: 403, statusText: 'Forbidden' });
    render();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('permission');
    click('button');
    http.expectOne({ method: 'GET', url: orderUrl }).flush('', { status: 404, statusText: 'Not Found' });
    render();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('unavailable');
    http.expectNone(base);
  });

  it('sends one payment attempt on double click and verifies success with a GET', async () => {
    await open(orderRoute + '/payment');
    http.expectOne(orderUrl).flush(pending);
    render();
    click('.primary-action');
    click('.primary-action');
    const payment = http.expectOne({ method: 'POST', url: orderUrl + '/mock-payment' });
    expect(payment.request.body).toEqual({ outcome: MockPaymentOutcome.Success });
    expect(payment.request.headers.get('Authorization')).toBe('Bearer checkout.fixture.signature');
    render();
    expect(view().querySelector<HTMLButtonElement>('.primary-action')?.disabled).toBe(true);
    payment.flush({ orderStatus: OrderStatus.Paid, paymentStatus: PaymentStatus.Succeeded, orderId: id });

    const verify = await vi.waitFor(() => http.expectOne({ method: 'GET', url: orderUrl }));
    verify.flush(pending);
    render();
    expect(view().querySelector('h1')?.textContent).toContain('Payment pending');
    expect(view().textContent).not.toContain('Payment successful');
    click('button');
    http.expectOne(orderUrl).flush(paid);
    render();
    expect(view().querySelector('h1')?.textContent).toContain('Payment successful');
    expect(view().textContent).toContain('part of your game library');
  });

  it('posts the mock failure outcome and gets a failed order from the server', async () => {
    await open(orderRoute + '/payment');
    http.expectOne(orderUrl).flush(pending);
    render();
    click('.payment-actions .button-secondary');
    const payment = http.expectOne(orderUrl + '/mock-payment');
    expect(payment.request.body).toEqual({ outcome: MockPaymentOutcome.Failure });
    payment.flush({ orderId: id, orderStatus: OrderStatus.Cancelled, paymentStatus: PaymentStatus.Failed });
    const verify = await vi.waitFor(() => http.expectOne(orderUrl));
    verify.flush({ ...pending, status: OrderStatus.Cancelled, paymentStatus: PaymentStatus.Failed, paymentMessage: 'Fixture decline' });
    render();
    expect(view().querySelector('h1')?.textContent).toContain('Payment failed');
    expect(view().textContent).toContain('Fixture decline');
    expect(view().textContent).not.toContain('part of your game library');
  });

  it('offers a server status check after an interrupted payment without automatically sending another attempt', async () => {
    await open(orderRoute + '/payment');
    http.expectOne(orderUrl).flush(pending);
    render();
    click('.primary-action');
    const payment = http.expectOne({ method: 'POST', url: orderUrl + '/mock-payment' });
    payment.error(new ProgressEvent('error'));
    render();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('Check your connection');
    click('[role=alert] a');
    const status = await vi.waitFor(() => http.expectOne({ method: 'GET', url: orderUrl }));
    status.flush(paid);
    render();
    expect(view().querySelector('h1')?.textContent).toContain('Payment successful');
    http.expectNone({ method: 'POST', url: orderUrl + '/mock-payment' });
  });

  it('times out a stalled confirmation and requires a status check before sending another payment', async () => {
    await open(orderRoute + '/payment');
    http.expectOne(orderUrl).flush(pending);
    render();
    click('.primary-action');
    const payment = http.expectOne({ method: 'POST', url: orderUrl + '/mock-payment' });
    expect(payment.request.timeout).toBe(15_000);
    payment.error(new ProgressEvent('timeout'));
    render();
    expect(view().querySelector('[role=alert]')?.textContent).toContain('Check order status');
    expect(view().querySelector<HTMLButtonElement>('.primary-action')?.disabled).toBe(true);
    expect(view().querySelector<HTMLButtonElement>('.payment-actions button.button-secondary')?.disabled).toBe(true);
    http.expectNone({ method: 'POST', url: orderUrl + '/mock-payment' });
  });

  it('renders the cents from a large authoritative order total without converting it to Number', async () => {
    await open(orderRoute);
    http.expectOne(orderUrl).flush({ ...pending, originalAmount: '9999999999999999.99', discountAmount: '0.00', totalAmount: '9999999999999999.99' });
    render();
    expect(view().querySelector('.total dd')?.textContent).toContain('9.999.999.999.999.999,99 ₫');
  });

  it('gets result state again on direct navigation and ignores success in the URL', async () => {
    await open(orderRoute + '/result?status=success');
    http.expectOne(orderUrl).flush({ ...paid, ownershipGranted: false });
    render();
    expect(view().querySelector('h1')?.textContent).toContain('Payment result');
    expect(view().textContent).toContain('has not confirmed ownership');
    http.expectNone(req => req.method === 'POST' && req.url.startsWith(base));
  });

  it('lands on the result of a free claim without a payment POST', async () => {
    await open(gameRoute);
    http.expectOne(gameUrl).flush({ ...game, price: 0 });
    render();
    expect(view().querySelector('.buy')?.textContent).toContain('Claim game');
    click('.buy');
    const free = { ...paid, totalAmount: '0.00', paymentStatus: null };
    http.expectOne(base).flush(free);
    const verify = await vi.waitFor(() => http.expectOne(orderUrl));
    verify.flush(free);
    render();
    expect(view().querySelector('h1')?.textContent).toContain('Game claimed');
    http.expectNone(orderUrl + '/mock-payment');
  });
});
