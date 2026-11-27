import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { App } from './app';
import { AUTH_API } from './core/auth/auth-api';
import { AuthService } from './core/auth/auth.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render the account navigation for a guest', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('header .brand')?.textContent).toContain('GameVault');
    expect(compiled.querySelector('nav a')?.getAttribute('href')).toBe('/register');
    expect(compiled.querySelector('main router-outlet')).not.toBeNull();
  });

  it('hides a signed-in view before browser history can cache that document', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const auth = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    const host = fixture.nativeElement as HTMLElement;

    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
    expect(host.hidden).toBe(false);

    const signIn = firstValueFrom(auth.login({ email: 'owner@example.test', password: 'Fixture!123' }));
    http.expectOne(AUTH_API.login).flush({
      accessToken: 'owner.fixture.signature', tokenType: 'Bearer', expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });
    http.expectOne(AUTH_API.me).flush({ id: 'owner', displayName: 'Fixture owner', email: 'owner@example.test', roles: ['User'] });
    await signIn;

    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }));
    expect(host.hidden).toBe(false);
    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
    expect(host.hidden).toBe(true);
    auth.logout();
    http.verify();
  });
});
