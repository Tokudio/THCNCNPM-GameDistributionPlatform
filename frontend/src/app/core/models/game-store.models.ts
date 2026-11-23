export enum GameStatus { Draft, Published, Archived }
export enum MediaType { Image, Video }
export enum GameSort { Newest, Title, PriceAsc, PriceDesc }

export interface Category {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  isActive: boolean;
  isDefault: boolean;
}

export interface GameMedia {
  id: string;
  mediaUrl: string;
  type: MediaType;
  displayOrder: number;
}

export interface GameSummary<Price = number> {
  id: string;
  title: string;
  slug: string;
  price: Price;
  currency: string;
  discountPercent: number;
  status: GameStatus;
  coverImageUrl: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface GameDetails<Price = number> extends GameSummary<Price> {
  description: string;
  bannerImageUrl: string | null;
  trailerUrl: string | null;
  publisher: string | null;
  developer: string | null;
  releaseDate: string | null;
  minimumRequirements: string | null;
  recommendedRequirements: string | null;
  categories: Category[];
  media: GameMedia[];
}

export interface Page<T> {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  items: T[];
}

export interface StoreQuery {
  page?: number;
  pageSize?: number;
  search?: string;
  category?: string;
  minPrice?: string;
  maxPrice?: string;
  sort?: GameSort;
}

export type StoreGame = GameSummary<string>;
export type StoreGameDetails = GameDetails<string>;
export type StoreCardGame = StoreGame & { categories: Category[] };
