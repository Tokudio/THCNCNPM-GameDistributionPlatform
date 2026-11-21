import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { API_BASE } from '../../core/auth/auth-api';
import { adminContext } from './catalog-http';
import { Category, CategoryInput } from './catalog.models';

const categoriesUrl = `${API_BASE}/categories`;

@Injectable({ providedIn: 'root' })
export class CategoryService {
  private readonly http = inject(HttpClient);

  list() {
    return this.http.get<Category[]>(`${API_BASE}/admin/categories`, { context: adminContext() });
  }

  create(input: CategoryInput) {
    return this.http.post<Category>(categoriesUrl, input, { context: adminContext() });
  }

  update(id: string, input: CategoryInput) {
    return this.http.put<Category>(`${categoriesUrl}/${id}`, input, { context: adminContext() });
  }

  delete(id: string) {
    return this.http.delete<void>(`${categoriesUrl}/${id}`, { context: adminContext() });
  }
}
