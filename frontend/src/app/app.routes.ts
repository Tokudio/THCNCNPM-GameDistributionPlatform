import { Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  {
    path: '',
    loadComponent: () => import('./features/auth/auth-layout').then(m => m.AuthLayout),
    children: [
      {
        path: 'login',
        title: 'Sign in | GameVault',
        canActivate: [guestGuard],
        loadComponent: () => import('./features/auth/login-page').then(m => m.LoginPage),
      },
      {
        path: 'register',
        title: 'Create your account | GameVault',
        canActivate: [guestGuard],
        loadComponent: () => import('./features/auth/register-page').then(m => m.RegisterPage),
      },
    ],
  },
  {
    path: 'profile',
    title: 'Your profile | GameVault',
    canActivate: [authGuard],
    data: { protected: true },
    loadComponent: () => import('./features/profile/profile-page').then(m => m.ProfilePage),
  },
  {
    path: 'admin/games',
    title: 'Manage games | GameVault',
    canActivate: [authGuard, roleGuard('Admin')],
    data: { protected: true },
    loadComponent: () => import('./features/admin/games/game-list').then(m => m.GameList),
  },
  {
    path: 'admin/games/new',
    title: 'Add game | GameVault',
    canActivate: [authGuard, roleGuard('Admin')],
    data: { protected: true },
    loadComponent: () => import('./features/admin/games/game-form').then(m => m.GameForm),
  },
  {
    path: 'admin/games/:id/edit',
    title: 'Edit game | GameVault',
    canActivate: [authGuard, roleGuard('Admin')],
    data: { protected: true },
    loadComponent: () => import('./features/admin/games/game-form').then(m => m.GameForm),
  },
  {
    path: 'admin/categories',
    title: 'Manage categories | GameVault',
    canActivate: [authGuard, roleGuard('Admin')],
    data: { protected: true },
    loadComponent: () => import('./features/admin/categories/category-page').then(m => m.CategoryPage),
  },
  {
    path: 'admin',
    title: 'Administrator access | GameVault',
    canActivate: [roleGuard('Admin')],
    data: { protected: true, granted: true },
    loadComponent: () => import('./features/profile/access-page').then(m => m.AccessPage),
  },
  {
    path: 'access-denied',
    title: 'Account access | GameVault',
    canActivate: [authGuard],
    data: { protected: true },
    loadComponent: () => import('./features/profile/access-page').then(m => m.AccessPage),
  },
  { path: '**', redirectTo: 'login' },
];
