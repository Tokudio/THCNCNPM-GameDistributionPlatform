export enum GameStatus { Draft, Published, Archived }
export enum MediaType { Image, Video }
export enum GameSort { Newest, Title, PriceAsc, PriceDesc }

export const gameStatusLabels: Record<GameStatus, string> = {
  [GameStatus.Draft]: 'Draft',
  [GameStatus.Published]: 'Published',
  [GameStatus.Archived]: 'Archived',
};

export interface CategoryInput {
  name: string;
  slug: string;
  description: string | null;
  isActive: boolean;
}

export interface Category extends CategoryInput {
  id: string;
  isDefault: boolean;
}

export interface GameMediaInput {
  mediaUrl: string;
  type: MediaType;
  displayOrder: number;
}

export interface GameSummary {
  id: string;
  title: string;
  slug: string;
  price: number;
  currency: string;
  discountPercent: number;
  status: GameStatus;
  coverImageUrl: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface GameInput {
  title: string;
  slug: string;
  description: string;
  price: number;
  currency: string;
  discountPercent: number;
  coverImageUrl: string | null;
  bannerImageUrl: string | null;
  trailerUrl: string | null;
  publisher: string | null;
  developer: string | null;
  releaseDate: string | null;
  minimumRequirements: string | null;
  recommendedRequirements: string | null;
  categoryIds: string[];
  media: GameMediaInput[];
}

export interface GameDetails extends GameSummary, Omit<GameInput, keyof GameSummary | 'categoryIds' | 'media'> {
  categories: Category[];
  media: (GameMediaInput & { id: string })[];
}

export interface GameFilters {
  search: string;
  categoryId: string;
  status: GameStatus | null;
  minPrice: number | null;
  maxPrice: number | null;
  sort: GameSort;
}

export interface Page<T> {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  items: T[];
}
