import { HttpContextToken } from '@angular/common/http';

export const API_BASE = '/api/v1';
export const AUTH_API = {
  register: `${API_BASE}/auth/register`,
  login: `${API_BASE}/auth/login`,
  me: `${API_BASE}/auth/me`,
};

export const REQUIRES_AUTH = new HttpContextToken(() => false);
