import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext } from '@angular/common/http';
import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';
import {
  catchError, defer, finalize, firstValueFrom, map, Observable, switchMap, tap, throwError, timeout,
} from 'rxjs';
import { AUTH_API, REQUIRES_AUTH } from './auth-api';
import { AuthResponse, LoginRequest, RegisterRequest, UserInfoResponse } from './auth.models';

interface Session {
  credentials: AuthResponse;
  expiresAt: number;
}

const SESSION_KEY = 'gamevault.auth';
const EXPIRED = 'Your session expired. Sign in again to continue.';
const REJECTED = 'Your session is no longer valid. Sign in again to continue.';
const VERIFICATION_FAILED = 'We couldn’t verify your session. Please sign in again.';
const REQUEST_TIMEOUT = 10_000;
const MAX_TIMER_DELAY = 2_147_483_647;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly session = signal<Session | null>(null);
  private readonly user = signal<UserInfoResponse | null>(null);
  private readonly notice = signal<string | null>(null);
  private expirationTimer: ReturnType<typeof setTimeout> | undefined;
  private revision = 0;

  readonly currentUser = this.user.asReadonly();
  readonly sessionNotice = this.notice.asReadonly();
  readonly isAuthenticated = computed(() => this.session() !== null && this.user() !== null);

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.expirationTimer));
  }

  async initialize(): Promise<void> {
    const session = this.readStoredSession();
    if (!session) {
      this.writeStorage(null);
      return;
    }

    if (session.expiresAt <= Date.now()) {
      this.clearSession(EXPIRED);
      return;
    }

    this.startSession(session);
    try {
      await firstValueFrom(this.getCurrentUser());
    } catch {
      if (this.session() === session) {
        this.clearSession(VERIFICATION_FAILED);
      } else if (!this.notice()) {
        this.notice.set(REJECTED);
      }
    }
  }

  register(request: RegisterRequest): Observable<UserInfoResponse> {
    return this.http.post<UserInfoResponse>(AUTH_API.register, request).pipe(timeout(REQUEST_TIMEOUT));
  }

  login(request: LoginRequest): Observable<UserInfoResponse> {
    return defer(() => {
      const revision = ++this.revision;
      return this.http.post<AuthResponse>(AUTH_API.login, request).pipe(
        timeout(REQUEST_TIMEOUT),
        switchMap(response => {
          const session = this.parseSession(response);
          if (this.revision !== revision || !session || session.expiresAt <= Date.now()) {
            return throwError(() => new Error(VERIFICATION_FAILED));
          }

          this.writeStorage(null);
          this.startSession(session);
          return this.getCurrentUser().pipe(
            tap(() => this.writeStorage(session.credentials)),
            catchError(() => throwError(() => new Error(VERIFICATION_FAILED))),
            finalize(() => {
              if (this.session() === session && !this.user()) {
                this.clearSession(this.notice());
              }
            }),
          );
        }),
      );
    });
  }

  getCurrentUser(): Observable<UserInfoResponse> {
    return defer(() => {
      const session = this.activeSession();
      if (!session) {
        return throwError(() => new Error(REJECTED));
      }

      const context = new HttpContext().set(REQUIRES_AUTH, true);
      return this.http.get<UserInfoResponse>(AUTH_API.me, { context }).pipe(
        timeout(REQUEST_TIMEOUT),
        map(user => {
          if (
            this.activeSession() !== session || !user || typeof user.id !== 'string' ||
            typeof user.email !== 'string' || typeof user.displayName !== 'string' ||
            !Array.isArray(user.roles) || !user.roles.every(role => typeof role === 'string')
          ) {
            throw new Error(VERIFICATION_FAILED);
          }
          this.user.set(user);
          return user;
        }),
      );
    });
  }

  getAccessToken(): string | null {
    return this.activeSession()?.credentials.accessToken ?? null;
  }

  hasVerifiedSession(): boolean {
    return this.activeSession() !== null && this.user() !== null;
  }

  rejectToken(accessToken: string): void {
    if (this.session()?.credentials.accessToken === accessToken) {
      this.clearSession(this.user() ? REJECTED : null);
    }
  }

  logout(): void {
    this.clearSession();
  }

  private activeSession(): Session | null {
    const session = this.session();
    if (session && session.expiresAt <= Date.now()) {
      this.clearSession(EXPIRED);
      return null;
    }
    return session;
  }

  private startSession(session: Session): void {
    this.user.set(null);
    this.notice.set(null);
    this.session.set(session);
    clearTimeout(this.expirationTimer);
    this.scheduleExpiration(session);
  }

  private scheduleExpiration(session: Session): void {
    const wait = Math.max(0, Math.min(session.expiresAt - Date.now(), MAX_TIMER_DELAY));
    this.expirationTimer = setTimeout(() => {
      if (this.activeSession() === session) {
        this.scheduleExpiration(session);
      }
    }, wait);
  }

  private clearSession(notice: string | null = null): void {
    this.revision++;
    clearTimeout(this.expirationTimer);
    this.session.set(null);
    this.user.set(null);
    this.writeStorage(null);
    this.notice.set(notice);
  }

  private parseSession(value: unknown): Session | null {
    if (typeof value !== 'object' || value === null) {
      return null;
    }
    const { accessToken, tokenType, expiresAt } = value as Partial<AuthResponse>;
    if (
      typeof accessToken !== 'string' || !/^[\w-]+\.[\w-]+\.[\w-]+$/.test(accessToken) ||
      typeof tokenType !== 'string' || tokenType.toLowerCase() !== 'bearer' ||
      typeof expiresAt !== 'string'
    ) {
      return null;
    }

    const expiry = Date.parse(expiresAt);
    return Number.isFinite(expiry)
      ? { credentials: { accessToken, tokenType, expiresAt }, expiresAt: expiry }
      : null;
  }

  private readStoredSession(): Session | null {
    try {
      const value = this.document.defaultView?.sessionStorage.getItem(SESSION_KEY);
      return value ? this.parseSession(JSON.parse(value)) : null;
    } catch {
      return null;
    }
  }

  private writeStorage(credentials: AuthResponse | null): void {
    try {
      const storage = this.document.defaultView?.sessionStorage;
      if (credentials) {
        storage?.setItem(SESSION_KEY, JSON.stringify(credentials));
      } else {
        storage?.removeItem(SESSION_KEY);
      }
    } catch {
      // The in-memory session still works when browser storage is unavailable.
    }
  }
}
