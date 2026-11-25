import { Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'store' },
  {
    path: 'store',
    title: 'Game Distribution',
    data: { store: true },
    loadComponent: () => import('./features/store/store-home/store-home').then(m => m.StoreHome),
  },
  {
    path: 'store/games',
    title: 'Explore all games | Game Distribution',
    data: { store: true },
    loadComponent: () => import('./features/store/game-catalog/game-catalog').then(m => m.GameCatalog),
  },
  {
    path: 'store/games/:id',
    title: 'Game details | Game Distribution',
    data: { store: true },
    loadComponent: () => import('./features/store/game-detail/game-detail').then(m => m.GameDetail),
  },
  {
    path: 'checkout/:id/payment',
    title: 'Confirm payment | Game Distribution',
    canActivate: [authGuard],
    data: { store: true, protected: true, confirm: true },
    loadComponent: () => import('./features/checkout/order-checkout/order-checkout').then(m => m.OrderCheckout),
  },
  {
    path: 'checkout/:id/result',
    title: 'Payment result | Game Distribution',
    canActivate: [authGuard],
    data: { store: true, protected: true },
    loadComponent: () => import('./features/checkout/payment-result/payment-result').then(m => m.PaymentResult),
  },
  {
    path: 'checkout/:id',
    title: 'Review order | Game Distribution',
    canActivate: [authGuard],
    data: { store: true, protected: true },
    loadComponent: () => import('./features/checkout/order-checkout/order-checkout').then(m => m.OrderCheckout),
  },
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
    path: 'library',
    title: 'My Library | Game Distribution',
    canActivate: [authGuard],
    data: { store: true, protected: true },
    loadComponent: () => import('./features/library/my-library/my-library').then(m => m.MyLibrary),
  },
  {
    path: 'library/:gameId',
    title: 'Owned game | Game Distribution',
    canActivate: [authGuard],
    data: { store: true, protected: true },
    loadComponent: () => import('./features/library/library-game-detail/library-game-detail').then(m => m.LibraryGameDetail),
  },
  {
    path: 'profile',
    title: 'Your profile | GameVault',
    canActivate: [authGuard],
    data: { store: true, protected: true },
    loadComponent: () => import('./features/profile/profile-page').then(m => m.ProfilePage),
  },
  {
    path: 'admin',
    data: { protected: true, admin: true },
    // Guard đọc role mới từ API khi chuyển màn hình quản trị.
    canActivateChild: [roleGuard('Admin')],
    loadComponent: () => import('./features/admin/admin-layout/admin-layout').then(m => m.AdminLayout),
    children: [
      {
        path: '', pathMatch: 'full', title: 'Tổng quan hệ thống | GameVault',
        loadComponent: () => import('./features/admin/dashboard/dashboard').then(m => m.Dashboard),
      },
      {
        path: 'games', title: 'Quản lý game | GameVault',
        loadComponent: () => import('./features/admin/games/game-list').then(m => m.GameList),
      },
      {
        path: 'games/new', title: 'Thêm game | GameVault',
        loadComponent: () => import('./features/admin/games/game-form').then(m => m.GameForm),
      },
      {
        path: 'games/:id/edit', title: 'Chỉnh sửa game | GameVault',
        loadComponent: () => import('./features/admin/games/game-form').then(m => m.GameForm),
      },
      {
        path: 'categories', title: 'Danh mục | GameVault',
        loadComponent: () => import('./features/admin/categories/category-page').then(m => m.CategoryPage),
      },
      {
        path: 'orders', title: 'Đơn hàng | GameVault',
        loadComponent: () => import('./features/admin/orders/order-list/order-list').then(m => m.OrderList),
      },
      {
        path: 'orders/:id', title: 'Chi tiết đơn hàng | GameVault',
        loadComponent: () => import('./features/admin/orders/order-detail/order-detail').then(m => m.OrderDetail),
      },
      {
        path: 'users', title: 'Người dùng | GameVault',
        loadComponent: () => import('./features/admin/users/user-list/user-list').then(m => m.UserList),
      },
      {
        path: 'users/:id', title: 'Chi tiết người dùng | GameVault',
        loadComponent: () => import('./features/admin/users/user-detail/user-detail').then(m => m.UserDetail),
      },
    ],
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
