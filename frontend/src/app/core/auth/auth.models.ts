export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest extends LoginRequest {
  confirmPassword: string;
  displayName: string;
}

export interface AuthResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
}

export interface UserInfoResponse {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
}
